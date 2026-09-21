using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;

namespace TradingPlatform.Research;

public static class Wave2Report
{
    public static string RenderMarkdown(
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> books,
        IReadOnlyList<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Model B Wave-2 strategy research report");
        sb.AppendLine();
        sb.AppendLine("Research screen only. Frozen five templates, FrozenRisk, LIVE, and Isolated LOW catalog numbers were not changed.");
        sb.AppendLine("Replay uses Isolated LOW book sizing: **$1,000**, **0.5% risk**, **3x**, daily 3%, max 2 positions, consecutive-loss 5, cooldown 30.");
        sb.AppendLine("When HonorSuggestedStops is on, stop distance sizes the Isolated quantity so dollar risk stays 0.5% (leverage-capped). Cross margin is not used.");
        sb.AppendLine("Parameters were pre-registered. OOS was not used to retune. Full 528-coin × 3-timeframe (1,584-book) run is **not** launched from this screen.");
        sb.AppendLine();
        sb.AppendLine("## 1. Executive Summary");
        sb.AppendLine();
        sb.AppendLine("Prior Model B work (frozen five, 15 filter/native candidates, advanced alpha families, funding Phase 4) did not find a robust edge. Wave-2 tests **new mechanisms** plus **structural exits that match the entry**, which the existing 2%/4% book always overrode.");
        sb.AppendLine();
        var classifications = candidates.ToDictionary(c => c.CandidateId, c => Classify(c, books), StringComparer.OrdinalIgnoreCase);
        var robust = classifications.Count(kv => kv.Value == "ROBUST CANDIDATE");
        var promising = classifications.Count(kv => kv.Value == "PROMISING");
        var fragile = classifications.Count(kv => kv.Value == "FRAGILE");
        var rejected = classifications.Count(kv => kv.Value == "REJECTED");
        sb.AppendLine($"Classifications: ROBUST CANDIDATE={robust}, PROMISING={promising}, FRAGILE={fragile}, REJECTED={rejected}.");
        if (robust == 0)
        {
            sb.AppendLine();
            sb.AppendLine("**NO ROBUST ALPHA FOUND**");
            sb.AppendLine();
            sb.AppendLine("No Wave-2 candidate cleared the research gates (PF>1 overall and OOS, positive OOS expectancy, multi-coin/TF, cost robustness, walk-forward stability) without using OOS as a training set.");
        }

        sb.AppendLine();
        sb.AppendLine("## 2. Candidate Table");
        sb.AppendLine();
        sb.AppendLine("| Strategy | Hypothesis | n | WR | PF | Exp | Mean book | Median book | OOS PF | OOS Exp | OOS WR | DD | Profitable books % | Class |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var candidate in candidates)
        {
            var row = Snapshot(candidate.CandidateId, books);
            sb.AppendLine(
                $"| {candidate.CandidateId} | {Escape(Trim(candidate.Hypothesis, 72))} | {row.Trades} | {Wr(row.WinRate)} | {Pf(row.PfState, row.Pf)} | {Dec(row.Expectancy)} | {Pct(row.MeanBookReturn)} | {Pct(row.MedianBookReturn)} | {Pf(row.OosPfState, row.OosPf)} | {Dec(row.OosExpectancy)} | {Wr(row.OosWinRate)} | n/a | {Pct(row.ProfitableBooks)} | {classifications[candidate.CandidateId]} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 3. Robustness Analysis");
        sb.AppendLine();
        foreach (var candidate in candidates)
        {
            var id = candidate.CandidateId;
            sb.AppendLine($"### {id}");
            sb.AppendLine($"- Hypothesis: {candidate.Hypothesis}");
            sb.AppendLine($"- Entry: {candidate.EntryRules}");
            sb.AppendLine($"- Exit: {candidate.ExitRules}");
            sb.AppendLine($"- Classification: **{classifications[id]}**");
            AppendTimeframes(sb, id, books);
            AppendCoins(sb, id, books);
            AppendCosts(sb, id, books);
            AppendWalkForward(sb, id, books);
            sb.AppendLine();
        }

        sb.AppendLine("## 4. Walk-Forward Results");
        sb.AppendLine();
        sb.AppendLine("Walk-forward rows are TEST windows only (parameters frozen). Empty windows are NO_TRADES, not PF=0.");
        sb.AppendLine();
        foreach (var candidate in candidates)
        {
            var wf = books.Where(b => b.CandidateId == candidate.CandidateId && b.Phase.StartsWith("WF", StringComparison.OrdinalIgnoreCase) && b.CostLabel == ResearchCostLabels.Base).ToList();
            if (wf.Count == 0)
            {
                sb.AppendLine($"- **{candidate.CandidateId}**: walk-forward windows were not produced in this screen.");
                continue;
            }

            var totals = ResearchDiagnostics.CombinePf(wf.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var (state, pf) = ResearchPf.From(totals);
            sb.AppendLine($"- **{candidate.CandidateId}**: WF test n={totals.Trades} PF {ResearchPf.Render(state, pf)} across {wf.Count} windows.");
        }

        sb.AppendLine();
        sb.AppendLine("## 5. Failure Analysis");
        sb.AppendLine();
        sb.AppendLine("Existing catalog strategies fail under Model B because mean-reversion is forced into a 2%/4% Isolated book (wrong target), trend systems have WR too low for 2R after costs, and SHORT Combined PF is below 1 on every previously runnable advanced family.");
        sb.AppendLine("Wave-2 rejects a candidate when IS or VALIDATION PF ≤ 1, OOS expectancy ≤ 0, a single coin dominates net PnL, costs at +100% flip PF below 1, or walk-forward test windows are unstable.");
        sb.AppendLine();
        foreach (var candidate in candidates)
        {
            sb.AppendLine($"- **{candidate.CandidateId}** ({classifications[candidate.CandidateId]}): {FailureNote(candidate.CandidateId, books, classifications[candidate.CandidateId])}");
        }

        sb.AppendLine();
        sb.AppendLine("## 6. Best Candidate Architecture");
        sb.AppendLine();
        var best = classifications
            .OrderBy(kv => Rank(kv.Value))
            .ThenByDescending(kv => Snapshot(kv.Key, books).OosPf ?? 0m)
            .First();
        var bestCandidate = candidates.First(c => c.CandidateId == best.Key);
        sb.AppendLine($"Strongest label in this screen: **{bestCandidate.CandidateId}** ({best.Value}).");
        sb.AppendLine();
        sb.AppendLine($"- Entry rules: {bestCandidate.EntryRules}");
        sb.AppendLine($"- Exit rules: {bestCandidate.ExitRules}");
        sb.AppendLine($"- Features: {string.Join(", ", bestCandidate.Indicators)}");
        sb.AppendLine("- Parameters (pre-registered, not OOS-fit): swing n=3, Donchian=20, ATR=14, ATR-percentile lookback=50, rel-vol lookback=20, compression≤0.25, low-vol≤0.35, high-vol≥0.65, exhaustion rel-vol≥2 / body≤0.35, VWAP extension 0.75 ATR, displacement 1.5 ATR / 6 bars, BOS lookback 8, min stop 0.20% of fill, 2R when TP omitted.");
        sb.AppendLine("- Regime filter: per-candidate (see entry). No future regime labels.");
        sb.AppendLine("- Position sizing: Isolated LOW Risk Engine. 0.5% of $1,000 = $5 planned loss at the structural (or 2%) stop. 3x cap. Daily 3% halt on new entries.");
        sb.AppendLine("- Risk logic: Isolated, one position per coin, books not sharing margin. FrozenRisk ($10k/1%/5x) was not used for this screen.");
        if (best.Value != "ROBUST CANDIDATE")
        {
            sb.AppendLine();
            sb.AppendLine("This is the least-bad screen result, not a production promotion. Do not enable LIVE. Do not mark VALIDATED_FOR_PAPER.");
        }

        sb.AppendLine();
        sb.AppendLine("## 7. Final Classification");
        sb.AppendLine();
        foreach (var candidate in candidates)
        {
            sb.AppendLine($"- `{candidate.CandidateId}`: **{classifications[candidate.CandidateId]}**");
        }

        sb.AppendLine();
        sb.AppendLine("## Framework audit (Wave-2)");
        sb.AppendLine();
        sb.AppendLine("- Model B timing: signal on closed candle t, fill next open T+1, SL before TP on the same bar, fees 0.04% + slippage 0.02% at BASE.");
        sb.AppendLine("- IS / VAL / OOS: chronological 60 / 20 / 20 of closed bars. OOS is reported, not used to change parameters.");
        sb.AppendLine("- Indicators: CausalIndicatorCache / confirmed swings (published at k+n). Donchian at i uses [i-n, i). No future close/volume.");
        sb.AppendLine("- Holdout limitation: OOS is computed in the same process after IS/VAL. It was not a never-inspected vault. Parameters were not edited after OOS.");
        sb.AppendLine("- Cross-section, pairs, historical OI, and predicted funding remain DATA_UNAVAILABLE in single-book replay and were not fabricated.");
        sb.AppendLine();
        sb.AppendLine("## Run notes");
        foreach (var note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    public static void WriteArtifacts(string directory, IReadOnlyList<ResearchBookResult> books)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "books.json"),
            JsonSerializer.Serialize(books, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string Classify(ResearchCandidate candidate, IReadOnlyList<ResearchBookResult> books)
    {
        var snap = Snapshot(candidate.CandidateId, books);
        if (snap.Trades < 20)
        {
            return "REJECTED";
        }

        var isOk = PhasePf(candidate.CandidateId, books, "IS") is > 1m;
        var valOk = PhasePf(candidate.CandidateId, books, "VALIDATION") is > 1m;
        if (!isOk || !valOk)
        {
            return "REJECTED";
        }

        var oosPf = snap.OosPf;
        var oosExp = snap.OosExpectancy;
        if (oosPf is null || oosPf <= 1m || oosExp <= 0m)
        {
            return "FRAGILE";
        }

        var stress = PhasePf(candidate.CandidateId, books, "OOS", ResearchCostLabels.Stress);
        if (stress is not null && stress < 1m)
        {
            return "FRAGILE";
        }

        if (snap.TopCoinShare > 0.50m)
        {
            return "FRAGILE";
        }

        var tfs = books
            .Where(b => b.CandidateId == candidate.CandidateId && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base)
            .GroupBy(b => b.Timeframe)
            .Select(g => ResearchPf.From(ResearchDiagnostics.CombinePf(g.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>())).Ratio)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();
        if (tfs.Count >= 2 && tfs.Count(v => v > 1m) < 2)
        {
            return "PROMISING";
        }

        if (snap.MedianBookReturn <= 0m && snap.ProfitableBooks < 0.50m)
        {
            return "PROMISING";
        }

        var wf = books.Where(b => b.CandidateId == candidate.CandidateId && b.Phase.StartsWith("WF", StringComparison.OrdinalIgnoreCase) && b.CostLabel == ResearchCostLabels.Base).ToList();
        if (wf.Count == 0)
        {
            return "PROMISING";
        }

        var wfPf = ResearchPf.From(ResearchDiagnostics.CombinePf(wf.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>())).Ratio;
        if (wfPf is null || wfPf <= 1m)
        {
            return "PROMISING";
        }

        return "ROBUST CANDIDATE";
    }

    private static decimal? PhasePf(string id, IReadOnlyList<ResearchBookResult> books, string phase, string cost = ResearchCostLabels.Base)
    {
        var rows = books.Where(b => b.CandidateId == id && b.Phase == phase && b.CostLabel == cost).ToList();
        var totals = ResearchDiagnostics.CombinePf(rows.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
        return ResearchPf.From(totals).Ratio;
    }

    private static SnapshotRow Snapshot(string id, IReadOnlyList<ResearchBookResult> books)
    {
        var baseAll = books.Where(b => b.CandidateId == id && b.CostLabel == ResearchCostLabels.Base && (b.Phase is "IS" or "VALIDATION" or "OOS")).ToList();
        var oos = books.Where(b => b.CandidateId == id && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
        var allTotals = ResearchDiagnostics.CombinePf(baseAll.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
        var oosTotals = ResearchDiagnostics.CombinePf(oos.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
        var (state, pf) = ResearchPf.From(allTotals);
        var (oosState, oosPf) = ResearchPf.From(oosTotals);
        var bookRets = baseAll.Select(b => b.NetPnl / 1000m).OrderBy(x => x).ToList();
        var profitable = baseAll.Count == 0 ? 0m : baseAll.Count(b => b.NetPnl > 0m) / (decimal)baseAll.Count;
        var coinPnL = baseAll.GroupBy(b => b.Symbol).Select(g => g.Sum(x => x.NetPnl)).OrderByDescending(x => x).ToList();
        var abs = coinPnL.Sum(x => Math.Abs(x));
        var topShare = abs <= 0m || coinPnL.Count == 0 ? 0m : Math.Abs(coinPnL[0]) / abs;
        return new SnapshotRow(
            allTotals.Trades,
            allTotals.WinRate,
            state,
            pf,
            allTotals.Expectancy,
            bookRets.Count == 0 ? 0m : bookRets.Average(),
            Median(bookRets),
            oosState,
            oosPf,
            oosTotals.Expectancy,
            oosTotals.WinRate,
            0m,
            profitable,
            topShare);
    }

    private static void AppendTimeframes(StringBuilder sb, string id, IReadOnlyList<ResearchBookResult> books)
    {
        var oos = books.Where(b => b.CandidateId == id && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
        foreach (var g in oos.GroupBy(b => b.Timeframe).OrderBy(g => g.Key))
        {
            var totals = ResearchDiagnostics.CombinePf(g.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var (state, pf) = ResearchPf.From(totals);
            sb.AppendLine($"- Timeframe {g.Key} OOS: n={totals.Trades} PF {ResearchPf.Render(state, pf)} WR {Wr(totals.WinRate)}");
        }
    }

    private static void AppendCoins(StringBuilder sb, string id, IReadOnlyList<ResearchBookResult> books)
    {
        var oos = books.Where(b => b.CandidateId == id && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
        var byCoin = oos.GroupBy(b => b.Symbol).Select(g => (g.Key, Net: g.Sum(x => x.NetPnl), n: g.Sum(x => x.TradeCount))).OrderByDescending(x => x.Net).ToList();
        if (byCoin.Count == 0)
        {
            sb.AppendLine("- Coins: no OOS BASE rows.");
            return;
        }

        sb.AppendLine($"- Coin PnL (OOS BASE): best {byCoin[0].Key} {Dec(byCoin[0].Net)}, worst {byCoin[^1].Key} {Dec(byCoin[^1].Net)}, coins with trades {byCoin.Count(x => x.n > 0)}.");
    }

    private static void AppendCosts(StringBuilder sb, string id, IReadOnlyList<ResearchBookResult> books)
    {
        foreach (var cost in new[] { ResearchCostLabels.Base, ResearchCostLabels.Mild, ResearchCostLabels.High, ResearchCostLabels.Stress })
        {
            var pf = PhasePf(id, books, "OOS", cost);
            if (pf is null && !books.Any(b => b.CandidateId == id && b.CostLabel == cost))
            {
                continue;
            }

            sb.AppendLine($"- Cost {cost} OOS PF: {(pf is null ? "N/A" : pf.Value.ToString("0.000", CultureInfo.InvariantCulture))}");
        }
    }

    private static void AppendWalkForward(StringBuilder sb, string id, IReadOnlyList<ResearchBookResult> books)
    {
        var wf = books.Where(b => b.CandidateId == id && b.Phase.StartsWith("WF", StringComparison.OrdinalIgnoreCase) && b.CostLabel == ResearchCostLabels.Base)
            .OrderBy(b => b.Phase)
            .ThenBy(b => b.Symbol)
            .ThenBy(b => b.Timeframe)
            .ToList();
        if (wf.Count == 0)
        {
            return;
        }

        foreach (var row in wf.Take(24))
        {
            sb.AppendLine($"- {row.Phase} {row.Symbol} {row.Timeframe}: n={row.TradeCount} PF {ResearchPf.Render(row.ProfitFactorState, row.ProfitFactor)}");
        }
    }

    private static string FailureNote(string id, IReadOnlyList<ResearchBookResult> books, string cls)
    {
        var snap = Snapshot(id, books);
        if (cls == "REJECTED" && snap.Trades < 20)
        {
            return "Too few BASE IS/VAL/OOS trades for a stable sample.";
        }

        if (cls == "REJECTED")
        {
            return "In-sample or validation Combined PF was not above 1 after costs. Not promoted.";
        }

        if (cls == "FRAGILE")
        {
            return "IS/VAL passed a weak gate but OOS PF/expectancy, cost stress, or coin concentration failed.";
        }

        if (cls == "PROMISING")
        {
            return "OOS Combined PF > 1 on the screen universe, but timeframe breadth, book-return median, or walk-forward is not robust.";
        }

        return "Passed screen gates on this limited universe. Still not a 1,584-book proof.";
    }

    private static int Rank(string cls) => cls switch
    {
        "ROBUST CANDIDATE" => 0,
        "PROMISING" => 1,
        "FRAGILE" => 2,
        _ => 3
    };

    private static decimal Median(IReadOnlyList<decimal> ordered)
    {
        if (ordered.Count == 0)
        {
            return 0m;
        }

        var mid = ordered.Count / 2;
        return ordered.Count % 2 == 0 ? (ordered[mid - 1] + ordered[mid]) / 2m : ordered[mid];
    }

    private static string Pf(string state, decimal? ratio) => ResearchPf.Render(state, ratio);

    private static string Dec(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Pct(decimal value) => value.ToString("0.00%", CultureInfo.InvariantCulture);

    private static string Wr(decimal winRate0To100) => (winRate0To100 / 100m).ToString("0.00%", CultureInfo.InvariantCulture);

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string Escape(string text) => text.Replace("|", "/", StringComparison.Ordinal);

    private sealed record SnapshotRow(
        int Trades,
        decimal WinRate,
        string PfState,
        decimal? Pf,
        decimal Expectancy,
        decimal MeanBookReturn,
        decimal MedianBookReturn,
        string OosPfState,
        decimal? OosPf,
        decimal OosExpectancy,
        decimal OosWinRate,
        decimal MaxDd,
        decimal ProfitableBooks,
        decimal TopCoinShare);
}
