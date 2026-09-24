using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

/// <summary>
/// Pre-entry features for the existing reconstructed book. Definitions are fixed.
/// A positive signed value means the measurement agrees with the trade direction.
/// </summary>
public static class SignalQualityCatalog
{
    public const string Version = "signal-quality-v1";
    public const int ReturnLookback = 20;
    public const int SlopeBars = 5;
    public const int EmaFast = 20;
    public const int EmaSlow = 50;
    public const decimal MinimumAbsRho = 0.05m;
    public const int MinimumWindowTrades = 200;
    public const int MinimumSliceTrades = 100;
    public const int MinimumBucketTrades = 30;

    public const string RepeatableRule =
        "Fixed before aggregation. Spearman rank correlation is between the pre-entry feature and net PnL. " +
        "REPEATABLE requires the same sign and absolute rho of at least 0.05 in IS, validation, and OOS, each with at least 200 trades, " +
        "the same sign in at least 3 of 4 chronological blocks with at least 100 trades, at least two independent family groups, and at least two of BTC, ETH, and BNB. " +
        "FAMILY_SPECIFIC: the three windows agree, but only one family group meets the slice bar. " +
        "SYMBOL_SPECIFIC: the three windows agree, but only one symbol meets the slice bar. " +
        "OOS_ONLY: OOS meets the bar and IS does not share that sign. " +
        "UNSTABLE: the windows disagree, or they agree but the block, family, or symbol bar fails. " +
        "NO_EVIDENCE: everything else. Tertile cuts are the 1/3 and 2/3 quantiles of the feature on IS trades only. " +
        "Session and weekday are pre-registered slices, not ranked features, and they are not eligible for REPEATABLE. " +
        "None of these labels is an edge.";

    public static readonly string[] Features =
    [
        "signed_ret_1",
        "signed_ret_3",
        "signed_ret_5",
        "signed_ret_10",
        "dist_high_20",
        "dist_low_20",
        "body_ratio",
        "upper_wick_ratio",
        "lower_wick_ratio",
        "consecutive_bars",
        "signed_accel_3",
        "atr_percentile",
        "atr_over_price",
        "range_over_atr",
        "realized_vol_20",
        "prior_compression",
        "adx",
        "signed_ema20_dist",
        "signed_ema50_dist",
        "signed_ema20_slope",
        "signed_ema50_slope",
        "signed_hourly_bias",
        "bos_aligned",
        "choch_aligned",
        "relative_volume",
        "volume_over_median_20",
        "signed_vwap_dist",
        "signed_vwap_slope",
        "btc_signed_ret_20",
        "btc_adx",
        "btc_atr_percentile",
        "btc_same_clock_signed",
        "coins_same_sign",
        "signals_already_closed",
        "symbols_already_closed",
        "minutes_since_signal",
        "minutes_since_trade"
    ];
}

public static class SignalQualityFeatures
{
    public static Dictionary<string, decimal?> Compute(
        CausalIndicatorCache entry,
        CausalIndicatorCache hourly,
        CausalIndicatorCache btcHourly,
        CausalIndicatorCache ethHourly,
        CausalIndicatorCache bnbHourly,
        int index,
        bool longSide)
    {
        var values = SignalQualityCatalog.Features.ToDictionary(name => name, _ => (decimal?)null, StringComparer.Ordinal);
        var candles = entry.Candles;
        if (index < 0 || index >= candles.Count || candles[index].Close <= 0m)
        {
            return values;
        }

        var side = longSide ? 1m : -1m;
        var close = candles[index].Close;
        values["signed_ret_1"] = SignedReturn(candles, index, 1, side);
        values["signed_ret_3"] = SignedReturn(candles, index, 3, side);
        values["signed_ret_5"] = SignedReturn(candles, index, 5, side);
        values["signed_ret_10"] = SignedReturn(candles, index, 10, side);
        values["signed_accel_3"] = Accel(candles, index, side);
        values["consecutive_bars"] = Consecutive(candles, index, longSide);
        Distance(candles, index, close, values);
        CandleShape(candles[index], values);

        var atr = entry.Atr(14);
        if (atr[index] is { } volatility && volatility > 0m)
        {
            values["atr_over_price"] = volatility / close;
            values["range_over_atr"] = (candles[index].High - candles[index].Low) / volatility;
        }

        var percentile = entry.AtrPercentile(14, 50);
        values["atr_percentile"] = percentile[index];
        values["realized_vol_20"] = RealizedVol(candles, index);
        values["prior_compression"] = PriorCompression(candles, atr, index);
        var adx = entry.Adx(14);
        values["adx"] = adx[index];
        SignedEma(entry, index, close, side, values);
        values["signed_hourly_bias"] = FinalFiveSignals.BiasAt(hourly, hourly.PriceAction(), candles[index].CloseTime) * side;
        Structure(entry, index, longSide, values);
        values["relative_volume"] = entry.RelativeVolume(20)[index];
        values["volume_over_median_20"] = VolumeOverMedian(candles, index);
        SignedVwap(entry, index, close, side, values);
        Btc(btcHourly, ethHourly, bnbHourly, candles[index].CloseTime, side, values);
        return values;
    }

    public static string Classify(
        decimal? isRho,
        int isN,
        decimal? valRho,
        int valN,
        decimal? oosRho,
        int oosN,
        int agreeingBlocks,
        int agreeingFamilies,
        int agreeingSymbols)
    {
        var isReady = isN >= SignalQualityCatalog.MinimumWindowTrades && isRho is { } ir && Math.Abs(ir) >= SignalQualityCatalog.MinimumAbsRho;
        var valReady = valN >= SignalQualityCatalog.MinimumWindowTrades && valRho is { } vr && Math.Abs(vr) >= SignalQualityCatalog.MinimumAbsRho;
        var oosReady = oosN >= SignalQualityCatalog.MinimumWindowTrades && oosRho is { } orho && Math.Abs(orho) >= SignalQualityCatalog.MinimumAbsRho;
        var same = isReady && valReady && oosReady && Math.Sign(isRho!.Value) == Math.Sign(valRho!.Value) && Math.Sign(isRho.Value) == Math.Sign(oosRho!.Value);
        if (same && agreeingBlocks >= 3 && agreeingFamilies >= 2 && agreeingSymbols >= 2)
        {
            return "REPEATABLE";
        }

        if (same && agreeingFamilies == 1)
        {
            return "FAMILY_SPECIFIC";
        }

        if (same && agreeingSymbols == 1)
        {
            return "SYMBOL_SPECIFIC";
        }

        if (same)
        {
            return "UNSTABLE";
        }

        if (oosReady && isN >= SignalQualityCatalog.MinimumWindowTrades && (!isReady || Math.Sign(isRho!.Value) != Math.Sign(oosRho!.Value)))
        {
            return "OOS_ONLY";
        }

        if (isReady && oosReady && Math.Sign(isRho!.Value) != Math.Sign(oosRho!.Value))
        {
            return "UNSTABLE";
        }

        return "NO_EVIDENCE";
    }

    private static decimal? SignedReturn(IReadOnlyList<MarketCandle> candles, int index, int bars, decimal side)
    {
        var prior = index - bars;
        if (prior < 0 || candles[prior].Close <= 0m)
        {
            return null;
        }

        return side * (candles[index].Close - candles[prior].Close) / candles[prior].Close;
    }

    private static decimal? Accel(IReadOnlyList<MarketCandle> candles, int index, decimal side)
    {
        if (SignedReturn(candles, index, 3, side) is not { } recent || index < 6)
        {
            return null;
        }

        var earlier = SignedReturn(candles, index - 3, 3, side);
        return earlier is null ? null : recent - earlier;
    }

    private static decimal Consecutive(IReadOnlyList<MarketCandle> candles, int index, bool longSide)
    {
        var count = 0;
        for (var j = index; j > 0 && count < 20; j--)
        {
            var step = candles[j].Close - candles[j - 1].Close;
            var aligned = longSide ? step > 0m : step < 0m;
            if (!aligned)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static void Distance(IReadOnlyList<MarketCandle> candles, int index, decimal close, Dictionary<string, decimal?> values)
    {
        var start = Math.Max(0, index - (SignalQualityCatalog.ReturnLookback - 1));
        if (index - start + 1 < SignalQualityCatalog.ReturnLookback)
        {
            return;
        }

        var high = decimal.MinValue;
        var low = decimal.MaxValue;
        for (var j = start; j <= index; j++)
        {
            high = Math.Max(high, candles[j].High);
            low = Math.Min(low, candles[j].Low);
        }

        values["dist_high_20"] = (high - close) / close;
        values["dist_low_20"] = (close - low) / close;
    }

    private static void CandleShape(MarketCandle bar, Dictionary<string, decimal?> values)
    {
        var range = bar.High - bar.Low;
        if (range <= 0m)
        {
            return;
        }

        var bodyTop = Math.Max(bar.Open, bar.Close);
        var bodyBottom = Math.Min(bar.Open, bar.Close);
        values["body_ratio"] = (bodyTop - bodyBottom) / range;
        values["upper_wick_ratio"] = (bar.High - bodyTop) / range;
        values["lower_wick_ratio"] = (bodyBottom - bar.Low) / range;
    }

    private static decimal? RealizedVol(IReadOnlyList<MarketCandle> candles, int index)
    {
        if (index < SignalQualityCatalog.ReturnLookback)
        {
            return null;
        }

        var returns = new List<decimal>();
        for (var j = index - SignalQualityCatalog.ReturnLookback + 1; j <= index; j++)
        {
            if (candles[j - 1].Close <= 0m)
            {
                return null;
            }

            returns.Add((candles[j].Close - candles[j - 1].Close) / candles[j - 1].Close);
        }

        var mean = returns.Average();
        var variance = returns.Average(value => (value - mean) * (value - mean));
        return (decimal)Math.Sqrt((double)variance);
    }

    private static decimal? PriorCompression(IReadOnlyList<MarketCandle> candles, IReadOnlyList<decimal?> atr, int index)
    {
        if (index < 8)
        {
            return null;
        }

        for (var j = index - 8; j < index; j++)
        {
            if (atr[j] is not { } prior || prior <= 0m || candles[j].High - candles[j].Low >= prior)
            {
                return 0m;
            }
        }

        return 1m;
    }

    private static void SignedEma(CausalIndicatorCache entry, int index, decimal close, decimal side, Dictionary<string, decimal?> values)
    {
        var fast = entry.Ema(SignalQualityCatalog.EmaFast);
        var slow = entry.Ema(SignalQualityCatalog.EmaSlow);
        if (fast[index] is { } ema20)
        {
            values["signed_ema20_dist"] = side * (close - ema20) / close;
            var prior = index - SignalQualityCatalog.SlopeBars;
            if (prior >= 0 && fast[prior] is { } old)
            {
                values["signed_ema20_slope"] = side * (ema20 - old) / close;
            }
        }

        if (slow[index] is { } ema50)
        {
            values["signed_ema50_dist"] = side * (close - ema50) / close;
            var prior = index - SignalQualityCatalog.SlopeBars;
            if (prior >= 0 && slow[prior] is { } old)
            {
                values["signed_ema50_slope"] = side * (ema50 - old) / close;
            }
        }
    }

    private static void Structure(CausalIndicatorCache entry, int index, bool longSide, Dictionary<string, decimal?> values)
    {
        var structure = entry.PriceAction().Structure[index];
        var bos = structure.BosBull ? 1m : structure.BosBear ? -1m : 0m;
        var choch = structure.ChochBull ? 1m : structure.ChochBear ? -1m : 0m;
        var side = longSide ? 1m : -1m;
        values["bos_aligned"] = bos * side;
        values["choch_aligned"] = choch * side;
    }

    private static decimal? VolumeOverMedian(IReadOnlyList<MarketCandle> candles, int index)
    {
        if (index < SignalQualityCatalog.ReturnLookback)
        {
            return null;
        }

        var sample = new decimal[SignalQualityCatalog.ReturnLookback];
        for (var j = 0; j < sample.Length; j++)
        {
            sample[j] = candles[index - sample.Length + j].Volume;
        }

        Array.Sort(sample);
        var median = sample[sample.Length / 2];
        return median <= 0m ? null : candles[index].Volume / median;
    }

    private static void SignedVwap(CausalIndicatorCache entry, int index, decimal close, decimal side, Dictionary<string, decimal?> values)
    {
        var vwap = entry.SessionVwap();
        if (vwap[index] is not { } current || current == 0m)
        {
            return;
        }

        values["signed_vwap_dist"] = side * (close - current) / close;
        var prior = index - SignalQualityCatalog.SlopeBars;
        if (prior >= 0 && vwap[prior] is { } old)
        {
            values["signed_vwap_slope"] = side * (current - old) / close;
        }
    }

    private static void Btc(
        CausalIndicatorCache btc,
        CausalIndicatorCache eth,
        CausalIndicatorCache bnb,
        DateTimeOffset close,
        decimal side,
        Dictionary<string, decimal?> values)
    {
        var index = EdgeFeatureBuilder.LastClosed(btc, close);
        if (index < 0)
        {
            return;
        }

        values["btc_adx"] = btc.Adx(14)[index];
        values["btc_atr_percentile"] = btc.AtrPercentile(14, 50)[index];
        if (index >= SignalQualityCatalog.ReturnLookback && btc.Candles[index - SignalQualityCatalog.ReturnLookback].Close > 0m)
        {
            var raw = (btc.Candles[index].Close - btc.Candles[index - SignalQualityCatalog.ReturnLookback].Close) / btc.Candles[index - SignalQualityCatalog.ReturnLookback].Close;
            values["btc_signed_ret_20"] = side * raw;
        }

        var start = EdgeFeatureBuilder.LastClosed(btc, close.AddHours(-SignalQualityCatalog.ReturnLookback));
        if (start >= 0 && btc.Candles[start].Close > 0m)
        {
            values["btc_same_clock_signed"] = side * (btc.Candles[index].Close - btc.Candles[start].Close) / btc.Candles[start].Close;
        }

        var btcSign = ReturnSign(btc, close);
        var ethSign = ReturnSign(eth, close);
        var bnbSign = ReturnSign(bnb, close);
        if (btcSign != 0m && btcSign == ethSign && ethSign == bnbSign)
        {
            values["coins_same_sign"] = 1m;
        }
        else if (btcSign != 0m && ethSign != 0m && bnbSign != 0m)
        {
            values["coins_same_sign"] = 0m;
        }
    }

    private static decimal ReturnSign(CausalIndicatorCache series, DateTimeOffset close)
    {
        var index = EdgeFeatureBuilder.LastClosed(series, close);
        if (index < 1 || series.Candles[index - 1].Close <= 0m)
        {
            return 0m;
        }

        var change = series.Candles[index].Close - series.Candles[index - 1].Close;
        return change > 0m ? 1m : change < 0m ? -1m : 0m;
    }
}

public static class SignalQualityMath
{
    public static decimal? Spearman(IReadOnlyList<decimal> xs, IReadOnlyList<decimal> ys)
    {
        if (xs.Count != ys.Count || xs.Count < SignalQualityCatalog.MinimumBucketTrades)
        {
            return null;
        }

        var rx = Ranks(xs);
        var ry = Ranks(ys);
        double num = 0;
        double dx = 0;
        double dy = 0;
        var mx = rx.Average();
        var my = ry.Average();
        for (var i = 0; i < rx.Length; i++)
        {
            var a = rx[i] - mx;
            var b = ry[i] - my;
            num += a * b;
            dx += a * a;
            dy += b * b;
        }

        if (dx <= 0 || dy <= 0)
        {
            return 0m;
        }

        return (decimal)(num / Math.Sqrt(dx * dy));
    }

    public static (decimal Low, decimal High) TertileCuts(IReadOnlyList<decimal> values)
    {
        var ordered = values.OrderBy(value => value).ToList();
        return (Quantile(ordered, 1m / 3m), Quantile(ordered, 2m / 3m));
    }

    public static decimal Quantile(IReadOnlyList<decimal> sorted, decimal p)
    {
        if (sorted.Count == 0)
        {
            return 0m;
        }

        var index = (int)Math.Round((sorted.Count - 1) * (double)p, MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static double[] Ranks(IReadOnlyList<decimal> values)
    {
        var order = values.Select((value, index) => (value, index)).OrderBy(row => row.value).ToArray();
        var ranks = new double[values.Count];
        var cursor = 0;
        while (cursor < order.Length)
        {
            var end = cursor;
            while (end + 1 < order.Length && order[end + 1].value == order[cursor].value)
            {
                end++;
            }

            var rank = (cursor + end) / 2d + 1d;
            for (var i = cursor; i <= end; i++)
            {
                ranks[order[i].index] = rank;
            }

            cursor = end + 1;
        }

        return ranks;
    }
}
