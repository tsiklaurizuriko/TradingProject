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
        StrategyTemplateKeys.All.Should().HaveCount(
            StrategyTemplateKeys.Frozen.Length
            + StrategyTemplateKeys.Research.Length
            + StrategyTemplateKeys.NearMiss.Length
            + StrategyTemplateKeys.CrossSectionalReversal.Length
            + StrategyTemplateKeys.Range.Length
            + StrategyTemplateKeys.Flow.Length
            + StrategyTemplateKeys.Positioning.Length
            + StrategyTemplateKeys.Imported.Length
            + StrategyTemplateKeys.Refactored.Length
            + StrategyTemplateKeys.Observation.Length);
        StrategyTemplateKeys.Research.Should().HaveCount(75);
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
    public void Scalping_registry_is_research_only_and_never_validated_for_paper()
    {
        ResearchRegistry.Scalping.Should().HaveCount(StrategyTemplateKeys.Scalping.Length);
        ResearchRegistry.Scalping.Should().OnlyContain(c =>
            c.Status == ResearchStatuses.Researching
            && c.SupportedTimeframes.Contains("1m")
            && c.SupportedTimeframes.Contains("5m"));
        StrategyTemplateKeys.OperatorCatalog.Should().NotContain(StrategyTemplateKeys.ScalpEmaMomentum);
        StrategyTemplateKeys.OperatorCatalog.Should().NotContain(StrategyTemplateKeys.PaWDoubleBottom);
        ResearchRegistry.PriceAction.Should().HaveCount(18);
        ResearchRegistry.PriceAction.Should().OnlyContain(c => c.Status == ResearchStatuses.Researching);
        ResearchStatuses.ValidatedForPaper.Should().Be("VALIDATED_FOR_PAPER");

        var skipped = ResearchRunner.Evaluate(new ResearchRunRequest
        {
            Phase = "ALL",
            Candidates = [ResearchRegistry.Scalping[0]],
            Symbols = ["BTCUSDT"],
            Timeframes = ["5m"],
            Series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>(),
            UseLowIsolated = true,
            SkipWalkForward = true,
            CostLabels = [ResearchCostLabels.Base]
        });
        skipped.Should().NotBeEmpty();
        skipped.Should().OnlyContain(b => b.Status != ResearchStatuses.ValidatedForPaper);
    }

    [Fact]
    public void Cost_stress_flags_fragile_when_base_pf_beats_one_and_high_does_not()
    {
        var baseTotals = new PnlTotals(10, 6, 4, 0, 20m, 10m, 10m, 0.4m);
        var highTotals = new PnlTotals(10, 4, 6, 0, 8m, 12m, -4m, 0.6m);
        var robustness = ResearchDiagnostics.Summarize("SCALP-X", [
            CostBook("SCALP-X", "OOS", ResearchCostLabels.Base, baseTotals),
            CostBook("SCALP-X", "OOS", ResearchCostLabels.High, highTotals)
        ]);
        baseTotals.ProfitFactor.IsFinite.Should().BeTrue();
        baseTotals.ProfitFactor.Ratio.Should().BeGreaterThan(1m);
        highTotals.ProfitFactor.Ratio.Should().BeLessThan(1m);
        robustness.CostFragile.Should().BeTrue();
        robustness.Status.Should().Be(ResearchStatuses.CostFragile);
        robustness.Status.Should().NotBe(ResearchStatuses.ValidatedForPaper);
    }

    private static ResearchBookResult CostBook(string id, string phase, string cost, PnlTotals totals) =>
        new(
            id, "BTCUSDT", "5m", phase, cost, ResearchStatuses.Researching,
            100, totals.Trades, totals.NetPnl, totals.Fees, totals.WinRate, totals.Expectancy,
            "NORMAL", totals.ProfitFactor.FiniteRatio,
            totals.PositivePnlSum, totals.AbsoluteNegativePnlSum,
            totals.WinningTrades, totals.LosingTrades, totals.ZeroPnlTrades,
            null, null, totals,
            null, null, null, null, null, null, null, null, null,
            "RANGE", []);

    [Fact]
    public void Holding_percentiles_are_filled_on_research_books()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var replay = new ReplayResult(
            1000m, 1000m, 0m, 0m, 2, 0.5m, 1m, 1m, 1m, 0m, null, 0m, 1m, -1m, 10, start, start.AddHours(1), "",
            [
                new ReplayTrade(start, start.AddMinutes(10), 1m, 100m, 101m, 1m, 0.1m, "Take profit"),
                new ReplayTrade(start, start.AddMinutes(40), 1m, 100m, 99m, -1m, 0.1m, "Stop loss")
            ],
            [],
            new ReplaySideMetrics(1, 1m, 1m, 0m, 1m, 0m),
            new ReplaySideMetrics(1, -1m, 0m, 0m, -1m, 0m));
        var seed = new ResearchBookResult(
            "SCALP-EMA", "BTCUSDT", "5m", "OOS", "BASE", ResearchStatuses.Researching,
            10, 0, 0, 0, 0, 0, "NO_TRADES",
            null, 0, 0, 0, 0, 0,
            null, null, null, null, null, null, null, null, null, null, null, null,
            "RANGE", []);
        var candles = Enumerable.Range(0, 12).Select(i => new MarketCandle
        {
            OpenTime = start.AddMinutes(i * 5),
            CloseTime = start.AddMinutes(i * 5 + 5),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100m,
            IsClosed = true
        }).ToList();
        ResearchDiagnostics.Fill(seed, replay, candles, out var filled);
        filled.MedianHoldingMinutes.Should().NotBeNull();
        filled.P25HoldingMinutes.Should().NotBeNull();
        filled.P75HoldingMinutes.Should().NotBeNull();
        filled.Status.Should().NotBe(ResearchStatuses.ValidatedForPaper);
    }

    [Fact]
    public void Registry_has_fifteen_hypothesis_candidates()
    {
        ResearchRegistry.All.Should().HaveCount(15);
        ResearchRegistry.All.Select(c => c.CandidateId).Should().OnlyHaveUniqueItems();
        ResearchRegistry.All.Should().OnlyContain(c => c.SupportedDirections.Contains("LONG") && c.SupportedDirections.Contains("SHORT"));
    }

    [Fact]
    public void Wave2_registry_has_eight_unique_natives()
    {
        ResearchRegistry.Wave2.Should().HaveCount(8);
        ResearchRegistry.Wave2.Select(c => c.CandidateId).Should().OnlyHaveUniqueItems();
        ResearchRegistry.Wave2.Select(c => c.NativeKey).Should().OnlyHaveUniqueItems();
        ResearchRegistry.All.Select(c => c.CandidateId)
            .Intersect(ResearchRegistry.Wave2.Select(c => c.CandidateId), StringComparer.OrdinalIgnoreCase)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Low_isolated_research_book_matches_catalog_and_does_not_change_frozen_risk()
    {
        var from = new DateTimeOffset(2024, 9, 18, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        var frozen = StrategyValidation.FrozenRisk(from, to);
        frozen.InitialBalance.Should().Be(10_000m);
        frozen.RiskPercent.Should().Be(1m);
        frozen.Leverage.Should().Be(5m);
        frozen.HonorSuggestedStops.Should().BeFalse();

        var low = StrategyValidation.LowIsolatedRisk(from, to);
        low.InitialBalance.Should().Be(1_000m);
        low.RiskPercent.Should().Be(0.5m);
        low.Leverage.Should().Be(3m);
        low.StopLossPercent.Should().Be(2m);
        low.TakeProfitPercent.Should().Be(4m);
        low.MaxDailyLossPercent.Should().Be(3m);
        low.MaxSimultaneousPositions.Should().Be(2);
        low.MaxConsecutiveLosses.Should().Be(5);
        low.CooldownMinutes.Should().Be(30);
    }

    [Fact]
    public void Wave2_signal_at_index_does_not_change_when_future_bars_are_appended()
    {
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var prefix = Enumerable.Range(0, 80).Select(i => WaveBar(t0, i, 100m + (i % 7) - 3m)).ToList();
        var full = prefix.Concat(Enumerable.Range(80, 20).Select(i => WaveBar(t0, i, 140m))).ToList();
        var candidate = ResearchRegistry.Wave2.First(c => c.CandidateId == "W2-SWEEP-RECLAIM-001");
        var engine = new ResearchStrategyEngine(candidate);
        var definition = ResearchRunner.DefinitionFor(candidate, "1h");
        var ctx = new StrategyContext { ClosedCandles = prefix, CurrentPrice = prefix[^1].Close };
        var a = engine.EvaluateDetailAt(definition, ctx, new CausalIndicatorCache(prefix), 70);
        var b = engine.EvaluateDetailAt(definition, ctx, new CausalIndicatorCache(full), 70);
        a.Signal.Should().Be(b.Signal);
        a.SuggestedStop.Should().Be(b.SuggestedStop);
        a.SuggestedTakeProfit.Should().Be(b.SuggestedTakeProfit);
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

        var later = ResearchStrategyEngine.LastClosedHigherTimeframeIndex(htf, t0.AddMinutes(45));
        later.Should().Be(2);
    }

    [Fact]
    public void Price_action_mtf_gate_is_unchanged_when_future_context_is_appended()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var entry = Enumerable.Range(0, 180).Select(i =>
        {
            var bar = Bar(t0, i, 1);
            bar.Open = 100m + i * 0.01m;
            bar.High = 101m + i * 0.01m;
            bar.Low = 99m + i * 0.01m;
            bar.Close = 100.5m + i * 0.01m;
            return bar;
        }).ToList();
        var confirmationPrefix = Enumerable.Range(0, 50).Select(i => Bar(t0, i * 3, 3)).ToList();
        var confirmationFull = confirmationPrefix
            .Concat(Enumerable.Range(50, 10).Select(i =>
            {
                var bar = Bar(t0, i * 3, 3);
                bar.Close = 500m;
                bar.High = 501m;
                return bar;
            }))
            .ToList();
        var contextPrefix = Enumerable.Range(0, 10).Select(i => Bar(t0, i * 15, 15)).ToList();
        var contextFull = contextPrefix
            .Concat(Enumerable.Range(10, 5).Select(i =>
            {
                var bar = Bar(t0, i * 15, 15);
                bar.Close = 500m;
                bar.High = 501m;
                return bar;
            }))
            .ToList();
        var source = ResearchRegistry.PriceAction.First();
        var candidate = source with
        {
            Filters = source.Filters with { ConfirmationTimeframe = "3m", ContextTimeframe = "15m" },
            SupportedTimeframes = ["1m"]
        };
        var definition = ResearchRunner.DefinitionFor(candidate, "1m");
        var cache = new CausalIndicatorCache(entry);
        var context = new StrategyContext { ClosedCandles = entry, CurrentPrice = entry[89].Close };
        var a = new PriceActionMtfResearchEngine(
            candidate,
            new CausalIndicatorCache(confirmationPrefix),
            new CausalIndicatorCache(contextPrefix)).EvaluateDetailAt(definition, context, cache, 89);
        var b = new PriceActionMtfResearchEngine(
            candidate,
            new CausalIndicatorCache(confirmationFull),
            new CausalIndicatorCache(contextFull)).EvaluateDetailAt(definition, context, cache, 89);

        b.Signal.Should().Be(a.Signal);
        b.Reason.Should().Be(a.Reason);
    }

    [Fact]
    public void Precomputed_price_action_signals_match_direct_causal_evaluation()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(300).ToList();
        var candidate = ResearchRegistry.PriceAction.First(c =>
            c.ParentTemplateKey == StrategyTemplateKeys.PaStructureBreak) with
        {
            SupportedTimeframes = ["1h"]
        };
        var definition = ResearchRunner.DefinitionFor(candidate, "1h");
        var cache = new CausalIndicatorCache(candles);
        var direct = new ResearchStrategyEngine(candidate);
        var signals = Enumerable.Range(0, candles.Count).Select(i =>
            direct.EvaluateAt(
                definition,
                new StrategyContext { ClosedCandles = candles, CurrentPrice = candles[i].Close },
                cache,
                i,
                out _)).ToArray();
        var precomputed = new PrecomputedResearchSignalEngine(signals);
        for (var i = 0; i < candles.Count; i++)
        {
            precomputed.EvaluateAt(
                definition,
                new StrategyContext { ClosedCandles = candles, CurrentPrice = candles[i].Close },
                cache,
                i,
                out _).Should().Be(signals[i]);
        }
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

    private static MarketCandle WaveBar(DateTimeOffset t0, int i, decimal close) => new()
    {
        Open = close - 0.4m,
        High = close + 0.8m,
        Low = close - 0.8m,
        Close = close,
        Volume = 50m + (i % 5),
        IsClosed = true,
        OpenTime = t0.AddHours(i),
        CloseTime = t0.AddHours(i + 1),
        ExchangeTimestamp = t0.AddHours(i + 1)
    };
}
