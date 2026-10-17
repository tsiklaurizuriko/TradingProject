namespace TradingPlatform.Backtesting;

/// <summary>
/// Strategy-level gates. A single-series PASS is not VALIDATED.
/// These thresholds were fixed before the multi-symbol research run and are not fitted on the holdout.
/// </summary>
public static class ProductionNewVerdict
{
    public const string Validated = "VALIDATED";
    public const string Rejected = "REJECTED";
    public const string InsufficientEvidence = "INSUFFICIENT_EVIDENCE";
    public const string DataBlocked = "DATA_BLOCKED";
    public const string ImplementationBlocked = "IMPLEMENTATION_BLOCKED";

    public const int MinimumSymbols = 8;
    public const int MinimumOutOfSampleTrades = 100;
    public const int MinimumSideTrades = 30;
    public const int MinimumWalkForwardWindows = 4;
    public const decimal MinimumProfitFactor = 1.15m;
    public const decimal MaximumDrawdownPercent = 25m;

    public static string Judge(ProductionNewEvidence evidence)
    {
        if (!evidence.RequiredDataPresent)
        {
            return DataBlocked;
        }

        if (evidence.SymbolsWithOutOfSampleTrades < MinimumSymbols
            || evidence.OutOfSampleTrades < MinimumOutOfSampleTrades
            || evidence.OutOfSampleLong < MinimumSideTrades
            || evidence.OutOfSampleShort < MinimumSideTrades
            || evidence.WalkForwardWindowsExecuted < MinimumWalkForwardWindows
            || !evidence.ProfitFactorDefined)
        {
            return InsufficientEvidence;
        }

        var windowsWithTrades = evidence.WalkForwardWindowsWithTrades;
        var stable = windowsWithTrades == 0 || evidence.WalkForwardWindowsNonNegative * 2 >= windowsWithTrades;
        var passed = evidence.OutOfSampleNet > 0m
            && evidence.OutOfSampleProfitFactor >= MinimumProfitFactor
            && evidence.OutOfSampleMaxDrawdownPercent < MaximumDrawdownPercent
            && evidence.InSampleTrades >= 30
            && evidence.InSampleNet > 0m
            && stable;
        return passed ? Validated : Rejected;
    }

    public static string? FormatProfitFactor(int trades, int losingTrades, decimal? profitFactor)
    {
        if (trades == 0)
        {
            return null;
        }

        if (losingTrades == 0)
        {
            return "undefined_no_losing_trades";
        }

        return profitFactor?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed record ProductionNewEvidence(
    int SymbolsWithOutOfSampleTrades,
    int OutOfSampleTrades,
    int OutOfSampleLong,
    int OutOfSampleShort,
    decimal OutOfSampleNet,
    decimal OutOfSampleProfitFactor,
    bool ProfitFactorDefined,
    decimal OutOfSampleMaxDrawdownPercent,
    int InSampleTrades,
    decimal InSampleNet,
    int WalkForwardWindowsExecuted,
    int WalkForwardWindowsWithTrades,
    int WalkForwardWindowsNonNegative,
    bool RequiredDataPresent);
