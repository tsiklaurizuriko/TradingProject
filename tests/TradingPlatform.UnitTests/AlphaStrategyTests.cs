using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class AlphaStrategyTests
{
    [Fact]
    public void Registry_keeps_frozen_five_and_adds_phase3_keys()
    {
        StrategyTemplateKeys.Frozen.Should().HaveCount(5);
        StrategyTemplateKeys.AdvancedSix.Should().HaveCount(6);
        StrategyTemplateKeys.Alpha.Should().HaveCount(24);
        StrategyTemplateKeys.Research.Should().HaveCount(72);
        StrategyTemplateKeys.All.Should().HaveCount(
            StrategyTemplateKeys.Frozen.Length
            + StrategyTemplateKeys.Research.Length
            + StrategyTemplateKeys.NearMiss.Length
            + StrategyTemplateKeys.CrossSectionalReversal.Length);
        StrategyTemplateKeys.Scalping.Should().HaveCount(22);
        StrategyTemplateKeys.OperatorCatalog.Should().HaveCount(10);
        StrategyTemplateKeys.IsOperatorCatalog(StrategyTemplateKeys.RsiPullback).Should().BeTrue();
        StrategyTemplateKeys.IsOperatorCatalog(StrategyTemplateKeys.MacdTrend).Should().BeFalse();
        StrategyTemplateKeys.IsOperatorCatalog(StrategyTemplateKeys.ScalpEmaMomentum).Should().BeFalse();
        StrategyTemplateKeys.Family(StrategyTemplateKeys.ScalpEmaMomentum).Should().Be("SCALPING");
        StrategyTemplateKeys.All.Should().Equal(
            StrategyTemplateKeys.Frozen
                .Concat(StrategyTemplateKeys.Research)
                .Concat(StrategyTemplateKeys.NearMiss)
                .Concat(StrategyTemplateKeys.CrossSectionalReversal));
        StrategyTemplateKeys.IsOperatorCatalog(StrategyTemplateKeys.CrossSectionalReversalReturn15m).Should().BeFalse();
    }

    [Fact]
    public void Volume_profile_poc_sits_in_the_high_volume_bin()
    {
        var candles = Range(40, 100m);
        for (var i = 20; i < 40; i++)
        {
            candles[i] = Bar(i, 100m, volume: 1m, high: 101m, low: 99m);
        }

        candles[35] = Bar(35, 110m, volume: 500m, high: 111m, low: 109m);
        var (poc, vah, val) = AlphaIndicatorSeries.VolumeProfile(candles, 20, 0.70m);
        poc[39].Should().NotBeNull();
        poc[39]!.Value.Should().BeGreaterThan(105m);
        vah[39].Should().NotBeNull();
        val[39].Should().NotBeNull();
        val[39]!.Value.Should().BeLessThanOrEqualTo(poc[39]!.Value);
        vah[39]!.Value.Should().BeGreaterThanOrEqualTo(poc[39]!.Value);
    }

    [Fact]
    public void Causal_swing_low_is_confirmed_only_after_right_bars()
    {
        var candles = Range(20, 100m);
        candles[10] = Bar(10, 90m, high: 91m, low: 80m);
        var lows = AlphaIndicatorSeries.ConfirmedSwingLow(candles, 3);
        lows[10].Should().NotBe(80m);
        lows[13].Should().Be(80m);
    }

    [Fact]
    public void Future_candle_does_not_change_confirmed_swing_at_prior_index()
    {
        var candles = Range(20, 100m);
        candles[10] = Bar(10, 90m, high: 91m, low: 80m);
        var before = AlphaIndicatorSeries.ConfirmedSwingLow(candles, 3)[13];
        candles.Add(Bar(20, 50m, high: 51m, low: 40m));
        var after = AlphaIndicatorSeries.ConfirmedSwingLow(candles, 3)[13];
        after.Should().Be(before);
    }

    [Fact]
    public void Session_vwap_is_causal()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 8).Select(i => BarAt(t0.AddMinutes(i * 5), 100m + i, 10m)).ToList();
        var vwap = ResearchIndicatorSeries.SessionVwap(candles);
        vwap[3].Should().NotBeNull();
        var later = ResearchIndicatorSeries.SessionVwap(candles.Concat([BarAt(t0.AddMinutes(40), 200m, 1000m)]).ToList());
        later[3].Should().Be(vwap[3]);
    }

    [Fact]
    public void Close_z_score_is_extreme_on_spike()
    {
        var candles = Range(40, 100m);
        candles[^1] = Bar(39, 130m);
        var z = AlphaIndicatorSeries.CloseZScore(candles, 20);
        z[^1].Should().NotBeNull();
        z[^1]!.Value.Should().BeGreaterThan(2m);
    }

    [Fact]
    public void Taker_imbalance_unavailable_without_taker_volume()
    {
        var detail = Eval(StrategyTemplateKeys.TakerFlowMomentum, Range(40, 100m));
        detail.Status.Should().Be("DATA_UNAVAILABLE");
        detail.Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Taker_flow_long_requires_persistent_positive_imbalance()
    {
        var candles = Uptrend(50);
        for (var i = 0; i < candles.Count; i++)
        {
            candles[i].TakerBuyVolume = candles[i].Volume * 0.8m;
        }

        var detail = Eval(StrategyTemplateKeys.TakerFlowMomentum, candles, volumeOff: true);
        detail.Status.Should().NotBe("DATA_UNAVAILABLE");
        detail.Signal.Should().BeOneOf(SignalType.Buy, SignalType.NoAction, SignalType.Hold);
    }

    [Fact]
    public void Funding_basis_is_data_unavailable()
    {
        Eval(StrategyTemplateKeys.FundingBasisRv, Range(40, 100m)).Status.Should().Be("DATA_UNAVAILABLE");
        Eval(StrategyTemplateKeys.FundingOiReversal, Range(40, 100m)).Status.Should().Be("DATA_UNAVAILABLE");
        Eval(StrategyTemplateKeys.OiPriceVolumeRegime, Range(40, 100m)).Status.Should().Be("DATA_UNAVAILABLE");
        Eval(StrategyTemplateKeys.CryptoPairsArb, Range(40, 100m)).Status.Should().Be("DATA_UNAVAILABLE");
        Eval(StrategyTemplateKeys.XsRelativeStrength, Range(40, 100m)).Status.Should().Be("DATA_UNAVAILABLE");
    }

    [Fact]
    public void Router_is_deferred_researching_no_signal()
    {
        var detail = Eval(StrategyTemplateKeys.RegimeStrategyRouter, Range(40, 100m));
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Status.Should().Be("RESEARCHING");
        detail.Reason.Should().Contain("deferred");
    }

    [Fact]
    public void Z_score_long_on_deep_discount_in_range()
    {
        var candles = Range(50, 100m);
        candles[^1] = Bar(49, 85m, high: 86m, low: 84m);
        var detail = Eval(StrategyTemplateKeys.ZscoreMeanReversion, candles, volumeOff: true);
        detail.Signal.Should().Be(SignalType.Buy);
    }

    [Fact]
    public void Z_score_short_on_spike()
    {
        var candles = Range(50, 100m);
        candles[^1] = Bar(49, 115m, high: 116m, low: 114m);
        Eval(StrategyTemplateKeys.ZscoreMeanReversion, candles, volumeOff: true).Signal.Should().Be(SignalType.Sell);
    }

    [Fact]
    public void Insufficient_bars_emit_no_action()
    {
        Eval(StrategyTemplateKeys.VpVwapReversion, Range(3, 100m)).Signal.Should().Be(SignalType.NoAction);
        Eval(StrategyTemplateKeys.LiqSweepReversal, Range(3, 100m)).Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Sweep_reversal_long_after_causal_low_is_swept_and_reclaimed()
    {
        var candles = Range(30, 100m);
        candles[12] = Bar(12, 95m, high: 96m, low: 90m);
        for (var i = 13; i < 28; i++)
        {
            candles[i] = Bar(i, 100m, high: 101m, low: 99m);
        }

        candles[29] = Bar(29, 96m, high: 97m, low: 85m, volume: 40m);
        var detail = Eval(StrategyTemplateKeys.LiqSweepReversal, candles, volumeOff: true);
        detail.Signal.Should().BeOneOf(SignalType.Buy, SignalType.NoAction);
        if (detail.Signal == SignalType.Buy)
        {
            detail.Reason.Should().Contain("sweep");
        }
    }

    [Fact]
    public void Atr_momentum_does_not_use_future_close()
    {
        var candles = Uptrend(40);
        var at = 30;
        var before = EvalAt(StrategyTemplateKeys.AtrNormalizedMomentum, candles.Take(at + 1).ToList(), at, volumeOff: true);
        candles.Add(Bar(40, 200m));
        var after = EvalAt(StrategyTemplateKeys.AtrNormalizedMomentum, candles.Take(at + 1).ToList(), at, volumeOff: true);
        after.Signal.Should().Be(before.Signal);
        after.Reason.Should().Be(before.Reason);
    }

    [Fact]
    public void Htf_index_ignores_unclosed_future_bar()
    {
        var htf = Range(10, 100m);
        var signalClose = htf[7].CloseTime;
        htf[9].CloseTime = signalClose.AddHours(2);
        htf[9].IsClosed = true;
        var idx = AlphaIndicatorSeries.LastCompletedHigherTimeframe(htf, signalClose);
        idx.Should().BeLessThanOrEqualTo(7);
    }

    [Fact]
    public void Mtf_without_htf_cache_does_not_signal()
    {
        var detail = Eval(StrategyTemplateKeys.MtfTrendStructure, Uptrend(40), volumeOff: true);
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Reason.Should().Contain("HTF");
    }

    [Fact]
    public void Open_position_does_not_repeat_entry()
    {
        var candles = Range(50, 100m);
        candles[^1] = Bar(49, 85m);
        var cache = new CausalIndicatorCache(candles);
        var p = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ZscoreMeanReversion, false) with
        {
            TemplateKey = StrategyTemplateKeys.ZscoreMeanReversion,
            AllowedSide = StrategySides.Both,
            MinimumAdx = 80m,
            ZScoreEntry = 2m
        };
        var ctx = new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = true,
            PositionSide = PositionSide.Long
        };
        var detail = AdvancedStrategyEvaluator.Evaluate(p, candles, candles.Count - 1, ctx, cache);
        detail.Signal.Should().Be(SignalType.Hold);
        detail.Reason.Should().Contain("Position open");
    }

    [Fact]
    public void Failed_breakout_short_after_close_back_inside()
    {
        var candles = Range(40, 100m);
        for (var i = 0; i < 30; i++)
        {
            candles[i] = Bar(i, 100m, high: 101m, low: 99m);
        }

        candles[38] = Bar(38, 108m, high: 109m, low: 107m, volume: 30m);
        candles[39] = Bar(39, 100m, high: 101m, low: 99m, volume: 30m);
        var detail = Eval(StrategyTemplateKeys.FailedBreakoutReversal, candles, volumeOff: true);
        detail.Signal.Should().BeOneOf(SignalType.Sell, SignalType.NoAction);
    }

    [Fact]
    public void Vwap_deviation_long_below_vwap_with_rejection()
    {
        var t0 = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 40; i++)
        {
            candles.Add(BarAt(t0.AddMinutes(i * 5), 100m, 10m, 101m, 99m));
        }

        candles[^1] = BarAt(t0.AddMinutes(39 * 5), 90m, 20m, 91m, 88m);
        candles[^1].Open = 88.5m;
        var detail = Eval(StrategyTemplateKeys.VwapDeviationReversion, candles, volumeOff: true);
        detail.Signal.Should().BeOneOf(SignalType.Buy, SignalType.NoAction);
    }

    private static StrategySignalDetail Eval(string template, List<MarketCandle> candles, bool volumeOff = false)
    {
        var cache = new CausalIndicatorCache(candles);
        var p = StrategyTemplates.DefaultsFor(template, false) with
        {
            TemplateKey = template,
            AllowedSide = StrategySides.Both,
            VolumeFilterEnabled = !volumeOff,
            MinimumAdx = 80m,
            MinimumRelativeVolume = 0m,
            BreakoutRelativeVolume = 0m,
            ZScoreEntry = 2m,
            MaxVwapDistanceAtr = 1.0m,
            SwingLength = 3,
            EntryLookback = 10,
            AtrPeriod = 5,
            AdxPeriod = 5
        };
        return AdvancedStrategyEvaluator.Evaluate(p, candles, candles.Count - 1, Ctx(candles), cache);
    }

    private static StrategySignalDetail EvalAt(string template, List<MarketCandle> candles, int i, bool volumeOff = false)
    {
        var cache = new CausalIndicatorCache(candles);
        var p = StrategyTemplates.DefaultsFor(template, false) with
        {
            TemplateKey = template,
            AllowedSide = StrategySides.Both,
            VolumeFilterEnabled = !volumeOff,
            MinimumAdx = 80m,
            EntryLookback = 5,
            AtrPeriod = 5
        };
        return AdvancedStrategyEvaluator.Evaluate(p, candles, i, Ctx(candles), cache);
    }

    private static StrategyContext Ctx(IReadOnlyList<MarketCandle> candles) =>
        new()
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = false
        };

    private static List<MarketCandle> Range(int count, decimal price) =>
        Enumerable.Range(0, count).Select(i => Bar(i, price)).ToList();

    private static List<MarketCandle> Uptrend(int count)
    {
        var rows = new List<MarketCandle>(count);
        decimal price = 100m;
        for (var i = 0; i < count; i++)
        {
            price += 0.5m;
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
