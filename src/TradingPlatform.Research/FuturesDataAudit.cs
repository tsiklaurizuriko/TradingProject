using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public sealed record DatasetCoverage(
    string Dataset,
    string Symbol,
    string Timeframe,
    string Status,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Observations,
    string Notes);

public static class FuturesDataAudit
{
    public static IReadOnlyList<DatasetCoverage> AuditTakerCache(string klineCacheDir, IReadOnlySet<string>? fullScanSymbols = null)
    {
        if (!Directory.Exists(klineCacheDir))
        {
            return [];
        }

        var rows = new List<DatasetCoverage>();
        var universe = new Dictionary<string, (int Files, int WithField, int PositiveOnly)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(klineCacheDir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var split = name.LastIndexOf('_');
            if (split <= 0)
            {
                continue;
            }

            var symbol = name[..split];
            var tf = name[(split + 1)..];
            universe.TryGetValue(tf, out var agg);
            agg.Files++;
            var head = ReadHead(path, 1600);
            var hasField = head.Contains("\"TakerBuyVolume\"", StringComparison.Ordinal);
            if (hasField)
            {
                agg.WithField++;
            }

            var full = fullScanSymbols is null || fullScanSymbols.Contains(symbol);
            if (!full)
            {
                universe[tf] = agg;
                continue;
            }

            var scan = ScanTaker(path);
            if (scan.Positive > 0)
            {
                agg.PositiveOnly++;
            }

            universe[tf] = agg;
            var coverageRatio = scan.Total == 0 ? 0d : (double)scan.Positive / scan.Total;
            var complete = coverageRatio >= 0.95;
            var status = complete ? "AVAILABLE" : ResearchStatuses.DataUnavailable;
            var notes = complete
                ? $"Binance kline index 9 present. Positive taker-buy bars {scan.Positive}/{scan.Total}. TakerSell = Volume - TakerBuy only when buy>0 and buy<=volume."
                : scan.Positive == 0
                    ? hasField
                        ? "TakerBuyVolume field is 0 on scanned bars. Treated as missing, not as zero imbalance. Not fabricated."
                        : "Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated."
                    : $"PARTIAL only. Positive taker-buy bars {scan.Positive}/{scan.Total} ({coverageRatio.ToString("P1", CultureInfo.InvariantCulture)}). First positive {Fmt(scan.FirstPositive)} last positive {Fmt(scan.LastPositive)}. Zeros = DATA_UNAVAILABLE, not imbalance 0.";
            rows.Add(new DatasetCoverage(
                "TakerFlow",
                symbol,
                tf,
                status,
                complete ? scan.From : scan.FirstPositive,
                complete ? scan.To : scan.LastPositive,
                scan.Positive,
                notes));
        }

        foreach (var (tf, agg) in universe.OrderBy(x => x.Key))
        {
            rows.Add(new DatasetCoverage(
                "TakerFlow",
                "UNIVERSE",
                tf,
                agg.WithField == agg.Files && agg.Files > 0 ? "FIELD_PRESENT" : ResearchStatuses.DataUnavailable,
                null,
                null,
                agg.Files,
                $"Kline cache files={agg.Files}; head contains TakerBuyVolume in {agg.WithField}. Full bar scan limited to requested coins. Do not treat FIELD_PRESENT as full historical coverage."));
        }

        return rows.OrderBy(r => r.Symbol == "UNIVERSE" ? "zzz" : r.Symbol).ThenBy(r => r.Timeframe).ToList();
    }

    public static IReadOnlyList<TakerFlowPoint> ExtractTaker(string klineCachePath, string symbol)
    {
        if (!File.Exists(klineCachePath))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(klineCachePath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var rows = new List<TakerFlowPoint>();
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (!TryTime(row, "CloseTime", out var close))
                {
                    continue;
                }

                var volume = Dec(row, "Volume");
                var buy = Dec(row, "TakerBuyVolume");
                var candle = new Domain.Market.MarketCandle
                {
                    Volume = volume,
                    TakerBuyVolume = buy,
                    CloseTime = close,
                    IsClosed = true
                };
                rows.Add(new TakerFlowPoint(symbol, close, volume, buy, TakerFlow.TakerSellVolume(candle), TakerFlow.Imbalance(candle)));
            }

            return rows;
        }
        catch
        {
            return [];
        }
    }

    public static DatasetCoverage Coverage<T>(
        string dataset,
        string symbol,
        string timeframe,
        IReadOnlyList<T> rows,
        Func<T, DateTimeOffset> time,
        string notes)
    {
        if (rows.Count == 0)
        {
            return new DatasetCoverage(dataset, symbol, timeframe, ResearchStatuses.DataUnavailable, null, null, 0, notes);
        }

        return new DatasetCoverage(
            dataset,
            symbol,
            timeframe,
            "AVAILABLE",
            time(rows[0]),
            time(rows[^1]),
            rows.Count,
            notes);
    }

    public static string RenderMarkdown(
        IReadOnlyList<DatasetCoverage> coverage,
        IReadOnlyList<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Futures data availability report");
        sb.AppendLine();
        sb.AppendLine("Phase 2 data infrastructure. No strategy parameters were tuned. LIVE = OFF. No 528-universe strategy run.");
        sb.AppendLine("No missing series was fabricated.");
        sb.AppendLine();
        sb.AppendLine("## Timestamp alignment");
        sb.AppendLine("An observation is visible on candle `i` only when `observation.Timestamp <= candle.CloseTime`.");
        sb.AppendLine("Funding uses **settled** `fundingTime` from `GET /fapi/v1/fundingRate`, not the next predicted rate from `premiumIndex.lastFundingRate`.");
        sb.AppendLine("Mark/index use the **closed** kline `closeTime` and **close** price.");
        sb.AppendLine("Basis = MarkClose − IndexClose. NormalizedBasis = Basis / IndexClose. Same closeTime only; unmatched bars are dropped.");
        sb.AppendLine("Taker flow: Binance kline field 9 = taker buy base volume. TakerSell = Volume − TakerBuy only when buy > 0 and buy ≤ volume. Denominator 0 → DATA_UNAVAILABLE, not 0.");
        sb.AppendLine();
        sb.AppendLine("## Binance sources currently used");
        sb.AppendLine();
        sb.AppendLine("| Source | Endpoint | Symbol | Interval | Timestamp | Pagination | Max / request | Raw or derived | Deterministic replay |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        sb.AppendLine("| OHLCV | `GET /fapi/v1/klines` | `symbol` | 5m/15m/1h | openTime + closeTime | startTime cursor, limit 1500 | 1500 | raw | yes |");
        sb.AppendLine("| Taker buy | kline field 9 | `symbol` | same as kline | kline closeTime | with klines | 1500 | raw | yes when field present |");
        sb.AppendLine("| Taker sell | Volume − field 9 | `symbol` | same as kline | kline closeTime | derived | n/a | derived | yes when buy is valid |");
        sb.AppendLine("| Funding (settled) | `GET /fapi/v1/fundingRate` | `symbol` | settlement (~8h) | `fundingTime` | startTime cursor, limit 1000 | 1000 | raw settled interval rate | yes |");
        sb.AppendLine("| Mark price | `GET /fapi/v1/markPriceKlines` | `symbol` | 5m/15m/1h | closed closeTime | startTime cursor, limit 1000 | 1000 | raw close | yes |");
        sb.AppendLine("| Index price | `GET /fapi/v1/indexPriceKlines` | USDT-M `pair` = symbol | 5m/15m/1h | closed closeTime | startTime cursor, limit 1000 | 1000 | raw close | yes |");
        sb.AppendLine("| Basis | derived | `symbol` | matching mark/index | matching closeTime | n/a | n/a | derived MarkClose−IndexClose | yes |");
        sb.AppendLine("| Open interest hist | `GET /futures/data/openInterestHist` | `symbol` | period 5m/15m/1h | `timestamp` | startTime cursor, limit 500 | 500 | raw `sumOpenInterest` | only latest ~30 days |");
        sb.AppendLine("| Premium snapshot | `GET /fapi/v1/premiumIndex` | all / symbol | n/a | request time | none | snapshot | raw mark + lastFundingRate | **not historical** |");
        sb.AppendLine("| OI snapshot | `GET /fapi/v1/openInterest` | `symbol` | n/a | request time | none | snapshot | raw | **not historical** |");
        sb.AppendLine();
        sb.AppendLine("`BinancePublicMarketDataClient` currently supports: `GET /fapi/v1/klines` (range + latest), `GET /fapi/v1/ticker/price`, `GET /fapi/v1/ticker/24hr`, `GET /fapi/v1/exchangeInfo`, `GET /fapi/v1/ticker/bookTicker`, `GET /fapi/v1/premiumIndex` (snapshot). It does **not** download fundingRate, markPriceKlines, indexPriceKlines, or openInterestHist.");
        sb.AppendLine();
        sb.AppendLine("PostgreSQL `MarketCandles` stores OHLCV. `TakerBuyVolume` is ignored by EF (`TradingModelConfiguration`). Research kline disk cache is `artifacts/strategy-validation-cache/{symbol}_{tf}.json`.");
        sb.AppendLine();
        sb.AppendLine("Rate limits (Binance public USD-M, not independently measured here): klines / fundingRate / mark-index klines are request-weight endpoints; `openInterestHist` is documented as IP weight 0. The research client pauses ~80ms between pages and retries 429.");
        sb.AppendLine();
        sb.AppendLine("## Coverage");
        foreach (var dataset in coverage.Select(c => c.Dataset).Distinct())
        {
            sb.AppendLine($"### {dataset}");
            foreach (var row in coverage.Where(c => c.Dataset == dataset))
            {
                var from = row.FromUtc?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "n/a";
                var to = row.ToUtc?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "n/a";
                sb.AppendLine($"- {row.Symbol} {row.Timeframe}: {row.Status} from {from} to {to} (n={row.Observations}). {row.Notes}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Missing coverage");
        sb.AppendLine("- Historical open interest older than the public ~30-day window: `OI_HISTORICAL_DATA_LIMITATION`. Not fabricated. A longer series would require `EXTERNAL_DATA_SOURCE_REQUIRED`.");
        sb.AppendLine("- Historical liquidation prints: DATA_UNAVAILABLE. Not ingested. Not fabricated.");
        sb.AppendLine("- Historical order book / depth snapshots: DATA_UNAVAILABLE.");
        sb.AppendLine("- Causal pair-universe snapshots: `EXTERNAL_DATA_SOURCE_REQUIRED`.");
        sb.AppendLine("- Predicted next funding (`premiumIndex.lastFundingRate`): not used as a historical series.");
        sb.AppendLine("- Taker buy on older kline cache files that predate the field, and bars where the stored value is 0: DATA_UNAVAILABLE, not imbalance 0.");
        sb.AppendLine();
        sb.AppendLine("## Cache");
        sb.AppendLine("Persistent research cache root: `artifacts/data/futures-history-v1/`.");
        sb.AppendLine("Keys: `{dataset}/{symbol}_{timeframe}.json` plus `funding/{symbol}.json`. Dataset version `futures-history-v1`.");
        sb.AppendLine("Resume: skip Binance when the file already covers the requested start/end. Dedup by timestamp. Checkpoint: `meta/checkpoint.jsonl`.");
        sb.AppendLine();
        sb.AppendLine("## Strategy RequiredData");
        foreach (var key in StrategyTemplateKeys.All)
        {
            sb.AppendLine($"- `{key}`: {string.Join(" + ", StrategyTemplateKeys.RequiredDatasets(key))}");
        }

        sb.AppendLine();
        sb.AppendLine("## Research skip states");
        sb.AppendLine("- `DATA_UNAVAILABLE`: required historical series does not exist for the window (not fabricated, not replaced with zeros).");
        sb.AppendLine("- `INSUFFICIENT_DATA`: series exists but is too short to evaluate (warmup / window).");
        sb.AppendLine("- `IMPLEMENTATION_ERROR`: ingest or evaluation threw.");
        sb.AppendLine("- `NO_TRADES`: evaluation ran and produced zero trades.");
        sb.AppendLine("- `VALIDATION_FAILED` / `OOS_FAILED`: evaluated PF below 1 on that split.");
        sb.AppendLine();
        sb.AppendLine("## Run notes");
        foreach (var note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    public static void WriteJson(string path, IReadOnlyList<DatasetCoverage> coverage)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(coverage, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ReadHead(string path, int bytes)
    {
        using var fs = File.OpenRead(path);
        var buf = new byte[Math.Min(bytes, fs.Length)];
        var n = fs.Read(buf, 0, buf.Length);
        return Encoding.UTF8.GetString(buf, 0, n);
    }

    private static (DateTimeOffset? From, DateTimeOffset? To, DateTimeOffset? FirstPositive, DateTimeOffset? LastPositive, int Positive, int Total) ScanTaker(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (null, null, null, null, 0, 0);
            }

            DateTimeOffset? from = null;
            DateTimeOffset? to = null;
            DateTimeOffset? firstPositive = null;
            DateTimeOffset? lastPositive = null;
            var positive = 0;
            var total = 0;
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                total++;
                if (TryTime(row, "OpenTime", out var open))
                {
                    from ??= open;
                    to = open;
                }

                var buy = Dec(row, "TakerBuyVolume");
                var volume = Dec(row, "Volume");
                if (buy > 0m && volume > 0m && buy <= volume)
                {
                    positive++;
                    if (TryTime(row, "CloseTime", out var close) || TryTime(row, "OpenTime", out close))
                    {
                        firstPositive ??= close;
                        lastPositive = close;
                    }
                }
            }

            return (from, to, firstPositive, lastPositive, positive, total);
        }
        catch
        {
            return (null, null, null, null, 0, 0);
        }
    }

    private static bool TryTime(JsonElement row, string name, out DateTimeOffset time)
    {
        time = default;
        return row.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(el.GetString(), out time);
    }

    private static decimal Dec(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var el))
        {
            return 0m;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDecimal(out var n) => n,
            JsonValueKind.String when decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) => s,
            _ => 0m
        };
    }

    private static string Fmt(DateTimeOffset? time) =>
        time?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "n/a";
}
