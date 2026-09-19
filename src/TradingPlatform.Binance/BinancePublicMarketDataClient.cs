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
    private static readonly SemaphoreSlim UniverseLock = new(1, 1);
    private static IReadOnlyList<RankedUsdtSpotSymbol>? CachedUniverse;
    private static DateTimeOffset CacheUntil;

    private readonly HttpClient _futures;
    private readonly ILogger<BinancePublicMarketDataClient> _logger;

    public BinancePublicMarketDataClient(IHttpClientFactory httpFactory, ILogger<BinancePublicMarketDataClient> logger)
    {
        _futures = httpFactory.CreateClient("binance-futures");
        _logger = logger;
    }

    public async Task<IReadOnlyList<MarketCandle>> GetClosedKlinesAsync(
        string symbol,
        Timeframe timeframe,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var interval = timeframe.ToBinanceInterval();
        var url = $"fapi/v1/klines?symbol={symbol.ToUpperInvariant()}&interval={interval}&limit={limit}";
        var payload = await GetJsonOrNullAsync(url, cancellationToken);
        if (payload is null)
        {
            return [];
        }

        var candles = ParseClosedKlines(payload.Value);
        _logger.LogDebug("Loaded {Count} closed USD-M {Interval} candles for {Symbol}", candles.Count, interval, symbol);
        return candles;
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

        _logger.LogInformation(
            "Loaded {Count} historical USD-M {Interval} candles for {Symbol} from {Start} to {End}",
            candles.Count,
            interval,
            symbol,
            start,
            end);
        return candles;
    }

    public async Task<decimal> GetLastPriceAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var url = $"fapi/v1/ticker/price?symbol={symbol.ToUpperInvariant()}";
        var payload = await GetJsonOrNullAsync(url, cancellationToken);
        if (payload is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new HttpRequestException("Binance USD-M last price is unavailable.");
        }

        return Dec(payload.Value.GetProperty("price"));
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
                IsClosed = true,
                ExchangeTimestamp = closeTime
            });
        }

        return candles;
    }

    private async Task<JsonElement?> GetJsonOrNullAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _futures.GetAsync(url, cancellationToken);
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
