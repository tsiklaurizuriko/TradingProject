using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Research;

namespace TradingPlatform.StrategyResearch;

internal sealed record Phase7CoverageRow(
    string Symbol,
    string Timeframe,
    DateTimeOffset RequestedFrom,
    DateTimeOffset RequestedTo,
    DateTimeOffset? ActualFirstBar,
    DateTimeOffset? ActualLastBar,
    int BarCount,
    int ExpectedBarCount,
    int GapCount,
    long MissingBarCount,
    int DuplicateCount,
    int OutOfOrderCount,
    int MisalignedTimestampCount,
    int ImpossibleOhlcCount,
    int NegativeVolumeCount,
    decimal CoveragePercent,
    int DownloadedBars,
    int DownloadedPages,
    int CacheHits,
    int CacheMisses,
    bool Continuous,
    bool QualityPassed,
    string Status,
    string Notes);

internal sealed record Phase7PreAuditRow(
    string Symbol,
    string Timeframe,
    DateTimeOffset? First,
    DateTimeOffset? Last,
    int Bars,
    int GapCount,
    long MissingBarCount,
    int DuplicateCount,
    int OutOfOrderCount,
    int MisalignedTimestampCount,
    int ImpossibleOhlcCount,
    int NegativeVolumeCount,
    bool Continuous,
    bool Has5m,
    bool Has15m,
    string Status);

internal sealed record Phase7UniverseArtifact(
    string Version,
    DateTimeOffset SelectedAtUtc,
    IReadOnlyList<string> Symbols,
    string SelectionBasis,
    bool LiveRestriction);

internal sealed record Phase7ExpansionArtifact(
    string Confirmation,
    DateTimeOffset GeneratedAtUtc,
    int RequestedDays,
    int MaxParallel,
    Phase7UniverseArtifact Universe,
    IReadOnlyList<Phase7PreAuditRow> Before,
    IReadOnlyList<Phase7CoverageRow> Coverage,
    string FuturesDataAvailability,
    bool LiveOff,
    bool ScalpingLiveOff,
    bool PriceActionLiveOff,
    bool ValidatedForPaperAssigned);

internal static class Phase7DataExpansion
{
    private const int RequestedDays = 180;
    private static readonly string[] ResearchTimeframes = ["1m", "3m"];
    private static readonly string[] AuditTimeframes = ["1m", "3m", "5m", "15m"];

    public static async Task<int> AuditAsync(string root, string cacheDir)
    {
        var outDir = OutputDirectory(root);
        Directory.CreateDirectory(outDir);
        var before = await AuditExistingAsync(cacheDir, ScalpingCatalog.Universe);
        var universe = UniverseArtifact();
        await WriteJsonAsync(Path.Combine(outDir, "phase7-universe.json"), universe);
        await WriteJsonAsync(Path.Combine(outDir, "phase7-pre-expansion.json"), before);
        await File.WriteAllTextAsync(
            Path.Combine(outDir, "phase7-pre-expansion.md"),
            PreAuditMarkdown(before, universe));

        Console.WriteLine(Confirmation);
        Console.WriteLine($"Pre-expansion audit rows={before.Count}; selected symbols={universe.Symbols.Count}.");
        foreach (var row in before.Where(x => ResearchTimeframes.Contains(x.Timeframe)))
        {
            Console.WriteLine(
                $"{row.Symbol} {row.Timeframe} bars={row.Bars} first={Iso(row.First)} last={Iso(row.Last)} "
                + $"gaps={row.GapCount} missing={row.MissingBarCount} dup={row.DuplicateCount} continuous={row.Continuous}");
        }

        return 0;
    }

    public static async Task<int> ExpandAsync(string root, string cacheDir, string[] args)
    {
        var outDir = OutputDirectory(root);
        Directory.CreateDirectory(outDir);
        var maxParallel = Math.Clamp(ParseInt(args, "--max-parallel", 2), 1, 4);
        var prePath = Path.Combine(outDir, "phase7-pre-expansion.json");
        IReadOnlyList<Phase7PreAuditRow> before;
        if (File.Exists(prePath))
        {
            before = JsonSerializer.Deserialize<List<Phase7PreAuditRow>>(
                await File.ReadAllTextAsync(prePath),
                JsonOptions()) ?? [];
        }
        else
        {
            before = await AuditExistingAsync(cacheDir, ScalpingCatalog.Universe);
            await WriteJsonAsync(prePath, before);
        }

        var universe = UniverseArtifact();
        await WriteJsonAsync(Path.Combine(outDir, "phase7-universe.json"), universe);
        Console.WriteLine(Confirmation);
        Console.WriteLine($"Phase 7 data-only expansion: {universe.Symbols.Count} coins, 1m/3m, {RequestedDays} days, maxParallel={maxParallel}.");

        using var http = new HttpClient
        {
            BaseAddress = new Uri("https://fapi.binance.com/"),
            Timeout = TimeSpan.FromSeconds(120)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformPhase7DataExpansion/1.0");
        using var gate = new SemaphoreSlim(maxParallel, maxParallel);
        var tasks = (
            from symbol in universe.Symbols
            from timeframe in ResearchTimeframes
            select ExpandOneAsync(http, gate, cacheDir, symbol, timeframe)).ToArray();
        var coverage = (await Task.WhenAll(tasks))
            .Select(row =>
            {
                var previousBars = before.FirstOrDefault(x =>
                    string.Equals(x.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Timeframe, row.Timeframe, StringComparison.OrdinalIgnoreCase))?.Bars ?? 0;
                var phaseDownloaded = Math.Max(row.DownloadedBars, Math.Max(0, row.BarCount - previousBars));
                return row with
                {
                    DownloadedBars = phaseDownloaded,
                    DownloadedPages = phaseDownloaded == 0
                        ? row.DownloadedPages
                        : (int)Math.Ceiling(phaseDownloaded / (double)ResearchKlineCache.PageSize)
                };
            })
            .OrderBy(x => Array.IndexOf(universe.Symbols.ToArray(), x.Symbol))
            .ThenBy(x => Array.IndexOf(ResearchTimeframes, x.Timeframe))
            .ToArray();

        var artifact = new Phase7ExpansionArtifact(
            Confirmation,
            DateTimeOffset.UtcNow,
            RequestedDays,
            maxParallel,
            universe,
            before,
            coverage,
            "OHLCV + Binance kline taker-buy volume where genuinely present. Open interest, funding, basis, liquidations and historical order book are DATA_UNAVAILABLE and were not fabricated.",
            true,
            true,
            true,
            false);
        await WriteJsonAsync(Path.Combine(outDir, "phase7-data-expansion.json"), artifact);
        await File.WriteAllTextAsync(
            Path.Combine(root, "docs", "PRICE_ACTION_DATA_EXPANSION_REPORT.md"),
            ExpansionReport(artifact));

        Console.WriteLine($"Wrote {Path.Combine(outDir, "phase7-data-expansion.json")}");
        Console.WriteLine($"Wrote {Path.Combine(root, "docs", "PRICE_ACTION_DATA_EXPANSION_REPORT.md")}");
        Console.WriteLine(Confirmation);
        return coverage.All(x => x.QualityPassed && x.CoveragePercent >= 99.5m) ? 0 : 3;
    }

    private static async Task<Phase7CoverageRow> ExpandOneAsync(
        HttpClient http,
        SemaphoreSlim gate,
        string cacheDir,
        string symbol,
        string timeframe)
    {
        await gate.WaitAsync();
        try
        {
            var interval = ResearchKlineCache.IntervalMs(timeframe);
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var lastOpenMs = nowMs - nowMs % interval - interval;
            var requestedTo = DateTimeOffset.FromUnixTimeMilliseconds(lastOpenMs + interval - 1);
            var rawStart = requestedTo.AddDays(-RequestedDays).ToUnixTimeMilliseconds() + 1;
            var requestedStartMs = rawStart - rawStart % interval;
            var requestedFrom = DateTimeOffset.FromUnixTimeMilliseconds(requestedStartMs);
            var expected = checked((int)((lastOpenMs - requestedStartMs) / interval + 1));

            Exception? last = null;
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    var (loaded, hit, downloaded) = await ResearchKlineCache.LoadAsync(
                        http,
                        cacheDir,
                        symbol,
                        timeframe,
                        requestedFrom,
                        requestedTo,
                        strictCoverage: true);
                    var window = loaded
                        .Where(x => x.OpenTime >= requestedFrom && x.CloseTime <= requestedTo)
                        .OrderBy(x => x.OpenTime)
                        .ToList();
                    var quality = PriceActionDataQuality.Audit(window, interval);
                    var coverage = expected <= 0 ? 0m : Math.Min(100m, window.Count * 100m / expected);
                    var status = window.Count == 0
                        ? ResearchStatuses.DataUnavailable
                        : !quality.QualityPassed
                            ? "DATA_QUALITY_FAILED"
                            : coverage >= 99.5m
                                ? "FULL_COVERAGE"
                                : "PARTIAL_COVERAGE";
                    var row = new Phase7CoverageRow(
                        symbol,
                        timeframe,
                        requestedFrom,
                        requestedTo,
                        window.Count == 0 ? null : window[0].OpenTime,
                        window.Count == 0 ? null : window[^1].CloseTime,
                        window.Count,
                        expected,
                        quality.GapSegments,
                        quality.MissingBars,
                        quality.DuplicateTimestamps,
                        quality.OutOfOrderTimestamps,
                        quality.MisalignedTimestamps,
                        quality.ImpossibleOhlc,
                        quality.NegativeVolume,
                        coverage,
                        downloaded,
                        downloaded == 0 ? 0 : (int)Math.Ceiling(downloaded / (double)ResearchKlineCache.PageSize),
                        hit ? 1 : 0,
                        hit ? 0 : 1,
                        quality.Continuous,
                        quality.QualityPassed,
                        status,
                        "No candles were synthesized or substituted. Existing cache was extended and merged by open timestamp.");
                    Console.WriteLine(
                        $"{symbol} {timeframe} {status} bars={row.BarCount}/{row.ExpectedBarCount} "
                        + $"coverage={row.CoveragePercent:0.000}% gaps={row.GapCount} dup={row.DuplicateCount} "
                        + $"downloaded={row.DownloadedBars} pages={row.DownloadedPages} cache={(hit ? "hit" : "miss")}");
                    return row;
                }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
                }
            }

            return new Phase7CoverageRow(
                symbol,
                timeframe,
                requestedFrom,
                requestedTo,
                null,
                null,
                0,
                expected,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                1,
                false,
                false,
                ResearchStatuses.DataUnavailable,
                last?.Message ?? "Unknown download failure.");
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<IReadOnlyList<Phase7PreAuditRow>> AuditExistingAsync(
        string cacheDir,
        IReadOnlyList<string> symbols)
    {
        var rows = new List<Phase7PreAuditRow>(symbols.Count * AuditTimeframes.Length);
        foreach (var symbol in symbols)
        {
            var has5m = File.Exists(Path.Combine(cacheDir, $"{symbol}_5m.json"));
            var has15m = File.Exists(Path.Combine(cacheDir, $"{symbol}_15m.json"));
            foreach (var timeframe in AuditTimeframes)
            {
                var cached = await ResearchKlineCache.ReadCachedAsync(cacheDir, symbol, timeframe);
                var quality = PriceActionDataQuality.Audit(cached, ResearchKlineCache.IntervalMs(timeframe));
                rows.Add(new Phase7PreAuditRow(
                    symbol,
                    timeframe,
                    cached.Count == 0 ? null : cached.Min(x => x.OpenTime),
                    cached.Count == 0 ? null : cached.Max(x => x.CloseTime),
                    cached.Count,
                    quality.GapSegments,
                    quality.MissingBars,
                    quality.DuplicateTimestamps,
                    quality.OutOfOrderTimestamps,
                    quality.MisalignedTimestamps,
                    quality.ImpossibleOhlc,
                    quality.NegativeVolume,
                    quality.Continuous,
                    has5m,
                    has15m,
                    cached.Count == 0
                        ? ResearchStatuses.DataUnavailable
                        : quality.QualityPassed ? "AVAILABLE" : "DATA_QUALITY_FAILED"));
            }
        }

        return rows;
    }

    private static Phase7UniverseArtifact UniverseArtifact() =>
        new(
            Phase7PriceActionUniverse.Version,
            DateTimeOffset.UtcNow,
            Phase7PriceActionUniverse.Symbols,
            Phase7PriceActionUniverse.SelectionBasis,
            LiveRestriction: false);

    private static string PreAuditMarkdown(
        IReadOnlyList<Phase7PreAuditRow> rows,
        Phase7UniverseArtifact universe)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Phase 7 pre-expansion audit");
        sb.AppendLine();
        sb.AppendLine(Confirmation);
        sb.AppendLine();
        sb.AppendLine($"Selected research-only universe: {string.Join(", ", universe.Symbols)}.");
        sb.AppendLine();
        sb.AppendLine("| Coin | TF | First | Last | Bars | Gaps | Missing | Duplicates | Continuous | 5m | 15m |");
        sb.AppendLine("| --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- |");
        foreach (var row in rows.Where(x => ResearchTimeframes.Contains(x.Timeframe)))
        {
            sb.AppendLine(
                $"| {row.Symbol} | {row.Timeframe} | {Iso(row.First)} | {Iso(row.Last)} | {row.Bars} | "
                + $"{row.GapCount} | {row.MissingBarCount} | {row.DuplicateCount} | {row.Continuous} | {row.Has5m} | {row.Has15m} |");
        }

        sb.AppendLine();
        sb.AppendLine("ResearchKlineCache extension behavior: reads the existing file, downloads only a missing head and/or tail, merges on OpenTime, writes one closed/ordered copy, and never substitutes another timeframe.");
        return sb.ToString();
    }

    private static string ExpansionReport(Phase7ExpansionArtifact artifact)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PRICE ACTION DATA EXPANSION REPORT");
        sb.AppendLine();
        sb.AppendLine(artifact.Confirmation);
        sb.AppendLine();
        sb.AppendLine("## 1. Selected symbols");
        sb.AppendLine();
        sb.AppendLine(string.Join(", ", artifact.Universe.Symbols));
        sb.AppendLine();
        sb.AppendLine(artifact.Universe.SelectionBasis);
        sb.AppendLine("This artifact is research configuration only; it does not restrict LIVE/PAPER trading.");
        sb.AppendLine();
        sb.AppendLine("## 2. Existing coverage before expansion");
        sb.AppendLine();
        sb.AppendLine("| Coin | TF | First | Last | Bars | Gaps | Missing | Duplicates | Continuous | 5m | 15m |");
        sb.AppendLine("| --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- |");
        foreach (var row in artifact.Before.Where(x =>
                     artifact.Universe.Symbols.Contains(x.Symbol)
                     && ResearchTimeframes.Contains(x.Timeframe)))
        {
            sb.AppendLine(
                $"| {row.Symbol} | {row.Timeframe} | {Iso(row.First)} | {Iso(row.Last)} | {row.Bars} | "
                + $"{row.GapCount} | {row.MissingBarCount} | {row.DuplicateCount} | {row.Continuous} | {row.Has5m} | {row.Has15m} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 3–6. Requested and actual 1m/3m coverage");
        sb.AppendLine();
        sb.AppendLine($"Requested approximately {artifact.RequestedDays} days. Expected intervals: 1m=60 seconds, 3m=180 seconds.");
        sb.AppendLine();
        sb.AppendLine("| Coin | TF | Requested from | Actual first | Actual last | Bars / expected | Coverage | Gaps | Missing | Duplicates | Pages | Hit/Miss | Status |");
        sb.AppendLine("| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |");
        foreach (var row in artifact.Coverage)
        {
            sb.AppendLine(
                $"| {row.Symbol} | {row.Timeframe} | {Iso(row.RequestedFrom)} | {Iso(row.ActualFirstBar)} | {Iso(row.ActualLastBar)} | "
                + $"{row.BarCount} / {row.ExpectedBarCount} | {row.CoveragePercent.ToString("0.000", CultureInfo.InvariantCulture)}% | "
                + $"{row.GapCount} | {row.MissingBarCount} | {row.DuplicateCount} | {row.DownloadedPages} | "
                + $"{row.CacheHits}/{row.CacheMisses} | {row.Status} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 7. Missing/gapped data");
        sb.AppendLine();
        var imperfect = artifact.Coverage.Where(x => x.Status != "FULL_COVERAGE").ToArray();
        sb.AppendLine(imperfect.Length == 0
            ? "No missing intervals, duplicates, or unexplained gaps were found in the requested windows."
            : string.Join(Environment.NewLine, imperfect.Select(x =>
                $"- {x.Symbol} {x.Timeframe}: {x.Status}; gaps={x.GapCount}; missing={x.MissingBarCount}; coverage={x.CoveragePercent:0.000}%.")));
        sb.AppendLine();
        sb.AppendLine("## 8. Cache behavior");
        sb.AppendLine();
        sb.AppendLine("The existing ResearchKlineCache was extended in place. Existing bars were retained, missing heads/tails downloaded, and bars merged by OpenTime. Downloaded bars/pages report the minimum Phase-7 expansion implied by the persisted pre-expansion audit; transient rate-limit retries are not counted as data pages. No second store or database table was created. Parallelism was bounded.");
        sb.AppendLine();
        sb.AppendLine("## 9. Data quality");
        sb.AppendLine();
        sb.AppendLine($"Passed rows: {artifact.Coverage.Count(x => x.QualityPassed)}/{artifact.Coverage.Count}. Checks: ordering, duplicates, interval alignment, impossible OHLC, non-negative volume, gaps.");
        sb.AppendLine();
        sb.AppendLine("## 10. Futures-data availability");
        sb.AppendLine();
        sb.AppendLine(artifact.FuturesDataAvailability);
        sb.AppendLine();
        sb.AppendLine("## 11. Causal/leakage status");
        sb.AppendLine();
        sb.AppendLine("This phase performed ingestion and validation only. No indicators, patterns, signals, forward returns, MFE/MAE, OOS tuning, or new strategies were run/created. Existing leakage tests remain intact.");
        sb.AppendLine();
        sb.AppendLine("## 12. Limitations");
        sb.AppendLine();
        sb.AppendLine("- Universe is deliberately limited to 15 high-activity coins from the existing project universe.");
        sb.AppendLine("- Binance may expose taker-buy volume in klines, but OI/funding/basis/liquidations/order book are not inferred from it.");
        sb.AppendLine("- Coverage percentage measures requested closed kline intervals, not exchange tick completeness.");
        sb.AppendLine();
        sb.AppendLine("## 13. Exact commands");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("dotnet run --project tools/TradingPlatform.StrategyResearch -- --phase7-data-audit");
        sb.AppendLine("dotnet run --project tools/TradingPlatform.StrategyResearch -- --phase7-data-expand --max-parallel 2");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## 14–15. Safety confirmation");
        sb.AppendLine();
        sb.AppendLine("- LIVE = OFF");
        sb.AppendLine("- PAPER promotion = OFF; VALIDATED_FOR_PAPER = none");
        sb.AppendLine("- Scalping LIVE = OFF");
        sb.AppendLine("- Price Action LIVE = OFF");
        sb.AppendLine("- Isolated margin unchanged; Cross Margin not introduced");
        sb.AppendLine("- Risk Engine / Portfolio Risk / Execution unchanged");
        sb.AppendLine("- Frozen Five and Isolated LOW parameters unchanged");
        sb.AppendLine("- No new strategies; no OOS tuning; no fabricated data");
        return sb.ToString();
    }

    private static string OutputDirectory(string root) =>
        Path.Combine(root, "artifacts", "strategy-research", "price-action");

    private static async Task WriteJsonAsync<T>(string path, T value) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions()));

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static int ParseInt(string[] args, string name, int fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static string Iso(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture) ?? "—";

    private const string Confirmation =
        "LIVE = OFF. SCALPING LIVE = OFF. PRICE ACTION LIVE = OFF. ISOLATED MARGIN = ENFORCED. NO VALIDATED_FOR_PAPER. DATA ONLY.";
}
