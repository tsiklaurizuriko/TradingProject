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
    public void Impulse_buys_the_first_pump_bar_on_a_trending_coin()
    {
        var candles = PumpSeries();
        var signal = PumpEval(candles, Context(candles, Days(close: 90m, high: 130m)));
        signal.Signal.Should().Be(SignalType.Buy, "reason {0}", signal.Reason);
        signal.SuggestedStop.Should().Be(117m * 0.88m);
        signal.SuggestedTakeProfit.Should().BeNull();
        signal.Snapshot!["volRatio"].Should().Be(5m);
        signal.Snapshot["ret30d"]!.Value.Should().BeApproximately(0.3m, 0.001m);
    }

    [Fact]
    public void Impulse_skips_a_coin_without_a_thirty_day_trend()
    {
        var candles = PumpSeries();
        var signal = PumpEval(candles, Context(candles, Days(close: 100m, high: 130m)));
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("30 days");
    }

    [Fact]
    public void Impulse_skips_a_fresh_thirty_day_breakout()
    {
        var candles = PumpSeries();
        var signal = PumpEval(candles, Context(candles, Days(close: 90m, high: 110m)));
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("30-day high");
    }

    [Fact]
    public void Impulse_skips_a_volume_climax()
    {
        var candles = PumpSeries(signalVolume: 400_000m);
        var signal = PumpEval(candles, Context(candles, Days(close: 90m, high: 130m)));
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("climax");
    }

    [Fact]
    public void Impulse_does_not_buy_when_the_previous_bar_already_crossed()
    {
        var candles = PumpSeries();
        candles[^2] = PumpBar(candles.Count - 2, open: 100m, close: 116.5m, high: 117m, low: 100m, volume: 10_000m);
        var signal = PumpEval(candles, Context(candles, Days(close: 90m, high: 130m)));
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("first cross");
    }

    [Fact]
    public void Impulse_without_daily_candles_sends_no_order()
    {
        var candles = PumpSeries();
        var signal = PumpEval(candles, Context(candles));
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("DATA_UNAVAILABLE");
    }

    [Fact]
    public void Impulse_short_history_does_not_trade()
    {
        var signal = PumpEval(Flat(100, 100m), Context(Flat(100, 100m)));
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("warmup");
    }

    [Theory]
    [InlineData(151, SignalType.Hold)]
    [InlineData(149, SignalType.Exit)]
    public void Impulse_rides_until_a_close_falls_25_percent_under_the_peak(int close, SignalType expected)
    {
        var candles = PumpSeries();
        var opened = candles[^1].CloseTime;
        candles.Add(PumpBar(candles.Count, open: 117m, close: 190m, high: 200m, low: 117m, volume: 10_000m));
        candles.Add(PumpBar(candles.Count, open: 160m, close: close, high: 160m, low: close - 1m, volume: 10_000m));
        var signal = PumpEval(candles, Ride(candles, opened));
        signal.Signal.Should().Be(expected, "reason {0}", signal.Reason);
        signal.Snapshot!["peak"].Should().Be(200m);
    }

    [Fact]
    public void Impulse_closes_after_four_days()
    {
        var candles = PumpSeries();
        var signal = PumpEval(candles, Ride(candles, candles[^1].CloseTime.AddHours(-97)));
        signal.Signal.Should().Be(SignalType.Exit);
        signal.Reason.Should().Contain("4-day");
    }

    [Fact]
    public void Impulse_ignores_a_future_bar()
    {
        var candles = PumpSeries();
        var days = Days(close: 90m, high: 130m);
        var p = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatch, false);
        var at = candles.Count - 1;
        var seen = RefactoredStrategyEvaluator.Evaluate(p, candles, at, Context(candles, days), new CausalIndicatorCache(candles));
        var future = candles.Concat(new[] { PumpBar(candles.Count, open: 117m, close: 40m, high: 300m, low: 10m, volume: 900_000m) }).ToList();
        var later = RefactoredStrategyEvaluator.Evaluate(p, future, at, Context(future, days), new CausalIndicatorCache(future));
        later.Signal.Should().Be(seen.Signal);
        later.SuggestedStop.Should().Be(seen.SuggestedStop);
    }

    private static StrategySignalDetail PumpEval(List<MarketCandle> candles, StrategyContext context) =>
        RefactoredStrategyEvaluator.Evaluate(
            StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatch, false),
            candles,
            candles.Count - 1,
            context,
            new CausalIndicatorCache(candles));

    private static StrategyContext Ride(IReadOnlyList<MarketCandle> candles, DateTimeOffset opened) =>
        new()
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = true,
            PositionSide = PositionSide.Long,
            AverageEntryPrice = 117m,
            PositionOpenedAt = opened
        };

    /// <summary>720 flat 15m bars, the last one lifts price 17% above the 24h low on 5x hourly volume.</summary>
    private static List<MarketCandle> PumpSeries(decimal signalVolume = 170_000m)
    {
        var candles = Enumerable.Range(0, 719)
            .Select(i => PumpBar(i, open: 100m, close: 100m, high: 100.4m, low: 99.6m, volume: 10_000m))
            .ToList();
        candles.Add(PumpBar(719, open: 101m, close: 117m, high: 118m, low: 100m, volume: signalVolume));
        return candles;
    }

    private static MarketCandle PumpBar(int index, decimal open, decimal close, decimal high, decimal low, decimal volume)
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(index * 15);
        return new MarketCandle
        {
            OpenTime = start,
            CloseTime = start.AddMinutes(15),
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
            IsClosed = true,
            ExchangeTimestamp = start.AddMinutes(15)
        };
    }

    /// <summary>40 completed daily candles ending at the UTC midnight before the pump bar.</summary>
    private static CausalIndicatorCache Days(decimal close, decimal high)
    {
        var last = new DateTimeOffset(2024, 1, 8, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 40)
            .Select(k => new MarketCandle
            {
                OpenTime = last.AddDays(k - 40),
                CloseTime = last.AddDays(k - 39),
                Open = close,
                High = high,
                Low = close - 5m,
                Close = close,
                Volume = 1_000_000m,
                IsClosed = true,
                ExchangeTimestamp = last.AddDays(k - 39)
            })
            .ToList();
        return new CausalIndicatorCache(candles);
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
        foreach (var key in StrategyTemplateKeys.Canonical)
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
    public void Live_entries_stay_blocked_until_the_operator_turns_the_flag_on()
    {
        LiveEntryGate.BlockNewEntry(TradingMode.Live, liveTradingEnabled: false).Should().Be(LiveEntryGate.BlockedMessage);
        LiveEntryGate.BlockNewEntry(TradingMode.Paper, liveTradingEnabled: false).Should().Contain("Only live mode");
        LiveEntryGate.BlockNewEntry(TradingMode.Live, liveTradingEnabled: true).Should().BeNull();
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "TradingPlatform.Api", "appsettings.json")).Should().Contain("\"LiveTradingEnabled\": false");
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "TradingPlatform.Workers", "appsettings.json")).Should().Contain("\"LiveTradingEnabled\": false");
    }

    [Fact]
    public void Canonical_strategies_are_unique_and_do_not_enable_live_trading()
    {
        StrategyTemplateKeys.Canonical.Should().OnlyHaveUniqueItems();
        StrategyTemplateKeys.All.Should().OnlyHaveUniqueItems();
        StrategyTemplateKeys.Refactored.Should().BeEmpty();
        foreach (var key in StrategyTemplateKeys.Canonical)
        {
            StrategyTemplateKeys.IsResearchWorkflow(key).Should().BeFalse();
            StrategyTemplates.DefaultsFor(key, false).Timeframe.Should().Be(StrategyTemplateKeys.TimeframesFor(key)[0]);
            var duplicate = StrategyTemplateKeys.Canonical.Count(row => string.Equals(row, key, StringComparison.OrdinalIgnoreCase));
            duplicate.Should().Be(1, key);
        }

        StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatch, false).MaxImpulseAgeBars.Should().Be(32);
        var roundTrip = StrategyTemplates.Read(StrategyTemplates.Build("Impulse Catch", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatch, false)));
        roundTrip.MaxImpulseAgeBars.Should().Be(32);
        var legacy = StrategyTemplates.Read("""{"template":"impulse_catch_v2","timeframe":"15m","params":{}}""");
        legacy.MaxImpulseAgeBars.Should().Be(32);

        var obsolete = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatchV2, false);
        var detail = RefactoredStrategyEvaluator.Evaluate(
            obsolete with { TemplateKey = StrategyTemplateKeys.ImpulseCatchV2 },
            Flat(40, 100m),
            39,
            Context(Flat(40, 100m)),
            new CausalIndicatorCache(Flat(40, 100m)));
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Reason.Should().Contain("Obsolete strategy id");
    }

    private static StrategySignalDetail EvalKey(string key, List<MarketCandle> candles)
    {
        var p = StrategyTemplates.DefaultsFor(key, false);
        return RefactoredStrategyEvaluator.Evaluate(p, candles, candles.Count - 1, Context(candles), new CausalIndicatorCache(candles));
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
