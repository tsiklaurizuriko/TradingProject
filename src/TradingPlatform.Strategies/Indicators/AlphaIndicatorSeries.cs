using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.Indicators;

/// <summary>
/// Causal alpha research series. Value at index i uses candles 0..i only.
/// </summary>
public static class AlphaIndicatorSeries
{
    public static (decimal?[] Poc, decimal?[] Vah, decimal?[] Val) VolumeProfile(
        IReadOnlyList<MarketCandle> candles,
        int window,
        decimal valueAreaPercent,
        int bins = 24)
    {
        window = Math.Max(8, window);
        bins = Math.Clamp(bins, 8, 64);
        var va = valueAreaPercent is > 0.4m and < 0.95m ? valueAreaPercent : 0.70m;
        var poc = new decimal?[candles.Count];
        var vah = new decimal?[candles.Count];
        var val = new decimal?[candles.Count];
        for (var i = window - 1; i < candles.Count; i++)
        {
            var start = i - window + 1;
            var lo = candles[start].Low;
            var hi = candles[start].High;
            decimal volSum = 0m;
            for (var j = start; j <= i; j++)
            {
                if (candles[j].Low < lo)
                {
                    lo = candles[j].Low;
                }

                if (candles[j].High > hi)
                {
                    hi = candles[j].High;
                }

                volSum += candles[j].Volume;
            }

            if (hi <= lo || volSum <= 0m)
            {
                continue;
            }

            var width = (hi - lo) / bins;
            if (width <= 0m)
            {
                continue;
            }

            var bucket = new decimal[bins];
            for (var j = start; j <= i; j++)
            {
                var typical = (candles[j].High + candles[j].Low + candles[j].Close) / 3m;
                var idx = (int)Math.Floor((typical - lo) / width);
                idx = Math.Clamp(idx, 0, bins - 1);
                bucket[idx] += candles[j].Volume;
            }

            var pocBin = 0;
            for (var b = 1; b < bins; b++)
            {
                if (bucket[b] > bucket[pocBin])
                {
                    pocBin = b;
                }
            }

            var target = volSum * va;
            decimal acc = bucket[pocBin];
            var left = pocBin;
            var right = pocBin;
            while (acc < target && (left > 0 || right < bins - 1))
            {
                var leftVol = left > 0 ? bucket[left - 1] : -1m;
                var rightVol = right < bins - 1 ? bucket[right + 1] : -1m;
                if (leftVol >= rightVol)
                {
                    left--;
                    acc += bucket[left];
                }
                else
                {
                    right++;
                    acc += bucket[right];
                }
            }

            poc[i] = lo + (pocBin + 0.5m) * width;
            val[i] = lo + left * width;
            vah[i] = lo + (right + 1) * width;
        }

        return (poc, vah, val);
    }

    /// <summary>
    /// Confirmed swing low at k is the min of [k-n, k+n] and is only written at index k+n.
    /// </summary>
    public static IReadOnlyList<decimal?> ConfirmedSwingLow(IReadOnlyList<MarketCandle> candles, int n)
    {
        n = Math.Max(2, n);
        var result = new decimal?[candles.Count];
        decimal? last = null;
        for (var i = 0; i < candles.Count; i++)
        {
            var k = i - n;
            if (k >= n)
            {
                var lo = candles[k].Low;
                var ok = true;
                for (var j = k - n; j <= k + n; j++)
                {
                    if (j == k)
                    {
                        continue;
                    }

                    if (candles[j].Low < lo)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    last = lo;
                }
            }

            result[i] = last;
        }

        return result;
    }

    public static IReadOnlyList<decimal?> ConfirmedSwingHigh(IReadOnlyList<MarketCandle> candles, int n)
    {
        n = Math.Max(2, n);
        var result = new decimal?[candles.Count];
        decimal? last = null;
        for (var i = 0; i < candles.Count; i++)
        {
            var k = i - n;
            if (k >= n)
            {
                var hi = candles[k].High;
                var ok = true;
                for (var j = k - n; j <= k + n; j++)
                {
                    if (j == k)
                    {
                        continue;
                    }

                    if (candles[j].High > hi)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    last = hi;
                }
            }

            result[i] = last;
        }

        return result;
    }

    /// <summary>1 = HH/HL bullish, -1 = LH/LL bearish, 0 = mixed/unknown. Uses only confirmed swings at i.</summary>
    public static IReadOnlyList<decimal?> StructureBias(IReadOnlyList<MarketCandle> candles, int n)
    {
        n = Math.Max(2, n);
        var highs = new List<(int I, decimal P)>();
        var lows = new List<(int I, decimal P)>();
        var result = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            var k = i - n;
            if (k >= n)
            {
                var lo = candles[k].Low;
                var hi = candles[k].High;
                var isLow = true;
                var isHigh = true;
                for (var j = k - n; j <= k + n; j++)
                {
                    if (j == k)
                    {
                        continue;
                    }

                    if (candles[j].Low < lo)
                    {
                        isLow = false;
                    }

                    if (candles[j].High > hi)
                    {
                        isHigh = false;
                    }
                }

                if (isLow)
                {
                    lows.Add((k, lo));
                }

                if (isHigh)
                {
                    highs.Add((k, hi));
                }
            }

            if (highs.Count >= 2 && lows.Count >= 2)
            {
                var hh = highs[^1].P > highs[^2].P;
                var hl = lows[^1].P > lows[^2].P;
                var lh = highs[^1].P < highs[^2].P;
                var ll = lows[^1].P < lows[^2].P;
                if (hh && hl)
                {
                    result[i] = 1m;
                }
                else if (lh && ll)
                {
                    result[i] = -1m;
                }
                else
                {
                    result[i] = 0m;
                }
            }
        }

        return result;
    }

    public static (decimal?[] Mid, decimal?[] Upper, decimal?[] Lower) Keltner(
        IReadOnlyList<MarketCandle> candles,
        int period,
        decimal multiplier)
    {
        period = Math.Max(2, period);
        multiplier = multiplier <= 0m ? 1.5m : multiplier;
        var ema = new EmaIndicator(period).Compute(candles);
        var atr = new AtrIndicator(period).Compute(candles);
        var mid = new decimal?[candles.Count];
        var upper = new decimal?[candles.Count];
        var lower = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            if (ema[i] is not { } m || atr[i] is not { } a)
            {
                continue;
            }

            mid[i] = m;
            upper[i] = m + a * multiplier;
            lower[i] = m - a * multiplier;
        }

        return (mid, upper, lower);
    }

    public static IReadOnlyList<decimal?> CloseZScore(IReadOnlyList<MarketCandle> candles, int period)
    {
        period = Math.Max(5, period);
        var result = new decimal?[candles.Count];
        for (var i = period - 1; i < candles.Count; i++)
        {
            decimal sum = 0m;
            for (var j = i - period + 1; j <= i; j++)
            {
                sum += candles[j].Close;
            }

            var mean = sum / period;
            decimal sq = 0m;
            for (var j = i - period + 1; j <= i; j++)
            {
                var d = candles[j].Close - mean;
                sq += d * d;
            }

            var std = (decimal)Math.Sqrt((double)(sq / period));
            if (std > 0m)
            {
                result[i] = (candles[i].Close - mean) / std;
            }
        }

        return result;
    }

    public static IReadOnlyList<decimal?> TakerImbalance(IReadOnlyList<MarketCandle> candles)
    {
        var result = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            result[i] = TakerFlow.Imbalance(candles[i]);
        }

        return result;
    }

    public static bool HasTakerData(IReadOnlyList<MarketCandle> candles)
    {
        for (var i = 0; i < candles.Count; i++)
        {
            if (candles[i].TakerBuyVolume > 0m)
            {
                return true;
            }
        }

        return false;
    }

    public static int LastCompletedHigherTimeframe(IReadOnlyList<MarketCandle> htf, DateTimeOffset signalCloseTime)
    {
        for (var i = htf.Count - 1; i >= 0; i--)
        {
            if (htf[i].IsClosed && htf[i].CloseTime <= signalCloseTime)
            {
                return i;
            }
        }

        return -1;
    }
}
