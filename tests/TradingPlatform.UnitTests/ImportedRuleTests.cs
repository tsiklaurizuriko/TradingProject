using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class ImportedRuleTests
{
    [Fact]
    public void Contrarian_sma_shorts_when_the_fast_average_clears_the_upper_band_and_flips_long_below_the_lower_band()
    {
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 12; i++)
        {
            candles.Add(Bar(i, 100m));
        }

        Eval(StrategyTemplateKeys.MacContrarian710, candles, false, PositionSide.Long).Signal.Should().Be(SignalType.NoAction);

        for (var i = 0; i < 8; i++)
        {
            candles.Add(Bar(candles.Count, 100m + (i + 1) * 2m));
        }

        var shortSignal = Eval(StrategyTemplateKeys.MacContrarian710, candles, false, PositionSide.Long);
        shortSignal.Signal.Should().Be(SignalType.Sell);

        var held = Eval(StrategyTemplateKeys.MacContrarian710, candles, true, PositionSide.Short);
        held.Signal.Should().Be(SignalType.Hold);

        for (var i = 0; i < 10; i++)
        {
            candles.Add(Bar(candles.Count, 80m - i));
        }

        var flip = Eval(StrategyTemplateKeys.MacContrarian710, candles, true, PositionSide.Short);
        flip.Signal.Should().Be(SignalType.Buy);
        flip.SuggestedStop.Should().BeNull();
    }

    [Fact]
    public void Zigzag_fade_shorts_a_close_through_the_prior_swing_high()
    {
        var candles = new List<MarketCandle>();
        decimal[] highs = [11, 11, 11, 11, 11, 11, 16, 12, 12, 12, 18];
        decimal[] lows = [10, 10, 10, 7, 10, 10, 12, 10, 10, 10, 16];
        decimal[] closes = [10, 10, 10, 8, 10, 10, 15, 11, 11, 11, 17];
        for (var i = 0; i < closes.Length; i++)
        {
            candles.Add(Bar(i, closes[i], highs[i], lows[i]));
        }

        var parameters = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ZigZagFade, false) with
        {
            SwingLength = 2,
            PriceChangeThreshold = 1m,
            AtrPeriod = 3,
            AtrStopMultiplier = 1.5m
        };
        var detail = Evaluate(parameters, candles, false, PositionSide.Long);
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Status.Should().NotBe("IMPORTED_RULE");
    }

    [Fact]
    public void Donchian_v2_buys_a_close_above_the_prior_entry_channel_without_a_take_profit()
    {
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 8; i++)
        {
            candles.Add(Bar(i, 100m, high: 101m, low: 99m));
        }

        candles.Add(Bar(8, 105m, high: 106m, low: 104m));
        var parameters = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.DonchianV2, false) with
        {
            EntryLookback = 5,
            ExitLookback = 3,
            AtrPeriod = 3
        };
        var detail = Evaluate(parameters, candles, false, PositionSide.Long);
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Reason.Should().Contain("Donchian v2");
        detail.Status.Should().NotBe("IMPORTED_RULE");
    }

    [Fact]
    public void Existing_ema_template_is_not_routed_through_the_imported_rules()
    {
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 60; i++)
        {
            candles.Add(Bar(i, 100m + i));
        }

        var detail = Eval(StrategyTemplateKeys.EmaRsiTrend, candles, false, PositionSide.Long);
        detail.Status.Should().NotBe("IMPORTED_RULE");
    }

    [Fact]
    public void BinHV45_buys_a_close_under_the_prior_lower_band_with_a_short_wick()
    {
        var candles = Flat(40, 100m);
        candles.Add(Bar(40, 90m, high: 100m, low: 89.99m));

        var detail = Eval(StrategyTemplateKeys.BinHv45, candles, false, PositionSide.Long);
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Reason.Should().Contain("BinHV45");
        detail.Status.Should().NotBe("IMPORTED_RULE");
    }

    [Fact]
    public void BinHV45_holds_when_a_wick_tags_the_bracket_and_the_close_recovers()
    {
        var candles = Flat(5, 100m);
        candles[^1] = Bar(4, 100m, high: 103m, low: 96m);

        Evaluate(
            StrategyTemplates.DefaultsFor(StrategyTemplateKeys.BinHv45, false),
            candles,
            true,
            PositionSide.Long,
            entry: 100m).Signal.Should().Be(SignalType.NoAction);

        candles[^1] = Bar(4, 102.5m, high: 102.5m, low: 100m);
        Evaluate(
            StrategyTemplates.DefaultsFor(StrategyTemplateKeys.BinHv45, false),
            candles,
            true,
            PositionSide.Long,
            entry: 100m).Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Cluc_buys_under_the_slow_average_and_exits_above_the_middle_band()
    {
        var candles = Flat(50, 100m);
        candles.Add(Bar(50, 80m, high: 80m, low: 80m));

        Eval(StrategyTemplateKeys.ClucMay72018, candles, false, PositionSide.Long).Signal.Should().Be(SignalType.NoAction);

        var held = candles.Take(50).ToList();
        held.Add(Bar(50, 120m, high: 120m, low: 119m));
        Evaluate(StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ClucMay72018, false), held, true, PositionSide.Long, entry: 119m)
            .Signal.Should().Be(SignalType.Exit);
    }

    [Fact]
    public void Combined_keeps_a_losing_long_when_price_is_only_above_the_middle_band()
    {
        var candles = Flat(50, 100m);
        candles.Add(Bar(50, 101m, high: 102m, low: 100.5m));

        var detail = Evaluate(
            StrategyTemplates.DefaultsFor(StrategyTemplateKeys.CombinedBinHCluc, false),
            candles,
            true,
            PositionSide.Long,
            entry: 103m);
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Status.Should().NotBe("IMPORTED_RULE");
    }

    [Fact]
    public void Hlhb_buys_when_rsi_and_ema_cross_up_together()
    {
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 60; i++)
        {
            candles.Add(Bar(i, 300m - i));
        }

        candles.Add(Bar(candles.Count, 320m, high: 322m, low: 240m));

        var bought = false;
        for (var i = 20; i < candles.Count; i++)
        {
            if (Eval(StrategyTemplateKeys.Hlhb, candles.Take(i + 1).ToList(), false, PositionSide.Long).Signal == SignalType.Buy)
            {
                bought = true;
                break;
            }
        }

        bought.Should().BeTrue();
    }

    private static List<MarketCandle> Flat(int count, decimal price)
    {
        var candles = new List<MarketCandle>(count);
        for (var i = 0; i < count; i++)
        {
            candles.Add(Bar(i, price));
        }

        return candles;
    }

    private static StrategySignalDetail Eval(string key, IReadOnlyList<MarketCandle> candles, bool open, PositionSide side) =>
        Evaluate(StrategyTemplates.DefaultsFor(key, false), candles, open, side);

    private static StrategySignalDetail Evaluate(
        StrategyTemplateParams parameters,
        IReadOnlyList<MarketCandle> candles,
        bool open,
        PositionSide side,
        decimal? entry = null)
    {
        var json = StrategyTemplates.Build("imported", 1, parameters);
        var definition = new StrategyDefinitionValidator().Parse(json);
        return new StrategyEngine().EvaluateDetailAt(
            definition,
            new StrategyContext
            {
                ClosedCandles = candles,
                HasOpenPosition = open,
                PositionSide = side,
                AverageEntryPrice = entry,
                CurrentPrice = candles[^1].Close
            },
            new CausalIndicatorCache(candles),
            candles.Count - 1);
    }

    [Fact]
    public void Adx_sma_stays_flat_until_the_slow_average_exists()
    {
        var candles = Enumerable.Range(0, 40).Select(i => Bar(i, 100m + i)).ToList();
        Eval(StrategyTemplateKeys.FAdxSma, candles, false, PositionSide.Long).Signal.Should().Be(SignalType.NoAction);
    }

    private static MarketCandle Bar(int index, decimal close, decimal? high = null, decimal? low = null)
    {
        var open = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(index * 5);
        return new MarketCandle
        {
            Open = close,
            High = high ?? close,
            Low = low ?? close,
            Close = close,
            Volume = 1m,
            IsClosed = true,
            OpenTime = open,
            CloseTime = open.AddMinutes(5),
            ExchangeTimestamp = open.AddMinutes(5)
        };
    }
}
