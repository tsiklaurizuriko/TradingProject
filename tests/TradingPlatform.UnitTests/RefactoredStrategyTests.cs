using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class RefactoredStrategyTests
{
    [Fact]
    public void Refactored_keys_stay_out_of_the_operator_catalog()
    {
        StrategyTemplateKeys.Refactored.Should().HaveCount(18);
        foreach (var key in StrategyTemplateKeys.Refactored)
        {
            StrategyTemplateKeys.IsKnown(key).Should().BeTrue();
            StrategyTemplateKeys.IsOperatorCatalog(key).Should().BeFalse();
            StrategyTemplateKeys.IsResearchWorkflow(key).Should().BeTrue();
            StrategyTemplates.ResearchStatus(key).Should().Be("NOT_VALIDATED");
            StrategyTemplates.DisplayName(key).Should().Contain("v2");
            StrategyTemplateKeys.TimeframesFor(key).Should().NotBeEmpty();
            StrategyTemplateKeys.Normalize(key).Should().Be(key);
        }

        StrategyTemplateKeys.DirectionsFor(StrategyTemplateKeys.ImpulseCatchV2).Should().Equal("LONG");
        StrategyTemplateKeys.DirectionsFor(StrategyTemplateKeys.TsMomentumV2).Should().Equal("LONG");
        StrategyTemplateKeys.TimeframesFor(StrategyTemplateKeys.EmaCrossV2).Should().Equal("30m");
        StrategyTemplateKeys.TimeframesFor(StrategyTemplateKeys.DonchianBreakoutV2Daily).Should().Equal("1d");
    }

    [Fact]
    public void Unclosed_candle_does_not_enter()
    {
        var candles = Flat(80, 100m);
        candles[^1].IsClosed = false;
        var signal = Eval(StrategyTemplateKeys.DonchianBreakoutV2FourHour, candles);
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("Unclosed");
    }

    [Fact]
    public void Same_candle_is_deterministic()
    {
        var candles = Breakout(130);
        var a = Eval(StrategyTemplateKeys.DonchianBreakoutV2FourHour, candles);
        var b = Eval(StrategyTemplateKeys.DonchianBreakoutV2FourHour, candles);
        a.Signal.Should().Be(SignalType.Buy);
        b.Signal.Should().Be(a.Signal);
        b.SuggestedStop.Should().Be(a.SuggestedStop);
        b.Reason.Should().Be(a.Reason);
    }

    [Fact]
    public void Donchian_uses_the_prior_channel_and_ignores_a_future_spike()
    {
        var candles = Breakout(130);
        var withFuture = candles.Concat(new[] { Bar(candles.Count, 100m, high: 900m, low: 90m) }).ToList();
        var at = candles.Count - 1;
        var prefix = Eval(StrategyTemplateKeys.DonchianBreakoutV2FourHour, candles);
        var cache = new CausalIndicatorCache(withFuture);
        var p = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.DonchianBreakoutV2FourHour, false);
        var later = RefactoredStrategyEvaluator.Evaluate(p, withFuture, at, FlatContext(withFuture), cache);
        later.Signal.Should().Be(prefix.Signal);
        later.SuggestedStop.Should().Be(prefix.SuggestedStop);
        prefix.SuggestedStop.Should().NotBeNull();
        prefix.SuggestedStop!.Value.Should().BeLessThan(candles[at].Close);
    }

    [Fact]
    public void ZigZag_does_not_treat_an_unconfirmed_pivot_as_known()
    {
        var candles = Flat(40, 100m);
        candles[^1] = Bar(candles.Count - 1, 100m, high: 101m, low: 70m);
        var cache = new CausalIndicatorCache(candles);
        var swing = cache.ConfirmedSwingLow(5);
        swing[^1].Should().NotBe(70m);
        var signal = Eval(StrategyTemplateKeys.ZigZagFadeV2, candles);
        signal.Signal.Should().NotBe(SignalType.Buy);
    }

    [Fact]
    public void ZigZag_long_reclaims_a_confirmed_swing_and_does_not_flip_same_bar()
    {
        var candles = Flat(21, 100m);
        candles[10] = Bar(10, 99m, high: 100m, low: 90m);
        candles[19] = Bar(19, 95m, high: 96m, low: 70m);
        candles[20] = Bar(20, 96m, high: 98m, low: 94m);
        var entry = Eval(StrategyTemplateKeys.ZigZagFadeV2, candles);
        entry.Signal.Should().Be(SignalType.Buy, "reason {0}", entry.Reason);
        entry.SuggestedStop.Should().NotBeNull();
        entry.SuggestedTakeProfit.Should().NotBeNull();
        entry.SuggestedStop!.Value.Should().BeLessThan(candles[^1].Close);

        var p = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ZigZagFadeV2, false);
        var cache = new CausalIndicatorCache(candles);
        var exit = RefactoredStrategyEvaluator.Evaluate(
            p,
            candles,
            candles.Count - 1,
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[^1].Close,
                HasOpenPosition = true,
                PositionSide = PositionSide.Long,
                AverageEntryPrice = 96m
            },
            cache);
        exit.Signal.Should().NotBe(SignalType.Sell);
    }

    [Fact]
    public void Squeeze_does_not_treat_missing_funding_as_zero()
    {
        var candles = Flat(40, 100m);
        var signal = Eval(StrategyTemplateKeys.SqueezeWatchV2, candles);
        signal.Signal.Should().Be(SignalType.NoAction);
        signal.Reason.Should().Contain("DATA_UNAVAILABLE");
        signal.Reason.Should().Contain("not zero");
    }

    [Fact]
    public void Flow_and_BinHV_do_not_invent_a_higher_timeframe_regime()
    {
        var candles = Flat(80, 100m);
        Eval(StrategyTemplateKeys.FlowZoneV2, candles).Reason.Should().Contain("DATA_UNAVAILABLE");
        Eval(StrategyTemplateKeys.BinHv45V2, candles).Reason.Should().Contain("DATA_UNAVAILABLE");
        Eval(StrategyTemplateKeys.EmaRsiTrendV2, candles).Reason.Should().Contain("DATA_UNAVAILABLE");
    }

    [Fact]
    public void Time_series_momentum_rejects_a_non_btc_symbol_and_does_not_sell()
    {
        var candles = Rising(140);
        var p = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.TsMomentumV2, false);
        var cache = new CausalIndicatorCache(candles);
        var other = RefactoredStrategyEvaluator.Evaluate(
            p,
            candles,
            candles.Count - 1,
            new StrategyContext { ClosedCandles = candles, CurrentPrice = candles[^1].Close, Symbol = "ETHUSDT" },
            cache);
        other.Signal.Should().Be(SignalType.NoAction);
        other.Reason.Should().Contain("BTC");

        var btc = RefactoredStrategyEvaluator.Evaluate(
            p,
            candles,
            candles.Count - 1,
            new StrategyContext { ClosedCandles = candles, CurrentPrice = candles[^1].Close, Symbol = "BTCUSDT" },
            cache);
        btc.Signal.Should().NotBe(SignalType.Sell);
    }

    [Fact]
    public void Short_history_stays_flat()
    {
        var candles = Flat(10, 100m);
        foreach (var key in StrategyTemplateKeys.Refactored)
        {
            Eval(key, candles).Signal.Should().Be(SignalType.NoAction);
        }
    }

    [Fact]
    public void Tick_rounding_does_not_widen_the_stop()
    {
        var longStop = StrategyExecutionRules.RoundStop(PositionSide.Long, 100.03m, 0.1m);
        var shortStop = StrategyExecutionRules.RoundStop(PositionSide.Short, 100.07m, 0.1m);
        longStop.Should().Be(100.1m);
        shortStop.Should().Be(100.0m);
        longStop.Should().BeGreaterThan(100.03m);
        shortStop.Should().BeLessThan(100.07m);
        StrategyExecutionRules.RoundStop(PositionSide.Long, 100m, 0m).Should().Be(100m);
    }

    [Fact]
    public void Round_trip_cost_gate_rejects_a_tiny_target()
    {
        StrategyExecutionRules.PaysRoundTrip(100m, 100.1m).Should().BeFalse();
        StrategyExecutionRules.PaysRoundTrip(100m, 102m).Should().BeTrue();
    }

    [Fact]
    public void Repeated_candle_keeps_the_same_order_key()
    {
        var bot = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var open = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var again = CandleIdempotency.Key(bot, open, live: false);
        CandleIdempotency.Key(bot, open, live: false).Should().Be(again);
        CandleIdempotency.Key(bot, open.AddMinutes(15), live: false).Should().NotBe(again);
        CandleIdempotency.Key(bot, open, live: true).Should().NotBe(again);
    }

    private static StrategySignalDetail Eval(string key, List<MarketCandle> candles)
    {
        var p = StrategyTemplates.DefaultsFor(key, false);
        var cache = new CausalIndicatorCache(candles);
        return RefactoredStrategyEvaluator.Evaluate(p, candles, candles.Count - 1, FlatContext(candles), cache);
    }

    private static StrategyContext FlatContext(IReadOnlyList<MarketCandle> candles) =>
        new()
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close
        };

    private static List<MarketCandle> Flat(int count, decimal price) =>
        Enumerable.Range(0, count).Select(i => Bar(i, price)).ToList();

    private static List<MarketCandle> Rising(int count)
    {
        var candles = new List<MarketCandle>();
        decimal price = 100m;
        for (var i = 0; i < count; i++)
        {
            price += 1m;
            candles.Add(Bar(i, price, high: price + 0.4m, low: price - 0.4m));
        }

        return candles;
    }

    private static List<MarketCandle> Breakout(int count)
    {
        var candles = Flat(count - 1, 100m);
        candles.Add(Bar(count - 1, 110m, high: 111m, low: 109m));
        return candles;
    }

    private static MarketCandle Bar(int index, decimal close, decimal? high = null, decimal? low = null) =>
        new()
        {
            OpenTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(index * 15),
            CloseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes((index + 1) * 15),
            Open = close - 0.2m,
            High = high ?? close + 0.4m,
            Low = low ?? close - 0.4m,
            Close = close,
            Volume = 10m,
            IsClosed = true,
            ExchangeTimestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes((index + 1) * 15)
        };
}
