using TradingPlatform.Domain.Positions;

namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Shared price and cost rules for refactored strategy versions.
/// Research defaults are not written over saved risk books.
/// </summary>
public static class StrategyExecutionRules
{
    public const string VersionStatus = "NOT_VALIDATED";

    /// <summary>Starting research risk. Not an optimized parameter and not applied to stored profiles.</summary>
    public const decimal ResearchRiskPerTradePercent = 0.25m;

    public const decimal ResearchAggregateStopRiskPercent = 1.5m;
    public const decimal ResearchDailyLossLockPercent = 2m;

    public const decimal EntryFeeRate = 0.0004m;
    public const decimal ExitFeeRate = 0.0004m;

    /// <summary>10 bps per side. Sensitivity cases are 5, 10 and 20 bps in the replay settings.</summary>
    public const decimal BaselineSlippageRate = 0.0010m;

    public const decimal MinGrossToCostMultiple = 3m;

    public static decimal RoundTripCostRate => EntryFeeRate + ExitFeeRate + (2m * BaselineSlippageRate);

    public static bool VolumeUsable(decimal volume) => volume > 0m;

    public static bool PaysRoundTrip(decimal entry, decimal target)
    {
        if (entry <= 0m || target <= 0m)
        {
            return false;
        }

        var gross = Math.Abs(target - entry) / entry;
        return gross + 0.0000001m >= RoundTripCostRate * MinGrossToCostMultiple;
    }

    public static bool RewardMultiple(decimal entry, decimal stop, decimal target, decimal minimum)
    {
        var risk = Math.Abs(entry - stop);
        var reward = Math.Abs(target - entry);
        return risk > 0m && reward + 0.0000001m >= risk * minimum;
    }

    /// <summary>
    /// Long stops round up (tighter). Short stops round down (tighter).
    /// The result is never farther from the market than the raw stop.
    /// </summary>
    public static decimal RoundStop(PositionSide side, decimal price, decimal tick)
    {
        if (tick <= 0m || price <= 0m)
        {
            return price;
        }

        var steps = price / tick;
        var rounded = side == PositionSide.Short
            ? Math.Floor(steps) * tick
            : Math.Ceiling(steps) * tick;
        return rounded <= 0m ? price : rounded;
    }

    /// <summary>Targets round toward a worse fill: long down, short up.</summary>
    public static decimal RoundTarget(PositionSide side, decimal price, decimal tick)
    {
        if (tick <= 0m || price <= 0m)
        {
            return price;
        }

        var steps = price / tick;
        var rounded = side == PositionSide.Short
            ? Math.Ceiling(steps) * tick
            : Math.Floor(steps) * tick;
        return rounded <= 0m ? price : rounded;
    }

    public static decimal? TighterStop(PositionSide side, decimal? current, decimal candidate, decimal price)
    {
        if (side == PositionSide.Short)
        {
            if (candidate <= price)
            {
                return current;
            }

            return current is null || candidate < current ? candidate : current;
        }

        if (candidate >= price)
        {
            return current;
        }

        return current is null || candidate > current ? candidate : current;
    }
}
