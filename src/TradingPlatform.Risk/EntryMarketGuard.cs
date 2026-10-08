namespace TradingPlatform.Risk;

public sealed record EntryBar(decimal High, decimal Low, decimal Close);

/// <summary>
/// Entry-time market checks that sizing cannot see: a wide book (the fill will be far from the signal price)
/// and a shock bar (the stop distance was sized for calmer conditions).
/// </summary>
public static class EntryMarketGuard
{
    public const int ShockLookback = 50;

    public static decimal? SpreadBps(decimal? bid, decimal? ask)
    {
        if (bid is not > 0m || ask is not > 0m || ask < bid)
        {
            return null;
        }

        var mid = (bid.Value + ask.Value) / 2m;
        return (ask.Value - bid.Value) / mid * 10_000m;
    }

    /// <summary>Last bar's true range over the median true range of the bars before it. Null with too little history.</summary>
    public static decimal? RangeShock(IReadOnlyList<EntryBar> bars, int lookback = ShockLookback)
    {
        if (bars.Count < 12)
        {
            return null;
        }

        var start = Math.Max(1, bars.Count - 1 - lookback);
        var ranges = new List<decimal>(bars.Count);
        for (var i = start; i < bars.Count - 1; i++)
        {
            ranges.Add(TrueRange(bars[i], bars[i - 1].Close));
        }

        ranges.Sort();
        var median = ranges[ranges.Count / 2];
        if (median <= 0m)
        {
            return null;
        }

        return TrueRange(bars[^1], bars[^2].Close) / median;
    }

    /// <summary>
    /// Null when entry may proceed. A missing book blocks only when <paramref name="requireBook"/> (live),
    /// so paper keeps running on candle-only data.
    /// </summary>
    public static string? Reject(
        decimal? spreadBps,
        decimal? rangeShock,
        decimal maxSpreadBps,
        decimal maxRangeShock,
        bool requireBook)
    {
        if (spreadBps is null && requireBook && maxSpreadBps > 0m)
        {
            return "Order book spread is unknown. Live entry is blocked.";
        }

        if (spreadBps is { } spread && maxSpreadBps > 0m && spread > maxSpreadBps)
        {
            return $"Spread {spread:0.0} bps is wider than {maxSpreadBps:0.0} bps. Entry skipped.";
        }

        if (rangeShock is { } shock && maxRangeShock > 0m && shock > maxRangeShock)
        {
            return $"Last bar moved {shock:0.0}x its usual range (limit {maxRangeShock:0.0}x). Entry skipped.";
        }

        return null;
    }

    /// <summary>Half the spread is the expected cost of crossing the book, in percent.</summary>
    public static decimal SlippagePercent(decimal? spreadBps, decimal floorPercent) =>
        spreadBps is { } spread ? Math.Max(floorPercent, spread / 2m / 100m) : floorPercent;

    /// <summary>
    /// Planned take profit must be at least this many times the market round trip.
    /// Matches <c>StrategyExecutionRules.MinGrossToCostMultiple</c>.
    /// </summary>
    public const decimal MinTakeProfitMultiple = 3m;

    /// <summary>
    /// Percent of notional paid to enter and exit with market orders: the taker fee on each side,
    /// plus the full spread (buy the ask, sell the bid). A missing book uses <paramref name="unknownSpreadPercent"/>
    /// instead of a zero spread. Leverage does not change this figure; the target and the cost are both fractions of notional.
    /// </summary>
    public static decimal RoundTripCostPercent(decimal takerFeePercent, decimal? spreadBps, decimal unknownSpreadPercent)
    {
        var fee = takerFeePercent > 0m ? takerFeePercent : 0m;
        var spread = spreadBps is { } bps
            ? (bps > 0m ? bps / 100m : 0m)
            : Math.Max(0m, unknownSpreadPercent);
        return fee * 2m + spread;
    }

    /// <summary>Null when the planned take profit covers the market round trip. Otherwise the entry must be skipped.</summary>
    public static string? RejectRoundTrip(
        decimal takeProfitPercent,
        decimal takerFeePercent,
        decimal? spreadBps,
        decimal unknownSpreadPercent)
    {
        if (takeProfitPercent <= 0m)
        {
            return "Take profit is missing. Entry skipped.";
        }

        var cost = RoundTripCostPercent(takerFeePercent, spreadBps, unknownSpreadPercent);
        if (cost <= 0m || takeProfitPercent + 0.0000001m >= cost * MinTakeProfitMultiple)
        {
            return null;
        }

        return $"Take profit {takeProfitPercent:0.00}% does not cover {MinTakeProfitMultiple:0.#}× the round-trip cost of {cost:0.00}% (taker fee both sides and the spread). Entry skipped.";
    }

    private static decimal TrueRange(EntryBar bar, decimal previousClose) =>
        Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));
}
