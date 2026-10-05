using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Market;

public sealed record CandleGap(DateTimeOffset ExpectedOpen, DateTimeOffset NextOpen, int MissingBars);

public sealed record CandleQuality(int Bars, int Duplicates, int MissingBars, IReadOnlyList<CandleGap> Gaps)
{
    public decimal MissingPercent => Bars + MissingBars == 0 ? 0m : Math.Round(MissingBars * 100m / (Bars + MissingBars), 4);
}

public static class KlineSeries
{
    /// <summary>Sorted by open time with one candle per open time (the later copy wins, as a re-fetched page is newer).</summary>
    public static IReadOnlyList<MarketCandle> Normalize(IEnumerable<MarketCandle> candles, out int duplicates)
    {
        var byOpen = new SortedDictionary<DateTimeOffset, MarketCandle>();
        var seen = 0;
        foreach (var candle in candles)
        {
            seen++;
            byOpen[candle.OpenTime] = candle;
        }

        duplicates = seen - byOpen.Count;
        return byOpen.Values.ToList();
    }

    /// <summary>Gaps between consecutive candles of a sorted, deduplicated series.</summary>
    public static CandleQuality Inspect(IReadOnlyList<MarketCandle> sorted, Timeframe timeframe, int duplicates = 0)
    {
        var step = timeframe.ToDuration();
        var gaps = new List<CandleGap>();
        var missing = 0;
        for (var i = 1; i < sorted.Count; i++)
        {
            var expected = sorted[i - 1].OpenTime + step;
            if (sorted[i].OpenTime <= expected)
            {
                continue;
            }

            var bars = (int)((sorted[i].OpenTime - expected).Ticks / step.Ticks);
            if (bars <= 0)
            {
                continue;
            }

            missing += bars;
            gaps.Add(new CandleGap(expected, sorted[i].OpenTime, bars));
        }

        return new CandleQuality(sorted.Count, duplicates, missing, gaps);
    }
}
