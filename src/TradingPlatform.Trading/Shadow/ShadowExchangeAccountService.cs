using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Execution.Shadow;

namespace TradingPlatform.Trading.Shadow;

/// <summary>
/// Shadow counterpart of the Binance account sync. It fills the account cache from the simulated account and books
/// stops, takes and funding that the shadow exchange settled on its own, the way the Binance history sync books real ones.
/// </summary>
public sealed class ShadowExchangeAccountService : IExchangeAccountService
{
    public const string PlaceholderKey = "SHADOW-NO-KEY";

    private readonly ShadowExchange _exchange;
    private readonly IShadowPriceFeed _prices;
    private readonly ITradingStore _store;
    private readonly IExchangeCredentialStore _credentials;
    private readonly ILiveAccountCache _cache;
    private readonly IClock _clock;
    private readonly ILogger<ShadowExchangeAccountService> _logger;

    public ShadowExchangeAccountService(
        ShadowExchange exchange,
        IShadowPriceFeed prices,
        ITradingStore store,
        IExchangeCredentialStore credentials,
        ILiveAccountCache cache,
        IClock clock,
        ILogger<ShadowExchangeAccountService> logger)
    {
        _exchange = exchange;
        _prices = prices;
        _store = store;
        _credentials = credentials;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ExchangeConnectionDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userId, cancellationToken);
        await EnsureAccountAsync(user, cancellationToken);
        await BookPendingAsync(cancellationToken);

        IReadOnlyDictionary<string, decimal> marks;
        var fresh = true;
        try
        {
            marks = (await _prices.GetPremiumAsync(cancellationToken))
                .ToDictionary(pair => pair.Key, pair => pair.Value.MarkPrice, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Shadow mark prices unavailable. The account snapshot is marked stale.");
            marks = new Dictionary<string, decimal>();
            fresh = false;
        }

        var account = _exchange.Account(marks);
        var positions = _exchange.Positions(marks)
            .Select(row => new LiveOpenPosition(
                row.Symbol,
                row.Side == PositionSide.Short ? "Short" : "Long",
                row.Quantity,
                row.EntryPrice,
                row.MarkPrice,
                (row.Side == PositionSide.Short ? -1m : 1m) * row.Quantity * (row.MarkPrice - row.EntryPrice),
                "Futures"))
            .ToList();
        var orders = _exchange.WorkingAlgos()
            .Select(row => new LiveOpenOrder(
                row.Symbol,
                row.CloseSide == OrderSide.Sell ? "Sell" : "Buy",
                row.IsStop ? "STOP_MARKET" : "TAKE_PROFIT_MARKET",
                "NEW",
                0m,
                0m,
                row.Trigger,
                row.ExchangeOrderId,
                row.ClientOrderId,
                row.At,
                "Futures"))
            .ToList();
        const string message = "Shadow account: simulated fills on real Binance prices. No order reaches Binance.";
        _cache.Set(new LiveAccountSnapshot
        {
            HasKeys = true,
            CanTrade = true,
            ApiKeyHint = "shadow",
            UsdtFree = account.Available,
            FuturesUsdt = account.Wallet,
            FuturesEquity = account.Equity,
            Message = message,
            OpenOrders = orders,
            OpenPositions = positions,
            FuturesBookFresh = fresh,
            UpdatedAt = _clock.UtcNow
        });
        return new ExchangeConnectionDto(true, true, true, "shadow", account.Available, message, 0m, 0m, account.Wallet, account.Equity);
    }

    public Task<ExchangeConnectionDto> SaveLiveKeysAsync(Guid userId, string apiKey, string apiSecret, CancellationToken cancellationToken = default) =>
        throw new DomainException(ErrorCodes.ValidationFailed, "This process runs the Shadow venue. It uses no Binance API key, so none was saved.");

    /// <summary>Books every settled shadow fill once. A fill is acknowledged only after the store saved it.</summary>
    public async Task<int> BookPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = _exchange.PendingFills();
        if (pending.Count == 0)
        {
            return 0;
        }

        var book = (await _store.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken)).ToList();
        foreach (var fill in pending)
        {
            var position = book.FirstOrDefault(row =>
                row.Quantity > 0m
                && row.ClosedAt is null
                && string.Equals(row.Symbol, fill.Symbol, StringComparison.OrdinalIgnoreCase));
            if (fill.Kind == ShadowFillKind.Funding)
            {
                await BookFundingAsync(position, fill, cancellationToken);
            }
            else
            {
                await BookTriggerAsync(position, fill, cancellationToken);
            }
        }

        await _store.SaveChangesAsync(cancellationToken);
        _exchange.Acknowledge(pending.Select(fill => fill.Id));
        return pending.Count;
    }

    private async Task BookTriggerAsync(Position? position, ShadowFill fill, CancellationToken cancellationToken)
    {
        var order = string.IsNullOrWhiteSpace(fill.ClientOrderId)
            ? null
            : await _store.GetOrderByClientOrderIdAsync(fill.ClientOrderId, cancellationToken);
        if (order is { Status: OrderStatus.Filled })
        {
            return;
        }

        if (order is not null)
        {
            order.Status = OrderStatus.Filled;
            order.FilledQuantity = fill.Quantity;
            order.RemainingQuantity = 0m;
            order.AverageFillPrice = fill.Price;
            order.ExchangeTimestamp = fill.At;
            order.RejectReason = null;
            await _store.AddExecutionAsync(new Domain.Orders.Execution
            {
                OrderId = order.Id,
                Order = order,
                ExchangeTradeId = fill.Id,
                Price = fill.Price,
                Quantity = fill.Quantity,
                Fee = fill.Fee,
                FeeAsset = ShadowExchange.FeeAsset,
                FeeStatus = FeeKnowledge.Known,
                ExchangeTimestamp = fill.At,
                CorrelationId = "shadow-trigger"
            }, cancellationToken);
        }

        if (position is null)
        {
            _logger.LogWarning("Shadow {Reason} on {Symbol} had no open local position. The fill was recorded on the order only.", fill.Reason, fill.Symbol);
            return;
        }

        var booking = PositionFillBook.Exit(position.Side, position.Quantity, position.AverageEntryPrice, fill.Quantity, fill.Price, 0m);
        var commission = FeeBook.Known(fill.Fee, ShadowExchange.FeeAsset);
        position.RealizedPnL += booking.RealizedPnl;
        position.Fees += fill.Fee;
        position.CurrentPrice = fill.Price;
        position.Events.Add(new PositionEvent
        {
            EventType = booking.EventType,
            Quantity = fill.Quantity,
            Price = fill.Price,
            RealizedPnLDelta = booking.RealizedPnl,
            CorrelationId = "shadow-" + fill.Id
        });
        position.Quantity = booking.RemainingQuantity;
        if (booking.Closed)
        {
            position.UnrealizedPnL = 0m;
            position.ClosedAt = fill.At;
        }

        var trade = await _store.GetOpenTradeAsync(position.BotId, cancellationToken);
        if (trade is null || !string.Equals(trade.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        trade.PnL += booking.RealizedPnl;
        if (booking.Closed)
        {
            trade.ExitPrice = fill.Price;
            trade.ExitOrderId = order?.Id ?? trade.ExitOrderId;
            trade.ClosedAt = fill.At;
            trade.PnLPercent = position.AverageEntryPrice == 0m
                ? 0m
                : (position.Side == PositionSide.Short ? -1m : 1m) * (fill.Price - position.AverageEntryPrice) / position.AverageEntryPrice * 100m;
        }

        TradeFee.Apply(trade, commission, replace: false);
    }

    private async Task BookFundingAsync(Position? position, ShadowFill fill, CancellationToken cancellationToken)
    {
        if (position is null)
        {
            return;
        }

        var trade = await _store.GetOpenTradeAsync(position.BotId, cancellationToken);
        if (trade is null || !string.Equals(trade.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        trade.FundingPnL = (trade.FundingPnL ?? 0m) + fill.Funding;
        TradePnl.Refresh(trade);
    }

    private async Task EnsureAccountAsync(User user, CancellationToken cancellationToken)
    {
        var account = await _credentials.GetOrCreateLiveAccountAsync(user.Id, cancellationToken);
        if (await _credentials.GetAsync(account.Id, cancellationToken) is null)
        {
            await _credentials.StoreAsync(account.Id, PlaceholderKey, PlaceholderKey, cancellationToken);
        }
    }

    private async Task<User> ResolveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        userId == Guid.Empty
            ? await _store.GetFirstAdminAsync(cancellationToken)
            : await _store.GetUserAsync(userId, cancellationToken) ?? await _store.GetFirstAdminAsync(cancellationToken);
}
