using System.Globalization;
using System.Text.Json;
using TradingPlatform.Domain.Market;

namespace TradingPlatform.StrategyResearch;

internal static class ResearchKlineCache
{
    internal const int PageSize = 1500;
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    internal sealed class CachedBar
    {
        public DateTimeOffset OpenTime { get; set; }
        public DateTimeOffset CloseTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public decimal TakerBuyVolume { get; set; }
    }

    public static async Task<(IReadOnlyList<MarketCandle> Candles, bool CacheHit, int Downloaded)> LoadAsync(
        HttpClient http,
        string cacheDir,
        string symbol,
        string timeframe,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default,
        bool requireTaker = false,
        bool strictCoverage = false)
    {
        Directory.CreateDirectory(cacheDir);
        var path = Path.Combine(cacheDir, $"{symbol}_{timeframe}.json");
        var existing = await ReadAsync(path);
        var interval = IntervalMs(timeframe);
        var downloaded = 0;
        List<MarketCandle> merged = existing.Count == 0
            ? []
            : existing.Select(ToCandle).OrderBy(c => c.OpenTime).ToList();
        var takerOk = TakerCoverage(merged) >= 0.8;

        if (requireTaker && !takerOk)
        {
            merged = await DownloadAsync(http, symbol, timeframe, start, end, cancellationToken);
            downloaded = merged.Count;
            var closedFresh = ClosedUnique(merged);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(closedFresh.Select(FromCandle).ToList()), cancellationToken);
            return (Slice(closedFresh, start, end), false, downloaded);
        }

        var headCovered = strictCoverage
            ? merged.Count > 0 && merged[0].OpenTime <= start
            : merged.Count > 0 && merged[0].OpenTime <= start.AddMilliseconds(interval * 2);
        var tailCovered = strictCoverage
            ? merged.Count > 0 && merged[^1].CloseTime >= end
            : merged.Count > 0 && merged[^1].CloseTime >= end.AddMilliseconds(-interval * 2);
        var windowGaps = strictCoverage
            ? GapCount(Slice(merged, start, end).Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList(), timeframe)
            : 0;
        if (merged.Count >= 80
            && headCovered
            && tailCovered
            && (!requireTaker || takerOk)
            && windowGaps == 0)
        {
            return (Slice(merged, start, end), true, 0);
        }

        if (merged.Count > 0
            && (strictCoverage
                ? merged[0].OpenTime > start
                : merged[0].OpenTime > start.AddMilliseconds(interval * 2)))
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
        else if (strictCoverage
            ? merged[^1].CloseTime < end
            : merged[^1].CloseTime < end.AddMilliseconds(-interval * 2))
        {
            var tail = await DownloadAsync(http, symbol, timeframe, merged[^1].CloseTime.AddMilliseconds(1), end, cancellationToken);
            downloaded += tail.Count;
            merged = Merge(merged, tail);
        }

        if (strictCoverage && merged.Count > 1)
        {
            for (var hole = 0; hole < 6; hole++)
            {
                merged = merged.OrderBy(c => c.OpenTime).ToList();
                var found = false;
                for (var i = 1; i < merged.Count; i++)
                {
                    var delta = merged[i].OpenTime.ToUnixTimeMilliseconds() - merged[i - 1].OpenTime.ToUnixTimeMilliseconds();
                    if (delta <= interval + 1)
                    {
                        continue;
                    }

                    var holeStart = merged[i - 1].CloseTime.AddMilliseconds(1);
                    var holeEnd = merged[i].OpenTime.AddMilliseconds(-1);
                    if (holeEnd <= holeStart)
                    {
                        continue;
                    }

                    var patch = await DownloadAsync(http, symbol, timeframe, holeStart, holeEnd, cancellationToken);
                    downloaded += patch.Count;
                    merged = Merge(merged, patch);
                    found = true;
                    break;
                }

                if (!found)
                {
                    break;
                }
            }
        }

        var closed = ClosedUnique(merged, preferLast: requireTaker);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(closed.Select(FromCandle).ToList()), cancellationToken);
        return (Slice(closed, start, end), downloaded == 0 && existing.Count >= 80 && (!requireTaker || takerOk), downloaded);
    }

    public static async Task<IReadOnlyList<MarketCandle>> ReadCachedAsync(
        string cacheDir,
        string symbol,
        string timeframe)
    {
        var path = Path.Combine(cacheDir, $"{symbol}_{timeframe}.json");
        var cached = await ReadAsync(path);
        return cached.Select(ToCandle).ToList();
    }

    public static double TakerCoverage(IReadOnlyList<MarketCandle> candles) =>
        candles.Count == 0 ? 0 : candles.Count(c => c.TakerBuyVolume > 0m && c.TakerBuyVolume <= c.Volume) / (double)candles.Count;

    private static List<MarketCandle> ClosedUnique(IReadOnlyList<MarketCandle> merged, bool preferLast = false) =>
        merged
            .Where(c => c.IsClosed)
            .GroupBy(c => c.OpenTime)
            .Select(g => preferLast ? g.Last() : g.First())
            .OrderBy(c => c.OpenTime)
            .ToList();

    private static List<MarketCandle> Slice(IReadOnlyList<MarketCandle> candles, DateTimeOffset start, DateTimeOffset end) =>
        candles.Where(c => c.CloseTime >= start && c.OpenTime <= end).ToList();

    public static async Task<List<MarketCandle>> ReadClosedAsync(string path) =>
        (await ReadAsync(path)).Select(ToCandle).ToList();

    private static async Task<List<CachedBar>> ReadAsync(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<CachedBar>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
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

        var cap = timeframe switch
        {
            "1m" => 1_200_000,
            "3m" => 400_000,
            "5m" => 250_000,
            "15m" => 80_000,
            "30m" => 50_000,
            _ => 20_000
        };

        while (candles.Count < cap && cursor <= endMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = Math.Min(PageSize, cap - candles.Count);
            var url = $"fapi/v1/klines?symbol={symbol}&interval={timeframe}&startTime={cursor}&endTime={endMs}&limit={remaining}";
            using var response = await GetWithRateLimitAsync(http, url, cancellationToken);
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

        }

        return candles;
    }

    private static async Task<HttpResponseMessage> GetWithRateLimitAsync(
        HttpClient http,
        string url,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? last = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            await RequestGate.WaitAsync(cancellationToken);
            try
            {
                var delay = _nextRequestAt - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                _nextRequestAt = DateTimeOffset.UtcNow.AddMilliseconds(400);
                last?.Dispose();
                last = await http.GetAsync(url, cancellationToken);
            }
            finally
            {
                RequestGate.Release();
            }

            if (last.IsSuccessStatusCode)
            {
                return last;
            }

            var code = (int)last.StatusCode;
            if (code is not (418 or 429))
            {
                last.EnsureSuccessStatusCode();
            }

            var retry = last.Headers.RetryAfter?.Delta
                ?? (code == 418 ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(60));
            await RequestGate.WaitAsync(cancellationToken);
            try
            {
                var retryAt = DateTimeOffset.UtcNow + retry;
                if (_nextRequestAt < retryAt)
                {
                    _nextRequestAt = retryAt;
                }
            }
            finally
            {
                RequestGate.Release();
            }
        }

        last?.EnsureSuccessStatusCode();
        throw new HttpRequestException("Binance kline request failed after rate-limit retries.");
    }

    private static List<MarketCandle> Merge(IReadOnlyList<MarketCandle> left, IReadOnlyList<MarketCandle> right) =>
        left.Concat(right).GroupBy(c => c.OpenTime).Select(g => g.First()).OrderBy(c => c.OpenTime).ToList();

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
                TakerBuyVolume = row.GetArrayLength() > 9 ? Dec(row[9]) : 0m,
                IsClosed = true,
                ExchangeTimestamp = closeTime
            });
        }

        return candles;
    }

    internal static long IntervalMs(string timeframe) => timeframe switch
    {
        "1m" => 60 * 1000,
        "3m" => 3 * 60 * 1000,
        "5m" => 5 * 60 * 1000,
        "15m" => 15 * 60 * 1000,
        "30m" => 30 * 60 * 1000,
        "1h" => 60 * 60 * 1000,
        _ => 60 * 60 * 1000
    };

    internal static int GapCount(IReadOnlyList<MarketCandle> candles, string timeframe)
    {
        if (candles.Count < 2)
        {
            return 0;
        }

        var step = IntervalMs(timeframe);
        var gaps = 0;
        for (var i = 1; i < candles.Count; i++)
        {
            var delta = candles[i].OpenTime.ToUnixTimeMilliseconds() - candles[i - 1].OpenTime.ToUnixTimeMilliseconds();
            if (delta > step + 1)
            {
                gaps++;
            }
        }

        return gaps;
    }

    private static decimal Dec(JsonElement value) =>
        decimal.Parse(value.GetString() ?? value.ToString(), CultureInfo.InvariantCulture);

    private static CachedBar FromCandle(MarketCandle c) => new()
    {
        OpenTime = c.OpenTime,
        CloseTime = c.CloseTime,
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Volume = c.Volume,
        TakerBuyVolume = c.TakerBuyVolume
    };

    private static MarketCandle ToCandle(CachedBar c) => new()
    {
        OpenTime = c.OpenTime,
        CloseTime = c.CloseTime,
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Volume = c.Volume,
        TakerBuyVolume = c.TakerBuyVolume,
        IsClosed = true,
        ExchangeTimestamp = c.CloseTime
    };
}
