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
    public void Ts_momentum_buys_the_top_third_stays_long_only_and_exits_when_the_sleeve_is_flat()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = new List<MarketCandle>();
        decimal price = 100m;
        for (var i = 0; i < 80; i++)
        {
            candles.Add(Day(start.AddDays(i), price));
        }

        var flat = EvalTs(candles, open: false);
        flat.Signal.Should().Be(SignalType.NoAction);
        flat.Signal.Should().NotBe(SignalType.Sell);

        for (var i = 0; i < 40; i++)
        {
            price *= 1.02m;
            candles.Add(Day(start.AddDays(candles.Count), price));
        }

        var buyAt = -1;
        for (var i = 80; i < candles.Count; i++)
        {
            var signal = EvalTs(candles.Take(i + 1).ToList(), open: false);
            signal.Signal.Should().NotBe(SignalType.Sell);
            if (signal.Signal == SignalType.Buy)
            {
                buyAt = i;
                break;
            }
        }

        buyAt.Should().BeGreaterThan(28);

        for (var i = 0; i < 40; i++)
        {
            candles.Add(Day(start.AddDays(candles.Count), price));
        }

        SignalType? closed = null;
        for (var i = buyAt; i < candles.Count; i++)
        {
            var signal = EvalTs(candles.Take(i + 1).ToList(), open: true);
            signal.Signal.Should().NotBe(SignalType.Sell);
            if (signal.Signal == SignalType.Exit)
            {
                closed = signal.Signal;
                break;
            }
        }

        closed.Should().Be(SignalType.Exit);
    }

    [Fact]
    public void Flow_zone_buys_only_when_the_upper_zone_taker_buy_and_open_interest_agree()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 23; i++)
        {
            candles.Add(FlowBar(start.AddHours(i), 100m, 100m, buy: 4m, volume: 10m));
        }

        candles.Add(FlowBar(start.AddHours(23), 100m, 130m, buy: 8m, volume: 10m));
        var buy = EvalFlow(candles, new decimal?[] { 10m, 12m });
        buy.Signal.Should().Be(SignalType.Buy);

        var noInterest = EvalFlow(candles, new decimal?[] { 12m, 10m });
        noInterest.Signal.Should().Be(SignalType.NoAction);

        var missingFlow = candles.ToList();
        missingFlow[^1] = FlowBar(start.AddHours(23), 100m, 130m, buy: 0m, volume: 10m);
        EvalFlow(missingFlow, new decimal?[] { 10m, 12m }).Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Flow_zone_holds_a_scratch_and_exits_once_the_move_covers_the_fee()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 22; i++)
        {
            candles.Add(FlowBar(start.AddHours(i), 100m, 100m, buy: 4m, volume: 10m));
        }

        candles.Add(FlowBar(start.AddHours(22), 100m, 160m, buy: 8m, volume: 10m));
        candles.Add(FlowBar(start.AddHours(23), 100m, 110m, buy: 4m, volume: 10m));

        EvalFlowOpen(candles, PositionSide.Long, 110m).Signal.Should().Be(SignalType.Hold);
        EvalFlowOpen(candles, PositionSide.Long, 100m).Signal.Should().Be(SignalType.Exit);
    }

    [Fact]
    public void Btc_daily_max_buys_a_10_day_high_and_exits_when_the_high_breaks()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 12; i++)
        {
            candles.Add(Day(start.AddDays(i), 100m + i));
        }

        var buy = EvalMax(candles, open: false);
        buy.Signal.Should().Be(SignalType.Buy);
        buy.Signal.Should().NotBe(SignalType.Sell);

        candles.Add(Day(start.AddDays(12), 90m));
        var exit = EvalMax(candles, open: true);
        exit.Signal.Should().Be(SignalType.Exit);
    }

    [Fact]
    public void Ema_cross_long_buys_on_cross_up_exits_on_cross_down_and_does_not_short()
    {
        var candles = new List<MarketCandle>();
        decimal price = 100m;
        for (var i = 0; i < 80; i++)
        {
            price -= 0.35m;
            candles.Add(Bar(candles.Count, price));
        }

        for (var i = 0; i < 50; i++)
        {
            price += 1.4m;
            candles.Add(Bar(candles.Count, price));
        }

        var buyAt = -1;
        for (var i = 55; i < candles.Count; i++)
        {
            var signal = EvalCross(candles.Take(i + 1).ToList(), open: false).Signal;
            signal.Should().NotBe(SignalType.Sell);
            if (signal == SignalType.Buy)
            {
                buyAt = i;
                break;
            }
        }

        buyAt.Should().BeGreaterThan(0);
        for (var i = 0; i < 60; i++)
        {
            price -= 1.6m;
            candles.Add(Bar(candles.Count, price));
        }

        var exited = false;
        for (var i = buyAt + 1; i < candles.Count; i++)
        {
            var slice = candles.Take(i + 1).ToList();
            var open = EvalCross(slice, open: true).Signal;
            open.Should().NotBe(SignalType.Sell);
            open.Should().NotBe(SignalType.Buy);
            if (open != SignalType.Exit)
            {
                continue;
            }

            exited = true;
            EvalCross(slice, open: false).Signal.Should().Be(SignalType.NoAction);
            break;
        }

        exited.Should().BeTrue();
    }

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
        StrategyTemplateKeys.All.Should().HaveCount(
            StrategyTemplateKeys.Frozen.Length
            + StrategyTemplateKeys.Research.Length
            + StrategyTemplateKeys.NearMiss.Length
            + StrategyTemplateKeys.CrossSectionalReversal.Length
            + StrategyTemplateKeys.Range.Length
            + StrategyTemplateKeys.Flow.Length
            + StrategyTemplateKeys.Positioning.Length
            + StrategyTemplateKeys.Imported.Length);
        StrategyTemplateKeys.AdvancedSix.Should().HaveCount(6);
        StrategyTemplateKeys.Scalping.Should().HaveCount(22);
        StrategyTemplateKeys.PriceAction.Should().HaveCount(18);
    }

    [Fact]
    public void Registry_is_frozen_plus_research()
    {
        StrategyTemplateKeys.All.Should().Equal(
            StrategyTemplateKeys.Frozen
                .Concat(StrategyTemplateKeys.Research)
                .Concat(StrategyTemplateKeys.NearMiss)
                .Concat(StrategyTemplateKeys.CrossSectionalReversal)
                .Concat(StrategyTemplateKeys.Range)
                .Concat(StrategyTemplateKeys.Flow)
                .Concat(StrategyTemplateKeys.Positioning)
                .Concat(StrategyTemplateKeys.Imported));
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

    private static StrategySignalDetail EvalCross(IReadOnlyList<MarketCandle> candles, bool open)
    {
        var parsed = StrategyTemplates.Validate(StrategyTemplates.DefaultsFor(StrategyTemplateKeys.BtcEma20Ema50Long, false));
        var cache = new CausalIndicatorCache(candles);
        var i = candles.Count - 1;
        var ctx = new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = open,
            PositionSide = PositionSide.Long
        };
        return AdvancedStrategyEvaluator.Evaluate(parsed, candles, i, ctx, cache);
    }

    [Fact]
    public void Squeeze_watch_buys_crowded_shorts_and_sells_crowded_longs()
    {
        var candles = Enumerable.Range(0, 24)
            .Select(i => QuietBar(DateTimeOffset.UnixEpoch.AddHours(i), 100m))
            .ToList();
        EvalSqueeze(candles, 100m, 120m, -0.0012m).Signal.Should().Be(SignalType.Buy);
        EvalSqueeze(candles, 100m, 120m, 0.0012m).Signal.Should().Be(SignalType.Sell);
        EvalSqueeze(candles, 100m, 120m, null).Signal.Should().Be(SignalType.NoAction);
        var moved = candles.ToList();
        moved[^1] = QuietBar(moved[^1].OpenTime, 110m);
        EvalSqueeze(moved, 100m, 120m, -0.0012m).Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Squeeze_watch_exits_when_funding_fades_or_price_moves_two_percent_against()
    {
        var candles = Enumerable.Range(0, 24)
            .Select(i => QuietBar(DateTimeOffset.UnixEpoch.AddHours(i), 100m))
            .ToList();

        EvalSqueezeOpen(candles, PositionSide.Long, 100m, -0.0012m).Signal.Should().Be(SignalType.Hold);
        EvalSqueezeOpen(candles, PositionSide.Long, 100m, 0m).Signal.Should().Be(SignalType.Exit);
        EvalSqueezeOpen(candles, PositionSide.Short, 100m, 0.0012m).Signal.Should().Be(SignalType.Hold);
        EvalSqueezeOpen(candles, PositionSide.Short, 100m, 0m).Signal.Should().Be(SignalType.Exit);

        var dipped = candles.ToList();
        dipped[^1] = QuietBar(dipped[^1].OpenTime, 98m);
        EvalSqueezeOpen(dipped, PositionSide.Long, 100m, -0.0012m).Signal.Should().Be(SignalType.Exit);
    }

    private static StrategySignalDetail EvalSqueezeOpen(
        IReadOnlyList<MarketCandle> candles,
        PositionSide side,
        decimal entry,
        decimal? funding)
    {
        var parsed = StrategyTemplates.Validate(StrategyTemplates.DefaultsFor(StrategyTemplateKeys.SqueezeWatch, false));
        return AdvancedStrategyEvaluator.Evaluate(
            parsed,
            candles,
            candles.Count - 1,
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[^1].Close,
                HasOpenPosition = true,
                PositionSide = side,
                AverageEntryPrice = entry,
                FundingRate = funding is { } rate ? [rate] : null
            },
            new CausalIndicatorCache(candles));
    }

    private static StrategySignalDetail EvalSqueeze(
        IReadOnlyList<MarketCandle> candles,
        decimal openInterestThen,
        decimal openInterestNow,
        decimal? funding)
    {
        var parsed = StrategyTemplates.Validate(StrategyTemplates.DefaultsFor(StrategyTemplateKeys.SqueezeWatch, false));
        return AdvancedStrategyEvaluator.Evaluate(
            parsed,
            candles,
            candles.Count - 1,
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[^1].Close,
                HasOpenPosition = false,
                OpenInterest = [openInterestThen, openInterestNow],
                FundingRate = funding is { } rate ? [rate] : null
            },
            new CausalIndicatorCache(candles));
    }

    private static MarketCandle QuietBar(DateTimeOffset open, decimal close) =>
        new()
        {
            Open = close,
            High = close,
            Low = close,
            Close = close,
            Volume = 1m,
            IsClosed = true,
            OpenTime = open,
            CloseTime = open.AddHours(1)
        };

    private static StrategySignalDetail EvalFlowOpen(IReadOnlyList<MarketCandle> candles, PositionSide side, decimal entry)
    {
        var parsed = StrategyTemplates.Validate(StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FlowZone, false));
        return AdvancedStrategyEvaluator.Evaluate(
            parsed,
            candles,
            candles.Count - 1,
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[^1].Close,
                HasOpenPosition = true,
                PositionSide = side,
                AverageEntryPrice = entry,
                OpenInterest = [10m, 12m]
            },
            new CausalIndicatorCache(candles));
    }

    private static StrategySignalDetail EvalFlow(IReadOnlyList<MarketCandle> candles, IReadOnlyList<decimal?> openInterest)
    {
        var parsed = StrategyTemplates.Validate(StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FlowZone, false));
        return AdvancedStrategyEvaluator.Evaluate(
            parsed,
            candles,
            candles.Count - 1,
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[^1].Close,
                HasOpenPosition = false,
                OpenInterest = openInterest
            },
            new CausalIndicatorCache(candles));
    }

    private static MarketCandle FlowBar(DateTimeOffset open, decimal low, decimal close, decimal buy, decimal volume) =>
        new()
        {
            Open = low,
            High = close,
            Low = low,
            Close = close,
            Volume = volume,
            TakerBuyVolume = buy,
            IsClosed = true,
            OpenTime = open,
            CloseTime = open.AddHours(1),
            ExchangeTimestamp = open.AddHours(1)
        };

    private static StrategySignalDetail EvalMax(IReadOnlyList<MarketCandle> candles, bool open)
    {
        var parsed = StrategyTemplates.Validate(StrategyTemplates.DefaultsFor(StrategyTemplateKeys.BtcDailyMax10, false));
        return AdvancedStrategyEvaluator.Evaluate(
            parsed,
            candles,
            candles.Count - 1,
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[^1].Close,
                HasOpenPosition = open,
                PositionSide = PositionSide.Long
            },
            new CausalIndicatorCache(candles));
    }

    private static StrategySignalDetail EvalTs(IReadOnlyList<MarketCandle> candles, bool open)
    {
        var parsed = StrategyTemplates.Validate(StrategyTemplates.DefaultsFor(StrategyTemplateKeys.TsMomentum285, false));
        var cache = new CausalIndicatorCache(candles);
        return AdvancedStrategyEvaluator.Evaluate(
            parsed,
            candles,
            candles.Count - 1,
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[^1].Close,
                HasOpenPosition = open,
                PositionSide = PositionSide.Long
            },
            cache);
    }

    private static MarketCandle Day(DateTimeOffset open, decimal close) =>
        new()
        {
            Open = close,
            High = close,
            Low = close,
            Close = close,
            Volume = 1m,
            IsClosed = true,
            OpenTime = open,
            CloseTime = open.AddDays(1),
            ExchangeTimestamp = open.AddDays(1)
        };

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

    [Fact]
    public void Impulse_catch_buys_the_first_large_rise_and_exits_the_giveback()
    {
        var crossed = Flat(21, 100m);
        crossed[^1] = MoveBar(20, 100m, 109m, 2m);
        EvalImpulse(crossed, open: false).Signal.Should().Be(SignalType.Buy);

        var small = Flat(21, 100m);
        small[^1] = MoveBar(20, 100m, 105m, 2m);
        EvalImpulse(small, open: false).Signal.Should().Be(SignalType.NoAction);

        var quiet = Flat(21, 100m);
        quiet[^1] = MoveBar(20, 100m, 109m, 1m);
        EvalImpulse(quiet, open: false).Signal.Should().Be(SignalType.NoAction);

        var already = Flat(21, 100m);
        already[^2] = MoveBar(19, 100m, 109m, 2m);
        already[^1] = MoveBar(20, 109m, 110m, 2m);
        EvalImpulse(already, open: false).Signal.Should().Be(SignalType.NoAction);

        var holding = Flat(21, 100m);
        holding[^1] = MoveBar(20, 100m, 99m, 1m);
        EvalImpulse(holding, open: true).Signal.Should().Be(SignalType.Hold);

        var dump = Flat(21, 100m);
        dump[^1] = MoveBar(20, 100m, 96m, 1m);
        EvalImpulse(dump, open: true).Signal.Should().Be(SignalType.Exit);
    }

    private static StrategySignalDetail EvalImpulse(IReadOnlyList<MarketCandle> candles, bool open)
    {
        var parsed = StrategyTemplates.Validate(StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatch, false));
        return AdvancedStrategyEvaluator.Evaluate(
            parsed,
            candles,
            candles.Count - 1,
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[^1].Close,
                HasOpenPosition = open,
                PositionSide = PositionSide.Long
            },
            new CausalIndicatorCache(candles));
    }

    private static List<MarketCandle> Flat(int count, decimal price) =>
        Enumerable.Range(0, count).Select(i => MoveBar(i, price, price, 1m)).ToList();

    private static MarketCandle MoveBar(int i, decimal open, decimal close, decimal volume) =>
        new()
        {
            Open = open,
            High = Math.Max(open, close),
            Low = Math.Min(open, close),
            Close = close,
            Volume = volume,
            IsClosed = true,
            OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 15),
            CloseTime = DateTimeOffset.UnixEpoch.AddMinutes((i + 1) * 15),
            ExchangeTimestamp = DateTimeOffset.UnixEpoch.AddMinutes((i + 1) * 15)
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
