using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.News;
using TradingPlatform.Strategies.Engine;
using Xunit;

namespace TradingPlatform.NewsTests;

public sealed class NewsMarketConfirmationTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Bullish_news_and_bullish_market_is_a_long_candidate()
    {
        var decision = Evaluate(Direction: EventDirection.Bullish, up: true);
        decision.Signal.Should().Be(NewsMarketSignals.LongCandidate);
        decision.Detail.Signal.Should().Be(SignalType.Buy);
        decision.Reason.Should().Contain("LONG_CANDIDATE");
        decision.Reason.Should().Contain("AffectedAsset=SOL");
        decision.Reason.Should().Contain("OI=Unavailable");
        decision.FinalScore.Should().BeGreaterThanOrEqualTo(75);
        decision.Record.Should().NotBeNull();
        decision.Record!.ForwardReturn.Keys.Should().BeEquivalentTo(["5m", "15m", "30m", "1h", "4h"]);
        decision.Record.ForwardReturn.Values.Should().AllSatisfy(value => value.Should().BeNull());
    }

    [Fact]
    public void Bearish_news_and_bearish_market_is_a_short_candidate()
    {
        var decision = Evaluate(Direction: EventDirection.Bearish, up: false);
        decision.Signal.Should().Be(NewsMarketSignals.ShortCandidate);
        decision.Detail.Signal.Should().Be(SignalType.Sell);
        decision.Reason.Should().Contain("SHORT_CANDIDATE");
    }

    [Fact]
    public void Bullish_news_against_a_bearish_market_is_no_trade()
    {
        var decision = Evaluate(Direction: EventDirection.Bullish, up: false);
        decision.Signal.Should().Be(NewsMarketSignals.NoTrade);
        decision.Detail.Signal.Should().Be(SignalType.NoAction);
        decision.Reason.Should().Contain("Bullish news but strong opposing market confirmation.");
        decision.Record.Should().BeNull();
    }

    [Fact]
    public void Bearish_news_against_a_bullish_market_is_no_trade()
    {
        var decision = Evaluate(Direction: EventDirection.Bearish, up: true);
        decision.Signal.Should().Be(NewsMarketSignals.NoTrade);
        decision.Reason.Should().Contain("Bearish news but strong opposing market confirmation.");
    }

    [Fact]
    public void Insufficient_market_confirmation_is_no_trade()
    {
        var decision = Evaluate(Direction: EventDirection.Bullish, up: true, expandVolume: false, takerFraction: 0);
        decision.Signal.Should().Be(NewsMarketSignals.NoTrade);
        decision.Reason.Should().Contain("NO_TRADE");
    }

    [Theory]
    [InlineData(EventDirection.Neutral, "ignored")]
    [InlineData(EventDirection.Unknown, "ignored")]
    public void Neutral_and_unknown_news_are_ignored(EventDirection direction, string expected)
    {
        Evaluate(direction, up: true).Reason.Should().Contain(expected);
    }

    [Fact]
    public void Low_confidence_low_impact_expired_and_future_news_do_not_trade()
    {
        Evaluate(EventDirection.Bullish, up: true, confidence: 0.4).Reason.Should().Contain("confidence");
        Evaluate(EventDirection.Bullish, up: true, impact: 0.2).Reason.Should().Contain("impact");
        Evaluate(EventDirection.Bullish, up: true, ageMinutes: 90).Reason.Should().Contain("age");
        var future = Evaluate(EventDirection.Bullish, up: true, ageMinutes: -5);
        future.Signal.Should().Be(NewsMarketSignals.NoTrade);
        future.Reason.Should().Contain("Future news");
    }

    [Fact]
    public void Maps_asset_specific_multi_asset_and_global_events_onto_the_universe()
    {
        var catalog = Catalog();
        var when = Start.AddHours(20);
        var market = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>>();

        var btc = Analyze(catalog, market, when, EventFor("btc", ["BTC"], MarketScope.Asset));
        btc.Decisions.Select(item => item.Symbol).Should().Equal("BTCUSDT");

        var eth = Analyze(catalog, market, when, EventFor("eth", ["ETH"], MarketScope.Asset));
        eth.Decisions.Select(item => item.Symbol).Should().Equal("ETHUSDT");

        var sol = Analyze(catalog, market, when, EventFor("sol", ["SOL"], MarketScope.Asset));
        sol.Decisions.Select(item => item.Symbol).Should().Equal("SOLUSDT");

        var multi = Analyze(catalog, market, when, EventFor("multi", ["ETH", "ARB", "OP"], MarketScope.MultiAsset));
        multi.Decisions.Select(item => item.Symbol).Should().BeEquivalentTo(["ETHUSDT", "ARBUSDT"]);
        multi.Decisions.Single(item => item.Symbol == "ETHUSDT").Reason.Should().Contain("Relevance=1.00");
        multi.Decisions.Single(item => item.Symbol == "ARBUSDT").Reason.Should().Contain("Relevance=0.60");

        var global = Analyze(catalog, market, when, new NewsEvent
        {
            EventId = "cpi",
            PublishedAtUtc = when.AddMinutes(-10),
            Direction = EventDirection.Bearish,
            ImpactScore = 0.9,
            ConfidenceScore = 0.9,
            EventType = NewsEventType.Inflation,
            MarketScope = MarketScope.Global
        });
        global.Decisions.Select(item => item.Symbol).Should().BeEquivalentTo(catalog.Identities.Select(item => item.Symbol));
        global.Decisions.Should().OnlyContain(item => item.Reason.Contains("Relevance=0.35"));
    }

    [Fact]
    public void Scores_are_deterministic_and_weights_and_thresholds_are_configurable()
    {
        var first = Evaluate(EventDirection.Bullish, up: true);
        var second = Evaluate(EventDirection.Bullish, up: true);
        second.FinalScore.Should().Be(first.FinalScore);
        second.Reason.Should().Be(first.Reason);

        var lighter = Evaluate(EventDirection.Bullish, up: true, configure: options => options.Strategy.Weights.News = 10);
        lighter.Reason.Should().Contain("News=");
        lighter.NewsScore.Should().BeLessThan(first.NewsScore);

        var stricter = Evaluate(EventDirection.Bullish, up: true, configure: options => options.Strategy.MinTotalScore = 99);
        stricter.Signal.Should().Be(NewsMarketSignals.NoTrade);
        stricter.Reason.Should().Contain("below the configured minimum");
    }

    [Fact]
    public void Records_a_missing_timeframe_and_still_reads_the_frames_that_exist()
    {
        var missing = Evaluate(EventDirection.Bullish, up: true, omit: "1h");
        missing.Reason.Should().Contain("Trend=1h unavailable");

        var present = Evaluate(EventDirection.Bullish, up: true);
        present.Reason.Should().Contain("Trend=Bullish");
        present.Reason.Should().Contain("VolumeExpansion=");
        present.Reason.Should().Contain("OI=Unavailable");
        present.Reason.Should().NotContain("Trend=1h unavailable");
        present.Reason.Should().NotContain("Volume=5m unavailable");
    }

    [Fact]
    public void Rejects_an_unknown_timeframe_before_scoring()
    {
        var options = new NewsOptions { Enabled = true };
        options.Strategy.Timeframes.Trend = "2y";
        var report = NewsLiveAnalyzer.Analyze(options, Catalog(), [EventFor("btc", ["BTC"], MarketScope.Asset)], new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>>(), Start.AddHours(20));
        report.Decisions.Should().BeEmpty();
        report.Errors.Should().Contain(error => error.Contains("2y"));
    }

    [Fact]
    public void Disabled_news_emits_no_decisions_and_leaves_the_strategy_engine_path_unchanged()
    {
        var options = new NewsOptions { Enabled = false };
        var report = NewsLiveAnalyzer.Analyze(options, Catalog(), [EventFor("sol", ["SOL"], MarketScope.Asset)], Market("SOLUSDT", up: true), Start.AddHours(30));
        report.Decisions.Should().BeEmpty();
        report.Errors.Should().Contain(error => error.Contains("disabled"));

        var engine = new StrategyEngine();
        var signal = engine.Evaluate(
            new StrategyDefinition { Name = "EMA RSI Strategy", Timeframe = "15m", Template = "ema_rsi_trend" },
            new StrategyContext { ClosedCandles = [], HasOpenPosition = false },
            out var reason);
        signal.Should().Be(SignalType.NoAction);
        reason.Should().Be("No closed candles yet.");
    }

    [Fact]
    public void Live_outcome_tracking_fills_forward_return_when_a_later_candle_exists()
    {
        var decision = Evaluate(EventDirection.Bullish, up: true);
        var later = Bars(up: true, expandVolume: true, takerFraction: 0.9m).ToList();
        var reference = decision.Record!.ReferencePrice;
        later.Add(Bar(decision.Record.SignalTimestamp, reference * 1.02m, 10m, 8m));
        NewsMarketConfirmation.FillForwardReturns(decision.Record, later);
        decision.Record.ForwardReturn["5m"].Should().BeApproximately(0.02m, 0.0001m);
        decision.Record.ForwardReturn["4h"].Should().BeNull();
    }

    private static NewsMarketDecision Evaluate(
        EventDirection Direction,
        bool up,
        double impact = 0.90,
        double confidence = 0.94,
        int ageMinutes = 6,
        bool expandVolume = true,
        decimal takerFraction = -1,
        string? omit = null,
        Action<NewsOptions>? configure = null)
    {
        if (takerFraction < 0)
        {
            takerFraction = up ? 0.9m : 0.15m;
        }

        var candles = Bars(up, expandVolume, takerFraction);
        var decisionTime = candles[^1].CloseTime;
        var book = new Dictionary<string, IReadOnlyList<MarketCandle>>
        {
            ["5m"] = candles,
            ["15m"] = candles,
            ["1h"] = candles
        };
        if (omit is not null)
        {
            book.Remove(omit);
        }

        var options = new NewsOptions { Enabled = true };
        configure?.Invoke(options);
        return NewsMarketConfirmation.Evaluate(
            new NewsEvent
            {
                EventId = "sol-etf",
                PublishedAtUtc = decisionTime.AddMinutes(-ageMinutes),
                Direction = Direction,
                ImpactScore = impact,
                ConfidenceScore = confidence,
                EventType = NewsEventType.Etf,
                PrimaryAsset = "SOL",
                Assets = ["SOL"],
                AffectedAssets = [new AssetRelationship { BaseAsset = "SOL", Symbol = "SOLUSDT", Relevance = 1, IsPrimary = true }],
                MarketScope = MarketScope.Asset
            },
            new NewsAssetContext("SOLUSDT", "SOL", "solana"),
            decisionTime,
            book,
            options);
    }

    private static NewsLiveReport Analyze(
        NewsAssetCatalog catalog,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>> market,
        DateTimeOffset when,
        NewsEvent item) =>
        NewsLiveAnalyzer.Analyze(new NewsOptions { Enabled = true }, catalog, [item], market, when);

    private static NewsEvent EventFor(string id, IReadOnlyList<string> assets, MarketScope scope)
    {
        var links = assets.Select((asset, index) => new AssetRelationship
        {
            BaseAsset = asset,
            Symbol = asset + "USDT",
            Relevance = index == 0 ? 1 : NewsAssetCatalog.SecondaryRelevance,
            IsPrimary = index == 0
        }).ToList();
        return new NewsEvent
        {
            EventId = id,
            PublishedAtUtc = Start.AddHours(19),
            Direction = EventDirection.Bullish,
            ImpactScore = 0.9,
            ConfidenceScore = 0.9,
            EventType = NewsEventType.Etf,
            PrimaryAsset = assets[0],
            Assets = assets.ToList(),
            AffectedAssets = links,
            MarketScope = scope
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>> Market(string symbol, bool up)
    {
        var candles = Bars(up, expandVolume: true, takerFraction: up ? 0.9m : 0.15m);
        return new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>>
        {
            [symbol] = new Dictionary<string, IReadOnlyList<MarketCandle>>
            {
                ["5m"] = candles,
                ["15m"] = candles,
                ["1h"] = candles
            }
        };
    }

    private static NewsAssetCatalog Catalog() =>
        new(
        [
            new NewsAssetIdentity("BTCUSDT", "BTC", "USDT", "bitcoin", "Bitcoin"),
            new NewsAssetIdentity("ETHUSDT", "ETH", "USDT", "ethereum", "Ethereum"),
            new NewsAssetIdentity("SOLUSDT", "SOL", "USDT", "solana", "Solana"),
            new NewsAssetIdentity("ARBUSDT", "ARB", "USDT", "arbitrum", "Arbitrum")
        ]);

    private static List<MarketCandle> Bars(bool up, bool expandVolume, decimal takerFraction)
    {
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 80; i++)
        {
            var close = up ? 100m + (i * 0.5m) : 200m - (i * 0.5m);
            var volume = expandVolume && i == 79 ? 200m : 10m;
            candles.Add(Bar(Start.AddMinutes(i * 15), close, volume, volume * takerFraction));
        }

        return candles;
    }

    private static MarketCandle Bar(DateTimeOffset open, decimal close, decimal volume, decimal taker) =>
        new()
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
}
