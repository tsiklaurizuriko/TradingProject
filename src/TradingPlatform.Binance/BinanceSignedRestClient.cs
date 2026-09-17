using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

public sealed class BinanceSignedRestClient
{
    private readonly HttpClient _spot;
    private readonly HttpClient _futures;
    private readonly ILogger<BinanceSignedRestClient> _logger;
    private readonly int _recvWindowMs;

    public BinanceSignedRestClient(HttpClient http, IHttpClientFactory httpFactory, ILogger<BinanceSignedRestClient> logger, IConfiguration configuration)
    {
        _spot = http;
        _futures = httpFactory.CreateClient("binance-futures");
        _logger = logger;
        _recvWindowMs = configuration.GetValue("Binance:RecvWindowMs", 5000);
    }

    public Task<JsonElement> GetAccountAsync(string apiKey, string apiSecret, CancellationToken cancellationToken) =>
        SendAsync(_spot, HttpMethod.Get, "api/v3/account", new Dictionary<string, string>(), apiKey, apiSecret, cancellationToken);

    public Task<JsonElement> GetFundingAssetsAsync(string apiKey, string apiSecret, CancellationToken cancellationToken) =>
        SendAsync(_spot, HttpMethod.Post, "sapi/v1/asset/get-funding-asset", new Dictionary<string, string>(), apiKey, apiSecret, cancellationToken);

    public Task<JsonElement> GetFuturesAccountAsync(string apiKey, string apiSecret, CancellationToken cancellationToken) =>
        SendAsync(_futures, HttpMethod.Get, "fapi/v2/account", new Dictionary<string, string>(), apiKey, apiSecret, cancellationToken);

    public Task<JsonElement> GetSpotOpenOrdersAsync(string apiKey, string apiSecret, CancellationToken cancellationToken) =>
        SendAsync(_spot, HttpMethod.Get, "api/v3/openOrders", new Dictionary<string, string>(), apiKey, apiSecret, cancellationToken);

    public Task<JsonElement> GetFuturesOpenOrdersAsync(string apiKey, string apiSecret, CancellationToken cancellationToken) =>
        SendAsync(_futures, HttpMethod.Get, "fapi/v1/openOrders", new Dictionary<string, string>(), apiKey, apiSecret, cancellationToken);

    public Task<JsonElement> GetFuturesPositionsAsync(string apiKey, string apiSecret, CancellationToken cancellationToken) =>
        SendAsync(_futures, HttpMethod.Get, "fapi/v2/positionRisk", new Dictionary<string, string>(), apiKey, apiSecret, cancellationToken);

    public Task<JsonElement> PlaceMarketOrderAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        OrderSide side,
        decimal quantity,
        string clientOrderId,
        CancellationToken cancellationToken) =>
        PlaceFuturesMarketOrderAsync(apiKey, apiSecret, symbol, side, quantity, clientOrderId, false, cancellationToken);

    public async Task<JsonElement> PlaceFuturesMarketOrderAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        OrderSide side,
        decimal quantity,
        string clientOrderId,
        bool reduceOnly,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["side"] = side == OrderSide.Buy ? "BUY" : "SELL",
            ["type"] = "MARKET",
            ["quantity"] = BinanceHmac.FormatDecimal(quantity),
            ["newClientOrderId"] = clientOrderId,
            ["newOrderRespType"] = "RESULT"
        };
        if (reduceOnly)
        {
            fields["reduceOnly"] = "true";
        }

        return await SendAsync(_futures, HttpMethod.Post, "fapi/v1/order", fields, apiKey, apiSecret, cancellationToken);
    }

    public async Task SetFuturesMarginTypeAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        string marginType,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["marginType"] = marginType
        };
        try
        {
            await SendAsync(_futures, HttpMethod.Post, "fapi/v1/marginType", fields, apiKey, apiSecret, cancellationToken);
        }
        catch (DomainException ex) when (
            ex.Message.Contains("No need to change", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("-4046", StringComparison.Ordinal))
        {
        }
    }

    public Task<JsonElement> SetFuturesLeverageAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        int leverage,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["leverage"] = Math.Max(1, leverage).ToString(CultureInfo.InvariantCulture)
        };
        return SendAsync(_futures, HttpMethod.Post, "fapi/v1/leverage", fields, apiKey, apiSecret, cancellationToken);
    }

    public Task<JsonElement> PlaceFuturesClosePositionOrderAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        OrderSide closeSide,
        string type,
        decimal stopPrice,
        string clientOrderId,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["side"] = closeSide == OrderSide.Buy ? "BUY" : "SELL",
            ["type"] = type,
            ["stopPrice"] = BinanceHmac.FormatDecimal(stopPrice),
            ["closePosition"] = "true",
            ["workingType"] = "MARK_PRICE",
            ["priceProtect"] = "TRUE",
            ["newClientOrderId"] = clientOrderId,
            ["newOrderRespType"] = "RESULT"
        };
        return SendAsync(_futures, HttpMethod.Post, "fapi/v1/order", fields, apiKey, apiSecret, cancellationToken);
    }

    public Task<JsonElement> CancelFuturesOrderAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        string? clientOrderId,
        string? exchangeOrderId,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant()
        };
        if (!string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            fields["orderId"] = exchangeOrderId;
        }
        else if (!string.IsNullOrWhiteSpace(clientOrderId))
        {
            fields["origClientOrderId"] = clientOrderId;
        }

        return SendAsync(_futures, HttpMethod.Delete, "fapi/v1/order", fields, apiKey, apiSecret, cancellationToken);
    }

    private async Task<JsonElement> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string> fields,
        string apiKey,
        string apiSecret,
        CancellationToken cancellationToken)
    {
        var query = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in fields)
        {
            query[kv.Key] = kv.Value;
        }
        query["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        query["recvWindow"] = _recvWindowMs.ToString(CultureInfo.InvariantCulture);
        var payload = string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        var signature = BinanceHmac.Sign(apiSecret, payload);
        var url = $"{path}?{payload}&signature={signature}";

        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("X-MBX-APIKEY", apiKey);
        if (method == HttpMethod.Post)
        {
            request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded");
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DomainException(ErrorCodes.ExchangeUnavailable, "Binance signed request failed.", new Dictionary<string, object?> { ["error"] = ex.Message });
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Binance signed {Path} failed: {Status} {Body}", path, (int)response.StatusCode, body);
            var code = (int)response.StatusCode == 429 ? ErrorCodes.ExchangeRateLimit : ErrorCodes.OrderRejected;
            throw new DomainException(code, TrimBinanceError(body));
        }

        return string.IsNullOrWhiteSpace(body)
            ? default
            : JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static string TrimBinanceError(string body)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            if (json.TryGetProperty("msg", out var msg))
            {
                return msg.GetString() ?? "Binance rejected the request.";
            }
        }
        catch
        {
            // fall through
        }

        return string.IsNullOrWhiteSpace(body) ? "Binance rejected the request." : body.Trim();
    }
}
