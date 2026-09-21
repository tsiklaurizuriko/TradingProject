using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

public sealed class ExchangeAccountService : IExchangeAccountService
{
    private readonly IExchangeCredentialStore _store;
    private readonly ITradingStore _trading;
    private readonly BinanceSignedRestClient _signed;
    private readonly ILiveAccountCache _cache;
    private readonly ILogger<ExchangeAccountService> _logger;

    public ExchangeAccountService(
        IExchangeCredentialStore store,
        ITradingStore trading,
        BinanceSignedRestClient signed,
        ILiveAccountCache cache,
        ILogger<ExchangeAccountService> logger)
    {
        _store = store;
        _trading = trading;
        _signed = signed;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ExchangeConnectionDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUser(userId, cancellationToken);
        var account = await _store.GetLiveAccountAsync(user.Id, cancellationToken);
        var keys = account is null ? null : await _store.GetAsync(account.Id, cancellationToken);
        if (keys is null)
        {
            _cache.Set(new LiveAccountSnapshot { UpdatedAt = DateTimeOffset.UtcNow });
            return new ExchangeConnectionDto(false, false, false, null, null, "No live API key saved. Nothing trades until you start one coin.");
        }

        try
        {
            var snapshot = await _signed.GetAccountAsync(keys.Value.ApiKey, keys.Value.ApiSecret, cancellationToken);
            var canTrade = snapshot.ValueKind == JsonValueKind.Object
                && snapshot.TryGetProperty("canTrade", out var trade)
                && trade.GetBoolean();
            var spotHoldings = HoldingsFromAccount(snapshot);
            var spotUsdt = StableFree(spotHoldings);
            var fundingHoldings = await TryFundingAsync(keys.Value.ApiKey, keys.Value.ApiSecret, cancellationToken);
            var fundingUsdt = StableFree(fundingHoldings);
            var futures = await TryFuturesAsync(keys.Value.ApiKey, keys.Value.ApiSecret, cancellationToken);
            var openOrders = await TryOpenOrdersAsync(keys.Value.ApiKey, keys.Value.ApiSecret, cancellationToken);
            var previous = _cache.Current;
            var (positionsOk, fetchedPositions) = await TryFuturesPositionsAsync(keys.Value.ApiKey, keys.Value.ApiSecret, cancellationToken);
            var openPositions = positionsOk ? fetchedPositions : previous.OpenPositions;
            var merged = Merge(spotHoldings, fundingHoldings);
            var hint = Hint(keys.Value.ApiKey);
            var futuresEquity = futures.Equity > 0m ? futures.Equity : futures.Wallet;
            var tradable = futures.Available;
            var usdtTotal = spotUsdt + fundingUsdt + futuresEquity;
            var message = BuildMessage(canTrade, spotUsdt, fundingUsdt, futures.Wallet, futures.Available);

            _logger.LogInformation(
                "Binance wallets spotUsdt={Spot} fundingUsdt={Funding} futuresWallet={Futures} futuresFree={Free} orders={Orders} positions={Positions}",
                spotUsdt,
                fundingUsdt,
                futures.Wallet,
                futures.Available,
                openOrders.Count,
                openPositions.Count);

            _cache.Set(new LiveAccountSnapshot
            {
                HasKeys = true,
                CanTrade = canTrade,
                ApiKeyHint = hint,
                UsdtFree = tradable,
                SpotUsdt = spotUsdt,
                FundingUsdt = fundingUsdt,
                FuturesUsdt = futures.Wallet,
                FuturesEquity = futuresEquity,
                Message = message,
                Holdings = merged,
                OpenOrders = openOrders,
                OpenPositions = openPositions,
                FuturesBookFresh = positionsOk,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            try
            {
                await PersistFuturesLedgerAsync(
                    keys.Value.ApiKey,
                    keys.Value.ApiSecret,
                    openOrders,
                    openPositions,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live Isolated order ledger persist failed");
            }
            return new ExchangeConnectionDto(
                true,
                canTrade,
                canTrade,
                hint,
                tradable,
                message,
                spotUsdt,
                fundingUsdt,
                futures.Wallet,
                usdtTotal);
        }
        catch (Exception ex)
        {
            var previous = _cache.Current;
            _logger.LogWarning(ex, "Binance live account read failed");
            _cache.Set(new LiveAccountSnapshot
            {
                HasKeys = true,
                CanTrade = previous.CanTrade,
                ApiKeyHint = Hint(keys.Value.ApiKey),
                UsdtFree = previous.UsdtFree,
                SpotUsdt = previous.SpotUsdt,
                FundingUsdt = previous.FundingUsdt,
                FuturesUsdt = previous.FuturesUsdt,
                FuturesEquity = previous.FuturesEquity,
                Message = ex.Message,
                Holdings = previous.Holdings,
                OpenOrders = previous.OpenOrders,
                OpenPositions = previous.OpenPositions,
                FuturesBookFresh = false,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            return new ExchangeConnectionDto(
                true,
                false,
                false,
                Hint(keys.Value.ApiKey),
                previous.UsdtFree,
                ex.Message,
                previous.SpotUsdt,
                previous.FundingUsdt,
                previous.FuturesUsdt,
                previous.SpotUsdt + previous.FundingUsdt + (previous.FuturesEquity > 0m ? previous.FuturesEquity : previous.FuturesUsdt));
        }
    }

    public async Task<ExchangeConnectionDto> SaveLiveKeysAsync(Guid userId, string apiKey, string apiSecret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "API key and secret are required.");
        }

        var accountJson = await _signed.GetAccountAsync(apiKey.Trim(), apiSecret.Trim(), cancellationToken);
        var canTrade = accountJson.ValueKind == JsonValueKind.Object
            && accountJson.TryGetProperty("canTrade", out var trade)
            && trade.GetBoolean();
        if (!canTrade)
        {
            throw new DomainException(ErrorCodes.LiveTradingDisabled, "This Binance key cannot trade. Enable Spot trading and leave withdraw off.");
        }

        var user = await ResolveUser(userId, cancellationToken);
        var account = await _store.GetOrCreateLiveAccountAsync(user.Id, cancellationToken);
        await _store.StoreAsync(account.Id, apiKey, apiSecret, cancellationToken);
        return await GetStatusAsync(user.Id, cancellationToken);
    }

    private async Task<User> ResolveUser(Guid userId, CancellationToken cancellationToken)
    {
        return userId == Guid.Empty
            ? await _trading.GetFirstAdminAsync(cancellationToken)
            : await _trading.GetUserAsync(userId, cancellationToken) ?? await _trading.GetFirstAdminAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<LiveHolding>> TryFundingAsync(string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _signed.GetFundingAssetsAsync(apiKey, apiSecret, cancellationToken);
            return HoldingsFromArray(json, "free", "locked");
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Funding wallet skipped: {Message}", ex.Message);
            return [];
        }
    }

    private async Task<(decimal Available, decimal Wallet, decimal Equity)> TryFuturesAsync(string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _signed.GetFuturesAccountAsync(apiKey, apiSecret, cancellationToken);
            if (json.ValueKind != JsonValueKind.Object)
            {
                return (0m, 0m, 0m);
            }

            var available = 0m;
            var wallet = 0m;
            var equity = 0m;
            if (json.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in assets.EnumerateArray())
                {
                    if (!row.TryGetProperty("asset", out var asset) || !IsUsdt(asset.GetString()))
                    {
                        continue;
                    }

                    available = row.TryGetProperty("availableBalance", out var free) ? ParseDecimal(free) : 0m;
                    wallet = row.TryGetProperty("walletBalance", out var bal) ? ParseDecimal(bal) : available;
                    var margin = row.TryGetProperty("marginBalance", out var marginEl) ? ParseDecimal(marginEl) : 0m;
                    var pnl = row.TryGetProperty("unrealizedProfit", out var pnlEl) ? ParseDecimal(pnlEl) : 0m;
                    equity = margin > 0m ? margin : wallet + pnl;
                    break;
                }
            }

            if (wallet == 0m && equity == 0m)
            {
                available = json.TryGetProperty("availableBalance", out var avail) ? ParseDecimal(avail) : 0m;
                wallet = json.TryGetProperty("totalWalletBalance", out var total) ? ParseDecimal(total) : available;
                equity = json.TryGetProperty("totalMarginBalance", out var accountMargin) ? ParseDecimal(accountMargin) : wallet;
            }

            return (available, wallet, equity > 0m ? equity : wallet);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("USD-M futures wallet skipped: {Message}", ex.Message);
        }

        return (0m, 0m, 0m);
    }

    private async Task<IReadOnlyList<LiveOpenOrder>> TryOpenOrdersAsync(string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        var list = new List<LiveOpenOrder>();
        try
        {
            list.AddRange(ParseOrders(await _signed.GetSpotOpenOrdersAsync(apiKey, apiSecret, cancellationToken), "Spot"));
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Spot open orders skipped: {Message}", ex.Message);
        }

        try
        {
            list.AddRange(ParseOrders(await _signed.GetFuturesOpenOrdersAsync(apiKey, apiSecret, cancellationToken), "Futures"));
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Futures open orders skipped: {Message}", ex.Message);
        }

        try
        {
            list.AddRange(ParseAlgoOrders(await _signed.GetFuturesOpenAlgoOrdersAsync(apiKey, apiSecret, cancellationToken)));
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Futures open algo orders skipped: {Message}", ex.Message);
        }

        return list;
    }

    private async Task<(bool Ok, IReadOnlyList<LiveOpenPosition> Rows)> TryFuturesPositionsAsync(string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _signed.GetFuturesPositionsAsync(apiKey, apiSecret, cancellationToken);
            if (json.ValueKind != JsonValueKind.Array)
            {
                return (false, []);
            }

            var list = new List<LiveOpenPosition>();
            foreach (var row in json.EnumerateArray())
            {
                var amt = ParseDecimal(row, "positionAmt");
                if (amt == 0m)
                {
                    continue;
                }

                var symbol = row.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? "" : "";
                list.Add(new LiveOpenPosition(
                    symbol,
                    amt > 0m ? "Long" : "Short",
                    Math.Abs(amt),
                    ParseDecimal(row, "entryPrice"),
                    ParseDecimal(row, "markPrice"),
                    ParseDecimal(row, "unRealizedProfit"),
                    "Futures"));
            }

            return (true, list);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Futures positions skipped: {Message}", ex.Message);
            return (false, []);
        }
    }

    private async Task PersistFuturesLedgerAsync(
        string apiKey,
        string apiSecret,
        IReadOnlyList<LiveOpenOrder> openOrders,
        IReadOnlyList<LiveOpenPosition> openPositions,
        CancellationToken cancellationToken)
    {
        var user = await _trading.GetFirstAdminAsync(cancellationToken);
        var bots = new Dictionary<string, Bot>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task<Bot?> BotFor(string symbol)
        {
            var key = symbol.Trim().ToUpperInvariant();
            if (key.Length == 0)
            {
                return null;
            }

            if (bots.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var bot = await _trading.FindBotBySymbolAsync(user.Id, key, TradingMode.Live, null, null, cancellationToken);
            if (bot is not null)
            {
                bots[key] = bot;
            }

            return bot;
        }

        foreach (var row in openOrders.Where(item =>
                     string.Equals(item.Venue, "Futures", StringComparison.OrdinalIgnoreCase)))
        {
            var bot = await BotFor(row.Symbol);
            if (bot is null)
            {
                continue;
            }

            var qty = row.Quantity > 0m
                ? row.Quantity
                : openPositions.FirstOrDefault(item =>
                    string.Equals(item.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase))?.Quantity ?? 0m;
            await UpsertLiveOrderAsync(
                bot,
                row.Symbol,
                OrderLedger.ParseSide(row.Side),
                OrderLedger.ParseType(row.Type),
                OrderLedger.ParseStatus(row.Status),
                row.Price is > 0m ? row.Price : null,
                qty,
                row.FilledQuantity,
                row.ClientOrderId,
                row.ExchangeOrderId,
                row.CreatedAt,
                seen,
                cancellationToken);
        }

        foreach (var position in openPositions.Where(item => item.Quantity > 0m))
        {
            var bot = await BotFor(position.Symbol);
            if (bot is null)
            {
                continue;
            }

            try
            {
                var json = await _signed.GetFuturesUserTradesAsync(
                    apiKey,
                    apiSecret,
                    position.Symbol,
                    null,
                    cancellationToken);
                foreach (var trade in ParseUserTrades(json))
                {
                    await UpsertLiveOrderAsync(
                        bot,
                        position.Symbol,
                        trade.Side,
                        OrderType.Market,
                        OrderStatus.Filled,
                        trade.Price,
                        trade.Quantity,
                        trade.Quantity,
                        null,
                        trade.OrderId,
                        trade.Time,
                        seen,
                        cancellationToken,
                        trade.Fee,
                        trade.TradeId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("userTrades skipped for {Symbol}: {Message}", position.Symbol, ex.Message);
            }
        }

        await _trading.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertLiveOrderAsync(
        Bot bot,
        string symbol,
        OrderSide side,
        OrderType type,
        OrderStatus status,
        decimal? price,
        decimal quantity,
        decimal filled,
        string? clientOrderId,
        string? exchangeOrderId,
        DateTimeOffset createdAt,
        HashSet<string> seen,
        CancellationToken cancellationToken,
        decimal? fee = null,
        string? tradeId = null)
    {
        var clientKey = OrderLedger.ClientKey(clientOrderId, exchangeOrderId);
        if (clientKey.Length == 0)
        {
            return;
        }

        if (!seen.Add("c:" + clientKey)
            || (!string.IsNullOrWhiteSpace(exchangeOrderId) && !seen.Add("x:" + exchangeOrderId))
            || await _trading.HasKnownOrderAsync(clientKey, exchangeOrderId, cancellationToken)
            || (!string.IsNullOrWhiteSpace(clientOrderId)
                && !string.Equals(clientOrderId, clientKey, StringComparison.Ordinal)
                && await _trading.HasKnownOrderAsync(clientOrderId, exchangeOrderId, cancellationToken)))
        {
            return;
        }

        var order = new Order
        {
            BotId = bot.Id,
            ExchangeAccountId = bot.ExchangeAccountId,
            StrategyId = bot.StrategyVersion.StrategyId,
            StrategyVersionId = bot.StrategyVersionId,
            Symbol = symbol.ToUpperInvariant(),
            Side = side,
            Type = type,
            Status = status,
            Price = price,
            AverageFillPrice = status == OrderStatus.Filled ? price : null,
            Quantity = quantity,
            FilledQuantity = filled,
            RemainingQuantity = Math.Max(0m, quantity - filled),
            ClientOrderId = clientKey,
            IdempotencyKey = clientKey,
            ExchangeOrderId = exchangeOrderId,
            Mode = TradingMode.Live,
            CorrelationId = "binance-ledger",
            SubmittedAt = createdAt,
            ExchangeTimestamp = createdAt,
            CreatedAt = createdAt
        };
        await _trading.AddOrderAsync(order, cancellationToken);
        if (status == OrderStatus.Filled && filled > 0m)
        {
            await _trading.AddExecutionAsync(new Execution
            {
                OrderId = order.Id,
                Order = order,
                ExchangeTradeId = tradeId,
                Price = price ?? 0m,
                Quantity = filled,
                Fee = fee ?? 0m,
                FeeAsset = "USDT",
                IsMaker = false,
                ExchangeTimestamp = createdAt,
                CorrelationId = "binance-ledger"
            }, cancellationToken);
        }
    }

    private static IEnumerable<(string OrderId, OrderSide Side, decimal Price, decimal Quantity, decimal Fee, DateTimeOffset Time, string TradeId)> ParseUserTrades(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var row in json.EnumerateArray())
        {
            var orderId = row.TryGetProperty("orderId", out var orderEl) ? orderEl.ToString() : "";
            var tradeId = row.TryGetProperty("id", out var idEl) ? idEl.ToString() : orderId;
            var created = DateTimeOffset.UtcNow;
            if (row.TryGetProperty("time", out var timeEl) && timeEl.TryGetInt64(out var ms))
            {
                created = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }

            yield return (
                orderId,
                OrderLedger.ParseSide(row.TryGetProperty("side", out var sideEl) ? sideEl.GetString() : null),
                ParseDecimal(row, "price"),
                ParseDecimal(row, "qty"),
                Math.Abs(ParseDecimal(row, "commission")),
                created,
                tradeId);
        }
    }

    private static IReadOnlyList<LiveOpenOrder> ParseAlgoOrders(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<LiveOpenOrder>();
        foreach (var row in json.EnumerateArray())
        {
            var symbol = row.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? "" : "";
            var side = row.TryGetProperty("side", out var sideEl) ? sideEl.GetString() ?? "" : "";
            var type = row.TryGetProperty("orderType", out var typeEl)
                ? typeEl.GetString() ?? ""
                : row.TryGetProperty("type", out var fallbackType)
                    ? fallbackType.GetString() ?? ""
                    : "";
            var status = row.TryGetProperty("algoStatus", out var statusEl) ? statusEl.GetString() ?? "WORKING" : "WORKING";
            var orderId = row.TryGetProperty("algoId", out var idEl) ? idEl.ToString() : null;
            var clientId = row.TryGetProperty("clientAlgoId", out var cidEl) ? cidEl.GetString() : null;
            var created = DateTimeOffset.UtcNow;
            if (row.TryGetProperty("createTime", out var timeEl) && timeEl.TryGetInt64(out var ms))
            {
                created = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }

            var trigger = ParseDecimal(row, "triggerPrice");
            list.Add(new LiveOpenOrder(
                symbol,
                string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase) ? "Sell" : "Buy",
                string.IsNullOrWhiteSpace(type) ? "STOP_MARKET" : type,
                status,
                ParseDecimal(row, "quantity"),
                0m,
                trigger,
                orderId,
                clientId,
                created,
                "Futures"));
        }

        return list;
    }

    private static IReadOnlyList<LiveOpenOrder> ParseOrders(JsonElement json, string venue)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<LiveOpenOrder>();
        foreach (var row in json.EnumerateArray())
        {
            var symbol = row.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? "" : "";
            var side = row.TryGetProperty("side", out var sideEl) ? sideEl.GetString() ?? "" : "";
            var type = row.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
            var status = row.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "" : "";
            var orderId = row.TryGetProperty("orderId", out var idEl) ? idEl.ToString() : null;
            var clientId = row.TryGetProperty("clientOrderId", out var cidEl) ? cidEl.GetString() : null;
            var created = DateTimeOffset.UtcNow;
            if (row.TryGetProperty("time", out var timeEl) && timeEl.TryGetInt64(out var ms))
            {
                created = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }

            list.Add(new LiveOpenOrder(
                symbol,
                string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase) ? "Sell" : "Buy",
                type,
                status,
                ParseDecimal(row, "origQty"),
                ParseDecimal(row, "executedQty"),
                ParseDecimal(row, "price"),
                orderId,
                clientId,
                created,
                venue));
        }

        return list;
    }

    private static string BuildMessage(bool canTrade, decimal spotUsdt, decimal fundingUsdt, decimal futuresWallet, decimal futuresAvailable)
    {
        if (!canTrade)
        {
            return "Key works but Binance canTrade is false.";
        }

        if (futuresWallet > 0m || futuresAvailable > 0m)
        {
            return $"USD-M Futures wallet {futuresWallet:0.##} · free {futuresAvailable:0.##} USDT. Bots trade perpetual USDT contracts.";
        }

        if (spotUsdt <= 0m && fundingUsdt > 0m)
        {
            return $"Spot USDT is 0. Funding {fundingUsdt:0.##}. Transfer to USD-M Futures in Binance to trade.";
        }

        if (spotUsdt <= 0m && fundingUsdt <= 0m)
        {
            return "Binance returned 0 USDT in USD-M Futures. Move funds into the Futures wallet to trade.";
        }

        return "Live Binance wallets loaded. Start one USD-M coin from Bots — nothing starts by itself.";
    }

    private static IReadOnlyList<LiveHolding> HoldingsFromAccount(JsonElement account)
    {
        if (account.ValueKind != JsonValueKind.Object || !account.TryGetProperty("balances", out var rows))
        {
            return [];
        }

        return HoldingsFromArray(rows, "free", "locked");
    }

    private static IReadOnlyList<LiveHolding> HoldingsFromArray(JsonElement rows, string freeName, string lockedName)
    {
        if (rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<LiveHolding>();
        foreach (var row in rows.EnumerateArray())
        {
            var asset = row.TryGetProperty("asset", out var assetEl) ? assetEl.GetString() ?? string.Empty : string.Empty;
            if (asset.Length == 0)
            {
                continue;
            }

            var qty = ParseDecimal(row, freeName) + ParseDecimal(row, lockedName);
            if (qty > 0m)
            {
                list.Add(new LiveHolding(asset, qty));
            }
        }

        return list;
    }

    private static IReadOnlyList<LiveHolding> Merge(params IReadOnlyList<LiveHolding>[] groups)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var holding in group)
            {
                map[holding.Asset] = map.GetValueOrDefault(holding.Asset) + holding.Quantity;
            }
        }

        return map.Select(kv => new LiveHolding(kv.Key, kv.Value)).ToList();
    }

    private static decimal StableFree(IReadOnlyList<LiveHolding> holdings) => UsdtQty(holdings);

    private static decimal UsdtQty(IReadOnlyList<LiveHolding> holdings) =>
        holdings.Where(h => IsUsdt(h.Asset)).Sum(h => h.Quantity);

    private static bool IsUsdt(string? asset) =>
        string.Equals(asset, "USDT", StringComparison.OrdinalIgnoreCase);

    private static decimal ParseDecimal(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) ? ParseDecimal(value) : 0m;

    private static decimal ParseDecimal(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    private static string Hint(string apiKey)
    {
        var trimmed = apiKey.Trim();
        if (trimmed.Length <= 8)
        {
            return "••••";
        }

        return $"{trimmed[..4]}…{trimmed[^4..]}";
    }
}
