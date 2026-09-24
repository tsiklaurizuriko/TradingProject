namespace TradingPlatform.Application.Trading;

public sealed record CrossSectionalReversalVariantDto(
    string Key,
    string Name,
    string Feature,
    string Status,
    bool Enabled);

public sealed record CrossSectionalReversalStatusDto(
    string StrategyKey,
    string Family,
    IReadOnlyList<CrossSectionalReversalVariantDto> Variants,
    string Status,
    bool Enabled,
    string Universe,
    int MinimumUniverse,
    int HistoryBarsRequired,
    string RankingClock,
    int TopDecilePercent,
    int BottomDecilePercent,
    string ValidationStatus,
    string PaperStatus,
    string LiveStatus,
    string Notice,
    string ManifestSha256,
    string RankingVersion,
    string ProductionApproval,
    string LiveApproved,
    bool GlobalLiveTradingEnabled,
    int MaxLongPositions,
    int MaxShortPositions,
    int MaxTotalPositions,
    decimal MaxCrossSectionalRiskPercent,
    decimal MaxPerPositionRiskPercent,
    int MaxLeverage);
