using Microsoft.EntityFrameworkCore;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Backtesting;
using TradingPlatform.Domain.Balances;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Operations;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Signals;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Infrastructure.Persistence;

namespace TradingPlatform.Infrastructure.Persistence;

public sealed class TradingStore : ITradingStore
{
    private readonly TradingDbContext _db;

    public TradingStore(TradingDbContext db)
    {
        _db = db;
    }

    public async Task<User> GetFirstAdminAsync(CancellationToken cancellationToken = default) =>
        await _db.Users.OrderBy(u => u.CreatedAt).FirstAsync(cancellationToken);

    public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public async Task<IReadOnlyList<Bot>> GetRunningBotsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await BotsWithGraph().Where(b => b.Status == BotStatus.Running).ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    public async Task<IReadOnlyList<Bot>> ListBotsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await BotsWithGraph().ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    public async Task<IReadOnlyList<Bot>> ListWorkspaceBotsAsync(
        Guid userId,
        TradingMode mode,
        CancellationToken cancellationToken = default) =>
        await BotsWithGraph()
            .Where(bot => bot.UserId == userId && bot.Mode == mode)
            .ToListAsync(cancellationToken);

    public Task<Bot?> GetBotAsync(Guid botId, CancellationToken cancellationToken = default) =>
        BotsWithGraph().FirstOrDefaultAsync(b => b.Id == botId, cancellationToken);

    public Task<Bot?> FindPaperBotBySymbolAsync(Guid userId, string symbol, CancellationToken cancellationToken = default) =>
        FindBotBySymbolAsync(userId, symbol, TradingMode.Paper, null, null, cancellationToken);

    public Task<Bot?> FindBotBySymbolAsync(
        Guid userId,
        string symbol,
        TradingMode mode,
        Guid? strategyId,
        Guid? riskProfileId,
        CancellationToken cancellationToken = default)
    {
        var name = symbol.ToUpperInvariant();
        var query = BotsWithGraph().Where(b => b.UserId == userId && b.Symbol == name && b.Mode == mode);
        if (strategyId is { } sid && sid != Guid.Empty)
        {
            query = query.Where(b => b.StrategyVersion.StrategyId == sid);
        }

        if (riskProfileId is { } rid && rid != Guid.Empty)
        {
            query = query.Where(b => b.RiskProfileId == rid);
        }

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddBotAsync(Bot bot, CancellationToken cancellationToken = default)
    {
        await _db.Bots.AddAsync(bot, cancellationToken);
    }

    public async Task AddBotRunAsync(BotRun run, CancellationToken cancellationToken = default)
    {
        await _db.BotRuns.AddAsync(run, cancellationToken);
    }

    public async Task<StrategyVersion> GetSampleStrategyVersionAsync(CancellationToken cancellationToken = default)
    {
        var enabled = await _db.StrategyVersions
            .Include(v => v.Strategy)
            .Where(v => v.Strategy.IsEnabled)
            .OrderBy(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (enabled is not null)
        {
            return enabled;
        }

        return await _db.StrategyVersions
            .Include(v => v.Strategy)
            .OrderBy(v => v.VersionNumber)
            .FirstAsync(cancellationToken);
    }

    public async Task<StrategyVersion?> GetLatestStrategyVersionAsync(Guid strategyId, CancellationToken cancellationToken = default)
    {
        return await _db.StrategyVersions
            .Include(v => v.Strategy)
            .Where(v => v.StrategyId == strategyId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Strategy>> ListStrategiesAsync(CancellationToken cancellationToken = default) =>
        await _db.Strategies
            .Include(s => s.Versions)
            .Include(s => s.RiskProfile)
            .Where(s => !s.IsArchived)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

    public Task<Strategy?> GetStrategyAsync(Guid strategyId, CancellationToken cancellationToken = default) =>
        _db.Strategies.Include(s => s.Versions).Include(s => s.RiskProfile).FirstOrDefaultAsync(s => s.Id == strategyId, cancellationToken);

    public async Task AddStrategyAsync(Strategy strategy, CancellationToken cancellationToken = default) =>
        await _db.Strategies.AddAsync(strategy, cancellationToken);

    public async Task<RiskProfile> GetConservativeRiskAsync(CancellationToken cancellationToken = default)
    {
        var books = await _db.RiskProfiles.OrderBy(r => r.RiskPerTradePercent).ToListAsync(cancellationToken);
        if (books.Count == 0)
        {
            await SystemRiskCatalog.EnsureAsync(_db, cancellationToken);
            books = await _db.RiskProfiles.OrderBy(r => r.RiskPerTradePercent).ToListAsync(cancellationToken);
        }

        if (books.Count == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "No Isolated risk book is seeded.");
        }

        return books.FirstOrDefault(r => r.IsActive)
            ?? books.FirstOrDefault(r => string.Equals(r.Name, "LOW", StringComparison.OrdinalIgnoreCase))
            ?? books.FirstOrDefault(r => string.Equals(r.Name, "Low Risk", StringComparison.OrdinalIgnoreCase))
            ?? books.First();
    }

    public Task<RiskProfile?> GetRiskProfileByIdAsync(Guid riskProfileId, CancellationToken cancellationToken = default) =>
        _db.RiskProfiles.FirstOrDefaultAsync(r => r.Id == riskProfileId, cancellationToken);

    public async Task<IReadOnlyList<RiskProfile>> ListRiskProfilesAsync(CancellationToken cancellationToken = default) =>
        await _db.RiskProfiles.OrderBy(r => r.RiskPerTradePercent).ThenBy(r => r.Name).ToListAsync(cancellationToken);

    public async Task AddRiskProfileAsync(RiskProfile risk, CancellationToken cancellationToken = default) =>
        await _db.RiskProfiles.AddAsync(risk, cancellationToken);

    public async Task<ExchangeAccount> GetOrCreatePaperAccountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await _db.ExchangeAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Name == "Paper Simulator", cancellationToken);
        if (account is not null)
        {
            return account;
        }

        account = new ExchangeAccount
        {
            UserId = userId,
            Name = "Paper Simulator",
            Exchange = ExchangeType.BinanceSpot,
            IsTestnet = false,
            LiveEnabled = false,
            CanTrade = true,
            ApiKeyFingerprint = "paper"
        };
        await _db.ExchangeAccounts.AddAsync(account, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<Symbol> UpsertSymbolAsync(
        string name,
        string baseAsset,
        string quoteAsset,
        decimal tickSize,
        decimal stepSize,
        decimal minQuantity,
        decimal minNotional,
        int pricePrecision,
        int quantityPrecision,
        CancellationToken cancellationToken = default)
    {
        var symbolName = name.ToUpperInvariant();
        var existing = await _db.Symbols.FirstOrDefaultAsync(s => s.Name == symbolName, cancellationToken);
        if (existing is null)
        {
            existing = new Symbol
            {
                Name = symbolName,
                BaseAsset = baseAsset,
                QuoteAsset = quoteAsset,
                TickSize = tickSize,
                StepSize = stepSize,
                MinQuantity = minQuantity,
                MinNotional = minNotional,
                PricePrecision = pricePrecision,
                QuantityPrecision = quantityPrecision,
                IsActive = true
            };
            await _db.Symbols.AddAsync(existing, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        existing.BaseAsset = baseAsset;
        existing.QuoteAsset = quoteAsset;
        existing.TickSize = tickSize;
        existing.StepSize = stepSize;
        existing.MinQuantity = minQuantity;
        existing.MinNotional = minNotional;
        existing.PricePrecision = pricePrecision;
        existing.QuantityPrecision = quantityPrecision;
        existing.IsActive = true;
        await _db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public Task<Symbol?> GetSymbolAsync(string name, CancellationToken cancellationToken = default) =>
        _db.Symbols.FirstOrDefaultAsync(s => s.Name == name.ToUpperInvariant(), cancellationToken);

    public async Task UpsertClosedCandleAsync(Guid symbolId, Timeframe timeframe, MarketCandle candle, CancellationToken cancellationToken = default)
    {
        var local = _db.MarketCandles.Local.FirstOrDefault(row =>
            row.SymbolId == symbolId && row.Timeframe == timeframe && row.OpenTime == candle.OpenTime);
        if (local is not null)
        {
            CopyClosedCandle(local, candle);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "MarketCandles" (
                "Id", "SymbolId", "Timeframe", "OpenTime", "CloseTime",
                "Open", "High", "Low", "Close", "Volume", "TradeCount",
                "IsClosed", "ExchangeTimestamp", "CreatedAt", "UpdatedAt")
            VALUES (
                {Guid.NewGuid()}, {symbolId}, {(int)timeframe}, {candle.OpenTime}, {candle.CloseTime},
                {candle.Open}, {candle.High}, {candle.Low}, {candle.Close}, {candle.Volume}, {candle.TradeCount},
                TRUE, {candle.ExchangeTimestamp}, {now}, {now})
            ON CONFLICT ("SymbolId", "Timeframe", "OpenTime")
            DO UPDATE SET
                "CloseTime" = EXCLUDED."CloseTime",
                "Open" = EXCLUDED."Open",
                "High" = EXCLUDED."High",
                "Low" = EXCLUDED."Low",
                "Close" = EXCLUDED."Close",
                "Volume" = EXCLUDED."Volume",
                "TradeCount" = EXCLUDED."TradeCount",
                "IsClosed" = TRUE,
                "ExchangeTimestamp" = EXCLUDED."ExchangeTimestamp",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """,
            cancellationToken);
    }

    public Task<Position?> GetOpenPositionAsync(Guid botId, string symbol, CancellationToken cancellationToken = default) =>
        _db.Positions.FirstOrDefaultAsync(
            p => p.BotId == botId && p.Symbol == symbol && p.ClosedAt == null && p.Quantity > 0m,
            cancellationToken);

    public Task<Position?> GetOpenPositionByIdAsync(Guid positionId, CancellationToken cancellationToken = default) =>
        _db.Positions.FirstOrDefaultAsync(
            p => p.Id == positionId && p.ClosedAt == null && p.Quantity > 0m,
            cancellationToken);

    public async Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.Positions
                .Where(p => p.ClosedAt == null && p.Quantity > 0m)
                .OrderByDescending(p => p.OpenedAt)
                .ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    public async Task<IReadOnlyList<Position>> GetOpenPositionsForModeAsync(TradingMode mode, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _db.Positions
                .Include(p => p.Events)
                .Where(p => p.ClosedAt == null && p.Quantity > 0m)
                .OrderByDescending(p => p.OpenedAt)
                .ToListAsync(cancellationToken);
            var botIds = rows.Select(position => position.BotId).Distinct().ToList();
            var bots = await _db.Bots
                .Where(bot => botIds.Contains(bot.Id))
                .ToListAsync(cancellationToken);
            var versionIds = bots.Select(bot => bot.StrategyVersionId).Distinct().ToList();
            var versions = await _db.StrategyVersions
                .Where(version => versionIds.Contains(version.Id))
                .ToListAsync(cancellationToken);
            foreach (var bot in bots)
            {
                bot.StrategyVersion = versions.FirstOrDefault(version => version.Id == bot.StrategyVersionId)!;
            }

            foreach (var position in rows)
            {
                position.Bot = bots.FirstOrDefault(bot => bot.Id == position.BotId)!;
            }

            return rows
                .Where(position => position.Bot is null
                    ? mode == TradingMode.Live
                    : position.Bot.Mode == mode)
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    public Task<int> CountOpenPositionsAsync(Guid botId, CancellationToken cancellationToken = default) =>
        _db.Positions.CountAsync(p => p.BotId == botId && p.ClosedAt == null && p.Quantity > 0m, cancellationToken);

    public Task<int> CountOrdersSinceForModeAsync(TradingMode mode, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        _db.Orders.CountAsync(o => o.Mode == mode && o.CreatedAt >= sinceUtc, cancellationToken);

    public async Task<decimal> SumClosedPnLSinceForModeAsync(TradingMode mode, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
    {
        var rows = await LoadClosedSinceAsync(mode, sinceUtc, cancellationToken);
        return ClosedTripMatch.Unique(
            rows,
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => t.Fees).Sum(t => t.PnL);
    }

    public async Task<int> CollapseDuplicateClosedTripsAsync(CancellationToken cancellationToken = default)
    {
        var trades = await _db.Trades
            .IgnoreQueryFilters()
            .Where(t => t.ClosedAt != null)
            .ToListAsync(cancellationToken);
        if (trades.Count < 2)
        {
            return 0;
        }

        var keep = ClosedTripMatch.Unique(
                trades,
                t => t.Symbol,
                t => t.Quantity,
                t => t.OpenedAt,
                t => t.ClosedAt,
                t => t.CorrelationId,
                t => t.Fees)
            .Select(t => t.Id)
            .ToHashSet();
        var extras = trades.Where(t => !keep.Contains(t.Id)).ToList();
        if (extras.Count == 0)
        {
            return 0;
        }

        _db.Trades.RemoveRange(extras);
        return extras.Count;
    }

    public async Task StopRunningBotsForModeAsync(TradingMode mode, string reason, CancellationToken cancellationToken = default)
    {
        var bots = await _db.Bots
            .Where(b => b.Mode == mode && (b.Status == BotStatus.Running || b.Status == BotStatus.Starting))
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var bot in bots)
        {
            bot.Status = BotStatus.Stopped;
            bot.StoppedAt = now;
            bot.LastError = reason;
        }
    }

    public async Task AddPositionAsync(Position position, CancellationToken cancellationToken = default)
    {
        await _db.Positions.AddAsync(position, cancellationToken);
    }

    public Task<bool> HasClientOrderAsync(string clientOrderId, CancellationToken cancellationToken = default) =>
        _db.Orders.AnyAsync(o => o.ClientOrderId == clientOrderId, cancellationToken);

    public async Task<bool> HasKnownOrderAsync(
        string? clientOrderId,
        string? exchangeOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(clientOrderId)
            && await _db.Orders.AnyAsync(o => o.ClientOrderId == clientOrderId, cancellationToken))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(exchangeOrderId)
            && await _db.Orders.AnyAsync(o => o.ExchangeOrderId == exchangeOrderId, cancellationToken);
    }

    public Task<Order?> GetOrderByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default) =>
        _db.Orders.FirstOrDefaultAsync(o => o.ClientOrderId == clientOrderId, cancellationToken);

    public async Task<IReadOnlyList<Order>> GetUnresolvedLiveOrdersAsync(CancellationToken cancellationToken = default) =>
        await _db.Orders
            .Include(order => order.Executions)
            .Include(order => order.Bot)
            .ThenInclude(bot => bot.StrategyVersion)
            .Where(order => order.Mode == TradingMode.Live
                && (order.Status == OrderStatus.Uncertain
                    || order.Status == OrderStatus.Submitting
                    || order.Status == OrderStatus.PartiallyFilled))
            .ToListAsync(cancellationToken);

    public async Task AddPositionEventAsync(PositionEvent positionEvent, CancellationToken cancellationToken = default)
    {
        await _db.PositionEvents.AddAsync(positionEvent, cancellationToken);
    }

    public Task<bool> HasUnresolvedEntryAsync(Guid botId, string symbol, CancellationToken cancellationToken = default) =>
        _db.Orders.AnyAsync(
            order => order.BotId == botId
                && order.Symbol == symbol
                && (order.Status == OrderStatus.Uncertain
                    || order.Status == OrderStatus.PartiallyFilled
                    || order.Status == OrderStatus.Submitting),
            cancellationToken);

    public Task<int> CountOrdersSinceAsync(Guid botId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        _db.Orders.CountAsync(o => o.BotId == botId && o.CreatedAt >= sinceUtc, cancellationToken);

    public async Task AddOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _db.Orders.AddAsync(order, cancellationToken);
    }

    public async Task AddExecutionAsync(Execution execution, CancellationToken cancellationToken = default)
    {
        await _db.Executions.AddAsync(execution, cancellationToken);
    }

    public async Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        await _db.Signals.AddAsync(signal, cancellationToken);
    }

    public async Task AddTradeAsync(Trade trade, CancellationToken cancellationToken = default)
    {
        await _db.Trades.AddAsync(trade, cancellationToken);
    }

    public Task<bool> HasTradeCorrelationAsync(string correlationId, CancellationToken cancellationToken = default) =>
        _db.Trades.AnyAsync(t => t.CorrelationId == correlationId, cancellationToken);

    public async Task<IReadOnlyList<Trade>> FindClosedTradesAroundAsync(
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var name = symbol.ToUpperInvariant();
        var start = from - TimeSpan.FromMinutes(15);
        var end = to + TimeSpan.FromMinutes(15);
        return await _db.Trades
            .Where(t => t.Symbol == name
                && t.ClosedAt != null
                && t.ClosedAt >= start
                && t.ClosedAt <= end)
            .ToListAsync(cancellationToken);
    }

    public void RemoveTrade(Trade trade) => _db.Trades.Remove(trade);

    public Task<Trade?> FindClosedTradeNearAsync(
        string symbol,
        decimal quantity,
        DateTimeOffset openedAt,
        DateTimeOffset around,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        var name = symbol.ToUpperInvariant();
        var from = around - window;
        var to = around + window;
        return _db.Trades.FirstOrDefaultAsync(
            t => t.Symbol == name
                && t.ClosedAt != null
                && t.ClosedAt >= from
                && t.ClosedAt <= to
                && t.OpenedAt <= around
                && openedAt <= t.ClosedAt
                && t.Quantity == quantity,
            cancellationToken);
    }

    public Task<Trade?> GetOpenTradeAsync(Guid botId, CancellationToken cancellationToken = default) =>
        _db.Trades.FirstOrDefaultAsync(t => t.BotId == botId && t.ClosedAt == null, cancellationToken);

    public async Task<decimal> SumClosedPnLSinceAsync(Guid botId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _db.Trades
            .AsNoTracking()
            .Where(t => t.BotId == botId && t.ClosedAt != null && t.ClosedAt >= sinceUtc)
            .Select(t => new ClosedPnlRow(t.Symbol, t.Quantity, t.OpenedAt, t.ClosedAt, t.PnL, t.Fees, t.CorrelationId))
            .ToListAsync(cancellationToken);
        return ClosedTripMatch.Unique(
            rows,
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => t.Fees).Sum(t => t.PnL);
    }

    public async Task<(int ConsecutiveLosses, DateTimeOffset? LastLossAt)> GetLossStreakAsync(
        Guid botId,
        CancellationToken cancellationToken = default)
    {
        var recent = await _db.Trades
            .Where(t => t.BotId == botId && t.ClosedAt != null)
            .OrderByDescending(t => t.ClosedAt)
            .Take(20)
            .Select(t => new { t.PnL, t.ClosedAt })
            .ToListAsync(cancellationToken);

        var losses = 0;
        DateTimeOffset? lastLoss = null;
        foreach (var trade in recent)
        {
            if (trade.PnL >= 0m)
            {
                break;
            }

            losses++;
            lastLoss ??= trade.ClosedAt;
        }

        return (losses, lastLoss);
    }

    public async Task<(int ConsecutiveLosses, DateTimeOffset? LastLossAt)> GetLossStreakForModeAsync(
        TradingMode mode,
        CancellationToken cancellationToken = default)
    {
        var recent = await LoadClosedSinceAsync(mode, DateTimeOffset.UnixEpoch, cancellationToken, take: 80);
        var unique = ClosedTripMatch.Unique(
            recent,
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => t.Fees);
        return ClosedTripMatch.ConsecutiveLosses(unique, t => t.PnL, t => t.ClosedAt);
    }

    public async Task<(int ConsecutiveLosses, DateTimeOffset? LastLossAt)> GetSymbolLossStreakAsync(
        TradingMode mode,
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var name = symbol.Trim().ToUpperInvariant();
        var recent = await _db.Trades
            .AsNoTracking()
            .Where(t => t.Bot.Mode == mode && t.Symbol == name && t.ClosedAt != null)
            .OrderByDescending(t => t.ClosedAt)
            .Take(40)
            .Select(t => new ClosedPnlRow(t.Symbol, t.Quantity, t.OpenedAt, t.ClosedAt, t.PnL, t.Fees, t.CorrelationId))
            .ToListAsync(cancellationToken);
        var unique = ClosedTripMatch.Unique(
            recent,
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => t.Fees);
        return ClosedTripMatch.ConsecutiveLosses(unique, t => t.PnL, t => t.ClosedAt);
    }

    private async Task<List<ClosedPnlRow>> LoadClosedSinceAsync(
        TradingMode mode,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken,
        int? take = null)
    {
        var query = _db.Trades
            .AsNoTracking()
            .Where(t => t.ClosedAt != null && t.ClosedAt >= sinceUtc && t.Bot.Mode == mode)
            .OrderByDescending(t => t.ClosedAt);
        var limited = take is { } cap ? query.Take(cap) : query;
        return await limited
            .Select(t => new ClosedPnlRow(t.Symbol, t.Quantity, t.OpenedAt, t.ClosedAt, t.PnL, t.Fees, t.CorrelationId))
            .ToListAsync(cancellationToken);
    }

    private sealed record ClosedPnlRow(
        string Symbol,
        decimal Quantity,
        DateTimeOffset OpenedAt,
        DateTimeOffset? ClosedAt,
        decimal PnL,
        decimal Fees,
        string? CorrelationId);

    public async Task<Balance> GetOrCreateBalanceAsync(
        Guid exchangeAccountId,
        Guid? botId,
        string asset,
        TradingMode mode,
        decimal initialFree,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.Balances.FirstOrDefaultAsync(
            b => b.ExchangeAccountId == exchangeAccountId && b.BotId == botId && b.Asset == asset && b.Mode == mode,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var balance = new Balance
        {
            ExchangeAccountId = exchangeAccountId,
            BotId = botId,
            Asset = asset,
            Mode = mode,
            Free = initialFree,
            Locked = 0m
        };
        await _db.Balances.AddAsync(balance, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return balance;
    }

    public async Task<IReadOnlyList<Balance>> GetPaperBalancesAsync(CancellationToken cancellationToken = default) =>
        await _db.Balances.Where(b => b.Mode == TradingMode.Paper).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> GetRecentOrdersAsync(int take, CancellationToken cancellationToken = default) =>
        await _db.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Trade>> GetRecentTradesAsync(int take, CancellationToken cancellationToken = default) =>
        await _db.Trades
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(t => t.Bot)
            .OrderByDescending(t => t.ClosedAt ?? t.OpenedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PerformanceTradeRow>> GetPerformanceTradesAsync(
        TradingMode mode,
        CancellationToken cancellationToken = default) =>
        await _db.Trades
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.Bot.Mode == mode)
            .OrderByDescending(t => t.ClosedAt ?? t.OpenedAt)
            .Take(5000)
            .Select(t => new PerformanceTradeRow(
                t.Id,
                t.BotId,
                t.Symbol,
                t.Quantity,
                t.EntryPrice,
                t.ExitPrice,
                t.PnL,
                t.PnLPercent,
                t.Fees,
                t.OpenedAt,
                t.ClosedAt,
                t.Strategy.Name,
                t.Bot.Mode.ToString(),
                t.Side == OrderSide.Sell ? "Short" : "Long",
                t.CorrelationId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Signal>> GetRecentSignalsAsync(int take, CancellationToken cancellationToken = default) =>
        await _db.Signals.OrderByDescending(s => s.Timestamp).Take(take).ToListAsync(cancellationToken);

    public async Task StopAllRunningBotsAsync(string reason, CancellationToken cancellationToken = default)
    {
        var bots = await _db.Bots.Where(b => b.Status == BotStatus.Running || b.Status == BotStatus.Starting).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var bot in bots)
        {
            bot.Status = BotStatus.Stopped;
            bot.StoppedAt = now;
            bot.LastError = reason;
        }
    }

    public async Task SoftDeletePaperBotsNotInAsync(
        Guid userId,
        IReadOnlyCollection<string> keepSymbols,
        CancellationToken cancellationToken = default)
    {
        var keep = keepSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = await _db.Bots
            .Where(b => b.UserId == userId && b.Mode == TradingMode.Paper)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var bot in extras.Where(b => !keep.Contains(b.Symbol)))
        {
            bot.Status = BotStatus.Stopped;
            bot.StoppedAt = now;
            bot.DeletedAt = now;
            bot.LastError = "Removed from the market-cap universe.";
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        var prefix = keyPrefix ?? "";
        var rows = await _db.SystemSettings
            .Where(row => row.Key.StartsWith(prefix))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Key, row => row.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetSettingAsync(string key, string value, string? description, CancellationToken cancellationToken = default)
    {
        var row = await _db.SystemSettings.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (row is null)
        {
            _db.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = value,
                Description = description
            });
            return;
        }

        row.Value = value;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        if (description is not null)
        {
            row.Description = description;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);

    public async Task AddBacktestAsync(Backtest backtest, CancellationToken cancellationToken = default) =>
        await _db.Backtests.AddAsync(backtest, cancellationToken);

    private IQueryable<Bot> BotsWithGraph() =>
        _db.Bots
            .Include(b => b.StrategyVersion)
            .ThenInclude(v => v.Strategy)
            .Include(b => b.RiskProfile)
            .Include(b => b.ExchangeAccount);

    private static void CopyClosedCandle(MarketCandle target, MarketCandle source)
    {
        target.CloseTime = source.CloseTime;
        target.Open = source.Open;
        target.High = source.High;
        target.Low = source.Low;
        target.Close = source.Close;
        target.Volume = source.Volume;
        target.TradeCount = source.TradeCount;
        target.IsClosed = true;
        target.ExchangeTimestamp = source.ExchangeTimestamp;
    }
}
