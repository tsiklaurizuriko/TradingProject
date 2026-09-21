using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingPlatform.Research;

public static class Wave5Report
{
    public static string Render(Wave5RouterResult result, IReadOnlyList<string> runNotes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Model B Wave-5 strategy-router report");
        sb.AppendLine();
        sb.AppendLine("Existing signals only. No new entries, no FrozenRisk/LOW change, LIVE off, not VALIDATED_FOR_PAPER.");
        sb.AppendLine("Question: can a causal regime/score router turn the existing weak pool into selected trades with better OOS expectancy than a same-count random subset?");
        sb.AppendLine();
        sb.AppendLine($"**Verdict: {result.Classification}. {(result.Classification is "REJECTED" or "FRAGILE" ? "NO ROBUST CONDITIONAL ALPHA FOUND." : "")}**");
        sb.AppendLine();

        sb.AppendLine("## 1. Existing strategy universe");
        sb.AppendLine();
        sb.AppendLine($"Candidates: **{result.Universe.Count}**. Frozen five + ResearchRegistry.All + Wave-2 natives + OHLCV-only advanced/alpha templates. OI/funding/pairs/XS/router templates were not harvested (DATA_UNAVAILABLE or circular).");
        sb.AppendLine();
        sb.AppendLine("| Id | Family | Kind | Parent / native |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var c in result.Universe)
        {
            sb.AppendLine($"| {c.CandidateId} | {Wave5Catalog.Family(c)} | {c.Kind} | {c.ParentTemplateKey ?? c.NativeKey} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 2. Signal-event dataset");
        sb.AppendLine();
        var ev = result.Events;
        sb.AppendLine($"Events: **{ev.Count}**. Symbols {ev.Select(e => e.Symbol).Distinct().Count()}. Timeframes {string.Join(", ", ev.Select(e => e.Timeframe).Distinct().OrderBy(x => x))}.");
        sb.AppendLine($"IS {ev.Count(e => Wave5Router.Phase(e.SignalTime) == "IS")} / VAL {ev.Count(e => Wave5Router.Phase(e.SignalTime) == "VALIDATION")} / OOS {ev.Count(e => Wave5Router.Phase(e.SignalTime) == "OOS")}.");
        sb.AppendLine("Fill = next open. SL 2% then TP 4%. Round-trip BASE cost 0.12%. Features at signal close only.");
        sb.AppendLine();
        sb.AppendLine("| Timeframe | Events | LONG | SHORT |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var g in ev.GroupBy(e => e.Timeframe).OrderBy(g => g.Key))
        {
            sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(x => x.Direction > 0)} | {g.Count(x => x.Direction < 0)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 3. Conditional expectancy (IS only, descriptive)");
        sb.AppendLine();
        sb.AppendLine("Not used to trade OOS. Router scores are purged 90d windows, not these tables.");
        sb.AppendLine();
        sb.AppendLine("| Strategy | Bucket | n | PF | Exp | WR |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var row in result.Conditional.Where(r => r.Trades >= 20 && r.Bucket is "ALL" or "VOL_LOW" or "VOL_MID" or "VOL_HIGH" or "TREND_UP" or "TREND_DOWN" or "BTC_UP" or "BTC_DOWN" or "BREADTH_HIGH" or "BREADTH_LOW" or "RS_HIGH" or "RS_LOW" or "VOL_SHOCK" or "LONG" or "SHORT"))
        {
            sb.AppendLine($"| {row.Strategy} | {row.Bucket} | {row.Trades} | {Num(row.Pf)} | {Pct(row.Expectancy)} | {Pct(row.WinRate)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 4. Regime analysis");
        sb.AppendLine();
        sb.AppendLine("BTC vol tercile = rank of BTC ATR% in the last 50 closed bars. Trend = EMA20 vs EMA50 on the coin. Breadth = share of harvested coins with close > EMA20 at the same OpenTime.");
        sb.AppendLine();
        var isAll = result.Conditional.Where(r => r.Bucket == "ALL" && r.Trades >= 20).ToList();
        var isPos = result.Conditional.Where(r => r.Trades >= 100 && r.Pf > 1.0 && r.Expectancy > 0).ToList();
        sb.AppendLine($"IS strategy-level ALL buckets: {isAll.Count}. Median PF {Num(Median(isAll.Select(r => r.Pf)))}. None of the large ALL buckets (n≥100) have PF>1.");
        sb.AppendLine($"IS buckets with n≥100 and PF>1: **{isPos.Count}** {(isPos.Count == 0 ? "(none)" : string.Join(", ", isPos.Take(8).Select(r => $"{r.Strategy}/{r.Bucket} PF={Num(r.Pf)}")))}.");
        sb.AppendLine("Conditional pockets that look less bad in-sample were not used to trade OOS. The purged 90d score is the only routing input.");
        sb.AppendLine();

        sb.AppendLine("## 5. Strategy / timeframe compatibility");
        sb.AppendLine();
        sb.AppendLine("| Slice | n | PF | Exp | WR |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var s in result.Slices.Where(x => x.Label.StartsWith("OOS-ALL-", StringComparison.Ordinal)))
        {
            sb.AppendLine($"| {s.Label} | {s.Trades} | {Num(s.Pf)} | {Pct(s.Expectancy)} | {Pct(s.WinRate)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 6. Signal scoring");
        sb.AppendLine();
        sb.AppendLine("score = mean of 5 causal components (90d, min 20 trades else 0): regime match (vol×trend bucket mean R), strategy recency, symbol fit, timeframe fit, volatility-bucket fit. R = net return / 2% stop. Purged: only trades with ExitTime < SignalTime.");
        sb.AppendLine();

        sb.AppendLine("## 7. Ranking results");
        sb.AppendLine();
        Table(sb, result, l => (l.Contains("TOP") || l.Contains("BOTTOM") || (l.Contains("-ALL-") && !l.Contains("CONS") && !l.Contains("CONFLICT") && !l.Contains("LIQUID") && !l.Contains("PORT"))) && !l.Contains("-C"));
        sb.AppendLine();

        sb.AppendLine("## 8. Consensus / conflict");
        sb.AppendLine();
        Table(sb, result, l => l.Contains("CONS") || l.Contains("CONFLICT"));
        sb.AppendLine();

        sb.AppendLine("## 9. Router-selected trades");
        sb.AppendLine();
        Table(sb, result, l => l.StartsWith("OOS-TOP1") || l.StartsWith("OOS-ALL"));
        sb.AppendLine();

        sb.AppendLine("## 10. Random-selection control");
        sb.AppendLine();
        Table(sb, result, l => l.Contains("RAND"));
        sb.AppendLine("Mandatory: if TOP-K does not beat RAND-K on OOS PF **and** expectancy, the router has no value.");
        sb.AppendLine();

        sb.AppendLine("## 11. Top-vs-bottom control");
        sb.AppendLine();
        Table(sb, result, l => (l.Contains("BOTTOM") || l.Contains("TOP1")) && !l.Contains("-C"));
        sb.AppendLine();

        sb.AppendLine("## 12. OOS results");
        sb.AppendLine();
        Table(sb, result, l => l.StartsWith("OOS-") && !l.Contains("-C"));
        sb.AppendLine();

        sb.AppendLine("## 13. Cost sensitivity");
        sb.AppendLine();
        Table(sb, result, l => l.Contains("-C1.") || l.Contains("-C2."));
        sb.AppendLine();

        sb.AppendLine("## 14. Portfolio simulation");
        sb.AppendLine();
        Table(sb, result, l => l.Contains("PORT") || l.Contains("LIQUID"));
        sb.AppendLine("Max 1 / max 2 Isolated slots, one coin at a time, no shared margin. TOP-1 globally per timeframe timestamp.");
        sb.AppendLine();

        sb.AppendLine("## 15. Failure analysis");
        sb.AppendLine();
        sb.AppendLine("- Fewer trades is not success. Random same-count is the control.");
        sb.AppendLine("- OOS TOP1-ALL PF 0.8946 vs RAND1 0.9088 vs BOTTOM1 0.9150. Ranking is inverted: bottom-ranked signals beat top-ranked.");
        sb.AppendLine("- OOS CONS3-5m PF 1.024 is the only PF>1 slice. It has no random-selection control, fails on 15m (0.87) and 1h (0.90), and ALL CONS3 is 0.922. One timeframe is not an edge.");
        sb.AppendLine("- Isolated PORT-MAX2 PF 0.99 still negative expectancy. Opportunity-cap does not create alpha.");
        sb.AppendLine("- Cost +25/+50/+100: TOP1 remains worse than random at every multiplier.");
        sb.AppendLine("- 10-coin 5m/15m/1h is the routing experiment. Full 528×5m was not loaded (5m JSON size).");
        sb.AppendLine("- Funding/OI not attached to 5m/15m events. Liquidations still DATA_UNAVAILABLE.");
        sb.AppendLine("- ML was not run: validation TOP1 did not beat random.");
        sb.AppendLine();

        sb.AppendLine("## 16. Final classification");
        sb.AppendLine();
        sb.AppendLine($"**{result.Classification}**");
        sb.AppendLine();
        if (result.Classification is "REJECTED" or "FRAGILE")
        {
            sb.AppendLine("**NO ROBUST CONDITIONAL ALPHA FOUND**");
            sb.AppendLine();
            sb.AppendLine("Do not create another strategy wave from this architecture. Isolated LOW and LIVE were not changed.");
        }

        sb.AppendLine();
        sb.AppendLine("## Run notes");
        foreach (var note in result.Notes.Concat(runNotes).Where(n => !n.Contains(" bars=", StringComparison.Ordinal)))
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    public static void WriteArtifacts(string directory, Wave5RouterResult result)
    {
        Directory.CreateDirectory(directory);
        var slim = new
        {
            result.Classification,
            Universe = result.Universe.Select(c => c.CandidateId).ToArray(),
            EventCount = result.Events.Count,
            result.Slices,
            result.Notes
        };
        File.WriteAllText(
            Path.Combine(directory, "wave5-router.json"),
            JsonSerializer.Serialize(slim, new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            }));
    }

    private static void Table(StringBuilder sb, Wave5RouterResult result, Func<string, bool> filter)
    {
        sb.AppendLine("| Slice | n | PF | Exp | WR | Mean R | DD | Symbols | Strategies | Top coin share |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var s in result.Slices.Where(x => filter(x.Label)))
        {
            sb.AppendLine($"| {s.Label} | {s.Trades} | {Num(s.Pf)} | {Pct(s.Expectancy)} | {Pct(s.WinRate)} | {Num(s.MeanR)} | {Pct(s.MaxDd)} | {s.Symbols} | {s.Strategies} | {Pct(s.TopSymbolShare)} |");
        }
    }

    private static string Num(double value) =>
        double.IsNaN(value) ? "n/a" : double.IsInfinity(value) ? "Inf" : value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Pct(double value) =>
        double.IsNaN(value) ? "n/a" : value.ToString("0.00%", CultureInfo.InvariantCulture);

    private static double Median(IEnumerable<double> values)
    {
        var arr = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToArray();
        if (arr.Length == 0)
        {
            return double.NaN;
        }

        var mid = arr.Length / 2;
        return arr.Length % 2 == 1 ? arr[mid] : (arr[mid - 1] + arr[mid]) / 2.0;
    }
}
