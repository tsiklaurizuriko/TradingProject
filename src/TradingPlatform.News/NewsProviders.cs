using System.Globalization;
using System.Text.Json;

namespace TradingPlatform.News;

public interface INewsProvider
{
    string Name { get; }

    Task<IReadOnlyList<RawNewsItem>> GetNewsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed class DisabledNewsProvider : INewsProvider
{
    public string Name => "disabled";

    public Task<IReadOnlyList<RawNewsItem>> GetNewsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RawNewsItem>>([]);
}

public sealed class CoinGeckoNewsProvider : INewsProvider
{
    private readonly HttpClient _http;
    private readonly NewsOptions _options;

    public CoinGeckoNewsProvider(HttpClient http, NewsOptions options)
    {
        _http = http;
        _options = options;
    }

    public string Name => "coingecko";

    public async Task<IReadOnlyList<RawNewsItem>> GetNewsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CoinGeckoApiKey))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.coingecko.com/api/v3/news");
        request.Headers.TryAddWithoutValidation("x-cg-demo-api-key", _options.CoinGeckoApiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(json, DateTimeOffset.UtcNow)
            .Where(item => item.PublishedAtUtc >= from && item.PublishedAtUtc <= to)
            .ToList();
    }

    public static IReadOnlyList<RawNewsItem> Parse(string json, DateTimeOffset retrievedAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var rows = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("data", out var data) ? data : default;
        if (rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<RawNewsItem>();
        foreach (var row in rows.EnumerateArray())
        {
            var title = ReadString(row, "title");
            var url = ReadString(row, "url");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var published = ReadTime(row, "updated_at") ?? ReadTime(row, "created_at") ?? retrievedAtUtc;
            var providerIds = ReadStringList(row, "related_coin_ids");
            items.Add(new RawNewsItem
            {
                Id = "coingecko:" + (ReadString(row, "id") ?? url),
                Source = ReadString(row, "news_site") ?? "CoinGecko",
                SourceUrl = url,
                PublishedAtUtc = published,
                RetrievedAtUtc = retrievedAtUtc,
                Title = title,
                Summary = ReadString(row, "description") ?? string.Empty,
                Author = ReadString(row, "author"),
                Language = "en",
                OriginalSourceId = ReadString(row, "id"),
                RelatedProviderIds = providerIds,
                TimestampPrecision = TimestampPrecision.Instant
            });
        }

        return items;
    }

    private static List<string> ReadStringList(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ReadString(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadTime(JsonElement row, string name)
    {
        var text = ReadString(row, name);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}

public sealed class CryptoPanicNewsProvider : INewsProvider
{
    private readonly HttpClient _http;
    private readonly NewsOptions _options;

    public CryptoPanicNewsProvider(HttpClient http, NewsOptions options)
    {
        _http = http;
        _options = options;
    }

    public string Name => "cryptopanic";

    public async Task<IReadOnlyList<RawNewsItem>> GetNewsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CryptoPanicToken))
        {
            return [];
        }

        var url = "https://cryptopanic.com/api/v1/posts/?public=true&auth_token="
            + Uri.EscapeDataString(_options.CryptoPanicToken);
        var json = await _http.GetStringAsync(url, cancellationToken);
        return Parse(json, DateTimeOffset.UtcNow)
            .Where(item => item.PublishedAtUtc >= from && item.PublishedAtUtc <= to)
            .ToList();
    }

    public static IReadOnlyList<RawNewsItem> Parse(string json, DateTimeOffset retrievedAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<RawNewsItem>();
        foreach (var row in rows.EnumerateArray())
        {
            var title = row.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            var url = row.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var published = row.TryGetProperty("published_at", out var timeEl)
                && DateTimeOffset.TryParse(timeEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : retrievedAtUtc;
            var source = "CryptoPanic";
            if (row.TryGetProperty("source", out var sourceEl)
                && sourceEl.TryGetProperty("title", out var sourceTitle)
                && sourceTitle.ValueKind == JsonValueKind.String)
            {
                source = sourceTitle.GetString() ?? source;
            }

            var assets = new List<string>();
            if (row.TryGetProperty("currencies", out var currencies) && currencies.ValueKind == JsonValueKind.Array)
            {
                foreach (var currency in currencies.EnumerateArray())
                {
                    if (currency.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                    {
                        var asset = code.GetString();
                        if (!string.IsNullOrWhiteSpace(asset))
                        {
                            assets.Add(asset.ToUpperInvariant());
                        }
                    }
                }
            }

            var id = row.TryGetProperty("id", out var idEl) ? idEl.ToString() : url;
            items.Add(new RawNewsItem
            {
                Id = "cryptopanic:" + id,
                Source = source,
                SourceUrl = url,
                PublishedAtUtc = published,
                RetrievedAtUtc = retrievedAtUtc,
                Title = title,
                Summary = title,
                Language = "en",
                OriginalSourceId = id,
                RelatedAssets = assets,
                RelatedProviderIds = assets,
                TimestampPrecision = TimestampPrecision.Instant
            });
        }

        return items;
    }
}

public sealed class GdeltNewsProvider : INewsProvider
{
    private readonly HttpClient _http;

    public GdeltNewsProvider(HttpClient http) => _http = http;

    public string Name => "gdelt";

    public async Task<IReadOnlyList<RawNewsItem>> GetNewsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var url = "https://api.gdeltproject.org/api/v2/doc/doc?query=cryptocurrency&mode=artlist&format=json&maxrecords=75&timespan=6h";
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "TradingPlatform/1.0");
        try
        {
            using var response = await _http.SendAsync(request, budget.Token);
            if ((int)response.StatusCode == 429 || !response.IsSuccessStatusCode)
            {
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(budget.Token);
            if (!json.TrimStart().StartsWith('{'))
            {
                return [];
            }

            return Parse(json, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    public static IReadOnlyList<RawNewsItem> Parse(string json, DateTimeOffset retrievedAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("articles", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<RawNewsItem>();
        foreach (var row in rows.EnumerateArray())
        {
            var title = row.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            var url = row.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var seen = row.TryGetProperty("seendate", out var seenEl) ? seenEl.GetString() : null;
            var published = DateTime.TryParseExact(seen, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc))
                : retrievedAtUtc;
            var domain = row.TryGetProperty("domain", out var domainEl) ? domainEl.GetString() : "GDELT";
            items.Add(new RawNewsItem
            {
                Id = "gdelt:" + url,
                Source = domain ?? "GDELT",
                SourceUrl = url,
                PublishedAtUtc = published,
                RetrievedAtUtc = retrievedAtUtc,
                Title = title,
                Summary = title,
                Language = row.TryGetProperty("language", out var lang) ? lang.GetString() ?? "en" : "en",
                OriginalSourceId = url,
                TimestampPrecision = TimestampPrecision.Instant
            });
        }

        return items;
    }
}

public sealed class FredMacroReleaseProvider : INewsProvider
{
    private static readonly string[] Series = ["CPIAUCSL", "PPIACO", "PAYEMS", "FEDFUNDS"];
    private readonly HttpClient _http;
    private readonly NewsOptions _options;

    public FredMacroReleaseProvider(HttpClient http, NewsOptions options)
    {
        _http = http;
        _options = options;
    }

    public string Name => "fred";

    public async Task<IReadOnlyList<RawNewsItem>> GetNewsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.FredApiKey))
        {
            return [];
        }

        var items = new List<RawNewsItem>();
        foreach (var series in Series)
        {
            var url = "https://api.stlouisfed.org/fred/series/observations?file_type=json&series_id="
                + series
                + "&observation_start=" + from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + "&observation_end=" + to.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + "&api_key=" + Uri.EscapeDataString(_options.FredApiKey);
            var json = await _http.GetStringAsync(url, cancellationToken);
            items.AddRange(Parse(json, series, DateTimeOffset.UtcNow));
        }

        return items;
    }

    public static IReadOnlyList<RawNewsItem> Parse(string json, string seriesId, DateTimeOffset retrievedAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("observations", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<RawNewsItem>();
        foreach (var row in rows.EnumerateArray())
        {
            var dateText = row.TryGetProperty("date", out var dateEl) ? dateEl.GetString() : null;
            var valueText = row.TryGetProperty("value", out var valueEl) ? valueEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(dateText)
                || !DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                || !decimal.TryParse(valueText, NumberStyles.Number, CultureInfo.InvariantCulture, out var actual))
            {
                continue;
            }

            var published = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc));
            items.Add(new RawNewsItem
            {
                Id = "fred:" + seriesId + ":" + dateText,
                Source = "FRED",
                SourceUrl = "https://fred.stlouisfed.org/series/" + seriesId,
                PublishedAtUtc = published,
                RetrievedAtUtc = retrievedAtUtc,
                Title = "FRED " + seriesId + " " + dateText,
                Summary = seriesId + " actual " + actual.ToString(CultureInfo.InvariantCulture) + ". Consensus was not provided.",
                Language = "en",
                OriginalSourceId = seriesId + ":" + dateText,
                TimestampPrecision = TimestampPrecision.DateOnly,
                Actual = actual,
                IsScheduled = true
            });
        }

        return items;
    }
}

public sealed class GenericCryptoNewsProvider : INewsProvider
{
    private readonly CoinGeckoNewsProvider _feed;

    public GenericCryptoNewsProvider(HttpClient http, NewsOptions options) =>
        _feed = new CoinGeckoNewsProvider(http, options);

    public string Name => "generic-crypto";

    public Task<IReadOnlyList<RawNewsItem>> GetNewsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        _feed.GetNewsAsync(from, to, cancellationToken);
}

public sealed class RssNewsProvider : INewsProvider
{
    private readonly HttpClient _http;

    public RssNewsProvider(HttpClient http) => _http = http;

    public string Name => "rss";

    public async Task<IReadOnlyList<RawNewsItem>> GetNewsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cointelegraph.com/rss");
        request.Headers.TryAddWithoutValidation("User-Agent", "TradingPlatform/1.0");
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(xml, from, DateTimeOffset.UtcNow);
    }

    public static IReadOnlyList<RawNewsItem> Parse(string xml, DateTimeOffset from, DateTimeOffset retrievedAtUtc)
    {
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var items = new List<RawNewsItem>();
        foreach (var row in doc.Descendants("item"))
        {
            var title = row.Element("title")?.Value?.Trim();
            var url = row.Element("link")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var publishedText = row.Element("pubDate")?.Value;
            var published = DateTimeOffset.TryParse(publishedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : retrievedAtUtc;
            if (published < from)
            {
                continue;
            }

            var summary = row.Element("description")?.Value ?? title;
            items.Add(new RawNewsItem
            {
                Id = "rss:" + url,
                Source = "Cointelegraph",
                SourceUrl = url,
                PublishedAtUtc = published,
                RetrievedAtUtc = retrievedAtUtc,
                Title = title,
                Summary = summary,
                Language = "en",
                OriginalSourceId = url,
                TimestampPrecision = TimestampPrecision.Instant
            });
        }

        return items;
    }
}

