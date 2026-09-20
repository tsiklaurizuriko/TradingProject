using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.Indicators;

/// <summary>
/// Causal research indicators. Value at index i depends only on candles 0..i.
/// </summary>
public static class ResearchIndicatorSeries
{
    public static IReadOnlyList<decimal?> SessionVwap(IReadOnlyList<MarketCandle> candles)
    {
        var result = new decimal?[candles.Count];
        decimal cumPv = 0m;
        decimal cumV = 0m;
        DateTime? day = null;
        for (var i = 0; i < candles.Count; i++)
        {
            var bar = candles[i];
            var utcDay = bar.OpenTime.UtcDateTime.Date;
            if (day is null || utcDay != day)
            {
                day = utcDay;
                cumPv = 0m;
                cumV = 0m;
            }

            var typical = (bar.High + bar.Low + bar.Close) / 3m;
            cumPv += typical * bar.Volume;
            cumV += bar.Volume;
            if (cumV > 0m)
            {
                result[i] = cumPv / cumV;
            }
        }

        return result;
    }

    public static IReadOnlyList<decimal?> Adx(IReadOnlyList<MarketCandle> candles, int period)
    {
        period = period < 2 ? 14 : period;
        var result = new decimal?[candles.Count];
        if (candles.Count <= period * 2)
        {
            return result;
        }

        var tr = new decimal[candles.Count];
        var plusDm = new decimal[candles.Count];
        var minusDm = new decimal[candles.Count];
        for (var i = 1; i < candles.Count; i++)
        {
            var up = candles[i].High - candles[i - 1].High;
            var down = candles[i - 1].Low - candles[i].Low;
            plusDm[i] = up > down && up > 0m ? up : 0m;
            minusDm[i] = down > up && down > 0m ? down : 0m;
            var hl = candles[i].High - candles[i].Low;
            var hc = Math.Abs(candles[i].High - candles[i - 1].Close);
            var lc = Math.Abs(candles[i].Low - candles[i - 1].Close);
            tr[i] = Math.Max(hl, Math.Max(hc, lc));
        }

        decimal smTr = 0m, smPlus = 0m, smMinus = 0m;
        for (var i = 1; i <= period; i++)
        {
            smTr += tr[i];
            smPlus += plusDm[i];
            smMinus += minusDm[i];
        }

        var dx = new decimal?[candles.Count];
        void WriteDx(int i)
        {
            if (smTr <= 0m)
            {
                return;
            }

            var plusDi = 100m * smPlus / smTr;
            var minusDi = 100m * smMinus / smTr;
            var den = plusDi + minusDi;
            if (den > 0m)
            {
                dx[i] = 100m * Math.Abs(plusDi - minusDi) / den;
            }
        }

        WriteDx(period);
        for (var i = period + 1; i < candles.Count; i++)
        {
            smTr = smTr - smTr / period + tr[i];
            smPlus = smPlus - smPlus / period + plusDm[i];
            smMinus = smMinus - smMinus / period + minusDm[i];
            WriteDx(i);
        }

        decimal? adx = null;
        decimal dxSum = 0m;
        var dxCount = 0;
        for (var i = period; i < candles.Count; i++)
        {
            if (dx[i] is not { } value)
            {
                continue;
            }

            if (adx is null)
            {
                dxSum += value;
                dxCount++;
                if (dxCount == period)
                {
                    adx = dxSum / period;
                    result[i] = adx;
                }

                continue;
            }

            adx = (adx.Value * (period - 1) + value) / period;
            result[i] = adx;
        }

        return result;
    }

    public static IReadOnlyList<decimal?> SupertrendDirection(IReadOnlyList<MarketCandle> candles, int period, decimal multiplier) =>
        Supertrend(candles, period, multiplier).Direction;

    public static IReadOnlyList<decimal?> SupertrendLine(IReadOnlyList<MarketCandle> candles, int period, decimal multiplier) =>
        Supertrend(candles, period, multiplier).Line;

    public static (IReadOnlyList<decimal?> Direction, IReadOnlyList<decimal?> Line) Supertrend(
        IReadOnlyList<MarketCandle> candles,
        int period,
        decimal multiplier)
    {
        period = period < 2 ? 10 : period;
        multiplier = multiplier <= 0m ? 3m : multiplier;
        var atr = new AtrIndicator(period).Compute(candles);
        var dir = new decimal?[candles.Count];
        var line = new decimal?[candles.Count];
        decimal? finalUpper = null;
        decimal? finalLower = null;
        decimal trend = 1m;
        for (var i = 0; i < candles.Count; i++)
        {
            if (atr[i] is not { } a)
            {
                continue;
            }

            var hl2 = (candles[i].High + candles[i].Low) / 2m;
            var basicUpper = hl2 + multiplier * a;
            var basicLower = hl2 - multiplier * a;
            var upper = finalUpper is { } pu && basicUpper > pu && candles[i].Close <= pu ? pu : basicUpper;
            var lower = finalLower is { } pl && basicLower < pl && candles[i].Close >= pl ? pl : basicLower;
            if (finalUpper is { } prevUpper && candles[i].Close > prevUpper)
            {
                trend = 1m;
            }
            else if (finalLower is { } prevLower && candles[i].Close < prevLower)
            {
                trend = -1m;
            }

            finalUpper = upper;
            finalLower = lower;
            dir[i] = trend;
            line[i] = trend > 0m ? lower : upper;
        }

        return (dir, line);
    }

    public static IReadOnlyList<decimal?> AtrPercentile(IReadOnlyList<MarketCandle> candles, int period, int lookback)
    {
        lookback = Math.Max(5, lookback);
        var atr = new AtrIndicator(period).Compute(candles);
        var result = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            if (atr[i] is not { } current)
            {
                continue;
            }

            var start = Math.Max(0, i - lookback + 1);
            var count = 0;
            var below = 0;
            for (var j = start; j <= i; j++)
            {
                if (atr[j] is not { } value)
                {
                    continue;
                }

                count++;
                if (value <= current)
                {
                    below++;
                }
            }

            if (count > 0)
            {
                result[i] = (decimal)below / count;
            }
        }

        return result;
    }

    public static IReadOnlyList<decimal?> RelativeVolume(IReadOnlyList<MarketCandle> candles, int lookback)
    {
        lookback = Math.Max(2, lookback);
        var avg = new AverageVolumeIndicator(lookback).Compute(candles);
        var result = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            if (avg[i] is { } mean && mean > 0m)
            {
                result[i] = candles[i].Volume / mean;
            }
        }

        return result;
    }

    public static IReadOnlyList<decimal?> BollingerWidth(
        IReadOnlyList<MarketCandle> candles,
        int period,
        decimal stdDev)
    {
        var (mid, upper, lower) = BollingerSeries.Compute(candles, period, stdDev);
        var width = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            if (mid[i] is { } m && m != 0m && upper[i] is { } u && lower[i] is { } l)
            {
                width[i] = (u - l) / m;
            }
        }

        return width;
    }

    public static IReadOnlyList<decimal?> PercentileRank(IReadOnlyList<decimal?> series, int lookback)
    {
        lookback = Math.Max(5, lookback);
        var result = new decimal?[series.Count];
        for (var i = 0; i < series.Count; i++)
        {
            if (series[i] is not { } current)
            {
                continue;
            }

            var start = Math.Max(0, i - lookback + 1);
            var count = 0;
            var below = 0;
            for (var j = start; j <= i; j++)
            {
                if (series[j] is not { } value)
                {
                    continue;
                }

                count++;
                if (value <= current)
                {
                    below++;
                }
            }

            if (count > 0)
            {
                result[i] = (decimal)below / count;
            }
        }

        return result;
    }

    public static IReadOnlyList<decimal?> Sma(IReadOnlyList<decimal?> series, int period)
    {
        period = Math.Max(2, period);
        var result = new decimal?[series.Count];
        decimal sum = 0m;
        var window = new Queue<decimal>();
        for (var i = 0; i < series.Count; i++)
        {
            if (series[i] is { } value)
            {
                window.Enqueue(value);
                sum += value;
                if (window.Count > period)
                {
                    sum -= window.Dequeue();
                }
            }

            if (window.Count == period)
            {
                result[i] = sum / period;
            }
        }

        return result;
    }
}
