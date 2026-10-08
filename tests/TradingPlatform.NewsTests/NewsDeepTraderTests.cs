using System.Net;
using System.Text;
using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.News;
using Xunit;

namespace TradingPlatform.NewsTests;

public sealed class NewsDeepTraderTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_article_is_one_event_and_a_later_copy_does_not_move_detection()
    {
        var firstSeen = Start;
        var copySeen = Start.AddMinutes(4);
        var events = new NewsPipeline(new NewsOptions { DeduplicationEnabled = true }).Build(
        [
            Article("https://example.com/etf", "SEC approves a bitcoin ETF", firstSeen, firstSeen.AddSeconds(20)),
            Article("https://example.com/etf", "SEC approves a bitcoin ETF", firstSeen.AddMinutes(3), copySeen)
        ]);

        events.Should().ContainSingle();
        events[0].DetectedAtUtc.Should().Be(firstSeen.AddSeconds(20));
        events[0].PublishedAtUtc.Should().Be(firstSeen);
    }

    [Fact]
    public async Task Each_unique_event_is_analyzed_once_and_detection_time_stays_put()
    {
        var seen = Start.AddSeconds(15);
        var item = new NewsPipeline(new NewsOptions()).Build([Article("https://example.com/sol", "Solana ETF approved", Start, seen)]).Single();
        var model = new ScriptedModel
        {
            Next = request => Ok(request.Model, Sample("SOLUSDT", 90, 10))
        };
        var options = AiOptions();
        options.Ai.FastModel = "fast";
        options.Ai.StrongModel = "fast";
        var clock = seen.AddSeconds(5);
        await new DeepNewsAnalyzer(options, model, clock: () => clock).AnalyzeAsync(item, Catalog(), CancellationToken.None);

        model.Models.Should().Equal("fast");
        item.DetectedAtUtc.Should().Be(seen);
        item.PublishedAtUtc.Should().Be(Start);
        item.ClassifiedAtUtc.Should().Be(clock);
        item.AiModel.Should().Be("fast");
        item.PromptVersion.Should().Be("news-deep-v2");
        item.AnalysisStatus.Should().Be("Ok");
        item.PrimaryAsset.Should().Be("SOL");
    }

    [Fact]
    public async Task A_high_impact_event_is_sent_to_the_strong_model()
    {
        var item = EventArticle();
        var model = new ScriptedModel
        {
            Next = request => Ok(request.Model, Sample("SOLUSDT", request.Model == "strong" ? 88 : 90, 10))
        };
        var options = AiOptions();
        options.Ai.FastModel = "fast";
        options.Ai.UseStrongModel = true;
        options.Ai.StrongModel = "strong";
        options.Ai.StrongImpactThreshold = 70;
        await new DeepNewsAnalyzer(options, model).AnalyzeAsync(item, Catalog(), CancellationToken.None);

        model.Models.Should().Equal("fast", "strong");
        item.UsedStrongModel.Should().BeTrue();
        item.AiModel.Should().Be("strong");
        item.ImpactScore.Should().BeApproximately(0.88, 0.001);
    }

    [Fact]
    public async Task The_fast_model_is_used_alone_unless_the_strong_model_is_enabled()
    {
        var item = EventArticle();
        var model = new ScriptedModel
        {
            Next = request => Ok(request.Model, Sample("SOLUSDT", 90, 10))
        };
        var options = AiOptions();
        options.Ai.FastModel = "fast";
        options.Ai.StrongModel = "strong";
        options.Ai.StrongImpactThreshold = 70;
        await new DeepNewsAnalyzer(options, model).AnalyzeAsync(item, Catalog(), CancellationToken.None);

        model.Models.Should().Equal("fast");
        item.UsedStrongModel.Should().BeFalse();
        item.AiModel.Should().Be("fast");
    }

    [Fact]
    public async Task A_timeout_does_not_trade_and_clears_a_keyword_direction()
    {
        var item = EventArticle();
        item.Direction = EventDirection.Bullish;
        var model = new ScriptedModel
        {
            Next = request => new NewsModelCompletion(false, null, "The model timed out.", true, "openai-compatible", request.Model)
        };
        var options = AiOptions();
        options.Ai.FastModel = "fast";
        options.Ai.StrongModel = "fast";
        await new DeepNewsAnalyzer(options, model).AnalyzeAsync(item, Catalog(), CancellationToken.None);

        item.AnalysisStatus.Should().Be("Timeout");
        item.Direction.Should().Be(EventDirection.Unknown);
        item.AffectedAssets.Should().BeEmpty();
        Decide(item).RejectionCode.Should().Be(NewsRejection.AiAnalysisFailed);
        Decide(item).Signal.Should().Be(NewsMarketSignals.NoTrade);
    }

    [Fact]
    public async Task A_model_failure_is_no_trade()
    {
        var item = EventArticle();
        var model = new ScriptedModel
        {
            Next = request => new NewsModelCompletion(false, null, "The model call failed.", false, "openai-compatible", request.Model)
        };
        var options = AiOptions();
        options.Ai.StrongModel = options.Ai.FastModel;
        await new DeepNewsAnalyzer(options, model).AnalyzeAsync(item, Catalog(), CancellationToken.None);
        item.AnalysisStatus.Should().Be("Failed");
        Decide(item).RejectionCode.Should().Be(NewsRejection.AiAnalysisFailed);
    }

    [Fact]
    public async Task Malformed_model_output_is_no_trade()
    {
        var item = EventArticle();
        var model = new ScriptedModel { Next = request => Ok(request.Model, "{\"signal\":\"BUY\"}") };
        var options = AiOptions();
        options.Ai.StrongModel = options.Ai.FastModel;
        await new DeepNewsAnalyzer(options, model).AnalyzeAsync(item, Catalog(), CancellationToken.None);
        item.AnalysisStatus.Should().Be("Invalid");
        item.Reason.Should().Contain("signal");
        var decision = Decide(item);
        decision.RejectionCode.Should().Be(NewsRejection.InvalidAiResponse);
        NewsRejection.IsRetryable(decision.RejectionCode).Should().BeTrue();
    }

    [Fact]
    public void Stored_news_is_not_treated_as_a_model_failure()
    {
        var item = EventArticle();
        item.AnalysisStatus = "Skipped";
        item.AnalysisError = "This news is already stored or too old. It was not sent to the model.";
        var decision = Decide(item);
        decision.Signal.Should().Be(NewsMarketSignals.NoTrade);
        decision.RejectionCode.Should().Be(NewsRejection.NewsTooOld);
        NewsRejection.IsRetryable(decision.RejectionCode).Should().BeFalse();
    }

    [Fact]
    public void Parser_accepts_the_schema_and_drops_symbols_outside_the_universe()
    {
        var json = Sample("SOLUSDT", 92, 12).Replace("SOLUSDT", "FAKEUSDT", StringComparison.Ordinal) is var swapped
            ? swapped.Replace("\"affectedAssets\":[", "\"affectedAssets\":[{\"symbol\":\"SOLUSDT\",\"role\":\"PRIMARY\",\"direction\":\"LONG\",\"impact\":90,\"confidence\":95,\"reason\":\"named\"},", StringComparison.Ordinal)
            : swapped;
        NewsAnalysisParser.TryParse(json, ["SOLUSDT"], out var analysis, out var error).Should().BeTrue(error);
        analysis.AffectedAssets.Select(asset => asset.Symbol).Should().Equal("SOLUSDT");
        analysis.Impact.Should().Be(92);
        analysis.AlreadyPricedIn.Should().Be(12);
        analysis.ParsedType.Should().Be(NewsEventType.Etf);
        analysis.ShouldConsiderTrading.Should().BeTrue();
    }

    [Fact]
    public void Parser_rejects_scores_above_100_and_a_missing_horizon()
    {
        NewsAnalysisParser.TryParse(Sample("SOLUSDT", 140, 10), ["SOLUSDT"], out _, out var high).Should().BeFalse();
        high.Should().Contain("required");
        NewsAnalysisParser.TryParse("not-json", ["SOLUSDT"], out _, out var broken).Should().BeFalse();
        broken.Should().Contain("JSON");
        NewsAnalysisParser.ParseHorizon("1h").Should().Be(60);
        NewsAnalysisParser.ParseHorizon("4h").Should().Be(240);
        NewsAnalysisParser.ParseHorizon("1-4 hours").Should().Be(240);
        NewsAnalysisParser.ParseHorizon("within 24 hours").Should().Be(1440);
        NewsAnalysisParser.ParseHorizon("about 2 hours").Should().Be(120);
        NewsAnalysisParser.ParseHorizon("15-30 minutes").Should().Be(30);
        NewsAnalysisParser.ParseHorizon("intraday").Should().Be(240);
        NewsAnalysisParser.ParseHorizon("30m").Should().Be(30);
        NewsAnalysisParser.ParseHorizon("no idea").Should().Be(0);
    }

    [Fact]
    public void News_that_names_no_listed_coin_is_not_a_model_failure()
    {
        var item = EventArticle();
        item.AnalysisStatus = "NoCoin";
        item.AnalysisError = "This news is not about a Binance coin. It was not sent to the model.";
        var decision = Decide(item);
        decision.Signal.Should().Be(NewsMarketSignals.NoTrade);
        decision.RejectionCode.Should().Be(NewsRejection.UnknownAsset);
        decision.Reason.Should().Contain("not about a Binance coin");
        NewsRejection.IsRetryable(decision.RejectionCode).Should().BeFalse();
        NewsActivityCopy.Why("NO_TRADE", decision.RejectionCode, null, "NotSent")
            .Should().Be("This news is not about a Binance coin. No order was sent.");
    }

    [Fact]
    public void An_unknown_asset_is_no_trade()
    {
        var (item, _, _, _, options) = Ready();
        item.AffectedAssets.Clear();
        NewsTradeDecisionEngine.Decide(item, new NewsAssetContext("DOGEUSDT", "DOGE"), item.ClassifiedAtUtc!.Value.AddSeconds(1), Book(Up(true)), EarlyFacts(), options)
            .RejectionCode.Should().Be(NewsRejection.UnknownAsset);
    }

    [Fact]
    public void Stale_news_low_confidence_and_a_rumor_do_not_trade()
    {
        var (item, facts, candles, when, options) = Ready();
        item.PublishedAtUtc = when.AddHours(-3);
        item.DetectedAtUtc = item.PublishedAtUtc.AddSeconds(1);
        item.ClassifiedAtUtc = item.DetectedAtUtc.AddSeconds(1);
        Decide(item, facts, candles, when, options).RejectionCode.Should().Be(NewsRejection.NewsTooOld);

        var (young, youngFacts, youngCandles, youngWhen, youngOptions) = Ready();
        young.ConfidenceScore = 0.2;
        young.AffectedAssets[0].Confidence = 20;
        Decide(young, youngFacts, youngCandles, youngWhen, youngOptions).RejectionCode.Should().Be(NewsRejection.LowConfidence);

        var (rumor, rumorFacts, rumorCandles, rumorWhen, rumorOptions) = Ready();
        rumor.VerificationStatus = "rumor";
        Decide(rumor, rumorFacts, rumorCandles, rumorWhen, rumorOptions).RejectionCode.Should().Be(NewsRejection.UnverifiedSource);
    }

    [Fact]
    public void Early_bullish_news_with_a_confirming_market_is_long()
    {
        var (item, facts, candles, when, options) = Ready();
        var decision = Decide(item, facts, candles, when, options);
        decision.Signal.Should().Be(NewsMarketSignals.LongCandidate);
        decision.RejectionCode.Should().BeNull();
        NewsTradeAdapterDirection(decision.Signal).Should().Be("LONG");
    }

    [Fact]
    public void The_same_news_after_a_large_move_is_already_priced_in()
    {
        var (item, facts, candles, when, options) = Ready();
        var chased = facts with { Return15m = 11, ReturnSinceDetection = 11 };
        var decision = Decide(item, chased, candles, when, options);
        decision.Signal.Should().Be(NewsMarketSignals.NoTrade);
        decision.RejectionCode.Should().Be(NewsRejection.AlreadyPricedIn);
    }

    [Fact]
    public void A_bearish_market_blocks_bullish_news()
    {
        var (item, facts, _, when, options) = Ready();
        Decide(item, facts, Book(Up(false)), when, options).RejectionCode.Should().Be(NewsRejection.MarketConflict);
    }

    [Fact]
    public void Btc_selling_off_blocks_an_ordinary_altcoin_long()
    {
        var (item, facts, candles, when, options) = Ready();
        item.ConfidenceScore = 0.8;
        item.ImpactScore = 0.8;
        item.AffectedAssets[0].Confidence = 80;
        item.AffectedAssets[0].Impact = 80;
        var crashing = facts with { BtcReturn15m = -1.2, BtcReturn1h = -2 };
        Decide(item, crashing, candles, when, options).RejectionCode.Should().Be(NewsRejection.MarketConflict);
    }

    [Fact]
    public void Stale_or_missing_market_data_wide_spread_and_thin_liquidity_do_not_trade()
    {
        var (item, facts, candles, when, options) = Ready();
        Decide(item, facts with { Stale = true }, candles, when, options).RejectionCode.Should().Be(NewsRejection.StaleMarketData);
        Decide(item, facts with { OpenInterest = null }, candles, when, options).RejectionCode.Should().Be(NewsRejection.InsufficientMarketData);
        Decide(item, facts with { SpreadPercent = 0.4m }, candles, when, options).RejectionCode.Should().Be(NewsRejection.WideSpread);
        Decide(item, facts with { QuoteVolume24h = 1_000m }, candles, when, options).RejectionCode.Should().Be(NewsRejection.LowLiquidity);
    }

    [Fact]
    public void A_small_expected_move_does_not_cover_fees_spread_and_slippage()
    {
        var (item, facts, candles, when, options) = Ready();
        Decide(item, facts with { AtrPercent = 0.05 }, candles, when, options).RejectionCode.Should().Be(NewsRejection.ExpectedEdgeTooSmall);
    }

    [Fact]
    public void An_open_position_cooldown_or_a_second_pass_does_not_trade()
    {
        var (item, facts, candles, when, options) = Ready();
        NewsTradeDecisionEngine.Decide(item, Asset(), when, candles, facts, options, new NewsDecisionContext(SymbolOccupied: true))
            .RejectionCode.Should().Be(NewsRejection.ExistingPosition);
        NewsTradeDecisionEngine.Decide(item, Asset(), when, candles, facts, options, new NewsDecisionContext(InCooldown: true))
            .RejectionCode.Should().Be(NewsRejection.Cooldown);
        NewsTradeDecisionEngine.Decide(item, Asset(), when, candles, facts, options, new NewsDecisionContext(EventAlreadyTraded: true))
            .RejectionCode.Should().Be(NewsRejection.EventAlreadyTraded);
        NewsTradeDecisionEngine.Decide(item, Asset(), when, candles, facts, options, new NewsDecisionContext(DuplicateEvent: true))
            .RejectionCode.Should().Be(NewsRejection.DuplicateEvent);
    }

    [Fact]
    public void A_decision_cannot_see_a_candle_that_closes_after_it()
    {
        var candles = Up(true);
        var when = candles[^1].CloseTime;
        var future = Bar(when, 999m, 500m, 450m);
        var book = Book(candles.Concat([future]).ToList());
        var options = new NewsOptions { Enabled = true };
        var without = NewsMarketFactsBuilder.FromCandles("SOLUSDT", Book(candles), when, when.AddMinutes(-10), options);
        var withFuture = NewsMarketFactsBuilder.FromCandles("SOLUSDT", book, when, when.AddMinutes(-10), options);
        withFuture.Price.Should().Be(without.Price);
        withFuture.Price.Should().NotBe(999m);
        NewsLatency.HasLookAhead(when.AddMinutes(-5), when.AddMinutes(-4), when.AddMinutes(-3), when, when.AddMinutes(-1)).Should().BeTrue();
        NewsLatency.HasLookAhead(when.AddMinutes(-5), when.AddMinutes(-4), when.AddMinutes(-3), when, when.AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void Out_of_order_timestamps_are_no_trade()
    {
        var (item, facts, candles, when, options) = Ready();
        item.ClassifiedAtUtc = when.AddMinutes(1);
        Decide(item, facts, candles, when, options).RejectionCode.Should().Be(NewsRejection.LookAhead);
    }

    [Fact]
    public void Prompt_carries_the_article_and_version_and_no_market_numbers()
    {
        var item = EventArticle();
        var (system, user) = NewsAnalysisPrompt.Build(item, AiOptions(), ["SOLUSDT", "BTCUSDT"]);
        system.Should().Contain("news-deep-v2");
        system.Should().Contain("30m");
        system.Should().Contain("shouldConsiderTrading");
        user.Should().Contain("Solana ETF approved");
        user.Should().Contain("SOLUSDT");
        user.Should().NotContain("12345.67");
        NewsLatency.Milliseconds(item.PublishedAtUtc, item.DetectedAtUtc).Should().Be(15_000);
    }

    [Fact]
    public async Task The_model_client_reports_a_timeout_and_reads_json_content()
    {
        var handler = new CancelHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://models.example/") };
        var model = new OpenAiCompatibleLanguageModel(client, AiOptions());
        var completion = await model.CompleteAsync(new NewsModelRequest("fast", "system", "user", TimeSpan.FromSeconds(5)), CancellationToken.None);
        completion.TimedOut.Should().BeTrue();
        completion.Succeeded.Should().BeFalse();
        OpenAiCompatibleLanguageModel.ReadContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"ok\\\":true}\"}}]}").Should().Contain("ok");
        OpenAiCompatibleLanguageModel.ChatUrl("https://api.openai.com/v1/").Should().Be("https://api.openai.com/v1/chat/completions");
    }

    [Fact]
    public void Risk_rejections_keep_the_existing_engine_reasons()
    {
        NewsRejection.FromRisk("This coin already has an open Isolated position. Binance USD-M is one position per coin.").Should().Be(NewsRejection.ExistingPosition);
        NewsRejection.FromRisk("Consecutive loss limit reached. New entries are locked until the cooldown ends.").Should().Be(NewsRejection.Cooldown);
        NewsRejection.FromRisk("Market data is stale. New Isolated entries are locked.").Should().Be(NewsRejection.StaleMarketData);
        NewsRejection.FromRisk("Daily loss reached the limit.").Should().Be(NewsRejection.RiskLimit);
        NewsRejection.IsRetryable(NewsRejection.AiAnalysisFailed).Should().BeTrue();
        NewsRejection.IsRetryable(NewsRejection.AlreadyPricedIn).Should().BeFalse();
    }

    private static NewsMarketDecision Decide(NewsEvent item) =>
        Decide(item, EarlyFacts(), Book(Up(true)), item.ClassifiedAtUtc?.AddSeconds(1) ?? Start.AddMinutes(1), AiOptions());

    private static NewsMarketDecision Decide(NewsEvent item, NewsMarketFacts facts, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> candles, DateTimeOffset when, NewsOptions options) =>
        NewsTradeDecisionEngine.Decide(item, Asset(), when, candles, facts, options);

    private static string NewsTradeAdapterDirection(string signal) => signal switch
    {
        NewsMarketSignals.LongCandidate => "LONG",
        NewsMarketSignals.ShortCandidate => "SHORT",
        _ => "NO_TRADE"
    };

    private static (NewsEvent Item, NewsMarketFacts Facts, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> Candles, DateTimeOffset When, NewsOptions Options) Ready()
    {
        var candles = Up(true);
        var when = candles[^1].CloseTime;
        var item = new NewsEvent
        {
            EventId = "sol-etf",
            PublishedAtUtc = when.AddMinutes(-6),
            DetectedAtUtc = when.AddMinutes(-6).AddSeconds(20),
            ClassifiedAtUtc = when.AddMinutes(-5),
            AnalysisStatus = "Ok",
            VerificationStatus = "confirmed",
            SourceReliability = 90,
            Direction = EventDirection.Bullish,
            EventType = NewsEventType.Etf,
            ImpactScore = 0.90,
            ConfidenceScore = 0.95,
            NoveltyScore = 0.95,
            AlreadyPricedIn = 10,
            ShouldConsiderTrading = true,
            ExpectedHorizonMinutes = 60,
            PrimaryAsset = "SOL",
            Assets = ["SOL"],
            AffectedAssets =
            [
                new AssetRelationship
                {
                    Symbol = "SOLUSDT",
                    BaseAsset = "SOL",
                    IsPrimary = true,
                    Role = "PRIMARY",
                    Relevance = 1,
                    AssetDirection = "LONG",
                    Impact = 90,
                    Confidence = 95,
                    Reason = "The approved ETF is for Solana."
                }
            ],
            AiProvider = "openai-compatible",
            AiModel = "gpt-4.1",
            PromptVersion = "news-deep-v1",
            OriginalArticles = [new NewsArticleRef { Id = "a", Title = "Solana ETF approved", Source = "SEC" }]
        };
        return (item, EarlyFacts(), Book(candles), when, AiOptions());
    }

    private static NewsMarketFacts EarlyFacts() => new()
    {
        Symbol = "SOLUSDT",
        Price = 100m,
        ReturnSinceDetection = 0.5,
        Return1m = 0.1,
        Return5m = 0.4,
        Return15m = 0.5,
        Return1h = 0.2,
        RelativeVolume = 2.2,
        AtrPercent = 1.2,
        TakerImbalance = 0.4,
        SpreadPercent = 0.01m,
        QuoteVolume24h = 50_000_000m,
        OpenInterest = 1_000m,
        OpenInterestChange = 4,
        FundingRate = 0.0001m,
        BtcReturn5m = 0.1,
        BtcReturn15m = 0.1,
        BtcReturn1h = 0.2,
        EvaluateBtc = true
    };

    private static NewsAssetContext Asset() => new("SOLUSDT", "SOL");

    private static NewsOptions AiOptions() => new()
    {
        Enabled = true,
        Ai = new NewsAiOptions { Enabled = true, ApiKey = "test-key", PromptVersion = "news-deep-v2" }
    };

    private static NewsEvent EventArticle() =>
        new NewsPipeline(new NewsOptions()).Build([Article("https://example.com/sol", "Solana ETF approved", Start, Start.AddSeconds(15))]).Single();

    private static RawNewsItem Article(string url, string title, DateTimeOffset published, DateTimeOffset retrieved) => new()
    {
        Id = url,
        Provider = "rss",
        Source = "CoinDesk",
        SourceUrl = url,
        PublishedAtUtc = published,
        RetrievedAtUtc = retrieved,
        Title = title,
        Summary = title,
        Content = title + " The regulator confirmed the decision."
    };

    private static NewsAssetCatalog Catalog() => new(
    [
        new NewsAssetIdentity("BTCUSDT", "BTC", "USDT", "bitcoin", "Bitcoin"),
        new NewsAssetIdentity("SOLUSDT", "SOL", "USDT", "solana", "Solana")
    ]);

    private static string Sample(string symbol, int impact, int priced) =>
        $$"""
        {"eventType":"etf","verificationStatus":"confirmed","sourceReliability":90,"overallConfidence":95,"impact":{{impact}},"novelty":95,"alreadyPricedIn":{{priced}},"expectedHorizon":"1h","marketMechanism":"A new regulated buyer has to acquire the coin.","affectedAssets":[{"symbol":"{{symbol}}","role":"PRIMARY","direction":"LONG","impact":90,"confidence":95,"reason":"The product holds this coin."}],"riskFlags":[],"shouldConsiderTrading":true}
        """;

    private static NewsModelCompletion Ok(string model, string json) =>
        new(true, json, null, false, "openai-compatible", model);

    private static Dictionary<string, IReadOnlyList<MarketCandle>> Book(IReadOnlyList<MarketCandle> candles) => new()
    {
        ["5m"] = candles,
        ["15m"] = candles,
        ["1h"] = candles
    };

    private static List<MarketCandle> Up(bool up)
    {
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 80; i++)
        {
            var close = up ? 100m + (i * 0.5m) : 200m - (i * 0.5m);
            var volume = i == 79 ? 200m : 10m;
            var taker = up ? 0.9m : 0.15m;
            candles.Add(Bar(Start.AddMinutes(i * 15), close, volume, volume * taker));
        }

        return candles;
    }

    private static MarketCandle Bar(DateTimeOffset open, decimal close, decimal volume, decimal taker) => new()
    {
        OpenTime = open,
        CloseTime = open.AddMinutes(15),
        Open = close,
        High = close * 1.004m,
        Low = close * 0.996m,
        Close = close,
        Volume = volume,
        TakerBuyVolume = taker,
        IsClosed = true
    };

    private sealed class ScriptedModel : INewsLanguageModel
    {
        public List<string> Models { get; } = [];
        public Func<NewsModelRequest, NewsModelCompletion> Next { get; set; } = _ => new(false, null, "missing", false, "test", "fast");

        public Task<NewsModelCompletion> CompleteAsync(NewsModelRequest request, CancellationToken cancellationToken)
        {
            Models.Add(request.Model);
            return Task.FromResult(Next(request));
        }
    }

    private sealed class CancelHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("timed out");
    }
}
