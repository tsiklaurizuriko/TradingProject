using System.Globalization;
using System.Text.Json;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

public sealed class BinanceLiveExchangeConnector : IExchangeConnector
{
    private readonly BinanceSignedRestClient _signed;
    private readonly IExchangeCredentialStore _credentials;
    private readonly Guid _accountId;

    public BinanceLiveExchangeConnector(
        BinanceSignedRestClient signed,
        IExchangeCredentialStore credentials,
        Guid accountId)
    {
        _signed = signed;
        _credentials = credentials;
        _accountId = accountId;
    }

    public string Name => "BinanceUsdMLive";
    public TradingMode Mode => TradingMode.Live;

    public async Task<ExchangeAccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        var (key, secret) = await RequireKeys(cancellationToken);
        var account = await _signed.GetFuturesAccountAsync(key, secret, cancellationToken);
        var canTrade = !account.TryGetProperty("canTrade", out var trade) || trade.GetBoolean();
        var balances = ReadFuturesBalances(account);
        return new ExchangeAccountSnapshot("USDT-M", balances, canTrade);
    }

    public async Task<IReadOnlyList<ExchangeBalance>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetAccountAsync(cancellationToken);
        return snapshot.Balances;
    }

    public Task<SymbolFilters> GetSymbolInformationAsync(string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SymbolFilters(
            symbol.ToUpperInvariant(),
            symbol.ToUpperInvariant().Replace("USDT", "", StringComparison.Ordinal),
            "USDT",
            0.01m,
            0.00001m,
            0.00001m,
            5m,
            2,
            5));

    public Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExchangeOrder>>([]);

    public Task<ExchangeOrder?> GetOrderAsync(string? clientOrderId, string? exchangeOrderId, string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult<ExchangeOrder?>(null);

    public async Task<ExchangeOrder> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        var (key, secret) = await RequireKeys(cancellationToken);
        var payload = await _signed.PlaceFuturesMarketOrderAsync(
            key,
            secret,
            request.Symbol,
            request.Side,
            request.Quantity,
            request.ClientOrderId,
            request.ReduceOnly,
            cancellationToken);
        var mapped = MapOrder(payload, request);
        if ((mapped.Status == OrderStatus.Filled || mapped.Status == OrderStatus.PartiallyFilled)
            && !string.IsNullOrWhiteSpace(mapped.ExchangeOrderId))
        {
            try
            {
                var trades = await _signed.GetFuturesUserTradesAsync(
                    key,
                    secret,
                    request.Symbol,
                    mapped.ExchangeOrderId,
                    cancellationToken);
                var fromTrades = SumCommission(trades);
                if (fromTrades != 0m)
                {
                    mapped = mapped with { Fee = fromTrades };
                }
            }
            catch (DomainException)
            {
            }
        }

        return mapped;
    }

    public async Task PrepareSymbolRiskAsync(string symbol, MarginMode marginMode, int leverage, CancellationToken cancellationToken = default)
    {
        if (marginMode != MarginMode.Isolated)
        {
            throw new DomainException(ErrorCodes.RiskLimitExceeded, "Isolated margin only. Cross is not allowed.");
        }

        var (key, secret) = await RequireKeys(cancellationToken);
        await _signed.SetFuturesMarginTypeAsync(key, secret, symbol, "ISOLATED", cancellationToken);
        var cap = await GetMaxIsolatedLeverageAsync(symbol, cancellationToken);
        var used = Math.Clamp(leverage, 1, cap);
        await _signed.SetFuturesLeverageAsync(key, secret, symbol, used, cancellationToken);
        var positions = await _signed.GetFuturesPositionsAsync(key, secret, cancellationToken);
        if (!IsIsolated(positions, symbol))
        {
            throw new DomainException(ErrorCodes.RiskLimitExceeded, "Could not switch this coin to Isolated margin.");
        }
    }

    public async Task<int> GetMaxIsolatedLeverageAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var (key, secret) = await RequireKeys(cancellationToken);
        try
        {
            var payload = await _signed.GetFuturesLeverageBracketsAsync(key, secret, symbol, cancellationToken);
            var max = 1;
            foreach (var row in ReadBrackets(payload, symbol))
            {
                if (row > max)
                {
                    max = row;
                }
            }

            return Math.Max(1, max);
        }
        catch (DomainException)
        {
            return 1;
        }
    }

    public async Task PlaceClosePositionStopsAsync(
        string symbol,
        OrderSide closeSide,
        decimal stopLossPrice,
        decimal takeProfitPrice,
        string stopClientOrderId,
        string takeProfitClientOrderId,
        CancellationToken cancellationToken = default)
    {
        var (key, secret) = await RequireKeys(cancellationToken);
        Exception? stopFailure = null;
        Exception? takeFailure = null;
        try
        {
            await _signed.PlaceFuturesClosePositionOrderAsync(
                key,
                secret,
                symbol,
                closeSide,
                "STOP_MARKET",
                stopLossPrice,
                stopClientOrderId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            stopFailure = ex;
        }

        try
        {
            await _signed.PlaceFuturesClosePositionOrderAsync(
                key,
                secret,
                symbol,
                closeSide,
                "TAKE_PROFIT_MARKET",
                takeProfitPrice,
                takeProfitClientOrderId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            takeFailure = ex;
        }

        if (stopFailure is not null && takeFailure is not null)
        {
            throw new DomainException(
                ErrorCodes.OrderRejected,
                $"STOP_MARKET failed: {stopFailure.Message}; TAKE_PROFIT_MARKET failed: {takeFailure.Message}");
        }

        if (stopFailure is not null)
        {
            throw new DomainException(
                ErrorCodes.OrderRejected,
                $"STOP_MARKET failed: {stopFailure.Message}. TAKE_PROFIT_MARKET was placed.");
        }

        if (takeFailure is not null)
        {
            throw new DomainException(
                ErrorCodes.OrderRejected,
                $"TAKE_PROFIT_MARKET failed: {takeFailure.Message}. STOP_MARKET was placed.");
        }
    }

    public async Task CancelOrderAsync(string symbol, string? clientOrderId, string? exchangeOrderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId) && string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            return;
        }

        var (key, secret) = await RequireKeys(cancellationToken);
        try
        {
            await _signed.CancelFuturesOrderAsync(key, secret, symbol, clientOrderId, exchangeOrderId, cancellationToken);
        }
        catch (DomainException)
        {
            // Already filled, cancelled, or unknown — position protection may have already triggered.
        }
    }

    public async Task CancelAllOrdersAsync(string symbol, CancellationToken cancellationToken = default)
    {
        await CancelOrderAsync(symbol, null, null, cancellationToken);
    }

    public Task SubscribeMarketDataAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SubscribeUserDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private async Task<(string ApiKey, string ApiSecret)> RequireKeys(CancellationToken cancellationToken)
    {
        var keys = await _credentials.GetAsync(_accountId, cancellationToken);
        if (keys is null)
        {
            throw new DomainException(ErrorCodes.LiveTradingDisabled, "Save a Binance API key before starting a live bot.");
        }

        return keys.Value;
    }

    private static IReadOnlyList<ExchangeBalance> ReadFuturesBalances(JsonElement account)
    {
        if (account.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            var list = new List<ExchangeBalance>();
            foreach (var row in assets.EnumerateArray())
            {
                var asset = row.TryGetProperty("asset", out var assetEl) ? assetEl.GetString() ?? "" : "";
                var free = row.TryGetProperty("availableBalance", out var avail) ? Dec(avail) : 0m;
                var wallet = row.TryGetProperty("walletBalance", out var walletEl) ? Dec(walletEl) : free;
                var locked = Math.Max(0m, wallet - free);
                if (free == 0m && locked == 0m)
                {
                    continue;
                }

                list.Add(new ExchangeBalance(asset, free, locked));
            }

            if (list.Count > 0)
            {
                return list;
            }
        }

        var available = account.TryGetProperty("availableBalance", out var availEl) ? Dec(availEl) : 0m;
        return available > 0m ? [new ExchangeBalance("USDT", available, 0m)] : [];
    }

    private static IReadOnlyList<ExchangeBalance> ReadBalances(JsonElement account)
    {
        if (!account.TryGetProperty("balances", out var rows))
        {
            return [];
        }

        var list = new List<ExchangeBalance>();
        foreach (var row in rows.EnumerateArray())
        {
            var asset = row.GetProperty("asset").GetString() ?? "";
            var free = Dec(row.GetProperty("free"));
            var locked = Dec(row.GetProperty("locked"));
            if (free == 0m && locked == 0m)
            {
                continue;
            }

            list.Add(new ExchangeBalance(asset, free, locked));
        }

        return list;
    }

    private static ExchangeOrder MapOrder(JsonElement payload, PlaceOrderRequest request)
    {
        var statusText = payload.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : "NEW";
        var status = statusText switch
        {
            "FILLED" => OrderStatus.Filled,
            "PARTIALLY_FILLED" => OrderStatus.PartiallyFilled,
            "CANCELED" or "CANCELLED" => OrderStatus.Cancelled,
            "REJECTED" => OrderStatus.Rejected,
            "EXPIRED" => OrderStatus.Expired,
            _ => OrderStatus.Submitted
        };
        var executed = payload.TryGetProperty("executedQty", out var qtyEl) ? Dec(qtyEl) : request.Quantity;
        var quote = payload.TryGetProperty("cumQuote", out var quoteEl)
            ? Dec(quoteEl)
            : payload.TryGetProperty("cummulativeQuoteQty", out var spotQuote)
                ? Dec(spotQuote)
                : 0m;
        var avg = executed > 0m && quote > 0m ? quote / executed : request.Price;
        long? transact = payload.TryGetProperty("transactTime", out var timeEl) ? timeEl.GetInt64() : null;
        return new ExchangeOrder(
            payload.TryGetProperty("clientOrderId", out var cid) ? cid.GetString() ?? request.ClientOrderId : request.ClientOrderId,
            payload.TryGetProperty("orderId", out var oid) ? oid.ToString() : null,
            request.Symbol.ToUpperInvariant(),
            request.Side,
            request.Type,
            status,
            request.Quantity,
            executed,
            avg,
            avg,
            transact is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(transact.Value),
            ReadCommission(payload));
    }

    private static decimal ReadCommission(JsonElement payload)
    {
        var total = 0m;
        if (payload.TryGetProperty("commission", out var commissionEl))
        {
            total += Dec(commissionEl);
        }

        if (payload.TryGetProperty("fills", out var fills) && fills.ValueKind == JsonValueKind.Array)
        {
            foreach (var fill in fills.EnumerateArray())
            {
                if (fill.TryGetProperty("commission", out var fillCommission))
                {
                    total += Dec(fillCommission);
                }
            }
        }

        return total;
    }

    private static decimal SumCommission(JsonElement trades)
    {
        if (trades.ValueKind != JsonValueKind.Array)
        {
            return 0m;
        }

        var total = 0m;
        foreach (var trade in trades.EnumerateArray())
        {
            if (trade.TryGetProperty("commissionAsset", out var assetEl)
                && !string.Equals(assetEl.GetString(), "USDT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (trade.TryGetProperty("commission", out var commissionEl))
            {
                total += Dec(commissionEl);
            }
        }

        return total;
    }

    private static decimal Dec(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.Parse(element.GetString() ?? "0", CultureInfo.InvariantCulture);
    }

    private static bool IsIsolated(JsonElement positions, string symbol)
    {
        if (positions.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var row in positions.EnumerateArray())
        {
            if (!row.TryGetProperty("symbol", out var name)
                || !string.Equals(name.GetString(), symbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return row.TryGetProperty("marginType", out var margin)
                && string.Equals(margin.GetString(), "isolated", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static IEnumerable<int> ReadBrackets(JsonElement payload, string symbol)
    {
        if (payload.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in payload.EnumerateArray())
            {
                if (row.TryGetProperty("symbol", out var name)
                    && !string.Equals(name.GetString(), symbol, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var value in ReadBracketList(row))
                {
                    yield return value;
                }
            }

            yield break;
        }

        foreach (var value in ReadBracketList(payload))
        {
            yield return value;
        }
    }

    private static IEnumerable<int> ReadBracketList(JsonElement row)
    {
        if (!row.TryGetProperty("brackets", out var brackets) || brackets.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var bracket in brackets.EnumerateArray())
        {
            if (bracket.TryGetProperty("initialLeverage", out var lev) && lev.TryGetInt32(out var value))
            {
                yield return value;
            }
        }
    }
}

public sealed class BinanceLiveExchangeConnectorFactory : ILiveExchangeConnectorFactory
{
    private readonly BinanceSignedRestClient _signed;
    private readonly IExchangeCredentialStore _credentials;

    public BinanceLiveExchangeConnectorFactory(BinanceSignedRestClient signed, IExchangeCredentialStore credentials)
    {
        _signed = signed;
        _credentials = credentials;
    }

    public IExchangeConnector Create(Guid? exchangeAccountId)
    {
        if (exchangeAccountId is null || exchangeAccountId == Guid.Empty)
        {
            throw new DomainException(ErrorCodes.LiveTradingDisabled, "A live Binance account is required.");
        }

        return new BinanceLiveExchangeConnector(_signed, _credentials, exchangeAccountId.Value);
    }
}
