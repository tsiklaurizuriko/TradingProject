using System.Globalization;
using TradingPlatform.Backtesting;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Research;

public static class ResearchStatuses
{
    public const string Researching = "RESEARCHING";
    public const string ResearchComplete = "RESEARCH_COMPLETE";
    public const string InsufficientData = "INSUFFICIENT_DATA";
    public const string ImplementationError = "IMPLEMENTATION_ERROR";
    public const string IsPromising = "IS_PROMISING";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string OosFailed = "OOS_FAILED";
    public const string CostFragile = "COST_FRAGILE";
    public const string ParameterFragile = "PARAMETER_FRAGILE";
    public const string SymbolFragile = "SYMBOL_FRAGILE";
    public const string TimeframeFragile = "TIMEFRAME_FRAGILE";
    public const string RegimeFragile = "REGIME_FRAGILE";
    public const string RobustnessInsufficient = "ROBUSTNESS_INSUFFICIENT";
    public const string ValidatedForPaper = "VALIDATED_FOR_PAPER";
    public const string Rejected = "REJECTED";
    public const string SkippedTimeframe = "SKIPPED_TIMEFRAME";
    public const string DataUnavailable = "DATA_UNAVAILABLE";
    public const string NoTrades = "NO_TRADES";
    public const string HistoricallyFittedCandidate = "HISTORICALLY_FITTED_CANDIDATE";
}

public static class ResearchPhases
{
    public const string Pilot = "1";
    public const string Two = "2";
    public const string Is = "IS";
    public const string Validation = "VALIDATION";
    public const string Freeze = "FREEZE";
    public const string Oos = "OOS";
    public const string WalkForward = "WALK_FORWARD";
}

public static class ResearchKinds
{
    public const string ParentFilter = "parent_filter";
    public const string Native = "native";
}

public sealed record ResearchFilters(
    bool RequireEmaAlignment = false,
    bool RequireEmaSlope = false,
    bool RequirePriceVsSlowEma = false,
    int? MinAdx = null,
    bool RequireAtrExpansion = false,
    decimal? MinAtrPercentile = null,
    decimal? MaxAtrPercentile = null,
    decimal? MinRelativeVolume = null,
    string? HigherTimeframe = null,
    string? ConfirmationTimeframe = null,
    string? ContextTimeframe = null,
    int AdxPeriod = 14,
    int AtrPeriod = 14,
    int AtrPercentileLookback = 50,
    int RelativeVolumeLookback = 20,
    int EmaFast = 20,
    int EmaSlow = 50);

public sealed record ResearchNativeParams(
    int SupertrendPeriod = 10,
    decimal SupertrendMultiplier = 3m,
    int DonchianLength = 20,
    int RsiPeriod = 14,
    decimal RsiLongRecover = 40m,
    decimal RsiShortRecover = 60m);

public sealed record ResearchCandidate(
    string CandidateId,
    string ParentStrategyId,
    int CandidateVersion,
    string Hypothesis,
    string Kind,
    string? ParentTemplateKey,
    string NativeKey,
    IReadOnlyList<string> Indicators,
    string EntryRules,
    string ExitRules,
    ResearchFilters Filters,
    ResearchNativeParams Native,
    IReadOnlyList<string> SupportedTimeframes,
    IReadOnlyList<string> SupportedDirections,
    IReadOnlyList<int> ParameterGridDonchian,
    DateTimeOffset CreatedAtUtc,
    string DatasetScope,
    string Status,
    string CodeVersion = ResearchCandidate.EngineVersion,
    string FrozenSymbol = "",
    decimal StopLossPercent = 0m,
    decimal TakeProfitPercent = 0m,
    int MaxHoldBars = 0)
{
    public const string EngineVersion = "research-layer-1";
}

public sealed record ResearchBookKey(string CandidateId, string Symbol, string Timeframe, string Phase, string CostLabel);

public sealed record ResearchBookResult(
    string CandidateId,
    string Symbol,
    string Timeframe,
    string Phase,
    string CostLabel,
    string Status,
    int Bars,
    int TradeCount,
    decimal NetPnl,
    decimal Fees,
    decimal WinRate,
    decimal Expectancy,
    string ProfitFactorState,
    decimal? ProfitFactor,
    decimal PositivePnlSum,
    decimal AbsoluteNegativePnlSum,
    int WinningTrades,
    int LosingTrades,
    int ZeroPnlTrades,
    PnlTotals? LongTotals,
    PnlTotals? ShortTotals,
    PnlTotals? CombinedTotals,
    decimal? MeanHoldingMinutes,
    decimal? MedianHoldingMinutes,
    decimal? MaxHoldingMinutes,
    decimal? MfeMean,
    decimal? MaeMean,
    decimal? Return1,
    decimal? Return3,
    decimal? Return5,
    decimal? Return10,
    string Regime,
    IReadOnlyList<string> Notes,
    decimal? P25HoldingMinutes = null,
    decimal? P75HoldingMinutes = null);

public sealed record ResearchRobustness(
    string CandidateId,
    int SymbolsTested,
    int SymbolsPfAboveOne,
    int SymbolsPfBelowOne,
    decimal? MedianSymbolPf,
    decimal? MeanSymbolPf,
    decimal? WorstDecilePf,
    decimal? BestDecilePf,
    decimal TopTwoSymbolNetShare,
    IReadOnlyDictionary<string, string> TimeframePf,
    bool CostFragile,
    string Status,
    IReadOnlyList<string> Notes);

public static class ResearchCostLabels
{
    public const string Base = "BASE";
    public const string Mild = "MILD";
    public const string High = "HIGH";
    public const string Stress = "STRESS";

    public static decimal Multiplier(string label) => label switch
    {
        Mild => 1.25m,
        High => 1.5m,
        Stress => 2.0m,
        _ => 1.0m
    };
}

public static class ResearchPf
{
    public static (string State, decimal? Ratio) From(PnlTotals? totals)
    {
        if (totals is null || totals.Trades <= 0)
        {
            return ("NO_TRADES", null);
        }

        var pf = totals.ProfitFactor;
        return pf.Kind switch
        {
            ProfitFactorKind.NoTrades => ("NO_TRADES", null),
            ProfitFactorKind.NoLosses => ("NO_LOSSES", null),
            ProfitFactorKind.NoWins => ("NORMAL", 0m),
            ProfitFactorKind.Finite => ("NORMAL", pf.Ratio),
            _ => ("INSUFFICIENT", null)
        };
    }

    public static string Render(string state, decimal? ratio) => state switch
    {
        "NO_TRADES" => "N/A",
        "NO_LOSSES" => "Infinity / NoLosses",
        _ when ratio is { } value => value.ToString("0.00000000", CultureInfo.InvariantCulture),
        _ => "N/A"
    };
}
