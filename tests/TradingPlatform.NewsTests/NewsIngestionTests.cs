using FluentAssertions;
using TradingPlatform.News;
using Xunit;

namespace TradingPlatform.NewsTests;

public sealed class NewsIngestionTests
{
    private static readonly DateTimeOffset Retrieved = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CoinGecko_keeps_the_publisher_separate_from_the_provider()
    {
        var json = """
            [{"title":"Bitcoin ETF approval","url":"https://decrypt.co/etf","author":"Ada","posted_at":"2026-09-26T11:00:00Z","type":"news","source_name":"Decrypt","related_coin_ids":["bitcoin"]},{"title":"A guide","url":"https://www.coingecko.com/learn/guide","author":"Ben","posted_at":"2026-09-26T11:00:00Z","type":"guide","source_name":"CoinGecko","related_coin_ids":[]}]
            """;
        var batch = CoinGeckoNewsProvider.ParseBatch(json, Retrieved);
        batch.Rejected.Should().Be(1);
        var item = batch.Items.Should().ContainSingle().Subject;
        item.Provider.Should().Be("coingecko");
        item.Source.Should().Be("Decrypt");
        item.RelatedProviderIds.Should().Contain("bitcoin");
    }

    [Fact]
    public void CoinDesk_stores_the_original_publisher()
    {
        var json = """
            {"Data":[{"ID":42,"TITLE":"Ethereum ETF inflow","URL":"https://www.theblock.co/etf","PUBLISHED_ON":1760000000,"BODY":"ETH inflow","SOURCE_DATA":{"NAME":"The Block"}}],"Err":{}}
            """;
        var item = CoinDeskNewsProvider.ParseBatch(json, Retrieved).Items.Should().ContainSingle().Subject;
        item.Provider.Should().Be("coindesk");
        item.Source.Should().Be("The Block");
        item.SourceUrl.Should().Be("https://www.theblock.co/etf");
        item.PublishedAtUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1760000000));
    }

    [Fact]
    public void CryptoPanic_prefers_the_original_article_url()
    {
        var json = """
            {"results":[{"id":7,"title":"Solana listing","published_at":"2026-09-26T10:00:00Z","url":"https://cryptopanic.com/news/7","original_url":"https://www.coindesk.com/solana","source":{"title":"CoinDesk"},"instruments":[{"code":"SOL"}]}]}
            """;
        var item = CryptoPanicNewsProvider.Parse(json, Retrieved).Should().ContainSingle().Subject;
        item.Provider.Should().Be("cryptopanic");
        item.Source.Should().Be("CoinDesk");
        item.SourceUrl.Should().Be("https://www.coindesk.com/solana");
        item.RelatedAssets.Should().Contain("SOL");
    }

    [Fact]
    public void Binance_announcements_use_binance_as_the_publisher()
    {
        var json = """
            {"code":"000000","data":{"catalogs":[{"catalogName":"New Cryptocurrency Listing","articles":[{"id":285530,"code":"abc","title":"Binance Will List Hyperliquid (HYPE)","releaseDate":1790246704777}]}]}}
            """;
        var item = BinanceAnnouncementProvider.ParseBatch(json, Retrieved).Items.Should().ContainSingle().Subject;
        item.Provider.Should().Be("binance");
        item.Source.Should().Be("Binance");
        item.SourceUrl.Should().Be("https://www.binance.com/en/support/announcement/abc");
        item.PublishedAtUtc.ToUnixTimeMilliseconds().Should().Be(1790246704777);
    }

    [Fact]
    public void Rss_uses_the_feed_title_as_the_publisher()
    {
        var xml = """
            <rss><channel><title>Decrypt</title><item><title>Bitcoin ETF approval</title><link>https://decrypt.co/etf?utm=1</link><pubDate>Fri, 26 Sep 2026 11:00:00 GMT</pubDate><description>ETF</description></item></channel></rss>
            """;
        var item = RssNewsProvider.ParseBatch(xml, Retrieved.AddHours(-1), Retrieved, "rss", "Fallback").Items.Should().ContainSingle().Subject;
        item.Provider.Should().Be("rss");
        item.Source.Should().Be("Decrypt");
        item.Id.Should().Be("rss:https://decrypt.co/etf");
    }

    [Fact]
    public void Gdelt_domain_is_the_publisher()
    {
        var json = """
            {"articles":[{"title":"Regulator sues exchange","url":"https://www.example.com/story?x=1","seendate":"20260926110000","domain":"example.com","language":"English"}]}
            """;
        var item = GdeltNewsProvider.Parse(json, Retrieved).Should().ContainSingle().Subject;
        item.Provider.Should().Be("gdelt");
        item.Source.Should().Be("example.com");
    }

    [Fact]
    public void The_same_url_from_two_providers_is_one_event()
    {
        var published = new DateTimeOffset(2026, 9, 26, 11, 0, 0, TimeSpan.Zero);
        var coingecko = Article("coingecko", "Cointelegraph", "https://cointelegraph.com/news/etf?utm=rss", published);
        var gdelt = Article("gdelt", "cointelegraph.com", "https://www.cointelegraph.com/news/etf", published);
        var events = new NewsPipeline(new NewsOptions()).Build([coingecko, gdelt]);
        events.Should().ContainSingle();
        events[0].OriginalArticles.Select(article => article.Provider).Should().BeEquivalentTo(["coingecko", "gdelt"]);
        events[0].OriginalArticles.Select(article => article.Source).Should().OnlyContain(source => source != "coingecko" && source != "gdelt");
        events[0].EventId.Should().Be(new NewsPipeline(new NewsOptions()).Build([gdelt]).Single().EventId);
    }

    [Fact]
    public void Ai_classification_without_a_model_client_still_scores_events()
    {
        var published = new DateTimeOffset(2026, 9, 26, 11, 0, 0, TimeSpan.Zero);
        var article = Article("coindesk", "CoinDesk", "https://www.coindesk.com/etf", published);
        var rules = new NewsPipeline(new NewsOptions()).Build([article]).Single();
        var schema = new NewsPipeline(new NewsOptions(), new SchemaNewsClassifier()).Build([article]).Single();
        schema.Direction.Should().Be(rules.Direction).And.Be(EventDirection.Bullish);
        schema.ImpactScore.Should().Be(rules.ImpactScore);
        schema.Reason.Should().StartWith("AI classification has no model client");
    }

    [Fact]
    public void A_copy_of_any_clustered_article_joins_that_event()
    {
        var published = new DateTimeOffset(2026, 9, 26, 11, 0, 0, TimeSpan.Zero);
        var first = Article("coindesk", "CoinDesk", "https://www.coindesk.com/etf", published);
        var second = Article("rss", "The Block", "https://www.theblock.co/post/etf", published.AddMinutes(10));
        var copy = Article("gdelt", "theblock.co", "https://theblock.co/post/etf/", published.AddMinutes(20));
        copy.Title = "The Block: spot fund decision lands";
        var events = new NewsPipeline(new NewsOptions()).Build([first, second, copy]);
        events.Should().ContainSingle();
        events[0].OriginalArticles.Should().HaveCount(3);
    }

    [Fact]
    public void An_event_is_detected_when_the_first_copy_arrives()
    {
        var published = new DateTimeOffset(2026, 9, 26, 11, 0, 0, TimeSpan.Zero);
        var first = Article("coingecko", "Cointelegraph", "https://cointelegraph.com/news/etf", published);
        var second = Article("gdelt", "cointelegraph.com", "https://www.cointelegraph.com/news/etf", published);
        first.RetrievedAtUtc = published.AddMinutes(4);
        second.RetrievedAtUtc = published.AddHours(3);
        var events = new NewsPipeline(new NewsOptions()).Build([second, first]);
        events.Should().ContainSingle();
        events[0].DetectedAtUtc.Should().Be(published.AddMinutes(4));
    }

    [Fact]
    public async Task A_provider_failure_does_not_stop_the_other_provider()
    {
        var root = Path.Combine(Path.GetTempPath(), "news-isolation-" + Guid.NewGuid().ToString("N"));
        var collector = new NewsCollector(
            new NewsOptions { Enabled = true, Mode = "Live", Providers = ["ok", "boom"], CacheEnabled = false },
            [new OkProvider(), new BoomProvider()],
            new FileNewsStore(root));
        var result = await collector.CollectDetailedAsync(Retrieved.AddHours(-2), Retrieved.AddHours(1), CancellationToken.None);
        result.Events.Should().ContainSingle();
        result.Providers.Should().Contain(report => report.Provider == "ok" && report.Succeeded && report.Fetched == 1);
        result.Providers.Should().Contain(report => report.Provider == "boom" && !report.Succeeded && report.Error!.Contains("rate limit"));
    }

    [Fact]
    public async Task Missing_credentials_fail_before_a_network_call()
    {
        var http = new HttpClient();
        var options = new NewsOptions();
        var actGecko = () => new CoinGeckoNewsProvider(http, options).FetchAsync(Retrieved, Retrieved, CancellationToken.None);
        var actDesk = () => new CoinDeskNewsProvider(http, options).FetchAsync(Retrieved, Retrieved, CancellationToken.None);
        var actPanic = () => new CryptoPanicNewsProvider(http, options).FetchAsync(Retrieved, Retrieved, CancellationToken.None);
        (await actGecko.Should().ThrowAsync<NewsProviderException>()).Which.Message.Should().Contain("News__CoinGeckoApiKey");
        (await actDesk.Should().ThrowAsync<NewsProviderException>()).Which.Message.Should().Contain("News__CoinDeskApiKey");
        (await actPanic.Should().ThrowAsync<NewsProviderException>()).Which.Message.Should().Contain("News__CryptoPanicToken");
    }

    [Fact]
    public void Poll_schedule_blocks_overlap_and_respects_the_interval()
    {
        var schedule = new NewsPollSchedule();
        var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        schedule.TryBegin("gdelt", now, TimeSpan.FromMinutes(15)).Should().BeTrue();
        schedule.TryBegin("gdelt", now.AddMinutes(1), TimeSpan.FromMinutes(15)).Should().BeFalse();
        schedule.Complete("gdelt", now);
        schedule.TryBegin("gdelt", now.AddMinutes(1), TimeSpan.FromMinutes(15)).Should().BeFalse();
        schedule.TryBegin("binance", now.AddMinutes(1), TimeSpan.FromMinutes(5)).Should().BeTrue();
        schedule.TryBegin("gdelt", now.AddMinutes(15), TimeSpan.FromMinutes(15)).Should().BeTrue();
    }

    [Fact]
    public void Default_sources_do_not_require_credentials()
    {
        var created = NewsProviderCatalog.Create(new NewsOptions(), new HttpClient());
        created.Select(provider => provider.Name).Should().Contain(["binance", "Cointelegraph", "CoinDesk", "Ethereum Foundation", "Stellar"]);
        created.Should().NotContain(provider => provider.Name == "rss" || provider.Name == "official-blogs" || provider.Name == "gdelt" || provider.Name == "Bitcoinist" || provider.Name == "U.Today");
        created.Should().Contain(provider => provider.Name == "Cointelegraph" && provider.ScheduleKey == "rss");
        created.Should().Contain(provider => provider.Name == "Ethereum Foundation" && provider.ScheduleKey == "official-blogs");
        created.Should().HaveCount(1 + RssNewsProvider.DefaultPublisherFeeds.Count + RssNewsProvider.DefaultOfficialFeeds.Count);
        var disabled = NewsProviderCatalog.DisabledCredentialReports(new NewsOptions(), Retrieved);
        disabled.Select(report => report.Provider).Should().BeEquivalentTo(["coingecko", "coindesk", "cryptopanic", "fred"]);
        disabled.Should().OnlyContain(report => !report.Enabled && report.Error == NewsProviderCatalog.CredentialsMissing);
    }

    [Fact]
    public void Rss_with_illegal_control_character_still_parses()
    {
        var xml = "<rss><channel><title>Blockstream</title><item><title>Liquid update</title><link>https://blog.blockstream.com/liquid</link><pubDate>Sat, 27 Sep 2026 10:00:00 GMT</pubDate><description>ok" + "\u000B" + "</description></item></channel></rss>";
        var item = RssNewsProvider.ParseBatch(xml, Retrieved.AddHours(-1), Retrieved, "Blockstream", "Blockstream").Items.Should().ContainSingle().Subject;
        item.Title.Should().Be("Liquid update");
        item.Source.Should().Be("Blockstream");
    }

    [Fact]
    public void Invalid_rss_is_an_xml_error()
    {
        var act = () => RssNewsProvider.ParseBatch("this is not xml", Retrieved.AddHours(-1), Retrieved, "rss", "Fallback");
        act.Should().Throw<System.Xml.XmlException>();
    }

    [Fact]
    public async Task Invalid_feed_is_recorded_without_stopping_the_other_provider()
    {
        var root = Path.Combine(Path.GetTempPath(), "news-xml-" + Guid.NewGuid().ToString("N"));
        var collector = new NewsCollector(
            new NewsOptions { Enabled = true, Mode = "Live", Providers = ["rss", "ok"], CacheEnabled = false },
            [new BadXmlProvider(), new OkProvider()],
            new FileNewsStore(root));
        var result = await collector.CollectDetailedAsync(Retrieved.AddHours(-2), Retrieved.AddHours(1), CancellationToken.None);
        result.Events.Should().ContainSingle();
        result.Providers.Should().Contain(report => report.Provider == "rss" && !report.Succeeded && report.Error!.Contains("XML"));
        result.Providers.Should().Contain(report => report.Provider == "ok" && report.Succeeded);
    }

    [Fact]
    public void Rate_limit_backoff_blocks_the_next_poll()
    {
        NewsBackoff.IsRateLimited("GDELT rate limit (HTTP 429).").Should().BeTrue();
        NewsBackoff.Delay("gdelt", TimeSpan.FromMinutes(15)).Should().Be(TimeSpan.FromMinutes(30));
        var schedule = new NewsPollSchedule();
        var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        schedule.TryBegin("gdelt", now, TimeSpan.FromMinutes(15)).Should().BeTrue();
        schedule.Complete("gdelt", now);
        schedule.DelayUntil("gdelt", now.Add(NewsBackoff.Delay("gdelt", TimeSpan.FromMinutes(15))));
        schedule.TryBegin("gdelt", now.AddMinutes(15), TimeSpan.FromMinutes(15)).Should().BeFalse();
        schedule.TryBegin("binance", now.AddMinutes(15), TimeSpan.FromMinutes(5)).Should().BeTrue();
        schedule.TryBegin("gdelt", now.AddMinutes(30), TimeSpan.FromMinutes(15)).Should().BeTrue();
    }

    [Fact]
    public void Gdelt_poll_interval_cannot_be_faster_than_five_minutes()
    {
        var options = new NewsOptions { ProviderPollMinutes = new Dictionary<string, int> { ["gdelt"] = 1 } };
        NewsProviderCatalog.PollIntervalMinutes(options, "gdelt").Should().Be(5);
        NewsProviderCatalog.PollIntervalMinutes(new NewsOptions(), "binance").Should().Be(1);
        NewsProviderCatalog.PollIntervalMinutes(new NewsOptions(), "official-blogs").Should().Be(5);
    }

    private static RawNewsItem Article(string provider, string publisher, string url, DateTimeOffset published) =>
        new()
        {
            Id = provider + ":" + url,
            Provider = provider,
            Source = publisher,
            SourceUrl = url,
            PublishedAtUtc = published,
            RetrievedAtUtc = Retrieved,
            Title = "Bitcoin ETF approval inflow",
            Summary = "Bitcoin ETF approval inflow",
            OriginalSourceId = url
        };

    private sealed class OkProvider : INewsProvider
    {
        public string Name => "ok";

        public Task<NewsProviderBatch> FetchAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult(new NewsProviderBatch
            {
                Items =
                [
                    new RawNewsItem
                    {
                        Id = "ok:1",
                        Provider = "ok",
                        Source = "Decrypt",
                        SourceUrl = "https://decrypt.co/one",
                        PublishedAtUtc = Retrieved,
                        RetrievedAtUtc = Retrieved,
                        Title = "Bitcoin ETF approval"
                    }
                ]
            });
    }

    private sealed class BadXmlProvider : INewsProvider
    {
        public string Name => "rss";

        public Task<NewsProviderBatch> FetchAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromException<NewsProviderBatch>(new System.Xml.XmlException("Invalid RSS/XML."));
    }

    private sealed class BoomProvider : INewsProvider
    {
        public string Name => "boom";

        public Task<NewsProviderBatch> FetchAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            throw new NewsProviderException("boom rate limit (HTTP 429).");
    }
}
