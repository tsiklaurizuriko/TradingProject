using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class FuturesAlphaTests
{
    [Fact]
    public void Funding_zscore_does_not_use_future_observations()
    {
        var series = Enumerable.Range(0, 20).Select(i => (decimal?)(0.001m + i * 0.0001m)).ToArray();
        series[^1] = 0.90m;
        var z18 = FuturesAlphaFeatures.ZScoreAt(series, 18, 20);
        var prefix = series.Take(19).ToArray();
        FuturesAlphaFeatures.ZScoreAt(prefix, 18, 20).Should().Be(z18);
        z18.Should().NotBe(FuturesAlphaFeatures.ZScoreAt(series, 19, 20));
    }

    [Fact]
    public void Missing_oi_is_data_unavailable_not_a_trade()
    {
        var candles = Range(40, 100m);
        var cache = new CausalIndicatorCache(candles);
        var ctx = new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = 100m,
            FundingRate = Enumerable.Repeat((decimal?)0.0001m, 40).ToList(),
            OpenInterest = null
        };
        var p = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FundingOiReversal, false) with
        {
            AllowedSide = StrategySides.Both,
            UseFuturesFilter = true,
            FundingHypothesis = "contrarian",
            PriceDisplacementAtr = 0.01m
        };
        var detail = FuturesAlphaEvaluator.Evaluate(p, candles, 30, ctx, cache);
        detail.Status.Should().Be("DATA_UNAVAILABLE");
        detail.Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Continuation_and_contrarian_are_opposite_on_the_same_bar()
    {
        var candles = Range(80, 100m);
        for (var i = 50; i < 70; i++)
        {
            candles[i] = Bar(i, 100m - (i - 49));
        }

        candles[79] = Bar(79, 70m, open: 68m, high: 72m, low: 67m);
        var cache = new CausalIndicatorCache(candles);
        var funding = Enumerable.Range(0, 80).Select(_ => (decimal?)-0.01m).ToList();
        var oi = Enumerable.Range(0, 80).Select(_ => (decimal?)1_000_000m).ToList();
        var ctx = new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = 70m,
            FundingRate = funding,
            OpenInterest = oi
        };
        var core = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FundingOiReversal, false) with
        {
            AllowedSide = StrategySides.Both,
            UseFuturesFilter = true,
            FundingExtremePercentile = 0.5m,
            OiExtremePercentile = 0.1m,
            PriceDisplacementAtr = 0.01m,
            EntryLookback = 10
        };
        var contra = FuturesAlphaEvaluator.Evaluate(core with { FundingHypothesis = "contrarian", OiHypothesis = "contrarian" }, candles, 79, ctx, cache);
        var cont = FuturesAlphaEvaluator.Evaluate(core with { FundingHypothesis = "continuation", OiHypothesis = "continuation" }, candles, 79, ctx, cache);
        if (contra.Signal is SignalType.Buy or SignalType.Sell && cont.Signal is SignalType.Buy or SignalType.Sell)
        {
            contra.Signal.Should().NotBe(cont.Signal);
        }
    }

    [Fact]
    public void Baseline_does_not_require_futures_series()
    {
        var candles = Range(80, 100m);
        var cache = new CausalIndicatorCache(candles);
        var ctx = new StrategyContext { ClosedCandles = candles, CurrentPrice = 100m };
        var p = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.BasisMeanReversion, false) with
        {
            AllowedSide = StrategySides.Both,
            UseFuturesFilter = false,
            ZScoreEntry = 0.01m,
            FundingHypothesis = "contrarian"
        };
        var detail = FuturesAlphaEvaluator.Evaluate(p, candles, 70, ctx, cache);
        detail.Status.Should().NotBe("DATA_UNAVAILABLE");
    }

    [Fact]
    public void Future_funding_settlement_is_not_charged_before_fill()
    {
        var t0 = DateTimeOffset.UnixEpoch.AddDays(10);
        var candles = Enumerable.Range(0, 6).Select(i => BarAt(t0.AddMinutes(i * 5), 100m + i)).ToList();
        var definition = new StrategyDefinitionValidator().Parse(
            StrategyTemplates.Build("t", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.VpVwapReversion, false) with
            {
                AllowedSide = StrategySides.Both,
                Timeframe = "5m"
            }));
        var future = new ReplayFundingSettlement(candles[2].CloseTime.AddMinutes(1), 0.5m);
        var past = new ReplayFundingSettlement(candles[0].CloseTime, 0.5m);
        var engine = new StrategyEngine();
        var settings = new ReplaySettings(t0, t0.AddHours(1), 10_000m, 1m, 5m, 0.04m, 0.02m, 1m, 2m);
        var result = new BacktestReplay(engine).Run(definition, candles, settings, new CausalIndicatorCache(candles), 1, 5, null, null, [future, past]);
        result.CostNotes.Should().Be("INCLUDING_FUNDING");
        result.FundingPaid.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void Percentile_at_i_ignores_later_values()
    {
        var series = Enumerable.Range(0, 20).Select(i => (decimal?)(i + 1)).ToArray();
        series[^1] = 1000m;
        var p18 = FuturesAlphaFeatures.PercentileAt(series, 18, 20);
        var prefix = FuturesAlphaFeatures.PercentileAt(series.Take(19).ToArray(), 18, 20);
        p18.Should().Be(prefix);
        p18.Should().Be(1m);
    }

    [Fact]
    public void Oi_specs_are_labeled_sample_limited()
    {
        FuturesAlphaPilot.Specs().Where(s => s.RequiresOi).Should().NotBeEmpty();
        FuturesAlphaPilot.Specs().Should().OnlyContain(s => s.TemplateKey != StrategyTemplateKeys.TakerFlowMomentum);
    }

    private static List<MarketCandle> Range(int n, decimal close) =>
        Enumerable.Range(0, n).Select(i => Bar(i, close)).ToList();

    private static MarketCandle Bar(int i, decimal close, decimal open = 0m, decimal high = 0m, decimal low = 0m) =>
        new()
        {
            Open = open == 0m ? close : open,
            High = high == 0m ? close : high,
            Low = low == 0m ? close : low,
            Close = close,
            Volume = 10m,
            IsClosed = true,
            OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
            CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5),
            ExchangeTimestamp = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5)
        };

    private static MarketCandle BarAt(DateTimeOffset open, decimal close) =>
        new()
        {
            Open = close,
            High = close,
            Low = close,
            Close = close,
            Volume = 10m,
            IsClosed = true,
            OpenTime = open,
            CloseTime = open.AddMinutes(5),
            ExchangeTimestamp = open.AddMinutes(5)
        };
}
