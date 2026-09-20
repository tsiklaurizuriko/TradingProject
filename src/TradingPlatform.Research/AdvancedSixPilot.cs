using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public static class AdvancedSixPilot
{
    public static IReadOnlyList<ResearchBookResult> Evaluate(
        IReadOnlyList<string> symbols,
        IReadOnlyList<string> timeframes,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        int maxParallel = 2)
    {
        var jobs = StrategyTemplateKeys.AdvancedSix
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
        return rows;
    }

    public static string RenderMarkdown(IReadOnlyList<ResearchBookResult> books, IReadOnlyList<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Advanced six strategy pilot");
        sb.AppendLine();
        sb.AppendLine("Research templates added to the Strategy Engine. Frozen five baselines were not modified. LIVE = OFF.");
        sb.AppendLine("This pilot does **not** promote Paper or LIVE. Do not treat any PF as profitable.");
        sb.AppendLine();
        sb.AppendLine("## Status");
        foreach (var key in StrategyTemplateKeys.AdvancedSix)
        {
            var oos = books.Where(b => b.CandidateId == key && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
            var status = oos.Select(x => x.Status).FirstOrDefault() ?? ResearchStatuses.Researching;
            if (key is StrategyTemplateKeys.OiPriceMomentum or StrategyTemplateKeys.FundingOiRegime)
            {
                status = ResearchStatuses.DataUnavailable;
            }

            var totals = ResearchDiagnostics.CombinePf(oos.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var (state, pf) = ResearchPf.From(totals);
            sb.AppendLine($"- **{StrategyTemplates.DisplayName(key)}** (`{key}`): {status}; OOS n={totals.Trades} PF {ResearchPf.Render(state, pf)}; {StrategyTemplates.DataDependencies(key)}");
        }

        sb.AppendLine();
        sb.AppendLine("## LONG / SHORT (OOS BASE)");
        foreach (var key in StrategyTemplateKeys.AdvancedSix)
        {
            var slice = books.Where(b => b.CandidateId == key && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
            var longT = ResearchDiagnostics.CombinePf(slice.Select(s => s.LongTotals).Where(t => t is not null).Cast<PnlTotals>());
            var shortT = ResearchDiagnostics.CombinePf(slice.Select(s => s.ShortTotals).Where(t => t is not null).Cast<PnlTotals>());
            var comb = ResearchDiagnostics.CombinePf(slice.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            sb.AppendLine($"- {key}: Combined {Pf(comb)}; LONG {Pf(longT)}; SHORT {Pf(shortT)}.");
        }

        sb.AppendLine();
        sb.AppendLine("## Cost sensitivity (OOS)");
        foreach (var key in StrategyTemplateKeys.AdvancedSix)
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
        sb.AppendLine("## Open interest");
        sb.AppendLine("Historical OI series is **not** wired into backtest/paper/LIVE. `oi_price_momentum` = DATA_UNAVAILABLE. No OI was fabricated.");
        sb.AppendLine();
        sb.AppendLine("## Funding");
        sb.AppendLine("Historical funding series is **not** wired into backtest/paper/LIVE. `funding_oi_regime` = DATA_UNAVAILABLE. No funding was fabricated.");
        sb.AppendLine();
        sb.AppendLine("## LIVE");
        sb.AppendLine("LIVE remains OFF. Catalog research rows seed as disabled.");
        sb.AppendLine();
        sb.AppendLine("## Run notes");
        foreach (var note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    public static void WriteJson(string path, IReadOnlyList<ResearchBookResult> books)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(books, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<ResearchBookResult> EvaluateOne(
        string template,
        string symbol,
        string timeframe,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series)
    {
        var dataStatus = ResearchDataCatalog.SkipStatus(template, new ResearchDataSnapshot());
        if (dataStatus == ResearchStatuses.DataUnavailable)
        {
            return
            [
                Skip(template, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.DataUnavailable, "Required historical series not available; not fabricated.")
            ];
        }

        if (!series.TryGetValue((symbol, timeframe), out var candles) || candles.Count == 0)
        {
            return [Skip(template, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, "No candles.")];
        }

        var closed = candles.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
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
                var replay = new BacktestReplay(new StrategyEngine()).Run(definition, closed, settings, cache, Math.Max(from, warmup), to);
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

        return rows;
    }

    private static string Assign(ResearchBookResult row, string phase)
    {
        if (row.TradeCount == 0)
        {
            return ResearchStatuses.NoTrades;
        }

        if (phase == "OOS" && row.ProfitFactor is { } pf && pf < 1m)
        {
            return ResearchStatuses.OosFailed;
        }

        return ResearchStatuses.Researching;
    }

    private static string Pf(PnlTotals totals)
    {
        var (state, ratio) = ResearchPf.From(totals);
        return $"PF {ResearchPf.Render(state, ratio)} n={totals.Trades} exp {totals.Expectancy.ToString("0.00", CultureInfo.InvariantCulture)}";
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
