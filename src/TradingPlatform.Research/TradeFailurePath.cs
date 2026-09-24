using TradingPlatform.Domain.Market;

namespace TradingPlatform.Research;

/// <summary>
/// Diagnostic buckets for an existing Model B book. The 2% stop and 4% target are the book's own distances.
/// They are not a search, and they are not a proposed new exit.
/// </summary>
public static class TradeFailureCatalog
{
    public const string Version = "trade-failure-v1";
    public const decimal StopDistance = 0.02m;
    public const decimal TargetDistance = 0.04m;
    public const int ImmediateBars = 3;
    public const decimal ImmediateAdverse = 0.01m;
    public const decimal ImmediateFavorableCeiling = 0.005m;
    public const decimal HalfStop = 0.01m;
    public const int HorizonBars = 20;
    public const int MinimumFamilyLosers = 50;
    public const decimal SharedModeShare = 0.30m;

    public const string Rule =
        "Fixed before aggregation. Losers are classified in this order and one label only. " +
        "COST_DOMINATED: net is negative and gross is not. " +
        "IMMEDIATELY_WRONG: within the first 3 bars, adverse excursion reaches 1% and favorable excursion stays under 0.5%. " +
        "RIGHT_DIRECTION_STOP_TOO_TIGHT: the book exit is Stop loss, and within 20 bars after that exit the favorable excursion from the original entry reaches 2%. " +
        "RIGHT_DIRECTION_EXIT_TOO_EARLY: the exit is not Stop loss and not Take profit, and within 20 bars after the exit the favorable excursion from the original entry reaches 4%. " +
        "RIGHT_DIRECTION_BAD_ENTRY: favorable excursion during the trade reaches 1% and the trade still finishes negative. " +
        "NO_CLEAR_CAUSE: every other loser. " +
        "Same-bar order is unknown, so a favorable wick on the stop bar is not counted as movement before the stop, and an adverse wick on the target bar is not counted as movement before the target. " +
        "A label is cross-family only when at least two independent groups each have 50 losers and that label is at least 30% of those losers. " +
        "Nothing here is an edge, a new stop, or a new target.";
}

public sealed record FailureFacts(
    decimal Mae,
    decimal Mfe,
    int BarsToMae,
    int BarsToMfe,
    int? BarsToStop,
    int? BarsToTarget,
    decimal MfeBeforeStop,
    decimal MaeBeforeTarget,
    bool RecoveredAfterStop,
    bool ReachedTargetAfterExit,
    decimal? Mfe1,
    decimal? Mfe3,
    decimal? Mfe5,
    decimal? Mfe10,
    decimal? Mfe20,
    int HoldBars,
    string Reason,
    decimal Gross,
    decimal Fees,
    decimal Slippage,
    decimal Funding,
    decimal Net,
    bool ImmediateWrong,
    bool MfeAtLeastHalfStop);

public static class TradeFailurePath
{
    public static FailureFacts Measure(
        IReadOnlyList<MarketCandle> candles,
        int fillIndex,
        int exitIndex,
        bool longSide,
        decimal entryPrice,
        string reason,
        decimal gross,
        decimal fees,
        decimal slippage,
        decimal funding,
        decimal net)
    {
        if (fillIndex < 0 || exitIndex < fillIndex || exitIndex >= candles.Count || entryPrice <= 0m)
        {
            return Empty(reason, gross, fees, slippage, funding, net);
        }

        decimal mae = 0m;
        decimal mfe = 0m;
        var barsToMae = 0;
        var barsToMfe = 0;
        int? barsToStop = null;
        int? barsToTarget = null;
        var mfeBeforeStop = 0m;
        var maeBeforeTarget = 0m;
        var stopSeen = false;
        var targetSeen = false;
        for (var i = fillIndex; i <= exitIndex; i++)
        {
            var adverse = Adverse(candles[i], entryPrice, longSide);
            var favorable = Favorable(candles[i], entryPrice, longSide);
            var bars = i - fillIndex;
            if (!stopSeen)
            {
                mfeBeforeStop = Math.Max(mfeBeforeStop, favorable);
            }

            if (!targetSeen)
            {
                maeBeforeTarget = Math.Max(maeBeforeTarget, adverse);
            }

            if (adverse >= mae)
            {
                mae = adverse;
                barsToMae = bars;
            }

            if (favorable >= mfe)
            {
                mfe = favorable;
                barsToMfe = bars;
            }

            if (barsToStop is null && adverse >= TradeFailureCatalog.StopDistance)
            {
                barsToStop = bars;
                stopSeen = true;
                mfeBeforeStop = MaxFavorableBefore(candles, fillIndex, i, entryPrice, longSide);
            }

            if (barsToTarget is null && favorable >= TradeFailureCatalog.TargetDistance)
            {
                barsToTarget = bars;
                targetSeen = true;
                maeBeforeTarget = MaxAdverseBefore(candles, fillIndex, i, entryPrice, longSide);
            }
        }

        var immediateAdverse = 0m;
        var immediateFavorable = 0m;
        var immediateEnd = Math.Min(candles.Count - 1, fillIndex + TradeFailureCatalog.ImmediateBars - 1);
        for (var i = fillIndex; i <= immediateEnd; i++)
        {
            immediateAdverse = Math.Max(immediateAdverse, Adverse(candles[i], entryPrice, longSide));
            immediateFavorable = Math.Max(immediateFavorable, Favorable(candles[i], entryPrice, longSide));
        }

        var recovered = false;
        var reachedAfter = false;
        if (string.Equals(reason, "Stop loss", StringComparison.Ordinal))
        {
            recovered = WindowFavorable(candles, exitIndex + 1, TradeFailureCatalog.HorizonBars, entryPrice, longSide) >= TradeFailureCatalog.StopDistance;
        }

        if (!string.Equals(reason, "Stop loss", StringComparison.Ordinal)
            && !string.Equals(reason, "Take profit", StringComparison.Ordinal))
        {
            reachedAfter = WindowFavorable(candles, exitIndex + 1, TradeFailureCatalog.HorizonBars, entryPrice, longSide) >= TradeFailureCatalog.TargetDistance;
        }

        return new FailureFacts(
            mae,
            mfe,
            barsToMae,
            barsToMfe,
            barsToStop,
            barsToTarget,
            mfeBeforeStop,
            maeBeforeTarget,
            recovered,
            reachedAfter,
            Horizon(candles, fillIndex, 1, entryPrice, longSide),
            Horizon(candles, fillIndex, 3, entryPrice, longSide),
            Horizon(candles, fillIndex, 5, entryPrice, longSide),
            Horizon(candles, fillIndex, 10, entryPrice, longSide),
            Horizon(candles, fillIndex, 20, entryPrice, longSide),
            exitIndex - fillIndex + 1,
            reason,
            gross,
            fees,
            slippage,
            funding,
            net,
            immediateAdverse >= TradeFailureCatalog.ImmediateAdverse && immediateFavorable < TradeFailureCatalog.ImmediateFavorableCeiling,
            mfe >= TradeFailureCatalog.HalfStop);
    }

    public static string Classify(FailureFacts facts)
    {
        if (facts.Net >= 0m)
        {
            return "WINNER";
        }

        if (facts.Gross >= 0m)
        {
            return "COST_DOMINATED";
        }

        if (facts.ImmediateWrong)
        {
            return "IMMEDIATELY_WRONG";
        }

        if (string.Equals(facts.Reason, "Stop loss", StringComparison.Ordinal) && facts.RecoveredAfterStop)
        {
            return "RIGHT_DIRECTION_STOP_TOO_TIGHT";
        }

        if (facts.ReachedTargetAfterExit)
        {
            return "RIGHT_DIRECTION_EXIT_TOO_EARLY";
        }

        if (facts.MfeAtLeastHalfStop)
        {
            return "RIGHT_DIRECTION_BAD_ENTRY";
        }

        return "NO_CLEAR_CAUSE";
    }

    private static decimal WindowFavorable(IReadOnlyList<MarketCandle> candles, int start, int bars, decimal entry, bool longSide)
    {
        if (start >= candles.Count)
        {
            return 0m;
        }

        var end = Math.Min(candles.Count - 1, start + bars - 1);
        var best = 0m;
        for (var i = start; i <= end; i++)
        {
            best = Math.Max(best, Favorable(candles[i], entry, longSide));
        }

        return best;
    }

    private static decimal? Horizon(IReadOnlyList<MarketCandle> candles, int fill, int bars, decimal entry, bool longSide)
    {
        var end = fill + bars - 1;
        if (end >= candles.Count)
        {
            return null;
        }

        var best = 0m;
        for (var i = fill; i <= end; i++)
        {
            best = Math.Max(best, Favorable(candles[i], entry, longSide));
        }

        return best;
    }

    private static decimal MaxFavorableBefore(IReadOnlyList<MarketCandle> candles, int fill, int stopBar, decimal entry, bool longSide)
    {
        var best = 0m;
        for (var i = fill; i < stopBar; i++)
        {
            best = Math.Max(best, Favorable(candles[i], entry, longSide));
        }

        return best;
    }

    private static decimal MaxAdverseBefore(IReadOnlyList<MarketCandle> candles, int fill, int targetBar, decimal entry, bool longSide)
    {
        var best = 0m;
        for (var i = fill; i < targetBar; i++)
        {
            best = Math.Max(best, Adverse(candles[i], entry, longSide));
        }

        return best;
    }

    private static decimal Favorable(MarketCandle bar, decimal entry, bool longSide) =>
        longSide ? Math.Max(0m, (bar.High - entry) / entry) : Math.Max(0m, (entry - bar.Low) / entry);

    private static decimal Adverse(MarketCandle bar, decimal entry, bool longSide) =>
        longSide ? Math.Max(0m, (entry - bar.Low) / entry) : Math.Max(0m, (bar.High - entry) / entry);

    private static FailureFacts Empty(string reason, decimal gross, decimal fees, decimal slippage, decimal funding, decimal net) =>
        new(0m, 0m, 0, 0, null, null, 0m, 0m, false, false, null, null, null, null, null, 0, reason, gross, fees, slippage, funding, net, false, false);
}
