using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.Indicators;

/// <summary>
/// Causal scalping indicators. Value at index i depends only on candles 0..i.
/// </summary>
public static class ScalpingIndicatorSeries
{
    public static IReadOnlyList<decimal?> StochasticK(IReadOnlyList<MarketCandle> candles, int period)
    {
        period = period < 2 ? 14 : period;
        var result = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            if (i < period - 1)
            {
                continue;
            }

            var high = candles[i].High;
            var low = candles[i].Low;
            for (var j = i - period + 1; j < i; j++)
            {
                if (candles[j].High > high) high = candles[j].High;
                if (candles[j].Low < low) low = candles[j].Low;
            }

            var range = high - low;
            result[i] = range <= 0m ? 50m : 100m * (candles[i].Close - low) / range;
        }

        return result;
    }

    public static IReadOnlyList<decimal?> StochasticD(IReadOnlyList<decimal?> k, int smooth)
    {
        smooth = smooth < 1 ? 3 : smooth;
        var result = new decimal?[k.Count];
        for (var i = 0; i < k.Count; i++)
        {
            if (i < smooth - 1)
            {
                continue;
            }

            decimal sum = 0m;
            var n = 0;
            var ok = true;
            for (var j = i - smooth + 1; j <= i; j++)
            {
                if (k[j] is not { } v)
                {
                    ok = false;
                    break;
                }

                sum += v;
                n++;
            }

            if (ok && n > 0)
            {
                result[i] = sum / n;
            }
        }

        return result;
    }

    public static IReadOnlyList<decimal?> StochRsi(IReadOnlyList<decimal?> rsi, int period)
    {
        period = period < 2 ? 14 : period;
        var result = new decimal?[rsi.Count];
        for (var i = 0; i < rsi.Count; i++)
        {
            if (i < period - 1 || rsi[i] is not { } now)
            {
                continue;
            }

            decimal min = now;
            decimal max = now;
            var ok = true;
            for (var j = i - period + 1; j <= i; j++)
            {
                if (rsi[j] is not { } v)
                {
                    ok = false;
                    break;
                }

                if (v < min) min = v;
                if (v > max) max = v;
            }

            if (!ok)
            {
                continue;
            }

            result[i] = max <= min ? 50m : 100m * (now - min) / (max - min);
        }

        return result;
    }

    public static IReadOnlyList<decimal?> Wma(IReadOnlyList<decimal> values, int period)
    {
        period = period < 1 ? 1 : period;
        var result = new decimal?[values.Count];
        var denom = period * (period + 1) / 2m;
        for (var i = 0; i < values.Count; i++)
        {
            if (i < period - 1)
            {
                continue;
            }

            decimal sum = 0m;
            for (var w = 1; w <= period; w++)
            {
                sum += values[i - period + w] * w;
            }

            result[i] = sum / denom;
        }

        return result;
    }

    public static IReadOnlyList<decimal?> Hma(IReadOnlyList<MarketCandle> candles, int period)
    {
        period = period < 2 ? 9 : period;
        var closes = CandleSeries.Closes(candles);
        var half = Math.Max(1, period / 2);
        var sqrt = Math.Max(1, (int)Math.Round(Math.Sqrt(period)));
        var wmaHalf = Wma(closes, half);
        var wmaFull = Wma(closes, period);
        var raw = new decimal[closes.Length];
        for (var i = 0; i < closes.Length; i++)
        {
            raw[i] = (wmaHalf[i] is { } a && wmaFull[i] is { } b) ? 2m * a - b : closes[i];
        }

        return Wma(raw, sqrt);
    }

    public static (IReadOnlyList<decimal?> Up, IReadOnlyList<decimal?> Down) Aroon(IReadOnlyList<MarketCandle> candles, int period)
    {
        period = period < 2 ? 14 : period;
        var up = new decimal?[candles.Count];
        var down = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            if (i < period)
            {
                continue;
            }

            var highI = i;
            var lowI = i;
            for (var j = i - period; j <= i; j++)
            {
                if (candles[j].High >= candles[highI].High) highI = j;
                if (candles[j].Low <= candles[lowI].Low) lowI = j;
            }

            up[i] = 100m * (period - (i - highI)) / period;
            down[i] = 100m * (period - (i - lowI)) / period;
        }

        return (up, down);
    }

    public static IReadOnlyList<decimal?> WilliamsR(IReadOnlyList<MarketCandle> candles, int period)
    {
        period = period < 2 ? 14 : period;
        var result = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            if (i < period - 1)
            {
                continue;
            }

            var high = candles[i].High;
            var low = candles[i].Low;
            for (var j = i - period + 1; j < i; j++)
            {
                if (candles[j].High > high) high = candles[j].High;
                if (candles[j].Low < low) low = candles[j].Low;
            }

            var range = high - low;
            result[i] = range <= 0m ? -50m : -100m * (high - candles[i].Close) / range;
        }

        return result;
    }

    public static IReadOnlyList<decimal?> Mfi(IReadOnlyList<MarketCandle> candles, int period)
    {
        period = period < 2 ? 14 : period;
        var result = new decimal?[candles.Count];
        var typical = new decimal[candles.Count];
        var raw = new decimal[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            typical[i] = (candles[i].High + candles[i].Low + candles[i].Close) / 3m;
            if (i == 0)
            {
                continue;
            }

            var sign = typical[i] > typical[i - 1] ? 1m : typical[i] < typical[i - 1] ? -1m : 0m;
            raw[i] = sign * typical[i] * candles[i].Volume;
        }

        for (var i = period; i < candles.Count; i++)
        {
            decimal pos = 0m;
            decimal neg = 0m;
            for (var j = i - period + 1; j <= i; j++)
            {
                if (raw[j] > 0m) pos += raw[j];
                else if (raw[j] < 0m) neg -= raw[j];
            }

            result[i] = pos + neg <= 0m ? 50m : 100m * pos / (pos + neg);
        }

        return result;
    }

    public static IReadOnlyList<decimal?> Cmf(IReadOnlyList<MarketCandle> candles, int period)
    {
        period = period < 2 ? 20 : period;
        var result = new decimal?[candles.Count];
        var mfv = new decimal[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            var range = candles[i].High - candles[i].Low;
            var mfm = range <= 0m ? 0m : ((candles[i].Close - candles[i].Low) - (candles[i].High - candles[i].Close)) / range;
            mfv[i] = mfm * candles[i].Volume;
        }

        for (var i = period - 1; i < candles.Count; i++)
        {
            decimal flow = 0m;
            decimal vol = 0m;
            for (var j = i - period + 1; j <= i; j++)
            {
                flow += mfv[j];
                vol += candles[j].Volume;
            }

            result[i] = vol <= 0m ? 0m : flow / vol;
        }

        return result;
    }

    public static IReadOnlyList<decimal?> AwesomeOscillator(IReadOnlyList<MarketCandle> candles)
    {
        var mid = new decimal[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            mid[i] = (candles[i].High + candles[i].Low) / 2m;
        }

        var fast = Sma(mid, 5);
        var slow = Sma(mid, 34);
        var result = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            if (fast[i] is { } a && slow[i] is { } b)
            {
                result[i] = a - b;
            }
        }

        return result;
    }

    public static IReadOnlyList<decimal?> ParabolicSar(IReadOnlyList<MarketCandle> candles, decimal step = 0.02m, decimal max = 0.2m)
    {
        var result = new decimal?[candles.Count];
        if (candles.Count < 3)
        {
            return result;
        }

        var up = candles[1].Close >= candles[0].Close;
        var af = step;
        var ep = up ? candles[1].High : candles[1].Low;
        var sar = up ? candles[0].Low : candles[0].High;
        result[1] = sar;
        for (var i = 2; i < candles.Count; i++)
        {
            sar += af * (ep - sar);
            if (up)
            {
                sar = Math.Min(sar, Math.Min(candles[i - 1].Low, candles[i - 2].Low));
                if (candles[i].Low < sar)
                {
                    up = false;
                    sar = ep;
                    ep = candles[i].Low;
                    af = step;
                }
                else if (candles[i].High > ep)
                {
                    ep = candles[i].High;
                    af = Math.Min(max, af + step);
                }
            }
            else
            {
                sar = Math.Max(sar, Math.Max(candles[i - 1].High, candles[i - 2].High));
                if (candles[i].High > sar)
                {
                    up = true;
                    sar = ep;
                    ep = candles[i].High;
                    af = step;
                }
                else if (candles[i].Low < ep)
                {
                    ep = candles[i].Low;
                    af = Math.Min(max, af + step);
                }
            }

            result[i] = sar;
        }

        return result;
    }

    public static (IReadOnlyList<decimal?> High, IReadOnlyList<decimal?> Low) SessionHighLow(IReadOnlyList<MarketCandle> candles)
    {
        var high = new decimal?[candles.Count];
        var low = new decimal?[candles.Count];
        DateTime? day = null;
        decimal sessionHigh = 0m;
        decimal sessionLow = 0m;
        for (var i = 0; i < candles.Count; i++)
        {
            var utcDay = candles[i].OpenTime.UtcDateTime.Date;
            if (day is null || utcDay != day)
            {
                day = utcDay;
                sessionHigh = candles[i].High;
                sessionLow = candles[i].Low;
            }
            else
            {
                if (candles[i].High > sessionHigh) sessionHigh = candles[i].High;
                if (candles[i].Low < sessionLow) sessionLow = candles[i].Low;
            }

            high[i] = sessionHigh;
            low[i] = sessionLow;
        }

        return (high, low);
    }

    public static int UtcHour(MarketCandle candle) => candle.OpenTime.UtcDateTime.Hour;

    private static IReadOnlyList<decimal?> Sma(IReadOnlyList<decimal> values, int period)
    {
        var result = new decimal?[values.Count];
        decimal sum = 0m;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= period)
            {
                sum -= values[i - period];
            }

            if (i >= period - 1)
            {
                result[i] = sum / period;
            }
        }

        return result;
    }
}
