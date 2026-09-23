namespace TradingPlatform.Application.Trading;

public sealed record PriceActionSequenceDto(
    string Coin,
    string Timeframe,
    string Name,
    int Occurrences,
    decimal MeanFwd1,
    decimal MeanFwd3,
    decimal MeanFwd5,
    decimal MedianMfe,
    decimal MedianMae,
    decimal HitPos50,
    decimal HitNeg50);

public sealed record PriceActionPatternStatDto(
    string Coin,
    string Timeframe,
    string PatternType,
    string Status,
    int Occurrences,
    decimal MeanFwd3,
    decimal MedianMfe,
    decimal MedianMae);

public sealed record PriceActionOccurrenceDto(
    string Coin,
    string Timeframe,
    string PatternType,
    string Version,
    DateTimeOffset? Start,
    DateTimeOffset? Detection,
    DateTimeOffset? Confirmation,
    DateTimeOffset? Entry,
    decimal? Neckline,
    decimal? Level,
    string Direction,
    string Status,
    IReadOnlyList<PriceActionPointDto> Points,
    IReadOnlyList<PriceActionBarDto> Bars);

public sealed record PriceActionPointDto(string Role, decimal Price, DateTimeOffset? Time);

public sealed record PriceActionBarDto(long Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

public sealed record PriceActionDataCoverageDto(
    string Coin,
    string Timeframe,
    DateTimeOffset RequestedFrom,
    DateTimeOffset RequestedTo,
    DateTimeOffset? ActualFirstBar,
    DateTimeOffset? ActualLastBar,
    int BarCount,
    int ExpectedBarCount,
    int GapCount,
    long MissingBarCount,
    int DuplicateCount,
    decimal CoveragePercent,
    int DownloadedPages,
    int CacheHits,
    int CacheMisses,
    bool Continuous,
    bool QualityPassed,
    string Status);

public sealed record PriceActionResearchSummaryDto(
    string Confirmation,
    bool LiveOff,
    bool ScalpingLiveOff,
    bool PriceActionLiveOff,
    bool IsolatedEnforced,
    bool RiskEngineAuthoritative,
    bool ValidatedForPaperAssigned,
    string CupAndHandle,
    string? LastRunId,
    IReadOnlyList<ScalpingStrategyStatusDto> Strategies,
    IReadOnlyList<ScalpingCoverageDto> Coverage,
    IReadOnlyList<ScalpingBookDto> Books,
    IReadOnlyList<PriceActionSequenceDto> Sequences,
    IReadOnlyList<PriceActionPatternStatDto> Patterns,
    IReadOnlyList<PriceActionOccurrenceDto> Occurrences,
    IReadOnlyList<string> Hypotheses,
    IReadOnlyList<PriceActionDataCoverageDto> DataExpansion,
    IReadOnlyList<ScalpingRejectDto> OccupancyRejects,
    int SameCoinRejects,
    int SlotRejects,
    int HeatRejects);

public sealed record ContextualPriceActionSummaryDto(
    string Confirmation,
    bool LiveOff,
    bool ScalpingLiveOff,
    bool PriceActionLiveOff,
    bool PaperPromotionOff,
    bool ValidatedForPaperAssigned,
    int HypothesisCount,
    int Families,
    int Survivors,
    IReadOnlyList<string> Hypotheses,
    string FuturesData);

public interface IPriceActionResearchQuery
{
    Task<PriceActionResearchSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceActionOccurrenceDto>> GetOccurrencesAsync(CancellationToken cancellationToken = default);
    Task<ContextualPriceActionSummaryDto> GetContextualSummaryAsync(CancellationToken cancellationToken = default);
}
