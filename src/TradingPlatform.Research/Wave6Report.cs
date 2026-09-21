using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingPlatform.Research;

public static class Wave6Report
{
    public static string Render(Wave6Result r, IReadOnlyList<string> runNotes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Model B Wave-6 trade-path report");
        sb.AppendLine();
        sb.AppendLine("Existing 43-strategy signals only. No new entries, no router, no FrozenRisk/LOW change, LIVE off, not VALIDATED_FOR_PAPER.");
        sb.AppendLine("Question: do those signals contain any economically useful information about the **future price path** (MFE/MAE, first-touch, volatility, holding horizon) even when they do not predict final 2%/4% direction?");
        sb.AppendLine();
        sb.AppendLine($"**Verdict: {r.Classification}**");
        sb.AppendLine();
        if (r.Classification == "VOLATILITY INFORMATION FOUND")
        {
            sb.AppendLine("This is **not** directional alpha and **not** a tradable Isolated book. Signals cluster in high-future-range windows; MFE and MAE both inflate; flip-direction P(+1R) ≈ signal P. Exits were not OOS-better than random after an IS freeze.");
            sb.AppendLine();
        }
        sb.AppendLine("1R = existing LOW stop distance (2% of slipped next-open fill). Path labels do not use the 2%/4% book as an exit. Same-bar both sides → adverse first.");
        sb.AppendLine();

        sb.AppendLine("## 1. Dataset");
        sb.AppendLine();
        sb.AppendLine($"Complete 24h signal paths: **{r.SignalCount}**. Random-entry controls: **{r.RandomCount}** (same coins, timeframes, phase, long/short counts; signal fill bars excluded).");
        sb.AppendLine("Window 2024-09-18 → 2026-09-19. Coins: BTC, ETH, BNB, SOL, XRP, DOGE, ADA, AVAX, LINK, LTC. TFs: 5m / 15m / 1h.");
        sb.AppendLine($"IS < {Wave5Router.TrainEnd:yyyy-MM-dd}, VAL < {Wave5Router.ValEnd:yyyy-MM-dd}, then OOS. Occupancy matches Wave-5 Isolated one-position harvest.");
        sb.AppendLine();
        PathTable(sb, r, k => k.EndsWith("|sig", StringComparison.Ordinal) && !k.Contains("|sym:", StringComparison.Ordinal) && !k.Contains("|st:", StringComparison.Ordinal));

        sb.AppendLine("## 2. MFE distribution (24h, R units)");
        sb.AppendLine();
        sb.AppendLine("Mean maximum favorable excursion after slipped entry. Signal vs random-entry vs flipped direction.");
        sb.AppendLine();
        CompareTable(sb, r, row => row.Mfe);

        sb.AppendLine("## 3. MAE distribution (24h, R units)");
        sb.AppendLine();
        sb.AppendLine("Mean maximum adverse excursion. Lower is better for a directional signal.");
        sb.AppendLine();
        CompareTable(sb, r, row => row.Mae);

        sb.AppendLine("## 4. First-touch probabilities");
        sb.AppendLine();
        sb.AppendLine($"OOS ALL signals n={r.OosN24}. Rate of **+F R before −A R** (same-bar adverse first).");
        sb.AppendLine();
        sb.AppendLine("| +F \\ −A | 0.25R | 0.50R | 0.75R | 1.00R | 1.25R | 1.50R | 2.00R |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        for (var f = 0; f < 7; f++)
        {
            sb.Append($"| +{Wave6Path.Levels[f]:0.00}R |");
            for (var a = 0; a < 7; a++)
            {
                var v = r.OosN24 == 0 ? double.NaN : r.OosBeforeSig[f * 7 + a] / (double)r.OosN24;
                sb.Append($" {Pct(v)} |");
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("P(+1R before −1R) by slice:");
        sb.AppendLine();
        CompareTable(sb, r, row => row.P1R);

        sb.AppendLine("## 5. Holding-period analysis");
        sb.AppendLine();
        sb.AppendLine("1-bar and 4-bar close in R. Whip = both +0.5R and −0.5R inside 24h. Delay = 1-bar close ≤0 but MFE ≥0.5R.");
        sb.AppendLine();
        sb.AppendLine("| Key | n | Ret1R | Ret4R | Cont1 | Whip | Delay |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var row in Pool(r))
        {
            sb.AppendLine($"| {row.Key} | {row.N} | {Num(row.Ret1)} | {Num(row.Ret4)} | {Pct(row.Cont1)} | {Pct(row.Whip)} | {Pct(row.Delay)} |");
        }

        sb.AppendLine();

        sb.AppendLine("## 6. Direction vs random");
        sb.AppendLine();
        sb.AppendLine("Flip = same path, opposite signed direction. Random-dir P ≈ 0.5×(P_sig+P_flip).");
        sb.AppendLine();
        CompareTable(sb, r, row => row.P1R);

        sb.AppendLine("## 7. Signal vs random entry");
        sb.AppendLine();
        sb.AppendLine("Random fill times in the same phase/coin/TF, same long/short count, signal bars excluded.");
        sb.AppendLine();
        sb.AppendLine("| Key | n | P+1R | MFE | MAE | Range | Ret4 |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var row in Pool(r))
        {
            sb.AppendLine($"| {row.Key} | {row.N} | {Pct(row.P1R)} | {Num(row.Mfe)} | {Num(row.Mae)} | {Num(row.Range)} | {Num(row.Ret4)} |");
        }

        sb.AppendLine();

        sb.AppendLine("## 8. TP/SL grid");
        sb.AppendLine();
        sb.AppendLine("Pre-registered 6×6 at **24h** max hold. Gross before extra cost shock. IS is descriptive; OOS is frozen.");
        sb.AppendLine();
        GridMatrix(sb, r, "OOS", "ALL", "sig");
        sb.AppendLine("Random-entry OOS ALL 24h grid PF:");
        sb.AppendLine();
        GridMatrix(sb, r, "OOS", "ALL", "rnd");

        sb.AppendLine("## 9. Time exits");
        sb.AppendLine();
        ExtraTable(sb, r, n => n.StartsWith("TIME-", StringComparison.Ordinal));

        sb.AppendLine("## 10. Breakeven / trailing");
        sb.AppendLine();
        ExtraTable(sb, r, n => n.StartsWith("BE-", StringComparison.Ordinal) || n.StartsWith("TRAIL", StringComparison.Ordinal) || n.StartsWith("ATR", StringComparison.Ordinal) || n.Contains("/SL1-", StringComparison.Ordinal));

        sb.AppendLine("## 11. IS / VAL / OOS");
        sb.AppendLine();
        sb.AppendLine("| Phase | Kind | n | P+1R | MFE | MAE | Range | Ret4 |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var phase in new[] { "IS", "VALIDATION", "OOS" })
        {
            foreach (var kind in new[] { "sig", "rnd", "flip" })
            {
                var row = r.Rows.FirstOrDefault(x => x.Key == $"{phase}|ALL|{kind}");
                if (row is null)
                {
                    continue;
                }

                sb.AppendLine($"| {phase} | {kind} | {row.N} | {Pct(row.P1R)} | {Num(row.Mfe)} | {Num(row.Mae)} | {Num(row.Range)} | {Num(row.Ret4)} |");
            }
        }

        sb.AppendLine();

        sb.AppendLine("## 12. Cost sensitivity");
        sb.AppendLine();
        sb.AppendLine("Round-trip cost = 2 × multiplier × (0.04%+0.02%). Applied to OOS ALL 24h **TP1.00/SL1.00** expectancy (not to path probabilities).");
        sb.AppendLine();
        var cell = r.Grid.FirstOrDefault(g => g is { Phase: "OOS", Tf: "ALL", Kind: "sig", Label: "TP1.00/SL1.00" });
        var rnd = r.Grid.FirstOrDefault(g => g is { Phase: "OOS", Tf: "ALL", Kind: "rnd", Label: "TP1.00/SL1.00" });
        sb.AppendLine("| Mult | Signal net exp | Random net exp |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var m in new[] { 1.0m, 1.25m, 1.5m, 2.0m })
        {
            var c = 2.0 * (double)(m * (Wave6Path.Fee + Wave6Path.Slip));
            sb.AppendLine($"| {m.ToString(CultureInfo.InvariantCulture)} | {Pct((cell?.Exp ?? double.NaN) - c)} | {Pct((rnd?.Exp ?? double.NaN) - c)} |");
        }

        sb.AppendLine();

        sb.AppendLine("## 13. 5m / 15m / 1h");
        sb.AppendLine();
        sb.AppendLine("| TF | Kind | n | P+1R | Range | Ret4 |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var tf in new[] { "5m", "15m", "1h" })
        {
            foreach (var kind in new[] { "sig", "rnd", "flip" })
            {
                var row = r.Rows.FirstOrDefault(x => x.Key == $"OOS|{tf}|{kind}");
                if (row is null)
                {
                    continue;
                }

                sb.AppendLine($"| {tf} | {kind} | {row.N} | {Pct(row.P1R)} | {Num(row.Range)} | {Num(row.Ret4)} |");
            }
        }

        sb.AppendLine();

        sb.AppendLine("## 14. Symbol robustness");
        sb.AppendLine();
        sb.AppendLine("| Coin | n sig | P+1R sig | n rnd | P+1R rnd | Δ |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var row in r.Rows.Where(x => x.Key.StartsWith("OOS|15m|sym:", StringComparison.Ordinal) && x.Key.EndsWith("|sig", StringComparison.Ordinal)).OrderBy(x => x.Key))
        {
            var coin = row.Key.Replace("OOS|15m|sym:", "", StringComparison.Ordinal).Replace("|sig", "", StringComparison.Ordinal);
            var other = r.Rows.FirstOrDefault(x => x.Key == $"OOS|15m|sym:{coin}|rnd");
            var d = other is null ? double.NaN : row.P1R - other.P1R;
            sb.AppendLine($"| {coin} | {row.N} | {Pct(row.P1R)} | {other?.N ?? 0} | {Pct(other?.P1R ?? double.NaN)} | {Pct(d)} |");
        }

        sb.AppendLine();

        sb.AppendLine("## 15. Final information classification");
        sb.AppendLine();
        sb.AppendLine($"**{r.Classification}**");
        sb.AppendLine();
        foreach (var a in r.Answers)
        {
            sb.AppendLine($"- {a}");
        }

        sb.AppendLine();
        if (r.Classification == "NO MEASURABLE INFORMATION FOUND")
        {
            sb.AppendLine("TYPE E. Stop. Do not create another strategy family from this architecture. Isolated LOW and LIVE were not changed.");
            sb.AppendLine();
        }

        if (r.Classification == "VOLATILITY INFORMATION FOUND")
        {
            sb.AppendLine("TYPE C. Range information only. Phase 11 P(+1R) model skipped because OOS P_signal ≈ P_flip. Isolated LOW and LIVE were not changed.");
            sb.AppendLine();
        }

        sb.AppendLine("## Run notes");
        foreach (var n in r.Notes.Concat(runNotes).Where(x => !x.Contains(" bars=", StringComparison.Ordinal)))
        {
            sb.AppendLine($"- {n}");
        }

        return sb.ToString();
    }

    public static void WriteArtifacts(string directory, Wave6Result result)
    {
        Directory.CreateDirectory(directory);
        var slim = new
        {
            result.Classification,
            result.SignalCount,
            result.RandomCount,
            result.Answers,
            result.Notes,
            Rows = result.Rows.Where(x => !x.Key.Contains("|st:", StringComparison.Ordinal)).ToArray(),
            Grid = result.Grid.Where(g => g is { Tf: "ALL" or "5m" or "15m" or "1h", Phase: "OOS" or "IS" or "VALIDATION" }).ToArray()
        };
        File.WriteAllText(
            Path.Combine(directory, "wave6-paths.json"),
            JsonSerializer.Serialize(slim, new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            }));
    }

    private static IEnumerable<Wave6Row> Pool(Wave6Result r) =>
        r.Rows.Where(x =>
        {
            var p = x.Key.Split('|');
            return p.Length == 3 && p[2] is "sig" or "rnd" or "flip" && p[1] is "5m" or "15m" or "1h" or "ALL";
        });

    private static void PathTable(StringBuilder sb, Wave6Result r, Func<string, bool> filter)
    {
        sb.AppendLine("| Key | n | MFE | MAE | Range | P+1R | P−1R first |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var row in r.Rows.Where(x => filter(x.Key)))
        {
            sb.AppendLine($"| {row.Key} | {row.N} | {Num(row.Mfe)} | {Num(row.Mae)} | {Num(row.Range)} | {Pct(row.P1R)} | {Pct(row.PAdv1R)} |");
        }

        sb.AppendLine();
    }

    private static void CompareTable(StringBuilder sb, Wave6Result r, Func<Wave6Row, double> sel)
    {
        sb.AppendLine("| Slice | Signal | Random entry | Flip |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var tf in new[] { "ALL", "5m", "15m", "1h" })
        {
            foreach (var phase in new[] { "OOS", "VALIDATION", "IS" })
            {
                var s = r.Rows.FirstOrDefault(x => x.Key == $"{phase}|{tf}|sig");
                var n = r.Rows.FirstOrDefault(x => x.Key == $"{phase}|{tf}|rnd");
                var f = r.Rows.FirstOrDefault(x => x.Key == $"{phase}|{tf}|flip");
                if (s is null)
                {
                    continue;
                }

                sb.AppendLine($"| {phase} {tf} | {Num(sel(s))} | {Num(n is null ? double.NaN : sel(n))} | {Num(f is null ? double.NaN : sel(f))} |");
            }
        }

        sb.AppendLine();
    }

    private static void GridMatrix(StringBuilder sb, Wave6Result r, string phase, string tf, string kind)
    {
        sb.AppendLine($"|{kind} PF TP\\SL | 0.50 | 0.75 | 1.00 | 1.25 | 1.50 | 2.00 |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var tp in Wave6Path.GridR)
        {
            sb.Append($"| TP {tp.ToString("0.00", CultureInfo.InvariantCulture)} |");
            foreach (var sl in Wave6Path.GridR)
            {
                var cell = r.Grid.FirstOrDefault(g =>
                    g.Phase == phase && g.Tf == tf && g.Kind == kind
                    && Math.Abs(g.TpR - tp) < 1e-9 && Math.Abs(g.SlR - sl) < 1e-9);
                sb.Append($" {Num(cell?.Pf ?? double.NaN)} |");
            }

            sb.AppendLine();
        }

        sb.AppendLine();
    }

    private static void ExtraTable(StringBuilder sb, Wave6Result r, Func<string, bool> names)
    {
        sb.AppendLine("| Exit | Phase | TF | Kind | n | PF | Exp | WR |");
        sb.AppendLine("|---|---|---|---|---:|---:|---:|---:|");
        foreach (var g in r.Grid.Where(x => names(x.Label) && x.Tf == "ALL" && x.Phase is "OOS" or "IS" or "VALIDATION"))
        {
            sb.AppendLine($"| {g.Label} | {g.Phase} | {g.Tf} | {g.Kind} | {g.N} | {Num(g.Pf)} | {Pct(g.Exp)} | {Pct(g.Wr)} |");
        }

        sb.AppendLine();
    }

    private static string Num(double value) =>
        double.IsNaN(value) ? "n/a" : double.IsInfinity(value) ? "Inf" : value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Pct(double value) =>
        double.IsNaN(value) ? "n/a" : value.ToString("0.00%", CultureInfo.InvariantCulture);
}
