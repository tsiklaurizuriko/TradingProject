using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

public sealed class BinancePublicMarketDataClient : IPublicMarketDataClient
{
    private static readonly TimeSpan StreamBarWait = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan EmptyHold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BackgroundWait = TimeSpan.FromSeconds(30);

    /// <summary>Weight background refresh jobs leave for bot cycles on top of <see cref="BinancePublicWeight.SignedReserve"/>.</summary>
    private const int ForegroundReserve = 400;

    private static readonly SemaphoreSlim UniverseLock = new(1, 1);
    private static IReadOnlyList<RankedUsdtSpotSymbol>? CachedUniverse;
    private static DateTimeOffset CacheUntil;
    private static readonly object FundingGate = new();
    private static Dictionary<string, decimal>? FundingCache;
    private static DateTimeOffset FundingUntil;

    private static readonly SemaphoreSlim InFlight = new(8, 8);

    private readonly HttpClient _futures;
    private readonly ILogger<BinancePublicMarketDataClient> _logger;
    private readonly FuturesKlineStore _klines;

    public BinancePublicMarketDataClient(IHttpClientFactory httpFactory, ILogger<BinancePublicMarketDataClient> logger)
        : this(httpFactory.CreateClient("binance-futures"), logger, FuturesKlineStore.Shared)
    {
    }

    internal BinancePublicMarketDataClient(HttpClient futures, ILogger<BinancePublicMarketDataClient> logger, FuturesKlineStore klines)
    {
        _futures = futures;
        _logger = logger;
        _klines = klines;
    }

    /// <summary>
    /// Same rows as one REST call with this limit: the latest <c>limit - 1</c> closed bars.
    /// Served from the shared store while current; otherwise only the missing tail is requested.
    /// </summary>
    public async Task<IReadOnlyList<MarketCandle>> GetClosedKlinesAsync(
        string symbol,
        Timeframe timeframe,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var id = symbol.ToUpperInvariant();
        var need = limit - 1;
        if (need <= 0 || limit > FuturesKlineStore.MaxRows)
        {
            return await FetchLatestAsync(id, timeframe, limit, cancellationToken) ?? [];
        }

        var store = _klines;
        store.Demand(id, timeframe, need, DateTimeOffset.UtcNow);
        if (store.TryLatest(id, timeframe, need, DateTimeOffset.UtcNow, out var hit))
        {
            return hit;
        }

        if (store.ExpectsStreamBar(id, timeframe, DateTimeOffset.UtcNow)
            && await store.WaitForChangeAsync(id, timeframe, StreamBarWait, cancellationToken)
            && store.TryLatest(id, timeframe, need, DateTimeOffset.UtcNow, out hit))
        {
            return hit;
        }

        var gate = store.FetchLock(id, timeframe);
        await gate.WaitAsync(cancellationToken);

        try
        {
            if (store.TryLatest(id, timeframe, need, DateTimeOffset.UtcNow, out hit))
            {
                return hit;
            }

            var cover = store.Cover(id, timeframe, DateTimeOffset.UtcNow);
            if (cover.Count > 0
                && cover.Missing is > 0 and <= FuturesKlineStore.MaxIncrementalBars
                && (cover.Count + cover.Missing >= need || cover.FromListing))
            {
                var tail = await FetchLatestAsync(id, timeframe, cover.Missing + 2, cancellationToken);
                if (tail is null)
                {
                    return [];
                }

                if (tail.Count > 0)
                {
                    store.Merge(id, timeframe, tail, tail[0].OpenTime, fromListing: false, DateTimeOffset.UtcNow);
                }

                if (store.TryLatest(id, timeframe, need, DateTimeOffset.UtcNow, out hit))
                {
                    return hit;
                }
            }

            var candles = await FetchLatestAsync(id, timeframe, limit, cancellationToken);
            if (candles is null)
            {
                return [];
            }

            if (candles.Count == 0)
            {
                store.MarkEmpty(id, timeframe, DateTimeOffset.UtcNow + EmptyHold);
                return candles;
            }

            store.Merge(id, timeframe, candles, candles[0].OpenTime, fromListing: candles.Count < need, DateTimeOffset.UtcNow);
            return candles.Count > need ? candles.GetRange(candles.Count - need, need) : candles;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>One REST page of the latest bars, with the forming bar dropped. Null when the request was skipped or failed.</summary>
    private async Task<List<MarketCandle>?> FetchLatestAsync(string symbol, Timeframe timeframe, int limit, CancellationToken cancellationToken)
    {
        var interval = timeframe.ToBinanceInterval();
        var payload = await GetJsonOrNullAsync($"fapi/v1/klines?symbol={symbol}&interval={interval}&limit={limit}", cancellationToken);
        if (payload is null)
        {
            return null;
        }

        var candles = ParseClosedKlines(payload.Value);
        _logger.LogDebug("Loaded {Count} closed USD-M {Interval} candles for {Symbol}", candles.Count, interval, symbol);
        return candles;
    }

    /// <summary>One closed bar straight from REST, bypassing the store. Used to audit bars built from the stream.</summary>
    internal async Task<MarketCandle?> FetchClosedBarAsync(string symbol, Timeframe timeframe, DateTimeOffset open, CancellationToken cancellationToken)
    {
        var ms = open.ToUnixTimeMilliseconds();
        var payload = await GetJsonOrNullAsync(
            $"fapi/v1/klines?symbol={symbol.ToUpperInvariant()}&interval={timeframe.ToBinanceInterval()}&startTime={ms}&endTime={ms}&limit=1",
            cancellationToken);
        return payload is null ? null : ParseClosedKlines(payload.Value).FirstOrDefault(row => row.OpenTime == open);
    }

    public async Task<IReadOnlyList<MarketCandle>> GetClosedKlinesRangeAsync(
        string symbol,
        Timeframe timeframe,
        DateTimeOffset start,
        DateTimeOffset end,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var cap = Math.Clamp(limit, 50, 250_000);
        var id = symbol.ToUpperInvariant();
        var step = timeframe.ToDuration();
        var now = DateTimeOffset.UtcNow;
        var rowsBack = (now - start).Ticks / (double)step.Ticks + 2;
        var live = end >= now - step - step && start < end && rowsBack <= FuturesKlineStore.MaxRows;
        if (!live)
        {
            return await LoadRangeAsync(id, timeframe, start, end, cap, cancellationToken);
        }

        var store = _klines;
        store.Demand(id, timeframe, (int)Math.Ceiling(rowsBack), now);
        if (store.TryRange(id, timeframe, start, end, cap, now, out var hit))
        {
            return hit;
        }

        var gate = store.FetchLock(id, timeframe);
        await gate.WaitAsync(cancellationToken);

        try
        {
            now = DateTimeOffset.UtcNow;
            if (store.TryRange(id, timeframe, start, end, cap, now, out hit))
            {
                return hit;
            }

            var cover = store.Cover(id, timeframe, now);
            if (cover.Count > 0
                && (cover.CoveredFrom <= start || cover.FromListing)
                && cover.Missing is > 0 and <= FuturesKlineStore.MaxIncrementalBars)
            {
                var tail = await FetchLatestAsync(id, timeframe, cover.Missing + 2, cancellationToken);
                if (tail is { Count: > 0 })
                {
                    store.Merge(id, timeframe, tail, tail[0].OpenTime, fromListing: false, DateTimeOffset.UtcNow);
                }

                if (store.TryRange(id, timeframe, start, end, cap, DateTimeOffset.UtcNow, out hit))
                {
                    return hit;
                }
            }

            var load = await LoadRangePagesAsync(id, timeframe, start, end, cap, cancellationToken);
            if (load.Complete && load.Rows.Count > 0)
            {
                store.Merge(id, timeframe, load.Rows, start, fromListing: load.Rows[0].OpenTime > start + step, DateTimeOffset.UtcNow);
            }
            else if (load.Complete)
            {
                store.MarkEmpty(id, timeframe, DateTimeOffset.UtcNow + EmptyHold);
            }

            return load.Rows;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<MarketCandle>> LoadRangeAsync(
        string symbol,
        Timeframe timeframe,
        DateTimeOffset start,
        DateTimeOffset end,
        int cap,
        CancellationToken cancellationToken) =>
        (await LoadRangePagesAsync(symbol, timeframe, start, end, cap, cancellationToken)).Rows;

    /// <summary>REST pages for [start, end]. Not complete when a page was skipped or failed.</summary>
    private async Task<(IReadOnlyList<MarketCandle> Rows, bool Complete)> LoadRangePagesAsync(
        string symbol,
        Timeframe timeframe,
        DateTimeOffset start,
        DateTimeOffset end,
        int cap,
        CancellationToken cancellationToken)
    {
        var complete = true;
        var interval = timeframe.ToBinanceInterval();
        var candles = new List<MarketCandle>(Math.Min(cap, 1500));
        var cursor = start.ToUnixTimeMilliseconds();
        var endMs = end.ToUnixTimeMilliseconds();
        const int page = 1500;

        while (candles.Count < cap && cursor <= endMs)
        {
            var remaining = Math.Min(page, cap - candles.Count);
            var url =
                $"fapi/v1/klines?symbol={symbol.ToUpperInvariant()}&interval={interval}&startTime={cursor}&endTime={endMs}&limit={remaining}";
            var payload = await GetJsonOrNullAsync(url, cancellationToken);
            if (payload is null)
            {
                complete = false;
                break;
            }

            var batch = ParseClosedKlines(payload.Value);
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

        var series = KlineSeries.Normalize(candles, out var duplicates);
        var quality = KlineSeries.Inspect(series, timeframe, duplicates);
        _logger.LogInformation(
            "Loaded {Count} historical USD-M {Interval} candles for {Symbol} from {Start} to {End} ({Duplicates} duplicates dropped, {Missing} bars missing in {Gaps} gaps)",
            series.Count,
            interval,
            symbol,
            start,
            end,
            quality.Duplicates,
            quality.MissingBars,
            quality.Gaps.Count);
        return (series, complete);
    }

    public Task<IReadOnlyList<TimedValue>> GetOpenInterestHistoryAsync(
        string symbol,
        Timeframe period,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        var floor = DateTimeOffset.UtcNow.AddDays(-29);
        var from = start < floor ? floor : start;
        return ReadTimedPagesAsync(
            cursor => $"futures/data/openInterestHist?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}&period={period.ToBinanceInterval()}&startTime={cursor}&endTime={end.ToUnixTimeMilliseconds()}&limit=500",
            500,
            "timestamp",
            "sumOpenInterest",
            from,
            end,
            cancellationToken);
    }

    public Task<IReadOnlyList<TimedValue>> GetTopTraderPositionRatioHistoryAsync(
        string symbol,
        Timeframe period,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        var floor = DateTimeOffset.UtcNow.AddDays(-29);
        var from = start < floor ? floor : start;
        return ReadTimedPagesAsync(
            cursor => $"futures/data/topLongShortPositionRatio?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}&period={period.ToBinanceInterval()}&startTime={cursor}&endTime={end.ToUnixTimeMilliseconds()}&limit=500",
            500,
            "timestamp",
            "longShortRatio",
            from,
            end,
            cancellationToken);
    }

    public Task<IReadOnlyList<TimedValue>> GetFundingHistoryAsync(
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default) =>
        ReadTimedPagesAsync(
            cursor => $"fapi/v1/fundingRate?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}&startTime={cursor}&endTime={end.ToUnixTimeMilliseconds()}&limit=1000",
            1000,
            "fundingTime",
            "fundingRate",
            start,
            end,
            cancellationToken);

    private async Task<IReadOnlyList<TimedValue>> ReadTimedPagesAsync(
        Func<long, string> url,
        int pageSize,
        string timeField,
        string valueField,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var rows = new List<TimedValue>();
        var cursor = start.ToUnixTimeMilliseconds();
        var endMs = end.ToUnixTimeMilliseconds();
        for (var page = 0; page < 40 && cursor <= endMs; page++)
        {
            var payload = await GetJsonOrNullAsync(url(cursor), cancellationToken);
            if (payload is null || payload.Value.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var batch = ParseTimedValues(payload.Value, timeField, valueField);
            if (batch.Count == 0)
            {
                break;
            }

            rows.AddRange(batch);
            var next = batch[^1].Time.ToUnixTimeMilliseconds() + 1;
            if (next <= cursor || batch.Count < pageSize)
            {
                break;
            }

            cursor = next;
        }

        return rows
            .GroupBy(row => row.Time)
            .Select(group => group.Last())
            .OrderBy(row => row.Time)
            .ToList();
    }

    internal static List<TimedValue> ParseTimedValues(JsonElement payload, string timeField, string valueField)
    {
        var rows = new List<TimedValue>();
        foreach (var row in payload.EnumerateArray())
        {
            if (!row.TryGetProperty(timeField, out var timeEl)
                || !timeEl.TryGetInt64(out var ms)
                || !row.TryGetProperty(valueField, out var valueEl)
                || valueEl.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var text = valueEl.ValueKind == JsonValueKind.String ? valueEl.GetString() : valueEl.GetRawText();
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                rows.Add(new TimedValue(DateTimeOffset.FromUnixTimeMilliseconds(ms), value));
            }
        }

        rows.Sort((a, b) => a.Time.CompareTo(b.Time));
        return rows;
    }

    public async Task<(decimal? Previous, decimal? Latest)> GetOpenInterestPairAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var url = $"futures/data/openInterestHist?symbol={Uri.EscapeDataString(symbol)}&period=1h&limit=2";
        var payload = await GetJsonOrNullAsync(url, cancellationToken);
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        var points = new List<(long Ts, decimal Oi)>();
        foreach (var row in payload.Value.EnumerateArray())
        {
            if (!row.TryGetProperty("sumOpenInterest", out var oiEl) || !row.TryGetProperty("timestamp", out var tsEl))
            {
                continue;
            }

            var oi = Dec(oiEl);
            var ts = tsEl.ValueKind == JsonValueKind.Number ? tsEl.GetInt64() : 0;
            if (oi > 0m && ts > 0)
            {
                points.Add((ts, oi));
            }
        }

        if (points.Count < 2)
        {
            return (null, null);
        }

        points.Sort((a, b) => a.Ts.CompareTo(b.Ts));
        return (points[^2].Oi, points[^1].Oi);
    }

    public async Task<(decimal? DayAgo, decimal? Latest)> GetOpenInterestDayAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var url = $"futures/data/openInterestHist?symbol={Uri.EscapeDataString(symbol)}&period=1h&limit=24";
        var payload = await GetJsonOrNullAsync(url, cancellationToken);
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        var points = new List<(long Ts, decimal Oi)>();
        foreach (var row in payload.Value.EnumerateArray())
        {
            if (!row.TryGetProperty("sumOpenInterest", out var oiEl) || !row.TryGetProperty("timestamp", out var tsEl))
            {
                continue;
            }

            var oi = Dec(oiEl);
            var ts = tsEl.ValueKind == JsonValueKind.Number ? tsEl.GetInt64() : 0;
            if (oi > 0m && ts > 0)
            {
                points.Add((ts, oi));
            }
        }

        if (points.Count < 20)
        {
            return (null, null);
        }

        points.Sort((a, b) => a.Ts.CompareTo(b.Ts));
        return (points[0].Oi, points[^1].Oi);
    }

    public async Task<decimal?> GetLastFundingRateAsync(string symbol, CancellationToken cancellationToken = default)
    {
        Dictionary<string, decimal>? map;
        lock (FundingGate)
        {
            map = DateTimeOffset.UtcNow < FundingUntil ? FundingCache : null;
        }

        if (map is null)
        {
            var rows = await GetPremiumIndexAsync(cancellationToken);
            if (rows.Count == 0)
            {
                return null;
            }

            map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                map[row.Symbol] = row.LastFundingRate;
            }

            lock (FundingGate)
            {
                FundingCache = map;
                FundingUntil = DateTimeOffset.UtcNow.AddSeconds(60);
            }
        }

        return map.TryGetValue(symbol, out var rate) ? rate : null;
    }

    public async Task<decimal> GetLastPriceAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var id = symbol.ToUpperInvariant();
        if (FuturesPriceBook.TryGet(id, DateTimeOffset.UtcNow, out var cached))
        {
            return cached;
        }

        var url = $"fapi/v1/ticker/price?symbol={id}";
        var payload = await GetJsonOrNullAsync(url, cancellationToken);
        if (payload is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new HttpRequestException("Binance USD-M last price is unavailable.");
        }

        var price = Dec(payload.Value.GetProperty("price"));
        FuturesPriceBook.Set(id, price, DateTimeOffset.UtcNow);
        return price;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetLastPricesAsync(CancellationToken cancellationToken = default)
    {
        var payload = await GetJsonOrNullAsync("fapi/v1/ticker/price", cancellationToken);
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        foreach (var item in payload.Value.EnumerateArray())
        {
            var name = item.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name) || !item.TryGetProperty("price", out var priceEl))
            {
                continue;
            }

            var price = Dec(priceEl);
            if (price <= 0m)
            {
                continue;
            }

            prices[name] = price;
            FuturesPriceBook.Set(name, price, now);
        }

        return prices;
    }

    public async Task<IReadOnlyList<RankedUsdtSpotSymbol>> GetPaperUniverseAsync(CancellationToken cancellationToken = default)
    {
        var cached = CachedUniverse;
        if (cached is { Count: > 0 } && DateTimeOffset.UtcNow < CacheUntil)
        {
            return cached;
        }

        try
        {
            await UniverseLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return cached ?? [];
        }

        try
        {
            cached = CachedUniverse;
            if (cached is { Count: > 0 } && DateTimeOffset.UtcNow < CacheUntil)
            {
                return cached;
            }

            var universe = await FetchUsdtFuturesUniverseAsync(cancellationToken);
            if (universe.Count > 0)
            {
                CachedUniverse = universe;
                CacheUntil = DateTimeOffset.UtcNow.AddMinutes(5);
                return universe;
            }

            return cached ?? [];
        }
        catch (OperationCanceledException)
        {
            return CachedUniverse ?? [];
        }
        finally
        {
            UniverseLock.Release();
        }
    }

    private async Task<IReadOnlyList<RankedUsdtSpotSymbol>> FetchUsdtFuturesUniverseAsync(CancellationToken cancellationToken)
    {
        var tickers = await GetJsonOrNullAsync("fapi/v1/ticker/24hr", cancellationToken);
        var info = await GetJsonOrNullAsync("fapi/v1/exchangeInfo", cancellationToken);
        if (tickers is null || info is null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CachedUniverse ?? [];
            }

            throw new HttpRequestException("Binance USD-M futures universe is unavailable.");
        }

        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var changes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var volumes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var highs = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var lows = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var trades = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var ticker in tickers.Value.EnumerateArray())
        {
            var name = ticker.GetProperty("symbol").GetString() ?? "";
            prices[name] = Dec(ticker.GetProperty("lastPrice"));
            changes[name] = Dec(ticker.GetProperty("priceChangePercent"));
            volumes[name] = Dec(ticker.GetProperty("quoteVolume"));
            if (ticker.TryGetProperty("highPrice", out var highEl))
            {
                highs[name] = Dec(highEl);
            }
            if (ticker.TryGetProperty("lowPrice", out var lowEl))
            {
                lows[name] = Dec(lowEl);
            }
            if (ticker.TryGetProperty("count", out var countEl))
            {
                if (countEl.TryGetInt32(out var count))
                {
                    trades[name] = count;
                }
                else if (countEl.TryGetInt64(out var count64))
                {
                    trades[name] = count64 > int.MaxValue ? int.MaxValue : (int)count64;
                }
            }
        }

        var universe = new List<RankedUsdtSpotSymbol>();
        foreach (var symbol in info.Value.GetProperty("symbols").EnumerateArray())
        {
            if (!UsdtPerpetualContractRules.TryMap(symbol, out var contract, out _) || contract is null)
            {
                continue;
            }

            var name = contract.Symbol;
            universe.Add(new RankedUsdtSpotSymbol(
                name,
                contract.BaseAsset,
                contract.QuoteAsset,
                volumes.GetValueOrDefault(name),
                prices.GetValueOrDefault(name),
                changes.GetValueOrDefault(name),
                contract.TickSize,
                contract.StepSize,
                contract.MinQuantity,
                contract.MinNotional,
                contract.PricePrecision,
                contract.QuantityPrecision,
                highs.GetValueOrDefault(name),
                lows.GetValueOrDefault(name),
                trades.GetValueOrDefault(name)));
        }

        universe.Sort((a, b) =>
        {
            var volume = b.QuoteVolume.CompareTo(a.QuoteVolume);
            return volume != 0 ? volume : string.Compare(a.Symbol, b.Symbol, StringComparison.Ordinal);
        });

        _logger.LogInformation("USD-M USDT perpetual universe loaded: {Count} contracts", universe.Count);
        return universe;
    }

    public async Task<IReadOnlyList<DiscoveredFuturesContract>> DiscoverUsdtPerpetualsAsync(
        CancellationToken cancellationToken = default)
    {
        var info = await GetJsonOrNullAsync("fapi/v1/exchangeInfo", cancellationToken);
        if (info is null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return [];
            }

            throw new HttpRequestException("Binance USD-M exchangeInfo is unavailable.");
        }

        var contracts = UsdtPerpetualContractRules.MapExchangeInfo(info.Value);
        _logger.LogInformation("Discovered {Count} USD-M USDT perpetual contracts from exchangeInfo", contracts.Count);
        return contracts;
    }

    public async Task<IReadOnlyList<FuturesBookTicker>> GetBookTickersAsync(CancellationToken cancellationToken = default)
    {
        var payload = await GetJsonOrNullAsync("fapi/v1/ticker/bookTicker", cancellationToken);
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<FuturesBookTicker>();
        foreach (var item in payload.Value.EnumerateArray())
        {
            var name = item.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            rows.Add(new FuturesBookTicker(name, Dec(item.GetProperty("bidPrice")), Dec(item.GetProperty("askPrice"))));
        }

        return rows;
    }

    public async Task<IReadOnlyList<FuturesPremiumIndex>> GetPremiumIndexAsync(CancellationToken cancellationToken = default)
    {
        var payload = await GetJsonOrNullAsync("fapi/v1/premiumIndex", cancellationToken);
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<FuturesPremiumIndex>();
        foreach (var item in payload.Value.EnumerateArray())
        {
            var name = item.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var mark = item.TryGetProperty("markPrice", out var markEl) ? Dec(markEl) : 0m;
            var funding = item.TryGetProperty("lastFundingRate", out var fundEl) ? Dec(fundEl) : 0m;
            rows.Add(new FuturesPremiumIndex(name, mark, funding));
        }

        return rows;
    }

    private static List<MarketCandle> ParseClosedKlines(JsonElement payload)
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
                TradeCount = row[8].GetInt32(),
                TakerBuyVolume = row.GetArrayLength() > 9 ? Dec(row[9]) : 0m,
                IsClosed = true,
                ExchangeTimestamp = closeTime
            });
        }

        return candles;
    }

    private static readonly object SkipSync = new();
    private static readonly TimeSpan SkipReportInterval = TimeSpan.FromSeconds(30);
    private static DateTimeOffset _skipWindowStart = DateTimeOffset.MinValue;
    private static int _skippedInWindow;

    /// <summary>One warning per 30 s with the number of requests skipped, instead of one line per request.</summary>
    private void ReportSkipped(string url)
    {
        int skipped;
        lock (SkipSync)
        {
            var now = DateTimeOffset.UtcNow;
            _skippedInWindow++;
            if (now - _skipWindowStart < SkipReportInterval)
            {
                return;
            }

            skipped = _skippedInWindow;
            _skippedInWindow = 0;
            _skipWindowStart = now;
        }

        _logger.LogWarning(
            "Binance USD-M weight budget is full. {Count} public request(s) skipped since the last report; they are retried next cycle. Latest: {Url}",
            skipped,
            url);
    }

    private async Task<JsonElement?> GetJsonOrNullAsync(string url, CancellationToken cancellationToken)
    {
        var weight = BinancePublicWeight.ForRequest(url);
        var background = MarketDataPriority.IsBackground;
        var acquired = await BinancePublicWeightGate.TryAcquireAsync(
            weight,
            background ? BackgroundWait : TimeSpan.FromMilliseconds(500),
            cancellationToken,
            BinancePublicWeight.SignedReserve + (background ? ForegroundReserve : 0));
        if (!acquired)
        {
            ReportSkipped(url);
            return null;
        }

        var entered = false;
        try
        {
            await InFlight.WaitAsync(cancellationToken);
            entered = true;
            using var response = await _futures.GetAsync(url, cancellationToken);
            if ((int)response.StatusCode is 418 or 429)
            {
                var pause = response.Headers.RetryAfter?.Delta is { } retry && retry > TimeSpan.Zero
                    ? retry
                    : TimeSpan.FromMinutes(1);
                BinancePublicWeightGate.CoolDown(pause);
                _logger.LogWarning("Binance asked this IP to slow down for {Seconds}s after {Url}", (int)pause.TotalSeconds, url);
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Binance USD-M request timed out: {Url}", url);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Binance USD-M request failed: {Url}", url);
            return null;
        }
        finally
        {
            if (entered)
            {
                InFlight.Release();
            }
        }
    }

    private static int CountDecimals(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        var i = text.IndexOf('.');
        if (i < 0)
        {
            return 0;
        }

        return text.TrimEnd('0').Length - i - 1;
    }

    private static decimal Dec(JsonElement element) =>
        decimal.Parse(element.GetString() ?? element.GetRawText(), CultureInfo.InvariantCulture);
}
