using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.News;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.NewsTests;

public sealed class NewsIntelligenceTests
{
    private static readonly DateTimeOffset Retrieved = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_timestamp_asset_and_source()
    {
        var json = """
            {"data":[{"id":"1","title":"Bitcoin ETF approval","description":"BTC inflow","url":"https://www.coindesk.com/a?x=1","news_site":"CoinDesk","author":"Ada","updated_at":"2026-01-01T09:59:00Z","related_coin_ids":["bitcoin"]}]}
            """;
        var item = CoinGeckoNewsProvider.Parse(json, Retrieved).Single();
        item.PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 1, 1, 9, 59, 0, TimeSpan.Zero));
        item.RelatedProviderIds.Should().Contain("bitcoin");
        item.Provider.Should().Be("coingecko");
        item.Source.Should().Be("CoinDesk");
        item.SourceUrl.Should().Be("https://www.coindesk.com/a?x=1");
        var catalog = SampleCatalog();
        var mapped = new NewsPipeline(new NewsOptions(), catalog: catalog).Build([item]).Single();
        mapped.PrimaryAsset.Should().Be("BTC");
        mapped.AffectedAssets.Should().ContainSingle(asset => asset.BaseAsset == "BTC" && asset.Symbol == "BTCUSDT");
    }

    [Fact]
    public void Clusters_duplicate_articles_and_keeps_unrelated_events()
    {
        var options = new NewsOptions();
        var first = Article("coindesk", "https://www.coindesk.com/etf", new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "Bitcoin ETF approval inflow");
        var copy = Article("theblock", "https://www.theblock.co/etf", new DateTimeOffset(2026, 1, 1, 9, 20, 0, TimeSpan.Zero), "Bitcoin ETF approval inflow");
        var other = Article("coindesk", "https://www.coindesk.com/hack", new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero), "Exchange hack stolen funds");
        var events = new NewsPipeline(options).Build([first, copy, other]);
        events.Should().HaveCount(2);
        events.Single(item => item.EventType == NewsEventType.Etf).SourceCount.Should().Be(2);
        events.Single(item => item.EventType == NewsEventType.Etf).OriginalArticles.Should().HaveCount(2);
    }

    [Fact]
    public void Summary_text_classifies_a_major_source_above_the_trade_gate()
    {
        var item = new RawNewsItem
        {
            Id = "ct:1",
            Provider = "Cointelegraph",
            Source = "Cointelegraph",
            SourceUrl = "https://cointelegraph.com/news/sec-etf",
            PublishedAtUtc = Retrieved,
            RetrievedAtUtc = Retrieved,
            Title = "Markets wrap",
            Summary = "<p>The SEC approved a spot Bitcoin ETF after months of review.</p>"
        };
        var mapped = new NewsPipeline(new NewsOptions(), catalog: SampleCatalog()).Build([item]).Single();
        mapped.EventType.Should().Be(NewsEventType.Etf);
        mapped.Direction.Should().Be(EventDirection.Bullish);
        mapped.PrimaryAsset.Should().Be("BTC");
        mapped.ImpactScore.Should().BeGreaterThanOrEqualTo(0.70);
        mapped.ConfidenceScore.Should().BeGreaterThanOrEqualTo(0.75);
    }

    [Fact]
    public void Rejected_etf_is_bearish_and_bank_is_not_a_ban()
    {
        var rejected = Classify("Cointelegraph", "Bitcoin ETF was not approved");
        rejected.Direction.Should().Be(EventDirection.Bearish);
        var bank = Classify("Cointelegraph", "Bank adds bitcoin custody");
        bank.EventType.Should().NotBe(NewsEventType.Regulation);
        bank.Direction.Should().NotBe(EventDirection.Bearish);
    }

    private static NewsEvent Classify(string source, string title)
    {
        var item = new NewsEvent
        {
            SourceCount = 1,
            OriginalArticles = [new NewsArticleRef { Source = source, Title = title }]
        };
        return new RuleNewsClassifier().Classify(item);
    }

    [Fact]
    public void Candle_at_10_00_does_not_see_news_published_at_10_05()
    {
        var open = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var provider = Provider(
            Event("late", open.AddMinutes(5), EventDirection.Bullish),
            Event("early", open.AddMinutes(-1), EventDirection.Bullish));
        var bar = provider.FeaturesAt(open, TimeSpan.FromMinutes(5), "BTCUSDT");
        bar.EventId.Should().Be("early");
        bar.NewsEventCount.Should().Be(1);
        NewsFeatureProvider.IsVisible(Event("late", open.AddMinutes(5), EventDirection.Bullish), open, TimeSpan.FromMinutes(5), "BTCUSDT").Should().BeFalse();
    }

    [Fact]
    public void Decays_impact_with_the_configured_half_life()
    {
        var open = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero);
        var provider = Provider(Event("n", open.AddMinutes(-60), EventDirection.Bullish, impact: 1));
        var bar = provider.FeaturesAt(open, TimeSpan.FromHours(1), "BTCUSDT");
        bar.NewsImpact1h.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void Momentum_buys_when_news_and_market_confirm()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 40).Select(i => Bar(start.AddHours(i), 100m, 10m, 6m)).ToList();
        candles[^1] = Bar(start.AddHours(39), 110m, 100m, 80m);
        var news = Event("n", candles[^1].OpenTime.AddMinutes(-1), EventDirection.Bullish, impact: 0.9, sentiment: 0.8, confidence: 0.9, novelty: 1);
        var engine = new NewsStrategyEngine(NewsHypotheses.Momentum, "BTCUSDT", [news], new NewsOptions { Enabled = true });
        var detail = engine.EvaluateDetailAt(new StrategyDefinition(), Context(candles), new CausalIndicatorCache(candles), candles.Count - 1);
        detail.Signal.Should().Be(TradingPlatform.Domain.Trading.SignalType.Buy);
        detail.Reason.Should().Contain("eventId=n");
    }

    [Fact]
    public async Task Disabled_news_does_not_call_providers_or_trade()
    {
        var collector = new NewsCollector(new NewsOptions { Enabled = false, Mode = "Live" }, [new ExplodingProvider()], new FileNewsStore(Path.Combine(Path.GetTempPath(), "news-disabled-test")));
        var collected = await collector.CollectAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, CancellationToken.None);
        collected.Should().BeEmpty();

        var candles = Enumerable.Range(0, 5).Select(i => Bar(new DateTimeOffset(2026, 1, 1, i, 0, 0, TimeSpan.Zero), 100m + i, 10m, 6m)).ToList();
        var engine = new NewsStrategyEngine(
            NewsHypotheses.Momentum,
            "BTCUSDT",
            [Event("n", candles[^1].OpenTime.AddMinutes(-1), EventDirection.Bullish)],
            new NewsOptions { Enabled = false });
        var detail = engine.EvaluateDetailAt(new StrategyDefinition(), Context(candles), new CausalIndicatorCache(candles), candles.Count - 1);
        detail.Signal.Should().Be(TradingPlatform.Domain.Trading.SignalType.NoAction);
    }

    [Fact]
    public void Invalid_classifier_json_does_not_invent_a_direction()
    {
        var item = Event("n", Retrieved, EventDirection.Bullish);
        SchemaNewsClassifier.TryApply("{\"signal\":\"BUY\"}", item, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
        item.Direction.Should().Be(EventDirection.Unknown);
    }

    [Fact]
    public void One_article_maps_to_btc_eth_and_sol_once()
    {
        var item = Article("coindesk", "https://www.coindesk.com/multi", new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "Ethereum and Solana ETFs see record inflows");
        item.RelatedProviderIds = ["ethereum", "solana"];
        var events = new NewsPipeline(new NewsOptions(), catalog: SampleCatalog()).Build([item]);
        events.Should().ContainSingle();
        events[0].AffectedAssets.Select(asset => asset.BaseAsset).Should().BeEquivalentTo(["ETH", "SOL"]);
        events[0].MarketScope.Should().Be(MarketScope.MultiAsset);
        events[0].EventId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Asset_features_follow_relevance_and_global_scope()
    {
        var open = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var eth = Event("eth", open.AddMinutes(-1), EventDirection.Bullish, impact: 1);
        eth.Assets = ["ETH"];
        eth.AffectedAssets = [new AssetRelationship { BaseAsset = "ETH", Symbol = "ETHUSDT", Relevance = 1, IsPrimary = true }];
        eth.PrimaryAsset = "ETH";
        var global = Event("macro", open.AddMinutes(-1), EventDirection.Neutral, impact: 1);
        global.Assets = [];
        global.AffectedAssets = [];
        global.MarketScope = MarketScope.Global;
        global.EventType = NewsEventType.Fed;
        var provider = Provider(eth, global);
        var ethBar = provider.FeaturesAt(open, TimeSpan.FromMinutes(5), new NewsAssetContext("ETHUSDT", "ETH", "ethereum"));
        var btcBar = provider.FeaturesAt(open, TimeSpan.FromMinutes(5), new NewsAssetContext("BTCUSDT", "BTC", "bitcoin"));
        ethBar.NewsImpact.Should().BeGreaterThan(btcBar.NewsImpact);
        btcBar.NewsEventCount.Should().Be(1);
    }

    [Fact]
    public void A_non_btc_perpetual_can_receive_its_own_news()
    {
        var start = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 40).Select(i => Bar(start.AddHours(i), 1m, 10m, 6m)).ToList();
        candles[^1] = Bar(start.AddHours(39), 1.2m, 100m, 80m);
        var news = Event("wif", candles[^1].OpenTime.AddMinutes(-1), EventDirection.Bullish, impact: 0.9, sentiment: 0.8, confidence: 0.9, novelty: 1);
        news.Assets = ["WIF"];
        news.PrimaryAsset = "WIF";
        news.AffectedAssets = [new AssetRelationship { BaseAsset = "WIF", Symbol = "WIFUSDT", Relevance = 1, IsPrimary = true }];
        var engine = new NewsStrategyEngine(
            NewsHypotheses.Momentum,
            "WIFUSDT",
            [news],
            new NewsOptions { Enabled = true },
            asset: new NewsAssetContext("WIFUSDT", "WIF"));
        var detail = engine.EvaluateDetailAt(new StrategyDefinition(), Context(candles), new CausalIndicatorCache(candles), candles.Count - 1);
        detail.Signal.Should().Be(TradingPlatform.Domain.Trading.SignalType.Buy);
    }

    [Fact]
    public void Point_in_time_filter_uses_the_requested_asset()
    {
        var open = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var early = Event("early", open.AddMinutes(-1), EventDirection.Bullish);
        early.Assets = ["ETH"];
        var late = Event("late", open.AddMinutes(5), EventDirection.Bullish);
        late.Assets = ["ETH"];
        var provider = Provider(early, late);
        provider.FeaturesAt(open, TimeSpan.FromMinutes(5), new NewsAssetContext("ETHUSDT", "ETH")).EventId.Should().Be("early");
        provider.FeaturesAt(open, TimeSpan.FromMinutes(5), new NewsAssetContext("SOLUSDT", "SOL")).NewsEventCount.Should().Be(0);
    }

    [Fact]
    public void A_listed_coin_can_be_named_outside_the_title()
    {
        var catalog = SampleCatalog();
        var ether = Story("ether", "A buyer steps back", "Ether purchases are stopping.", "");
        var body = Story("body", "A treasury note", "The filing has no ticker.", "The firm holds Bitcoin.");
        var general = Story("general", "OpenAI delays its IPO", "The company cited safety work.", "");
        var events = new NewsPipeline(new NewsOptions(), catalog: catalog).Build([ether, body, general]);

        events.Single(item => item.OriginalArticles[0].Id == "ether").PrimaryAsset.Should().Be("ETH");
        events.Single(item => item.OriginalArticles[0].Id == "body").PrimaryAsset.Should().Be("BTC");
        NewsAssetCatalog.NamesListedCoin(events.Single(item => item.OriginalArticles[0].Id == "general")).Should().BeFalse();
    }

    [Fact]
    public void Empty_catalog_does_not_assume_bitcoin()
    {
        var item = Event("n", Retrieved, EventDirection.Bullish);
        item.Assets = [];
        item.ProviderAssetIds = ["bitcoin"];
        NewsAssetCatalog.Empty.Bind(item, "Bitcoin ETF approval");
        item.AffectedAssets.Should().BeEmpty();
        item.PrimaryAsset.Should().BeNull();
    }

    private static NewsAssetCatalog SampleCatalog() =>
        new(
        [
            new NewsAssetIdentity("BTCUSDT", "BTC", "USDT", "bitcoin", "Bitcoin"),
            new NewsAssetIdentity("ETHUSDT", "ETH", "USDT", "ethereum", "Ethereum"),
            new NewsAssetIdentity("SOLUSDT", "SOL", "USDT", "solana", "Solana")
        ]);

    private static NewsFeatureProvider Provider(params NewsEvent[] events) =>
        new(events, new NewsOptions());

    private static NewsEvent Event(
        string id,
        DateTimeOffset published,
        EventDirection direction,
        double impact = 0.8,
        double sentiment = 0.8,
        double confidence = 0.9,
        double novelty = 1) =>
        new()
        {
            EventId = id,
            PublishedAtUtc = published,
            DetectedAtUtc = published,
            Assets = ["BTC"],
            EventType = NewsEventType.Etf,
            Direction = direction,
            Sentiment = sentiment,
            ImpactScore = impact,
            ConfidenceScore = confidence,
            NoveltyScore = novelty,
            SourceQualityScore = 0.8,
            TimestampPrecision = TimestampPrecision.Instant,
            OriginalArticles = [new NewsArticleRef { Id = id, Source = "CoinDesk", SourceUrl = "https://www.coindesk.com/" + id, PublishedAtUtc = published, Title = "Bitcoin" }]
        };

    private static RawNewsItem Story(string id, string title, string summary, string content) =>
        new()
        {
            Id = id,
            Provider = "rss",
            Source = "CoinDesk",
            SourceUrl = "https://www.coindesk.com/" + id,
            PublishedAtUtc = Retrieved,
            RetrievedAtUtc = Retrieved,
            Title = title,
            Summary = summary,
            Content = content
        };

    private static RawNewsItem Article(string source, string url, DateTimeOffset published, string title) =>
        new()
        {
            Id = source + url,
            Source = source,
            SourceUrl = url,
            PublishedAtUtc = published,
            RetrievedAtUtc = Retrieved,
            Title = title,
            Summary = title,
            RelatedAssets = ["BTC"],
            OriginalSourceId = url
        };

    private static MarketCandle Bar(DateTimeOffset open, decimal close, decimal volume, decimal taker) =>
        new()
        {
            OpenTime = open,
            CloseTime = open.AddHours(1),
            Open = close,
            High = close,
            Low = close,
            Close = close,
            Volume = volume,
            TakerBuyVolume = taker,
            IsClosed = true
        };

    private static StrategyContext Context(IReadOnlyList<MarketCandle> candles) =>
        new()
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = false,
            PositionSide = PositionSide.Long
        };

    private sealed class ExplodingProvider : INewsProvider
    {
        public string Name => "boom";

        public Task<NewsProviderBatch> FetchAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider must not be called.");
    }
}
