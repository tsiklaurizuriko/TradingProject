using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class SecondPassStrategyTests
{
    [Fact]
    public void Impulse_recent_pullback_is_an_entry_candidate()
    {
        var candles = ImpulseSeries(reclaim: true);
        var signal = ImpulseEval(candles, maxAge: 12);
        signal.Signal.Should().Be(SignalType.Buy, "reason {0}", signal.Reason);
        signal.Snapshot.Should().NotBeNull();
        signal.Snapshot!["expired"].Should().Be(0m);
        signal.Snapshot["pullback"].Should().Be(1m);
        signal.Snapshot["impulseAge"]!.Value.Should().BeLessThanOrEqualTo(12m);
        signal.SuggestedStop.Should().NotBeNull();
        signal.SuggestedStop!.Value.Should().BeLessThan(candles[^1].Close);
    }

    [Fact]
    public void Impulse_older_than_max_age_does_not_enter()
    {
        var candles = Flat(28, 100m);
        candles[14] = Bar(14, 112m, high: 114m, low: 100m);
        candles[15] = Bar(15, 124m, high: 126m, low: 110m);
        candles[16] = Bar(16, 140m, high: 142m, low: 120m);
        var signal = ImpulseEval(candles, maxAge: 6);
        signal.Signal.Should().Be(SignalType.NoAction, "reason {0}", signal.Reason);
        signal.Reason.Should().Contain("expired");
        signal.Snapshot!["expired"].Should().Be(1m);
    }

    [Fact]
    public void Impulse_pullback_before_completion_is_not_a_setup()
    {
        var candles = Flat(40, 100m);
        for (var i = 16; i <= 20; i++)
        {
            candles[i] = Bar(i, 100m - (20 - i), high: 101m, low: 90m);
        }

        candles[24] = Bar(24, 130m, high: 131m, low: 125m);
        candles[25] = Bar(25, 131m, high: 132m, low: 128m);
        candles[26] = Bar(26, 132m, high: 133m, low: 129m);
        var signal = ImpulseEval(candles, maxAge: 12);
        signal.Signal.Should().Be(SignalType.NoAction, "reason {0}", signal.Reason);
        signal.Reason.Should().Contain("pullback=0");
    }

    [Fact]
    public void Impulse_invalidated_after_pullback_does_not_enter()
    {
        var candles = ImpulseSeries(reclaim: true);
        candles[24] = Bar(24, 80m, high: 110m, low: 70m);
        candles[25] = Bar(25, 130m, high: 132m, low: 110m);
        var signal = ImpulseEval(candles, maxAge: 12);
        signal.Signal.Should().Be(SignalType.NoAction, "reason {0}", signal.Reason);
        signal.Reason.Should().Contain("invalidated");
    }

    [Fact]
    public void Impulse_same_candle_does_not_emit_a_second_signal()
    {
        var candles = ImpulseSeries(reclaim: true);
        var first = ImpulseEval(candles, maxAge: 12);
        var second = ImpulseEval(candles, maxAge: 12);
        first.Signal.Should().Be(SignalType.Buy);
        second.Signal.Should().Be(first.Signal);
        second.SuggestedStop.Should().Be(first.SuggestedStop);
        second.Reason.Should().Be(first.Reason);
        var open = candles[^1].OpenTime;
        var bot = Guid.Parse("22222222-2222-2222-2222-222222222222");
        CandleIdempotency.Key(bot, open, live: false).Should().Be(CandleIdempotency.Key(bot, open, live: false));
    }

    [Fact]
    public void Impulse_short_history_does_not_trade()
    {
        var signal = ImpulseEval(Flat(8, 100m), maxAge: 32);
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("warmup");
    }

    [Fact]
    public void Impulse_ignores_a_future_bar()
    {
        var candles = ImpulseSeries(reclaim: true);
        var at = candles.Count - 1;
        var p = ImpulseParams(12);
        var seen = RefactoredStrategyEvaluator.Evaluate(p, candles, at, Context(candles), new CausalIndicatorCache(candles));
        var future = candles.Concat(new[] { Bar(candles.Count, 40m, high: 41m, low: 10m) }).ToList();
        var later = RefactoredStrategyEvaluator.Evaluate(p, future, at, Context(future), new CausalIndicatorCache(future));
        later.Signal.Should().Be(seen.Signal);
        later.SuggestedStop.Should().Be(seen.SuggestedStop);
    }

    [Fact]
    public void Cluc_open_long_exits_when_the_higher_timeframe_turns_bearish()
    {
        var candles = Flat(40, 100m);
        var signal = ClucEval(candles, OpenLong(candles), BearishHtf());
        signal.Signal.Should().Be(SignalType.Exit, "reason {0}", signal.Reason);
        signal.Reason.Should().Contain("bearish");
        signal.Reason.Should().NotContain("DATA_UNAVAILABLE");
    }

    [Fact]
    public void Cluc_open_long_keeps_managing_when_higher_timeframe_data_is_missing()
    {
        var candles = Flat(40, 100m);
        var signal = ClucEval(candles, OpenLong(candles), htf: null);
        signal.Signal.Should().Be(SignalType.Hold, "reason {0}", signal.Reason);
        signal.Reason.Should().Contain("missing");
        signal.Reason.Should().Contain("not treated as bearish");
    }

    [Fact]
    public void Cluc_without_a_position_does_not_enter_on_a_bearish_higher_timeframe()
    {
        var candles = Flat(40, 100m);
        var signal = ClucEval(candles, Context(candles), BearishHtf());
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("bullish");
        signal.Reason.Should().NotContain("DATA_UNAVAILABLE");
    }

    [Fact]
    public void Cluc_without_a_position_does_not_enter_when_higher_timeframe_data_is_missing()
    {
        var candles = Flat(40, 100m);
        var signal = ClucEval(candles, Context(candles), htf: null);
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("DATA_UNAVAILABLE");
    }

    [Fact]
    public void Cluc_bullish_higher_timeframe_can_enter()
    {
        var candles = ClucReclaim();
        var signal = ClucEval(candles, Context(candles, BullishHtf(candles[^1].CloseTime)), BullishHtf(candles[^1].CloseTime));
        signal.Signal.Should().Be(SignalType.Buy, "reason {0}", signal.Reason);
        signal.SuggestedStop.Should().NotBeNull();
    }

    [Fact]
    public void Cluc_same_candle_does_not_change_the_action()
    {
        var candles = Flat(40, 100m);
        var context = OpenLong(candles);
        var htf = BearishHtf();
        var first = ClucEval(candles, context, htf);
        var second = ClucEval(candles, context, htf);
        second.Signal.Should().Be(first.Signal);
        second.Reason.Should().Be(first.Reason);
    }

    [Fact]
    public void Every_v2_template_rejects_a_short_history_and_an_unclosed_bar()
    {
        foreach (var key in StrategyTemplateKeys.Refactored)
        {
            var cold = EvalKey(key, Flat(5, 100m));
            cold.Signal.Should().NotBe(SignalType.Buy, key);
            cold.Signal.Should().NotBe(SignalType.Sell, key);

            var candles = Flat(80, 100m);
            candles[^1].IsClosed = false;
            var openBar = EvalKey(key, candles);
            openBar.Signal.Should().Be(SignalType.NoAction, key);
            openBar.Reason.Should().Contain("Unclosed");

            candles[^1].IsClosed = true;
            var first = EvalKey(key, candles);
            var second = EvalKey(key, candles);
            second.Signal.Should().Be(first.Signal, key);
            second.Reason.Should().Be(first.Reason, key);
        }
    }

    [Fact]
    public void Research_workflow_lists_each_v2_once_without_enabling_live_trading()
    {
        StrategyTemplateKeys.Refactored.Should().OnlyHaveUniqueItems();
        StrategyTemplateKeys.All.Should().OnlyHaveUniqueItems();
        var present = new List<string>();
        foreach (var key in StrategyTemplateKeys.Refactored)
        {
            StrategyTemplateKeys.IsResearchWorkflow(key).Should().BeTrue();
            StrategyTemplateKeys.IsOperatorCatalog(key).Should().BeFalse();
            StrategyTemplates.DefaultsFor(key, false).Timeframe.Should().Be(StrategyTemplateKeys.TimeframesFor(key)[0]);
            StrategyTemplateKeys.ContainsTemplate(present, key).Should().BeFalse();
            present.Add(key);
            StrategyTemplateKeys.ContainsTemplate(present, key).Should().BeTrue();
        }

        StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatchV2, false).MaxImpulseAgeBars.Should().Be(32);
        var roundTrip = StrategyTemplates.Read(StrategyTemplates.Build("Impulse Catch v2", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatchV2, false)));
        roundTrip.MaxImpulseAgeBars.Should().Be(32);
        var legacy = StrategyTemplates.Read("""{"template":"impulse_catch_v2","timeframe":"15m","params":{}}""");
        legacy.MaxImpulseAgeBars.Should().Be(32);

        var root = RepoRoot();
        File.ReadAllText(Path.Combine(root, "src", "TradingPlatform.Api", "appsettings.json")).Should().Contain("\"LiveTradingEnabled\": false");
        File.ReadAllText(Path.Combine(root, "src", "TradingPlatform.Workers", "appsettings.json")).Should().Contain("\"LiveTradingEnabled\": false");
    }

    private static StrategySignalDetail EvalKey(string key, List<MarketCandle> candles)
    {
        var p = StrategyTemplates.DefaultsFor(key, false);
        return RefactoredStrategyEvaluator.Evaluate(p, candles, candles.Count - 1, Context(candles), new CausalIndicatorCache(candles));
    }

    private static StrategySignalDetail ImpulseEval(List<MarketCandle> candles, int maxAge)
    {
        var p = ImpulseParams(maxAge);
        return RefactoredStrategyEvaluator.Evaluate(p, candles, candles.Count - 1, Context(candles), new CausalIndicatorCache(candles));
    }

    private static StrategyTemplateParams ImpulseParams(int maxAge) =>
        StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatchV2, false) with
        {
            EntryLookback = 4,
            EmaFast = 3,
            RelativeVolumePeriod = 4,
            AtrPeriod = 5,
            MaxImpulseAgeBars = maxAge,
            PriceDisplacementAtr = 0.15m,
            MinimumRelativeVolume = 1m,
            StopAtrMultiplier = 4m
        };

    private static List<MarketCandle> ImpulseSeries(bool reclaim)
    {
        var candles = Flat(28, 100m);
        candles[18] = Bar(18, 110m, high: 112m, low: 100m);
        candles[19] = Bar(19, 120m, high: 122m, low: 108m);
        candles[20] = Bar(20, 130m, high: 132m, low: 118m);
        candles[21] = Bar(21, 142m, high: 144m, low: 128m);
        candles[22] = Bar(22, 108m, high: 130m, low: 100m);
        candles[23] = Bar(23, 104m, high: 110m, low: 100m);
        candles[24] = Bar(24, 103m, high: 108m, low: 99m);
        candles[25] = reclaim
            ? Bar(25, 120m, high: 122m, low: 108m)
            : Bar(25, 102m, high: 104m, low: 100m);
        return candles.Take(26).ToList();
    }

    private static StrategySignalDetail ClucEval(List<MarketCandle> candles, StrategyContext context, CausalIndicatorCache? htf)
    {
        var p = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ClucMay72018V2, false) with { TrendEmaPeriod = 5 };
        context = new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = context.HasOpenPosition,
            PositionSide = context.PositionSide,
            AverageEntryPrice = context.AverageEntryPrice,
            PositionOpenedAt = context.PositionOpenedAt,
            ProtectiveStopPrice = context.ProtectiveStopPrice,
            HigherTimeframeCache = htf
        };
        return RefactoredStrategyEvaluator.Evaluate(p, candles, candles.Count - 1, context, new CausalIndicatorCache(candles));
    }

    private static List<MarketCandle> ClucReclaim()
    {
        var candles = Flat(36, 100m);
        for (var i = 20; i <= 33; i++)
        {
            var close = 100m - ((i - 19) * 1.2m);
            candles[i] = Bar(i, close, high: close + 0.3m, low: close - 0.6m);
        }

        candles[34] = Bar(34, 70m, high: 84m, low: 68m);
        candles[35] = Bar(35, 90m, high: 92m, low: 78m);
        return candles;
    }

    private static StrategyContext OpenLong(IReadOnlyList<MarketCandle> candles) =>
        new()
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = true,
            PositionSide = PositionSide.Long,
            AverageEntryPrice = candles[^1].Close,
            PositionOpenedAt = candles[^1].CloseTime
        };

    private static StrategyContext Context(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache? htf = null) =>
        new()
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HigherTimeframeCache = htf
        };

    private static CausalIndicatorCache BullishHtf(DateTimeOffset signalClose)
    {
        var start = signalClose.AddHours(-20);
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 12; i++)
        {
            var close = 80m + i;
            candles.Add(Hour(start, i, close));
        }

        return new CausalIndicatorCache(candles);
    }

    private static CausalIndicatorCache BearishHtf()
    {
        var start = new DateTimeOffset(2023, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 12; i++)
        {
            candles.Add(Hour(start, i, 200m - (i * 5m)));
        }

        return new CausalIndicatorCache(candles);
    }

    private static MarketCandle Hour(DateTimeOffset start, int index, decimal close) =>
        new()
        {
            OpenTime = start.AddHours(index),
            CloseTime = start.AddHours(index + 1),
            Open = close + 0.2m,
            High = close + 0.5m,
            Low = close - 0.5m,
            Close = close,
            Volume = 10m,
            IsClosed = true,
            ExchangeTimestamp = start.AddHours(index + 1)
        };

    private static List<MarketCandle> Flat(int count, decimal price) =>
        Enumerable.Range(0, count).Select(i => Bar(i, price)).ToList();

    private static MarketCandle Bar(int index, decimal close, decimal? high = null, decimal? low = null) =>
        new()
        {
            OpenTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(index * 15),
            CloseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes((index + 1) * 15),
            Open = close - 0.4m,
            High = high ?? close + 0.4m,
            Low = low ?? close - 0.4m,
            Close = close,
            Volume = 10m,
            IsClosed = true,
            ExchangeTimestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes((index + 1) * 15)
        };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TradingPlatform.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
