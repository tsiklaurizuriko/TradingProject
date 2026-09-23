using System.Globalization;
using System.Text;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Research;

public static class ScalpingReport
{
    public const string Confirmation =
        "LIVE = OFF. Scalping LIVE = OFF. Isolated = enforced. Risk Engine = authoritative. No VALIDATED_FOR_PAPER.";

    public static string Render(
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> books,
        IReadOnlyList<ScalpingCoverageRow> coverage,
        OccupancyReplayResult? occupancy,
        IReadOnlyList<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SCALPING RESEARCH REPORT");
        sb.AppendLine();
        sb.AppendLine(Confirmation);
        sb.AppendLine();
        sb.AppendLine("Research-only. Isolated LOW $1,000 / 0.5% R / 2% SL / 4% TP / 3x. Frozen five unchanged.");
        sb.AppendLine("Statuses are factual. This run does not promote paper or LIVE.");
        sb.AppendLine();
        sb.AppendLine("## Coverage");
        sb.AppendLine();
        sb.AppendLine("| Coin | TF | Bars | Gaps | Taker | Status | Notes |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | --- | --- |");
        foreach (var row in coverage)
        {
            sb.AppendLine($"| {row.Symbol} | {row.Timeframe} | {row.Bars} | {row.Gaps} | {row.TakerCoverage.ToString("P0", CultureInfo.InvariantCulture)} | {row.Status} | {row.Notes} |");
        }

        sb.AppendLine();
        sb.AppendLine("Taker/OI/funding/mark/index are not fabricated. Missing futures keys stay DATA_UNAVAILABLE.");
        sb.AppendLine();
        sb.AppendLine("## Registry");
        sb.AppendLine();
        foreach (var c in candidates)
        {
            sb.AppendLine($"- `{c.CandidateId}` / `{c.ParentTemplateKey}` family SCALPING status {c.Status}");
        }

        sb.AppendLine();
        sb.AppendLine("## Books (IS / VALIDATION / OOS, LowIsolatedRisk, cost stress)");
        sb.AppendLine();
        sb.AppendLine("| Candidate | Coin | TF | Phase | Cost | n | PF | Status | Hold median (min) | P25 | P75 | Notes |");
        sb.AppendLine("| --- | --- | --- | --- | --- | ---: | --- | --- | ---: | ---: | ---: | --- |");
        foreach (var b in books.OrderBy(x => x.CandidateId).ThenBy(x => x.Symbol).ThenBy(x => x.Timeframe).ThenBy(x => x.Phase))
        {
            var pf = ResearchPf.Render(b.ProfitFactorState, b.ProfitFactor);
            sb.AppendLine($"| {b.CandidateId} | {b.Symbol} | {b.Timeframe} | {b.Phase} | {b.CostLabel} | {b.TradeCount} | {pf} | {b.Status} | {Num(b.MedianHoldingMinutes)} | {Num(b.P25HoldingMinutes)} | {Num(b.P75HoldingMinutes)} | {string.Join("; ", b.Notes)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Robustness / cost");
        sb.AppendLine();
        var promising = new List<string>();
        var failed = new List<string>();
        var insufficient = new List<string>();
        foreach (var candidate in candidates)
        {
            var robustness = ResearchDiagnostics.Summarize(candidate.CandidateId, books);
            sb.AppendLine($"- **{candidate.CandidateId}**: {robustness.Status}; costFragile={robustness.CostFragile}; symbols PF>1 {robustness.SymbolsPfAboveOne}/{robustness.SymbolsTested}.");
            if (robustness.Status == ResearchStatuses.CostFragile)
            {
                failed.Add($"{candidate.CandidateId} COST_FRAGILE");
            }
            else if (books.Any(b => b.CandidateId == candidate.CandidateId && b.Status == ResearchStatuses.IsPromising))
            {
                promising.Add(candidate.CandidateId);
            }

            if (books.Where(b => b.CandidateId == candidate.CandidateId).All(b =>
                    b.Status is ResearchStatuses.InsufficientData or ResearchStatuses.DataUnavailable or ResearchStatuses.NoTrades or ResearchStatuses.SkippedTimeframe))
            {
                insufficient.Add(candidate.CandidateId);
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Lists");
        sb.AppendLine();
        sb.AppendLine($"- Promising (IS_PROMISING on a book, not promotion): {(promising.Count == 0 ? "none" : string.Join(", ", promising))}");
        sb.AppendLine($"- Failed / cost-fragile: {(failed.Count == 0 ? "none" : string.Join(", ", failed))}");
        sb.AppendLine($"- Insufficient / unavailable: {(insufficient.Count == 0 ? "none" : string.Join(", ", insufficient))}");
        sb.AppendLine();
        sb.AppendLine("## Occupancy (account equity)");
        sb.AppendLine();
        if (occupancy is null)
        {
            sb.AppendLine("Occupancy replay was not attached to this run.");
        }
        else
        {
            sb.AppendLine($"- Net {occupancy.NetProfit:0.00} on {occupancy.InitialBalance:0} start. Portfolio DD {occupancy.MaximumDrawdownPercent:0.00}% from combined equity.");
            sb.AppendLine($"- Same-coin rejects {occupancy.SameCoinRejects}. Slot rejects {occupancy.SlotRejects}. Heat rejects {occupancy.HeatRejects}.");
            foreach (var reject in occupancy.Rejects.Take(40))
            {
                sb.AppendLine($"  - {reject.Time:u} {reject.StrategyKey} {reject.Symbol} {reject.Reason}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Multiple-testing caveat");
        sb.AppendLine();
        sb.AppendLine("Many keys × coins × timeframes × cost labels were screened. A lucky IS_PROMISING book is expected by chance. OOS was not used to retune. VALIDATED_FOR_PAPER was not assigned.");
        sb.AppendLine();
        sb.AppendLine("## Run notes");
        sb.AppendLine();
        foreach (var note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        sb.AppendLine();
        sb.AppendLine(Confirmation);
        return sb.ToString();
    }

    public static string CoverageMarkdown(IReadOnlyList<ScalpingCoverageRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scalping data coverage");
        sb.AppendLine();
        sb.AppendLine(Confirmation);
        sb.AppendLine();
        sb.AppendLine("OHLCV from `fapi/v1/klines`. Taker from kline[9] when present. Funding/OI/mark/index are not in this cache. Liquidations stay DATA_UNAVAILABLE.");
        sb.AppendLine();
        sb.AppendLine("| Coin | TF | Start | End | Bars | Gaps | Taker | Status |");
        sb.AppendLine("| --- | --- | --- | --- | ---: | ---: | ---: | --- |");
        foreach (var row in rows)
        {
            sb.AppendLine($"| {row.Symbol} | {row.Timeframe} | {row.Start:u} | {row.End:u} | {row.Bars} | {row.Gaps} | {row.TakerCoverage.ToString("P0", CultureInfo.InvariantCulture)} | {row.Status} |");
        }

        sb.AppendLine();
        sb.AppendLine(Confirmation);
        return sb.ToString();
    }

    private static string Num(decimal? value) =>
        value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : "—";
}
