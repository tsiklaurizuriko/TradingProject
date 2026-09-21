using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Risk;

namespace TradingPlatform.Trading;

/// <summary>
/// Isolated occupancy is one filled position per coin. SL/TP algo orders are protection, not extra slots.
/// </summary>
public static class IsolatedOccupancy
{
    public static readonly TimeSpan OverlayGrace = TimeSpan.FromSeconds(60);

    public static string CoinKey(string symbol) => symbol.Trim().ToUpperInvariant();

    public static bool HasFreshFuturesBook(LiveAccountSnapshot live) =>
        live.HasKeys && live.FuturesBookFresh;

    public static bool IsOnExchange(string symbol, IReadOnlyList<LiveOpenPosition> live) =>
        live.Any(row => row.Quantity > 0m && string.Equals(CoinKey(row.Symbol), CoinKey(symbol), StringComparison.OrdinalIgnoreCase));

    public static bool IsLiveGhost(Position position, IReadOnlyList<LiveOpenPosition> live, DateTimeOffset now) =>
        position.Quantity > 0m
        && position.ClosedAt is null
        && now - position.OpenedAt >= OverlayGrace
        && !IsOnExchange(position.Symbol, live);

    public static bool IsCoinOpen(
        string symbol,
        IReadOnlyList<Position> book,
        IReadOnlyList<LiveOpenPosition>? live,
        bool liveAuthoritative,
        DateTimeOffset now)
    {
        if (live is { Count: >= 0 } && IsOnExchange(symbol, live))
        {
            return true;
        }

        return book.Any(row =>
            string.Equals(CoinKey(row.Symbol), CoinKey(symbol), StringComparison.OrdinalIgnoreCase)
            && row.Quantity > 0m
            && (!liveAuthoritative || now - row.OpenedAt < OverlayGrace));
    }

    public static string NormalizeSide(string side) =>
        side is "Buy" or "BUY" or "Long" or "LONG" ? "Long"
        : side is "Sell" or "SELL" or "Short" or "SHORT" ? "Short"
        : side;

    public static int UniqueCoins(IEnumerable<string> symbols) =>
        symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(CoinKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    public static IReadOnlyList<Position> ForStrategy(IReadOnlyList<Position> book, Guid strategyId) =>
        book.Where(row => row.Bot?.StrategyVersion?.StrategyId == strategyId).ToList();

    public static decimal PlannedRiskPercent(IReadOnlyList<Position> book, decimal available) =>
        available > 0m
            ? book.Where(row => row.Quantity > 0m).Sum(row => row.InitialRiskUsdt) / available * 100m
            : 0m;

    /// <summary>
    /// Max simultaneous Isolated slots are per running strategy. Live coins owned by other strategies are ignored.
    /// One Isolated position per coin still applies globally via <see cref="IsCoinOpen"/>.
    /// </summary>
    public static int UniqueCoinsForStrategy(
        IReadOnlyList<Position> book,
        Guid strategyId,
        IReadOnlyList<LiveOpenPosition>? live = null,
        bool liveAuthoritative = false,
        DateTimeOffset? now = null)
    {
        var strategyBook = ForStrategy(book, strategyId);
        var owned = new HashSet<string>(
            strategyBook.Where(row => row.Quantity > 0m).Select(row => CoinKey(row.Symbol)),
            StringComparer.OrdinalIgnoreCase);
        var scopedLive = live?
            .Where(row => row.Quantity > 0m && owned.Contains(CoinKey(row.Symbol)))
            .ToList();
        return UniqueCoins(strategyBook, scopedLive, liveAuthoritative, now);
    }

    public static int UniqueCoins(
        IReadOnlyList<Position> book,
        IReadOnlyList<LiveOpenPosition>? live = null,
        bool liveAuthoritative = false,
        DateTimeOffset? now = null)
    {
        if (liveAuthoritative)
        {
            var coins = (live ?? [])
                .Where(row => row.Quantity > 0m)
                .Select(row => row.Symbol)
                .ToList();
            var stamp = now ?? DateTimeOffset.UtcNow;
            coins.AddRange(
                book.Where(row => row.Quantity > 0m && stamp - row.OpenedAt < OverlayGrace)
                    .Select(row => row.Symbol));
            return UniqueCoins(coins);
        }

        var all = book.Where(row => row.Quantity > 0m).Select(row => row.Symbol).ToList();
        if (live is { Count: > 0 })
        {
            all.AddRange(live.Where(row => row.Quantity > 0m).Select(row => row.Symbol));
        }

        return UniqueCoins(all);
    }

    public static decimal UniqueUnrealized(
        IReadOnlyList<Position> book,
        IReadOnlyList<LiveOpenPosition> live,
        bool liveAuthoritative = false,
        DateTimeOffset? now = null)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sum = 0m;
        foreach (var row in live.Where(item => item.Quantity > 0m))
        {
            if (taken.Add(CoinKey(row.Symbol)))
            {
                sum += row.UnrealizedPnL;
            }
        }

        if (liveAuthoritative)
        {
            var stamp = now ?? DateTimeOffset.UtcNow;
            foreach (var row in book.Where(item => item.Quantity > 0m && stamp - item.OpenedAt < OverlayGrace))
            {
                if (taken.Add(CoinKey(row.Symbol)))
                {
                    sum += row.UnrealizedPnL;
                }
            }

            return sum;
        }

        foreach (var row in book.Where(item => item.Quantity > 0m))
        {
            if (taken.Add(CoinKey(row.Symbol)))
            {
                sum += row.UnrealizedPnL;
            }
        }

        return sum;
    }

    public static List<PositionDto> MergeBotAndExchange(
        IReadOnlyList<PositionDto> botPositions,
        IReadOnlyList<LiveOpenPosition> livePositions,
        Func<LiveOpenPosition, PositionDto> mapExchange,
        bool liveAuthoritative = false,
        DateTimeOffset? now = null)
    {
        var bots = botPositions
            .Where(row => row.Quantity > 0m)
            .GroupBy(row => CoinKey(row.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var merged = new List<PositionDto>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var live in livePositions.Where(row => row.Quantity > 0m))
        {
            var key = CoinKey(live.Symbol);
            if (!taken.Add(key))
            {
                continue;
            }

            var mapped = mapExchange(live);
            if (bots.TryGetValue(key, out var bot))
            {
                var entry = mapped.AverageEntryPrice > 0m ? mapped.AverageEntryPrice : bot.AverageEntryPrice;
                merged.Add(bot with
                {
                    Quantity = mapped.Quantity,
                    AverageEntryPrice = entry,
                    CurrentPrice = mapped.CurrentPrice > 0m ? mapped.CurrentPrice : bot.CurrentPrice,
                    UnrealizedPnL = mapped.UnrealizedPnL,
                    NotionalUsdt = mapped.Quantity * entry
                });
            }
            else
            {
                merged.Add(mapped);
            }
        }

        var stamp = now ?? DateTimeOffset.UtcNow;
        foreach (var bot in bots.Values)
        {
            if (!taken.Add(CoinKey(bot.Symbol)))
            {
                continue;
            }

            if (liveAuthoritative && stamp - bot.OpenedAt >= OverlayGrace)
            {
                continue;
            }

            merged.Add(bot);
        }

        return merged;
    }

    public static PositionDto StampProtection(
        PositionDto row,
        decimal stopLossPercent,
        decimal takeProfitPercent,
        decimal riskPerTradePercent,
        decimal maxLeverage,
        decimal tickSize,
        Guid? botId = null)
    {
        if (row.Quantity <= 0m || row.AverageEntryPrice <= 0m)
        {
            return row;
        }

        var side = NormalizeSide(row.Side) == "Short" ? PositionSide.Short : PositionSide.Long;
        var (stop, take) = LiveProtectivePrices.FromEntry(
            row.AverageEntryPrice,
            stopLossPercent,
            takeProfitPercent,
            tickSize,
            side);
        var notional = row.NotionalUsdt > 0m ? row.NotionalUsdt : row.Quantity * row.AverageEntryPrice;
        var leverage = maxLeverage > 0m ? maxLeverage : 1m;
        var liquidation = side == PositionSide.Short
            ? row.AverageEntryPrice * (1m + 1m / leverage)
            : row.AverageEntryPrice * (1m - 1m / leverage);
        return row with
        {
            BotId = row.BotId == Guid.Empty && botId is { } id && id != Guid.Empty ? id : row.BotId,
            InitialRiskUsdt = row.InitialRiskUsdt > 0m ? row.InitialRiskUsdt : notional * (stopLossPercent / 100m),
            MarginUsdt = row.MarginUsdt > 0m ? row.MarginUsdt : PortfolioRisk.IsolatedMargin(notional, leverage),
            NotionalUsdt = notional,
            Leverage = row.Leverage > 0m ? row.Leverage : leverage,
            StopLossPercent = row.StopLossPercent > 0m ? row.StopLossPercent : stopLossPercent,
            TakeProfitPercent = row.TakeProfitPercent > 0m ? row.TakeProfitPercent : takeProfitPercent,
            StopLossPrice = row.StopLossPrice > 0m ? row.StopLossPrice : stop,
            TakeProfitPrice = row.TakeProfitPrice > 0m ? row.TakeProfitPrice : take,
            LiquidationPrice = row.LiquidationPrice > 0m ? row.LiquidationPrice : liquidation,
            RiskPerTradePercent = row.RiskPerTradePercent > 0m ? row.RiskPerTradePercent : riskPerTradePercent
        };
    }
}
