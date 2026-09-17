using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Identity;

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
            var openPositions = await TryFuturesPositionsAsync(keys.Value.ApiKey, keys.Value.ApiSecret, cancellationToken);
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
                UpdatedAt = DateTimeOffset.UtcNow
            });
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

        return list;
    }

    private async Task<IReadOnlyList<LiveOpenPosition>> TryFuturesPositionsAsync(string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _signed.GetFuturesPositionsAsync(apiKey, apiSecret, cancellationToken);
            if (json.ValueKind != JsonValueKind.Array)
            {
                return [];
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

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Futures positions skipped: {Message}", ex.Message);
            return [];
        }
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
