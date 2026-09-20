using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.Indicators;

/// <summary>
/// Aligns irregular futures observations to closed candles. An observation is visible at bar i
/// only when its timestamp is &lt;= that candle's CloseTime.
/// </summary>
public static class AlignedMarketSeries
{
    public static IReadOnlyList<decimal?> Align(
        IReadOnlyList<(DateTimeOffset Time, decimal Value)> observations,
        IReadOnlyList<MarketCandle> candles)
    {
        var result = new decimal?[candles.Count];
        if (observations.Count == 0 || candles.Count == 0)
        {
            return result;
        }

        var ordered = observations.OrderBy(o => o.Time).ToArray();
        var j = -1;
        decimal? last = null;
        for (var i = 0; i < candles.Count; i++)
        {
            var knownBy = candles[i].CloseTime;
            while (j + 1 < ordered.Length && ordered[j + 1].Time <= knownBy)
            {
                j++;
                last = ordered[j].Value;
            }

            result[i] = last;
        }

        return result;
    }

    public static decimal? Change(IReadOnlyList<decimal?> series, int index, int lookback)
    {
        if (index < lookback || series[index] is not { } now || series[index - lookback] is not { } prev || prev == 0m)
        {
            return null;
        }

        return (now - prev) / prev;
    }
}
