using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Trading;

/// <summary>
/// LIVE Isolated truth is Binance. A DB snapshot left open after SL/TP or an exchange flatten is a ghost.
/// </summary>
public sealed class LiveIsolatedReconciler
{
    private readonly ITradingStore _store;
    private readonly ILiveAccountCache _live;
    private readonly IMarketDataCache _cache;
    private readonly IClock _clock;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly ILogger<LiveIsolatedReconciler> _logger;

    public LiveIsolatedReconciler(
        ITradingStore store,
        ILiveAccountCache live,
        IMarketDataCache cache,
        IClock clock,
        ICorrelationIdAccessor correlation,
        ILogger<LiveIsolatedReconciler> logger)
    {
        _store = store;
        _live = live;
        _cache = cache;
        _clock = clock;
        _correlation = correlation;
        _logger = logger;
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var live = _live.Current;
            if (!IsolatedOccupancy.HasFreshFuturesBook(live))
            {
                return;
            }

            var book = await _store.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken);
        var now = _clock.UtcNow;
        var closed = 0;
        foreach (var position in book)
        {
            if (!IsolatedOccupancy.IsLiveGhost(position, live.OpenPositions, now))
            {
                continue;
            }

            var bot = position.Bot ?? await _store.GetBotAsync(position.BotId, cancellationToken);
            if (bot is null)
            {
                continue;
            }

            if (bot.StrategyVersion is null)
            {
                bot = await _store.GetBotAsync(bot.Id, cancellationToken) ?? bot;
            }

            if (bot.StrategyVersion is null)
            {
                continue;
            }

            await CloseGhostAsync(bot, position, cancellationToken);
            closed++;
        }

        if (closed > 0)
        {
            await _store.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Reconciled {Count} Isolated snapshot(s) that Binance no longer holds.", closed);
        }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private async Task CloseGhostAsync(Bot bot, Position position, CancellationToken cancellationToken)
    {
        var clientOrderId = $"RC{position.Id:N}"[..18];
        if (await _store.HasClientOrderAsync(clientOrderId, cancellationToken))
        {
            position.Quantity = 0m;
            position.UnrealizedPnL = 0m;
            position.ClosedAt ??= _clock.UtcNow;
            return;
        }

        var exit = position.CurrentPrice > 0m ? position.CurrentPrice : position.AverageEntryPrice;
        if (_cache.TryGetTicker(position.Symbol, out var mark) && mark > 0m)
        {
            exit = mark;
        }

        var correlationId = _correlation.GetOrCreate();
        var closeSide = position.Side == PositionSide.Short ? OrderSide.Buy : OrderSide.Sell;
        var order = new Order
        {
            BotId = bot.Id,
            ExchangeAccountId = bot.ExchangeAccountId,
            StrategyId = bot.StrategyVersion.StrategyId,
            StrategyVersionId = bot.StrategyVersionId,
            Symbol = position.Symbol,
            Side = closeSide,
            Type = OrderType.Market,
            Status = OrderStatus.Filled,
            Price = exit,
            AverageFillPrice = exit,
            Quantity = position.Quantity,
            FilledQuantity = position.Quantity,
            RemainingQuantity = 0m,
            ClientOrderId = clientOrderId,
            IdempotencyKey = clientOrderId,
            Mode = TradingMode.Live,
            CorrelationId = correlationId,
            SubmittedAt = _clock.UtcNow,
            ExchangeTimestamp = _clock.UtcNow,
            RejectReason = "Closed on Binance. Isolated snapshot reconciled."
        };
        await _store.AddOrderAsync(order, cancellationToken);

        var quantity = position.Quantity;
        var direction = position.Side == PositionSide.Short ? -1m : 1m;
        var pnl = direction * (exit - position.AverageEntryPrice) * quantity;
        position.Quantity = 0m;
        position.CurrentPrice = exit;
        position.UnrealizedPnL = 0m;
        position.RealizedPnL += pnl;
        position.ClosedAt = _clock.UtcNow;
        position.Events.Add(new PositionEvent
        {
            EventType = "CLOSE",
            Quantity = quantity,
            Price = exit,
            RealizedPnLDelta = pnl,
            CorrelationId = correlationId
        });

        var pnlPercent = position.AverageEntryPrice == 0m
            ? 0m
            : direction * (exit - position.AverageEntryPrice) / position.AverageEntryPrice * 100m;
        var already = await _store.FindClosedTradeNearAsync(
            bot.Id,
            position.Symbol,
            _clock.UtcNow,
            TimeSpan.FromMinutes(15),
            cancellationToken);
        if (already is not null)
        {
            await CancelProtectiveRowAsync(LiveProtectivePrices.StopClientOrderId(bot.Id), cancellationToken);
            await CancelProtectiveRowAsync(LiveProtectivePrices.TakeClientOrderId(bot.Id), cancellationToken);
            bot.LastError = $"Isolated {position.Symbol} closed on Binance. Snapshot reconciled.";
            _logger.LogInformation(
                "LIVE Isolated {Symbol} is flat on Binance. Snapshot closed; trade already stored for bot {BotId}.",
                position.Symbol,
                bot.Id);
            return;
        }

        var openTrade = await _store.GetOpenTradeAsync(bot.Id, cancellationToken);
        if (openTrade is not null &&
            !string.Equals(openTrade.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            openTrade = null;
        }

        if (openTrade is not null)
        {
            openTrade.ExitOrderId = order.Id;
            openTrade.ExitPrice = exit;
            openTrade.PnL = pnl;
            openTrade.PnLPercent = pnlPercent;
            openTrade.ClosedAt = _clock.UtcNow;
        }
        else
        {
            await _store.AddTradeAsync(new Trade
            {
                BotId = bot.Id,
                StrategyId = bot.StrategyVersion.StrategyId,
                StrategyVersionId = bot.StrategyVersionId,
                ExitOrderId = order.Id,
                Symbol = position.Symbol,
                Side = closeSide,
                Quantity = quantity,
                EntryPrice = position.AverageEntryPrice,
                ExitPrice = exit,
                PnL = pnl,
                PnLPercent = pnlPercent,
                Fees = 0m,
                OpenedAt = position.OpenedAt,
                ClosedAt = _clock.UtcNow,
                CorrelationId = correlationId
            }, cancellationToken);
        }

        await CancelProtectiveRowAsync(LiveProtectivePrices.StopClientOrderId(bot.Id), cancellationToken);
        await CancelProtectiveRowAsync(LiveProtectivePrices.TakeClientOrderId(bot.Id), cancellationToken);
        bot.LastError = $"Isolated {position.Symbol} closed on Binance. Snapshot reconciled.";
        _logger.LogInformation(
            "LIVE Isolated {Symbol} is flat on Binance. Closed leftover snapshot for bot {BotId}.",
            position.Symbol,
            bot.Id);
    }

    private async Task CancelProtectiveRowAsync(string clientOrderId, CancellationToken cancellationToken)
    {
        var existing = await _store.GetOrderByClientOrderIdAsync(clientOrderId, cancellationToken);
        if (existing is null || existing.Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Failed)
        {
            return;
        }

        existing.Status = OrderStatus.Cancelled;
        existing.RemainingQuantity = 0m;
    }
}
