using System.Security.Cryptography;
using System.Text;

namespace TradingPlatform.News;

public static class NewsEventClusterer
{
    public static IReadOnlyList<NewsEvent> Cluster(IReadOnlyList<RawNewsItem> items, NewsOptions options)
    {
        var ordered = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .OrderBy(item => item.PublishedAtUtc)
            .ToList();
        var clusters = new List<List<RawNewsItem>>();
        foreach (var item in ordered)
        {
            var match = clusters.FirstOrDefault(cluster => SameEvent(cluster[0], item, options));
            if (match is null)
            {
                clusters.Add([item]);
            }
            else
            {
                match.Add(item);
            }
        }

        return clusters.Select(ToEvent).OrderBy(item => item.PublishedAtUtc).ToList();
    }

    public static void AssignNovelty(IReadOnlyList<NewsEvent> events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            var prior = false;
            for (var j = 0; j < i; j++)
            {
                var age = (events[i].PublishedAtUtc - events[j].PublishedAtUtc).TotalHours;
                var sameAssets = events[i].Assets.Count == 0
                    || events[i].Assets.Any(asset => events[j].Assets.Contains(asset, StringComparer.OrdinalIgnoreCase));
                if (age is >= 0 and <= 24 && sameAssets && events[i].EventType == events[j].EventType)
                {
                    prior = true;
                    break;
                }
            }

            events[i].NoveltyScore = prior ? 0.25 : 1;
        }
    }

    public static bool SameEvent(RawNewsItem left, RawNewsItem right, NewsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(left.OriginalSourceId)
            && string.Equals(left.Source, right.Source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.OriginalSourceId, right.OriginalSourceId, StringComparison.Ordinal))
        {
            return true;
        }

        var leftUrl = CanonicalUrl(left.SourceUrl);
        var rightUrl = CanonicalUrl(right.SourceUrl);
        if (leftUrl.Length > 0 && string.Equals(leftUrl, rightUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var gap = Math.Abs((left.PublishedAtUtc - right.PublishedAtUtc).TotalMinutes);
        if (gap > options.ClusterWindowMinutes)
        {
            return false;
        }

        var title = TitleSimilarity(left.Title, right.Title);
        if (title < options.TitleSimilarity)
        {
            return false;
        }

        var assets = AssetOverlap(left.RelatedAssets, right.RelatedAssets);
        return assets || left.RelatedAssets.Count == 0 || right.RelatedAssets.Count == 0;
    }

    public static string CanonicalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return host.ToLowerInvariant() + uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
    }

    public static string NormalizeTitle(string title)
    {
        var chars = title.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static double TitleSimilarity(string left, string right)
    {
        var a = NormalizeTitle(left).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var b = NormalizeTitle(right).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static bool AssetOverlap(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Any(asset => right.Contains(asset, StringComparer.OrdinalIgnoreCase));

    private static NewsEvent ToEvent(List<RawNewsItem> cluster)
    {
        var first = cluster.OrderBy(item => item.PublishedAtUtc).First();
        var sources = cluster.Select(item => item.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new NewsEvent
        {
            EventId = Hash(first),
            PublishedAtUtc = first.PublishedAtUtc,
            DetectedAtUtc = cluster.Max(item => item.RetrievedAtUtc),
            Assets = cluster.SelectMany(item => item.RelatedAssets).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ProviderAssetIds = cluster.SelectMany(item => item.RelatedProviderIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceCount = sources,
            TimestampPrecision = cluster.Any(item => item.TimestampPrecision == TimestampPrecision.DateOnly)
                ? TimestampPrecision.DateOnly
                : TimestampPrecision.Instant,
            Actual = first.Actual,
            Consensus = first.Consensus,
            Previous = first.Previous,
            Surprise = first.Actual is { } actual && first.Consensus is { } consensus ? actual - consensus : null,
            IsScheduled = first.IsScheduled,
            OriginalArticles = cluster.Select(item => new NewsArticleRef
            {
                Id = item.Id,
                Source = item.Source,
                SourceUrl = item.SourceUrl,
                PublishedAtUtc = item.PublishedAtUtc,
                Title = item.Title
            }).ToList()
        };
    }

    private static string Hash(RawNewsItem item)
    {
        var text = item.Source + "|" + CanonicalUrl(item.SourceUrl) + "|" + item.PublishedAtUtc.UtcDateTime.ToString("O");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16];
    }
}
