using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.StrategyValidation;

if (args.Any(a => string.Equals(a, "--benchmark", StringComparison.OrdinalIgnoreCase)))
{
    ValidationBenchmark.Run(output: Console.Out);
    return 0;
}

var smoke = args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase));
var root = FindRepoRoot();
var cacheDir = Path.Combine(root, "artifacts", "strategy-validation-cache");
var jobDir = Path.Combine(cacheDir, "job-b");
Directory.CreateDirectory(cacheDir);
Directory.CreateDirectory(jobDir);
var reportPath = Path.Combine(jobDir, "strategy-audit-report.md");
var publishedReportPath = Path.Combine(root, "docs", "strategy-audit-report-model-b.md");
var checkpointPath = Path.Combine(jobDir, "checkpoint.json");
var perfPath = Path.Combine(jobDir, "perf.jsonl");
var failuresPath = Path.Combine(jobDir, "failures.jsonl");
var end = DateTimeOffset.UtcNow;
var start = end.AddYears(-2);
var maxParallel = Math.Max(1, ParseInt(args, "--max-parallel-datasets", ParseInt(args, "--parallel", 2)));

using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformStrategyValidation/1.0");

var coins = smoke
    ? new[] { "BTCUSDT" }
    : await DiscoverUniverseAsync(http, cacheDir);
var timeframes = smoke ? new[] { "1h" } : StrategyValidation.Timeframes;
var datasets = coins.SelectMany(symbol => timeframes.Select(tf => (Symbol: symbol, Timeframe: tf))).ToArray();
var fetchNotes = new List<string>
{
    smoke
        ? "Harness mode: smoke (BTCUSDT 1h). Discovery is unused."
        : $"Harness mode: discovered universe × {string.Join("/", timeframes)} × LONG+SHORT × all templates × IS/VAL/OOS × walk-forward. Candle cache is reused. Checkpoint resume is on. MaxParallelDatasets={maxParallel}."
};
fetchNotes.Add("Indicator model: B (causal full-history indicators, independent execution windows). Model A slice-reseeded checkpoints in job/ are stale and must not be resumed.");

var checkpoint = LoadCheckpoint(checkpointPath);
var pending = datasets
    .Where(ds => !IsDone(checkpoint, ds.Symbol, ds.Timeframe))
    .ToArray();
Console.WriteLine($"Model B validation. IndicatorModel=B. Checkpoint {jobDir}. EXCLUDING_FUNDING. LIVE disabled. MaxParallelDatasets={maxParallel}.");
Console.WriteLine($"Validation universe: {coins.Length} coins, {datasets.Length} datasets, {checkpoint.Done.Count} checkpoint keys, {pending.Length} remaining, MaxParallelDatasets={maxParallel}.");

var jobSw = Stopwatch.StartNew();
var mergeLock = new object();
var limiter = new SemaphoreSlim(maxParallel, maxParallel);
var completed = datasets.Length - pending.Length;
long totalBars = 0;
long cacheHits = 0;
long cacheMisses = 0;
long downloaded = 0;
var workers = new List<Task>();

foreach (var dataset in pending)
{
    await limiter.WaitAsync();
    workers.Add(Task.Run(async () =>
    {
        var dsSw = Stopwatch.StartNew();
        try
        {
            IReadOnlyList<MarketCandle> candles = [];
            var hit = false;
            var got = 0;
            Exception? last = null;
            for (var attempt = 1; attempt <= 8; attempt++)
            {
                try
                {
                    (candles, hit, got) = await KlineDiskCache.LoadAsync(http, cacheDir, dataset.Symbol, dataset.Timeframe, start, end);
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    var status = ex.Message;
                    var rateLimited = status.Contains("418", StringComparison.Ordinal) || status.Contains("429", StringComparison.Ordinal);
                    var delay = rateLimited ? 5_000 * attempt : 250 * attempt;
                    await Task.Delay(delay);
                }
            }

            if (last is not null)
            {
                throw last;
            }

            var span = candles.Count == 0
                ? "empty"
                : $"{candles[0].OpenTime:yyyy-MM-dd} → {candles[^1].CloseTime:yyyy-MM-dd} ({candles.Count} bars)";
            var series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>
            {
                [(dataset.Symbol, dataset.Timeframe)] = candles
            };
            var evalSw = Stopwatch.StartNew();
            var slice = StrategyValidationRunner.Evaluate(series, implementationOk: true);
            evalSw.Stop();
            dsSw.Stop();
            var trades = slice.Sum(r => r.Combined?.NumberOfTrades ?? 0);
            var cps = evalSw.Elapsed.TotalSeconds <= 0 ? 0 : candles.Count / evalSw.Elapsed.TotalSeconds;
            var line =
                $"{dataset.Symbol} {dataset.Timeframe}: {span}; eval {evalSw.ElapsedMilliseconds} ms; {cps:0} bars/s; trades {trades}; cache {(hit ? "hit" : "miss")}; downloaded {got}";
            Console.WriteLine(line);

            lock (mergeLock)
            {
                fetchNotes.Add(line);
                MergeInto(checkpoint, slice);
                checkpoint.Done.Add($"{dataset.Symbol}|{dataset.Timeframe}");
                SaveCheckpoint(checkpointPath, checkpoint);
                completed++;
                totalBars += candles.Count;
                if (hit)
                {
                    cacheHits++;
                }
                else
                {
                    cacheMisses++;
                }

                downloaded += got;
                var elapsed = jobSw.Elapsed;
                var remaining = datasets.Length - completed;
                var eta = completed <= 0
                    ? TimeSpan.Zero
                    : TimeSpan.FromTicks(elapsed.Ticks / completed * Math.Max(0, remaining));
                Console.WriteLine($"progress {completed}/{datasets.Length} elapsed {elapsed:hh\\:mm\\:ss} eta {eta:hh\\:mm\\:ss}");
                File.AppendAllText(
                    perfPath,
                    $"{{\"symbol\":\"{dataset.Symbol}\",\"timeframe\":\"{dataset.Timeframe}\",\"bars\":{candles.Count},\"evalMs\":{evalSw.ElapsedMilliseconds},\"trades\":{trades},\"cacheHit\":{(hit ? "true" : "false")}}}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            lock (mergeLock)
            {
                fetchNotes.Add($"{dataset.Symbol} {dataset.Timeframe}: failed ({ex.Message}). Will retry on resume.");
                File.AppendAllText(
                    failuresPath,
                    $"{{\"symbol\":\"{dataset.Symbol}\",\"timeframe\":\"{dataset.Timeframe}\",\"error\":{JsonSerializer.Serialize(ex.Message)}}}{Environment.NewLine}");
            }

            Console.WriteLine($"{dataset.Symbol} {dataset.Timeframe}: FAILED {ex.Message}");
        }
        finally
        {
            limiter.Release();
        }
    }));
}

await Task.WhenAll(workers);
jobSw.Stop();
var process = Process.GetCurrentProcess();
process.Refresh();
fetchNotes.Add($"Job runtime {jobSw.Elapsed}. Datasets completed {completed}/{datasets.Length}. Bars loaded {totalBars}. Cache hits {cacheHits}, misses {cacheMisses}, klines downloaded {downloaded}. Peak WS {process.WorkingSet64 / (1024 * 1024)} MB. DB operations 0. Network only for uncached klines + exchangeInfo.");

var rows = checkpoint.Templates.Values.OrderBy(r => r.TemplateKey).ToList();
var extra = string.Join(Environment.NewLine, fetchNotes.Distinct().Select(n => $"- {n}"));
var markdown = StrategyValidation.RenderReport(rows, extra);
await File.WriteAllTextAsync(reportPath, markdown);
await File.WriteAllTextAsync(publishedReportPath, markdown);
Console.WriteLine($"Wrote {reportPath}");
Console.WriteLine($"Wrote {publishedReportPath}");
return 0;

static bool IsDone(Checkpoint checkpoint, string symbol, string timeframe) =>
    checkpoint.Done.Contains($"{symbol}|{timeframe}", StringComparer.OrdinalIgnoreCase)
    || checkpoint.Done.Contains(symbol, StringComparer.OrdinalIgnoreCase);

static int ParseInt(string[] args, string key, int fallback)
{
    var i = Array.FindIndex(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
    if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var value) && value > 0)
    {
        return value;
    }

    return fallback;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "TradingPlatform.slnx")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static async Task<string[]> DiscoverUniverseAsync(HttpClient http, string cacheDir)
{
    var cache = Path.Combine(cacheDir, "universe.json");
    try
    {
        using var response = await http.GetAsync("fapi/v1/exchangeInfo");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var contracts = UsdtPerpetualContractRules.MapExchangeInfo(doc.RootElement);
        var symbols = contracts.Select(c => c.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToArray();
        if (symbols.Length > 0)
        {
            await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(symbols));
            return symbols;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"exchangeInfo fetch failed ({ex.Message}); using cached universe if present.");
    }

    if (File.Exists(cache))
    {
        return JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(cache)) ?? [];
    }

    throw new InvalidOperationException("Could not discover the Binance USD-M USDT perpetual universe.");
}

static Checkpoint LoadCheckpoint(string path)
{
    if (!File.Exists(path))
    {
        return new Checkpoint();
    }

    try
    {
        var loaded = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(path)) ?? new Checkpoint();
        if (!string.Equals(loaded.IndicatorModel, "B", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Ignoring stale Model A checkpoint. Full-universe validation must be rerun under Model B.");
            return new Checkpoint { IndicatorModel = "B" };
        }

        loaded.IndicatorModel = "B";
        return loaded;
    }
    catch
    {
        return new Checkpoint();
    }
}

static void SaveCheckpoint(string path, Checkpoint checkpoint)
{
    var json = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = false });
    File.WriteAllText(path, json);
}

static void MergeInto(Checkpoint checkpoint, IReadOnlyList<TemplateValidationResult> slice)
{
    foreach (var row in slice)
    {
        if (checkpoint.Templates.TryGetValue(row.TemplateKey, out var existing))
        {
            checkpoint.Templates[row.TemplateKey] = StrategyValidationRunner.MergeTemplates(existing, row);
        }
        else
        {
            checkpoint.Templates[row.TemplateKey] = row;
        }
    }
}

internal sealed class Checkpoint
{
    public string IndicatorModel { get; set; } = "B";
    public List<string> Done { get; set; } = [];
    public Dictionary<string, TemplateValidationResult> Templates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
