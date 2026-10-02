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
    private readonly ReconciliationState _state;

    public LiveIsolatedReconciler(
        ITradingStore store,
        ILiveAccountCache live,
        IMarketDataCache cache,
        IClock clock,
        ICorrelationIdAccessor correlation,
        ILogger<LiveIsolatedReconciler> logger,
        ReconciliationState? state = null)
    {
        _store = store;
        _live = live;
        _cache = cache;
        _clock = clock;
        _correlation = correlation;
        _logger = logger;
        _state = state ?? new ReconciliationState();
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var live = _live.Current;
            if (!IsolatedOccupancy.HasFreshFuturesBook(live) || live.UpdatedAt is null)
            {
                _state.Fail("Exchange account snapshot is missing, stale, or incomplete. New live entries are blocked.", _clock.UtcNow);
                _logger.LogError("Reconciliation failed closed. {Reason}", _state.BlockReason);
                return;
            }

            var droppedTrips = await _store.CollapseDuplicateClosedTripsAsync(cancellationToken);
            if (droppedTrips > 0)
            {
                await _store.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Collapsed {Trips} duplicate closed trip(s).", droppedTrips);
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
            if (bot is null || bot.StrategyVersion is null)
            {
                bot = bot is null ? null : await _store.GetBotAsync(bot.Id, cancellationToken) ?? bot;
            }

            if (bot?.StrategyVersion is null)
            {
                await CloseSnapshotWithoutStrategyAsync(bot, position, now, cancellationToken);
                closed++;
                _logger.LogInformation(
                    "LIVE Isolated {Symbol} is flat on Binance. Closed leftover snapshot without a strategy record.",
                    position.Symbol);
                continue;
            }

            if (await CloseGhostAsync(bot, position, cancellationToken))
            {
                closed++;
            }
        }

        if (closed > 0)
        {
            await _store.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Reconciled {Count} Isolated snapshot(s) that Binance no longer holds.", closed);
        }

        var unknown = new List<string>();
        foreach (var remote in live.OpenPositions.Where(row => row.Quantity > 0m))
        {
            var known = book.Any(position =>
                position.Quantity > 0m
                && string.Equals(position.Symbol, remote.Symbol, StringComparison.OrdinalIgnoreCase));
            if (!known)
            {
                unknown.Add("position " + remote.Symbol);
            }
        }

        foreach (var remote in live.OpenOrders)
        {
            if (string.IsNullOrWhiteSpace(remote.ClientOrderId)
                || await _store.GetOrderByClientOrderIdAsync(remote.ClientOrderId, cancellationToken) is null)
            {
                unknown.Add("order " + remote.Symbol);
            }
        }

        if (unknown.Count > 0)
        {
            var reason = "Unknown exchange state: " + string.Join(", ", unknown) + ". New live entries are blocked. Nothing was closed automatically.";
            _state.Fail(reason, _clock.UtcNow);
            _logger.LogError("Reconciliation exception. {Reason}", reason);
            return;
        }

        if (_state.BlockReason is not null)
        {
            return;
        }

        _state.Succeed(live.ApiKeyHint ?? "live-account", _clock.UtcNow);
        _logger.LogInformation("Reconciliation succeeded for {Account} at {At}.", _state.AccountId, _state.SucceededAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _state.Fail("Reconciliation failed: " + ex.Message, _clock.UtcNow);
            _logger.LogError(ex, "Reconciliation failed closed.");
        }
    }

    private async Task CloseSnapshotWithoutStrategyAsync(
        Bot? bot,
        Position position,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var quantity = position.Quantity;
        var exit = position.CurrentPrice > 0m ? position.CurrentPrice : position.AverageEntryPrice;
        if (_cache.TryGetTicker(position.Symbol, out var mark) && mark > 0m)
        {
            exit = mark;
        }

        var direction = position.Side == PositionSide.Short ? -1m : 1m;
        var pnl = direction * (exit - position.AverageEntryPrice) * quantity;
        position.Quantity = 0m;
        position.CurrentPrice = exit;
        position.UnrealizedPnL = 0m;
        position.RealizedPnL += pnl;
        position.ClosedAt = now;
        if (bot is null)
        {
            return;
        }

        var openTrade = await _store.GetOpenTradeAsync(bot.Id, cancellationToken);
        if (openTrade is null ||
            !string.Equals(openTrade.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var pnlPercent = position.AverageEntryPrice == 0m
            ? 0m
            : direction * (exit - position.AverageEntryPrice) / position.AverageEntryPrice * 100m;
        openTrade.Quantity = quantity > 0m ? quantity : openTrade.Quantity;
        openTrade.ExitPrice = exit;
        openTrade.PnL = pnl;
        openTrade.PnLPercent = pnlPercent;
        openTrade.ClosedAt = now;
    }

    private Task<bool> CloseGhostAsync(Bot bot, Position position, CancellationToken cancellationToken)
    {
        var reason = $"Local position {position.Symbol} is absent from a fresh exchange snapshot. It was left open. No exchange fill or realized PnL was created.";
        _state.Fail(reason, _clock.UtcNow);
        bot.LastError = reason;
        _logger.LogError("Reconciliation discrepancy for {Symbol}. {Reason}", position.Symbol, reason);
        return Task.FromResult(false);
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
