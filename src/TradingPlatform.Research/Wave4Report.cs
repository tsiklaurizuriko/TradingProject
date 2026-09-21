using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingPlatform.Research;

public static class Wave4Report
{
    public static string Render(Wave4SignalResult result, IReadOnlyList<string> runNotes)
    {
        var sb = new StringBuilder();
        var classes = result.Hypotheses.ToDictionary(h => h.Id, h => Wave4SignalResearch.Classify(h, result.Ic), StringComparer.OrdinalIgnoreCase);
        sb.AppendLine("# Model B Wave-4 information research report");
        sb.AppendLine();
        sb.AppendLine("Wave-1/2/3 found no robust Isolated LOW alpha in OHLCV, mechanism entries, relative ranks, or funding extremes.");
        sb.AppendLine("Wave-4 acquired missing market information and tested whether it has predictive value **before** any new trading strategy.");
        sb.AppendLine("Frozen five, FrozenRisk, Isolated LOW ($1,000 / 0.5% / 3x), Model B timing, and LIVE were not changed.");
        sb.AppendLine("No candidate was marked VALIDATED_FOR_PAPER. Parameters were pre-registered. OOS was not used to mutate hypotheses.");
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
        sb.AppendLine("Infrastructure inspected before any new download: `BinancePublicMarketDataClient` (`/fapi/v1/klines`, ticker, exchangeInfo, premiumIndex snapshot); `BinanceFuturesHistoryClient` (settled funding, mark/index klines, `openInterestHist` ~30d); `ResearchKlineCache` / `KlineDiskCache`; `FuturesHistoryCache`. No aggTrade, liquidation, or depth client existed.");
        sb.AppendLine();

        sb.AppendLine("## 2. New datasets acquired");
        sb.AppendLine();
        sb.AppendLine("- **Taker buy base volume** re-downloaded from `GET /fapi/v1/klines` field 9 into a **versioned** Wave-4 kline cache. The existing `artifacts/strategy-validation-cache` was not overwritten.");
        sb.AppendLine("- **Open interest and positioning ratios** from Binance Vision `data/futures/um/daily/metrics/{symbol}` (5-minute CSV inside daily zip).");
        sb.AppendLine("- Settled **funding** reused from the existing 2-year `FuturesHistoryCache` ingest.");
        sb.AppendLine("- USD-M **liquidations** were not acquired (archive gone).");
        sb.AppendLine();

        sb.AppendLine("## 3. Exact source and coverage");
        sb.AppendLine();
        sb.AppendLine("| Dataset | Source | Resolution | Window | Notes |");
        sb.AppendLine("|---|---|---|---|---|");
        sb.AppendLine("| OHLCV + taker field 9 | `https://fapi.binance.com/fapi/v1/klines` | 1h | 2024-09-18 → 2026-09-19 | 10 liquid coins; versioned cache `artifacts/strategy-research/wave-4/klines` |");
        sb.AppendLine("| Open interest | `https://data.binance.vision/data/futures/um/daily/metrics/` column `sum_open_interest` | 5m, last print ≤ candle close | same 10 coins / 2y | Archive exists from 2020-09 for BTCUSDT; not the 30-day REST hist |");
        sb.AppendLine("| Account LS ratio | same metrics zip `count_long_short_ratio` | 5m | same | |");
        sb.AppendLine("| Top-trader LS | `sum_toptrader_long_short_ratio` | 5m | same | |");
        sb.AppendLine("| Metrics taker LS vol | `sum_taker_long_short_vol_ratio` | 5m | same | distinct from kline field 9 |");
        sb.AppendLine("| Funding | `GET /fapi/v1/fundingRate` settled `fundingTime` | ~8h | same | Predicted next rate not used |");
        sb.AppendLine("| Liquidations | Vision `daily/liquidationSnapshot` | — | — | Prefix empty. Binance: data no longer provided after 2024-03-31 |");
        sb.AppendLine("| Depth | Vision `daily/bookDepth` | snapshots | from 2023 | Archive exists (~0.5MB/coin/day). Not ingested this wave |");
        sb.AppendLine();

        sb.AppendLine("## 4. Data quality checks");
        sb.AppendLine();
        sb.AppendLine("Legacy 1h cache (`strategy-validation-cache`): TakerBuyVolume>0 on ~18/17565 BTC bars. Root cause: `KlineDiskCache.Parse` never stored field 9; `ResearchKlineCache` then cache-hit the covered range and did not backfill. Schema did not drop the property — historical values were stored as 0. Field 9 is present on live `/fapi/v1/klines` and was re-downloaded into the versioned cache.");
        sb.AppendLine("OI alignment is last 5m `create_time` ≤ kline `CloseTime` (the 01:00 print is not used on the 00:00–00:59 bar). Missing zips are skipped, not interpolated. Zero taker buy is DATA_UNAVAILABLE, not imbalance 0.");
        sb.AppendLine();
        foreach (var note in result.Coverage)
        {
            sb.AppendLine($"- **{note.Dataset}**: {note.Status}. {note.Source}. {note.Coverage}");
        }

        sb.AppendLine();
        sb.AppendLine("## 5. Taker-flow research");
        sb.AppendLine();
        AppendIc(sb, result, id => id.StartsWith("W4_T", StringComparison.OrdinalIgnoreCase) || id == "W4_M_TAKER_LS" || id == "W4_TO_IMB_OI");
        sb.AppendLine();

        sb.AppendLine("## 6. OI research");
        sb.AppendLine();
        AppendIc(sb, result, id => id.StartsWith("W4_OI", StringComparison.OrdinalIgnoreCase));
        sb.AppendLine();

        sb.AppendLine("## 7. Liquidation research");
        sb.AppendLine();
        sb.AppendLine("**DATA_UNAVAILABLE.** USD-M `data/futures/um/daily/liquidationSnapshot/` currently lists no objects. Binance public-data issue #361: *this data is no longer provided*. COIN-M snapshots are a different contract and were not substituted. REST force-order endpoints are recent-only and were not treated as a 2-year history.");
        sb.AppendLine();

        sb.AppendLine("## 8. Funding + positioning research");
        sb.AppendLine();
        AppendIc(sb, result, id => id is "W4_FO_CROWD_REV" or "W4_M_LS_REV" or "W4_M_TOP_LS_REV");
        sb.AppendLine();

        sb.AppendLine("## 9. Full-universe cross-sectional research");
        sb.AppendLine();
        if (result.UniverseIc.Count == 0)
        {
            sb.AppendLine("Not evaluated in this run (panel not loaded).");
        }
        else
        {
            sb.AppendLine("OHLCV-only presence ranks on the existing 1h universe cache (no taker re-download, no inner join). A coin is ranked only if it has bars at t, t−24, and t+h. Deciles are not claimed; terciles among coins present at t.");
            sb.AppendLine();
            sb.AppendLine("| Hypothesis | Phase | H | n | CS IC | LONG mean | SHORT mean | Spread |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (var row in result.UniverseIc.OrderBy(r => r.HypothesisId).ThenBy(r => r.Phase).ThenBy(r => r.Horizon))
            {
                sb.AppendLine($"| {row.HypothesisId} | {row.Phase} | {row.Horizon} | {row.Observations} | {Num(row.MeanCsIc)} | {Pct(row.LongMean)} | {Pct(row.ShortMean)} | {Pct(row.Spread)} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 10. Incremental information-value analysis");
        sb.AppendLine();
        sb.AppendLine("Baseline = `W4_BASE_REL24` (OHLCV relative-to-BTC). `IcVsBaseline` is CS IC at 24h minus that baseline. A feature that only lifts IS is not useful.");
        sb.AppendLine();
        sb.AppendLine("| Hypothesis | Phase | IC 24h | Spread | Exec spread | IC vs OHLCV baseline |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var row in result.Incremental.OrderBy(r => r.HypothesisId).ThenBy(r => r.Phase))
        {
            sb.AppendLine($"| {row.HypothesisId} | {row.Phase} | {Num(row.Ic24)} | {Pct(row.Spread24)} | {Pct(row.ExecSpread24)} | {Num(row.IcVsBaseline)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 11. Forward-return analysis");
        sb.AppendLine();
        sb.AppendLine("Close-to-close CS IC and tercile spread at 1/2/4/8/12/24h. LONG = top tercile of the signed signal. SHORT = −bottom tercile. MAE/MFE are path min/max vs signal close for the top/bottom tercile at 24h.");
        sb.AppendLine();
        AppendIc(sb, result, _ => true, includeShortHorizons: true);
        sb.AppendLine();
        sb.AppendLine("| Hypothesis | Phase | LONG MAE | LONG MFE | SHORT MAE | SHORT MFE |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var row in result.Path.OrderBy(r => r.HypothesisId).ThenBy(r => r.Phase))
        {
            sb.AppendLine($"| {row.HypothesisId} | {row.Phase} | {Pct(row.LongMae)} | {Pct(row.LongMfe)} | {Pct(row.ShortMae)} | {Pct(row.ShortMfe)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 12. Candidate strategy results");
        sb.AppendLine();
        sb.AppendLine("| Id | Hypothesis | Data | Class |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var h in result.Hypotheses)
        {
            sb.AppendLine($"| {h.Id} | {h.Mechanism} | {h.DataUsed} | **{classes[h.Id]}** |");
        }

        var promising = classes.Values.Count(v => v is "PROMISING" or "ROBUST CANDIDATE");
        sb.AppendLine();
        if (promising == 0)
        {
            sb.AppendLine("No signal cleared IS IC>0.02 with positive tercile spread, same-sign VALIDATION, and execution spread above the 0.12% round-trip hurdle. **Stage 2 Isolated replay was not launched.**");
            sb.AppendLine();
            sb.AppendLine("Per-candidate trading rows (trades / WR / PF / expectancy / OOS PF / drawdown / cost sensitivity) are **not applicable**: no strategy was built.");
        }
        else
        {
            sb.AppendLine("Signals classified PROMISING would be the only ones eligible for Isolated LOW Model B replay. None were auto-promoted to VALIDATED_FOR_PAPER.");
        }

        sb.AppendLine();
        sb.AppendLine("Paired long-strong / short-weak remains a **statistical spread**, not an Isolated Cross book. LOW still sizes each position independently. Single-book replay cannot execute a pair.");
        sb.AppendLine();

        sb.AppendLine("## 13. OOS results");
        sb.AppendLine();
        sb.AppendLine("OOS is the last 20% of the aligned 1h panel. Lookbacks (1/24, vol 20, OI shock 24, terciles) were not edited after seeing OOS.");
        sb.AppendLine();
        foreach (var h in result.Hypotheses)
        {
            var oos = result.Ic.FirstOrDefault(r => r.HypothesisId == h.Id && r.Phase == "OOS" && r.Horizon == 24);
            if (oos is null)
            {
                continue;
            }

            sb.AppendLine($"- **{h.Id}** OOS 24h: CS IC {Num(oos.MeanCsIc)}; LONG {Pct(oos.LongMean)}; SHORT {Pct(oos.ShortMean)}; exec spread {Pct(oos.MeanExecSpread)}; **{classes[h.Id]}**.");
        }

        sb.AppendLine();
        sb.AppendLine("## 14. Cost sensitivity");
        sb.AppendLine();
        sb.AppendLine($"Exec spread is next-open → close[t+h] top-minus-bottom. BASE round-trip is {Wave4SignalResearch.RoundTripCost.ToString("P2", CultureInfo.InvariantCulture)} (0.04%×2 commission + 0.02%×2 slippage). +25% / +50% / +100% cost stress applies only after a signal clears BASE. None did, so cost-grid Isolated books were not run.");
        sb.AppendLine();

        sb.AppendLine("## 15. Walk-forward results");
        sb.AppendLine();
        sb.AppendLine("Not run. Walk-forward and 1,584-book validation are gated on PROMISING information-value plus a constructed Isolated strategy.");
        sb.AppendLine();

        sb.AppendLine("## 16. Full 1,584-book validation for survivors");
        sb.AppendLine();
        sb.AppendLine("Not run. No survivor.");
        sb.AppendLine();

        sb.AppendLine("## 17. Final conclusion");
        sb.AppendLine();
        if (promising == 0)
        {
            sb.AppendLine("**NO ROBUST ALPHA FOUND**");
            sb.AppendLine();
            sb.AppendLine("Available market data does not currently demonstrate a robust tradable alpha under Model B costs and LOW Isolated risk.");
            sb.AppendLine("Taker flow and Vision OI/LS were tested as information, not as another RSI combination. Machine learning was not applied (no stable individual feature). Strategy generation is stopped at this gate.");
        }
        else
        {
            sb.AppendLine("At least one signal is PROMISING at the information-value gate. Isolated LOW Model B construction remains a separate step and was not auto-started.");
        }

        var fragile = classes.Where(kv => kv.Value == "FRAGILE").Select(kv => kv.Key).ToList();
        if (fragile.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("FRAGILE means IS/VAL/OOS IC signs can agree but the execution spread does not clear BASE costs on OOS, or OOS confirmation is incomplete. That is not a ROBUST CANDIDATE and Isolated replay was not launched.");
        }

        sb.AppendLine();
        sb.AppendLine("### Failure reasons / data limitations");
        sb.AppendLine();
        sb.AppendLine("- 10 mega-caps are not the 528-coin rank universe; Phase 8 used the existing 1h cache when loaded.");
        sb.AppendLine("- Inner join drops hours missing any of the 10 coins.");
        sb.AppendLine("- Liquidations: DATA_UNAVAILABLE (Binance stopped USD-M snapshots).");
        sb.AppendLine("- Predicted funding and live depth snapshots are not historical series.");
        sb.AppendLine("- Do not raise Isolated 0.5%/3x to manufacture PF. Do not enable LIVE.");
        sb.AppendLine();
        sb.AppendLine("## Run notes");
        foreach (var note in result.Notes.Concat(runNotes))
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    public static void WriteArtifacts(string directory, Wave4SignalResult result)
    {
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        });
        File.WriteAllText(Path.Combine(directory, "wave4-signals.json"), json);
    }

    private static void AppendIc(StringBuilder sb, Wave4SignalResult result, Func<string, bool> filter, bool includeShortHorizons = false)
    {
        sb.AppendLine("| Hypothesis | Phase | H | n | CS IC | TS IC | LONG mean | SHORT mean | Spread | Exec spread |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var row in result.Ic.Where(r => filter(r.HypothesisId)).OrderBy(r => r.HypothesisId).ThenBy(r => r.Phase).ThenBy(r => r.Horizon))
        {
            if (!includeShortHorizons && row.Horizon is not (4 or 8 or 24))
            {
                continue;
            }

            if (includeShortHorizons && row.Horizon is not (1 or 4 or 8 or 24))
            {
                continue;
            }

            sb.AppendLine($"| {row.HypothesisId} | {row.Phase} | {row.Horizon} | {row.Observations} | {Num(row.MeanCsIc)} | {Num(row.MeanTsIc)} | {Pct(row.LongMean)} | {Pct(row.ShortMean)} | {Pct(row.Spread)} | {Pct(row.MeanExecSpread)} |");
        }
    }

    private static string Num(double value) =>
        double.IsNaN(value) ? "n/a" : value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Pct(double value) =>
        double.IsNaN(value) ? "n/a" : value.ToString("0.00%", CultureInfo.InvariantCulture);
}
