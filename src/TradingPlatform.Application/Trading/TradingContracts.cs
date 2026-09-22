using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Application.Trading;

public sealed record BotDto(
    Guid Id,
    string Name,
    string Status,
    string Mode,
    string Symbol,
    string DisplayName,
    int MarketCapRank,
    string Timeframe,
    string StrategyName,
    int StrategyVersion,
    string RiskProfileName,
    string? LastError,
    DateTimeOffset? StartedAt,
    Guid StrategyId,
    Guid RiskProfileId);

public sealed record PositionDto(
    Guid Id,
    Guid BotId,
    string Symbol,
    string Side,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal CurrentPrice,
    decimal UnrealizedPnL,
    decimal RealizedPnL,
    decimal Fees,
    DateTimeOffset OpenedAt,
    string Source = "Bot",
    decimal InitialRiskUsdt = 0m,
    decimal MarginUsdt = 0m,
    decimal NotionalUsdt = 0m,
    decimal Leverage = 0m,
    decimal StopLossPercent = 0m,
    decimal TakeProfitPercent = 0m,
    decimal StopLossPrice = 0m,
    decimal TakeProfitPrice = 0m,
    decimal LiquidationPrice = 0m,
    decimal RiskPerTradePercent = 0m);

public sealed record OrderDto(
    Guid Id,
    string ClientOrderId,
    string? ExchangeOrderId,
    Guid BotId,
    string Symbol,
    string Side,
    string Type,
    decimal? Price,
    decimal Quantity,
    decimal FilledQuantity,
    string Status,
    DateTimeOffset CreatedAt,
    string Source = "Bot",
    decimal? PnL = null,
    decimal? Fee = null,
    string Mode = "Paper",
    string Kind = "Fill");

public sealed record TradeDto(
    Guid Id,
    Guid BotId,
    string Symbol,
    decimal Quantity,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal PnL,
    decimal PnLPercent,
    decimal Fees,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    string Mode = "Paper",
    string Side = "Long");

public sealed record SignalDto(
    Guid Id,
    Guid BotId,
    string Symbol,
    string SignalType,
    decimal Price,
    string Reason,
    DateTimeOffset Timestamp);

public sealed record TickerDto(string Symbol, string DisplayName, int MarketCapRank, decimal Price, DateTimeOffset Timestamp);

public sealed record MarketQuoteDto(
    string Symbol,
    string DisplayName,
    int MarketCapRank,
    decimal Price,
    decimal ChangePercent24h,
    decimal QuoteVolume,
    DateTimeOffset Timestamp,
    decimal HighPrice24h = 0,
    decimal LowPrice24h = 0,
    int Trades24h = 0,
    decimal ScanScore = 0,
    decimal SpreadBps = 0,
    decimal FundingRate = 0,
    decimal VolatilityPercent = 0,
    decimal OpenInterest = 0,
    bool Eligible = false,
    string EligibilityReason = "",
    bool Watchable = true,
    string ContractType = "PERPETUAL");

public sealed record KlineBarDto(long Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

public sealed record RiskProfileDto(
    Guid Id,
    string Name,
    decimal RiskPerTradePercent,
    decimal StopLossPercent,
    decimal TakeProfitPercent,
    decimal MaxLeverage,
    decimal MaxDailyLossPercent,
    decimal MaxPortfolioRiskPercent,
    int MaxSimultaneousPositions,
    int MaxConsecutiveLosses,
    int CooldownMinutes,
    decimal MinimumLiquidationSafetyBufferPercent,
    bool IsActive,
    bool AllowLive,
    bool IsSystem);

public sealed record SaveRiskProfileRequest(
    decimal RiskPerTradePercent,
    decimal StopLossPercent,
    decimal TakeProfitPercent,
    decimal MaxLeverage,
    decimal MaxDailyLossPercent,
    decimal MaxPortfolioRiskPercent,
    int MaxSimultaneousPositions,
    int MaxConsecutiveLosses,
    int CooldownMinutes,
    decimal MinimumLiquidationSafetyBufferPercent,
    bool AllowLive);

public sealed record RiskPreviewDto(
    string ProfileName,
    decimal AvailableBalance,
    decimal RiskPerTradePercent,
    decimal RiskAmount,
    decimal EntryPrice,
    decimal StopLossPercent,
    decimal StopLossPrice,
    decimal TakeProfitPercent,
    decimal TakeProfitPrice,
    decimal PositionNotional,
    decimal Leverage,
    decimal IsolatedMargin,
    decimal EstimatedFee,
    decimal EstimatedEntryFee,
    decimal EstimatedExitFee,
    decimal EstimatedSlippage,
    decimal EstimatedTotalRisk,
    decimal LiquidationPrice,
    decimal PortfolioRiskBefore,
    decimal PortfolioRiskAfter,
    bool Allowed,
    string Reason);

public sealed record StrategyDto(
    Guid Id,
    string Name,
    string Description,
    int Version,
    string Timeframe,
    bool AppliesToAllSymbols,
    IReadOnlyList<string> AllowedSymbols,
    string TemplateKey,
    string TemplateLabel,
    string AllowedSide,
    string Blurb,
    int EmaFast,
    int EmaSlow,
    int RsiPeriod,
    decimal RsiMinimum,
    decimal RsiLongMax,
    decimal RsiOversold,
    decimal RsiOverbought,
    int MacdFast,
    int MacdSlow,
    int MacdSignal,
    int BbPeriod,
    decimal BbStdDev,
    int DonchianLength,
    bool RequireVolume,
    int VolumeLookback,
    decimal MinAtrPercent,
    decimal MaxAtrPercent,
    bool VersionUsed,
    bool IsEnabled,
    string ValidationStatus,
    IReadOnlyList<string> SupportedTimeframes,
    IReadOnlyList<string> SupportedDirections,
    string DataDependencies = "Closed kline candles only.",
    int EntryLookback = 20,
    int ExitLookback = 10,
    int AtrPeriod = 14,
    decimal AtrStopMultiplier = 2m,
    int TrendEmaPeriod = 50,
    bool VolumeFilterEnabled = true,
    int RelativeVolumePeriod = 20,
    decimal MinimumRelativeVolume = 1m,
    decimal MaxVwapDistanceAtr = 0.75m,
    decimal StopAtrMultiplier = 1.5m,
    int VolatilityLookback = 100,
    decimal CompressionPercentile = 0.20m,
    int AtrExpansionLookback = 20,
    decimal BreakoutRelativeVolume = 1.2m,
    int SupertrendPeriod = 10,
    decimal SupertrendMultiplier = 3m,
    int AdxPeriod = 14,
    decimal MinimumAdx = 20m,
    string Family = "TREND");

public sealed record SaveStrategyRequest(
    string Name,
    string Description,
    string Timeframe,
    bool AppliesToAllSymbols,
    string[]? Symbols,
    string TemplateKey = "ema_rsi_trend",
    string AllowedSide = "Long",
    int EmaFast = 20,
    int EmaSlow = 50,
    int RsiPeriod = 14,
    decimal RsiMinimum = 50m,
    decimal RsiLongMax = 68m,
    decimal RsiOversold = 30m,
    decimal RsiOverbought = 70m,
    int MacdFast = 12,
    int MacdSlow = 26,
    int MacdSignal = 9,
    int BbPeriod = 20,
    decimal BbStdDev = 2m,
    int DonchianLength = 20,
    bool RequireVolume = true,
    int VolumeLookback = 20,
    decimal MinAtrPercent = 0.15m,
    decimal MaxAtrPercent = 4m,
    int EntryLookback = 20,
    int ExitLookback = 10,
    int AtrPeriod = 14,
    decimal AtrStopMultiplier = 2m,
    int TrendEmaPeriod = 50,
    bool VolumeFilterEnabled = true,
    int RelativeVolumePeriod = 20,
    decimal MinimumRelativeVolume = 1m,
    decimal MaxVwapDistanceAtr = 0.75m,
    decimal StopAtrMultiplier = 1.5m,
    int VolatilityLookback = 100,
    decimal CompressionPercentile = 0.20m,
    int AtrExpansionLookback = 20,
    decimal BreakoutRelativeVolume = 1.2m,
    int SupertrendPeriod = 10,
    decimal SupertrendMultiplier = 3m,
    int AdxPeriod = 14,
    decimal MinimumAdx = 20m);

public sealed record SetStrategyEnabledRequest(bool Enabled);

public sealed record StrategyPreviewBarDto(
    DateTimeOffset Time,
    string Signal,
    decimal Close,
    string Reason);

public sealed record StrategyPreviewDto(
    Guid StrategyId,
    string Name,
    string TemplateKey,
    string Symbol,
    string Timeframe,
    string LastSignal,
    string LastReason,
    IReadOnlyList<StrategyPreviewBarDto> Bars);

public sealed record SaveSymbolScopeRequest(bool AppliesToAllSymbols, string[]? Symbols);

public sealed record PortfolioDto(
    decimal PortfolioValue,
    decimal AvailableBalance,
    decimal UnrealizedPnL,
    decimal RealizedPnL,
    decimal TodaysPnL,
    bool LiveTradingEnabled,
    string Health,
    TickerDto? Ticker,
    IReadOnlyList<TickerDto> Tickers,
    IReadOnlyList<BotDto> Bots,
    IReadOnlyList<PositionDto> Positions,
    IReadOnlyList<OrderDto> Orders,
    IReadOnlyList<TradeDto> Trades,
    IReadOnlyList<SignalDto> Signals,
    bool LiveHasKeys = false,
    bool LiveCanTrade = false,
    decimal LiveEquity = 0,
    decimal LiveAvailable = 0,
    decimal LiveUnrealizedPnL = 0,
    decimal LiveTodaysPnL = 0,
    decimal LiveSpotUsdt = 0,
    decimal LiveFundingUsdt = 0,
    decimal LiveFuturesUsdt = 0,
    string? LiveMessage = null,
    IReadOnlyList<PositionDto>? PositionBooks = null);

public sealed record PerformanceTradeRow(
    Guid Id,
    Guid BotId,
    string Symbol,
    decimal Quantity,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal PnL,
    decimal PnLPercent,
    decimal Fees,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    string StrategyName,
    string Mode,
    string Side = "Long");

public sealed record PerformanceDayDto(string Date, decimal PnL, decimal Cumulative);

public sealed record PerformanceSliceDto(
    string Name,
    int Closed,
    int Wins,
    decimal Realized,
    decimal WinRate,
    decimal Expectancy,
    int Bots = 0);

public sealed record PerformanceDto(
    string Mode,
    int ClosedTrades,
    int OpenTrades,
    int OpenPositions,
    int Wins,
    int Losses,
    int WeekClosed,
    decimal RealizedPnL,
    decimal UnrealizedPnL,
    decimal TodaysPnL,
    decimal FeesPaid,
    decimal WinRate,
    decimal Expectancy,
    decimal ProfitFactor,
    decimal AverageWin,
    decimal AverageLoss,
    decimal Best,
    decimal Worst,
    decimal MaxDrawdown,
    decimal ReturnPercent,
    double? AverageHoldHours,
    int WinStreak,
    int LossStreak,
    IReadOnlyList<PerformanceDayDto> Days,
    IReadOnlyList<PerformanceSliceDto> Strategies,
    IReadOnlyList<PerformanceSliceDto> Coins,
    IReadOnlyList<TradeDto> RecentTrades,
    decimal MonthlyPnL = 0m);

public interface ITradingQueryService
{
    Task<PortfolioDto> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<PerformanceDto> GetPerformanceAsync(string mode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BotDto>> GetBotsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PositionDto>> GetPositionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradeDto>> GetTradesAsync(CancellationToken cancellationToken = default);
    Task<RiskProfileDto> GetRiskProfileAsync(string? mode = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StrategyDto>> GetStrategiesAsync(string? mode = null, CancellationToken cancellationToken = default);
    Task<StrategyPreviewDto> PreviewStrategyAsync(Guid strategyId, string? symbol, int? limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RiskProfileDto>> GetRiskProfilesAsync(string? mode = null, CancellationToken cancellationToken = default);
    Task<StrategyDto> UpdateStrategyScopeAsync(Guid strategyId, bool appliesToAll, IEnumerable<string>? symbols, CancellationToken cancellationToken = default);
    Task<RiskProfileDto> UpdateRiskScopeAsync(Guid riskProfileId, bool appliesToAll, IEnumerable<string>? symbols, CancellationToken cancellationToken = default);
    Task<StrategyDto> CreateStrategyAsync(Guid userId, SaveStrategyRequest request, string? mode = null, CancellationToken cancellationToken = default);
    Task<StrategyDto> UpdateStrategyAsync(Guid strategyId, SaveStrategyRequest request, CancellationToken cancellationToken = default);
    Task<StrategyDto> SetStrategyEnabledAsync(Guid strategyId, bool enabled, CancellationToken cancellationToken = default);
    Task<RiskProfileDto> CreateRiskProfileAsync(SaveRiskProfileRequest request, CancellationToken cancellationToken = default);
    Task<RiskProfileDto> UpdateRiskProfileAsync(Guid riskProfileId, SaveRiskProfileRequest request, CancellationToken cancellationToken = default);
    Task<RiskProfileDto> ActivateRiskProfileAsync(Guid riskProfileId, CancellationToken cancellationToken = default);
    Task<RiskPreviewDto> PreviewRiskAsync(string mode, decimal price, CancellationToken cancellationToken = default);
}

public sealed record StartSymbolRequest(string Symbol, string Mode, Guid? StrategyId, Guid? RiskProfileId);

public sealed record CreateBotsRequest(string Mode, Guid StrategyId, Guid RiskProfileId, string[] Symbols);

public sealed record CreateBotsResult(int Created, int Skipped);

public sealed record StartBotsRequest(string Mode, Guid? PreferredStrategyId = null);

public sealed record StartBotsResult(int Started, int Failed, string? Detail);

public sealed record StopBotsResult(int Stopped, int Failed, string? Detail);

public sealed record DeleteBotsRequest(string Mode, Guid[] Ids);

public sealed record DeleteBotsResult(int Deleted, int Skipped);

public sealed record RunBacktestRequest(
    Guid? StrategyId,
    string Symbol,
    string Timeframe,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal InitialCapital,
    decimal RiskPercent,
    decimal Leverage,
    decimal FeesPercent,
    decimal SlippagePercent);

public sealed record BacktestEquityPointDto(long Time, decimal Equity);

public sealed record BacktestTradeDto(
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    decimal Quantity,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal PnL,
    decimal Fees,
    string Reason,
    string Side = "Long");

public sealed record BacktestResultDto(
    Guid Id,
    string Status,
    string Symbol,
    string Timeframe,
    string StrategyName,
    int StrategyVersion,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal InitialBalance,
    decimal FinalBalance,
    decimal NetProfit,
    decimal ReturnPercent,
    int NumberOfTrades,
    decimal WinRate,
    decimal ProfitFactor,
    decimal AverageWin,
    decimal AverageLoss,
    decimal MaximumDrawdown,
    decimal? SharpeRatio,
    decimal FeesPaid,
    decimal LargestWinningTrade,
    decimal LargestLosingTrade,
    int BarsUsed,
    string Assumptions,
    IReadOnlyList<BacktestEquityPointDto> Equity,
    IReadOnlyList<BacktestTradeDto> Trades);

public interface IBacktestService
{
    Task<BacktestResultDto> RunAsync(Guid userId, RunBacktestRequest request, CancellationToken cancellationToken = default);
}

public sealed record SaveExchangeCredentialsRequest(string ApiKey, string ApiSecret);

public sealed record ExchangeConnectionDto(
    bool HasKeys,
    bool LiveReady,
    bool CanTrade,
    string? ApiKeyHint,
    decimal? UsdtFree,
    string? Message,
    decimal SpotUsdt = 0,
    decimal FundingUsdt = 0,
    decimal FuturesUsdt = 0,
    decimal TotalEquity = 0);

public interface IBotLifecycleService
{
    Task<BotDto> StartSamplePaperBotAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BotDto>> StartTopVolumePaperBotsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<BotDto> StartSymbolAsync(Guid userId, string symbol, TradingMode mode, Guid? strategyId = null, Guid? riskProfileId = null, CancellationToken cancellationToken = default);
    Task<CreateBotsResult> CreateSymbolBotsAsync(Guid userId, TradingMode mode, Guid strategyId, Guid riskProfileId, IReadOnlyList<string> symbols, CancellationToken cancellationToken = default);
    Task<StartBotsResult> StartAllIdleAsync(Guid userId, TradingMode mode, Guid? preferredStrategyId = null, CancellationToken cancellationToken = default);
    Task<StopBotsResult> StopAllRunningAsync(Guid userId, TradingMode mode, CancellationToken cancellationToken = default);
    Task<BotDto> StartAsync(Guid botId, CancellationToken cancellationToken = default);
    Task<BotDto> StopAsync(Guid botId, CancellationToken cancellationToken = default);
    Task<DeleteBotsResult> DeleteBotsAsync(IReadOnlyList<Guid> ids, TradingMode? requiredMode, CancellationToken cancellationToken = default);
    Task EmergencyStopAsync(CancellationToken cancellationToken = default);
}

public interface IExchangeAccountService
{
    Task<ExchangeConnectionDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ExchangeConnectionDto> SaveLiveKeysAsync(Guid userId, string apiKey, string apiSecret, CancellationToken cancellationToken = default);
}

public interface IBotEngine
{
    Task EvaluateRunningBotsAsync(CancellationToken cancellationToken = default);
    Task ClosePositionAsync(Guid positionId, CancellationToken cancellationToken = default);
}
