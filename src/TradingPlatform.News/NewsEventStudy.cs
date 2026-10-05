using TradingPlatform.Domain.Market;

namespace TradingPlatform.News;

public sealed record NewsStudyHorizon(string Label, TimeSpan Span);

public sealed record NewsStudySummary(
    string Group,
    string Horizon,
    int Count,
    double? MeanPercent,
    double? MedianPercent,
    double? HitRate,
    double? TStat,
    double? MeanAfterCostPercent);

public static class NewsEventStudy
{
    /// <summary>Taker fee 0.05% and 0.02% slippage on each side.</summary>
    public const double RoundTripCostPercent = 0.14;

    public static readonly IReadOnlyList<NewsStudyHorizon> Horizons =
    [
        new("15m", TimeSpan.FromMinutes(15)),
        new("1h", TimeSpan.FromHours(1)),
        new("4h", TimeSpan.FromHours(4))
    ];

    /// <summary>
    /// The first bar close at or after the moment the platform saw the story. A decision can never use a bar that
    /// closes after the decision, so this is the earliest point where both the news and a closed candle exist.
    /// </summary>
    public static DateTimeOffset? DecisionTime(IReadOnlyList<MarketCandle> bars, DateTimeOffset firstSeenUtc)
    {
        var index = FirstCloseAtOrAfter(bars, firstSeenUtc);
        return index < 0 ? null : bars[index].CloseTime;
    }

    /// <summary>
    /// Percent move from the last close at or before <paramref name="decisionUtc"/> to the first close at or after
    /// decision + horizon, signed so that a correct call is positive. Null when either side is missing or the
    /// candle that would be used is more than one bar away from the target time.
    /// </summary>
    public static double? SignedForwardPercent(
        IReadOnlyList<MarketCandle> bars,
        DateTimeOffset decisionUtc,
        TimeSpan horizon,
        bool bullish)
    {
        if (bars.Count < 2)
        {
            return null;
        }

        var bar = bars[1].OpenTime - bars[0].OpenTime;
        var exitIndex = FirstCloseAtOrAfter(bars, decisionUtc + horizon);
        var entryIndex = LastCloseAtOrBefore(bars, decisionUtc);
        if (entryIndex < 0 || exitIndex < 0 || exitIndex <= entryIndex)
        {
            return null;
        }

        var entry = bars[entryIndex];
        var exit = bars[exitIndex];
        if (decisionUtc - entry.CloseTime > bar || exit.CloseTime - (decisionUtc + horizon) > bar || entry.Close <= 0m)
        {
            return null;
        }

        var move = (double)((exit.Close / entry.Close - 1m) * 100m);
        return bullish ? move : -move;
    }

    /// <summary>
    /// Rebuilds a clustered event from only the articles that had been retrieved when its first copy arrived, so
    /// the direction and scores are the ones the live pipeline could have seen at that moment.
    /// </summary>
    public static NewsEvent? AtFirstSighting(NewsEvent clustered, IReadOnlyList<RawNewsItem> raw, NewsPipeline pipeline)
    {
        var ids = clustered.OriginalArticles.Select(article => article.Id).ToHashSet(StringComparer.Ordinal);
        var members = raw.Where(item => ids.Contains(item.Id)).ToList();
        if (members.Count == 0)
        {
            return null;
        }

        var firstSeen = members.Min(item => item.RetrievedAtUtc);
        var visible = members.Where(item => item.RetrievedAtUtc <= firstSeen).ToList();
        return pipeline.Build(visible).OrderBy(item => item.PublishedAtUtc).FirstOrDefault();
    }

    public static NewsStudySummary Summarize(string group, string horizon, IEnumerable<double> signedPercents)
    {
        var values = signedPercents.ToList();
        if (values.Count == 0)
        {
            return new NewsStudySummary(group, horizon, 0, null, null, null, null, null);
        }

        var mean = values.Average();
        var sorted = values.Order().ToList();
        var median = sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2d;
        double? t = null;
        if (values.Count > 1)
        {
            var variance = values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1);
            t = variance > 0 ? mean / Math.Sqrt(variance / values.Count) : null;
        }

        return new NewsStudySummary(
            group,
            horizon,
            values.Count,
            mean,
            median,
            values.Count(value => value > 0) / (double)values.Count,
            t,
            mean - RoundTripCostPercent);
    }

    private static int FirstCloseAtOrAfter(IReadOnlyList<MarketCandle> bars, DateTimeOffset time)
    {
        int lo = 0, hi = bars.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (bars[mid].CloseTime >= time)
            {
                found = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return found;
    }

    private static int LastCloseAtOrBefore(IReadOnlyList<MarketCandle> bars, DateTimeOffset time)
    {
        int lo = 0, hi = bars.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (bars[mid].CloseTime <= time)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }
}
