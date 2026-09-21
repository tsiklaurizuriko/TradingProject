using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.Research;

public static class Wave3Report
{
    public static string Render(Wave3SignalResult result, IReadOnlyList<string> runNotes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Model B Wave-3 signal research report");
        sb.AppendLine();
        sb.AppendLine("Stage 1 only: predictive power of **relative / residual / volume-shock / taker / funding** signals.");
        sb.AppendLine("No Isolated strategy was promoted. Frozen five, FrozenRisk, LOW book numbers, and LIVE were not changed.");
        sb.AppendLine("Parameters were pre-registered. OOS was not used to mutate hypotheses.");
        sb.AppendLine();
        sb.AppendLine("## 1. Data availability audit");
        sb.AppendLine();
        sb.AppendLine("| Dataset | Status |");
        sb.AppendLine("|---|---|");
        foreach (var kv in result.DataAudit.OrderBy(k => k.Key))
        {
            sb.AppendLine($"| {kv.Key} | {kv.Value} |");
        }

        sb.AppendLine();
        sb.AppendLine("Cross-sectional ranks are **derived** from the aligned 10-coin 1h panel. They were not fabricated as a stored universe snapshot. A bar is ranked only against coins present at the same OpenTime.");
        sb.AppendLine();
        sb.AppendLine("## 2. Signal hypotheses");
        sb.AppendLine();
        sb.AppendLine("| Id | Mechanism | Direction | Data | Class |");
        sb.AppendLine("|---|---|---|---|---|");
        var classes = result.Hypotheses.ToDictionary(h => h.Id, h => Wave3SignalResearch.Classify(h, result.Ic), StringComparer.OrdinalIgnoreCase);
        foreach (var h in result.Hypotheses)
        {
            sb.AppendLine($"| {h.Id} | {h.Mechanism} | {h.Direction} | {h.DataUsed} | **{classes[h.Id]}** |");
        }

        var promising = classes.Count(kv => kv.Value is "PROMISING" or "ROBUST CANDIDATE");
        sb.AppendLine();
        if (promising == 0)
        {
            sb.AppendLine("**NO ROBUST ALPHA FOUND**");
            sb.AppendLine();
            sb.AppendLine("No pre-registered signal cleared IS predictive-power gates and validation confirmation with a cost-aware execution spread. Stage 2 trading simulation and the 1,584-book run were not launched.");
        }

        sb.AppendLine();
        sb.AppendLine("## 3. Forward-return analysis (close-to-close CS IC and tercile spread)");
        sb.AppendLine();
        sb.AppendLine("MeanCsIc = average cross-sectional Spearman(signal, fwd) across timestamps. Spread = top tercile mean fwd − bottom tercile mean fwd. LONG mean = top tercile. SHORT mean = −bottom tercile (shorting the weak).");
        sb.AppendLine();
        sb.AppendLine("| Hypothesis | Phase | H | n | CS IC | TS IC | LONG mean | SHORT mean | Spread | Exec spread |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var row in result.Ic.OrderBy(r => r.HypothesisId).ThenBy(r => r.Phase).ThenBy(r => r.Horizon))
        {
            if (row.Horizon is not (4 or 8 or 24))
            {
                continue;
            }

            sb.AppendLine($"| {row.HypothesisId} | {row.Phase} | {row.Horizon} | {row.Observations} | {Num(row.MeanCsIc)} | {Num(row.MeanTsIc)} | {Pct(row.LongMean)} | {Pct(row.ShortMean)} | {Pct(row.Spread)} | {Pct(row.MeanExecSpread)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 4. Cross-sectional bucket analysis (terciles, horizon 24, IS/VAL/OOS)");
        sb.AppendLine();
        sb.AppendLine("| Hypothesis | Phase | Bucket (0=weak) | n | Mean fwd | Median | Hit | Exec mean |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var row in result.Buckets.Where(b => b.Horizon == 24).OrderBy(b => b.HypothesisId).ThenBy(b => b.Phase).ThenBy(b => b.Bucket))
        {
            sb.AppendLine($"| {row.HypothesisId} | {row.Phase} | {row.Bucket} | {row.Observations} | {Pct(row.MeanFwd)} | {Pct(row.MedianFwd)} | {Pct(row.HitRate)} | {Pct(row.MeanExecFwd)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 5. LONG vs SHORT");
        sb.AppendLine();
        sb.AppendLine("LONG = hold the top tercile of the signed signal. SHORT = short the bottom tercile. Combined spread hides nothing: both sides are shown. A side with negative mean is not an Isolated book to enable.");
        sb.AppendLine();
        foreach (var h in result.Hypotheses)
        {
            var oos = result.Ic.FirstOrDefault(r => r.HypothesisId == h.Id && r.Phase == "OOS" && r.Horizon == 24);
            if (oos is null)
            {
                continue;
            }

            sb.AppendLine($"- **{h.Id}** OOS 24h: LONG mean {Pct(oos.LongMean)} hit {Pct(oos.HitTop)}; SHORT mean {Pct(oos.ShortMean)} hit {Pct(oos.HitBottom)}; class {classes[h.Id]}.");
        }

        sb.AppendLine();
        sb.AppendLine("## 6. Market regime analysis");
        sb.AppendLine();
        sb.AppendLine("BTC trend = sign(EMA20−EMA50) at t. BTC vol = ATR% percentile(50) at t. Low &lt; 0.4, high &gt; 0.6. Conditional IC at horizon 4 and 24.");
        sb.AppendLine();
        sb.AppendLine("| Hypothesis | Regime | Phase | H | n | CS IC | Spread |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|");
        foreach (var row in result.Regimes.Where(r => r.Phase is "IS" or "OOS" && r.Horizon == 24))
        {
            sb.AppendLine($"| {row.HypothesisId} | {row.Regime} | {row.Phase} | {row.Horizon} | {row.Observations} | {Num(row.MeanCsIc)} | {Pct(row.Spread)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 7. Funding / OI");
        sb.AppendLine();
        if (result.Funding.Count == 0)
        {
            sb.AppendLine("Funding IC was not evaluated in this run (series not loaded) or was skipped.");
        }
        else
        {
            sb.AppendLine("| Hypothesis | Phase | Horizon hours | n | IC | High funding mean fwd | Low funding mean fwd |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (var row in result.Funding)
            {
                sb.AppendLine($"| {row.HypothesisId} | {row.Phase} | {row.HorizonHours} | {row.Observations} | {Num(row.Ic)} | {Pct(row.HighFundingMeanFwd)} | {Pct(row.LowFundingMeanFwd)} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Open interest: **DATA_UNAVAILABLE** for a 2-year claim. Public `openInterestHist` is ~30 days. 30-day OI was not substituted.");
        sb.AppendLine();
        sb.AppendLine("## 8. Candidate strategies");
        sb.AppendLine();
        sb.AppendLine("Stage 2 Isolated replay is gated on PROMISING signals. None were promoted unless classified PROMISING above.");
        sb.AppendLine("Paired long-strong / short-weak is **not** an Isolated Cross book: LOW allows two independent Isolated positions, but they do not share margin and the current replay is single-book. The Stage 1 spread is the statistical long-short factor, not an executable pair.");
        sb.AppendLine();
        sb.AppendLine("## 9. Cost sensitivity");
        sb.AppendLine();
        sb.AppendLine($"Execution-aware spread (next open → close[t+h]) is reported as Exec spread. Round-trip hurdle is {Wave3SignalResearch.RoundTripCost.ToString("P2", CultureInfo.InvariantCulture)}. A signal whose exec spread is below the hurdle cannot survive BASE costs even with a perfect 2R exit.");
        sb.AppendLine();
        sb.AppendLine("## 10. OOS results");
        sb.AppendLine();
        sb.AppendLine("OOS is the last 20% of aligned 1h bars. It was not used to change lookbacks (1/4/12/24, beta 168, vol 20, ATR 14, terciles).");
        sb.AppendLine();
        sb.AppendLine("## 11. Full-universe results");
        sb.AppendLine();
        sb.AppendLine("Not run. Stage 3 (528 coins × 5m/15m/1h) is blocked until a Stage 1 signal is PROMISING.");
        sb.AppendLine();
        sb.AppendLine("## 12. Failure reasons");
        sb.AppendLine();
        sb.AppendLine("Wave-1/2 failed as absolute-price strategies (WR ≈ 33% at 2R after costs). Wave-3 asks whether **relative** information is predictable. A hypothesis fails when IS CS IC ≤ 0.02 or the tercile spread is not positive, or VALIDATION does not keep the IS sign, or the execution spread does not clear costs.");
        sb.AppendLine();
        foreach (var h in result.Hypotheses)
        {
            sb.AppendLine($"- `{h.Id}`: **{classes[h.Id]}**. {h.Mechanism}");
        }

        sb.AppendLine();
        sb.AppendLine("## 13. Data limitations");
        sb.AppendLine();
        sb.AppendLine("- 10 liquid coins is not the 528-coin cross-section; ranks among mega-caps can differ from the full universe.");
        sb.AppendLine("- Inner join drops hours where any coin is missing.");
        sb.AppendLine("- Terciles, not deciles.");
        sb.AppendLine("- Beta is vs BTC only, not a multi-factor market.");
        sb.AppendLine("- OI 2y, liquidations, depth, predicted funding: DATA_UNAVAILABLE.");
        sb.AppendLine("- Holdout is chronological 20% in the same process; parameters were not edited after OOS.");
        sb.AppendLine();
        sb.AppendLine("## 14. Further research (only if data exists)");
        sb.AppendLine();
        sb.AppendLine("- If CS IC is flat: the missing input is probably **full-universe ranks**, **OI**, or **liquidation/flow**, not another RSI period.");
        sb.AppendLine("- Do not raise Isolated 0.5%/3x to manufacture PF.");
        sb.AppendLine("- Do not enable LIVE.");
        sb.AppendLine();
        sb.AppendLine("## Run notes");
        foreach (var note in result.Notes.Concat(runNotes))
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    public static void WriteArtifacts(string directory, Wave3SignalResult result)
    {
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        });
        File.WriteAllText(Path.Combine(directory, "wave3-signals.json"), json);
    }

    private static string Num(double value) =>
        double.IsNaN(value) ? "n/a" : value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Pct(double value) =>
        double.IsNaN(value) ? "n/a" : value.ToString("0.00%", CultureInfo.InvariantCulture);
}
