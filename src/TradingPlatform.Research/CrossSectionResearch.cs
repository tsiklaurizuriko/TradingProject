namespace TradingPlatform.Research;

/// <summary>
/// Pre-registered cross-sectional feasibility rules. Baskets are percentile ranks.
/// A cross-sectional z-score is the same order as the raw feature, so it is not a second test.
/// </summary>
public static class CrossSectionCatalog
{
    public const string Version = "cross-section-v1";
    public const int MinimumSymbols = 30;
    public const int MinimumWindow = 200;
    public const int MinimumBlock = 100;
    public const int MinimumRegime = 100;
    public const double MinimumAbsSpread = 0.0001;
    public const double OneWayCost = 0.0006;
    public const double RoundTripPerLeg = 0.0012;
    public const int HistoryBars = 96;
    public const int VolumeLookback = 20;
    public const int AtrLookback = 14;
    public const int RegimeLookback = 96 * 30;
    public const int ParticipationMinimumNames = 20;
    public const double MaximumNameShare = 0.40;
    public const string Clock = "15m";

    public const string RepeatableRule =
        "Fixed before aggregation. The primary basket is the top decile minus the bottom decile of the cross-sectional percentile rank. " +
        "A window passes when it has at least 200 timestamps, the mean and the median spread share a sign, and the absolute mean spread is at least 1 basis point. " +
        "A horizon is stable when in-sample, validation, and out-of-sample pass with that same sign and at least 3 of 4 chronological blocks agree. " +
        "REPEATABLE requires at least 3 of the 5 pre-registered horizons to be stable with one shared sign, both BTC volatility regimes to share that sign on those horizons, " +
        "at least 20 distinct symbols in the out-of-sample baskets, and no single symbol in more than 40 percent of out-of-sample top-decile timestamps. " +
        "UNIVERSE_SPECIFIC: the horizons are stable but participation fails. " +
        "OOS_ONLY: out-of-sample passes on at least 3 horizons with one sign and in-sample does not share it. " +
        "UNSTABLE: windows or blocks or regimes disagree. " +
        "NO_EVIDENCE: otherwise. Quintiles are reported and are not used to choose the label. None of these labels is an edge.";

    public static readonly string[] Features =
    [
        "return_15m",
        "return_1h",
        "return_4h",
        "return_12h",
        "return_24h",
        "return_1h_over_atr",
        "return_4h_over_vol",
        "return_24h_over_vol",
        "relative_volume",
        "volume_acceleration"
    ];

    public static readonly string[] MicrostructureNotScored =
    [
        "oi_change",
        "oi_percentile",
        "funding_percentile",
        "taker_imbalance",
        "basis_percentile",
        "depth_imbalance"
    ];

    public static readonly (string Name, int Bars)[] Horizons =
    [
        ("15m", 1),
        ("1h", 4),
        ("4h", 16),
        ("12h", 48),
        ("24h", 96)
    ];

    public static readonly string[] ExcludedBases = ["USDC", "FDUSD", "TUSD", "DAI", "USDE", "BUSD", "USDP", "EUR", "USD"];
}

public static class CrossSectionMath
{
    public static bool AcceptedName(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !symbol.EndsWith("USDT", StringComparison.Ordinal))
        {
            return false;
        }

        if (symbol.Equals("BTCDOMUSDT", StringComparison.Ordinal))
        {
            return false;
        }

        var @base = symbol[..^4];
        return !CrossSectionCatalog.ExcludedBases.Contains(@base, StringComparer.Ordinal);
    }

    public static int PassSign(int count, double mean, double median, int minimum)
    {
        if (count < minimum || double.IsNaN(mean) || double.IsNaN(median))
        {
            return 0;
        }

        var meanSign = Math.Sign(mean);
        if (meanSign == 0 || meanSign != Math.Sign(median) || Math.Abs(mean) < CrossSectionCatalog.MinimumAbsSpread)
        {
            return 0;
        }

        return meanSign;
    }

    public static string Classify(
        IReadOnlyList<int> inSampleSigns,
        IReadOnlyList<int> validationSigns,
        IReadOnlyList<int> outOfSampleSigns,
        IReadOnlyList<int> agreeingBlocks,
        IReadOnlyList<int> highVolSigns,
        IReadOnlyList<int> lowVolSigns,
        int distinctNames,
        double maxNameShare)
    {
        var count = inSampleSigns.Count;
        var stablePositive = 0;
        var stableNegative = 0;
        for (var i = 0; i < count; i++)
        {
            if (agreeingBlocks[i] < 3)
            {
                continue;
            }

            if (inSampleSigns[i] > 0 && validationSigns[i] > 0 && outOfSampleSigns[i] > 0)
            {
                stablePositive++;
            }

            if (inSampleSigns[i] < 0 && validationSigns[i] < 0 && outOfSampleSigns[i] < 0)
            {
                stableNegative++;
            }
        }

        var sign = stablePositive >= 3 ? 1 : stableNegative >= 3 ? -1 : 0;
        var participation = distinctNames >= CrossSectionCatalog.ParticipationMinimumNames && maxNameShare <= CrossSectionCatalog.MaximumNameShare;
        if (sign != 0 && participation && RegimesAgree(sign, inSampleSigns, validationSigns, outOfSampleSigns, agreeingBlocks, highVolSigns, lowVolSigns))
        {
            return "REPEATABLE";
        }

        if (sign != 0 && !participation)
        {
            return "UNIVERSE_SPECIFIC";
        }

        var oosPositive = outOfSampleSigns.Count(value => value > 0);
        var oosNegative = outOfSampleSigns.Count(value => value < 0);
        var oosSign = oosPositive >= 3 ? 1 : oosNegative >= 3 ? -1 : 0;
        if (oosSign != 0 && inSampleSigns.Count(value => value == oosSign) < 3)
        {
            return "OOS_ONLY";
        }

        if (sign != 0 || inSampleSigns.Any(value => value != 0) || outOfSampleSigns.Any(value => value != 0))
        {
            return "UNSTABLE";
        }

        return "NO_EVIDENCE";
    }

    public static (int Bottom, int Top) TailCount(int count)
    {
        var tail = Math.Max(1, count / 10);
        return (tail, tail);
    }

    public static (int Bottom, int Top) QuintileCount(int count)
    {
        var tail = Math.Max(1, count / 5);
        return (tail, tail);
    }

    public static void FillReturn(float[] close, int stride, int symbol, int times, int lookback, float[] destination)
    {
        for (var t = 0; t < times; t++)
        {
            var index = (t * stride) + symbol;
            if (t < lookback)
            {
                destination[index] = float.NaN;
                continue;
            }

            var now = close[index];
            var past = close[((t - lookback) * stride) + symbol];
            destination[index] = now > 0f && past > 0f ? (now / past) - 1f : float.NaN;
        }
    }

    public static void FillRelativeVolume(float[] volume, int stride, int symbol, int times, float[] destination)
    {
        var lookback = CrossSectionCatalog.VolumeLookback;
        for (var t = 0; t < times; t++)
        {
            var index = (t * stride) + symbol;
            if (t < lookback || float.IsNaN(volume[index]) || volume[index] < 0f)
            {
                destination[index] = float.NaN;
                continue;
            }

            double sum = 0;
            var ok = true;
            for (var k = 1; k <= lookback; k++)
            {
                var prior = volume[((t - k) * stride) + symbol];
                if (float.IsNaN(prior) || prior < 0f)
                {
                    ok = false;
                    break;
                }

                sum += prior;
            }

            destination[index] = ok && sum > 0d ? volume[index] / (float)(sum / lookback) : float.NaN;
        }
    }

    public static void FillAcceleration(float[] relativeVolume, int stride, int symbol, int times, float[] destination)
    {
        for (var t = 0; t < times; t++)
        {
            var index = (t * stride) + symbol;
            if (t < 4 || float.IsNaN(relativeVolume[index]) || float.IsNaN(relativeVolume[((t - 4) * stride) + symbol]))
            {
                destination[index] = float.NaN;
                continue;
            }

            destination[index] = relativeVolume[index] - relativeVolume[((t - 4) * stride) + symbol];
        }
    }

    public static void FillRealizedVol(float[] close, int stride, int symbol, int times, float[] destination)
    {
        var window = CrossSectionCatalog.HistoryBars;
        var ring = new double[window];
        double sum = 0;
        double sumSq = 0;
        var streak = 0;
        destination[symbol] = float.NaN;
        for (var t = 1; t < times; t++)
        {
            var index = (t * stride) + symbol;
            destination[index] = float.NaN;
            var now = close[index];
            var past = close[((t - 1) * stride) + symbol];
            if (!(now > 0f) || !(past > 0f) || float.IsNaN(now) || float.IsNaN(past))
            {
                sum = 0;
                sumSq = 0;
                streak = 0;
                continue;
            }

            var ret = (now / past) - 1d;
            if (streak >= window)
            {
                var old = ring[streak % window];
                sum -= old;
                sumSq -= old * old;
            }

            ring[streak % window] = ret;
            sum += ret;
            sumSq += ret * ret;
            streak++;
            if (streak < window)
            {
                continue;
            }

            var variance = (sumSq - (sum * sum / window)) / (window - 1);
            destination[index] = variance > 1e-16 ? (float)Math.Sqrt(variance) : float.NaN;
        }
    }

    public static void FillAtrRatio(float[] close, float[] high, float[] low, int stride, int symbol, int times, float[] destination)
    {
        for (var t = 0; t < times; t++)
        {
            destination[(t * stride) + symbol] = AtrRatio(close, high, low, stride, symbol, t, 4);
        }
    }

    public static void DivideBy(float[] numerator, float[] divisor, int length)
    {
        for (var i = 0; i < length; i++)
        {
            numerator[i] = !float.IsNaN(numerator[i]) && !float.IsNaN(divisor[i]) && divisor[i] > 0f
                ? numerator[i] / divisor[i]
                : float.NaN;
        }
    }

    public static float BtcVol(float[] close, int stride, int symbol, int t)
    {
        if (t < CrossSectionCatalog.HistoryBars)
        {
            return float.NaN;
        }

        double sum = 0;
        double sumSq = 0;
        var n = CrossSectionCatalog.HistoryBars;
        for (var k = 0; k < n; k++)
        {
            var now = close[((t - k) * stride) + symbol];
            var past = close[((t - k - 1) * stride) + symbol];
            if (now <= 0f || past <= 0f || float.IsNaN(now) || float.IsNaN(past))
            {
                return float.NaN;
            }

            var ret = (now / past) - 1d;
            sum += ret;
            sumSq += ret * ret;
        }

        var variance = (sumSq - (sum * sum / n)) / (n - 1);
        return variance > 0d ? (float)Math.Sqrt(variance) : float.NaN;
    }

    private static float AtrRatio(float[] close, float[] high, float[] low, int stride, int symbol, int t, int returnBars)
    {
        if (t < Math.Max(returnBars, CrossSectionCatalog.AtrLookback))
        {
            return float.NaN;
        }

        var now = close[(t * stride) + symbol];
        var past = close[((t - returnBars) * stride) + symbol];
        if (now <= 0f || past <= 0f)
        {
            return float.NaN;
        }

        double sum = 0;
        for (var k = 0; k < CrossSectionCatalog.AtrLookback; k++)
        {
            var bar = t - k;
            var h = high[(bar * stride) + symbol];
            var l = low[(bar * stride) + symbol];
            var prev = close[((bar - 1) * stride) + symbol];
            if (float.IsNaN(h) || float.IsNaN(l) || float.IsNaN(prev))
            {
                return float.NaN;
            }

            var tr = Math.Max(h - l, Math.Max(Math.Abs(h - prev), Math.Abs(l - prev)));
            sum += tr;
        }

        var atr = sum / CrossSectionCatalog.AtrLookback;
        return atr > 0d ? (float)(((now / past) - 1d) / (atr / now)) : float.NaN;
    }

    private static bool RegimesAgree(
        int sign,
        IReadOnlyList<int> inSample,
        IReadOnlyList<int> validation,
        IReadOnlyList<int> outOfSample,
        IReadOnlyList<int> blocks,
        IReadOnlyList<int> highVol,
        IReadOnlyList<int> lowVol)
    {
        var matched = 0;
        for (var i = 0; i < inSample.Count; i++)
        {
            if (blocks[i] >= 3
                && inSample[i] == sign
                && validation[i] == sign
                && outOfSample[i] == sign
                && highVol[i] == sign
                && lowVol[i] == sign)
            {
                matched++;
            }
        }

        return matched >= 3;
    }
}
