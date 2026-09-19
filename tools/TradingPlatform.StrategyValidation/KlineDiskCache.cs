using System.Globalization;
using System.Text.Json;
using TradingPlatform.Domain.Market;

namespace TradingPlatform.StrategyValidation;

internal static class KlineDiskCache
{
    internal sealed record CachedBar(
        DateTimeOffset OpenTime,
        DateTimeOffset CloseTime,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal Volume);

    public static async Task<(IReadOnlyList<MarketCandle> Candles, bool CacheHit, int Downloaded)> LoadAsync(
        HttpClient http,
        string cacheDir,
        string symbol,
        string timeframe,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(cacheDir, $"{symbol}_{timeframe}.json");
        var existing = await ReadAsync(path);
        var interval = IntervalMs(timeframe);
        var needStart = start;
        var needEnd = end;
        var downloaded = 0;
        List<MarketCandle> merged = existing.Count == 0
            ? []
            : existing.Select(ToCandle).OrderBy(c => c.OpenTime).ToList();

        if (merged.Count >= 80
            && merged[0].OpenTime <= start.AddMilliseconds(interval * 2)
            && merged[^1].CloseTime >= end.AddMilliseconds(-interval * 2))
        {
            return (merged, true, 0);
        }

        if (merged.Count > 0 && merged[0].OpenTime > start.AddMilliseconds(interval * 2))
        {
            var head = await DownloadAsync(http, symbol, timeframe, start, merged[0].OpenTime.AddMilliseconds(-1), cancellationToken);
            downloaded += head.Count;
            merged = Merge(head, merged);
        }

        if (merged.Count == 0)
        {
            merged = await DownloadAsync(http, symbol, timeframe, start, end, cancellationToken);
            downloaded += merged.Count;
        }
        else if (merged[^1].CloseTime < end.AddMilliseconds(-interval * 2))
        {
            var tail = await DownloadAsync(http, symbol, timeframe, merged[^1].CloseTime.AddMilliseconds(1), end, cancellationToken);
            downloaded += tail.Count;
            merged = Merge(merged, tail);
        }

        var closed = merged
            .Where(c => c.IsClosed)
            .GroupBy(c => c.OpenTime)
            .Select(g => g.First())
            .OrderBy(c => c.OpenTime)
            .ToList();
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(closed.Select(FromCandle).ToList()), cancellationToken);
        return (closed, downloaded == 0 && existing.Count >= 80, downloaded);
    }

    private static async Task<List<CachedBar>> ReadAsync(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<CachedBar>>(stream) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task<List<MarketCandle>> DownloadAsync(
        HttpClient http,
        string symbol,
        string timeframe,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var candles = new List<MarketCandle>();
        var cursor = start.ToUnixTimeMilliseconds();
        var endMs = end.ToUnixTimeMilliseconds();
        if (cursor > endMs)
        {
            return candles;
        }

        const int page = 1500;
        var cap = timeframe switch
        {
            "5m" => 220_000,
            "15m" => 80_000,
            _ => 20_000
        };

        while (candles.Count < cap && cursor <= endMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = Math.Min(page, cap - candles.Count);
            var url = $"fapi/v1/klines?symbol={symbol}&interval={timeframe}&startTime={cursor}&endTime={endMs}&limit={remaining}";
            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var batch = Parse(doc.RootElement);
            if (batch.Count == 0)
            {
                break;
            }

            candles.AddRange(batch);
            var next = batch[^1].OpenTime.ToUnixTimeMilliseconds() + 1;
            if (next <= cursor)
            {
                break;
            }

            cursor = next;
            if (batch.Count < remaining)
            {
                break;
            }

            await Task.Delay(80, cancellationToken);
        }

        return candles;
    }

    private static List<MarketCandle> Merge(IReadOnlyList<MarketCandle> left, IReadOnlyList<MarketCandle> right) =>
        left.Concat(right)
            .GroupBy(c => c.OpenTime)
            .Select(g => g.First())
            .OrderBy(c => c.OpenTime)
            .ToList();

    private static List<MarketCandle> Parse(JsonElement payload)
    {
        var candles = new List<MarketCandle>();
        var now = DateTimeOffset.UtcNow;
        foreach (var row in payload.EnumerateArray())
        {
            var closeTime = DateTimeOffset.FromUnixTimeMilliseconds(row[6].GetInt64());
            if (closeTime > now)
            {
                continue;
            }

            candles.Add(new MarketCandle
            {
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(row[0].GetInt64()),
                CloseTime = closeTime,
                Open = Dec(row[1]),
                High = Dec(row[2]),
                Low = Dec(row[3]),
                Close = Dec(row[4]),
                Volume = Dec(row[5]),
                IsClosed = true,
                ExchangeTimestamp = closeTime
            });
        }

        return candles;
    }

    private static long IntervalMs(string timeframe) => timeframe switch
    {
        "5m" => 5 * 60 * 1000,
        "15m" => 15 * 60 * 1000,
        "1h" => 60 * 60 * 1000,
        _ => 60 * 60 * 1000
    };

    private static decimal Dec(JsonElement value) =>
        decimal.Parse(value.GetString() ?? value.ToString(), CultureInfo.InvariantCulture);

    private static CachedBar FromCandle(MarketCandle c) =>
        new(c.OpenTime, c.CloseTime, c.Open, c.High, c.Low, c.Close, c.Volume);

    private static MarketCandle ToCandle(CachedBar c) => new()
    {
        OpenTime = c.OpenTime,
        CloseTime = c.CloseTime,
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Volume = c.Volume,
        IsClosed = true,
        ExchangeTimestamp = c.CloseTime
    };
}
