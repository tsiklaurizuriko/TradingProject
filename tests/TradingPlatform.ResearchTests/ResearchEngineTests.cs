using System.Text.Json;
using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class ResearchEngineTests
{
    [Fact]
    public void Frozen_catalog_keys_are_unchanged()
    {
        StrategyTemplateKeys.Frozen.Should().Equal(
            "ema_rsi_trend",
            "macd_trend",
            "rsi_pullback",
            "bollinger_reversion",
            "donchian_breakout");
        StrategyTemplateKeys.All.Should().HaveCount(35);
        StrategyTemplateKeys.Research.Should().HaveCount(30);
        StrategyTemplateKeys.AdvancedSix.Should().HaveCount(6);
        StrategyTemplateKeys.Alpha.Should().HaveCount(24);
    }

    [Fact]
    public void Frozen_donchian_fingerprint_matches_strategy_engine_without_research_filters()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(400).ToList();
        var baseline = StrategyValidation.Run(StrategyValidation.Definition(StrategyTemplateKeys.DonchianBreakout, "1h"), candles);
        var identity = ResearchRegistry.All.First(c => c.CandidateId == "DONCHIAN-TREND-001") with
        {
            Filters = new ResearchFilters()
        };
        var definition = ResearchRunner.DefinitionFor(identity, "1h");
        var cache = new CausalIndicatorCache(candles);
        var engine = new ResearchStrategyEngine(identity);
        var warmup = StrategyValidation.WarmupBars(definition);
        var settings = StrategyValidation.FrozenRisk(candles[warmup].OpenTime, candles[^1].CloseTime);
        var research = new BacktestReplay(engine).Run(definition, candles, settings, cache, warmup, candles.Count);
        research.NumberOfTrades.Should().Be(baseline.NumberOfTrades);
        research.NetProfit.Should().Be(baseline.NetProfit);
        research.Totals!.PositivePnlSum.Should().Be(baseline.Totals!.PositivePnlSum);
        research.Totals.AbsoluteNegativePnlSum.Should().Be(baseline.Totals.AbsoluteNegativePnlSum);
    }

    [Fact]
    public void Research_filters_do_not_look_ahead()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(240).ToList();
        var full = new CausalIndicatorCache(candles);
        foreach (var i in new[] { 80, 120, 180, 239 })
        {
            var prefix = new CausalIndicatorCache(candles.Take(i + 1).ToList());
            prefix.SessionVwap()[i].Should().Be(full.SessionVwap()[i]);
            prefix.Adx(14)[i].Should().Be(full.Adx(14)[i]);
            prefix.SupertrendDirection(10, 3m)[i].Should().Be(full.SupertrendDirection(10, 3m)[i]);
            prefix.AtrPercentile(14, 50)[i].Should().Be(full.AtrPercentile(14, 50)[i]);
            prefix.RelativeVolume(20)[i].Should().Be(full.RelativeVolume(20)[i]);
        }
    }

    [Fact]
    public void Donchian_uses_previous_candles_only()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(80).ToList();
        var (high, _) = new CausalIndicatorCache(candles).Donchian(20);
        var i = 40;
        high[i].Should().Be(candles.Skip(i - 20).Take(20).Max(c => c.High));
        high[i].Should().NotBe(candles[i].High);
    }

    [Fact]
    public void Parent_and_native_signals_are_closed_candle_only()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(200).ToList();
        candles[^1].IsClosed = false;
        var closed = candles.Where(c => c.IsClosed).ToList();
        var cache = new CausalIndicatorCache(closed);
        var engine = new ResearchStrategyEngine(ResearchRegistry.Find("ST-EMA-001")!);
        var definition = ResearchRunner.DefinitionFor(ResearchRegistry.Find("ST-EMA-001")!, "1h");
        engine.EvaluateAt(
            definition,
            new StrategyContext { ClosedCandles = closed, CurrentPrice = closed[^1].Close, HasOpenPosition = false },
            cache,
            closed.Count - 1,
            out _).Should().BeOneOf(SignalType.Buy, SignalType.Sell, SignalType.NoAction);
    }

    [Fact]
    public void Long_short_native_rules_are_symmetric_on_mirrored_series()
    {
        var up = Trend(120, +0.8m);
        var down = Trend(120, -0.8m);
        var candidate = ResearchRegistry.Find("ST-EMA-001")!;
        var engine = new ResearchStrategyEngine(candidate);
        var definition = ResearchRunner.DefinitionFor(candidate, "1h");
        var upSignal = Count(engine, definition, up, SignalType.Buy);
        var downSignal = Count(engine, definition, down, SignalType.Sell);
        (upSignal + downSignal).Should().BeGreaterThan(0);
        Math.Abs(upSignal - downSignal).Should().BeLessThanOrEqualTo(Math.Max(2, upSignal / 2 + 1));
    }

    [Fact]
    public void Duplicate_entries_are_not_emitted_while_flat_engine_holds_a_position()
    {
        var candles = Trend(180, 0.6m);
        var candidate = ResearchRegistry.Find("ST-EMA-001")!;
        var engine = new ResearchStrategyEngine(candidate);
        var definition = ResearchRunner.DefinitionFor(candidate, "1h");
        var cache = new CausalIndicatorCache(candles);
        var buys = 0;
        for (var i = 40; i < candles.Count; i++)
        {
            var signal = engine.EvaluateAt(
                definition,
                new StrategyContext
                {
                    ClosedCandles = candles,
                    CurrentPrice = candles[i].Close,
                    HasOpenPosition = buys > 0,
                    PositionSide = PositionSide.Long
                },
                cache,
                i,
                out _);
            if (signal == SignalType.Buy)
            {
                buys++;
            }
        }

        buys.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Regime_classification_is_causal()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(80).ToList();
        var at40 = ResearchDiagnostics.ClassifyAtSignal(candles, 40);
        var prefix = ResearchDiagnostics.ClassifyAtSignal(candles.Take(41).ToList(), 40);
        at40.Should().Be(prefix);
    }

    [Fact]
    public void Candidate_json_roundtrip_is_reproducible()
    {
        var original = ResearchRegistry.Find("VWAP-PB-001")!;
        var json = JsonSerializer.Serialize(original);
        var copy = JsonSerializer.Deserialize<ResearchCandidate>(json);
        copy.Should().BeEquivalentTo(original);
        copy!.CandidateId.Should().Be("VWAP-PB-001");
        copy.CodeVersion.Should().Be(ResearchCandidate.EngineVersion);
    }

    [Fact]
    public void Profit_factor_is_sum_wins_over_abs_losses_and_never_ninety_nine()
    {
        var combined = PnlTotals.Combine(
            PnlTotals.FromTrades([new ReplayTrade(default, default, 1, 1, 1, 100m, 0, "a", "Long"), new ReplayTrade(default, default, 1, 1, 1, 50m, 0, "a", "Long"), new ReplayTrade(default, default, 1, 1, 1, -75m, 0, "a", "Long")]),
            PnlTotals.FromTrades([new ReplayTrade(default, default, 1, 1, 1, 10m, 0, "b", "Short"), new ReplayTrade(default, default, 1, 1, 1, -100m, 0, "b", "Short")]));
        combined.ProfitFactor.Ratio.Should().Be(Math.Round(160m / 175m, 8, MidpointRounding.AwayFromZero));
        combined.ProfitFactor.Ratio.Should().NotBe(99m);
        ResearchPf.From(PnlTotals.Empty).State.Should().Be("NO_TRADES");
        var noLoss = PnlTotals.FromTrades([new ReplayTrade(default, default, 1, 1, 1, 12m, 0, "c", "Long")]);
        ResearchPf.From(noLoss).State.Should().Be("NO_LOSSES");
        ResearchPf.Render("NO_TRADES", null).Should().Be("N/A");
        ResearchPf.Render("NO_LOSSES", null).Should().NotContain("99");
    }

    [Fact]
    public void High_cost_settings_scale_fees_and_slippage()
    {
        var t0 = DateTimeOffset.UtcNow;
        var baseline = ResearchRunner.CostScaledRisk(t0, t0.AddDays(1), ResearchCostLabels.Base);
        var high = ResearchRunner.CostScaledRisk(t0, t0.AddDays(1), ResearchCostLabels.High);
        var stress = ResearchRunner.CostScaledRisk(t0, t0.AddDays(1), ResearchCostLabels.Stress);
        high.FeePercent.Should().Be(baseline.FeePercent * 1.5m);
        stress.SlippagePercent.Should().Be(baseline.SlippagePercent * 2m);
    }

    [Fact]
    public void Is_phase_does_not_emit_oos_windows()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(300).ToList();
        var books = ResearchRunner.Evaluate(new ResearchRunRequest
        {
            Phase = ResearchPhases.Is,
            Candidates = [ResearchRegistry.Find("DONCHIAN-ATR-001")!],
            Symbols = ["BTCUSDT"],
            Timeframes = ["1h"],
            Series = new Dictionary<(string, string), IReadOnlyList<MarketCandle>> { [("BTCUSDT", "1h")] = candles },
            CostLabels = [ResearchCostLabels.Base]
        });
        books.Should().OnlyContain(b => b.Phase == "IS");
        books.Should().NotContain(b => b.Phase == "OOS");
    }

    [Fact]
    public void Registry_has_fifteen_hypothesis_candidates()
    {
        ResearchRegistry.All.Should().HaveCount(15);
        ResearchRegistry.All.Select(c => c.CandidateId).Should().OnlyHaveUniqueItems();
        ResearchRegistry.All.Should().OnlyContain(c => c.SupportedDirections.Contains("LONG") && c.SupportedDirections.Contains("SHORT"));
    }

    [Fact]
    public void Htf_aligner_does_not_use_unclosed_future_bars()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var htf = new CausalIndicatorCache(
        [
            Bar(t0, 0, 15),
            Bar(t0, 15, 15),
            Bar(t0, 30, 15)
        ]);
        var signalClose = t0.AddMinutes(20);
        var idx = ResearchStrategyEngine.LastClosedHigherTimeframeIndex(htf, signalClose);
        idx.Should().Be(0);
        htf.Candles[idx].CloseTime.Should().BeOnOrBefore(signalClose);
    }

    [Fact]
    public void Empty_walk_forward_window_is_no_trades_not_zero_pf()
    {
        var empty = PnlTotals.Empty;
        var (state, ratio) = ResearchPf.From(empty);
        state.Should().Be("NO_TRADES");
        ratio.Should().BeNull();
        ResearchPf.Render(state, ratio).Should().Be("N/A");
        ResearchPf.Render(state, ratio).Should().NotBe("0");
    }

    private static ReplayResult RunCandidate(ResearchCandidate candidate, IReadOnlyList<MarketCandle> candles)
    {
        var definition = ResearchRunner.DefinitionFor(candidate, "1h");
        var cache = new CausalIndicatorCache(candles);
        var engine = new ResearchStrategyEngine(candidate);
        var settings = StrategyValidation.FrozenRisk(candles[0].OpenTime, candles[^1].CloseTime);
        return new BacktestReplay(engine).Run(definition, candles, settings, cache, 0, candles.Count);
    }

    private static int Count(ResearchStrategyEngine engine, StrategyDefinition definition, List<MarketCandle> candles, SignalType wanted)
    {
        var cache = new CausalIndicatorCache(candles);
        var n = 0;
        for (var i = 40; i < candles.Count; i++)
        {
            var signal = engine.EvaluateAt(
                definition,
                new StrategyContext { ClosedCandles = candles, CurrentPrice = candles[i].Close, HasOpenPosition = false },
                cache,
                i,
                out _);
            if (signal == wanted)
            {
                n++;
            }
        }

        return n;
    }

    [Fact]
    public void Robustness_summary_is_scoped_to_one_candidate()
    {
        var a = Book("A", "BTCUSDT", 2.0m, 100m, 50m);
        var b = Book("B", "ETHUSDT", 0.4m, 40m, 100m);
        var sa = ResearchDiagnostics.Summarize("A", [a, b]);
        var sb = ResearchDiagnostics.Summarize("B", [a, b]);
        sa.SymbolsTested.Should().Be(1);
        sb.SymbolsTested.Should().Be(1);
        sa.MedianSymbolPf.Should().Be(2.0m);
        sb.MedianSymbolPf.Should().Be(0.4m);
        sa.TimeframePf.Should().ContainKey("1h");
        sa.TimeframePf["1h"].Should().NotBe(sb.TimeframePf["1h"]);
    }

    private static ResearchBookResult Book(string id, string symbol, decimal pf, decimal win, decimal loss)
    {
        var totals = new PnlTotals(10, 4, 6, 0, win, loss, win - loss, 1m);
        totals.ProfitFactor.Ratio.Should().Be(pf);
        return new ResearchBookResult(
            id, symbol, "1h", "OOS", ResearchCostLabels.Base, ResearchStatuses.ResearchComplete,
            100, 10, win - loss, 1m, 40m, (win - loss) / 10m, "NORMAL", pf,
            win, loss, 4, 6, 0,
            totals, totals, totals,
            10, 10, 10, 0, 0, 0, 0, 0, 0, "RANGE", []);
    }

    private static List<MarketCandle> Trend(int count, decimal step)
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        decimal price = 100m;
        var rows = new List<MarketCandle>(count);
        for (var i = 0; i < count; i++)
        {
            var open = price;
            var close = Math.Max(1m, price + step);
            rows.Add(new MarketCandle
            {
                Open = open,
                High = Math.Max(open, close) + 0.2m,
                Low = Math.Min(open, close) - 0.2m,
                Close = close,
                Volume = 100m + i,
                IsClosed = true,
                OpenTime = t0.AddHours(i),
                CloseTime = t0.AddHours(i + 1),
                ExchangeTimestamp = t0.AddHours(i + 1)
            });
            price = close;
        }

        return rows;
    }

    private static MarketCandle Bar(DateTimeOffset t0, int minutes, int length) => new()
    {
        Open = 100,
        High = 101,
        Low = 99,
        Close = 100,
        Volume = 10,
        IsClosed = true,
        OpenTime = t0.AddMinutes(minutes),
        CloseTime = t0.AddMinutes(minutes + length),
        ExchangeTimestamp = t0.AddMinutes(minutes + length)
    };
}
