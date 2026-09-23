using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.PriceAction;

/// <summary>Causal per-bar candle geometry. Uses only that bar's OHLC.</summary>
public readonly record struct CandleGeom(
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Range,
    decimal Body,
    decimal UpperWick,
    decimal LowerWick,
    decimal BodyRange,
    decimal UpperWickBody,
    decimal LowerWickBody,
    bool Bullish,
    bool Bearish)
{
    public static CandleGeom From(MarketCandle c)
    {
        var range = Math.Max(c.High - c.Low, 0.00000001m);
        var body = Math.Abs(c.Close - c.Open);
        var upper = c.High - Math.Max(c.Open, c.Close);
        var lower = Math.Min(c.Open, c.Close) - c.Low;
        var bodySafe = Math.Max(body, 0.00000001m);
        return new(
            c.Open,
            c.High,
            c.Low,
            c.Close,
            c.Volume,
            range,
            body,
            upper,
            lower,
            body / range,
            upper / bodySafe,
            lower / bodySafe,
            c.Close > c.Open,
            c.Close < c.Open);
    }

    public static CandleGeom[] Series(IReadOnlyList<MarketCandle> candles)
    {
        var result = new CandleGeom[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            result[i] = From(candles[i]);
        }

        return result;
    }

    public bool Inside(in CandleGeom prev) => High < prev.High && Low > prev.Low;

    public bool Outside(in CandleGeom prev) => High > prev.High && Low < prev.Low;

    public bool Expansion(decimal medianRange) => Range > medianRange * 1.5m;

    public bool Compression(decimal medianRange) => Range < medianRange * 0.6m;

    public decimal Efficiency => Range <= 0 ? 0 : Body / Range;
}

public static class PriceActionMath
{
    public static decimal MedianRange(CandleGeom[] geoms, int i, int lookback)
    {
        lookback = Math.Max(2, lookback);
        var from = Math.Max(0, i - lookback + 1);
        var n = i - from + 1;
        if (n <= 0)
        {
            return 0m;
        }

        var buf = new decimal[n];
        for (var k = 0; k < n; k++)
        {
            buf[k] = geoms[from + k].Range;
        }

        Array.Sort(buf);
        return buf[n / 2];
    }

    public static decimal RangePercentile(CandleGeom[] geoms, int i, int lookback)
    {
        lookback = Math.Max(2, lookback);
        var from = Math.Max(0, i - lookback + 1);
        var n = i - from + 1;
        if (n <= 1)
        {
            return 0.5m;
        }

        var cur = geoms[i].Range;
        var below = 0;
        for (var k = from; k <= i; k++)
        {
            if (geoms[k].Range <= cur)
            {
                below++;
            }
        }

        return (decimal)below / n;
    }

    public static int Consecutive(CandleGeom[] geoms, int i, bool bullish)
    {
        var n = 0;
        for (var k = i; k >= 0; k--)
        {
            if (bullish ? !geoms[k].Bullish : !geoms[k].Bearish)
            {
                break;
            }

            n++;
        }

        return n;
    }

    public static decimal RollingHigh(IReadOnlyList<MarketCandle> candles, int i, int n)
    {
        var from = Math.Max(0, i - n + 1);
        var hi = candles[from].High;
        for (var k = from + 1; k <= i; k++)
        {
            if (candles[k].High > hi)
            {
                hi = candles[k].High;
            }
        }

        return hi;
    }

    public static decimal RollingLow(IReadOnlyList<MarketCandle> candles, int i, int n)
    {
        var from = Math.Max(0, i - n + 1);
        var lo = candles[from].Low;
        for (var k = from + 1; k <= i; k++)
        {
            if (candles[k].Low < lo)
            {
                lo = candles[k].Low;
            }
        }

        return lo;
    }
}
