using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

internal static class PumpRideHoldoutRunner
{
    private static readonly DateTimeOffset HoldoutStartUtc = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public static async Task<int> RunAsync(string root, string cacheDir, CancellationToken cancellationToken)
    {
        var outputDir = Path.Combine(root, "artifacts", "research", "audit", "market-data-oos-2026-10");
        Directory.CreateDirectory(outputDir);
        var symbols = Directory.GetFiles(cacheDir, "*_15m.json")
            .Select(path => Path.GetFileName(path).Replace("_15m.json", "", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToList();
        var end = DateTimeOffset.UtcNow;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var failures = new ConcurrentBag<string>();
        var downloaded = 0;

        await Parallel.ForEachAsync(symbols, new ParallelOptions { MaxDegreeOfParallelism = 12, CancellationToken = cancellationToken }, async (symbol, token) =>
        {
            try
            {
                var path = Path.Combine(outputDir, symbol + ".json");
                if (HasCompleteHoldout(path, HoldoutStartUtc, end))
                {
                    Interlocked.Increment(ref downloaded);
                    return;
                }

                var bars = await FetchArchiveAsync(http, symbol, HoldoutStartUtc, end, token);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(bars, new JsonSerializerOptions { WriteIndented = false }), token);
                if (bars.Count > 0)
                {
                    Interlocked.Increment(ref downloaded);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(symbol + ": " + ex.GetType().Name + " " + ex.Message);
            }
        });

        var manifest = new
        {
            DownloadedAtUtc = DateTimeOffset.UtcNow,
            HoldoutStartUtc,
            EndUtc = end,
            Timeframe = "15m",
            RequestedSymbols = symbols.Count,
            SymbolsWithBars = downloaded,
            FailedSymbols = failures.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Source = "Binance USD-M public futures klines",
            OutputDirectory = outputDir
        };
        var manifestPath = Path.Combine(outputDir, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        Console.WriteLine($"Downloaded 15m holdout candles for {downloaded}/{symbols.Count} symbols; {failures.Count} symbols failed.");

        if (downloaded == 0)
        {
            return 2;
        }

        var reportPath = Path.Combine(root, "artifacts", "research", "audit", "pump-ride-oos-update-2026-10-22.md");
        return PumpRideStudy.RunLive(root, cacheDir, reportPath, outputDir);
    }

    private static bool HasCompleteHoldout(string path, DateTimeOffset start, DateTimeOffset end)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var bars = JsonSerializer.Deserialize<List<HoldoutBar>>(File.ReadAllText(path));
            if (bars is null || bars.Count < 1900) return false;
            var first = bars.Min(bar => bar.OpenTime);
            var last = bars.Max(bar => bar.OpenTime);
            var lastFullOpen = end.UtcDateTime.Date.AddDays(-1).AddHours(23).AddMinutes(45);
            return first <= start && last >= new DateTimeOffset(lastFullOpen, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<List<HoldoutBar>> FetchArchiveAsync(HttpClient http, string symbol, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var result = new List<HoldoutBar>();
        for (var day = start.UtcDateTime.Date; day < end.UtcDateTime.Date.AddDays(1); day = day.AddDays(1))
        {
            var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var file = $"{symbol}-15m-{date}.zip";
            var url = $"https://s3-ap-northeast-1.amazonaws.com/data.binance.vision/data/futures/um/daily/klines/{Uri.EscapeDataString(symbol)}/15m/{Uri.EscapeDataString(file)}";
            using var response = await GetArchiveAsync(http, url, cancellationToken);
            if (response is null)
            {
                continue; // No archive is normal for symbols that did not trade on this date.
            }

            using var archiveStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(item => item.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                throw new InvalidDataException($"No CSV entry in Binance archive for {symbol} on {date}.");
            }

            using var reader = new StreamReader(entry.Open());
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var values = line.Split(',');
                if (values.Length < 7 || !long.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var openMs))
                {
                    continue; // Ignore a CSV header if the archive includes one.
                }

                var closeMs = long.Parse(values[6], CultureInfo.InvariantCulture);
                if (openMs < start.ToUnixTimeMilliseconds() || closeMs >= end.ToUnixTimeMilliseconds()) continue;
                result.Add(new HoldoutBar(DateTimeOffset.FromUnixTimeMilliseconds(openMs), Parse(values[1]), Parse(values[2]), Parse(values[3]), Parse(values[4]), Parse(values[5])));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        return result.DistinctBy(bar => bar.OpenTime).OrderBy(bar => bar.OpenTime).ToList();
    }

    private static async Task<HttpResponseMessage?> GetArchiveAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var response = await http.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                return new HttpResponseMessage(response.StatusCode) { Content = new ByteArrayContent(bytes) };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if ((int)response.StatusCode != 429 && (int)response.StatusCode != 418 && (int)response.StatusCode < 500)
            {
                throw new HttpRequestException($"Binance archive returned {(int)response.StatusCode} for {url}.");
            }

            var retryAfter = response.Headers.RetryAfter?.Delta;
            await Task.Delay(retryAfter ?? TimeSpan.FromSeconds(Math.Min(30, 2 << attempt)), cancellationToken);
        }

        throw new HttpRequestException($"Binance archive retry limit reached for {url}.");
    }

    private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    private sealed record HoldoutBar(DateTimeOffset OpenTime, double Open, double High, double Low, double Close, double Volume);
}
