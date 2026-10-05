using System.Globalization;
using System.Text.Json;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

public sealed class BinanceLiveExchangeConnector : IExchangeConnector
{
    private readonly BinanceSignedRestClient _signed;
    private readonly IExchangeCredentialStore _credentials;
    private readonly IPublicMarketDataClient _market;
    private readonly Guid _accountId;

    public BinanceLiveExchangeConnector(
        BinanceSignedRestClient signed,
        IExchangeCredentialStore credentials,
        IPublicMarketDataClient market,
        Guid accountId)
    {
        _signed = signed;
        _credentials = credentials;
        _market = market;
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
        MapFiltersAsync(symbol, cancellationToken);

    private async Task<SymbolFilters> MapFiltersAsync(string symbol, CancellationToken cancellationToken)
    {
        var name = symbol.ToUpperInvariant();
        var ranked = (await _market.GetPaperUniverseAsync(cancellationToken))
            .FirstOrDefault(row => string.Equals(row.Symbol, name, StringComparison.OrdinalIgnoreCase));
        if (ranked is null)
        {
            throw new DomainException(ErrorCodes.InvalidSymbol, $"{name} is not a Binance USD-M USDT perpetual.");
        }

        return new SymbolFilters(
            ranked.Symbol,
            ranked.BaseAsset,
            ranked.QuoteAsset,
            ranked.TickSize,
            ranked.StepSize,
            ranked.MinQuantity,
            ranked.MinNotional,
            ranked.PricePrecision,
            ranked.QuantityPrecision);
    }

    public Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExchangeOrder>>([]);

    /// <summary>
    /// Read-only query. A Binance "order does not exist" (-2013/-2011) from both the order and algo
    /// endpoints is <see cref="OrderLookupKind.ConfirmedAbsent"/>. Timeouts, 429, and 5xx stay unavailable.
    /// Commission on <see cref="ExchangeOrder.Fee"/> is the cumulative commission for that order, with the
    /// asset read from the payload or from user trades. It is not a per-fill delta and it is not assumed
    /// to be USDT. A missing amount or asset leaves <see cref="ExchangeOrder.FeeKnown"/> false.
    /// </summary>
    public async Task<OrderLookup> GetOrderAsync(string? clientOrderId, string? exchangeOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        var (key, secret) = await RequireKeys(cancellationToken);
        var regular = await ReadOrderAsync(
            () => _signed.GetFuturesOrderAsync(key, secret, symbol, clientOrderId, exchangeOrderId, cancellationToken),
            payload => MapOrder(payload, new PlaceOrderRequest(clientOrderId ?? "", symbol, OrderSide.Buy, OrderType.Market, 0m, null, TimeSpan.Zero)));
        if (regular.Kind == OrderLookupKind.Found)
        {
            return await WithCumulativeFeeAsync(key, secret, regular, cancellationToken);
        }

        if (regular.Kind == OrderLookupKind.Unavailable)
        {
            return regular;
        }

        var algo = await ReadOrderAsync(
            () => _signed.GetFuturesAlgoOrderAsync(key, secret, symbol, clientOrderId, exchangeOrderId, cancellationToken),
            MapAlgoOrder);
        if (algo.Kind == OrderLookupKind.Found)
        {
            return algo;
        }

        if (algo.Kind == OrderLookupKind.Unavailable)
        {
            return algo;
        }

        return OrderLookup.Absent("Binance confirmed this client id on neither the order nor the algo endpoint. The order was not sent again.");
    }

    private async Task<OrderLookup> WithCumulativeFeeAsync(
        string key,
        string secret,
        OrderLookup lookup,
        CancellationToken cancellationToken)
    {
        var order = lookup.Order;
        if (order is null || string.IsNullOrWhiteSpace(order.ExchangeOrderId))
        {
            return lookup;
        }

        try
        {
            var trades = await _signed.GetFuturesUserTradesAsync(key, secret, order.Symbol, order.ExchangeOrderId, cancellationToken);
            order = WithTradePrice(order, trades);
            lookup = OrderLookup.Found(order);
            var commission = CommissionReader.FromUserTrades(trades);
            if (commission.Known)
            {
                if (order.FeeKnown
                    && !string.IsNullOrWhiteSpace(order.FeeAsset)
                    && !string.Equals(order.FeeAsset, commission.Asset, StringComparison.OrdinalIgnoreCase))
                {
                    return OrderLookup.Unavailable("Order commission asset does not match user trades. The fill was not booked.");
                }

                return OrderLookup.Found(order with { Fee = commission.Amount, FeeKnown = true, FeeAsset = commission.Asset });
            }

            if (order.FeeKnown && !string.IsNullOrWhiteSpace(order.FeeAsset))
            {
                return lookup;
            }

            return OrderLookup.Found(order with { Fee = 0m, FeeKnown = false, FeeAsset = null });
        }
        catch (DomainException)
        {
            if (order.FeeKnown && string.IsNullOrWhiteSpace(order.FeeAsset))
            {
                return OrderLookup.Found(order with { Fee = 0m, FeeKnown = false, FeeAsset = null });
            }
        }

        return lookup;
    }

    private static async Task<OrderLookup> ReadOrderAsync(Func<Task<JsonElement>> query, Func<JsonElement, ExchangeOrder> map)
    {
        try
        {
            return OrderLookup.Found(map(await query()));
        }
        catch (DomainException ex) when (IsConfirmedAbsent(ex))
        {
            return OrderLookup.Absent(ex.Message);
        }
        catch (DomainException ex)
        {
            return OrderLookup.Unavailable(ex.Message);
        }
    }

    private static bool IsConfirmedAbsent(DomainException ex) =>
        ex.Message.Contains("-2013", StringComparison.Ordinal)
        || ex.Message.Contains("-2011", StringComparison.Ordinal)
        || ex.Message.Contains("Order does not exist", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Unknown order", StringComparison.OrdinalIgnoreCase);

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
        mapped = await AwaitMarketResultAsync(key, secret, request, mapped, cancellationToken);
        if (mapped.FilledQuantity > 0m && !string.IsNullOrWhiteSpace(mapped.ExchangeOrderId))
        {
            JsonElement? read = null;
            foreach (var delay in TradePriceDelays)
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                    try
                    {
                        var again = await _signed.GetFuturesOrderAsync(key, secret, request.Symbol, null, mapped.ExchangeOrderId, cancellationToken);
                        mapped = MapOrder(again, request) with { Fee = mapped.Fee, FeeKnown = mapped.FeeKnown, FeeAsset = mapped.FeeAsset };
                    }
                    catch (DomainException)
                    {
                    }
                }

                try
                {
                    read = await _signed.GetFuturesUserTradesAsync(
                        key,
                        secret,
                        request.Symbol,
                        mapped.ExchangeOrderId,
                        cancellationToken);
                    mapped = WithTradePrice(mapped, read.Value);
                }
                catch (DomainException)
                {
                }

                if (mapped.AverageFillPrice is > 0m)
                {
                    break;
                }
            }

            if (read is { } trades)
            {
                var fromTrades = CommissionReader.FromUserTrades(trades);
                if (fromTrades.Known)
                {
                    if (mapped.FeeKnown
                        && !string.IsNullOrWhiteSpace(mapped.FeeAsset)
                        && !string.Equals(mapped.FeeAsset, fromTrades.Asset, StringComparison.OrdinalIgnoreCase))
                    {
                        mapped = mapped with { Fee = 0m, FeeKnown = false, FeeAsset = null };
                    }
                    else
                    {
                        mapped = mapped with { Fee = fromTrades.Amount, FeeKnown = true, FeeAsset = fromTrades.Asset };
                    }
                }
                else if (!mapped.FeeKnown || string.IsNullOrWhiteSpace(mapped.FeeAsset))
                {
                    mapped = mapped with { Fee = 0m, FeeKnown = false, FeeAsset = null };
                }
            }
        }

        return mapped;
    }

    /// <summary>User trades can lag the order by a few seconds. Zero is the first read without waiting.</summary>
    private static readonly TimeSpan[] TradePriceDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
        TimeSpan.FromMilliseconds(2000)
    ];

    private static readonly TimeSpan[] MarketResultDelays =
    [
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromMilliseconds(1500)
    ];

    /// <summary>
    /// USD-M can answer a MARKET order before matching finishes (status NEW, executedQty 0, avgPrice 0).
    /// The order is read again until Binance reports a final status with a usable price.
    /// </summary>
    private async Task<ExchangeOrder> AwaitMarketResultAsync(
        string key,
        string secret,
        PlaceOrderRequest request,
        ExchangeOrder mapped,
        CancellationToken cancellationToken)
    {
        if (request.Type != OrderType.Market || string.IsNullOrWhiteSpace(mapped.ExchangeOrderId))
        {
            return mapped;
        }

        foreach (var delay in MarketResultDelays)
        {
            if (!NeedsMarketResult(mapped))
            {
                return mapped;
            }

            await Task.Delay(delay, cancellationToken);
            try
            {
                var payload = await _signed.GetFuturesOrderAsync(key, secret, request.Symbol, null, mapped.ExchangeOrderId, cancellationToken);
                mapped = MapOrder(payload, request) with { Fee = mapped.Fee, FeeKnown = mapped.FeeKnown, FeeAsset = mapped.FeeAsset };
            }
            catch (DomainException)
            {
            }
        }

        return mapped;
    }

    private static bool NeedsMarketResult(ExchangeOrder order) =>
        order.Status is OrderStatus.New or OrderStatus.Submitted or OrderStatus.Submitting
        || (order.FilledQuantity > 0m && order.AverageFillPrice is not > 0m);

    /// <summary>Fills a missing price from user trades when those trades cover the whole executed quantity.</summary>
    private static ExchangeOrder WithTradePrice(ExchangeOrder order, JsonElement trades)
    {
        if (order.FilledQuantity <= 0m || order.AverageFillPrice is > 0m)
        {
            return order;
        }

        if (CommissionReader.FillFromUserTrades(trades) is not { } fill || fill.Quantity != order.FilledQuantity)
        {
            return order;
        }

        var average = fill.Quote / fill.Quantity;
        return order with { Price = average, AverageFillPrice = average, CumulativeQuote = fill.Quote };
    }

    public async Task PrepareSymbolRiskAsync(string symbol, MarginMode marginMode, int leverage, CancellationToken cancellationToken = default)
    {
        if (marginMode != MarginMode.Isolated)
        {
            throw new DomainException(ErrorCodes.RiskLimitExceeded, "Isolated margin only. Cross is not allowed.");
        }

        var hedge = await IsHedgeModeAsync(cancellationToken);
        if (hedge != false)
        {
            throw new DomainException(
                ErrorCodes.RiskLimitExceeded,
                hedge == true
                    ? "This Binance account is in Hedge position mode. Switch it to One-way mode before starting a bot."
                    : "Could not read the Binance position mode. No order was sent.");
        }

        var (key, secret) = await RequireKeys(cancellationToken);
        await _signed.SetFuturesMarginTypeAsync(key, secret, symbol, "ISOLATED", cancellationToken);
        var cap = await GetMaxIsolatedLeverageAsync(symbol, cancellationToken);
        var used = cap > 0 ? Math.Min(leverage, cap) : leverage;
        used = Math.Max(1, used);
        await _signed.SetFuturesLeverageAsync(key, secret, symbol, used, cancellationToken);
        JsonElement positions;
        try
        {
            positions = await _signed.GetFuturesPositionsAsync(key, secret, cancellationToken, symbol);
        }
        catch (Exception)
        {
            return;
        }

        if (TryReadMarginType(positions, symbol, out var marginType)
            && !string.Equals(marginType, "isolated", StringComparison.OrdinalIgnoreCase))
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
            var max = 0;
            foreach (var row in ReadBrackets(payload, symbol))
            {
                if (row > max)
                {
                    max = row;
                }
            }

            return max;
        }
        catch (DomainException)
        {
            return 0;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (decimal Percent, DateTimeOffset At)> TakerFees = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan TakerFeeTtl = TimeSpan.FromHours(6);

    public async Task<decimal?> GetTakerFeePercentAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{_accountId:N}:{symbol}";
        if (TakerFees.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.At < TakerFeeTtl)
        {
            return cached.Percent;
        }

        try
        {
            var (key, secret) = await RequireKeys(cancellationToken);
            var payload = await _signed.GetFuturesCommissionRateAsync(key, secret, symbol, cancellationToken);
            if (ReadTakerFeePercent(payload) is not { } percent)
            {
                return null;
            }

            TakerFees[cacheKey] = (percent, DateTimeOffset.UtcNow);
            return percent;
        }
        catch (DomainException)
        {
            return null;
        }
    }

    public async Task<MaintenanceBracket?> GetMaintenanceBracketAsync(string symbol, decimal notional, CancellationToken cancellationToken = default)
    {
        try
        {
            var (key, secret) = await RequireKeys(cancellationToken);
            var payload = await _signed.GetFuturesLeverageBracketsAsync(key, secret, symbol, cancellationToken);
            return ReadMaintenanceBracket(payload, symbol, notional);
        }
        catch (DomainException)
        {
            return null;
        }
    }

    /// <summary>takerCommissionRate is a fraction ("0.000400"); the result is percent (0.04).</summary>
    public static decimal? ReadTakerFeePercent(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("takerCommissionRate", out var rate))
        {
            return null;
        }

        var value = rate.ValueKind switch
        {
            JsonValueKind.String when decimal.TryParse(rate.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            JsonValueKind.Number when rate.TryGetDecimal(out var number) => number,
            _ => -1m
        };
        return value is >= 0m and < 0.01m ? value * 100m : null;
    }

    /// <summary>The bracket with notionalFloor ≤ notional &lt; notionalCap; the top bracket when notional is above every cap.</summary>
    public static MaintenanceBracket? ReadMaintenanceBracket(JsonElement payload, string symbol, decimal notional)
    {
        var row = payload;
        if (payload.ValueKind == JsonValueKind.Array)
        {
            row = payload.EnumerateArray().FirstOrDefault(item =>
                item.ValueKind == JsonValueKind.Object
                && (!item.TryGetProperty("symbol", out var name) || string.Equals(name.GetString(), symbol, StringComparison.OrdinalIgnoreCase)));
        }

        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("brackets", out var brackets) || brackets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        MaintenanceBracket? top = null;
        var topFloor = -1m;
        foreach (var bracket in brackets.EnumerateArray())
        {
            if (!bracket.TryGetProperty("maintMarginRatio", out var ratio)
                || !bracket.TryGetProperty("notionalFloor", out var floor)
                || !bracket.TryGetProperty("notionalCap", out var cap))
            {
                continue;
            }

            var found = new MaintenanceBracket(Dec(ratio), bracket.TryGetProperty("cum", out var cum) ? Dec(cum) : 0m);
            var low = Dec(floor);
            if (notional >= low && notional < Dec(cap))
            {
                return found;
            }

            if (low > topFloor)
            {
                topFloor = low;
                top = found;
            }
        }

        return notional >= topFloor && topFloor >= 0m ? top : null;
    }

    public async Task<ProtectiveStopsResult> PlaceClosePositionStopsAsync(
        string symbol,
        OrderSide closeSide,
        decimal stopLossPrice,
        decimal takeProfitPrice,
        string stopClientOrderId,
        string takeProfitClientOrderId,
        CancellationToken cancellationToken = default,
        bool placeStop = true,
        bool placeTake = true,
        bool acceptExisting = true)
    {
        var (key, secret) = await RequireKeys(cancellationToken);
        string? stopError = null;
        string? takeError = null;
        if (placeStop)
        {
            stopError = await TryPlaceCloseStopAsync(
                key, secret, symbol, closeSide, "STOP_MARKET", stopLossPrice, stopClientOrderId, cancellationToken, acceptExisting);
        }

        if (placeTake)
        {
            takeError = await TryPlaceCloseStopAsync(
                key, secret, symbol, closeSide, "TAKE_PROFIT_MARKET", takeProfitPrice, takeProfitClientOrderId, cancellationToken, acceptExisting);
        }

        return new ProtectiveStopsResult(!placeStop || stopError is null, !placeTake || takeError is null, stopError, takeError);
    }

    private async Task<string?> TryPlaceCloseStopAsync(
        string key,
        string secret,
        string symbol,
        OrderSide closeSide,
        string type,
        decimal stopPrice,
        string clientOrderId,
        CancellationToken cancellationToken,
        bool acceptExisting)
    {
        if (stopPrice <= 0m)
        {
            return "Protective trigger price is invalid.";
        }

        var error = await PlaceOnceAsync(stopPrice, priceProtect: true);
        if (IsAccepted(error, acceptExisting))
        {
            return null;
        }

        if (ProtectiveOrderMath.IsNoOpenPosition(error))
        {
            return error;
        }

        if (IsImmediateTrigger(error))
        {
            if (!acceptExisting)
            {
                return error;
            }

            var resting = await TryRestingTriggerAsync(key, secret, symbol, closeSide, type, cancellationToken);
            if (resting > 0m && resting != stopPrice)
            {
                var nudged = await PlaceOnceAsync(resting, priceProtect: false);
                if (IsAccepted(nudged, acceptExisting))
                {
                    return null;
                }

                return nudged;
            }

            return error;
        }

        var fallback = await PlaceOnceAsync(stopPrice, priceProtect: false);
        return IsAccepted(fallback, acceptExisting) ? null : fallback;

        async Task<string?> PlaceOnceAsync(decimal trigger, bool priceProtect)
        {
            try
            {
                await _signed.PlaceFuturesConditionalCloseAlgoAsync(
                    key,
                    secret,
                    symbol,
                    closeSide,
                    type,
                    trigger,
                    clientOrderId,
                    cancellationToken,
                    priceProtect);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    private async Task<decimal> TryRestingTriggerAsync(
        string key,
        string secret,
        string symbol,
        OrderSide closeSide,
        string type,
        CancellationToken cancellationToken)
    {
        try
        {
            var positions = await _signed.GetFuturesPositionsAsync(key, secret, cancellationToken);
            var mark = ReadMark(positions, symbol);
            var tick = (await MapFiltersAsync(symbol, cancellationToken)).TickSize;
            return ProtectiveOrderMath.RestingTrigger(
                mark,
                tick,
                closingShort: closeSide == OrderSide.Buy,
                stop: ProtectiveOrderMath.IsStopOrder(type));
        }
        catch (Exception)
        {
            return 0m;
        }
    }

    private static decimal ReadMark(JsonElement positions, string symbol)
    {
        if (positions.ValueKind != JsonValueKind.Array)
        {
            return 0m;
        }

        foreach (var row in positions.EnumerateArray())
        {
            var name = row.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() : null;
            if (!string.Equals(name, symbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var amount = row.TryGetProperty("positionAmt", out var amountEl) ? Dec(amountEl) : 0m;
            if (amount == 0m)
            {
                continue;
            }

            return row.TryGetProperty("markPrice", out var markEl) ? Dec(markEl) : 0m;
        }

        return 0m;
    }

    private static bool TryReadMarginType(JsonElement positions, string symbol, out string marginType)
    {
        foreach (var row in EnumeratePositionRows(positions))
        {
            if (!row.TryGetProperty("symbol", out var name)
                || !string.Equals(name.GetString(), symbol, StringComparison.OrdinalIgnoreCase)
                || !row.TryGetProperty("marginType", out var margin))
            {
                continue;
            }

            marginType = margin.GetString() ?? "";
            return marginType.Length > 0;
        }

        marginType = "";
        return false;
    }

    private static IEnumerable<JsonElement> EnumeratePositionRows(JsonElement positions)
    {
        if (positions.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in positions.EnumerateArray())
            {
                yield return row;
            }

            yield break;
        }

        if (positions.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var name in new[] { "positions", "data" })
        {
            if (positions.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in nested.EnumerateArray())
                {
                    yield return row;
                }

                yield break;
            }
        }

        if (positions.TryGetProperty("symbol", out _))
        {
            yield return positions;
        }
    }

    private static bool IsAccepted(string? error, bool acceptExisting) =>
        error is null || (acceptExisting && ProtectiveOrderMath.IsExistingProtectiveOrder(error));

    private static bool IsImmediateTrigger(string? message) =>
        message?.Contains("-2021", StringComparison.Ordinal) == true
        || message?.Contains("immediately trigger", StringComparison.OrdinalIgnoreCase) == true;

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
            // Classic order book has no matching id after the Algo migration.
        }

        if (!string.IsNullOrWhiteSpace(clientOrderId))
        {
            try
            {
                await _signed.CancelFuturesAlgoOrderAsync(key, secret, symbol, clientOrderId, cancellationToken);
            }
            catch (DomainException)
            {
                // Already filled, cancelled, or unknown — Isolated protection may have already triggered.
            }
        }
    }

    /// <summary>
    /// Cancels every regular open order and every open algo (conditional) order on the coin.
    /// Throws when either list could not be cleared, so callers do not assume the book is empty.
    /// </summary>
    public async Task CancelAllOrdersAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var (key, secret) = await RequireKeys(cancellationToken);
        var name = symbol.ToUpperInvariant();
        var failures = new List<string>();
        try
        {
            await _signed.CancelAllFuturesOpenOrdersAsync(key, secret, name, cancellationToken);
        }
        catch (DomainException ex)
        {
            failures.Add(ex.Message);
        }

        try
        {
            var open = await _signed.GetFuturesOpenAlgoOrdersAsync(key, secret, cancellationToken);
            foreach (var clientAlgoId in OpenAlgoClientIds(open, name))
            {
                try
                {
                    await _signed.CancelFuturesAlgoOrderAsync(key, secret, name, clientAlgoId, cancellationToken);
                }
                catch (DomainException ex) when (!IsConfirmedAbsent(ex))
                {
                    failures.Add(ex.Message);
                }
                catch (DomainException)
                {
                    // Triggered or cancelled in the meantime.
                }
            }
        }
        catch (DomainException ex)
        {
            failures.Add(ex.Message);
        }

        if (failures.Count > 0)
        {
            throw new DomainException(ErrorCodes.ExchangeUnavailable, $"Not every {name} order was cancelled: {string.Join("; ", failures)}");
        }
    }

    internal static IEnumerable<string> OpenAlgoClientIds(JsonElement payload, string symbol)
    {
        var rows = payload;
        if (rows.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "orders", "data", "rows" })
            {
                if (rows.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.Array)
                {
                    rows = nested;
                    break;
                }
            }
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.TryGetProperty("symbol", out var symbolEl)
                && string.Equals(symbolEl.GetString(), symbol, StringComparison.OrdinalIgnoreCase)
                && row.TryGetProperty("clientAlgoId", out var idEl)
                && idEl.GetString() is { Length: > 0 } id)
            {
                yield return id;
            }
        }
    }

    public async Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        var (key, secret) = await RequireKeys(cancellationToken);
        var payload = await _signed.GetFuturesPositionsAsync(key, secret, cancellationToken);
        return ReadExchangePositions(payload);
    }

    internal static IReadOnlyList<ExchangePosition> ReadExchangePositions(JsonElement payload)
    {
        var list = new List<ExchangePosition>();
        foreach (var row in EnumeratePositionRows(payload))
        {
            var amount = row.TryGetProperty("positionAmt", out var amountEl) ? Dec(amountEl) : 0m;
            var symbol = row.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? "" : "";
            if (amount == 0m || symbol.Length == 0)
            {
                continue;
            }

            list.Add(new ExchangePosition(
                symbol.ToUpperInvariant(),
                amount > 0m ? Domain.Positions.PositionSide.Long : Domain.Positions.PositionSide.Short,
                Math.Abs(amount),
                row.TryGetProperty("entryPrice", out var entryEl) ? Dec(entryEl) : 0m,
                row.TryGetProperty("markPrice", out var markEl) ? Dec(markEl) : 0m));
        }

        return list;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (bool Hedge, DateTimeOffset At)> PositionModeCache = new();

    public async Task<bool?> IsHedgeModeAsync(CancellationToken cancellationToken = default)
    {
        if (PositionModeCache.TryGetValue(_accountId, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(5))
        {
            return cached.Hedge;
        }

        var (key, secret) = await RequireKeys(cancellationToken);
        try
        {
            var payload = await _signed.GetFuturesPositionModeAsync(key, secret, cancellationToken);
            var hedge = ReadDualSide(payload);
            if (hedge is not null)
            {
                PositionModeCache[_accountId] = (hedge.Value, DateTimeOffset.UtcNow);
            }

            return hedge;
        }
        catch (DomainException)
        {
            return null;
        }
    }

    internal static bool? ReadDualSide(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("dualSidePosition", out var dual))
        {
            return null;
        }

        return dual.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(dual.GetString(), out var parsed) => parsed,
            _ => null
        };
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
        var quantity = payload.TryGetProperty("origQty", out var origEl) ? Dec(origEl) : request.Quantity;
        if (quantity <= 0m)
        {
            quantity = executed > 0m ? executed : request.Quantity;
        }

        var avg = ReadFillPrice(payload, executed) ?? (request.Price is > 0m ? request.Price : null);
        long? transact = payload.TryGetProperty("transactTime", out var timeEl) ? timeEl.GetInt64() : null;
        var commission = CommissionReader.FromOrderPayload(payload);
        return new ExchangeOrder(
            payload.TryGetProperty("clientOrderId", out var cid) ? cid.GetString() ?? request.ClientOrderId : request.ClientOrderId,
            payload.TryGetProperty("orderId", out var oid) ? oid.ToString() : null,
            request.Symbol.ToUpperInvariant(),
            request.Side,
            request.Type,
            status,
            quantity,
            executed,
            avg,
            avg,
            transact is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(transact.Value),
            commission.Known ? commission.Amount : 0m,
            commission.Known,
            payload.TryGetProperty("cumQuote", out var quoteEl) ? Dec(quoteEl) : null,
            commission.Known ? commission.Asset : null);
    }

    private static ExchangeOrder MapAlgoOrder(JsonElement payload)
    {
        var statusText = payload.TryGetProperty("algoStatus", out var statusEl) ? statusEl.GetString() : "WORKING";
        var executed = payload.TryGetProperty("executedQty", out var qtyEl) ? Dec(qtyEl) : 0m;
        var quantity = payload.TryGetProperty("quantity", out var requestedEl) ? Dec(requestedEl) : executed;
        var avg = ReadFillPrice(payload, executed);
        return new ExchangeOrder(
            payload.TryGetProperty("clientAlgoId", out var cid) ? cid.GetString() ?? "" : "",
            payload.TryGetProperty("algoId", out var oid) ? oid.ToString() : null,
            payload.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? "" : "",
            string.Equals(payload.TryGetProperty("side", out var sideEl) ? sideEl.GetString() : "", "SELL", StringComparison.OrdinalIgnoreCase)
                ? OrderSide.Sell
                : OrderSide.Buy,
            OrderType.StopMarket,
            OrderLedger.ParseStatus(statusText),
            quantity > 0m ? quantity : executed,
            executed,
            avg,
            avg,
            null,
            0m,
            false,
            null,
            null);
    }

    private static decimal? ReadFillPrice(JsonElement payload, decimal executed)
    {
        if (payload.TryGetProperty("avgPrice", out var avgEl))
        {
            var avgPrice = Dec(avgEl);
            if (avgPrice > 0m)
            {
                return avgPrice;
            }
        }

        var quote = payload.TryGetProperty("cumQuote", out var quoteEl)
            ? Dec(quoteEl)
            : payload.TryGetProperty("cummulativeQuoteQty", out var spotQuote)
                ? Dec(spotQuote)
                : 0m;
        if (executed > 0m && quote > 0m)
        {
            return quote / executed;
        }

        if (payload.TryGetProperty("price", out var priceEl))
        {
            var price = Dec(priceEl);
            if (price > 0m)
            {
                return price;
            }
        }

        return null;
    }

    private static decimal Dec(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.Parse(element.GetString() ?? "0", CultureInfo.InvariantCulture);
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
    private readonly IPublicMarketDataClient _market;

    public BinanceLiveExchangeConnectorFactory(
        BinanceSignedRestClient signed,
        IExchangeCredentialStore credentials,
        IPublicMarketDataClient market)
    {
        _signed = signed;
        _credentials = credentials;
        _market = market;
    }

    public IExchangeConnector Create(Guid? exchangeAccountId)
    {
        if (exchangeAccountId is null || exchangeAccountId == Guid.Empty)
        {
            throw new DomainException(ErrorCodes.LiveTradingDisabled, "A live Binance account is required.");
        }

        return new BinanceLiveExchangeConnector(_signed, _credentials, _market, exchangeAccountId.Value);
    }
}
