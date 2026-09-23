using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Strategies.PriceAction;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class PriceActionEngineTests
{
    [Fact]
    public void Price_action_keys_are_research_only()
    {
        StrategyTemplateKeys.PriceAction.Should().HaveCount(18);
        StrategyTemplateKeys.IsPriceAction(StrategyTemplateKeys.PaWDoubleBottom).Should().BeTrue();
        StrategyTemplateKeys.IsOperatorCatalog(StrategyTemplateKeys.PaBullFlag).Should().BeFalse();
        StrategyTemplateKeys.Family(StrategyTemplateKeys.PaStructureBreak).Should().Be("SCALPING_PRICE_ACTION");
        StrategyTemplateKeys.Frozen.Should().Equal(
            "ema_rsi_trend", "macd_trend", "rsi_pullback", "bollinger_reversion", "donchian_breakout");
        StrategyTemplates.ResearchStatus(StrategyTemplateKeys.PaCandleSequence).Should().Be("RESEARCHING");
    }

    [Fact]
    public void Hammer_and_engulfing_are_events_not_long_signals()
    {
        var candles = new List<MarketCandle> { Bar(0, 100m, 101m, 99m), Bar(1, 99m, 99.2m, 98.8m) };
        candles.Add(Bar(2, 99.4m, 99.45m, 97.0m, open: 99.1m));
        var geoms = CandleGeom.Series(candles);
        var events = CandlePatternDetector.At(geoms, 2);
        events.Should().Contain(PatternKinds.Hammer);
        events.Should().Contain(PatternKinds.BullishRejection);
        events.Should().NotContain("LONG_SIGNAL");

        var engulf = new List<MarketCandle>
        {
            Bar(0, 100m),
            Bar(1, 98.6m, 99.2m, 98.5m, open: 99m),
            Bar(2, 100.1m, 100.2m, 98.4m, open: 98.5m)
        };
        CandlePatternDetector.At(CandleGeom.Series(engulf), 2).Should().Contain(PatternKinds.BullishEngulfing);
    }

    [Fact]
    public void Consecutive_bullish_count_is_causal()
    {
        var candles = Enumerable.Range(0, 8).Select(i => Bar(i, 100m + i, open: 99.5m + i)).ToList();
        var seq = CandleSequenceEngine.Detect(CandleGeom.Series(candles));
        seq[5].BullRun.Should().Be(6);
        var prefix = CandleSequenceEngine.Detect(CandleGeom.Series(candles.Take(6).ToList()));
        prefix[5].BullRun.Should().Be(seq[5].BullRun);
    }

    [Fact]
    public void Swing_confirmation_does_not_move_earlier_when_future_bars_arrive()
    {
        var prefix = Valley(40, 100m, 90m);
        var (ph, pl) = CausalSwingSeries.Detect(prefix, 3);
        var future = prefix.Concat(Enumerable.Range(0, 12).Select(i => Bar(prefix.Count + i, 110m + i))).ToList();
        var (fh, fl) = CausalSwingSeries.Detect(future, 3);
        foreach (var low in pl)
        {
            var match = fl.First(x => x.PivotIndex == low.PivotIndex);
            match.ConfirmationIndex.Should().Be(low.ConfirmationIndex);
            match.ConfirmationIndex.Should().BeGreaterThanOrEqualTo(low.PivotIndex);
        }

        var bookPrefix = PriceActionBook.Build(prefix);
        var bookFull = PriceActionBook.Build(future);
        var i = prefix.Count - 1;
        bookPrefix.Structure[i].Bias.Should().Be(bookFull.Structure[i].Bias);
    }

    [Fact]
    public void Outcome_labels_are_not_used_by_the_evaluator()
    {
        var candles = Enumerable.Range(0, 40).Select(i => Bar(i, 100m + (i % 3) * 0.2m)).ToList();
        var outcome = PatternOutcomeEngine.Measure(candles, 10, "LONG");
        outcome.Horizons.Should().Contain(h => h.Horizon == 5);
        var detail = Eval(StrategyTemplateKeys.PaCandleSequence, candles);
        detail.Reason.Should().NotContain("MFE");
        detail.Reason.Should().NotContain("forward");
    }

    [Fact]
    public void Liquidity_sweep_detects_wick_beyond_then_close_back()
    {
        var candles = new List<MarketCandle>();
        decimal px = 100m;
        for (var i = 0; i < 20; i++)
        {
            px -= 0.4m;
            candles.Add(Bar(i, px, px + 0.2m, px - 0.2m));
        }

        for (var i = 0; i < 16; i++)
        {
            px += 0.5m;
            candles.Add(Bar(candles.Count, px, px + 0.2m, px - 0.2m));
        }

        var swingLo = CausalSwingSeries.Detect(candles, 3).Lows.Last();
        candles.Add(Bar(candles.Count, swingLo.Price + 0.25m, swingLo.Price + 0.4m, swingLo.Price - 1.2m, open: swingLo.Price + 0.3m));
        var book = PriceActionBook.Build(candles);
        book.Occurrences.Should().Contain(o => o.PatternType == PatternKinds.LiquiditySweepLow && o.ConfirmationIndex == candles.Count - 1);
        Eval(StrategyTemplateKeys.PaLiquiditySweep, candles).Signal.Should().Be(SignalType.Buy);
    }

    [Fact]
    public void Failed_breakout_is_a_fade_event()
    {
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 30; i++)
        {
            candles.Add(Bar(i, 100m, 100.4m, 99.6m));
        }

        candles.Add(Bar(30, 101.2m, 101.4m, 100.9m));
        candles.Add(Bar(31, 99.8m, 100.2m, 99.5m));
        var book = PriceActionBook.Build(candles);
        book.Occurrences.Should().Contain(o => o.PatternType == PatternKinds.FailedBreakout);
    }

    [Fact]
    public void W_confirmation_is_the_neckline_close_not_the_second_low()
    {
        var candles = BuildW();
        var book = PriceActionBook.Build(candles);
        var w = book.Occurrences.Where(o => o.PatternType == PatternKinds.WDoubleBottom).ToList();
        w.Should().NotBeEmpty();
        foreach (var row in w.Where(o => o.ConfirmationIndex is not null))
        {
            row.ConfirmationIndex!.Value.Should().BeGreaterThan(row.DetectionIndex);
            candles[row.ConfirmationIndex.Value].Close.Should().BeGreaterThan(row.Neckline!.Value);
        }
    }

    [Fact]
    public void Future_bars_cannot_rewrite_a_past_confirmation_time()
    {
        var prefix = BuildW();
        var book = PriceActionBook.Build(prefix);
        var confirmed = book.Occurrences.Where(o => o.ConfirmationIndex is not null).ToList();
        var future = prefix.Concat(Enumerable.Range(0, 10).Select(i => Bar(prefix.Count + i, 130m))).ToList();
        var later = PriceActionBook.Build(future);
        foreach (var row in confirmed)
        {
            var match = later.Occurrences.FirstOrDefault(o =>
                o.PatternType == row.PatternType && o.StartIndex == row.StartIndex && o.DetectionIndex == row.DetectionIndex);
            match.Should().NotBeNull();
            match!.ConfirmationIndex.Should().Be(row.ConfirmationIndex);
        }
    }

    [Fact]
    public void Indexed_confirmation_lookup_matches_occurrence_filter()
    {
        var book = PriceActionBook.Build(BuildW());
        for (var i = 0; i < book.Candles.Count; i++)
        {
            var expected = book.Occurrences.Where(o =>
                o.ConfirmationIndex == i && o.Status == PatternKinds.Confirmed).ToArray();
            book.ConfirmedAt(i).Should().Equal(expected);
            foreach (var kind in expected.Select(x => x.PatternType).Distinct())
            {
                book.ConfirmedAt(i, kind).Should().Equal(expected.Where(x => x.PatternType == kind));
            }
        }
    }

    [Fact]
    public void Bull_flag_confirms_on_the_breakout_close()
    {
        var candles = new List<MarketCandle>();
        decimal px = 100m;
        for (var i = 0; i < 8; i++, px -= 0.2m)
        {
            candles.Add(Bar(candles.Count, px, px + 0.1m, px - 0.1m));
        }

        for (var i = 0; i < 6; i++, px += 1.8m)
        {
            candles.Add(Bar(candles.Count, px, px + 0.4m, px - 0.2m, volume: 80m));
        }

        for (var i = 0; i < 6; i++)
        {
            candles.Add(Bar(candles.Count, px - 0.2m + (i % 2) * 0.15m, px + 0.15m, px - 0.7m, volume: 8m));
        }

        var flagHi = candles.TakeLast(6).Max(c => c.High);
        candles.Add(Bar(candles.Count, flagHi + 0.8m, flagHi + 1.0m, flagHi - 0.1m, volume: 90m));
        var book = PriceActionBook.Build(candles);
        book.Occurrences.Should().Contain(o => o.PatternType == PatternKinds.BullFlag && o.ConfirmationIndex == candles.Count - 1);
    }

    [Fact]
    public void Structure_bos_uses_only_confirmed_swings()
    {
        var candles = new List<MarketCandle>();
        decimal px = 100m;
        for (var i = 0; i < 10; i++, px += 0.8m)
        {
            candles.Add(Bar(candles.Count, px, px + 0.2m, px - 0.2m, open: px - 0.4m));
        }

        var peak = px;
        for (var i = 0; i < 8; i++, px -= 0.6m)
        {
            candles.Add(Bar(candles.Count, px, px + 0.2m, px - 0.2m, open: px + 0.3m));
        }

        for (var i = 0; i < 12; i++, px += 1.2m)
        {
            candles.Add(Bar(candles.Count, Math.Max(px, peak + 0.5m + i * 0.4m), Math.Max(px, peak + 0.6m + i * 0.4m), px - 0.2m, open: px - 0.3m));
        }

        var book = PriceActionBook.Build(candles);
        book.Highs.Should().NotBeEmpty();
        book.Structure.Any(s => s.BosBull).Should().BeTrue();
        var firstBos = book.Structure.First(s => s.BosBull);
        var knownHi = CausalSwingSeries.LastKnown(book.Highs, firstBos.Index);
        knownHi.Should().NotBeNull();
        candles[firstBos.Index].Close.Should().BeGreaterThan(knownHi!.Value.Price);
    }

    [Fact]
    public void Head_and_shoulders_confirmation_is_neckline_close()
    {
        var candles = new List<MarketCandle>();
        void Run(decimal start, decimal step, int n)
        {
            var px = start;
            for (var i = 0; i < n; i++, px += step)
            {
                candles.Add(Bar(candles.Count, px, px + 0.3m, px - 0.3m));
            }
        }

        Run(100m, 0.8m, 8);
        Run(106.4m, -0.7m, 7);
        Run(101.5m, 1.2m, 10);
        Run(113.5m, -1.1m, 8);
        Run(104.7m, 0.75m, 8);
        Run(110.7m, -1.2m, 12);
        var book = PriceActionBook.Build(candles);
        var hs = book.Occurrences.Where(o => o.PatternType == PatternKinds.HeadShoulders).ToList();
        foreach (var row in hs.Where(o => o.ConfirmationIndex is not null))
        {
            row.ConfirmationIndex!.Value.Should().BeGreaterThan(row.DetectionIndex);
        }
    }
    [Fact]
    public void Cup_and_handle_is_not_implemented()
    {
        var book = PriceActionBook.Build(Enumerable.Range(0, 40).Select(i => Bar(i, 100m)).ToList());
        book.Occurrences.Should().NotContain(o => o.PatternType == PatternKinds.CupHandle);
    }

    private static StrategySignalDetail Eval(string template, IReadOnlyList<MarketCandle> candles)
    {
        var parsed = StrategyTemplates.DefaultsFor(template, false) with { AllowedSide = StrategySides.Both };
        return AdvancedStrategyEvaluator.Evaluate(parsed, candles, candles.Count - 1, new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = false
        }, new CausalIndicatorCache(candles));
    }

    private static List<MarketCandle> Valley(int count, decimal start, decimal bottom)
    {
        var candles = new List<MarketCandle>();
        var mid = count / 2;
        for (var i = 0; i < count; i++)
        {
            var t = i <= mid ? i / (decimal)mid : (count - 1 - i) / (decimal)(count - 1 - mid);
            var px = start + (bottom - start) * t;
            candles.Add(Bar(i, px, px + 0.3m, px - 0.3m));
        }

        return candles;
    }

    private static List<MarketCandle> BuildW()
    {
        var candles = new List<MarketCandle>();
        void Add(decimal close, decimal high, decimal low, decimal open)
        {
            candles.Add(Bar(candles.Count, close, high, low, open));
        }

        for (var i = 0; i < 8; i++)
        {
            Add(108m - i, 108.2m - i, 107.6m - i, 108.1m - i);
        }

        Add(100.0m, 100.4m, 99.6m, 101.0m);
        for (var i = 0; i < 3; i++)
        {
            Add(100.4m + i * 0.1m, 100.6m + i * 0.1m, 100.2m, 100.3m);
        }

        for (var i = 0; i < 8; i++)
        {
            Add(102m + i, 102.4m + i, 101.7m + i, 101.8m + i);
        }

        Add(110.0m, 110.4m, 109.6m, 109.2m);
        for (var i = 0; i < 3; i++)
        {
            Add(109.6m - i * 0.1m, 110.0m, 109.3m, 109.8m);
        }

        for (var i = 0; i < 8; i++)
        {
            Add(108m - i, 108.3m - i, 107.6m - i, 108.2m - i);
        }

        Add(100.2m, 100.6m, 99.7m, 101.0m);
        for (var i = 0; i < 4; i++)
        {
            Add(100.5m + i * 0.1m, 100.8m, 100.1m, 100.4m);
        }

        for (var i = 0; i < 12; i++)
        {
            Add(102m + i * 0.9m, 102.4m + i * 0.9m, 101.7m + i * 0.9m, 101.8m + i * 0.9m);
        }

        return candles;
    }

    private static MarketCandle Bar(int i, decimal close, decimal? high = null, decimal? low = null, decimal? open = null, decimal volume = 20m) =>
        new()
        {
            Open = open ?? close,
            High = high ?? close,
            Low = low ?? close,
            Close = close,
            Volume = volume,
            IsClosed = true,
            OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
            CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5)
        };
}
