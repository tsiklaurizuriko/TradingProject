using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;

namespace TradingPlatform.News;

public interface INewsProvider
{
    string Name { get; }

    string ScheduleKey => Name;

    Task<NewsProviderBatch> FetchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed class NewsProviderBatch
{
    public IReadOnlyList<RawNewsItem> Items { get; init; } = [];

    public int Rejected { get; init; }

    public string? Warning { get; init; }
}

public sealed class NewsProviderException : Exception
{
    public NewsProviderException(string message) : base(message)
    {
    }
}

public sealed class DisabledNewsProvider : INewsProvider
{
    public string Name => "disabled";

    public Task<NewsProviderBatch> FetchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Task.FromResult(new NewsProviderBatch());
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

    public async Task<NewsProviderBatch> FetchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CoinGeckoApiKey))
        {
            throw new NewsProviderException(
                "CoinGecko /news requires an Analyst plan or above. Demo keys cannot call this endpoint. Set News__CoinGeckoApiKey.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://pro-api.coingecko.com/api/v3/news?language=en");
        request.Headers.TryAddWithoutValidation("x-cg-pro-api-key", _options.CoinGeckoApiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", "TradingPlatform/1.0");
        var json = await NewsHttp.ReadAsync(_http, request, "CoinGecko", cancellationToken);
        var batch = ParseBatch(json, DateTimeOffset.UtcNow);
        return Filter(batch, from, to);
    }

    public static IReadOnlyList<RawNewsItem> Parse(string json, DateTimeOffset retrievedAtUtc) =>
        ParseBatch(json, retrievedAtUtc).Items;

    public static NewsProviderBatch ParseBatch(string json, DateTimeOffset retrievedAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var rows = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("data", out var data) ? data : default;
        if (rows.ValueKind != JsonValueKind.Array)
        {
            return new NewsProviderBatch();
        }

        var items = new List<RawNewsItem>();
        var rejected = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var kind = ReadString(row, "type");
            if (string.Equals(kind, "guide", StringComparison.OrdinalIgnoreCase))
            {
                rejected++;
                continue;
            }

            var title = ReadString(row, "title");
            var url = ReadString(row, "url");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                rejected++;
                continue;
            }

            var published = ReadTime(row, "posted_at") ?? ReadTime(row, "updated_at") ?? ReadTime(row, "created_at") ?? retrievedAtUtc;
            var providerIds = ReadStringList(row, "related_coin_ids");
            var id = ReadString(row, "id") ?? url;
            items.Add(new RawNewsItem
            {
                Id = "coingecko:" + id,
                Provider = "coingecko",
                Source = ReadString(row, "news_site") ?? ReadString(row, "source_name") ?? "CoinGecko",
                SourceUrl = url,
                PublishedAtUtc = published,
                RetrievedAtUtc = retrievedAtUtc,
                Title = title,
                Summary = ReadString(row, "description") ?? title,
                Author = ReadString(row, "author"),
                Language = "en",
                OriginalSourceId = id,
                RelatedProviderIds = providerIds,
                TimestampPrecision = TimestampPrecision.Instant
            });
        }

        return new NewsProviderBatch { Items = items, Rejected = rejected };
    }

    private static NewsProviderBatch Filter(NewsProviderBatch batch, DateTimeOffset from, DateTimeOffset to)
    {
        var items = batch.Items.Where(item => item.PublishedAtUtc >= from && item.PublishedAtUtc <= to).ToList();
        return new NewsProviderBatch { Items = items, Rejected = batch.Rejected, Warning = batch.Warning };
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

    public async Task<NewsProviderBatch> FetchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CryptoPanicToken))
        {
            throw new NewsProviderException(
                "CryptoPanic requires a paid plan. The free Developer plan ended 2026-04-01. Set News__CryptoPanicToken and News__CryptoPanicPlan.");
        }

        var plan = Plan(_options.CryptoPanicPlan);
        var url = "https://cryptopanic.com/api/" + plan + "/v2/posts/?public=true&auth_token="
            + Uri.EscapeDataString(_options.CryptoPanicToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "TradingPlatform/1.0");
        var json = await NewsHttp.ReadAsync(_http, request, "CryptoPanic", cancellationToken);
        var batch = ParseBatch(json, DateTimeOffset.UtcNow);
        var items = batch.Items.Where(item => item.PublishedAtUtc >= from && item.PublishedAtUtc <= to).ToList();
        return new NewsProviderBatch { Items = items, Rejected = batch.Rejected };
    }

    public static IReadOnlyList<RawNewsItem> Parse(string json, DateTimeOffset retrievedAtUtc) =>
        ParseBatch(json, retrievedAtUtc).Items;

    public static NewsProviderBatch ParseBatch(string json, DateTimeOffset retrievedAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return new NewsProviderBatch();
        }

        var items = new List<RawNewsItem>();
        var rejected = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var title = ReadString(row, "title");
            var url = ReadString(row, "original_url") ?? ReadString(row, "url");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                rejected++;
                continue;
            }

            var published = ReadTime(row, "published_at") ?? retrievedAtUtc;
            var source = "CryptoPanic";
            if (row.TryGetProperty("source", out var sourceEl)
                && sourceEl.ValueKind == JsonValueKind.Object
                && sourceEl.TryGetProperty("title", out var sourceTitle)
                && sourceTitle.ValueKind == JsonValueKind.String)
            {
                source = sourceTitle.GetString() ?? source;
            }

            var assets = ReadAssets(row);
            var id = row.TryGetProperty("id", out var idEl) ? idEl.ToString() : url;
            items.Add(new RawNewsItem
            {
                Id = "cryptopanic:" + id,
                Provider = "cryptopanic",
                Source = source,
                SourceUrl = url,
                PublishedAtUtc = published,
                RetrievedAtUtc = retrievedAtUtc,
                Title = title,
                Summary = ReadString(row, "description") ?? title,
                Language = "en",
                OriginalSourceId = id,
                RelatedAssets = assets,
                RelatedProviderIds = assets,
                TimestampPrecision = TimestampPrecision.Instant
            });
        }

        return new NewsProviderBatch { Items = items, Rejected = rejected };
    }

    internal static string Plan(string? plan)
    {
        var value = (plan ?? "growth").Trim().ToLowerInvariant();
        return value is "growth" or "enterprise" or "developer" ? value : "growth";
    }

    private static List<string> ReadAssets(JsonElement row)
    {
        var assets = new List<string>();
        ReadCodes(row, "currencies", assets);
        ReadCodes(row, "instruments", assets);
        return assets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ReadCodes(JsonElement row, string name, List<string> assets)
    {
        if (!row.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var currency in values.EnumerateArray())
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

    private static string? ReadString(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadTime(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var timeEl) || timeEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(timeEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}

public sealed class GdeltNewsProvider : INewsProvider
{
    private readonly HttpClient _http;

    public GdeltNewsProvider(HttpClient http) => _http = http;

    public string Name => "gdelt";

    public async Task<NewsProviderBatch> FetchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var url = "https://api.gdeltproject.org/api/v2/doc/doc?query="
            + Uri.EscapeDataString("cryptocurrency OR bitcoin OR ethereum OR stablecoin")
            + "&mode=artlist&format=json&maxrecords=75&timespan=6h";
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "TradingPlatform/1.0");
        try
        {
            var json = await NewsHttp.ReadAsync(_http, request, "GDELT", budget.Token);
            return ParseBatch(json, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NewsProviderException("GDELT request timed out.");
        }
    }

    public static IReadOnlyList<RawNewsItem> Parse(string json, DateTimeOffset retrievedAtUtc) =>
        ParseBatch(json, retrievedAtUtc).Items;

    public static NewsProviderBatch ParseBatch(string json, DateTimeOffset retrievedAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("articles", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return new NewsProviderBatch();
        }

        var items = new List<RawNewsItem>();
        var rejected = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var title = row.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            var articleUrl = row.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(articleUrl))
            {
                rejected++;
                continue;
            }

            var seen = row.TryGetProperty("seendate", out var seenEl) ? seenEl.GetString() : null;
            var published = DateTime.TryParseExact(seen, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc))
                : retrievedAtUtc;
            var domain = row.TryGetProperty("domain", out var domainEl) ? domainEl.GetString() : null;
            items.Add(new RawNewsItem
            {
                Id = "gdelt:" + articleUrl,
                Provider = "gdelt",
                Source = string.IsNullOrWhiteSpace(domain) ? "GDELT" : domain,
                SourceUrl = articleUrl,
                PublishedAtUtc = published,
                RetrievedAtUtc = retrievedAtUtc,
                Title = title,
                Summary = title,
                Language = row.TryGetProperty("language", out var lang) ? lang.GetString() ?? "en" : "en",
                OriginalSourceId = articleUrl,
                TimestampPrecision = TimestampPrecision.Instant
            });
        }

        return new NewsProviderBatch { Items = items, Rejected = rejected };
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

    public async Task<NewsProviderBatch> FetchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.FredApiKey))
        {
            throw new NewsProviderException("FRED requires an API key. Set News__FredApiKey.");
        }

        var items = new List<RawNewsItem>();
        foreach (var series in Series)
        {
            var url = "https://api.stlouisfed.org/fred/series/observations?file_type=json&series_id="
                + series
                + "&observation_start=" + from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + "&observation_end=" + to.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + "&api_key=" + Uri.EscapeDataString(_options.FredApiKey);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "TradingPlatform/1.0");
            var json = await NewsHttp.ReadAsync(_http, request, "FRED", cancellationToken);
            items.AddRange(Parse(json, series, DateTimeOffset.UtcNow));
        }

        return new NewsProviderBatch { Items = items };
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
                Provider = "fred",
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

    public Task<NewsProviderBatch> FetchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        _feed.FetchAsync(from, to, cancellationToken);
}

public sealed class RssNewsProvider : INewsProvider
{
    public static IReadOnlyList<(string Publisher, string Url)> DefaultPublisherFeeds { get; } =
    [
        ("CoinDesk", "https://www.coindesk.com/arc/outboundfeeds/rss/"),
        ("Cointelegraph", "https://cointelegraph.com/rss"),
        ("The Block", "https://www.theblock.co/rss.xml"),
        ("Decrypt", "https://decrypt.co/feed"),
        ("DL News", "https://www.dlnews.com/arc/outboundfeeds/rss/"),
        ("Bitcoin Magazine", "https://bitcoinmagazine.com/feed"),
        ("Blockworks", "https://blockworks.com/feed"),
        ("The Defiant", "https://thedefiant.io/feed"),
        ("Unchained", "https://unchainedcrypto.com/feed/"),
        ("Protos", "https://protos.com/feed/"),
        ("Crypto Briefing", "https://cryptobriefing.com/feed/"),
        ("a16z crypto", "https://a16zcrypto.com/feed/")
    ];

    public static IReadOnlyList<(string Publisher, string Url)> DefaultOfficialFeeds { get; } =
    [
        ("Ethereum Foundation", "https://blog.ethereum.org/feed.xml"),
        ("Solana", "https://solana.com/news/rss.xml"),
        ("Arbitrum Foundation", "https://arbitrumfoundation.medium.com/feed"),
        ("Sui", "https://blog.sui.io/rss/"),
        ("Lido", "https://blog.lido.fi/rss/"),
        ("Polkadot", "https://medium.com/feed/polkadot-network"),
        ("Celestia", "https://blog.celestia.org/rss/"),
        ("Aptos", "https://aptoslabs.medium.com/feed"),
        ("Algorand", "https://medium.com/feed/algorand"),
        ("Compound", "https://medium.com/feed/compound-finance"),
        ("Avalanche", "https://medium.com/feed/@avalabs"),
        ("Stellar", "https://stellar.org/blog/rss.xml"),
        ("Hedera", "https://hedera.com/feed/"),
        ("Curve", "https://news.curve.finance/rss/"),
        ("Starknet", "https://medium.com/feed/@StarkWare"),
        ("The Graph", "https://medium.com/feed/the-graph"),
        ("Sei", "https://blog.sei.io/rss/"),
        ("Filecoin", "https://filecoin.io/blog/rss.xml"),
        ("Blockstream", "https://blog.blockstream.com/rss/"),
        ("Bitcoin.org", "https://bitcoin.org/en/rss/blog.xml"),
        ("Bitcoin Core", "https://bitcoincore.org/en/rss.xml")
    ];

    private readonly HttpClient _http;
    private readonly IReadOnlyList<(string Publisher, string Url)> _feeds;

    public RssNewsProvider(HttpClient http)
        : this(http, "rss", DefaultPublisherFeeds, "rss")
    {
    }

    public RssNewsProvider(HttpClient http, string name, IReadOnlyList<(string Publisher, string Url)> feeds, string? scheduleKey = null)
    {
        _http = http;
        Name = name;
        ScheduleKey = string.IsNullOrWhiteSpace(scheduleKey) ? name : scheduleKey;
        _feeds = feeds;
    }

    public string Name { get; }

    public string ScheduleKey { get; }

    public async Task<NewsProviderBatch> FetchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (_feeds.Count == 0)
        {
            throw new NewsProviderException(Name + " has no feeds configured.");
        }

        var items = new List<RawNewsItem>();
        var rejected = 0;
        var read = 0;
        var warnings = new List<string>();
        foreach (var feed in _feeds)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
                request.Headers.TryAddWithoutValidation("User-Agent", "TradingPlatform/1.0");
                request.Headers.TryAddWithoutValidation("Accept", "application/rss+xml, application/atom+xml, application/xml, text/xml");
                var xml = await NewsHttp.ReadAsync(_http, request, feed.Publisher, cancellationToken);
                var batch = ParseBatch(xml, from, DateTimeOffset.UtcNow, Name, feed.Publisher);
                read++;
                items.AddRange(batch.Items);
                rejected += batch.Rejected;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add(feed.Publisher + ": " + ex.Message);
            }
        }

        if (read == 0 && warnings.Count > 0)
        {
            throw new NewsProviderException(string.Join("; ", warnings));
        }

        return new NewsProviderBatch
        {
            Items = items,
            Rejected = rejected,
            Warning = warnings.Count == 0 ? null : string.Join("; ", warnings)
        };
    }

    public static IReadOnlyList<RawNewsItem> Parse(string xml, DateTimeOffset from, DateTimeOffset retrievedAtUtc) =>
        ParseBatch(xml, from, retrievedAtUtc, "rss", null).Items;

    public static NewsProviderBatch ParseBatch(
        string xml,
        DateTimeOffset from,
        DateTimeOffset retrievedAtUtc,
        string provider,
        string? fallbackPublisher)
    {
        var doc = XDocument.Parse(SanitizeXml(xml));
        var channelTitle = doc.Descendants()
            .FirstOrDefault(element => element.Name.LocalName is "channel" or "feed")
            ?.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "title")
            ?.Value
            ?.Trim();
        var publisher = string.IsNullOrWhiteSpace(channelTitle) ? fallbackPublisher : channelTitle;
        if (string.IsNullOrWhiteSpace(publisher))
        {
            publisher = provider;
        }

        var items = new List<RawNewsItem>();
        var rejected = 0;
        foreach (var row in doc.Descendants().Where(element => element.Name.LocalName is "item" or "entry"))
        {
            var title = ElementText(row, "title");
            var url = Link(row);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                rejected++;
                continue;
            }

            var publishedText = ElementText(row, "pubDate") ?? ElementText(row, "published") ?? ElementText(row, "updated");
            var published = DateTimeOffset.TryParse(publishedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : retrievedAtUtc;
            if (published < from)
            {
                continue;
            }

            var summary = ElementText(row, "description") ?? ElementText(row, "summary") ?? title;
            items.Add(new RawNewsItem
            {
                Id = provider + ":" + NewsEventClusterer.NavigableUrl(url),
                Provider = provider,
                Source = publisher,
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

        return new NewsProviderBatch { Items = items, Rejected = rejected };
    }

    internal static string SanitizeXml(string xml)
    {
        if (string.IsNullOrEmpty(xml) || !xml.Any(character => character < ' ' && character is not '\t' and not '\n' and not '\r'))
        {
            return xml;
        }

        var chars = xml.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var character = chars[i];
            if (character < ' ' && character is not '\t' and not '\n' and not '\r')
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }

    private static string? ElementText(XElement row, string name) =>
        row.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value?.Trim();

    private static string? Link(XElement row)
    {
        var link = row.Elements().FirstOrDefault(element => element.Name.LocalName == "link");
        if (link is null)
        {
            return null;
        }

        var href = link.Attribute("href")?.Value;
        if (!string.IsNullOrWhiteSpace(href))
        {
            return href.Trim();
        }

        return string.IsNullOrWhiteSpace(link.Value) ? null : link.Value.Trim();
    }
}

public sealed class CoinDeskNewsProvider : INewsProvider
{
    private readonly HttpClient _http;
    private readonly NewsOptions _options;

    public CoinDeskNewsProvider(HttpClient http, NewsOptions options)
    {
        _http = http;
        _options = options;
    }

    public string Name => "coindesk";

    public async Task<NewsProviderBatch> FetchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CoinDeskApiKey))
        {
            throw new NewsProviderException(
                "CoinDesk Data requires a paid API subscription. The free tier ended 2026-05-21. Set News__CoinDeskApiKey.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://data-api.coindesk.com/news/v1/article/list?lang=EN&limit=50");
        request.Headers.TryAddWithoutValidation("x-api-key", _options.CoinDeskApiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", "TradingPlatform/1.0");
        var json = await NewsHttp.ReadAsync(_http, request, "CoinDesk", cancellationToken);
        var batch = ParseBatch(json, DateTimeOffset.UtcNow);
        var items = batch.Items.Where(item => item.PublishedAtUtc >= from && item.PublishedAtUtc <= to).ToList();
        return new NewsProviderBatch { Items = items, Rejected = batch.Rejected };
    }

    public static NewsProviderBatch ParseBatch(string json, DateTimeOffset retrievedAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("Err", out var err)
            && err.ValueKind == JsonValueKind.Object
            && err.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(message.GetString()))
        {
            throw new NewsProviderException("CoinDesk: " + message.GetString());
        }

        if (!root.TryGetProperty("Data", out var rows) && !root.TryGetProperty("data", out rows))
        {
            return new NewsProviderBatch();
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return new NewsProviderBatch();
        }

        var items = new List<RawNewsItem>();
        var rejected = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var title = Read(row, "TITLE", "title");
            var url = Read(row, "URL", "url");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                rejected++;
                continue;
            }

            var published = ReadUnix(row, "PUBLISHED_ON") ?? ReadUnix(row, "published_on") ?? retrievedAtUtc;
            var id = Read(row, "ID", "id") ?? Read(row, "GUID", "guid") ?? url;
            items.Add(new RawNewsItem
            {
                Id = "coindesk:" + id,
                Provider = "coindesk",
                Source = Publisher(row),
                SourceUrl = url,
                PublishedAtUtc = published,
                RetrievedAtUtc = retrievedAtUtc,
                Title = title,
                Summary = Read(row, "BODY", "body") ?? Read(row, "SUBTITLE", "subtitle") ?? title,
                Author = Read(row, "AUTHORS", "authors"),
                Language = (Read(row, "LANG", "lang") ?? "en").ToLowerInvariant(),
                OriginalSourceId = id,
                TimestampPrecision = TimestampPrecision.Instant
            });
        }

        return new NewsProviderBatch { Items = items, Rejected = rejected };
    }

    private static string Publisher(JsonElement row)
    {
        if (row.TryGetProperty("SOURCE_DATA", out var source) || row.TryGetProperty("source_info", out source))
        {
            var name = Read(source, "NAME", "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return "CoinDesk";
    }

    private static string? Read(JsonElement row, string upper, string lower)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (row.TryGetProperty(upper, out var value) || row.TryGetProperty(lower, out value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    private static DateTimeOffset? ReadUnix(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var unix))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(unix);
    }
}

public sealed class BinanceAnnouncementProvider : INewsProvider
{
    private readonly HttpClient _http;

    public BinanceAnnouncementProvider(HttpClient http) => _http = http;

    public string Name => "binance";

    public async Task<NewsProviderBatch> FetchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://www.binance.com/bapi/apex/v1/public/apex/cms/article/list/query?type=1&pageNo=1&pageSize=20");
        request.Headers.TryAddWithoutValidation("User-Agent", "TradingPlatform/1.0");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        var json = await NewsHttp.ReadAsync(_http, request, "Binance", cancellationToken);
        var batch = ParseBatch(json, DateTimeOffset.UtcNow);
        var items = batch.Items.Where(item => item.PublishedAtUtc >= from && item.PublishedAtUtc <= to).ToList();
        return new NewsProviderBatch { Items = items, Rejected = batch.Rejected };
    }

    public static NewsProviderBatch ParseBatch(string json, DateTimeOffset retrievedAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var code = root.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
        if (!string.Equals(code, "000000", StringComparison.Ordinal))
        {
            throw new NewsProviderException("Binance announcements returned code " + (code ?? "missing") + ".");
        }

        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("catalogs", out var catalogs))
        {
            return new NewsProviderBatch();
        }

        var items = new List<RawNewsItem>();
        var rejected = 0;
        ReadCatalogs(catalogs, retrievedAtUtc, items, ref rejected);
        return new NewsProviderBatch { Items = items, Rejected = rejected };
    }

    private static void ReadCatalogs(JsonElement catalogs, DateTimeOffset retrievedAtUtc, List<RawNewsItem> items, ref int rejected)
    {
        if (catalogs.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var catalog in catalogs.EnumerateArray())
        {
            if (catalog.TryGetProperty("articles", out var articles) && articles.ValueKind == JsonValueKind.Array)
            {
                foreach (var article in articles.EnumerateArray())
                {
                    var title = article.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                    var articleCode = article.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(articleCode))
                    {
                        rejected++;
                        continue;
                    }

                    var id = article.TryGetProperty("id", out var idEl) ? idEl.ToString() : articleCode;
                    items.Add(new RawNewsItem
                    {
                        Id = "binance:" + id,
                        Provider = "binance",
                        Source = "Binance",
                        SourceUrl = "https://www.binance.com/en/support/announcement/" + articleCode,
                        PublishedAtUtc = ReadRelease(article) ?? retrievedAtUtc,
                        RetrievedAtUtc = retrievedAtUtc,
                        Title = title,
                        Summary = title,
                        Language = "en",
                        OriginalSourceId = id,
                        TimestampPrecision = TimestampPrecision.Instant
                    });
                }
            }

            if (catalog.TryGetProperty("catalogs", out var nested))
            {
                ReadCatalogs(nested, retrievedAtUtc, items, ref rejected);
            }
        }
    }

    private static DateTimeOffset? ReadRelease(JsonElement article)
    {
        if (!article.TryGetProperty("releaseDate", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var raw))
        {
            return null;
        }

        var milliseconds = raw > 10_000_000_000 ? raw : raw * 1000;
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }
}

internal static class NewsHttp
{
    public static async Task<string> ReadAsync(HttpClient http, HttpRequestMessage request, string provider, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        var detail = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (detail.StartsWith('<') || detail.Length > 180)
        {
            detail = detail.Length > 180 ? detail[..180] : string.Empty;
        }

        var status = (int)response.StatusCode;
        var prefix = status == (int)HttpStatusCode.TooManyRequests
            ? provider + " rate limit (HTTP 429)"
            : provider + " HTTP " + status;
        throw new NewsProviderException(string.IsNullOrWhiteSpace(detail) ? prefix + "." : prefix + ": " + detail);
    }
}
