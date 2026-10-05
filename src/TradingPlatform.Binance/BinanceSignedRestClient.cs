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
        SendAsync(_futures, HttpMethod.Get, "fapi/v3/account", new Dictionary<string, string>(), apiKey, apiSecret, cancellationToken);

    public Task<JsonElement> GetSpotOpenOrdersAsync(string apiKey, string apiSecret, CancellationToken cancellationToken) =>
        SendAsync(_spot, HttpMethod.Get, "api/v3/openOrders", new Dictionary<string, string>(), apiKey, apiSecret, cancellationToken);

    public Task<JsonElement> GetFuturesOpenOrdersAsync(string apiKey, string apiSecret, CancellationToken cancellationToken) =>
        SendAsync(_futures, HttpMethod.Get, "fapi/v1/openOrders", new Dictionary<string, string>(), apiKey, apiSecret, cancellationToken);

    public Task<JsonElement> GetFuturesOpenAlgoOrdersAsync(string apiKey, string apiSecret, CancellationToken cancellationToken) =>
        SendAsync(
            _futures,
            HttpMethod.Get,
            BinanceConditionalAlgoOrder.OpenPath,
            new Dictionary<string, string>(),
            apiKey,
            apiSecret,
            cancellationToken);

    public Task<JsonElement> GetFuturesAllAlgoOrdersAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        CancellationToken cancellationToken,
        int limit = 1000)
    {
        var fields = new Dictionary<string, string>
        {
            ["algoType"] = "CONDITIONAL",
            ["symbol"] = symbol.ToUpperInvariant(),
            ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture)
        };
        return SendAsync(_futures, HttpMethod.Get, BinanceConditionalAlgoOrder.HistoryPath, fields, apiKey, apiSecret, cancellationToken);
    }

    public Task<JsonElement> GetFuturesAllOrdersAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        CancellationToken cancellationToken,
        int limit = 1000)
    {
        var fields = new Dictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture)
        };
        return SendAsync(_futures, HttpMethod.Get, "fapi/v1/allOrders", fields, apiKey, apiSecret, cancellationToken);
    }

    public Task<JsonElement> GetFuturesOrderAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        string? clientOrderId,
        string? exchangeOrderId,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string> { ["symbol"] = symbol.ToUpperInvariant() };
        if (!string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            fields["orderId"] = exchangeOrderId;
        }
        else if (!string.IsNullOrWhiteSpace(clientOrderId))
        {
            fields["origClientOrderId"] = clientOrderId;
        }

        return SendAsync(_futures, HttpMethod.Get, "fapi/v1/order", fields, apiKey, apiSecret, cancellationToken);
    }

    public Task<JsonElement> GetFuturesAlgoOrderAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        string? clientAlgoId,
        string? algoId,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string> { ["symbol"] = symbol.ToUpperInvariant() };
        if (!string.IsNullOrWhiteSpace(algoId))
        {
            fields["algoId"] = algoId;
        }
        else if (!string.IsNullOrWhiteSpace(clientAlgoId))
        {
            fields["clientAlgoId"] = clientAlgoId;
        }

        return SendAsync(_futures, HttpMethod.Get, "fapi/v1/algoOrder", fields, apiKey, apiSecret, cancellationToken);
    }

    public Task<JsonElement> GetFuturesPositionsAsync(
        string apiKey,
        string apiSecret,
        CancellationToken cancellationToken,
        string? symbol = null) =>
        SendPositionRiskAsync(apiKey, apiSecret, "fapi/v3/positionRisk", symbol, cancellationToken);

    public Task<JsonElement> GetFuturesPositionsV2Async(
        string apiKey,
        string apiSecret,
        CancellationToken cancellationToken,
        string? symbol = null) =>
        SendPositionRiskAsync(apiKey, apiSecret, "fapi/v2/positionRisk", symbol, cancellationToken);

    private Task<JsonElement> SendPositionRiskAsync(
        string apiKey,
        string apiSecret,
        string path,
        string? symbol,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            fields["symbol"] = symbol.ToUpperInvariant();
        }

        return SendAsync(_futures, HttpMethod.Get, path, fields, apiKey, apiSecret, cancellationToken);
    }

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

    public Task<JsonElement> GetFuturesLeverageBracketsAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant()
        };
        return SendAsync(_futures, HttpMethod.Get, "fapi/v1/leverageBracket", fields, apiKey, apiSecret, cancellationToken);
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

    public Task<JsonElement> PlaceFuturesConditionalCloseAlgoAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        OrderSide closeSide,
        string type,
        decimal triggerPrice,
        string clientAlgoId,
        CancellationToken cancellationToken,
        bool priceProtect = true)
    {
        var fields = BinanceConditionalAlgoOrder.PlaceFields(
            symbol,
            closeSide,
            type,
            triggerPrice,
            clientAlgoId,
            priceProtect);
        return SendAsync(
            _futures,
            HttpMethod.Post,
            BinanceConditionalAlgoOrder.PlacePath,
            fields,
            apiKey,
            apiSecret,
            cancellationToken);
    }

    public Task<JsonElement> CancelFuturesAlgoOrderAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        string clientAlgoId,
        CancellationToken cancellationToken)
    {
        var fields = BinanceConditionalAlgoOrder.CancelFields(symbol, clientAlgoId);
        return SendAsync(
            _futures,
            HttpMethod.Delete,
            BinanceConditionalAlgoOrder.CancelPath,
            fields,
            apiKey,
            apiSecret,
            cancellationToken);
    }

    public Task<JsonElement> GetFuturesUserTradesAsync(
        string apiKey,
        string apiSecret,
        string symbol,
        string? orderId,
        CancellationToken cancellationToken,
        int limit = 1000)
    {
        var fields = new Dictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            fields["orderId"] = orderId;
        }

        return SendAsync(_futures, HttpMethod.Get, "fapi/v1/userTrades", fields, apiKey, apiSecret, cancellationToken);
    }

    public Task<JsonElement> GetFuturesIncomeAsync(
        string apiKey,
        string apiSecret,
        string incomeType,
        CancellationToken cancellationToken,
        int limit = 1000)
    {
        var fields = new Dictionary<string, string>
        {
            ["incomeType"] = incomeType,
            ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture)
        };
        return SendAsync(_futures, HttpMethod.Get, "fapi/v1/income", fields, apiKey, apiSecret, cancellationToken);
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
        var weight = BinancePublicWeight.ForRequest(path + (fields.ContainsKey("symbol") ? "?symbol=1" : ""));
        if (!await BinancePublicWeightGate.TryAcquireAsync(weight, TimeSpan.FromSeconds(2), cancellationToken))
        {
            throw new DomainException(
                ErrorCodes.ExchangeRateLimit,
                "Binance IP weight budget is full. The request was not sent.");
        }

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
        if (IsFrequencyBan(response.StatusCode, body))
        {
            var pause = TimeSpan.FromMinutes(1);
            if (response.Headers.RetryAfter?.Delta is { } retry && retry > TimeSpan.Zero)
            {
                pause = retry;
            }

            BinancePublicWeightGate.CoolDown(pause);
            _logger.LogWarning("Binance asked this IP to slow down for {Seconds}s. Further requests wait.", (int)pause.TotalSeconds);
            throw new DomainException(ErrorCodes.ExchangeRateLimit, TrimBinanceError(body));
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Binance signed {Path} failed: {Status} {Body}", path, (int)response.StatusCode, body);
            throw new DomainException(ErrorCodes.OrderRejected, TrimBinanceError(body));
        }

        return string.IsNullOrWhiteSpace(body)
            ? default
            : JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static bool IsFrequencyBan(System.Net.HttpStatusCode status, string body) =>
        (int)status is 418 or 429
        || body.Contains("-1003", StringComparison.Ordinal)
        || body.Contains("too frequent", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Too many requests", StringComparison.OrdinalIgnoreCase);

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
