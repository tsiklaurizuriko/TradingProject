using TradingPlatform.Domain.Market;

namespace TradingPlatform.Research;

public sealed record Wave3Bar(
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal TakerBuyVolume);

/// <summary>
/// Timestamp-aligned panel. A row exists only when every listed coin has a closed bar at that OpenTime.
/// No interpolation. Future bars are never required to form a row.
/// </summary>
public sealed class Wave3Panel
{
    public required IReadOnlyList<string> Symbols { get; init; }
    public required IReadOnlyList<DateTimeOffset> OpenTimes { get; init; }
    public required Wave3Bar[,] Bars { get; init; }

    public int Length => OpenTimes.Count;
    public int Width => Symbols.Count;

    public int IndexOf(string symbol)
    {
        for (var i = 0; i < Symbols.Count; i++)
        {
            if (string.Equals(Symbols[i], symbol, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public static Wave3Panel Align(
        IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> series,
        IReadOnlyList<string> symbols)
    {
        var maps = new List<Dictionary<DateTimeOffset, Wave3Bar>>();
        foreach (var symbol in symbols)
        {
            if (!series.TryGetValue(symbol, out var candles))
            {
                throw new InvalidOperationException($"Missing OHLCV for {symbol}.");
            }

            var map = new Dictionary<DateTimeOffset, Wave3Bar>();
            foreach (var c in candles.Where(x => x.IsClosed).OrderBy(x => x.OpenTime))
            {
                map[c.OpenTime] = new Wave3Bar(
                    c.OpenTime,
                    c.CloseTime,
                    c.Open,
                    c.High,
                    c.Low,
                    c.Close,
                    c.Volume,
                    c.TakerBuyVolume);
            }

            maps.Add(map);
        }

        var times = maps[0].Keys.ToHashSet();
        for (var i = 1; i < maps.Count; i++)
        {
            times.IntersectWith(maps[i].Keys);
        }

        var ordered = times.OrderBy(t => t).ToList();
        var bars = new Wave3Bar[ordered.Count, symbols.Count];
        for (var t = 0; t < ordered.Count; t++)
        {
            for (var s = 0; s < symbols.Count; s++)
            {
                bars[t, s] = maps[s][ordered[t]];
            }
        }

        return new Wave3Panel { Symbols = symbols, OpenTimes = ordered, Bars = bars };
    }
}

public static class Wave3Math
{
    public static double[] HourlyReturns(Wave3Panel panel, int symbolIndex)
    {
        var n = panel.Length;
        var r = new double[n];
        r[0] = double.NaN;
        for (var t = 1; t < n; t++)
        {
            var prev = panel.Bars[t - 1, symbolIndex].Close;
            var now = panel.Bars[t, symbolIndex].Close;
            r[t] = prev <= 0m ? double.NaN : (double)((now - prev) / prev);
        }

        return r;
    }

    public static double LookbackReturn(Wave3Panel panel, int t, int symbolIndex, int bars)
    {
        if (t < bars || bars <= 0)
        {
            return double.NaN;
        }

        var prev = panel.Bars[t - bars, symbolIndex].Close;
        var now = panel.Bars[t, symbolIndex].Close;
        return prev <= 0m ? double.NaN : (double)((now - prev) / prev);
    }

    public static double ForwardCloseReturn(Wave3Panel panel, int t, int symbolIndex, int horizon)
    {
        var j = t + horizon;
        if (j >= panel.Length || t < 0)
        {
            return double.NaN;
        }

        var now = panel.Bars[t, symbolIndex].Close;
        var fut = panel.Bars[j, symbolIndex].Close;
        return now <= 0m ? double.NaN : (double)((fut - now) / now);
    }

    public static double ForwardExecReturn(Wave3Panel panel, int t, int symbolIndex, int horizon)
    {
        var fill = t + 1;
        var j = t + horizon;
        if (fill >= panel.Length || j >= panel.Length)
        {
            return double.NaN;
        }

        var open = panel.Bars[fill, symbolIndex].Open;
        var fut = panel.Bars[j, symbolIndex].Close;
        return open <= 0m ? double.NaN : (double)((fut - open) / open);
    }

    public static double AtrPercent(Wave3Panel panel, int t, int symbolIndex, int period)
    {
        if (t < period || period < 2)
        {
            return double.NaN;
        }

        double sum = 0;
        for (var i = t - period + 1; i <= t; i++)
        {
            var bar = panel.Bars[i, symbolIndex];
            var prevClose = panel.Bars[i - 1, symbolIndex].Close;
            var tr = (double)Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - prevClose), Math.Abs(bar.Low - prevClose)));
            sum += tr;
        }

        var close = (double)panel.Bars[t, symbolIndex].Close;
        return close <= 0 ? double.NaN : (sum / period) / close;
    }

    public static double VolumeRatio(Wave3Panel panel, int t, int symbolIndex, int lookback)
    {
        if (t < lookback || lookback < 2)
        {
            return double.NaN;
        }

        decimal sum = 0m;
        for (var i = t - lookback; i < t; i++)
        {
            sum += panel.Bars[i, symbolIndex].Volume;
        }

        var mean = sum / lookback;
        var vol = panel.Bars[t, symbolIndex].Volume;
        return mean <= 0m ? double.NaN : (double)(vol / mean);
    }

    public static double TakerImbalance(Wave3Bar bar)
    {
        if (bar.TakerBuyVolume <= 0m || bar.Volume <= 0m || bar.TakerBuyVolume > bar.Volume)
        {
            return double.NaN;
        }

        return (double)(2m * bar.TakerBuyVolume / bar.Volume - 1m);
    }

    public static double Beta(double[] y, double[] x, int fromInclusive, int toInclusive)
    {
        var n = 0;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        var lo = Math.Max(0, fromInclusive);
        var hi = Math.Min(y.Length, toInclusive + 1);
        for (var i = lo; i < hi; i++)
        {
            if (double.IsNaN(y[i]) || double.IsNaN(x[i]))
            {
                continue;
            }

            n++;
            sx += x[i];
            sy += y[i];
            sxx += x[i] * x[i];
            sxy += x[i] * y[i];
        }

        if (n < 20)
        {
            return double.NaN;
        }

        var den = n * sxx - sx * sx;
        return Math.Abs(den) < 1e-18 ? double.NaN : (n * sxy - sx * sy) / den;
    }

    public static double[] PercentileRanks(double[] values)
    {
        var idx = Enumerable.Range(0, values.Length).Where(i => !double.IsNaN(values[i])).ToArray();
        var ranks = new double[values.Length];
        Array.Fill(ranks, double.NaN);
        if (idx.Length == 0)
        {
            return ranks;
        }

        Array.Sort(idx, (a, b) => values[a].CompareTo(values[b]));
        for (var r = 0; r < idx.Length; r++)
        {
            ranks[idx[r]] = idx.Length == 1 ? 0.5 : r / (double)(idx.Length - 1);
        }

        return ranks;
    }

    public static double Spearman(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        var n = Math.Min(x.Count, y.Count);
        for (var i = 0; i < n; i++)
        {
            if (double.IsNaN(x[i]) || double.IsNaN(y[i]))
            {
                continue;
            }

            xs.Add(x[i]);
            ys.Add(y[i]);
        }

        if (xs.Count < 8)
        {
            return double.NaN;
        }

        var rx = PercentileRanks(xs.ToArray());
        var ry = PercentileRanks(ys.ToArray());
        return Pearson(rx, ry);
    }

    public static double Pearson(double[] x, double[] y)
    {
        var n = 0;
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        for (var i = 0; i < x.Length; i++)
        {
            if (double.IsNaN(x[i]) || double.IsNaN(y[i]))
            {
                continue;
            }

            n++;
            sx += x[i];
            sy += y[i];
            sxx += x[i] * x[i];
            syy += y[i] * y[i];
            sxy += x[i] * y[i];
        }

        if (n < 8)
        {
            return double.NaN;
        }

        var num = n * sxy - sx * sy;
        var den = Math.Sqrt((n * sxx - sx * sx) * (n * syy - sy * sy));
        return den < 1e-18 ? double.NaN : num / den;
    }

    public static double Mean(IEnumerable<double> values)
    {
        var n = 0;
        double s = 0;
        foreach (var v in values)
        {
            if (double.IsNaN(v))
            {
                continue;
            }

            n++;
            s += v;
        }

        return n == 0 ? double.NaN : s / n;
    }

    public static double Median(IEnumerable<double> values)
    {
        var xs = values.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
        if (xs.Count == 0)
        {
            return double.NaN;
        }

        var mid = xs.Count / 2;
        return xs.Count % 2 == 0 ? (xs[mid - 1] + xs[mid]) / 2.0 : xs[mid];
    }

    public static double HitRate(IEnumerable<double> values)
    {
        var n = 0;
        var pos = 0;
        foreach (var v in values)
        {
            if (double.IsNaN(v))
            {
                continue;
            }

            n++;
            if (v > 0)
            {
                pos++;
            }
        }

        return n == 0 ? double.NaN : pos / (double)n;
    }
}
