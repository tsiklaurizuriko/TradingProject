namespace TradingPlatform.Strategies.Indicators;

/// <summary>
/// Causal futures features. Index i uses observations 0..i only. Null stays null; no gap fill.
/// </summary>
public static class FuturesAlphaFeatures
{
    public static decimal? ChangeAt(IReadOnlyList<decimal?> series, int i, int lookback)
    {
        if (i < lookback || series[i] is not { } now || series[i - lookback] is not { } prev || prev == 0m)
        {
            return null;
        }

        return (now - prev) / prev;
    }

    public static decimal? PriceChangeAt(IReadOnlyList<Domain.Market.MarketCandle> candles, int i, int lookback)
    {
        if (i < lookback || candles[i].Close == 0m || candles[i - lookback].Close == 0m)
        {
            return null;
        }

        return (candles[i].Close - candles[i - lookback].Close) / candles[i - lookback].Close;
    }

    public static decimal? ZScoreAt(IReadOnlyList<decimal?> series, int i, int lookback)
    {
        lookback = Math.Max(8, lookback);
        if (i < 0 || i >= series.Count || series[i] is not { } current)
        {
            return null;
        }

        var start = Math.Max(0, i - lookback + 1);
        decimal sum = 0m;
        decimal sumSq = 0m;
        var n = 0;
        for (var j = start; j <= i; j++)
        {
            if (series[j] is not { } value)
            {
                continue;
            }

            sum += value;
            sumSq += value * value;
            n++;
        }

        if (n < 8)
        {
            return null;
        }

        var mean = sum / n;
        var var = sumSq / n - mean * mean;
        if (var <= 0m)
        {
            return 0m;
        }

        return (current - mean) / (decimal)Math.Sqrt((double)var);
    }

    public static decimal? PercentileAt(IReadOnlyList<decimal?> series, int i, int lookback)
    {
        lookback = Math.Max(8, lookback);
        if (i < 0 || i >= series.Count || series[i] is not { } current)
        {
            return null;
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

        return count == 0 ? null : (decimal)below / count;
    }

    public static decimal? CumulativeAt(IReadOnlyList<decimal?> series, int i, int lookback)
    {
        lookback = Math.Max(1, lookback);
        if (i < 0 || i >= series.Count)
        {
            return null;
        }

        var start = Math.Max(0, i - lookback + 1);
        decimal sum = 0m;
        var n = 0;
        for (var j = start; j <= i; j++)
        {
            if (series[j] is not { } value)
            {
                continue;
            }

            sum += value;
            n++;
        }

        return n == 0 ? null : sum;
    }

    public static decimal? MeanAt(IReadOnlyList<decimal?> series, int i, int lookback)
    {
        var sum = CumulativeAt(series, i, lookback);
        if (sum is null)
        {
            return null;
        }

        lookback = Math.Max(1, lookback);
        var start = Math.Max(0, i - lookback + 1);
        var n = 0;
        for (var j = start; j <= i; j++)
        {
            if (series[j] is not null)
            {
                n++;
            }
        }

        return n == 0 ? null : sum / n;
    }
}
