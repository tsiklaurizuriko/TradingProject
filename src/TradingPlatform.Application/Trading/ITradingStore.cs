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

namespace TradingPlatform.Application.Trading;

public sealed record StrategyEntryClaim(string Symbol, Guid BotId, bool Filled, DateTimeOffset CreatedAt);

public sealed record LiveBotSlot(
    Guid BotId,
    string Symbol,
    Guid StrategyId,
    int MaxSimultaneousPositions,
    DateTimeOffset? StartedAt);

public interface ITradingStore
{
    Task<User> GetFirstAdminAsync(CancellationToken cancellationToken = default);
    Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Bot>> GetRunningBotsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Bot>> GetRunningLiveBotsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LiveBotSlot>> GetRunningLiveSlotsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StrategyEntryClaim>> GetStrategyEntryClaimsAsync(
        IReadOnlyCollection<string> openSymbols,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Bot>> ListBotsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Bot>> ListWorkspaceBotsAsync(Guid userId, TradingMode mode, CancellationToken cancellationToken = default);
    Task<Bot?> GetBotAsync(Guid botId, CancellationToken cancellationToken = default);
    Task<Bot?> FindPaperBotBySymbolAsync(Guid userId, string symbol, CancellationToken cancellationToken = default);
    Task<Bot?> FindBotBySymbolAsync(Guid userId, string symbol, TradingMode mode, Guid? strategyId, Guid? riskProfileId, CancellationToken cancellationToken = default);
    Task AddBotAsync(Bot bot, CancellationToken cancellationToken = default);
    Task AddBotRunAsync(BotRun run, CancellationToken cancellationToken = default);
    Task<StrategyVersion> GetSampleStrategyVersionAsync(CancellationToken cancellationToken = default);
    Task<StrategyVersion?> GetLatestStrategyVersionAsync(Guid strategyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Strategy>> ListStrategiesAsync(CancellationToken cancellationToken = default);
    Task<Strategy?> GetStrategyAsync(Guid strategyId, CancellationToken cancellationToken = default);
    Task AddStrategyAsync(Strategy strategy, CancellationToken cancellationToken = default);
    Task<RiskProfile> GetConservativeRiskAsync(CancellationToken cancellationToken = default);

    /// <summary>The news-only book. It is never the active book used by other strategies.</summary>
    Task<RiskProfile> GetNewsRiskAsync(CancellationToken cancellationToken = default);
    Task<RiskProfile?> GetRiskProfileByIdAsync(Guid riskProfileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RiskProfile>> ListRiskProfilesAsync(CancellationToken cancellationToken = default);
    Task AddRiskProfileAsync(RiskProfile risk, CancellationToken cancellationToken = default);
    Task<ExchangeAccount> GetOrCreatePaperAccountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Symbol> UpsertSymbolAsync(
        string name,
        string baseAsset,
        string quoteAsset,
        decimal tickSize,
        decimal stepSize,
        decimal minQuantity,
        decimal minNotional,
        int pricePrecision,
        int quantityPrecision,
        CancellationToken cancellationToken = default);
    Task<Symbol?> GetSymbolAsync(string name, CancellationToken cancellationToken = default);
    Task UpsertClosedCandleAsync(Guid symbolId, Timeframe timeframe, MarketCandle candle, CancellationToken cancellationToken = default);
    Task<Position?> GetOpenPositionAsync(Guid botId, string symbol, CancellationToken cancellationToken = default);
    Task<Position?> GetOpenPositionByIdAsync(Guid positionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Position>> GetOpenPositionsForModeAsync(TradingMode mode, CancellationToken cancellationToken = default);
    Task<int> CountOpenPositionsAsync(Guid botId, CancellationToken cancellationToken = default);
    Task<int> CountOrdersSinceForModeAsync(TradingMode mode, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
    Task<decimal> SumClosedPnLSinceForModeAsync(TradingMode mode, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Appends an account equity point, at most one per <paramref name="minGap"/>. Saved by the caller.</summary>
    Task RecordEquityPointAsync(Guid exchangeAccountId, TradingMode mode, decimal equity, DateTimeOffset at, TimeSpan minGap, CancellationToken cancellationToken = default);

    /// <summary>Highest recorded equity since <paramref name="sinceUtc"/> and the newest point's time. Nulls when nothing is recorded.</summary>
    Task<(decimal? Peak, DateTimeOffset? LastAt)> GetEquityPeakAsync(Guid exchangeAccountId, TradingMode mode, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
    Task StopRunningBotsForModeAsync(TradingMode mode, string reason, CancellationToken cancellationToken = default);
    Task AddPositionAsync(Position position, CancellationToken cancellationToken = default);
    Task<bool> HasClientOrderAsync(string clientOrderId, CancellationToken cancellationToken = default);
    Task<bool> HasKnownOrderAsync(string? clientOrderId, string? exchangeOrderId, CancellationToken cancellationToken = default);
    Task<Order?> GetOrderByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default);
    Task<bool> HasUnresolvedEntryAsync(Guid botId, string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetUnresolvedLiveOrdersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetRestingLiveProtectionAsync(CancellationToken cancellationToken = default);
    Task AddPositionEventAsync(PositionEvent positionEvent, CancellationToken cancellationToken = default);
    Task<int> CountOrdersSinceAsync(Guid botId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
    Task AddOrderAsync(Order order, CancellationToken cancellationToken = default);
    Task AddExecutionAsync(Execution execution, CancellationToken cancellationToken = default);
    Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default);
    Task AddTradeAsync(Trade trade, CancellationToken cancellationToken = default);
    Task<bool> HasTradeCorrelationAsync(string correlationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trade>> FindClosedTradesAroundAsync(
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
    void RemoveTrade(Trade trade);
    Task<Trade?> FindClosedTradeNearAsync(
        string symbol,
        decimal quantity,
        DateTimeOffset openedAt,
        DateTimeOffset around,
        TimeSpan window,
        CancellationToken cancellationToken = default);
    Task<Trade?> GetOpenTradeAsync(Guid botId, CancellationToken cancellationToken = default);
    Task<decimal> SumClosedPnLSinceAsync(Guid botId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
    Task<(int ConsecutiveLosses, DateTimeOffset? LastLossAt)> GetLossStreakAsync(Guid botId, CancellationToken cancellationToken = default);
    Task<(int ConsecutiveLosses, DateTimeOffset? LastLossAt)> GetLossStreakForModeAsync(TradingMode mode, CancellationToken cancellationToken = default);
    Task<(int ConsecutiveLosses, DateTimeOffset? LastLossAt)> GetSymbolLossStreakAsync(TradingMode mode, string symbol, CancellationToken cancellationToken = default);
    Task<int> CollapseDuplicateClosedTripsAsync(CancellationToken cancellationToken = default);
    Task<Balance> GetOrCreateBalanceAsync(
        Guid exchangeAccountId,
        Guid? botId,
        string asset,
        TradingMode mode,
        decimal initialFree,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Balance>> GetPaperBalancesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetRecentOrdersAsync(int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trade>> GetRecentTradesAsync(int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PerformanceTradeRow>> GetPerformanceTradesAsync(TradingMode mode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Signal>> GetRecentSignalsAsync(int take, CancellationToken cancellationToken = default);
    Task StopAllRunningBotsAsync(string reason, CancellationToken cancellationToken = default);
    Task SoftDeletePaperBotsNotInAsync(Guid userId, IReadOnlyCollection<string> keepSymbols, CancellationToken cancellationToken = default);
    Task AddBacktestAsync(Backtest backtest, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(string keyPrefix, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string value, string? description, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
