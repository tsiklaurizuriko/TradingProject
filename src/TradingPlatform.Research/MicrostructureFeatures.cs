using TradingPlatform.Domain.Market;

namespace TradingPlatform.Research;

/// <summary>
/// Causal futures-native features. A value is visible only when its timestamp is inside the signal bar,
/// except the settled funding rate, which stays known until the next settlement. Nothing is interpolated.
/// </summary>
public static class MicrostructureCatalog
{
    public const string Version = "microstructure-v1";
    public const int LevelLookback = 100;
    public const int FundingLookback = 90;
    public const double MinimumDayCoverage = 0.95;

    public const string RepeatableRule =
        "Same rule as signal-quality-v1. Spearman rank correlation is between the pre-entry feature and net PnL. " +
        "REPEATABLE requires the same sign and absolute rho of at least 0.05 in IS, validation, and OOS, each with at least 200 trades, " +
        "the same sign in at least 3 of 4 chronological blocks with at least 100 trades, at least two independent family groups, and at least two of BTC, ETH, and BNB. " +
        "FAMILY_SPECIFIC: the three windows agree, but only one family group meets the slice bar. " +
        "SYMBOL_SPECIFIC: the three windows agree, but only one symbol meets the slice bar. " +
        "OOS_ONLY: OOS meets the bar and IS does not share that sign. " +
        "UNSTABLE: the windows disagree, or they agree but the block, family, or symbol bar fails. " +
        "NO_EVIDENCE: everything else. Tertile cuts are the 1/3 and 2/3 quantiles of the feature on IS trades only. " +
        "None of these labels is an edge. Features are not signed. Long and short are reported as slices.";

    public static readonly string[] Features =
    [
        "oi_change_1",
        "oi_change_3",
        "oi_change_12",
        "oi_percentile",
        "funding_rate",
        "funding_percentile",
        "funding_change",
        "taker_imbalance",
        "taker_imbalance_change",
        "taker_buy_ratio",
        "normalized_basis",
        "basis_change",
        "basis_percentile",
        "depth_imbalance_1pct",
        "depth_imbalance_change"
    ];
}

public static class MicrostructureFeatures
{
    public static Dictionary<string, decimal?> Compute(
        int index,
        IReadOnlyList<decimal?> openInterest,
        IReadOnlyList<FundingPoint> funding,
        DateTimeOffset signalClose,
        IReadOnlyList<decimal?> takerImbalance,
        IReadOnlyList<decimal?> takerRatio,
        IReadOnlyList<decimal?> basis,
        IReadOnlyList<decimal?> depth)
    {
        var values = MicrostructureCatalog.Features.ToDictionary(name => name, _ => (decimal?)null, StringComparer.Ordinal);
        if (index < 0)
        {
            return values;
        }

        values["oi_change_1"] = RatioChange(openInterest, index, 1);
        values["oi_change_3"] = RatioChange(openInterest, index, 3);
        values["oi_change_12"] = RatioChange(openInterest, index, 12);
        values["oi_percentile"] = Rank(openInterest, index, MicrostructureCatalog.LevelLookback);
        Funding(funding, signalClose, values);
        values["taker_imbalance"] = At(takerImbalance, index);
        values["taker_imbalance_change"] = Diff(takerImbalance, index, 1);
        values["taker_buy_ratio"] = At(takerRatio, index);
        values["normalized_basis"] = At(basis, index);
        values["basis_change"] = Diff(basis, index, 1);
        values["basis_percentile"] = Rank(basis, index, MicrostructureCatalog.LevelLookback);
        values["depth_imbalance_1pct"] = At(depth, index);
        values["depth_imbalance_change"] = Diff(depth, index, 1);
        return values;
    }

    public static (decimal? Ratio, decimal? Imbalance) Taker(decimal volume, decimal takerBuy)
    {
        if (volume <= 0m || takerBuy <= 0m || takerBuy > volume)
        {
            return (null, null);
        }

        var ratio = takerBuy / volume;
        return (ratio, (2m * ratio) - 1m);
    }

    /// <summary>
    /// Last observation with OpenTime &lt;= time &lt;= CloseTime. A print outside the bar is not carried forward.
    /// </summary>
    public static decimal?[] AlignToBar(
        IReadOnlyList<(DateTimeOffset Time, decimal Value)> observations,
        IReadOnlyList<MarketCandle> candles)
    {
        var result = new decimal?[candles.Count];
        if (observations.Count == 0 || candles.Count == 0)
        {
            return result;
        }

        var ordered = observations.OrderBy(row => row.Time).ToArray();
        var cursor = 0;
        for (var i = 0; i < candles.Count; i++)
        {
            var open = candles[i].OpenTime;
            var close = candles[i].CloseTime;
            while (cursor < ordered.Length && ordered[cursor].Time < open)
            {
                cursor++;
            }

            decimal? value = null;
            var look = cursor;
            while (look < ordered.Length && ordered[look].Time <= close)
            {
                value = ordered[look].Value;
                look++;
            }

            result[i] = value;
            cursor = look;
        }

        return result;
    }

    private static void Funding(IReadOnlyList<FundingPoint> funding, DateTimeOffset signalClose, Dictionary<string, decimal?> values)
    {
        var index = LastFunding(funding, signalClose);
        if (index < 0)
        {
            return;
        }

        var rate = funding[index].FundingRate;
        values["funding_rate"] = rate;
        if (index >= 1)
        {
            values["funding_change"] = rate - funding[index - 1].FundingRate;
        }

        if (index + 1 < MicrostructureCatalog.FundingLookback)
        {
            return;
        }

        var below = 0;
        for (var i = index - (MicrostructureCatalog.FundingLookback - 1); i < index; i++)
        {
            if (funding[i].FundingRate < rate)
            {
                below++;
            }
        }

        values["funding_percentile"] = below / (decimal)(MicrostructureCatalog.FundingLookback - 1);
    }

    private static int LastFunding(IReadOnlyList<FundingPoint> funding, DateTimeOffset signalClose)
    {
        var low = 0;
        var high = funding.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (funding[mid].FundingTime <= signalClose)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    private static decimal? At(IReadOnlyList<decimal?> series, int index) =>
        index >= 0 && index < series.Count ? series[index] : null;

    private static decimal? RatioChange(IReadOnlyList<decimal?> series, int index, int lookback)
    {
        if (index < lookback || index >= series.Count || series[index] is not { } now || series[index - lookback] is not { } prev || prev == 0m)
        {
            return null;
        }

        return (now - prev) / prev;
    }

    private static decimal? Diff(IReadOnlyList<decimal?> series, int index, int lookback)
    {
        if (index < lookback || index >= series.Count || series[index] is not { } now || series[index - lookback] is not { } prev)
        {
            return null;
        }

        return now - prev;
    }

    private static decimal? Rank(IReadOnlyList<decimal?> series, int index, int lookback)
    {
        if (index < lookback || index >= series.Count || series[index] is not { } current)
        {
            return null;
        }

        var below = 0;
        for (var i = index - lookback; i < index; i++)
        {
            if (series[i] is not { } value)
            {
                return null;
            }

            if (value < current)
            {
                below++;
            }
        }

        return below / (decimal)lookback;
    }
}
