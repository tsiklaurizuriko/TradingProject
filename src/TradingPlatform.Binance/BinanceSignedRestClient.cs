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
    /// <summary>Signed USD-M calls. Its base URL is Binance:FuturesSignedRestBaseUrl, so Testnet orders never share the mainnet market-data client.</summary>
    public const string SignedFuturesClientName = "binance-futures-signed";

    private readonly HttpClient _spot;
    private readonly HttpClient _futures;
    private readonly ILogger<BinanceSignedRestClient> _logger;
    private readonly int _recvWindowMs;

    public BinanceSignedRestClient(HttpClient http, IHttpClientFactory httpFactory, ILogger<BinanceSignedRestClient> logger, IConfiguration configuration)
    {
        _spot = http;
        _futures = httpFactory.CreateClient(SignedFuturesClientName);
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

    /// <summary>GET fapi/v1/positionSide/dual. <c>dualSidePosition=true</c> means hedge mode.</summary>
    public Task<JsonElement> GetFuturesPositionModeAsync(string apiKey, string apiSecret, CancellationToken cancellationToken) =>
        SendAsync(_futures, HttpMethod.Get, "fapi/v1/positionSide/dual", new Dictionary<string, string>(), apiKey, apiSecret, cancellationToken);

    /// <summary>DELETE fapi/v1/allOpenOrders. Cancels regular open orders only; algo orders are cancelled separately.</summary>
    public Task<JsonElement> CancelAllFuturesOpenOrdersAsync(string apiKey, string apiSecret, string symbol, CancellationToken cancellationToken) =>
        SendAsync(
            _futures,
            HttpMethod.Delete,
            "fapi/v1/allOpenOrders",
            new Dictionary<string, string> { ["symbol"] = symbol.ToUpperInvariant() },
            apiKey,
            apiSecret,
            cancellationToken);

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

    public Task<JsonElement> GetFuturesCommissionRateAsync(string apiKey, string apiSecret, string symbol, CancellationToken cancellationToken) =>
        SendAsync(
            _futures,
            HttpMethod.Get,
            "fapi/v1/commissionRate",
            new Dictionary<string, string> { ["symbol"] = symbol.ToUpperInvariant() },
            apiKey,
            apiSecret,
            cancellationToken);

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

    public const string ListenKeyPath = "fapi/v1/listenKey";

    public async Task<string> CreateFuturesListenKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        var json = await SendKeyOnlyAsync(HttpMethod.Post, apiKey, cancellationToken);
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("listenKey", out var key)
            && key.GetString() is { Length: > 0 } value
            ? value
            : throw new DomainException(ErrorCodes.ExchangeUnavailable, "Binance did not return a user-data listen key.");
    }

    public Task KeepAliveFuturesListenKeyAsync(string apiKey, CancellationToken cancellationToken) =>
        SendKeyOnlyAsync(HttpMethod.Put, apiKey, cancellationToken);

    public Task CloseFuturesListenKeyAsync(string apiKey, CancellationToken cancellationToken) =>
        SendKeyOnlyAsync(HttpMethod.Delete, apiKey, cancellationToken);

    /// <summary>USER_STREAM endpoints take the API key header only. The secret is not sent or used.</summary>
    private async Task<JsonElement> SendKeyOnlyAsync(HttpMethod method, string apiKey, CancellationToken cancellationToken)
    {
        if (!await BinancePublicWeightGate.TryAcquireAsync(1, TimeSpan.FromSeconds(5), cancellationToken))
        {
            throw new DomainException(ErrorCodes.ExchangeRateLimit, "Binance IP weight budget is full. The request was not sent.");
        }

        using var request = new HttpRequestMessage(method, ListenKeyPath);
        request.Headers.TryAddWithoutValidation("X-MBX-APIKEY", apiKey);
        HttpResponseMessage response;
        try
        {
            response = await _futures.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DomainException(ErrorCodes.ExchangeUnavailable, "Binance listen key request failed.", new Dictionary<string, object?> { ["error"] = ex.Message });
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new DomainException(ErrorCodes.ExchangeUnavailable, TrimBinanceError(body));
        }

        return string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<JsonElement>(body);
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
        if (!await BinancePublicWeightGate.TryAcquireAsync(weight, WeightWait(method, path), cancellationToken))
        {
            throw new DomainException(
                ErrorCodes.ExchangeRateLimit,
                "Binance IP weight budget is full. The request was not sent.");
        }

        await EnsureClockAsync(client, force: false, cancellationToken);
        var (response, body) = await SendSignedOnceAsync(client, method, path, fields, apiKey, apiSecret, cancellationToken);
        if (!response.IsSuccessStatusCode && ReadBinanceCode(body) == -1021)
        {
            response.Dispose();
            await EnsureClockAsync(client, force: true, cancellationToken);
            (response, body) = await SendSignedOnceAsync(client, method, path, fields, apiKey, apiSecret, cancellationToken);
        }

        using var sent = response;
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
            var level = ReadBinanceCode(body) is { } code && BenignCodes.Contains(code) ? LogLevel.Debug : LogLevel.Warning;
            _logger.Log(level, "Binance signed {Path} failed: {Status} {Body}", path, (int)response.StatusCode, body);
            throw new DomainException(ClassifyFailure((int)response.StatusCode, body), TrimBinanceError(body));
        }

        return string.IsNullOrWhiteSpace(body)
            ? default
            : JsonSerializer.Deserialize<JsonElement>(body);
    }

    /// <summary>
    /// -4046 margin type unchanged and -4059 position side unchanged are success to callers.
    /// -2011 and -2013 (order unknown) are read by callers as confirmed absent.
    /// </summary>
    private static readonly HashSet<int> BenignCodes = [-4046, -4059, -2011, -2013];

    private async Task<(HttpResponseMessage Response, string Body)> SendSignedOnceAsync(
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
        query["timestamp"] = ServerNow(client).ToString(CultureInfo.InvariantCulture);
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
        return (response, body);
    }

    private sealed class ClockState
    {
        public long OffsetMs;
        public DateTimeOffset SyncedAt = DateTimeOffset.MinValue;
        public readonly SemaphoreSlim Gate = new(1, 1);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ClockState> Clocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan ClockResync = TimeSpan.FromMinutes(10);

    private static ClockState ClockFor(HttpClient client) =>
        Clocks.GetOrAdd(client.BaseAddress?.ToString() ?? "", _ => new ClockState());

    private static long ServerNow(HttpClient client) =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Volatile.Read(ref ClockFor(client).OffsetMs);

    /// <summary>
    /// Binance rejects a signed request whose timestamp is off its server clock by more than recvWindow (-1021).
    /// The local clock offset is measured against the server time endpoint and applied to every timestamp.
    /// </summary>
    private async Task EnsureClockAsync(HttpClient client, bool force, CancellationToken cancellationToken)
    {
        var state = ClockFor(client);
        if (!force && DateTimeOffset.UtcNow - state.SyncedAt < ClockResync)
        {
            return;
        }

        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!force && DateTimeOffset.UtcNow - state.SyncedAt < ClockResync)
            {
                return;
            }

            var timePath = ReferenceEquals(client, _futures) ? "fapi/v1/time" : "api/v3/time";
            var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using var response = await client.GetAsync(timePath, cancellationToken);
            var receivedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var json = JsonSerializer.Deserialize<JsonElement>(body);
            if (json.ValueKind != JsonValueKind.Object
                || !json.TryGetProperty("serverTime", out var serverEl)
                || !serverEl.TryGetInt64(out var serverTime))
            {
                return;
            }

            var offset = serverTime - ((sentAt + receivedAt) / 2);
            Volatile.Write(ref state.OffsetMs, offset);
            if (Math.Abs(offset) > 1000)
            {
                _logger.LogWarning("Local clock is {OffsetMs} ms off Binance server time. Signed requests use the server clock.", offset);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Binance server time sync failed. The previous clock offset is kept.");
        }
        finally
        {
            state.SyncedAt = DateTimeOffset.UtcNow;
            state.Gate.Release();
        }
    }

    /// <summary>
    /// 5xx, -1006 and -1007 mean Binance may or may not have executed the request, so callers must look
    /// the order up instead of treating it as rejected.
    /// </summary>
    internal static string ClassifyFailure(int status, string body)
    {
        if (status >= 500)
        {
            return ErrorCodes.ExchangeUnavailable;
        }

        var code = ReadBinanceCode(body);
        return code is -1006 or -1007 or -1001 ? ErrorCodes.ExchangeUnavailable : ErrorCodes.OrderRejected;
    }

    /// <summary>Signed calls that change exchange state. They wait longer for weight so a stop or close is not dropped.</summary>
    internal static TimeSpan WeightWait(HttpMethod method, string path) =>
        method != HttpMethod.Get && path.Contains("order", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(10)
            : TimeSpan.FromSeconds(2);

    private static int? ReadBinanceCode(string body)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            if (json.ValueKind == JsonValueKind.Object
                && json.TryGetProperty("code", out var code)
                && code.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch
        {
            // not JSON
        }

        return null;
    }

    private static bool IsFrequencyBan(System.Net.HttpStatusCode status, string body) =>
        (int)status is 418 or 429
        || body.Contains("-1003", StringComparison.Ordinal)
        || body.Contains("too frequent", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Too many requests", StringComparison.OrdinalIgnoreCase);

    /// <summary>Keeps the Binance error code in the message; callers match on codes such as -4130 and -2013.</summary>
    internal static string TrimBinanceError(string body)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("msg", out var msg))
            {
                var text = msg.GetString() ?? "Binance rejected the request.";
                return ReadBinanceCode(body) is { } code ? $"{text} (code {code})" : text;
            }
        }
        catch
        {
            // fall through
        }

        return string.IsNullOrWhiteSpace(body) ? "Binance rejected the request." : body.Trim();
    }
}
