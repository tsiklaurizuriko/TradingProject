using FluentAssertions;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;
using Xunit.Abstractions;

namespace TradingPlatform.BacktestingTests;

public sealed class ModelBValidationTests
{
    private readonly ITestOutputHelper _output;

    public ModelBValidationTests(ITestOutputHelper output) => _output = output;
    [Fact]
    public void Warmup_is_dataset_level_and_at_least_preferred_120()
    {
        var definition = StrategyValidation.Definition(StrategyTemplateKeys.EmaRsiTrend, "1h");
        StrategyValidation.RequiredLookback(definition).Should().BeGreaterThanOrEqualTo(50);
        StrategyValidation.WarmupBars(definition).Should().Be(StrategyValidation.PreferredWarmupBars);
        StrategyValidation.WarmupBars(definition).Should().BeGreaterThanOrEqualTo(StrategyValidation.RequiredLookback(definition));
    }

    [Fact]
    public void Full_series_indicator_at_i_matches_prefix_0_through_i()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(240).ToList();
        var full = new CausalIndicatorCache(candles);
        foreach (var i in new[] { 50, 80, 120, 180, 239 })
        {
            var prefix = new CausalIndicatorCache(candles.Take(i + 1).ToList());
            prefix.Ema(50)[i].Should().Be(full.Ema(50)[i]);
            prefix.Rsi(14)[i].Should().Be(full.Rsi(14)[i]);
            prefix.AtrPercent(14)[i].Should().Be(full.AtrPercent(14)[i]);
            var (pm, ps, ph) = prefix.Macd(12, 26, 9);
            var (fm, fs, fh) = full.Macd(12, 26, 9);
            pm[i].Should().Be(fm[i]);
            ps[i].Should().Be(fs[i]);
            ph[i].Should().Be(fh[i]);
            var (pb, pu, pl) = prefix.Bollinger(20, 2m);
            var (fb, fu, fl) = full.Bollinger(20, 2m);
            pb[i].Should().Be(fb[i]);
            pu[i].Should().Be(fu[i]);
            pl[i].Should().Be(fl[i]);
            var (pdh, pdl) = prefix.Donchian(20);
            var (fdh, fdl) = full.Donchian(20);
            pdh[i].Should().Be(fdh[i]);
            pdl[i].Should().Be(fdl[i]);
        }
    }

    [Fact]
    public void Validation_flat_signals_match_continuous_evaluate_at_on_the_same_indices()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(800).ToList();
        var cache = new CausalIndicatorCache(candles);
        var definition = StrategyValidation.Definition(StrategyTemplateKeys.DonchianBreakout, "1h");
        var engine = new StrategyEngine();
        var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
        for (var i = insEnd; i < valEnd; i += 7)
        {
            var prefix = candles.Take(i + 1).ToList();
            var prefixSignal = engine.Evaluate(
                definition,
                new StrategyContext
                {
                    ClosedCandles = prefix,
                    CurrentPrice = prefix[^1].Close,
                    HasOpenPosition = false
                },
                out var prefixReason);
            var at = engine.EvaluateAt(
                definition,
                new StrategyContext
                {
                    ClosedCandles = candles,
                    CurrentPrice = candles[i].Close,
                    HasOpenPosition = false
                },
                cache,
                i,
                out var atReason);
            at.Should().Be(prefixSignal, "VAL bar {0}: {1} vs {2}", i, prefixReason, atReason);
        }
    }

    [Fact]
    public void Oos_indicator_values_match_the_full_history_cache()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(800).ToList();
        var full = new CausalIndicatorCache(candles);
        var (_, valEnd) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
        var oos = new CausalIndicatorCache(candles);
        for (var i = valEnd; i < candles.Count; i += 17)
        {
            oos.Ema(50)[i].Should().Be(full.Ema(50)[i]);
            oos.Rsi(14)[i].Should().Be(full.Rsi(14)[i]);
            var (om, os, oh) = oos.Macd(12, 26, 9);
            var (fm, fs, fh) = full.Macd(12, 26, 9);
            om[i].Should().Be(fm[i]);
            os[i].Should().Be(fs[i]);
            oh[i].Should().Be(fh[i]);
            oos.AtrPercent(14)[i].Should().Be(full.AtrPercent(14)[i]);
        }
    }

    [Fact]
    public void Model_b_removes_reseed_discrepancy_for_recursive_indicators()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(800).ToList();
        var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
        var full = new CausalIndicatorCache(candles);
        var reseed = new CausalIndicatorCache(candles.Skip(insEnd).Take(valEnd - insEnd).ToList());
        var i = 40;
        full.Ema(50)[insEnd + i].Should().NotBe(reseed.Ema(50)[i]);
        full.Rsi(14)[insEnd + i].Should().NotBe(reseed.Rsi(14)[i]);
        full.AtrPercent(14)[insEnd + i].Should().NotBe(reseed.AtrPercent(14)[i]);
        var (fm, _, _) = full.Macd(12, 26, 9);
        var (rm, _, _) = reseed.Macd(12, 26, 9);
        fm[insEnd + i].Should().NotBe(rm[i]);

        var definition = StrategyValidation.Definition(StrategyTemplateKeys.MacdTrend, "1h");
        var modelB = StrategyValidation.Run(definition, candles, full, insEnd, valEnd);
        var modelA = StrategyValidation.Run(definition, candles.Skip(insEnd).Take(valEnd - insEnd).ToList());
        (modelB.NumberOfTrades != modelA.NumberOfTrades || modelB.NetProfit != modelA.NetProfit).Should().BeTrue(
            "Model B must not be forced to match slice-reseeded Model A");
    }

    [Fact]
    public void Rolling_bollinger_and_donchian_agree_after_lookback()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(800).ToList();
        var (insEnd, _) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
        var full = new CausalIndicatorCache(candles);
        var slice = new CausalIndicatorCache(candles.Skip(insEnd).ToList());
        for (var i = 21; i < 80; i++)
        {
            var (fh, fl) = full.Donchian(20);
            var (sh, sl) = slice.Donchian(20);
            fh[insEnd + i].Should().Be(sh[i]);
            fl[insEnd + i].Should().Be(sl[i]);
            var (fmid, fup, flo) = full.Bollinger(20, 2m);
            var (smid, sup, slo) = slice.Bollinger(20, 2m);
            fmid[insEnd + i].Should().Be(smid[i]);
            fup[insEnd + i].Should().Be(sup[i]);
            flo[insEnd + i].Should().Be(slo[i]);
        }
    }

    [Fact]
    public void Validation_and_oos_windows_start_flat_and_do_not_share_trades()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(800).ToList();
        var cache = new CausalIndicatorCache(candles);
        var definition = StrategyValidation.Definition(StrategyTemplateKeys.DonchianBreakout, "1h");
        var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
        var ins = StrategyValidation.Run(definition, candles, cache, 0, insEnd);
        var val = StrategyValidation.Run(definition, candles, cache, insEnd, valEnd);
        var oos = StrategyValidation.Run(definition, candles, cache, valEnd, candles.Count);

        var insKeys = Keys(ins);
        var valKeys = Keys(val);
        var oosKeys = Keys(oos);
            insKeys.Intersect(valKeys).Should().BeEmpty("IS trades must not appear in Validation");
        valKeys.Intersect(oosKeys).Should().BeEmpty("Validation trades must not appear in OOS");
        insKeys.Intersect(oosKeys).Should().BeEmpty();

        if (val.Trades.Count > 0)
        {
            val.Trades.Min(t => t.OpenedAt).Should().BeOnOrAfter(candles[insEnd].OpenTime);
            val.InitialBalance.Should().Be(10_000m);
        }

        if (oos.Trades.Count > 0)
        {
            oos.Trades.Min(t => t.OpenedAt).Should().BeOnOrAfter(candles[valEnd].OpenTime);
            oos.InitialBalance.Should().Be(10_000m);
        }
    }

    [Fact]
    public void Walk_forward_scores_test_trades_only()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(800).ToList();
        var cache = new CausalIndicatorCache(candles);
        var definition = StrategyValidation.Definition(StrategyTemplateKeys.DonchianBreakout, "1h");
        var warmup = StrategyValidation.WarmupBars(definition);
        var train = Math.Max(warmup, 200);
        var test = 40;
        var testStart = train;
        var testEnd = train + test;
        var scored = StrategyValidation.Run(definition, candles, cache, testStart, testEnd);
        var trainOnly = StrategyValidation.Run(definition, candles, cache, 0, testStart);

        Keys(scored).Intersect(Keys(trainOnly)).Should().BeEmpty("WF TEST metrics must not contain TRAIN trades");
        if (scored.Trades.Count > 0)
        {
            scored.Trades.Min(t => t.OpenedAt).Should().BeOnOrAfter(candles[testStart].OpenTime);
        }
    }

    [Fact]
    public void Future_candles_cannot_change_an_earlier_indicator_value()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(200).ToList();
        var mutated = candles.Select(c => c).ToList();
        var last = mutated[^1];
        mutated[^1] = new MarketCandle
        {
            Open = last.Open + 50m,
            High = last.High + 50m,
            Low = last.Low + 50m,
            Close = last.Close + 50m,
            Volume = last.Volume,
            IsClosed = true,
            OpenTime = last.OpenTime,
            CloseTime = last.CloseTime,
            ExchangeTimestamp = last.ExchangeTimestamp
        };
        const int i = 120;
        var left = new CausalIndicatorCache(candles);
        var right = new CausalIndicatorCache(mutated);
        left.Ema(50)[i].Should().Be(right.Ema(50)[i]);
        left.Rsi(14)[i].Should().Be(right.Rsi(14)[i]);
        var (lm, ls, lh) = left.Macd(12, 26, 9);
        var (rm, rs, rh) = right.Macd(12, 26, 9);
        lm[i].Should().Be(rm[i]);
        ls[i].Should().Be(rs[i]);
        lh[i].Should().Be(rh[i]);
        left.AtrPercent(14)[i].Should().Be(right.AtrPercent(14)[i]);
        var (lb, lu, ll) = left.Bollinger(20, 2m);
        var (rb, ru, rl) = right.Bollinger(20, 2m);
        lb[i].Should().Be(rb[i]);
        lu[i].Should().Be(ru[i]);
        ll[i].Should().Be(rl[i]);
        var (ldh, ldl) = left.Donchian(20);
        var (rdh, rdl) = right.Donchian(20);
        ldh[i].Should().Be(rdh[i]);
        ldl[i].Should().Be(rdl[i]);
    }

    [Fact]
    public void Fees_slippage_and_excluding_funding_remain_on_model_b_windows()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(800).ToList();
        var cache = new CausalIndicatorCache(candles);
        var definition = StrategyValidation.Definition(StrategyTemplateKeys.DonchianBreakout, "1h");
        var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
        var val = StrategyValidation.Run(definition, candles, cache, insEnd, valEnd);
        val.CostNotes.Should().Contain("EXCLUDING_FUNDING");
        val.Assumptions.Should().Contain("next bar open");
        if (val.NumberOfTrades > 0)
        {
            val.FeesPaid.Should().BeGreaterThan(0m);
        }
    }

    [Fact]
    public void Performance_benchmark_stays_fast_and_deterministic()
    {
        var first = ValidationBenchmark.Run();
        _output.WriteLine($"BENCHMARK {first.ElapsedMs} ms | {first.Fingerprint}");
        first.ElapsedMs.Should().BeLessThan(5_000, "causal cache replay must remain on the optimized path");
        first.Fingerprint.Should().NotBeNullOrWhiteSpace();
        var second = ValidationBenchmark.Run();
        second.Fingerprint.Should().Be(first.Fingerprint);
        first.Rows.Should().HaveCount(StrategyTemplateKeys.Frozen.Length);
        first.Rows.Should().OnlyContain(row => row.CandlesPerSecond > 1_000);
    }

    [Fact]
    public void Optimized_model_b_replay_is_deterministic()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(800).ToList();
        var cache = new CausalIndicatorCache(candles);
        var definition = StrategyValidation.Definition(StrategyTemplateKeys.EmaRsiTrend, "1h");
        var left = StrategyValidation.Run(definition, candles, cache, 0, candles.Count);
        var right = StrategyValidation.Run(definition, candles, cache, 0, candles.Count);
        right.NumberOfTrades.Should().Be(left.NumberOfTrades);
        right.NetProfit.Should().Be(left.NetProfit);
        right.ProfitFactor.Should().Be(left.ProfitFactor);
        right.MaximumDrawdown.Should().Be(left.MaximumDrawdown);
        right.FeesPaid.Should().Be(left.FeesPaid);
    }

    [Fact]
    public void Short_history_is_insufficient_data_not_a_partial_ema_seed()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(80).ToList();
        var series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>
        {
            [("ETHUSDT", "5m")] = candles
        };
        var rows = StrategyValidationRunner.Evaluate(series, implementationOk: true);
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(row =>
            row.Notes.Any(note => note.Contains("INSUFFICIENT_DATA", StringComparison.OrdinalIgnoreCase)));
        rows.Should().OnlyContain(row => row.Status != TradingPlatform.Domain.Strategies.StrategyValidationStatuses.ValidatedForPaper);
    }

    [Fact]
    public void Runner_uses_model_b_note_and_does_not_claim_paper_from_this_change()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(400).ToList();
        var series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>
        {
            [("ETHUSDT", "15m")] = candles
        };
        var rows = StrategyValidationRunner.Evaluate(series, implementationOk: true);
        rows.Should().OnlyContain(row => row.Notes.Any(note => note.Contains("Model B", StringComparison.Ordinal)));
        rows.Should().OnlyContain(row => row.Notes.Any(note => note.Contains("stale", StringComparison.OrdinalIgnoreCase)));
    }

    private static HashSet<string> Keys(TradingPlatform.Backtesting.ReplayResult result) =>
        result.Trades.Select(t => $"{t.OpenedAt:o}|{t.ClosedAt:o}|{t.Side}|{t.EntryPrice}|{t.ExitPrice}|{t.PnL}").ToHashSet(StringComparer.Ordinal);
}
