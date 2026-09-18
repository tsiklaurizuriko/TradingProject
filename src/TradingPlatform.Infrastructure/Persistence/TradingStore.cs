using Microsoft.EntityFrameworkCore;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Backtesting;
using TradingPlatform.Domain.Balances;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Signals;
using TradingPlatform.Domain.Strategies;
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

    public async Task<IReadOnlyList<Bot>> GetRunningBotsAsync(CancellationToken cancellationToken = default) =>
        await BotsWithGraph().Where(b => b.Status == BotStatus.Running).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Bot>> ListBotsAsync(CancellationToken cancellationToken = default) =>
        await BotsWithGraph().ToListAsync(cancellationToken);

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
        await _db.Strategies.Include(s => s.Versions).OrderBy(s => s.Name).ToListAsync(cancellationToken);

    public Task<Strategy?> GetStrategyAsync(Guid strategyId, CancellationToken cancellationToken = default) =>
        _db.Strategies.Include(s => s.Versions).FirstOrDefaultAsync(s => s.Id == strategyId, cancellationToken);

    public async Task AddStrategyAsync(Strategy strategy, CancellationToken cancellationToken = default) =>
        await _db.Strategies.AddAsync(strategy, cancellationToken);

    public async Task<RiskProfile> GetConservativeRiskAsync(CancellationToken cancellationToken = default)
    {
        var books = await _db.RiskProfiles.OrderBy(r => r.RiskPerTradePercent).ToListAsync(cancellationToken);
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
        var existing = await _db.MarketCandles.FirstOrDefaultAsync(
            c => c.SymbolId == symbolId && c.Timeframe == timeframe && c.OpenTime == candle.OpenTime,
            cancellationToken);
        if (existing is null)
        {
            await _db.MarketCandles.AddAsync(new MarketCandle
            {
                SymbolId = symbolId,
                Timeframe = timeframe,
                OpenTime = candle.OpenTime,
                CloseTime = candle.CloseTime,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Close = candle.Close,
                Volume = candle.Volume,
                TradeCount = candle.TradeCount,
                IsClosed = true,
                ExchangeTimestamp = candle.ExchangeTimestamp
            }, cancellationToken);
            return;
        }

        existing.High = candle.High;
        existing.Low = candle.Low;
        existing.Close = candle.Close;
        existing.Volume = candle.Volume;
        existing.TradeCount = candle.TradeCount;
        existing.IsClosed = true;
        existing.ExchangeTimestamp = candle.ExchangeTimestamp;
    }

    public Task<Position?> GetOpenPositionAsync(Guid botId, string symbol, CancellationToken cancellationToken = default) =>
        _db.Positions.FirstOrDefaultAsync(
            p => p.BotId == botId && p.Symbol == symbol && p.ClosedAt == null && p.Quantity > 0m,
            cancellationToken);

    public Task<Position?> GetOpenPositionByIdAsync(Guid positionId, CancellationToken cancellationToken = default) =>
        _db.Positions.FirstOrDefaultAsync(
            p => p.Id == positionId && p.ClosedAt == null && p.Quantity > 0m,
            cancellationToken);

    public async Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) =>
        await _db.Positions
            .Where(p => p.ClosedAt == null && p.Quantity > 0m)
            .OrderByDescending(p => p.OpenedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Position>> GetOpenPositionsForModeAsync(TradingMode mode, CancellationToken cancellationToken = default) =>
        await _db.Positions
            .Include(p => p.Bot)
            .ThenInclude(b => b.StrategyVersion)
            .Where(p => p.ClosedAt == null && p.Quantity > 0m && p.Bot.Mode == mode)
            .OrderByDescending(p => p.OpenedAt)
            .ToListAsync(cancellationToken);

    public Task<int> CountOpenPositionsAsync(Guid botId, CancellationToken cancellationToken = default) =>
        _db.Positions.CountAsync(p => p.BotId == botId && p.ClosedAt == null && p.Quantity > 0m, cancellationToken);

    public Task<int> CountOrdersSinceForModeAsync(TradingMode mode, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        _db.Orders.CountAsync(o => o.Mode == mode && o.CreatedAt >= sinceUtc, cancellationToken);

    public async Task<decimal> SumClosedPnLSinceForModeAsync(TradingMode mode, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        await _db.Trades
            .Where(t => t.ClosedAt != null && t.ClosedAt >= sinceUtc && t.Bot.Mode == mode)
            .SumAsync(t => (decimal?)t.PnL, cancellationToken) ?? 0m;

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

    public Task<Trade?> GetOpenTradeAsync(Guid botId, CancellationToken cancellationToken = default) =>
        _db.Trades.FirstOrDefaultAsync(t => t.BotId == botId && t.ClosedAt == null, cancellationToken);

    public async Task<decimal> SumClosedPnLSinceAsync(Guid botId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
    {
        return await _db.Trades
            .Where(t => t.BotId == botId && t.ClosedAt != null && t.ClosedAt >= sinceUtc)
            .SumAsync(t => (decimal?)t.PnL, cancellationToken) ?? 0m;
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
        var recent = await _db.Trades
            .Where(t => t.Bot.Mode == mode && t.ClosedAt != null)
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
            .Include(o => o.Executions)
            .OrderByDescending(o => o.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Trade>> GetRecentTradesAsync(int take, CancellationToken cancellationToken = default) =>
        await _db.Trades
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
                t.Bot.Mode.ToString()))
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

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);

    public async Task AddBacktestAsync(Backtest backtest, CancellationToken cancellationToken = default) =>
        await _db.Backtests.AddAsync(backtest, cancellationToken);

    private IQueryable<Bot> BotsWithGraph() =>
        _db.Bots
            .Include(b => b.StrategyVersion)
            .ThenInclude(v => v.Strategy)
            .Include(b => b.RiskProfile)
            .Include(b => b.ExchangeAccount);
}
