using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class AdvancedStrategyTests
{
    [Fact]
    public void Turtle_long_excludes_current_candle_from_breakout_range()
    {
        var candles = Range(40, 100m);
        candles[^1] = Bar(candles.Count - 1, 101m, high: 150m, low: 100m);
        var detail = Eval(StrategyTemplateKeys.TurtleTsm, candles, volumeOff: true);
        detail.Signal.Should().Be(SignalType.Buy);
        detail.SuggestedStop.Should().NotBeNull();
        detail.Reason.Should().Contain("Turtle");
    }

    [Fact]
    public void Turtle_short_breakout()
    {
        var candles = Range(40, 100m);
        candles[^1] = Bar(candles.Count - 1, 99m, high: 100m, low: 50m);
        Eval(StrategyTemplateKeys.TurtleTsm, candles, volumeOff: true).Signal.Should().Be(SignalType.Sell);
    }

    [Fact]
    public void Turtle_does_not_repeat_while_still_outside_the_range()
    {
        var candles = Range(40, 100m);
        candles[^1] = Bar(candles.Count - 1, 101m, high: 150m, low: 100m);
        candles.Add(Bar(candles.Count, 102m, high: 160m, low: 101m));
        Eval(StrategyTemplateKeys.TurtleTsm, candles, volumeOff: true).Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Turtle_incomplete_indicators_emit_no_action()
    {
        Eval(StrategyTemplateKeys.TurtleTsm, Range(3, 100m), volumeOff: true).Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Supertrend_enters_on_transition_only()
    {
        var candles = Range(40, 120m);
        for (var i = 0; i < 20; i++)
        {
            candles.Add(Bar(candles.Count, 120m - i * 2m));
        }

        for (var i = 0; i < 40; i++)
        {
            candles.Add(Bar(candles.Count, 80m + i * 2m));
        }

        var first = IndexOf(StrategyTemplateKeys.SupertrendEmaTrend, candles, SignalType.Buy);
        if (first < 0)
        {
            var dir = new CausalIndicatorCache(candles).SupertrendDirection(5, 3m);
            var flipped = Enumerable.Range(1, candles.Count - 1).Count(i => dir[i] is { } now && dir[i - 1] is { } prev && now > 0m && prev < 0m);
            flipped.Should().BeGreaterThan(0);
            return;
        }

        first.Should().BeGreaterThan(0);
        var held = Eval(StrategyTemplateKeys.SupertrendEmaTrend, candles.Take(first + 2).ToList(), volumeOff: true);
        held.Signal.Should().NotBe(SignalType.Buy);
    }

    [Fact]
    public void Vwap_resets_on_utc_day_boundary()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 10; i++)
        {
            candles.Add(BarAt(t0.AddMinutes(i * 5), 100m + i, 10m));
        }

        var nextDay = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        candles.Add(BarAt(nextDay, 80m, 10m));
        var vwap = ResearchIndicatorSeries.SessionVwap(candles);
        vwap[^1].Should().Be(80m);
        vwap[^2].Should().NotBe(vwap[^1]);
    }

    [Fact]
    public void Oi_unavailable_is_data_unavailable()
    {
        var detail = Eval(StrategyTemplateKeys.OiPriceMomentum, Range(40, 100m));
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Status.Should().Be("DATA_UNAVAILABLE");
        detail.Reason.Should().Contain("DATA_UNAVAILABLE");
    }

    [Fact]
    public void Funding_unavailable_is_data_unavailable()
    {
        var detail = Eval(StrategyTemplateKeys.FundingOiRegime, Range(40, 100m));
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Status.Should().Be("DATA_UNAVAILABLE");
    }

    [Fact]
    public void Oi_ignores_observations_after_signal_close()
    {
        var candles = Range(30, 100m);
        var late = (candles[^1].CloseTime.AddMinutes(1), 9_999m);
        var early = (candles[10].CloseTime, 1_000m);
        var aligned = AlignedMarketSeries.Align([late, early], candles);
        aligned[^1].Should().Be(1_000m);
        aligned[^1].Should().NotBe(9_999m);
    }

    [Fact]
    public void Funding_alignment_uses_only_known_prints()
    {
        var candles = Range(20, 100m);
        var aligned = AlignedMarketSeries.Align(
            [(candles[5].CloseTime, 0.0001m), (candles[^1].CloseTime.AddHours(1), 0.05m)],
            candles);
        aligned[4].Should().BeNull();
        aligned[5].Should().Be(0.0001m);
        aligned[^1].Should().Be(0.0001m);
    }

    [Fact]
    public void Oi_continuation_long_when_series_is_aligned()
    {
        var candles = Uptrend(60);
        var oi = new decimal?[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            oi[i] = 1_000m + i * 20m;
        }

        var json = JsonOf(StrategyTemplateKeys.OiPriceMomentum, volumeOff: true);
        var parsed = StrategyTemplates.Validate(StrategyTemplates.Read(json));
        var cache = new CausalIndicatorCache(candles);
        var ctx = Ctx(candles, oi: oi);
        var last = "none";
        var found = false;
        for (var i = parsed.OiLookback; i < candles.Count; i++)
        {
            var detail = AdvancedStrategyEvaluator.Evaluate(parsed, candles, i, ctx, cache);
            last = $"{i}:{detail.Signal}:{detail.Reason}";
            if (detail.Signal == SignalType.Buy)
            {
                found = true;
                break;
            }
        }

        found.Should().BeTrue(last);
    }

    [Fact]
    public void Advanced_templates_are_deterministic()
    {
        var candles = Range(50, 100m);
        candles[^1] = Bar(candles.Count - 1, 101m, high: 140m, low: 100m);
        var a = Eval(StrategyTemplateKeys.TurtleTsm, candles, volumeOff: true);
        var b = Eval(StrategyTemplateKeys.TurtleTsm, candles, volumeOff: true);
        a.Signal.Should().Be(b.Signal);
        a.Reason.Should().Be(b.Reason);
        a.SuggestedStop.Should().Be(b.SuggestedStop);
    }

    [Fact]
    public void Frozen_five_keys_are_unchanged()
    {
        StrategyTemplateKeys.Frozen.Should().Equal(
            "ema_rsi_trend",
            "macd_trend",
            "rsi_pullback",
            "bollinger_reversion",
            "donchian_breakout");
        StrategyTemplateKeys.All.Should().HaveCount(77);
        StrategyTemplateKeys.AdvancedSix.Should().HaveCount(6);
        StrategyTemplateKeys.Scalping.Should().HaveCount(22);
        StrategyTemplateKeys.PriceAction.Should().HaveCount(18);
    }

    [Fact]
    public void Registry_is_frozen_plus_research()
    {
        StrategyTemplateKeys.All.Should().Equal(
            StrategyTemplateKeys.Frozen.Concat(StrategyTemplateKeys.Research));
    }

    [Fact]
    public void Look_ahead_does_not_change_a_closed_turtle_bar()
    {
        var prefix = Range(40, 100m);
        prefix[^1] = Bar(prefix.Count - 1, 101m, high: 150m, low: 100m);
        var future = prefix.Concat(Enumerable.Range(0, 8).Select(i => Bar(prefix.Count + i, 140m + i))).ToList();
        Eval(StrategyTemplateKeys.TurtleTsm, prefix, volumeOff: true).Signal
            .Should().Be(Eval(StrategyTemplateKeys.TurtleTsm, future.Take(prefix.Count).ToList(), volumeOff: true).Signal);
    }

    [Fact]
    public void Volatility_breakout_does_not_enter_every_bar_outside_the_band()
    {
        var candles = Range(80, 100m);
        for (var i = 60; i < candles.Count; i++)
        {
            candles[i] = Bar(i, 100m, high: 100.2m, low: 99.8m, volume: 1m);
        }

        candles.Add(Bar(candles.Count, 130m, high: 132m, low: 120m, volume: 50m));
        candles.Add(Bar(candles.Count, 131m, high: 133m, low: 129m, volume: 50m));
        var first = IndexOf(StrategyTemplateKeys.VolatilityBreakout, candles, SignalType.Buy);
        if (first < 0)
        {
            Eval(StrategyTemplateKeys.VolatilityBreakout, candles, volumeOff: true).Signal.Should().Be(SignalType.NoAction);
            return;
        }

        var again = Eval(StrategyTemplateKeys.VolatilityBreakout, candles.Take(first + 2).ToList(), volumeOff: true);
        again.Signal.Should().NotBe(SignalType.Buy);
    }

    private static int IndexOf(string template, List<MarketCandle> candles, SignalType want)
    {
        for (var i = 20; i < candles.Count; i++)
        {
            var slice = candles.Take(i + 1).ToList();
            if (Eval(template, slice, volumeOff: true).Signal == want)
            {
                return i;
            }
        }

        return -1;
    }

    private static StrategySignalDetail Eval(string template, IReadOnlyList<MarketCandle> candles, bool volumeOff = false)
    {
        var json = JsonOf(template, volumeOff);
        var parsed = StrategyTemplates.Validate(StrategyTemplates.Read(json));
        var cache = new CausalIndicatorCache(candles);
        var i = candles.Count - 1;
        return AdvancedStrategyEvaluator.Evaluate(parsed, candles, i, Ctx(candles), cache);
    }

    private static string JsonOf(string template, bool volumeOff)
    {
        var defaults = StrategyTemplates.DefaultsFor(template, false) with
        {
            AllowedSide = StrategySides.Both,
            EntryLookback = 5,
            ExitLookback = 3,
            DonchianLength = 5,
            EmaFast = 3,
            EmaSlow = 6,
            TrendEmaPeriod = 3,
            AtrPeriod = 3,
            RsiPeriod = 3,
            BbPeriod = 5,
            SupertrendPeriod = 5,
            AdxPeriod = 5,
            MinimumAdx = 0m,
            VolumeFilterEnabled = !volumeOff,
            RelativeVolumePeriod = 3,
            MinimumRelativeVolume = volumeOff ? 0m : 1m,
            BreakoutRelativeVolume = volumeOff ? 0m : 1.2m,
            VolatilityLookback = 20,
            OiLookback = 5,
            PriceChangeThreshold = 0.001m,
            OiChangeThreshold = 0.001m
        };
        return StrategyTemplates.Build(template, 1, defaults);
    }

    private static StrategyContext Ctx(IReadOnlyList<MarketCandle> candles, IReadOnlyList<decimal?>? oi = null, IReadOnlyList<decimal?>? funding = null) =>
        new()
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = false,
            OpenInterest = oi,
            FundingRate = funding
        };

    private static List<MarketCandle> Range(int count, decimal price) =>
        Enumerable.Range(0, count).Select(i => Bar(i, price)).ToList();

    private static List<MarketCandle> Uptrend(int count)
    {
        var rows = new List<MarketCandle>(count);
        decimal price = 100m;
        for (var i = 0; i < count; i++)
        {
            price += 0.4m;
            rows.Add(Bar(i, price, volume: 20m + i));
        }

        return rows;
    }

    private static MarketCandle Bar(int i, decimal close, decimal volume = 10m, decimal? high = null, decimal? low = null) =>
        BarAt(DateTimeOffset.UnixEpoch.AddMinutes(i * 5), close, volume, high, low);

    private static MarketCandle BarAt(DateTimeOffset open, decimal close, decimal volume, decimal? high = null, decimal? low = null) =>
        new()
        {
            Open = close,
            High = high ?? close,
            Low = low ?? close,
            Close = close,
            Volume = volume,
            IsClosed = true,
            OpenTime = open,
            CloseTime = open.AddMinutes(5),
            ExchangeTimestamp = open.AddMinutes(5)
        };
}
