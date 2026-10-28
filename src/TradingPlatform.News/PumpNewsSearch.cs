using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace TradingPlatform.News;

public static class PumpNewsSearch
{
    public static async Task<PumpNewsBundle> CollectAsync(
        HttpClient http,
        string symbol,
        string baseAsset,
        IReadOnlyList<NewsEvent> localEvents,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var from = now.AddDays(-3);
        var headlines = new List<PumpHeadline>();
        headlines.AddRange(FromEvents(localEvents, baseAsset, symbol, from, now));
        var internet = false;

        var google = await GetAsync(http, GoogleUrl(baseAsset), cancellationToken);
        if (google is not null && ReadRss(google, "google-news") is { } rss)
        {
            headlines.AddRange(rss.Where(item => item.PublishedAt >= from && item.PublishedAt <= now.AddMinutes(5)));
            internet = true;
        }

        var binance = await GetAsync(
            http,
            "https://www.binance.com/bapi/composite/v1/public/cms/article/catalog/list/query?catalogId=48&pageNo=1&pageSize=30",
            cancellationToken);
        if (binance is not null && ReadBinanceArticles(binance) is { } articles)
        {
            headlines.AddRange(articles.Where(item => item.PublishedAt >= from && item.PublishedAt <= now.AddMinutes(5)));
            internet = true;
        }

        return internet
            ? PumpNewsBundle.Ready(headlines)
            : PumpNewsBundle.Failed("Internet news search failed. No short without a completed search.");
    }

    public static IReadOnlyList<PumpHeadline> FromEvents(
        IReadOnlyList<NewsEvent> events,
        string baseAsset,
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var rows = new List<PumpHeadline>();
        foreach (var item in events)
        {
            if (item.PublishedAtUtc < from || item.PublishedAtUtc > to)
            {
                continue;
            }

            var title = item.OriginalArticles.FirstOrDefault()?.Title ?? item.Reason;
            var body = string.Join(
                " ",
                item.OriginalArticles.Select(article => article.Title + " " + article.Summary));
            var named = item.Assets.Any(asset => asset.Equals(baseAsset, StringComparison.OrdinalIgnoreCase))
                || item.AffectedAssets.Any(asset => asset.BaseAsset.Equals(baseAsset, StringComparison.OrdinalIgnoreCase))
                || PumpCatalyst.Mentions(title + " " + body, baseAsset, symbol);
            if (!named)
            {
                continue;
            }

            rows.Add(new PumpHeadline(title, body, item.OriginalArticles.FirstOrDefault()?.Source ?? "local", item.PublishedAtUtc));
        }

        return rows;
    }

    public static IReadOnlyList<PumpHeadline>? ReadRss(string xml, string source)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            if (!doc.Descendants().Any(element => element.Name.LocalName is "rss" or "feed" or "item" or "entry"))
            {
                return null;
            }

            var rows = new List<PumpHeadline>();
            foreach (var item in doc.Descendants().Where(element => element.Name.LocalName is "item" or "entry"))
            {
                var title = Text(item, "title");
                var body = Text(item, "description");
                if (string.IsNullOrWhiteSpace(body))
                {
                    body = Text(item, "summary");
                }

                rows.Add(new PumpHeadline(title, body, source, Published(item)));
            }

            return rows;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    public static IReadOnlyList<PumpHeadline>? ReadBinanceArticles(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var rows = new List<PumpHeadline>();
            Collect(doc.RootElement, rows);
            return rows;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Collect(JsonElement element, List<PumpHeadline> rows)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string? title = null;
            long? release = null;
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("title") && property.Value.ValueKind == JsonValueKind.String)
                {
                    title = property.Value.GetString();
                }

                if (property.NameEquals("releaseDate") && property.Value.TryGetInt64(out var ms))
                {
                    release = ms;
                }
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                var published = release is { } stamp
                    ? DateTimeOffset.FromUnixTimeMilliseconds(stamp)
                    : DateTimeOffset.UtcNow;
                rows.Add(new PumpHeadline(title, title, "binance", published));
            }

            foreach (var property in element.EnumerateObject())
            {
                Collect(property.Value, rows);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, rows);
            }
        }
    }

    private static string GoogleUrl(string baseAsset) =>
        "https://news.google.com/rss/search?q="
        + Uri.EscapeDataString("\"" + baseAsset + "\" crypto")
        + "&hl=en-US&gl=US&ceid=US:en";

    private static async Task<string?> GetAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("TradingPlatform/1.0");
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Text(XElement item, string name) =>
        item.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value ?? string.Empty;

    private static DateTimeOffset Published(XElement item)
    {
        var raw = Text(item, "pubDate");
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Text(item, "published");
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.UtcNow;
    }
}
