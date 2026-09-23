using System.Globalization;
using System.Text;
using TradingPlatform.Backtesting;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.PriceAction;

namespace TradingPlatform.Research;

public static class PriceActionReport
{
    public const string Confirmation =
        "LIVE = OFF. SCALPING LIVE = OFF. PRICE ACTION LIVE = OFF. Isolated = enforced. Risk Engine = authoritative. No VALIDATED_FOR_PAPER. No future leakage.";

    public static string Render(
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> books,
        IReadOnlyList<ScalpingCoverageRow> coverage,
        OccupancyReplayResult? occupancy,
        IReadOnlyList<SequenceStat> sequences,
        IReadOnlyList<PatternStat> patterns,
        IReadOnlyList<string> hypotheses,
        IReadOnlyList<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PRICE ACTION RESEARCH REPORT");
        sb.AppendLine();
        sb.AppendLine(Confirmation);
        sb.AppendLine();
        sb.AppendLine("Pattern statistics are forward labels. They are not live features and are not strategy PnL.");
        sb.AppendLine("Cup & Handle = NOT_IMPLEMENTED (no objective causal detector).");
        sb.AppendLine("Multiple-testing / selection bias: many patterns and variants were scored on the same bars. IS_PROMISING is not validation.");
        sb.AppendLine();
        sb.AppendLine("## Coverage");
        sb.AppendLine();
        sb.AppendLine("| Coin | TF | Bars | Gaps | Status | Notes |");
        sb.AppendLine("| --- | --- | ---: | ---: | --- | --- |");
        foreach (var row in coverage)
        {
            sb.AppendLine($"| {row.Symbol} | {row.Timeframe} | {row.Bars} | {row.Gaps} | {row.Status} | {row.Notes} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Hypotheses tested");
        sb.AppendLine();
        sb.AppendLine($"Count: {hypotheses.Count}");
        foreach (var h in hypotheses)
        {
            sb.AppendLine($"- {h}");
        }

        sb.AppendLine();
        sb.AppendLine("## Candle sequences (pattern statistics, not trades)");
        sb.AppendLine();
        sb.AppendLine("| Coin | TF | Sequence | N | Fwd1 | Fwd3 | Fwd5 | Med MFE | Med MAE | +0.50% | -0.50% |");
        sb.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var row in sequences)
        {
            sb.AppendLine($"| {row.Symbol} | {row.Timeframe} | {row.Name} | {row.Occurrences} | {Pct(row.MeanFwd1)} | {Pct(row.MeanFwd3)} | {Pct(row.MeanFwd5)} | {Pct(row.MedianMfe)} | {Pct(row.MedianMae)} | {Pct(row.HitPos50)} | {Pct(row.HitNeg50)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Pattern occurrences (pattern statistics, not trades)");
        sb.AppendLine();
        sb.AppendLine("| Coin | TF | Pattern | Status | N | Fwd3 | Med MFE | Med MAE |");
        sb.AppendLine("| --- | --- | --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var row in patterns)
        {
            sb.AppendLine($"| {row.Symbol} | {row.Timeframe} | {row.PatternType} | {row.Status} | {row.Occurrences} | {Pct(row.MeanFwd3)} | {Pct(row.MedianMfe)} | {Pct(row.MedianMae)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Strategy books (Model B)");
        sb.AppendLine();
        sb.AppendLine("| Candidate | Coin | TF | Phase | Cost | Status | Trades | PF | Net |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: |");
        foreach (var b in books)
        {
            sb.AppendLine($"| {b.CandidateId} | {b.Symbol} | {b.Timeframe} | {b.Phase} | {b.CostLabel} | {b.Status} | {b.TradeCount} | {b.ProfitFactor?.ToString("0.00", CultureInfo.InvariantCulture) ?? "0"} | {b.NetPnl.ToString("0.00", CultureInfo.InvariantCulture)} |");
        }

        var promising = books.Where(b => b.Status == ResearchStatuses.IsPromising).Select(b => b.CandidateId).Distinct().ToArray();
        var fragile = books.Where(b => b.Status == ResearchStatuses.CostFragile).Select(b => b.CandidateId).Distinct().ToArray();
        var failed = books.Where(b => b.Status is ResearchStatuses.OosFailed or ResearchStatuses.ValidationFailed).Select(b => b.CandidateId).Distinct().ToArray();
        sb.AppendLine();
        sb.AppendLine("## Status summary");
        sb.AppendLine();
        sb.AppendLine($"- IS_PROMISING (IS only, not validation): {(promising.Length == 0 ? "none" : string.Join(", ", promising))}");
        sb.AppendLine($"- COST_FRAGILE: {(fragile.Length == 0 ? "none" : string.Join(", ", fragile))}");
        sb.AppendLine($"- OOS/VALIDATION failed: {(failed.Length == 0 ? "none" : string.Join(", ", failed))}");
        sb.AppendLine("- VALIDATED_FOR_PAPER: none");
        sb.AppendLine("- CUP_AND_HANDLE: NOT_IMPLEMENTED");
        if (occupancy is not null)
        {
            sb.AppendLine($"- Occupancy same-coin rejects={occupancy.SameCoinRejects} slots={occupancy.SlotRejects} heat={occupancy.HeatRejects} portfolio DD={occupancy.MaximumDrawdownPercent.ToString("0.00", CultureInfo.InvariantCulture)}%");
        }

        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        foreach (var note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    private static string Pct(decimal value) => (value * 100m).ToString("0.00", CultureInfo.InvariantCulture) + "%";
}

public sealed record SequenceStat(
    string Symbol,
    string Timeframe,
    string Name,
    int Occurrences,
    decimal MeanFwd1,
    decimal MeanFwd3,
    decimal MeanFwd5,
    decimal MedianMfe,
    decimal MedianMae,
    decimal HitPos50,
    decimal HitNeg50);

public sealed record PatternStat(
    string Symbol,
    string Timeframe,
    string PatternType,
    string Status,
    int Occurrences,
    decimal MeanFwd3,
    decimal MedianMfe,
    decimal MedianMae);

public static class PriceActionResearch
{
    public static List<SequenceStat> Sequences(string symbol, string timeframe, IReadOnlyList<MarketCandle> candles, PriceActionBook book)
    {
        var rows = new List<SequenceStat>();
        foreach (var (name, pred) in SequencePredicates())
        {
            var fwds1 = new List<decimal>();
            var fwds3 = new List<decimal>();
            var fwds5 = new List<decimal>();
            var mfes = new List<decimal>();
            var maes = new List<decimal>();
            var hitPos = 0;
            var hitNeg = 0;
            for (var i = 20; i < candles.Count - 1; i++)
            {
                if (!pred(book, i))
                {
                    continue;
                }

                if (i > 0 && pred(book, i - 1))
                {
                    continue;
                }

                var outcome = PatternOutcomeEngine.Measure(candles, i, "LONG");
                var h1 = outcome.Horizons.First(h => h.Horizon == 1);
                var h3 = outcome.Horizons.First(h => h.Horizon == 3);
                var h5 = outcome.Horizons.First(h => h.Horizon == 5);
                fwds1.Add(h1.ForwardReturn);
                fwds3.Add(h3.ForwardReturn);
                fwds5.Add(h5.ForwardReturn);
                mfes.Add(h5.Mfe);
                maes.Add(h5.Mae);
                if (h5.Mfe >= 0.005m)
                {
                    hitPos++;
                }

                if (h5.Mae <= -0.005m)
                {
                    hitNeg++;
                }
            }

            var n = fwds1.Count;
            rows.Add(new SequenceStat(
                symbol,
                timeframe,
                name,
                n,
                Mean(fwds1),
                Mean(fwds3),
                Mean(fwds5),
                Median(mfes),
                Median(maes),
                n == 0 ? 0 : (decimal)hitPos / n,
                n == 0 ? 0 : (decimal)hitNeg / n));
        }

        return rows;
    }

    public static List<PatternStat> Patterns(string symbol, string timeframe, IReadOnlyList<MarketCandle> candles, PriceActionBook book)
    {
        return book.Occurrences
            .GroupBy(o => (o.PatternType, o.Status))
            .Select(g =>
            {
                var fwds = new List<decimal>();
                var mfes = new List<decimal>();
                var maes = new List<decimal>();
                foreach (var o in g)
                {
                    var idx = o.ConfirmationIndex ?? o.DetectionIndex;
                    if (idx < 0 || idx >= candles.Count - 1)
                    {
                        continue;
                    }

                    var side = string.Equals(o.Direction, "BEARISH", StringComparison.OrdinalIgnoreCase) ? "SHORT" : "LONG";
                    var outcome = PatternOutcomeEngine.Measure(candles, idx, side);
                    var h3 = outcome.Horizons.First(h => h.Horizon == 3);
                    fwds.Add(h3.ForwardReturn);
                    mfes.Add(h3.Mfe);
                    maes.Add(h3.Mae);
                }

                return new PatternStat(symbol, timeframe, g.Key.PatternType, g.Key.Status, g.Count(), Mean(fwds), Median(mfes), Median(maes));
            })
            .OrderBy(r => r.PatternType)
            .ThenBy(r => r.Status)
            .ToList();
    }

    private static IEnumerable<(string Name, Func<PriceActionBook, int, bool> Pred)> SequencePredicates() =>
    [
        ("2_bullish", (b, i) => b.Sequences[i].BullRun == 2),
        ("3_bullish", (b, i) => b.Sequences[i].BullRun == 3),
        ("4_bullish", (b, i) => b.Sequences[i].BullRun == 4),
        ("5_bullish", (b, i) => b.Sequences[i].BullRun == 5),
        ("6plus_bullish", (b, i) => b.Sequences[i].BullRun >= 6),
        ("2_bearish", (b, i) => b.Sequences[i].BearRun == 2),
        ("3_bearish", (b, i) => b.Sequences[i].BearRun == 3),
        ("4_bearish", (b, i) => b.Sequences[i].BearRun == 4),
        ("5_bearish", (b, i) => b.Sequences[i].BearRun == 5),
        ("6plus_bearish", (b, i) => b.Sequences[i].BearRun >= 6),
        ("bullish_then_rejection", (b, i) => b.Sequences[i].BullRun >= 3 && b.HasEvent(i, PatternKinds.BearishRejection)),
        ("bearish_then_rejection", (b, i) => b.Sequences[i].BearRun >= 3 && b.HasEvent(i, PatternKinds.BullishRejection)),
        ("compression_then_expansion", (b, i) => b.Sequences[i].ExpansionRun == 1 && i > 0 && b.Sequences[i - 1].CompressionRun >= 3),
        ("expansion_continuation", (b, i) => b.Sequences[i].ImpulseFollowThrough),
        ("expansion_exhaustion", (b, i) => b.Sequences[i].FailedContinuation)
    ];

    private static decimal Mean(List<decimal> xs) => xs.Count == 0 ? 0 : xs.Average();

    private static decimal Median(List<decimal> xs)
    {
        if (xs.Count == 0)
        {
            return 0;
        }

        xs.Sort();
        return xs[xs.Count / 2];
    }
}
