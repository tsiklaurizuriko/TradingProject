using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

public sealed class ExchangeAccountService : IExchangeAccountService
{
    private static readonly SemaphoreSlim HistoryGate = new(1, 1);
    private static readonly TimeSpan HistoryEvery = TimeSpan.FromMinutes(4);
    private static DateTimeOffset LastHistoryUtc = DateTimeOffset.MinValue;

    private readonly IExchangeCredentialStore _store;
    private readonly ITradingStore _trading;
    private readonly BinanceSignedRestClient _signed;
    private readonly ILiveAccountCache _cache;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ExchangeAccountService> _logger;

    public ExchangeAccountService(
        IExchangeCredentialStore store,
        ITradingStore trading,
        BinanceSignedRestClient signed,
        ILiveAccountCache cache,
        IServiceScopeFactory scopes,
        ILogger<ExchangeAccountService> logger)
    {
        _store = store;
        _trading = trading;
        _signed = signed;
        _cache = cache;
        _scopes = scopes;
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
            var previous = _cache.Current;
            var futures = await TryFuturesAsync(keys.Value.ApiKey, keys.Value.ApiSecret, cancellationToken);
            var canTrade = futures.CanTrade;
            var openOrders = await TryOpenOrdersAsync(keys.Value.ApiKey, keys.Value.ApiSecret, cancellationToken);
            var (positionsOk, fetchedPositions, positionError) = await TryFuturesPositionsAsync(keys.Value.ApiKey, keys.Value.ApiSecret, cancellationToken);
            var openPositions = positionsOk ? fetchedPositions : previous.OpenPositions;
            var hint = Hint(keys.Value.ApiKey);
            var futuresEquity = futures.Equity > 0m ? futures.Equity : futures.Wallet;
            var tradable = futures.Available;
            var spotUsdt = previous.SpotUsdt;
            var fundingUsdt = previous.FundingUsdt;
            var usdtTotal = spotUsdt + fundingUsdt + futuresEquity;
            var message = positionsOk
                ? BuildMessage(canTrade, spotUsdt, fundingUsdt, futures.Wallet, futures.Available)
                : $"Binance position book unavailable. {positionError}";

            _logger.LogInformation(
                "Binance USD-M Isolated snapshot futuresWallet={Futures} futuresFree={Free} orders={Orders} positions={Positions}",
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
                Holdings = previous.Holdings,
                OpenOrders = openOrders,
                OpenPositions = openPositions,
                FuturesBookFresh = positionsOk,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            try
            {
                await PersistOpenLedgerAsync(openOrders, openPositions, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live Isolated open-order persist failed");
            }

            var forceHistory = positionsOk && await HasClosedSnapshotAsync(openPositions, cancellationToken);
            QueueHistoryPersist(keys.Value.ApiKey, keys.Value.ApiSecret, openPositions, forceHistory);
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

    private async Task<(decimal Available, decimal Wallet, decimal Equity, bool CanTrade)> TryFuturesAsync(string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _signed.GetFuturesAccountAsync(apiKey, apiSecret, cancellationToken);
            if (json.ValueKind != JsonValueKind.Object)
            {
                return (0m, 0m, 0m, false);
            }

            var canTrade = !json.TryGetProperty("canTrade", out var trade) || trade.GetBoolean();
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
                    wallet = row.TryGetProperty("walletBalance", out var bal)
                        ? ParseDecimal(bal)
                        : row.TryGetProperty("balance", out var balance)
                            ? ParseDecimal(balance)
                            : available;
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

            return (available, wallet, equity > 0m ? equity : wallet, canTrade);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("USD-M futures wallet skipped: {Message}", ex.Message);
        }

        return (0m, 0m, 0m, false);
    }

    private async Task<IReadOnlyList<LiveOpenOrder>> TryOpenOrdersAsync(string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        var list = new List<LiveOpenOrder>();
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

    private async Task<(bool Ok, IReadOnlyList<LiveOpenPosition> Rows, string? Error)> TryFuturesPositionsAsync(
        string apiKey,
        string apiSecret,
        CancellationToken cancellationToken)
    {
        string? error = null;
        foreach (var version in new[] { "v3", "v2" })
        {
            try
            {
                var json = version == "v3"
                    ? await _signed.GetFuturesPositionsAsync(apiKey, apiSecret, cancellationToken)
                    : await _signed.GetFuturesPositionsV2Async(apiKey, apiSecret, cancellationToken);
                if (TryReadOpenPositions(json, out var rows))
                {
                    return (true, rows, null);
                }

                error = "Binance position book was not a list.";
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.LogInformation("Futures positions {Version} skipped: {Message}", version, ex.Message);
            }
        }

        var symbols = (await _trading.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken))
            .Where(row => row.Quantity > 0m && row.ClosedAt is null)
            .Select(row => row.Symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (symbols.Count == 0)
        {
            return (false, [], error ?? "Binance position book was not readable.");
        }

        var confirmed = new List<LiveOpenPosition>();
        foreach (var symbol in symbols)
        {
            try
            {
                var json = await _signed.GetFuturesPositionsAsync(apiKey, apiSecret, cancellationToken, symbol);
                if (!TryReadOpenPositions(json, out var rows))
                {
                    return (false, [], error ?? $"Binance position book for {symbol} was not a list.");
                }

                confirmed.AddRange(rows);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Futures position {Symbol} skipped: {Message}", symbol, ex.Message);
                return (false, [], ex.Message);
            }
        }

        return (true, confirmed, null);
    }

    private static bool TryReadOpenPositions(JsonElement json, out IReadOnlyList<LiveOpenPosition> rows)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            rows = ReadOpenPositions(json);
            return true;
        }

        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "positions", "data" })
            {
                if (json.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.Array)
                {
                    rows = ReadOpenPositions(nested);
                    return true;
                }
            }

            if (json.TryGetProperty("symbol", out _) && json.TryGetProperty("positionAmt", out _))
            {
                rows = ReadOpenPositions(json);
                return true;
            }
        }

        rows = [];
        return false;
    }

    private static IReadOnlyList<LiveOpenPosition> ReadOpenPositions(JsonElement json)
    {
        var list = new List<LiveOpenPosition>();
        if (json.ValueKind == JsonValueKind.Object)
        {
            AddOpenPosition(list, json);
            return list;
        }

        if (json.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var row in json.EnumerateArray())
        {
            AddOpenPosition(list, row);
        }

        return list;
    }

    private static void AddOpenPosition(List<LiveOpenPosition> list, JsonElement row)
    {
        var amount = ParseDecimal(row, "positionAmt");
        if (amount == 0m)
        {
            return;
        }

        var symbol = row.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        list.Add(new LiveOpenPosition(
            symbol,
            amount > 0m ? "Long" : "Short",
            Math.Abs(amount),
            ParseDecimal(row, "entryPrice"),
            ParseDecimal(row, "markPrice"),
            ParseDecimal(row, "unRealizedProfit"),
            "Futures"));
    }

    private async Task<bool> HasClosedSnapshotAsync(
        IReadOnlyList<LiveOpenPosition> openPositions,
        CancellationToken cancellationToken)
    {
        var live = openPositions
            .Where(row => row.Quantity > 0m)
            .Select(row => row.Symbol.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var book = await _trading.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken);
        return book.Any(row =>
            row.Quantity > 0m
            && row.ClosedAt is null
            && !live.Contains(row.Symbol.Trim().ToUpperInvariant()));
    }

    private void QueueHistoryPersist(
        string apiKey,
        string apiSecret,
        IReadOnlyList<LiveOpenPosition> openPositions,
        bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - LastHistoryUtc < HistoryEvery)
        {
            return;
        }

        if (!HistoryGate.Wait(0))
        {
            return;
        }

        var snapshot = openPositions.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var inner = (ExchangeAccountService)scope.ServiceProvider.GetRequiredService<IExchangeAccountService>();
                await inner.PersistHistoryLedgerAsync(apiKey, apiSecret, snapshot, CancellationToken.None);
                LastHistoryUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live Isolated history persist skipped");
            }
            finally
            {
                HistoryGate.Release();
            }
        });
    }

    private async Task<Dictionary<string, Bot>> LoadLiveBotsBySymbolAsync(CancellationToken cancellationToken)
    {
        var user = await _trading.GetFirstAdminAsync(cancellationToken);
        var bots = (await _trading.ListWorkspaceBotsAsync(user.Id, TradingMode.Live, cancellationToken))
            .Where(bot => bot.StrategyVersion is not null)
            .ToList();
        var book = await _trading.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken);
        return bots
            .GroupBy(bot => bot.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => LiveOwnerFor(group.Key, group.ToList(), book),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Keep in sync with IsolatedOccupancy.PickLiveOwner: open snapshot owns the coin;
    /// otherwise the earliest-started running bot may occupy it.
    /// </summary>
    private static Bot LiveOwnerFor(string symbol, IReadOnlyList<Bot> bots, IReadOnlyList<Position> book)
    {
        var holders = book
            .Where(row =>
                row.Quantity > 0m
                && string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.OpenedAt)
            .ThenBy(row => row.BotId)
            .ToList();
        if (holders.Count > 0)
        {
            var ownerId = holders[0].BotId;
            return bots.FirstOrDefault(bot => bot.Id == ownerId) ?? bots[0];
        }

        return bots
            .OrderBy(bot => bot.Status == BotStatus.Running ? 0 : 1)
            .ThenBy(bot => bot.StartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(bot => bot.Id)
            .First();
    }

    private async Task PersistOpenLedgerAsync(
        IReadOnlyList<LiveOpenOrder> openOrders,
        IReadOnlyList<LiveOpenPosition> openPositions,
        CancellationToken cancellationToken)
    {
        var bots = await LoadLiveBotsBySymbolAsync(cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in openOrders.Where(item =>
                     string.Equals(item.Venue, "Futures", StringComparison.OrdinalIgnoreCase)))
        {
            if (!bots.TryGetValue(row.Symbol, out var bot) &&
                !bots.TryGetValue(row.Symbol.Trim().ToUpperInvariant(), out bot))
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

        await _trading.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistHistoryLedgerAsync(
        string apiKey,
        string apiSecret,
        IReadOnlyList<LiveOpenPosition> openPositions,
        CancellationToken cancellationToken)
    {
        var bots = await LoadLiveBotsBySymbolAsync(cancellationToken);
        Bot? BotFor(string symbol)
        {
            var key = symbol.Trim().ToUpperInvariant();
            return key.Length == 0 ? null : bots.GetValueOrDefault(key);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fillsBySymbol = new Dictionary<string, List<UserTradeFill>>(StringComparer.OrdinalIgnoreCase);
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var openCoins = new HashSet<string>(
            openPositions.Where(item => item.Quantity > 0m).Select(item => item.Symbol),
            StringComparer.OrdinalIgnoreCase);
        foreach (var coin in openCoins)
        {
            symbols.Add(coin);
        }

        foreach (var row in await _trading.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken))
        {
            if (row.Quantity > 0m && row.ClosedAt is null && !string.IsNullOrWhiteSpace(row.Symbol))
            {
                symbols.Add(row.Symbol);
            }
        }

        IReadOnlyList<IncomeRow> income = [];
        try
        {
            var incomeJson = await _signed.GetFuturesIncomeAsync(
                apiKey,
                apiSecret,
                "REALIZED_PNL",
                cancellationToken);
            income = ParseIncome(incomeJson);
            foreach (var row in income)
            {
                symbols.Add(row.Symbol);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Futures realized PnL income skipped: {Message}", ex.Message);
        }

        foreach (var symbol in symbols)
        {
            var bot = BotFor(symbol);
            if (bot is null)
            {
                continue;
            }

            if (openCoins.Contains(symbol))
            {
                await PersistOrderHistoryAsync(bot, symbol, apiKey, apiSecret, seen, cancellationToken);
            }

            try
            {
                var json = await _signed.GetFuturesUserTradesAsync(
                    apiKey,
                    apiSecret,
                    symbol,
                    null,
                    cancellationToken);
                var fills = ParseUserTrades(json).ToList();
                fillsBySymbol[symbol] = fills;
                foreach (var trade in fills)
                {
                    await UpsertLiveOrderAsync(
                        bot,
                        symbol,
                        trade.Side,
                        OrderType.Market,
                        OrderStatus.Filled,
                        trade.Price,
                        trade.Quantity,
                        trade.Quantity,
                        BinanceClosedFill.TradeKey(trade.TradeId),
                        trade.OrderId,
                        trade.Time,
                        seen,
                        cancellationToken,
                        trade.Fee,
                        trade.TradeId,
                        trade.RealizedPnl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("userTrades skipped for {Symbol}: {Message}", symbol, ex.Message);
            }
        }

        foreach (var (symbol, fills) in fillsBySymbol)
        {
            var bot = BotFor(symbol);
            if (bot is null)
            {
                continue;
            }

            foreach (var trip in BinanceClosedFill.RoundTrips(
                         symbol,
                         fills.Select(item => new BinanceClosedFill.Fill(
                             item.Side,
                             item.Price,
                             item.Quantity,
                             item.Fee,
                             item.Time,
                             item.TradeId,
                             item.RealizedPnl)).ToList()))
            {
                await UpsertClosedTradeAsync(bot, trip, cancellationToken);
            }
        }

        foreach (var row in income)
        {
            if (fillsBySymbol.TryGetValue(row.Symbol, out var known) && known.Count > 0)
            {
                continue;
            }

            var bot = BotFor(row.Symbol);
            if (bot is null)
            {
                continue;
            }

            var fill = MatchFill(fillsBySymbol, row);
            await UpsertClosedTradeAsync(
                bot,
                new BinanceClosedFill.ClosedIsolated(
                    row.Symbol,
                    fill?.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy,
                    fill?.Quantity ?? 0m,
                    fill is { Quantity: > 0m, Price: > 0m }
                        ? BinanceClosedFill.EntryPrice(fill.Side, fill.Price, fill.Quantity, fill.RealizedPnl)
                        : 0m,
                    fill?.Price ?? 0m,
                    fill is not null && BinanceClosedFill.IsClosing(fill.RealizedPnl) ? fill.RealizedPnl : row.Pnl,
                    fill?.Fee ?? 0m,
                    fill?.Time ?? row.Time,
                    fill?.Time ?? row.Time,
                    string.IsNullOrWhiteSpace(row.TradeId) ? row.TranId.ToString() : row.TradeId),
                cancellationToken);
        }

        await _trading.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistOrderHistoryAsync(
        Bot bot,
        string symbol,
        string apiKey,
        string apiSecret,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await _signed.GetFuturesAllOrdersAsync(apiKey, apiSecret, symbol, cancellationToken);
            foreach (var row in ParseOrders(json, "Futures"))
            {
                await UpsertLiveOrderAsync(
                    bot,
                    symbol,
                    OrderLedger.ParseSide(row.Side),
                    OrderLedger.ParseType(row.Type),
                    OrderLedger.ParseStatus(row.Status),
                    row.Price is > 0m ? row.Price : null,
                    row.Quantity,
                    row.FilledQuantity,
                    row.ClientOrderId,
                    row.ExchangeOrderId,
                    row.CreatedAt,
                    seen,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation("allOrders skipped for {Symbol}: {Message}", symbol, ex.Message);
        }

        try
        {
            var json = await _signed.GetFuturesAllAlgoOrdersAsync(apiKey, apiSecret, symbol, cancellationToken);
            foreach (var row in ParseAlgoOrders(json))
            {
                var qty = row.Quantity > 0m ? row.Quantity : 0m;
                await UpsertLiveOrderAsync(
                    bot,
                    symbol,
                    OrderLedger.ParseSide(row.Side),
                    OrderLedger.ParseType(row.Type),
                    OrderLedger.ParseStatus(row.Status),
                    row.Price is > 0m ? row.Price : null,
                    qty,
                    OrderLedger.ParseStatus(row.Status) == OrderStatus.Filled ? qty : 0m,
                    row.ClientOrderId,
                    row.ExchangeOrderId,
                    row.CreatedAt,
                    seen,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation("allAlgoOrders skipped for {Symbol}: {Message}", symbol, ex.Message);
        }
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
        string? tradeId = null,
        decimal? realizedPnl = null)
    {
        var fillRow = !string.IsNullOrWhiteSpace(tradeId);
        var clientKey = OrderLedger.ClientKey(clientOrderId, fillRow ? null : exchangeOrderId);
        if (clientKey.Length == 0)
        {
            return;
        }

        if (!seen.Add("c:" + clientKey)
            || (!fillRow && !string.IsNullOrWhiteSpace(exchangeOrderId) && !seen.Add("x:" + exchangeOrderId)))
        {
            return;
        }

        var existing = await _trading.GetOrderByClientOrderIdAsync(clientKey, cancellationToken);
        if (existing is not null)
        {
            if (fillRow)
            {
                return;
            }

            existing.Status = status;
            existing.FilledQuantity = filled;
            existing.RemainingQuantity = Math.Max(0m, (existing.Quantity > 0m ? existing.Quantity : quantity) - filled);
            if (price is > 0m)
            {
                existing.Price = price;
                if (status == OrderStatus.Filled)
                {
                    existing.AverageFillPrice = price;
                }
            }

            if (!string.IsNullOrWhiteSpace(exchangeOrderId))
            {
                existing.ExchangeOrderId = exchangeOrderId;
            }

            if (status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Failed or OrderStatus.Expired)
            {
                existing.RejectReason = null;
            }

            return;
        }

        if (!fillRow
            && !string.IsNullOrWhiteSpace(exchangeOrderId)
            && await _trading.HasKnownOrderAsync(null, exchangeOrderId, cancellationToken))
        {
            return;
        }

        if (!fillRow
            && !string.IsNullOrWhiteSpace(clientOrderId)
            && !string.Equals(clientOrderId, clientKey, StringComparison.Ordinal)
            && await _trading.HasKnownOrderAsync(clientOrderId, exchangeOrderId, cancellationToken))
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
            CorrelationId = realizedPnl is not null
                ? "binance-fill:"
                    + realizedPnl.Value.ToString(CultureInfo.InvariantCulture)
                    + ":"
                    + (fee ?? 0m).ToString(CultureInfo.InvariantCulture)
                : "binance-ledger",
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

    private async Task UpsertClosedTradeAsync(
        Bot bot,
        BinanceClosedFill.ClosedIsolated trip,
        CancellationToken cancellationToken)
    {
        if (trip.RealizedPnl == 0m && trip.Quantity <= 0m)
        {
            return;
        }

        // A bot cannot own a fill that closed before it existed. Flow Zone on every
        // coin was adopting the account's older round-trips and relabeling them.
        var born = bot.StartedAt ?? bot.CreatedAt;
        if (trip.ClosedAt < born)
        {
            return;
        }

        var correlationId = string.IsNullOrWhiteSpace(trip.CloseTradeId)
            ? BinanceClosedFill.TradeKey(trip.Symbol + trip.ClosedAt.ToUnixTimeMilliseconds())
            : BinanceClosedFill.TradeKey(trip.CloseTradeId);
        var around = (await _trading.FindClosedTradesAroundAsync(
                trip.Symbol,
                trip.OpenedAt,
                trip.ClosedAt,
                cancellationToken))
            .Where(item =>
                item.BotId == bot.Id
                && item.ClosedAt is { } closed
                && item.OpenedAt <= trip.ClosedAt
                && trip.OpenedAt <= closed
                && item.Quantity == trip.Quantity)
            .ToList();
        var existing = around.FirstOrDefault(item =>
                           string.Equals(item.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
                       ?? around.FirstOrDefault(item =>
                           item.CorrelationId.StartsWith(BinanceClosedFill.TradePrefix, StringComparison.OrdinalIgnoreCase)
                           || item.CorrelationId.StartsWith(BinanceClosedFill.IncomePrefix, StringComparison.OrdinalIgnoreCase))
                       ?? around.OrderByDescending(item => item.ClosedAt).FirstOrDefault();
        foreach (var extra in around.Where(item => existing is not null && item.Id != existing.Id))
        {
            _trading.RemoveTrade(extra);
        }

        var exitOrder = await _trading.GetOrderByClientOrderIdAsync(correlationId, cancellationToken);
        var open = await _trading.GetOpenTradeAsync(bot.Id, cancellationToken);
        if (open is not null && !string.Equals(open.Symbol, trip.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            open = null;
        }

        void Apply(Trade trade)
        {
            trade.BotId = bot.Id;
            if (bot.StrategyVersion is not null)
            {
                trade.StrategyId = bot.StrategyVersion.StrategyId;
                trade.StrategyVersionId = bot.StrategyVersionId;
            }

            trade.Side = trip.EntrySide;
            trade.Quantity = trip.Quantity > 0m ? trip.Quantity : trade.Quantity;
            trade.ExitPrice = trip.ExitPrice > 0m ? trip.ExitPrice : trade.ExitPrice;
            trade.EntryPrice = trip.EntryPrice > 0m ? trip.EntryPrice : trade.EntryPrice;
            trade.PnL = trip.RealizedPnl;
            trade.PnLPercent = BinanceClosedFill.PnLPercent(
                trade.EntryPrice,
                trade.Quantity,
                trip.RealizedPnl);
            trade.Fees = trip.Fees;
            trade.OpenedAt = trip.OpenedAt;
            trade.ClosedAt = trip.ClosedAt;
            trade.CorrelationId = correlationId;
            if (exitOrder is not null)
            {
                trade.ExitOrderId ??= exitOrder.Id;
            }
        }

        if (existing is not null)
        {
            Apply(existing);
            if (open is not null && open.Id != existing.Id)
            {
                _trading.RemoveTrade(open);
            }

            return;
        }

        if (open is not null)
        {
            Apply(open);
            return;
        }

        if (bot.StrategyVersion is null)
        {
            return;
        }

        await _trading.AddTradeAsync(new Trade
        {
            BotId = bot.Id,
            StrategyId = bot.StrategyVersion.StrategyId,
            StrategyVersionId = bot.StrategyVersionId,
            ExitOrderId = exitOrder?.Id,
            Symbol = trip.Symbol.ToUpperInvariant(),
            Side = trip.EntrySide,
            Quantity = trip.Quantity,
            EntryPrice = trip.EntryPrice,
            ExitPrice = trip.ExitPrice,
            PnL = trip.RealizedPnl,
            PnLPercent = BinanceClosedFill.PnLPercent(trip.EntryPrice, trip.Quantity, trip.RealizedPnl),
            Fees = trip.Fees,
            OpenedAt = trip.OpenedAt,
            ClosedAt = trip.ClosedAt,
            CorrelationId = correlationId
        }, cancellationToken);
    }

    private static UserTradeFill? MatchFill(
        IReadOnlyDictionary<string, List<UserTradeFill>> fillsBySymbol,
        IncomeRow income)
    {
        if (!fillsBySymbol.TryGetValue(income.Symbol, out var fills) || fills.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(income.TradeId))
        {
            var exact = fills.FirstOrDefault(item => item.TradeId == income.TradeId);
            if (exact is not null)
            {
                return exact;
            }
        }

        return fills
            .Where(item => BinanceClosedFill.IsClosing(item.RealizedPnl) && Math.Abs((item.Time - income.Time).TotalSeconds) <= 5)
            .OrderBy(item => Math.Abs(item.RealizedPnl - income.Pnl))
            .FirstOrDefault();
    }

    private sealed record IncomeRow(string Symbol, decimal Pnl, DateTimeOffset Time, long TranId, string TradeId);

    private sealed record UserTradeFill(
        string OrderId,
        OrderSide Side,
        decimal Price,
        decimal Quantity,
        decimal Fee,
        DateTimeOffset Time,
        string TradeId,
        decimal RealizedPnl);

    private static IReadOnlyList<IncomeRow> ParseIncome(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<IncomeRow>();
        foreach (var row in json.EnumerateArray())
        {
            var symbol = row.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            var created = DateTimeOffset.UtcNow;
            if (row.TryGetProperty("time", out var timeEl) && timeEl.TryGetInt64(out var ms))
            {
                created = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }

            var tranId = 0L;
            if (row.TryGetProperty("tranId", out var tranEl))
            {
                if (tranEl.ValueKind == JsonValueKind.Number)
                {
                    tranEl.TryGetInt64(out tranId);
                }
                else
                {
                    long.TryParse(tranEl.GetString(), out tranId);
                }
            }

            var tradeId = row.TryGetProperty("tradeId", out var tradeEl) ? tradeEl.ToString() : "";
            rows.Add(new IncomeRow(symbol.ToUpperInvariant(), ParseDecimal(row, "income"), created, tranId, tradeId));
        }

        return rows;
    }

    private static IEnumerable<UserTradeFill> ParseUserTrades(JsonElement json)
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

            yield return new UserTradeFill(
                orderId,
                OrderLedger.ParseSide(row.TryGetProperty("side", out var sideEl) ? sideEl.GetString() : null),
                ParseDecimal(row, "price"),
                ParseDecimal(row, "qty"),
                Math.Abs(ParseDecimal(row, "commission")),
                created,
                tradeId,
                ParseDecimal(row, "realizedPnl"));
        }
    }

    private static IReadOnlyList<LiveOpenOrder> ParseAlgoOrders(JsonElement json)
    {
        json = AsOrderArray(json);
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
        json = AsOrderArray(json);
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
                OrderDisplayPrice(row),
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

    private static JsonElement AsOrderArray(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            return json;
        }

        if (json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("orders", out var orders)
            && orders.ValueKind == JsonValueKind.Array)
        {
            return orders;
        }

        return default;
    }

    private static decimal OrderDisplayPrice(JsonElement row)
    {
        var avg = ParseDecimal(row, "avgPrice");
        if (avg > 0m)
        {
            return avg;
        }

        var price = ParseDecimal(row, "price");
        if (price > 0m)
        {
            return price;
        }

        var stop = ParseDecimal(row, "stopPrice");
        return stop > 0m ? stop : ParseDecimal(row, "triggerPrice");
    }

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
