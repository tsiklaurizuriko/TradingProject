using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;
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
        if (live is not null && IsOnExchange(symbol, live))
        {
            return true;
        }

        // Isolated is one Binance position per coin. A DB snapshot from any strategy occupies
        // the coin. Do not ignore rows older than OverlayGrace when the overlay omitted them —
        // that let a second strategy add to the same Isolated position.
        return book.Any(row =>
            string.Equals(CoinKey(row.Symbol), CoinKey(symbol), StringComparison.OrdinalIgnoreCase)
            && row.Quantity > 0m);
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

    public static IReadOnlyList<Position> ForStrategy(
        IReadOnlyList<Position> book,
        Guid strategyId,
        IReadOnlySet<Guid>? strategyBotIds = null,
        IReadOnlySet<Guid>? strategyVersionIds = null) =>
        book.Where(row =>
                row.Bot?.StrategyVersion?.StrategyId == strategyId
                || (strategyBotIds is { Count: > 0 } && strategyBotIds.Contains(row.BotId))
                || (strategyVersionIds is { Count: > 0 }
                    && row.Bot is not null
                    && strategyVersionIds.Contains(row.Bot.StrategyVersionId)))
            .ToList();

    public static decimal PlannedRiskPercent(IReadOnlyList<Position> book, decimal available) =>
        available > 0m
            ? book.Where(row => row.Quantity > 0m).Sum(row => row.InitialRiskUsdt) / available * 100m
            : 0m;

    /// <summary>
    /// Max simultaneous Isolated slots are per running strategy. Another strategy's coin does not fill this cap.
    /// One Isolated position per coin still applies globally via <see cref="IsCoinOpen"/>.
    /// When two books claim the same coin, the earliest fill owns it.
    /// Overlay lag must not drop those slots to zero; ghosts after <see cref="OverlayGrace"/> do not keep a slot
    /// once Binance still shows other coins.
    /// </summary>
    public static int UniqueCoinsForStrategy(
        IReadOnlyList<Position> book,
        Guid strategyId,
        IReadOnlyList<LiveOpenPosition>? live = null,
        bool liveAuthoritative = false,
        DateTimeOffset? now = null,
        IReadOnlySet<Guid>? strategyBotIds = null,
        IReadOnlySet<Guid>? strategyVersionIds = null)
    {
        var owned = OccupiedByStrategy(book, strategyId, strategyBotIds, strategyVersionIds);
        if (liveAuthoritative && live is { Count: > 0 })
        {
            var stamp = now ?? DateTimeOffset.UtcNow;
            var liveCoins = live
                .Where(row => row.Quantity > 0m)
                .Select(row => CoinKey(row.Symbol))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            owned = owned
                .Where(row => liveCoins.Contains(CoinKey(row.Symbol)) || stamp - row.OpenedAt < OverlayGrace)
                .ToList();
        }

        return UniqueCoins(owned.Select(row => row.Symbol));
    }

    public static IReadOnlyList<Position> OccupiedByStrategy(
        IReadOnlyList<Position> book,
        Guid strategyId,
        IReadOnlySet<Guid>? strategyBotIds = null,
        IReadOnlySet<Guid>? strategyVersionIds = null)
    {
        var owners = book
            .Where(row => row.Quantity > 0m)
            .GroupBy(row => CoinKey(row.Symbol), StringComparer.OrdinalIgnoreCase)
            .Select(IsolatedOwner);
        return owners.Where(row => OwnsStrategy(row, strategyId, strategyBotIds, strategyVersionIds)).ToList();
    }

    public static Position IsolatedOwner(IEnumerable<Position> rows) =>
        rows
            .Where(row => row.Quantity > 0m)
            .OrderBy(row => row.OpenedAt)
            .ThenBy(row => row.BotId)
            .First();

    public static PositionDto IsolatedOwner(IEnumerable<PositionDto> rows) =>
        rows
            .Where(row => row.Quantity > 0m)
            .OrderBy(row => row.BotId == Guid.Empty ? 1 : 0)
            .ThenBy(row => row.OpenedAt)
            .ThenBy(row => row.BotId)
            .First();

    /// <summary>
    /// One Isolated coin has one owner. An open snapshot owns it; otherwise the earliest-started
    /// running bot on that coin may enter. Later bots wait until it is flat.
    /// </summary>
    public static Bot? PickLiveOwner(
        string symbol,
        IReadOnlyList<Bot> bots,
        IReadOnlyList<Position> book,
        TradingMode? mode = null)
    {
        var key = CoinKey(symbol);
        var holders = book
            .Where(row =>
                row.Quantity > 0m
                && string.Equals(CoinKey(row.Symbol), key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (holders.Count > 0)
        {
            var owner = IsolatedOwner(holders);
            return bots.FirstOrDefault(bot => bot.Id == owner.BotId) ?? owner.Bot;
        }

        return bots
            .Where(bot =>
                (mode is null || bot.Mode == mode)
                && string.Equals(CoinKey(bot.Symbol), key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(bot => bot.Status == BotStatus.Running ? 0 : 1)
            .ThenBy(bot => bot.StartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(bot => bot.Id)
            .FirstOrDefault();
    }

    public static bool IsOwner(Bot bot, IReadOnlyList<Bot> peers, IReadOnlyList<Position> book) =>
        PickLiveOwner(bot.Symbol, peers, book, bot.Mode)?.Id == bot.Id;

    /// <summary>
    /// A bot created after the fill cannot be the strategy that opened it.
    /// Flat Range on every coin was adopting older Isolated positions and the open count collapsed onto it.
    /// </summary>
    public static bool BotExistedAtOpen(DateTimeOffset botCreatedAt, DateTimeOffset openedAt) =>
        botCreatedAt <= openedAt.AddSeconds(60);

    /// <summary>
    /// Give an adopted fill back to a bot on that coin that already existed when it opened.
    /// Prefer a running bot, then the one created latest before the fill.
    /// </summary>
    public static Bot? PriorOwner(
        IEnumerable<Bot> bots,
        string symbol,
        DateTimeOffset openedAt,
        Guid exceptBotId)
    {
        var key = CoinKey(symbol);
        return bots
            .Where(bot =>
                bot.Id != exceptBotId
                && bot.StrategyVersion is not null
                && string.Equals(CoinKey(bot.Symbol), key, StringComparison.OrdinalIgnoreCase)
                && BotExistedAtOpen(bot.CreatedAt, openedAt))
            .OrderBy(bot => bot.Status == BotStatus.Running ? 0 : 1)
            .ThenByDescending(bot => bot.CreatedAt)
            .ThenBy(bot => bot.Id)
            .FirstOrDefault();
    }

    public static bool IsExchangeLedgerKey(string? correlationId) =>
        !string.IsNullOrWhiteSpace(correlationId)
        && (correlationId.StartsWith("BNT", StringComparison.OrdinalIgnoreCase)
            || correlationId.StartsWith("BNI", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// One Isolated round-trip is one row. Overlapping same-coin same-size closes from different
    /// bots are the same Binance position recorded twice.
    /// </summary>
    public static IReadOnlyList<T> UniqueClosedTrips<T>(
        IReadOnlyList<T> rows,
        Func<T, string> symbol,
        Func<T, decimal> quantity,
        Func<T, DateTimeOffset> openedAt,
        Func<T, DateTimeOffset?> closedAt,
        Func<T, string?> correlationId,
        Func<T, decimal>? fees = null,
        Func<T, string?>? strategy = null) =>
        ClosedTripMatch.Unique(rows, symbol, quantity, openedAt, closedAt, correlationId, fees, strategy);

    public static bool SameIsolatedTrip(
        string leftSymbol,
        decimal leftQty,
        DateTimeOffset leftOpened,
        DateTimeOffset? leftClosed,
        string rightSymbol,
        decimal rightQty,
        DateTimeOffset rightOpened,
        DateTimeOffset? rightClosed) =>
        ClosedTripMatch.Same(
            leftSymbol,
            leftQty,
            leftOpened,
            leftClosed,
            rightSymbol,
            rightQty,
            rightOpened,
            rightClosed);

    private static bool OwnsStrategy(
        Position row,
        Guid strategyId,
        IReadOnlySet<Guid>? strategyBotIds,
        IReadOnlySet<Guid>? strategyVersionIds) =>
        row.Bot?.StrategyVersion?.StrategyId == strategyId
        || (strategyBotIds is { Count: > 0 } && strategyBotIds.Contains(row.BotId))
        || (strategyVersionIds is { Count: > 0 }
            && row.Bot is not null
            && strategyVersionIds.Contains(row.Bot.StrategyVersionId));

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
            .ToDictionary(
                group => group.Key,
                group => IsolatedOwner(group),
                StringComparer.OrdinalIgnoreCase);
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
