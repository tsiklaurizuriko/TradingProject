using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;

namespace TradingPlatform.Research;

public static class ResearchReport
{
    public static string RenderMarkdown(
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> books,
        IReadOnlyList<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Strategy research report");
        sb.AppendLine();
        sb.AppendLine("Research layer above Model B. Frozen catalog templates were not modified. LIVE = OFF.");
        sb.AppendLine("OOS is diagnostic only. Candidates are **not** frozen from this run.");
        sb.AppendLine("Do not treat this as a LIVE recommendation. No candidate is marked BEST, GUARANTEED, or PROFITABLE.");
        sb.AppendLine();
        sb.AppendLine("## 1. Executive summary");
        var oosBase = books.Where(b => b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
        var phase2 = new List<string>();
        var rejected = new List<(string Id, string Why)>();
        foreach (var candidate in candidates)
        {
            var robustness = ResearchDiagnostics.Summarize(candidate.CandidateId, books);
            var slice = oosBase.Where(b => b.CandidateId == candidate.CandidateId).ToList();
            var totals = ResearchDiagnostics.CombinePf(slice.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var (state, pf) = ResearchPf.From(totals);
            var why = RejectReason(candidate, robustness, totals, state, pf, slice.Count);
            if (candidate.SupportedTimeframes.Count > 0)
            {
                phase2.Add(candidate.CandidateId);
            }

            rejected.Add((candidate.CandidateId, why ?? "no Paper promotion on this research run"));
        }

        var oosPfCleared = oosBase.GroupBy(b => b.CandidateId).Any(g =>
        {
            var totals = ResearchDiagnostics.CombinePf(g.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var (state, pf) = ResearchPf.From(totals);
            return totals.Trades >= 20 && totals.Expectancy > 0m && pf is > 1m;
        });
        sb.AppendLine(oosPfCleared
            ? "Some candidates printed OOS PF > 1 on this sample. That is **not** a Paper/LIVE promotion."
            : "**No robust edge identified in the tested candidate set** on this run.");
        sb.AppendLine();
        sb.AppendLine("Paper remains OFF. LIVE remains OFF. Phase 3 (full 528 × 3) is not started.");
        sb.AppendLine();
        sb.AppendLine($"Candidates evaluated: {candidates.Count}. Books: {books.Count}. CodeVersion: {ResearchCandidate.EngineVersion}.");
        sb.AppendLine();
        sb.AppendLine("## 2. Baseline strategies");
        sb.AppendLine();
        sb.AppendLine("| Template | Model B Combined PF | Status |");
        sb.AppendLine("|---|---:|---|");
        sb.AppendLine("| Bollinger Reversion | 0.585 | VALIDATION_PENDING (immutable baseline) |");
        sb.AppendLine("| Donchian Breakout | 0.705 | VALIDATION_PENDING (immutable baseline) |");
        sb.AppendLine("| EMA RSI Trend | 0.754 | VALIDATION_PENDING (immutable baseline) |");
        sb.AppendLine("| MACD Trend | 0.695 | VALIDATION_PENDING (immutable baseline) |");
        sb.AppendLine("| RSI Pullback | 0.615 | VALIDATION_PENDING (immutable baseline) |");
        sb.AppendLine();
        sb.AppendLine("These frozen defaults did not demonstrate a cost-inclusive historical edge on 528 × 3. Research candidates are versioned separately.");
        sb.AppendLine();
        sb.AppendLine("## 3. Research hypotheses");
        foreach (var candidate in candidates)
        {
            sb.AppendLine($"- **{candidate.CandidateId}**: {candidate.Hypothesis}");
        }

        sb.AppendLine();
        sb.AppendLine("## 4. Candidate definitions");
        foreach (var candidate in candidates)
        {
            sb.AppendLine();
            sb.AppendLine($"### {candidate.CandidateId}");
            sb.AppendLine($"- Parent: `{candidate.ParentStrategyId}` v{candidate.CandidateVersion}");
            sb.AppendLine($"- Kind: {candidate.Kind}");
            sb.AppendLine($"- Indicators: {string.Join(", ", candidate.Indicators)}");
            sb.AppendLine($"- Entry: {candidate.EntryRules}");
            sb.AppendLine($"- Exit: {candidate.ExitRules}");
            sb.AppendLine($"- Directions: {string.Join(", ", candidate.SupportedDirections)}");
            sb.AppendLine($"- Timeframes: {string.Join(", ", candidate.SupportedTimeframes)}");
            sb.AppendLine($"- Created: {candidate.CreatedAtUtc:yyyy-MM-dd} UTC");
            sb.AppendLine($"- CodeVersion: {candidate.CodeVersion}");
            sb.AppendLine($"- Dataset scope: {candidate.DatasetScope}");
        }

        AppendPhase(sb, "5. IS results", books, "IS");
        AppendPhase(sb, "6. Validation results", books, "VALIDATION");
        AppendPhase(sb, "7. OOS results", books, "OOS");
        AppendWalk(sb, books);
        sb.AppendLine();
        sb.AppendLine("## 9. Symbol robustness");
        foreach (var candidate in candidates)
        {
            var r = ResearchDiagnostics.Summarize(candidate.CandidateId, books);
            sb.AppendLine($"- {candidate.CandidateId}: symbols {r.SymbolsTested}, PF>1 {r.SymbolsPfAboveOne}, PF<1 {r.SymbolsPfBelowOne}, median PF {Fmt(r.MedianSymbolPf)}, mean PF {Fmt(r.MeanSymbolPf)}, worst decile {Fmt(r.WorstDecilePf)}, best decile {Fmt(r.BestDecilePf)}, top-2 |net| share {r.TopTwoSymbolNetShare:0.0%}. {string.Join(" ", r.Notes)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 10. Timeframe robustness");
        foreach (var candidate in candidates)
        {
            var r = ResearchDiagnostics.Summarize(candidate.CandidateId, books);
            sb.AppendLine($"- {candidate.CandidateId}: {string.Join("; ", r.TimeframePf.Select(kv => kv.Key + " PF " + kv.Value))}");
        }

        sb.AppendLine();
        sb.AppendLine("## 11. LONG / SHORT");
        foreach (var candidate in candidates)
        {
            var slice = oosBase.Where(b => b.CandidateId == candidate.CandidateId).ToList();
            var longT = ResearchDiagnostics.CombinePf(slice.Select(s => s.LongTotals).Where(t => t is not null).Cast<PnlTotals>());
            var shortT = ResearchDiagnostics.CombinePf(slice.Select(s => s.ShortTotals).Where(t => t is not null).Cast<PnlTotals>());
            var comb = ResearchDiagnostics.CombinePf(slice.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            sb.AppendLine($"- {candidate.CandidateId}: Combined {PfLine(comb)}; LONG {PfLine(longT)}; SHORT {PfLine(shortT)}. Same W/|L| operator. Not an average of book PFs.");
        }

        sb.AppendLine();
        sb.AppendLine("## 12. Regime analysis");
        foreach (var grp in books.Where(b => b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).GroupBy(b => (b.CandidateId, b.Regime)))
        {
            var totals = ResearchDiagnostics.CombinePf(grp.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var label = totals.Trades < ResearchRunner.MinimumRegimeTrades ? "INSUFFICIENT_SAMPLE" : ResearchPf.Render(ResearchPf.From(totals).State, ResearchPf.From(totals).Ratio);
            sb.AppendLine($"- {grp.Key.CandidateId} {grp.Key.Regime}: n={totals.Trades} PF {label} net {totals.NetPnl:0.00}");
        }

        sb.AppendLine();
        sb.AppendLine("## 13. Cost sensitivity");
        foreach (var candidate in candidates)
        {
            var r = ResearchDiagnostics.Summarize(candidate.CandidateId, books);
            sb.AppendLine($"- {candidate.CandidateId}: {(r.CostFragile ? "COST_FRAGILE" : "BASE vs HIGH compared on OOS W/|L|")}");
            foreach (var cost in new[] { ResearchCostLabels.Base, ResearchCostLabels.High, ResearchCostLabels.Stress })
            {
                var slice = books.Where(b => b.CandidateId == candidate.CandidateId && b.Phase == "OOS" && b.CostLabel == cost).ToList();
                var totals = ResearchDiagnostics.CombinePf(slice.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
                sb.AppendLine($"  - {cost}: {PfLine(totals)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 14. Parameter stability");
        sb.AppendLine("Each candidate uses its primary frozen parameters. Neighbor grids (Donchian 20/30/55) are stored on the candidate and must be run as new versions if changed. A single-point spike would be flagged PARAMETER_FRAGILE; this run does not search for a max-PF parameter.");
        sb.AppendLine();
        sb.AppendLine("## 15. Trade frequency");
        foreach (var candidate in candidates)
        {
            var slice = oosBase.Where(b => b.CandidateId == candidate.CandidateId).ToList();
            var trades = slice.Sum(s => s.TradeCount);
            var hold = slice.Select(s => s.MedianHoldingMinutes).Where(v => v is not null).Select(v => v!.Value).ToList();
            sb.AppendLine($"- {candidate.CandidateId}: OOS trades {trades}, median hold {(hold.Count == 0 ? "N/A" : hold.Average().ToString("0.0", CultureInfo.InvariantCulture) + " min")}");
        }

        sb.AppendLine();
        sb.AppendLine("## 16. MFE/MAE diagnostics");
        sb.AppendLine("Post-signal only. Not used to create entries.");
        foreach (var candidate in candidates)
        {
            var slice = oosBase.Where(b => b.CandidateId == candidate.CandidateId).ToList();
            sb.AppendLine($"- {candidate.CandidateId}: MFE {Avg(slice.Select(s => s.MfeMean))} MAE {Avg(slice.Select(s => s.MaeMean))} r1 {Avg(slice.Select(s => s.Return1))} r3 {Avg(slice.Select(s => s.Return3))} r5 {Avg(slice.Select(s => s.Return5))} r10 {Avg(slice.Select(s => s.Return10))}");
        }

        sb.AppendLine();
        sb.AppendLine("## 17. Exit research");
        sb.AppendLine("Exit family: strategy opposite-signal plus Isolated book fixed SL/TP (unchanged Risk Engine). ATR trailing / extra TP grids are remaining work. Do not put sizing inside strategies.");
        sb.AppendLine();
        sb.AppendLine("## 18. Failure analysis");
        foreach (var (id, why) in rejected)
        {
            sb.AppendLine($"- {id}: {why}");
        }

        sb.AppendLine();
        sb.AppendLine("## 19. Candidates eligible for Paper");
        sb.AppendLine("None. Paper requires human approval after a later freeze + untouched OOS + walk-forward. LIVE remains OFF.");
        sb.AppendLine();
        sb.AppendLine("## 20. Candidates rejected / Phase 2");
        sb.AppendLine("Rejected for Paper: all 15 (this run does not promote VALIDATED_FOR_PAPER).");
        sb.AppendLine("Rejected as broken: none.");
        sb.AppendLine("Still hypotheses (not Paper): " + string.Join(", ", phase2) + ".");
        foreach (var (id, why) in rejected)
        {
            sb.AppendLine($"- {id}: {why}");
        }

        sb.AppendLine();
        sb.AppendLine("## 21. Remaining research questions");
        sb.AppendLine("- After this representative liquid-coin run, does any candidate still deserve a later freeze + untouched OOS (not a full 528 until asked)?");
        sb.AppendLine("- ATR-based SL/TP versus book percents: is there a stable region, not a 1.73 ATR spike?");
        sb.AppendLine("- Signal correlation / overlap for a future multi-coin book (diagnostics only).");
        sb.AppendLine("- Neighbor parameter dispersion on Donchian 20/30/55 after Phase 2, without touching OOS.");
        sb.AppendLine();
        sb.AppendLine("## Run notes");
        foreach (var note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    public static void WriteJson(string dir, IReadOnlyList<ResearchCandidate> candidates, IReadOnlyList<ResearchBookResult> books)
    {
        Directory.CreateDirectory(dir);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(dir, "candidate-summary.json"), JsonSerializer.Serialize(candidates, opts));
        File.WriteAllText(Path.Combine(dir, "candidate-results.json"), JsonSerializer.Serialize(books, opts));
        var regimes = books
            .Where(b => b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base)
            .GroupBy(b => new { b.CandidateId, b.Regime })
            .Select(g => new
            {
                g.Key.CandidateId,
                g.Key.Regime,
                Totals = ResearchDiagnostics.CombinePf(g.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>())
            });
        File.WriteAllText(Path.Combine(dir, "regime-results.json"), JsonSerializer.Serialize(regimes, opts));
        var tfs = books
            .Where(b => b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base)
            .GroupBy(b => new { b.CandidateId, b.Timeframe })
            .Select(g => new
            {
                g.Key.CandidateId,
                g.Key.Timeframe,
                Totals = ResearchDiagnostics.CombinePf(g.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>())
            });
        File.WriteAllText(Path.Combine(dir, "timeframe-results.json"), JsonSerializer.Serialize(tfs, opts));
        var symbols = books
            .Where(b => b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base)
            .GroupBy(b => new { b.CandidateId, b.Symbol })
            .Select(g => new
            {
                g.Key.CandidateId,
                g.Key.Symbol,
                Totals = ResearchDiagnostics.CombinePf(g.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>())
            });
        File.WriteAllText(Path.Combine(dir, "symbol-results.json"), JsonSerializer.Serialize(symbols, opts));
        var wf = books.Where(b => b.Phase.StartsWith("WF", StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(Path.Combine(dir, "walk-forward-results.json"), JsonSerializer.Serialize(wf, opts));
        var cost = books.Where(b => b.Phase == "OOS");
        File.WriteAllText(Path.Combine(dir, "cost-sensitivity-results.json"), JsonSerializer.Serialize(cost, opts));
    }

    private static void AppendPhase(StringBuilder sb, string title, IReadOnlyList<ResearchBookResult> books, string phase)
    {
        sb.AppendLine();
        sb.AppendLine($"## {title}");
        foreach (var grp in books.Where(b => b.Phase == phase && b.CostLabel == ResearchCostLabels.Base).GroupBy(b => b.CandidateId))
        {
            var totals = ResearchDiagnostics.CombinePf(grp.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            sb.AppendLine($"- {grp.Key}: n={totals.Trades} {PfLine(totals)} net {totals.NetPnl:0.00} fees {totals.Fees:0.00} wr {totals.WinRate:0.00}% exp {totals.Expectancy:0.00}");
        }
    }

    private static void AppendWalk(StringBuilder sb, IReadOnlyList<ResearchBookResult> books)
    {
        sb.AppendLine();
        sb.AppendLine("## 8. Walk-forward results");
        foreach (var grp in books.Where(b => b.Phase.StartsWith("WF", StringComparison.OrdinalIgnoreCase) && b.CostLabel == ResearchCostLabels.Base).GroupBy(b => b.CandidateId))
        {
            var windows = grp.ToList();
            var withTrades = windows.Where(w => w.TradeCount > 0).ToList();
            var finite = withTrades.Where(w => w.ProfitFactorState == "NORMAL" && w.ProfitFactor is not null).Select(w => w.ProfitFactor!.Value).OrderBy(x => x).ToList();
            var net = windows.Sum(w => w.NetPnl);
            var pos = withTrades.Count == 0 ? 0m : (decimal)withTrades.Count(w => w.NetPnl > 0m) / withTrades.Count;
            sb.AppendLine($"- {grp.Key}: windows {windows.Count}, with trades {withTrades.Count}, empty {windows.Count - withTrades.Count} (PF=N/A), finite median PF {(finite.Count == 0 ? "N/A" : finite[finite.Count / 2].ToString("0.00000000"))}, test net {net:0.00}, positive-window {pos:0.0%}. NO_LOSSES excluded from median.");
        }
    }

    private static string? RejectReason(
        ResearchCandidate candidate,
        ResearchRobustness robustness,
        PnlTotals oos,
        string state,
        decimal? pf,
        int books)
    {
        if (books == 0 || oos.Trades < 10)
        {
            return "insufficient OOS sample on this run";
        }

        if (state == "NO_TRADES")
        {
            return "NO_TRADES on OOS";
        }

        if (oos.Expectancy <= 0m || pf is null || pf <= 1m)
        {
            return "OOS expectancy/PF did not clear 1 after costs";
        }

        if (robustness.TopTwoSymbolNetShare >= 0.80m && robustness.SymbolsTested >= 3)
        {
            return "SYMBOL_FRAGILE: PnL concentrated in two coins";
        }

        if (robustness.CostFragile)
        {
            return "COST_FRAGILE";
        }

        return "this run does not promote VALIDATED_FOR_PAPER; remains a hypothesis only";
    }

    private static string PfLine(PnlTotals totals)
    {
        var (state, ratio) = ResearchPf.From(totals);
        return $"PF {ResearchPf.Render(state, ratio)} (+{totals.PositivePnlSum:0.00}/|{totals.AbsoluteNegativePnlSum:0.00}|)";
    }

    private static string Fmt(decimal? value) => value is { } v ? v.ToString("0.000") : "N/A";

    private static string Avg(IEnumerable<decimal?> values)
    {
        var rows = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return rows.Count == 0 ? "N/A" : rows.Average().ToString("0.0000");
    }
}
