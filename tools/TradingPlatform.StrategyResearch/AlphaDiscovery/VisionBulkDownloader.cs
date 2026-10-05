using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;

namespace TradingPlatform.StrategyResearch.AlphaDiscovery;

/// <summary>
/// Downloads Binance Vision USD-M archives for every crypto USDT perpetual Vision has published, including
/// delisted contracts: monthly 1h klines, monthly funding, and daily 5m metrics. Files already on disk are kept.
/// Keys come from the bucket listing, so no URL is guessed.
/// </summary>
public static class VisionBulkDownloader
{
    private const string Bucket = "https://s3-ap-northeast-1.amazonaws.com/data.binance.vision";
    private const string Files = "https://data.binance.vision/";

    public static readonly string[] ExcludedBases =
        ["USDC", "FDUSD", "TUSD", "BUSD", "USDP", "DAI", "USDE", "USD1", "RLUSD", "BTCDOM", "DEFI", "ALL", "FOOTBALL", "BLUEBIRD"];

    public static async Task<int> RunAsync(string root, string what, int parallel, CancellationToken ct)
    {
        var dir = Path.Combine(root, "artifacts", "data", "vision");
        var symbols = CryptoPerpetuals(dir);
        Console.WriteLine($"Crypto USDT perpetuals: {symbols.Count}");
        using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = parallel, AutomaticDecompression = DecompressionMethods.None })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        var kinds = what.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var manifest = new ConcurrentDictionary<string, object>();
        foreach (var kind in kinds)
        {
            var (prefix, local, from, to) = kind switch
            {
                "klines" => ("data/futures/um/monthly/klines/{0}/1h/", "klines-1h", "2022-01", "2026-09"),
                "funding" => ("data/futures/um/monthly/fundingRate/{0}/", "funding", "2022-01", "2026-09"),
                "metrics" => ("data/futures/um/daily/metrics/{0}/", "metrics", "2024-01-01", "2026-09-30"),
                _ => throw new ArgumentException($"Unknown kind {kind}.")
            };

            var sw = Stopwatch.StartNew();
            var keys = new ConcurrentBag<(string Symbol, string Key)>();
            await Parallel.ForEachAsync(symbols, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(16, parallel), CancellationToken = ct }, async (symbol, token) =>
            {
                foreach (var key in await ListAsync(http, string.Format(prefix, symbol), token))
                {
                    if (!key.EndsWith(".zip", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var stamp = Stamp(key);
                    if (stamp is null || string.CompareOrdinal(stamp, from) < 0 || string.CompareOrdinal(stamp, to + "~") > 0)
                    {
                        continue;
                    }

                    keys.Add((symbol, key));
                }
            });
            Console.WriteLine($"{kind}: {keys.Count} files listed in {sw.Elapsed:mm\\:ss}.");

            var done = 0;
            var fetched = 0;
            var failed = new ConcurrentBag<string>();
            await Parallel.ForEachAsync(keys, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = ct }, async (item, token) =>
            {
                var path = Path.Combine(dir, local, item.Symbol, Path.GetFileName(item.Key));
                if (!File.Exists(path) || new FileInfo(path).Length < 32)
                {
                    if (await FetchAsync(http, item.Key, path, token))
                    {
                        Interlocked.Increment(ref fetched);
                    }
                    else
                    {
                        failed.Add(item.Key);
                    }
                }

                var n = Interlocked.Increment(ref done);
                if (n % 5000 == 0)
                {
                    Console.WriteLine($"  {kind} {n}/{keys.Count} ({fetched} fetched) {sw.Elapsed:hh\\:mm\\:ss}");
                }
            });

            manifest[kind] = new
            {
                prefix,
                from,
                to,
                listed = keys.Count,
                fetched,
                failed = failed.Count,
                failedKeys = failed.Take(200).ToArray(),
                symbols = keys.Select(k => k.Symbol).Distinct().Count(),
                elapsed = sw.Elapsed.ToString()
            };
            Console.WriteLine($"{kind}: done, {fetched} fetched, {failed.Count} failed, {sw.Elapsed:hh\\:mm\\:ss}.");
        }

        var manifestPath = Path.Combine(dir, $"manifest-{string.Join("-", kinds)}.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            at = DateTimeOffset.UtcNow,
            symbols = symbols.Count,
            kinds = manifest
        }, new JsonSerializerOptions { WriteIndented = true }), ct);
        Console.WriteLine($"Wrote {manifestPath}");
        return 0;
    }

    /// <summary>
    /// Vision symbol list filtered to crypto: exchangeInfo underlyingType COIN, or absent from exchangeInfo
    /// (delisted before the snapshot, which predates the TradFi listings). ASCII names only.
    /// </summary>
    public static List<string> CryptoPerpetuals(string visionDir)
    {
        var listed = File.ReadAllLines(Path.Combine(visionDir, "symbols-usdt-perp.txt"))
            .Select(s => s.Trim())
            .Where(s => s.Length > 4 && s.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            .ToHashSet(StringComparer.Ordinal);
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        var info = Directory.GetFiles(visionDir, "exchangeInfo-*.json").OrderBy(f => f).LastOrDefault();
        if (info is not null)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(info));
            foreach (var s in doc.RootElement.GetProperty("symbols").EnumerateArray())
            {
                types[s.GetProperty("symbol").GetString()!] = s.TryGetProperty("underlyingType", out var t) ? t.GetString() ?? "" : "";
            }
        }

        return listed
            .Where(s => !types.TryGetValue(s, out var t) || t == "COIN")
            .Where(s => !ExcludedBases.Contains(s[..^4], StringComparer.Ordinal))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }

    private static string? Stamp(string key)
    {
        var name = Path.GetFileNameWithoutExtension(key);
        var parts = name.Split('-');
        // SYMBOL-1h-YYYY-MM, SYMBOL-fundingRate-YYYY-MM, SYMBOL-metrics-YYYY-MM-DD
        return parts.Length switch
        {
            >= 5 => $"{parts[^3]}-{parts[^2]}-{parts[^1]}",
            4 => $"{parts[^2]}-{parts[^1]}",
            _ => null
        };
    }

    private static async Task<List<string>> ListAsync(HttpClient http, string prefix, CancellationToken ct)
    {
        var keys = new List<string>();
        var marker = "";
        for (var page = 0; page < 200; page++)
        {
            var url = $"{Bucket}?prefix={Uri.EscapeDataString(prefix)}&marker={Uri.EscapeDataString(marker)}";
            XDocument? doc = null;
            for (var attempt = 1; attempt <= 5 && doc is null; attempt++)
            {
                try
                {
                    doc = XDocument.Parse(await http.GetStringAsync(url, ct));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
                {
                    await Task.Delay(500 * attempt, ct);
                }
            }

            if (doc is null)
            {
                Console.WriteLine($"  listing failed: {prefix}");
                break;
            }

            XNamespace ns = doc.Root!.Name.Namespace;
            var pageKeys = doc.Root.Elements(ns + "Contents").Select(c => c.Element(ns + "Key")!.Value).ToList();
            keys.AddRange(pageKeys);
            var truncated = string.Equals(doc.Root.Element(ns + "IsTruncated")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            if (!truncated || pageKeys.Count == 0)
            {
                break;
            }

            marker = pageKeys[^1];
        }

        return keys;
    }

    private static async Task<bool> FetchAsync(HttpClient http, string key, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(Files + key, ct);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(400 * attempt, ct);
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                var tmp = path + ".part";
                await File.WriteAllBytesAsync(tmp, bytes, ct);
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                await Task.Delay(400 * attempt, ct);
            }
        }

        return false;
    }
}
