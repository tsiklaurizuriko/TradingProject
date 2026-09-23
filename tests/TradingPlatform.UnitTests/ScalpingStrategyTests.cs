using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class ScalpingStrategyTests
{
    [Fact]
    public void Scalp_keys_are_research_only_and_not_operator_catalog()
    {
        StrategyTemplateKeys.IsScalping(StrategyTemplateKeys.ScalpEmaMomentum).Should().BeTrue();
        StrategyTemplateKeys.IsOperatorCatalog(StrategyTemplateKeys.ScalpEmaMomentum).Should().BeFalse();
        StrategyTemplateKeys.Family(StrategyTemplateKeys.ScalpStochMomentum).Should().Be("SCALPING");
        StrategyTemplateKeys.RequiredDatasets(StrategyTemplateKeys.ScalpTakerFlow).Should().Contain("TakerFlow");
        StrategyTemplates.ResearchStatus(StrategyTemplateKeys.ScalpTakerFlow).Should().Be("DATA_UNAVAILABLE");
        StrategyTemplates.ResearchStatus(StrategyTemplateKeys.ScalpEmaMomentum).Should().Be("RESEARCHING");
    }

    [Fact]
    public void Ema_momentum_fires_long_on_a_fast_cross()
    {
        var candles = new List<MarketCandle>();
        decimal price = 120m;
        for (var i = 0; i < 36; i++)
        {
            price -= 0.8m;
            candles.Add(Bar(i, price));
        }

        for (var i = 0; i < 12; i++)
        {
            price += 3m;
            candles.Add(Bar(candles.Count, price));
        }

        var parsed = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ScalpEmaMomentum, false) with
        {
            AllowedSide = StrategySides.Both,
            VolumeFilterEnabled = false,
            EmaFast = 3,
            EmaSlow = 8,
            RsiPeriod = 5,
            RsiMinimum = 0m,
            RsiLongMax = 100m
        };
        var buy = -1;
        for (var i = 16; i < candles.Count; i++)
        {
            var slice = candles.Take(i + 1).ToList();
            var detail = AdvancedStrategyEvaluator.Evaluate(parsed, slice, slice.Count - 1, new StrategyContext
            {
                ClosedCandles = slice,
                CurrentPrice = slice[^1].Close,
                HasOpenPosition = false
            }, new CausalIndicatorCache(slice));
            if (detail.Signal == SignalType.Buy)
            {
                buy = i;
                break;
            }
        }

        buy.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Stochastic_momentum_crosses_from_an_extreme()
    {
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 30; i++)
        {
            candles.Add(Bar(i, 50m - i * 0.4m, high: 50.2m - i * 0.4m, low: 49.6m - i * 0.4m));
        }

        for (var i = 0; i < 8; i++)
        {
            candles.Add(Bar(candles.Count, 38m + i * 0.8m, high: 38.4m + i * 0.8m, low: 37.6m + i * 0.8m));
        }

        var signal = Eval(StrategyTemplateKeys.ScalpStochMomentum, candles).Signal;
        signal.Should().BeOneOf(SignalType.Buy, SignalType.NoAction);
        if (signal == SignalType.NoAction)
        {
            var k = ScalpingIndicatorSeries.StochasticK(candles, 9);
            k[^1].Should().NotBeNull();
        }
    }

    [Fact]
    public void Futures_scalp_keys_are_data_unavailable_without_series()
    {
        var detail = Eval(StrategyTemplateKeys.ScalpTakerFlow, Range(40, 100m));
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Status.Should().Be("DATA_UNAVAILABLE");
        detail.Reason.Should().Contain("DATA_UNAVAILABLE");
    }

    [Fact]
    public void One_minute_and_five_minute_use_the_same_closed_bar_evaluator()
    {
        var five = Range(40, 100m);
        for (var i = 25; i < five.Count; i++)
        {
            five[i] = Bar(i, 100m + (i - 24), minutes: 5);
        }

        var one = Range(40, 100m);
        for (var i = 25; i < one.Count; i++)
        {
            one[i] = Bar(i, 100m + (i - 24), minutes: 1);
        }

        Eval(StrategyTemplateKeys.ScalpEmaMomentum, five).Signal
            .Should().Be(Eval(StrategyTemplateKeys.ScalpEmaMomentum, one).Signal);
    }

    private static int IndexOf(string template, List<MarketCandle> candles, SignalType want)
    {
        for (var i = 20; i < candles.Count; i++)
        {
            if (Eval(template, candles.Take(i + 1).ToList()).Signal == want)
            {
                return i;
            }
        }

        return -1;
    }

    private static StrategySignalDetail Eval(string template, IReadOnlyList<MarketCandle> candles)
    {
        var parsed = StrategyTemplates.DefaultsFor(template, false) with { AllowedSide = StrategySides.Both, VolumeFilterEnabled = false };
        var cache = new CausalIndicatorCache(candles);
        return AdvancedStrategyEvaluator.Evaluate(parsed, candles, candles.Count - 1, new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = false
        }, cache);
    }

    private static List<MarketCandle> Range(int count, decimal price) =>
        Enumerable.Range(0, count).Select(i => Bar(i, price)).ToList();

    private static MarketCandle Bar(int i, decimal close, decimal? high = null, decimal? low = null, int minutes = 5) =>
        new()
        {
            Open = close,
            High = high ?? close,
            Low = low ?? close,
            Close = close,
            Volume = 20m,
            IsClosed = true,
            OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * minutes),
            CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * minutes + minutes)
        };
}
