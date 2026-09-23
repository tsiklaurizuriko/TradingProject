namespace TradingPlatform.Application.Trading;

public sealed record ScalpingCoverageDto(
    string Coin,
    string Timeframe,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string Source,
    int Gaps,
    int Bars,
    double TakerCoverage,
    string Status,
    string Notes);

public sealed record ScalpingStrategyStatusDto(
    string TemplateKey,
    string Name,
    string Family,
    string Status,
    string Blurb,
    bool OperatorCatalog,
    bool Enabled);

public sealed record ScalpingBookDto(
    string CandidateId,
    string Coin,
    string Timeframe,
    string Phase,
    string CostLabel,
    string Status,
    int TradeCount,
    decimal? ProfitFactor,
    decimal? MedianHoldingMinutes,
    decimal? P25HoldingMinutes,
    decimal? P75HoldingMinutes,
    decimal NetPnl);

public sealed record ScalpingRejectDto(
    DateTimeOffset Time,
    string StrategyKey,
    string Coin,
    string Reason);

public sealed record ScalpingResearchSummaryDto(
    string Confirmation,
    bool LiveOff,
    bool ScalpingLiveOff,
    bool IsolatedEnforced,
    bool RiskEngineAuthoritative,
    bool ValidatedForPaperAssigned,
    string? LastRunId,
    IReadOnlyList<ScalpingStrategyStatusDto> Strategies,
    IReadOnlyList<ScalpingCoverageDto> Coverage,
    IReadOnlyList<ScalpingBookDto> Books,
    IReadOnlyList<ScalpingRejectDto> OccupancyRejects,
    int SameCoinRejects,
    int SlotRejects,
    int HeatRejects);

public sealed record ScalpingResearchRunDto(
    string Id,
    ScalpingResearchSummaryDto Summary);

public interface IScalpingResearchQuery
{
    Task<ScalpingResearchSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScalpingCoverageDto>> GetCoverageAsync(CancellationToken cancellationToken = default);
    Task<ScalpingResearchRunDto?> GetRunAsync(string id, CancellationToken cancellationToken = default);
}
