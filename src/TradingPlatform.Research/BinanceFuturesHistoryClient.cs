using System.Globalization;
using System.Net;
using System.Text.Json;

namespace TradingPlatform.Research;

public sealed record FundingPoint(string Symbol, DateTimeOffset FundingTime, decimal FundingRate, decimal? MarkPriceAtFunding);

public sealed record OpenInterestPoint(string Symbol, DateTimeOffset Timestamp, decimal OpenInterest, string Period);

public sealed record PricePoint(string Symbol, DateTimeOffset OpenTime, DateTimeOffset CloseTime, decimal Close);

public sealed record BasisPoint(
    string Symbol,
    DateTimeOffset CloseTime,
    decimal MarkPrice,
    decimal IndexPrice,
    decimal AbsoluteBasis,
    decimal NormalizedBasis);

public sealed record TakerFlowPoint(
    string Symbol,
    DateTimeOffset CloseTime,
    decimal Volume,
    decimal TakerBuyVolume,
    decimal? TakerSellVolume,
    decimal? Imbalance);

/// <summary>
/// Public Binance USD-M historical series. FundingRate is the settled interval rate at fundingTime.
/// </summary>
public sealed class BinanceFuturesHistoryClient
{
    public const int FundingPageLimit = 1000;
    public const int PriceKlinePageLimit = 1000;
    public const int OpenInterestPageLimit = 500;
    public const int OpenInterestMaxLookbackDays = 30;
    public const string OpenInterestLimitation = "OI_HISTORICAL_DATA_LIMITATION";

    private readonly HttpClient _http;
    private readonly TimeSpan _pause;
    private readonly int _retries;

    public BinanceFuturesHistoryClient(HttpClient http, TimeSpan? pause = null, int retries = 5)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _pause = pause ?? TimeSpan.FromMilliseconds(80);
        _retries = Math.Max(1, retries);
    }

    public async Task<IReadOnlyList<FundingPoint>> GetFundingAsync(
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<FundingPoint>();
        var cursor = start.ToUnixTimeMilliseconds();
        var endMs = end.ToUnixTimeMilliseconds();
        while (cursor <= endMs)
        {
            var url =
                $"fapi/v1/fundingRate?symbol={Uri.EscapeDataString(symbol)}&startTime={cursor}&endTime={endMs}&limit={FundingPageLimit}";
            using var doc = await GetDocumentAsync(url, cancellationToken)
                ?? throw new HttpRequestException($"Binance fundingRate returned empty: {url}");
            var batch = ParseFunding(doc.RootElement, symbol);
            if (batch.Count == 0)
            {
                break;
            }

            rows.AddRange(batch);
            var next = batch[^1].FundingTime.ToUnixTimeMilliseconds() + 1;
            if (next <= cursor)
            {
                break;
            }

            cursor = next;
            if (batch.Count < FundingPageLimit)
            {
                break;
            }

            await DelayAsync(cancellationToken);
        }

        return Dedupe(rows, r => r.FundingTime);
    }

    public async Task<IReadOnlyList<PricePoint>> GetMarkPriceKlinesAsync(
        string symbol,
        string interval,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default) =>
        await GetPriceKlinesAsync(
            $"fapi/v1/markPriceKlines?symbol={Uri.EscapeDataString(symbol)}&interval={interval}",
            symbol,
            start,
            end,
            cancellationToken);

    public async Task<IReadOnlyList<PricePoint>> GetIndexPriceKlinesAsync(
        string pair,
        string interval,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default) =>
        await GetPriceKlinesAsync(
            $"fapi/v1/indexPriceKlines?pair={Uri.EscapeDataString(pair)}&interval={interval}",
            pair,
            start,
            end,
            cancellationToken);

    public async Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistAsync(
        string symbol,
        string period,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<OpenInterestPoint>();
        var interval = period.Trim().ToLowerInvariant();
        var from = ClampOpenInterestStart(start);
        if (from > end)
        {
            return rows;
        }

        var step = PeriodSpan(interval) * OpenInterestPageLimit;
        while (from <= end)
        {
            var to = from + step;
            if (to > end)
            {
                to = end;
            }

            var url =
                $"futures/data/openInterestHist?symbol={Uri.EscapeDataString(symbol)}&period={Uri.EscapeDataString(interval)}&startTime={from.ToUnixTimeMilliseconds()}&endTime={to.ToUnixTimeMilliseconds()}&limit={OpenInterestPageLimit}";
            using var doc = await GetDocumentAsync(url, cancellationToken, emptyOnBadRequest: true);
            if (doc is null)
            {
                from = to.AddMilliseconds(1);
                continue;
            }

            var batch = ParseOpenInterest(doc.RootElement, symbol, interval);
            if (batch.Count == 0)
            {
                from = to.AddMilliseconds(1);
                continue;
            }

            rows.AddRange(batch);
            if (batch.Count < OpenInterestPageLimit)
            {
                from = to.AddMilliseconds(1);
                continue;
            }

            var newest = batch.Max(x => x.Timestamp);
            var next = newest.AddMilliseconds(1);
            from = next <= from ? to.AddMilliseconds(1) : next;
            await DelayAsync(cancellationToken);
        }

        return Dedupe(rows, r => r.Timestamp);
    }

    public static DateTimeOffset ClampOpenInterestStart(DateTimeOffset start)
    {
        var earliest = DateTimeOffset.UtcNow.AddDays(-(OpenInterestMaxLookbackDays - 1));
        return start < earliest ? earliest : start;
    }

    public static TimeSpan PeriodSpan(string period) => period.Trim().ToLowerInvariant() switch
    {
        "5m" => TimeSpan.FromMinutes(5),
        "15m" => TimeSpan.FromMinutes(15),
        "30m" => TimeSpan.FromMinutes(30),
        "1h" => TimeSpan.FromHours(1),
        "2h" => TimeSpan.FromHours(2),
        "4h" => TimeSpan.FromHours(4),
        "6h" => TimeSpan.FromHours(6),
        "12h" => TimeSpan.FromHours(12),
        "1d" => TimeSpan.FromDays(1),
        _ => TimeSpan.FromMinutes(5)
    };

    public static IReadOnlyList<BasisPoint> BuildBasis(IReadOnlyList<PricePoint> mark, IReadOnlyList<PricePoint> index)
    {
        var indexByClose = index
            .GroupBy(p => p.CloseTime)
            .ToDictionary(g => g.Key, g => g.Last().Close);
        var rows = new List<BasisPoint>();
        foreach (var m in mark)
        {
            if (!indexByClose.TryGetValue(m.CloseTime, out var idx) || idx == 0m)
            {
                continue;
            }

            var abs = m.Close - idx;
            rows.Add(new BasisPoint(m.Symbol, m.CloseTime, m.Close, idx, abs, abs / idx));
        }

        return rows;
    }

    public static List<FundingPoint> ParseFunding(JsonElement payload, string symbol)
    {
        var rows = new List<FundingPoint>();
        if (payload.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var item in payload.EnumerateArray())
        {
            var time = DateTimeOffset.FromUnixTimeMilliseconds(item.GetProperty("fundingTime").GetInt64());
            decimal? mark = null;
            if (item.TryGetProperty("markPrice", out var markEl) && markEl.ValueKind != JsonValueKind.Null)
            {
                var parsed = Dec(markEl);
                if (parsed != 0m)
                {
                    mark = parsed;
                }
            }

            rows.Add(new FundingPoint(
                item.TryGetProperty("symbol", out var s) ? s.GetString() ?? symbol : symbol,
                time,
                Dec(item.GetProperty("fundingRate")),
                mark));
        }

        return rows;
    }

    public static List<PricePoint> ParsePriceKlines(JsonElement payload, string symbol)
    {
        var rows = new List<PricePoint>();
        var now = DateTimeOffset.UtcNow;
        if (payload.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var row in payload.EnumerateArray())
        {
            var closeTime = DateTimeOffset.FromUnixTimeMilliseconds(row[6].GetInt64());
            if (closeTime > now)
            {
                continue;
            }

            rows.Add(new PricePoint(
                symbol,
                DateTimeOffset.FromUnixTimeMilliseconds(row[0].GetInt64()),
                closeTime,
                Dec(row[4])));
        }

        return rows;
    }

    public static List<OpenInterestPoint> ParseOpenInterest(JsonElement payload, string symbol, string period)
    {
        var rows = new List<OpenInterestPoint>();
        if (payload.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var item in payload.EnumerateArray())
        {
            var ts = DateTimeOffset.FromUnixTimeMilliseconds(item.GetProperty("timestamp").GetInt64());
            var oi = item.TryGetProperty("sumOpenInterest", out var oiEl) ? Dec(oiEl) : 0m;
            rows.Add(new OpenInterestPoint(symbol, ts, oi, period));
        }

        return rows;
    }

    private async Task<IReadOnlyList<PricePoint>> GetPriceKlinesAsync(
        string baseUrl,
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var rows = new List<PricePoint>();
        var cursor = start.ToUnixTimeMilliseconds();
        var endMs = end.ToUnixTimeMilliseconds();
        while (cursor <= endMs)
        {
            var url = $"{baseUrl}&startTime={cursor}&endTime={endMs}&limit={PriceKlinePageLimit}";
            using var doc = await GetDocumentAsync(url, cancellationToken)
                ?? throw new HttpRequestException($"Binance kline returned empty: {url}");
            var batch = ParsePriceKlines(doc.RootElement, symbol);
            if (batch.Count == 0)
            {
                break;
            }

            rows.AddRange(batch);
            var next = batch[^1].OpenTime.ToUnixTimeMilliseconds() + 1;
            if (next <= cursor)
            {
                break;
            }

            cursor = next;
            if (batch.Count < PriceKlinePageLimit)
            {
                break;
            }

            await DelayAsync(cancellationToken);
        }

        return Dedupe(rows, r => r.OpenTime);
    }

    private async Task<JsonDocument?> GetDocumentAsync(
        string url,
        CancellationToken cancellationToken,
        bool emptyOnBadRequest = false)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= _retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _http.GetAsync(url, cancellationToken);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(_pause * attempt, cancellationToken);
                    continue;
                }

                if (emptyOnBadRequest && response.StatusCode == HttpStatusCode.BadRequest)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
                await Task.Delay(_pause * attempt, cancellationToken);
            }
        }

        throw last ?? new HttpRequestException($"Binance request failed: {url}");
    }

    private Task DelayAsync(CancellationToken cancellationToken) =>
        _pause <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(_pause, cancellationToken);

    private static List<T> Dedupe<T>(List<T> rows, Func<T, DateTimeOffset> time)
    {
        return rows
            .GroupBy(time)
            .Select(g => g.Last())
            .OrderBy(time)
            .ToList();
    }

    private static decimal Dec(JsonElement element) =>
        decimal.Parse(element.GetString() ?? element.GetRawText(), CultureInfo.InvariantCulture);
}
