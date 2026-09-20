using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public static class AdvancedAlphaPilot
{
    private static readonly HashSet<string> SkipReplay = new(StringComparer.OrdinalIgnoreCase)
    {
        StrategyTemplateKeys.FundingBasisRv,
        StrategyTemplateKeys.FundingOiReversal,
        StrategyTemplateKeys.OiPriceVolumeRegime,
        StrategyTemplateKeys.CryptoPairsArb,
        StrategyTemplateKeys.XsRelativeStrength,
        StrategyTemplateKeys.RegimeStrategyRouter
    };

    public static IReadOnlyList<ResearchBookResult> Evaluate(
        IReadOnlyList<string> symbols,
        IReadOnlyList<string> timeframes,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        int maxParallel = 2)
    {
        var jobs = StrategyTemplateKeys.Alpha
            .SelectMany(template => symbols.SelectMany(symbol => timeframes.Select(tf => (template, symbol, tf))))
            .ToArray();
        var rows = new List<ResearchBookResult>();
        var gate = new object();
        Parallel.ForEach(
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxParallel) },
            job =>
            {
                var slice = EvaluateOne(job.template, job.symbol, job.tf, series);
                lock (gate)
                {
                    rows.AddRange(slice);
                }
            });
        return rows.OrderBy(r => r.CandidateId).ThenBy(r => r.Symbol).ThenBy(r => r.Timeframe).ThenBy(r => r.Phase).ToList();
    }

    public static string RenderMarkdown(IReadOnlyList<ResearchBookResult> books, IReadOnlyList<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Advanced alpha research report");
        sb.AppendLine();
        sb.AppendLine("Phase 1 implementation pilot only. Frozen five and advanced six were not modified. LIVE = OFF.");
        sb.AppendLine("Do **not** treat any candidate as profitable. Combined PF is cost-inclusive. Win rate is not the selection criterion.");
        sb.AppendLine("Phase 2 / 528-universe / parameter freeze / untouched OOS retune are **not** run here.");
        sb.AppendLine();
        sb.AppendLine("## Dataset inventory");
        sb.AppendLine("- Available: OHLCV, volume, reconstructed volume profile (typical-price × volume), session VWAP, ATR, EMA, RSI, ADX, Bollinger, Keltner, Donchian, causal swings.");
        sb.AppendLine("- Unavailable (not fabricated): historical open interest, funding rate, mark/index/basis, order book, liquidations, causal pair-universe snapshots.");
        sb.AppendLine("- Taker buy volume: Binance kline index 9. Existing disk cache without the field = DATA_UNAVAILABLE.");
        sb.AppendLine();
        sb.AppendLine("## Status (OOS BASE Combined)");
        foreach (var key in StrategyTemplateKeys.Alpha)
        {
            var oos = books.Where(b => b.CandidateId == key && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
            var status = CatalogStatus(key, oos);
            var totals = ResearchDiagnostics.CombinePf(oos.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var (state, pf) = ResearchPf.From(totals);
            sb.AppendLine($"- **{StrategyTemplates.DisplayName(key)}** (`{key}`): {status}; family {StrategyTemplateKeys.Family(key)}; OOS n={totals.Trades} PF {ResearchPf.Render(state, pf)}; {StrategyTemplates.DataDependencies(key)}");
        }

        sb.AppendLine();
        sb.AppendLine("## Per-strategy research notes");
        foreach (var key in StrategyTemplateKeys.Alpha)
        {
            AppendStrategy(sb, key, books);
        }

        sb.AppendLine();
        sb.AppendLine("## Cost sensitivity (OOS Combined)");
        foreach (var key in StrategyTemplateKeys.Alpha)
        {
            sb.AppendLine($"- {key}");
            foreach (var cost in new[] { ResearchCostLabels.Base, ResearchCostLabels.High, ResearchCostLabels.Stress })
            {
                var totals = ResearchDiagnostics.CombinePf(
                    books.Where(b => b.CandidateId == key && b.Phase == "OOS" && b.CostLabel == cost)
                        .Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
                sb.AppendLine($"  - {cost}: {Pf(totals)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Walk-forward");
        sb.AppendLine("TEST windows only. Empty window = NO_TRADES, not PF=0. No-loss window = NO_LOSSES, not PF=99.");
        foreach (var key in StrategyTemplateKeys.Alpha)
        {
            var wf = books.Where(b => b.CandidateId == key && b.Phase.StartsWith("WF", StringComparison.Ordinal) && b.CostLabel == ResearchCostLabels.Base).ToList();
            if (wf.Count == 0)
            {
                sb.AppendLine($"- {key}: walk-forward not evaluated (DATA_UNAVAILABLE or deferred).");
                continue;
            }

            var totals = ResearchDiagnostics.CombinePf(wf.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var empty = wf.Count(w => w.TradeCount == 0);
            sb.AppendLine($"- {key}: windows={wf.Count} empty={empty} {Pf(totals)}");
        }

        sb.AppendLine();
        sb.AppendLine("## LIVE / Risk / existing strategies");
        sb.AppendLine("- LIVE remains OFF. No AUTO_LIVE status exists.");
        sb.AppendLine("- Risk Engine was not modified. Isolated margin. No martingale.");
        sb.AppendLine("- Existing 11 strategy evaluators (frozen 5 + advanced 6) were not rewritten.");
        sb.AppendLine();
        sb.AppendLine("## Run notes");
        foreach (var note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    public static void WriteArtifacts(string dir, IReadOnlyList<ResearchBookResult> books)
    {
        Directory.CreateDirectory(dir);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        var candidates = StrategyTemplateKeys.Alpha.Select(key => new
        {
            StrategyId = key,
            Version = "1.0",
            Name = StrategyTemplates.DisplayName(key),
            Family = StrategyTemplateKeys.Family(key),
            DirectionSupport = "LONG, SHORT",
            SupportedTimeframes = StrategyTemplateKeys.SupportedTimeframes,
            RequiredData = StrategyTemplates.DataDependencies(key),
            ResearchStatus = StrategyTemplates.ResearchStatus(key),
            ValidationStatus = "RESEARCHING",
            Live = false
        }).ToList();
        File.WriteAllText(Path.Combine(dir, "advanced-alpha-candidates.json"), JsonSerializer.Serialize(candidates, opts));
        File.WriteAllText(Path.Combine(dir, "advanced-alpha-results.json"), JsonSerializer.Serialize(books, opts));
        WriteGroup(dir, "advanced-alpha-symbol-results.json", books, b => b.Symbol, opts);
        WriteGroup(dir, "advanced-alpha-timeframe-results.json", books, b => b.Timeframe, opts);
        WriteGroup(dir, "advanced-alpha-regime-results.json", books, b => b.Regime ?? "TRANSITION", opts);
        var cost = books.Where(b => b.Phase == "OOS").GroupBy(b => (b.CandidateId, b.CostLabel)).Select(g =>
        {
            var totals = ResearchDiagnostics.CombinePf(g.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var (state, pf) = ResearchPf.From(totals);
            return new { g.Key.CandidateId, g.Key.CostLabel, totals.Trades, totals.NetPnl, ProfitFactorState = state, ProfitFactor = pf, totals.Expectancy };
        });
        File.WriteAllText(Path.Combine(dir, "advanced-alpha-cost-results.json"), JsonSerializer.Serialize(cost, opts));
        var wf = books.Where(b => b.Phase.StartsWith("WF", StringComparison.Ordinal)).ToList();
        File.WriteAllText(Path.Combine(dir, "advanced-alpha-walkforward-results.json"), JsonSerializer.Serialize(wf, opts));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(dir)!, "advanced-alpha-results.json"), JsonSerializer.Serialize(books, opts));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(dir)!, "advanced-alpha-candidates.json"), JsonSerializer.Serialize(candidates, opts));
    }

    private static void WriteGroup(
        string dir,
        string name,
        IReadOnlyList<ResearchBookResult> books,
        Func<ResearchBookResult, string> key,
        JsonSerializerOptions opts)
    {
        var rows = books.Where(b => b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base)
            .GroupBy(b => (b.CandidateId, Bucket: key(b)))
            .Select(g =>
            {
                var totals = ResearchDiagnostics.CombinePf(g.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
                var (state, pf) = ResearchPf.From(totals);
                return new { g.Key.CandidateId, g.Key.Bucket, totals.Trades, totals.NetPnl, ProfitFactorState = state, ProfitFactor = pf, totals.Expectancy };
            });
        File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(rows, opts));
    }

    private static void AppendStrategy(StringBuilder sb, string key, IReadOnlyList<ResearchBookResult> books)
    {
        var oos = books.Where(b => b.CandidateId == key && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
        var comb = ResearchDiagnostics.CombinePf(oos.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
        var longT = ResearchDiagnostics.CombinePf(oos.Select(s => s.LongTotals).Where(t => t is not null).Cast<PnlTotals>());
        var shortT = ResearchDiagnostics.CombinePf(oos.Select(s => s.ShortTotals).Where(t => t is not null).Cast<PnlTotals>());
        var (state, pf) = ResearchPf.From(comb);
        sb.AppendLine($"### {StrategyTemplates.DisplayName(key)}");
        sb.AppendLine($"- Hypothesis / formula / entry / exit / stop: see engine `{key}` in `AlphaStrategyEvaluator`.");
        sb.AppendLine($"- Parameters (defaults): lookback={20}, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.");
        sb.AppendLine($"- Data: {StrategyTemplates.DataDependencies(key)}");
        sb.AppendLine($"- Status: {CatalogStatus(key, oos)}");
        sb.AppendLine($"- Combined OOS: n={comb.Trades} winRate={comb.WinRate.ToString("0.00", CultureInfo.InvariantCulture)}% exp={comb.Expectancy.ToString("0.00", CultureInfo.InvariantCulture)} PF {ResearchPf.Render(state, pf)} net={comb.NetPnl.ToString("0.00", CultureInfo.InvariantCulture)} fees={comb.Fees.ToString("0.00", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"- LONG {Pf(longT)}; SHORT {Pf(shortT)}.");
        sb.AppendLine($"- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.");
        sb.AppendLine();
    }

    private static string CatalogStatus(string key, List<ResearchBookResult> oos)
    {
        if (key == StrategyTemplateKeys.RegimeStrategyRouter)
        {
            return ResearchStatuses.Researching;
        }

        if (SkipReplay.Contains(key) || key == StrategyTemplateKeys.TakerFlowMomentum && oos.All(x => x.Status == ResearchStatuses.DataUnavailable))
        {
            return ResearchStatuses.DataUnavailable;
        }

        return oos.Select(x => x.Status).FirstOrDefault() ?? StrategyTemplates.ResearchStatus(key);
    }

    private static List<ResearchBookResult> EvaluateOne(
        string template,
        string symbol,
        string timeframe,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series)
    {
        if (template == StrategyTemplateKeys.RegimeStrategyRouter)
        {
            return [Skip(template, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.Researching, "Router deferred until independent validation. Not fit on OOS.")];
        }

        var dataStatus = ResearchDataCatalog.SkipStatus(template, new ResearchDataSnapshot());
        if (dataStatus == ResearchStatuses.DataUnavailable
            && template != StrategyTemplateKeys.TakerFlowMomentum
            && template != StrategyTemplateKeys.MtfTrendStructure)
        {
            return [Skip(template, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.DataUnavailable, "Required historical series not available; not fabricated.")];
        }

        if (SkipReplay.Contains(template))
        {
            return [Skip(template, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.DataUnavailable, "Required historical series not available; not fabricated.")];
        }

        if (!series.TryGetValue((symbol, timeframe), out var candles) || candles.Count == 0)
        {
            return [Skip(template, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, "No candles.")];
        }

        var closed = candles.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
        if (template == StrategyTemplateKeys.TakerFlowMomentum && !AlphaIndicatorSeries.HasTakerData(closed))
        {
            return [Skip(template, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.DataUnavailable, "Taker buy volume missing on this series.")];
        }

        CausalIndicatorCache? htf = null;
        if (template == StrategyTemplateKeys.MtfTrendStructure)
        {
            var htfTf = timeframe switch
            {
                "5m" => "15m",
                "15m" => "1h",
                _ => null
            };
            if (htfTf is null)
            {
                return [Skip(template, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, "1h has no higher timeframe in this pilot.")];
            }

            if (!series.TryGetValue((symbol, htfTf), out var htfBars) || htfBars.Count == 0)
            {
                return [Skip(template, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, $"HTF {htfTf} missing.")];
            }

            htf = new CausalIndicatorCache(htfBars.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList());
        }

        var definition = StrategyValidation.Definition(template, timeframe, StrategyTemplates.DefaultsFor(template, false) with
        {
            TemplateKey = template,
            AllowedSide = StrategySides.Both,
            Timeframe = timeframe
        });
        var warmup = StrategyValidation.WarmupBars(definition);
        if (closed.Count < warmup + StrategyValidation.MinimumEvaluatedBars)
        {
            return [Skip(template, symbol, timeframe, "IS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, $"{closed.Count} bars.")];
        }

        var cache = new CausalIndicatorCache(closed);
        var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(closed.Count);
        var rows = new List<ResearchBookResult>();
        foreach (var cost in new[] { ResearchCostLabels.Base, ResearchCostLabels.High, ResearchCostLabels.Stress })
        {
            foreach (var (phase, from, to) in new (string Phase, int From, int To)[]
                     {
                         ("IS", 0, insEnd),
                         ("VALIDATION", insEnd, valEnd),
                         ("OOS", valEnd, closed.Count)
                     })
            {
                if (to - Math.Max(from, warmup) < StrategyValidation.MinimumEvaluatedBars)
                {
                    rows.Add(Skip(template, symbol, timeframe, phase, cost, ResearchStatuses.InsufficientData, "window too short"));
                    continue;
                }

                var settings = ResearchRunner.CostScaledRisk(closed[Math.Max(from, 0)].OpenTime, closed[Math.Min(to, closed.Count) - 1].CloseTime, cost);
                var replay = new BacktestReplay(new StrategyEngine()).Run(definition, closed, settings, cache, Math.Max(from, warmup), to, htf);
                var seed = new ResearchBookResult(
                    template, symbol, timeframe, phase, cost, ResearchStatuses.Researching,
                    to - from, 0, 0, 0, 0, 0, "NO_TRADES",
                    null, 0, 0, 0, 0, 0,
                    null, null, null, null, null, null, null, null, null, null, null, null,
                    ResearchDiagnostics.ClassifyAtSignal(closed, Math.Min(closed.Count - 1, Math.Max(from, warmup))),
                    []);
                ResearchDiagnostics.Fill(seed, replay, closed, out var filled);
                rows.Add(filled with { Status = Assign(filled, phase) });
            }
        }

        if (ResearchCostLabels.Base is var baseCost)
        {
            var train = Math.Min(400, Math.Max(warmup, Math.Max(80, closed.Count / 3)));
            var test = Math.Min(80, Math.Max(20, closed.Count / 10));
            var span = train + test;
            var available = Math.Max(0, closed.Count - span);
            var step = available <= 0 ? test : Math.Max(test, available / 3);
            var wfIndex = 0;
            foreach (var window in StrategyValidation.WalkForwardWindows(closed.Count, train, test, step).Take(4))
            {
                var testStart = window.Start + train;
                var testEnd = window.Start + window.Length;
                if (testStart < warmup || testEnd > closed.Count)
                {
                    continue;
                }

                if (testEnd - testStart <= 0)
                {
                    rows.Add(Skip(template, symbol, timeframe, $"WF{wfIndex}", baseCost, ResearchStatuses.InsufficientData, "Empty walk-forward TEST window = NO_TRADES."));
                    wfIndex++;
                    continue;
                }

                var settings = ResearchRunner.CostScaledRisk(closed[testStart].OpenTime, closed[testEnd - 1].CloseTime, baseCost);
                var replay = new BacktestReplay(new StrategyEngine()).Run(definition, closed, settings, cache, testStart, testEnd, htf);
                var seed = new ResearchBookResult(
                    template, symbol, timeframe, $"WF{wfIndex}", baseCost, ResearchStatuses.Researching,
                    testEnd - testStart, 0, 0, 0, 0, 0, "NO_TRADES",
                    null, 0, 0, 0, 0, 0,
                    null, null, null, null, null, null, null, null, null, null, null, null,
                    ResearchDiagnostics.ClassifyAtSignal(closed, testStart),
                    ["Walk-forward TEST window only. Empty window PF is NO_TRADES, not 0."]);
                ResearchDiagnostics.Fill(seed, replay, closed, out var filled);
                rows.Add(filled with { Status = Assign(filled, $"WF{wfIndex}") });
                wfIndex++;
            }
        }

        return rows;
    }

    private static string Assign(ResearchBookResult row, string phase)
    {
        if (row.TradeCount == 0)
        {
            return ResearchStatuses.NoTrades;
        }

        if ((phase == "OOS" || phase.StartsWith("WF", StringComparison.Ordinal)) && row.ProfitFactor is { } pf && pf < 1m)
        {
            return ResearchStatuses.OosFailed;
        }

        return ResearchStatuses.Researching;
    }

    private static string Pf(PnlTotals totals)
    {
        var (state, ratio) = ResearchPf.From(totals);
        return $"PF {ResearchPf.Render(state, ratio)} n={totals.Trades} exp {totals.Expectancy.ToString("0.00", CultureInfo.InvariantCulture)} net {totals.NetPnl.ToString("0.00", CultureInfo.InvariantCulture)}";
    }

    private static ResearchBookResult Skip(
        string id,
        string symbol,
        string timeframe,
        string phase,
        string cost,
        string status,
        string note) =>
        new(
            id, symbol, timeframe, phase, cost, status,
            0, 0, 0, 0, 0, 0, "NO_TRADES",
            null, 0, 0, 0, 0, 0,
            null, null, null, null, null, null, null, null, null, null, null, null,
            "TRANSITION",
            [note]);
}
