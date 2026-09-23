using System.Globalization;
using System.Text.Json;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;

namespace TradingPlatform.StrategyResearch;

internal sealed record AlphaCoverageRow(
    string Symbol,
    string Timeframe,
    DateTimeOffset RequestedFrom,
    DateTimeOffset RequestedTo,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp,
    int BarCount,
    int ExpectedBarCount,
    decimal CoveragePercent,
    int GapCount,
    long MissingBarCount,
    int DuplicateCount,
    int OutOfOrderCount,
    int MisalignedTimestampCount,
    int ImpossibleOhlcCount,
    int NegativeVolumeCount,
    bool ChronologicallyOrdered,
    bool QualityPassed,
    DateTimeOffset? UsableFrom,
    DateTimeOffset? UsableTo,
    int UsableBarCount,
    decimal TakerBuyCoveragePercent,
    int DownloadedBars,
    int DownloadedPages,
    int CacheHits,
    int CacheMisses,
    string Status,
    string Notes);

internal sealed record AlphaDataArtifact(
    string Version,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<string> Symbols,
    IReadOnlyDictionary<string, int> RequestedDaysByTimeframe,
    IReadOnlyList<AlphaCoverageRow> Coverage,
    bool IsPostExpansion,
    string FuturesDataAvailability,
    string Confirmation);

internal static class PriceActionAlphaData
{
    internal const string Version = "pa-alpha-all-tf-v1";
    internal static readonly string[] Timeframes = ["1m", "3m", "5m", "15m", "1h"];
    internal static readonly IReadOnlyDictionary<string, int> RequestedDays =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = 180,
            ["3m"] = 180,
            ["5m"] = 365,
            ["15m"] = 365,
            ["1h"] = 730
        };

    public static async Task<int> AuditAsync(string root, string cacheDir)
    {
        var rows = await AuditExistingAsync(cacheDir);
        WriteArtifact(root, "coverage-pre.json", rows, false);
        WriteCoverageMarkdown(root, "coverage-pre.md", rows, false);
        Print(rows, "Pre-extension");
        return rows.All(x => x.QualityPassed) ? 0 : 3;
    }

    public static async Task<int> ExpandAsync(string root, string cacheDir, string[] args)
    {
        var pre = await AuditExistingAsync(cacheDir);
        WriteArtifact(root, "coverage-pre.json", pre, false);
        WriteCoverageMarkdown(root, "coverage-pre.md", pre, false);

        var maxParallel = Math.Clamp(ParseInt(args, "--max-parallel", 2), 1, 4);
        using var http = new HttpClient
        {
            BaseAddress = new Uri("https://fapi.binance.com/"),
            Timeout = TimeSpan.FromSeconds(120)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformPriceActionAlphaData/1.0");
        using var gate = new SemaphoreSlim(maxParallel, maxParallel);
        var generatedAt = DateTimeOffset.UtcNow;
        var tasks = (
            from symbol in Phase7PriceActionUniverse.Symbols
            from timeframe in Timeframes
            select ExtendOneAsync(http, gate, cacheDir, symbol, timeframe, generatedAt)).ToArray();
        var rows = (await Task.WhenAll(tasks))
            .OrderBy(x => Array.IndexOf(Phase7PriceActionUniverse.Symbols, x.Symbol))
            .ThenBy(x => Array.IndexOf(Timeframes, x.Timeframe))
            .ToArray();

        WriteArtifact(root, "coverage-post.json", rows, true);
        WriteCoverageMarkdown(root, "coverage-post.md", rows, true);
        Print(rows, "Post-extension");
        return rows.All(x => x.QualityPassed && x.CoveragePercent >= 99.5m) ? 0 : 3;
    }

    internal static async Task<IReadOnlyList<AlphaCoverageRow>> AuditExistingAsync(string cacheDir)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var rows = new List<AlphaCoverageRow>(Phase7PriceActionUniverse.Symbols.Length * Timeframes.Length);
        foreach (var symbol in Phase7PriceActionUniverse.Symbols)
        {
            foreach (var timeframe in Timeframes)
            {
                var interval = ResearchKlineCache.IntervalMs(timeframe);
                var (from, to, expected) = Window(timeframe, generatedAt);
                var cached = await ResearchKlineCache.ReadCachedAsync(cacheDir, symbol, timeframe);
                var window = cached
                    .Where(x => x.OpenTime >= from && x.CloseTime <= to)
                    .ToList();
                rows.Add(BuildRow(symbol, timeframe, from, to, expected, window, 0, cacheHits: 1, cacheMisses: 0,
                    "Read-only audit of the existing ResearchKlineCache; no download was attempted."));
            }
        }

        return rows;
    }

    private static async Task<AlphaCoverageRow> ExtendOneAsync(
        HttpClient http,
        SemaphoreSlim gate,
        string cacheDir,
        string symbol,
        string timeframe,
        DateTimeOffset generatedAt)
    {
        await gate.WaitAsync();
        try
        {
            var (from, to, expected) = Window(timeframe, generatedAt);
            try
            {
                var (loaded, hit, downloaded) = await ResearchKlineCache.LoadAsync(
                    http, cacheDir, symbol, timeframe, from, to, strictCoverage: true);
                var window = loaded
                    .Where(x => x.OpenTime >= from && x.CloseTime <= to)
                    .OrderBy(x => x.OpenTime)
                    .ToList();
                return BuildRow(symbol, timeframe, from, to, expected, window, downloaded,
                    hit ? 1 : 0, hit ? 0 : 1,
                    "The existing cache file was reused and extended only at a missing head/tail; no bars were synthesized or substituted.");
            }
            catch (Exception ex)
            {
                return BuildRow(symbol, timeframe, from, to, expected, [], 0, 0, 1,
                    $"DATA_UNAVAILABLE: {ex.Message}");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static AlphaCoverageRow BuildRow(
        string symbol,
        string timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        int expected,
        IReadOnlyList<MarketCandle> candles,
        int downloaded,
        int cacheHits,
        int cacheMisses,
        string notes)
    {
        var interval = ResearchKlineCache.IntervalMs(timeframe);
        var quality = PriceActionDataQuality.Audit(candles, interval);
        var (usableFrom, usableTo, usableBars) = LongestContinuous(candles, interval);
        var coverage = expected == 0 ? 0m : Math.Min(100m, candles.Count * 100m / expected);
        var status = candles.Count == 0
            ? ResearchStatuses.DataUnavailable
            : !quality.QualityPassed
                ? "DATA_QUALITY_FAILED"
                : coverage >= 99.5m ? "FULL_COVERAGE" : "PARTIAL_COVERAGE";
        return new AlphaCoverageRow(
            symbol, timeframe, from, to,
            candles.Count == 0 ? null : candles[0].OpenTime,
            candles.Count == 0 ? null : candles[^1].CloseTime,
            candles.Count, expected, coverage,
            quality.GapSegments, quality.MissingBars, quality.DuplicateTimestamps,
            quality.OutOfOrderTimestamps, quality.MisalignedTimestamps,
            quality.ImpossibleOhlc, quality.NegativeVolume,
            quality.OutOfOrderTimestamps == 0, quality.QualityPassed,
            usableFrom, usableTo, usableBars,
            candles.Count == 0 ? 0m : (decimal)ResearchKlineCache.TakerCoverage(candles) * 100m,
            downloaded,
            downloaded == 0 ? 0 : (int)Math.Ceiling(downloaded / (double)ResearchKlineCache.PageSize),
            cacheHits, cacheMisses, status, notes);
    }

    private static (DateTimeOffset From, DateTimeOffset To, int Expected) Window(
        string timeframe,
        DateTimeOffset generatedAt)
    {
        var interval = ResearchKlineCache.IntervalMs(timeframe);
        var nowMs = generatedAt.ToUnixTimeMilliseconds();
        var lastOpenMs = nowMs - nowMs % interval - interval;
        var to = DateTimeOffset.FromUnixTimeMilliseconds(lastOpenMs + interval - 1);
        var rawStart = to.AddDays(-RequestedDays[timeframe]).ToUnixTimeMilliseconds() + 1;
        var startMs = rawStart - rawStart % interval;
        var from = DateTimeOffset.FromUnixTimeMilliseconds(startMs);
        return (from, to, checked((int)((lastOpenMs - startMs) / interval + 1)));
    }

    private static (DateTimeOffset? From, DateTimeOffset? To, int Bars) LongestContinuous(
        IReadOnlyList<MarketCandle> candles,
        long intervalMs)
    {
        if (candles.Count == 0)
        {
            return (null, null, 0);
        }

        var bestStart = 0;
        var bestLength = 1;
        var currentStart = 0;
        for (var i = 1; i < candles.Count; i++)
        {
            if (candles[i].OpenTime.ToUnixTimeMilliseconds() - candles[i - 1].OpenTime.ToUnixTimeMilliseconds() != intervalMs)
            {
                currentStart = i;
            }

            var length = i - currentStart + 1;
            if (length > bestLength)
            {
                bestStart = currentStart;
                bestLength = length;
            }
        }

        return (candles[bestStart].OpenTime, candles[bestStart + bestLength - 1].CloseTime, bestLength);
    }

    private static void WriteArtifact(
        string root,
        string fileName,
        IReadOnlyList<AlphaCoverageRow> rows,
        bool post)
    {
        var dir = OutputDirectory(root);
        Directory.CreateDirectory(dir);
        var artifact = new AlphaDataArtifact(
            Version,
            DateTimeOffset.UtcNow,
            Phase7PriceActionUniverse.Symbols,
            RequestedDays,
            rows,
            post,
            "OHLCV and kline taker-buy volume are reported where genuinely present. Historical open interest, funding, basis, liquidation and order-book data are DATA_UNAVAILABLE for this study and are not fabricated.",
            Confirmation);
        File.WriteAllText(Path.Combine(dir, fileName), JsonSerializer.Serialize(artifact, JsonOptions()));
    }

    private static void WriteCoverageMarkdown(
        string root,
        string fileName,
        IReadOnlyList<AlphaCoverageRow> rows,
        bool post)
    {
        var lines = new List<string>
        {
            $"# Price Action Alpha {(post ? "post-extension" : "pre-extension")} coverage",
            "",
            Confirmation,
            "",
            "| Coin | TF | First | Last | Bars / expected | Coverage | Gaps | Duplicates | Quality | Usable continuous period |",
            "| --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- | --- |"
        };
        lines.AddRange(rows.Select(x =>
            $"| {x.Symbol} | {x.Timeframe} | {Iso(x.FirstTimestamp)} | {Iso(x.LastTimestamp)} | {x.BarCount} / {x.ExpectedBarCount} | "
            + $"{x.CoveragePercent.ToString("0.000", CultureInfo.InvariantCulture)}% | {x.GapCount} ({x.MissingBarCount} bars) | "
            + $"{x.DuplicateCount} | {x.Status} | {Iso(x.UsableFrom)} → {Iso(x.UsableTo)} ({x.UsableBarCount}) |"));
        File.WriteAllLines(Path.Combine(OutputDirectory(root), fileName), lines);
    }

    private static void Print(IReadOnlyList<AlphaCoverageRow> rows, string label)
    {
        Console.WriteLine($"{label}: rows={rows.Count}. {Confirmation}");
        foreach (var row in rows)
        {
            Console.WriteLine(
                $"{row.Symbol} {row.Timeframe} {row.Status} bars={row.BarCount}/{row.ExpectedBarCount} "
                + $"coverage={row.CoveragePercent:0.000}% gaps={row.GapCount} duplicate={row.DuplicateCount} "
                + $"downloaded={row.DownloadedBars}");
        }
    }

    internal static string OutputDirectory(string root) =>
        Path.Combine(root, "artifacts", "strategy-research", "price-action-alpha");

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
                && int.TryParse(args[i + 1], out var value))
            {
                return value;
            }
        }

        return fallback;
    }

    private static string Iso(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture) ?? "—";

    internal const string Confirmation =
        "LIVE = OFF. SCALPING LIVE = OFF. PRICE ACTION LIVE = OFF. PAPER PROMOTION = OFF. ISOLATED MARGIN UNCHANGED.";
}
