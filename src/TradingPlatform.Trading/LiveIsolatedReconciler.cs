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
/// LIVE Isolated truth is Binance. A local row still open after the exchange is flat is retired without a fill.
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

            var book = (await _store.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken)).ToList();
        var now = _clock.UtcNow;
        var missing = 0;
        var recovering = new List<string>();
        var unresolved = await _store.GetUnresolvedLiveOrdersAsync(cancellationToken);
        foreach (var position in book)
        {
            if (!IsolatedOccupancy.IsLiveGhost(position, live.OpenPositions, now))
            {
                continue;
            }

            if (unresolved.Any(order => order.BotId == position.BotId
                && string.Equals(order.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase)))
            {
                recovering.Add(position.Symbol);
                continue;
            }

            var bot = position.Bot ?? await _store.GetBotAsync(position.BotId, cancellationToken);
            await RecordMissingPositionAsync(bot, position, cancellationToken);
            missing++;
        }

        var running = await _store.GetRunningLiveBotsAsync(cancellationToken);
        var slotRows = await _store.GetRunningLiveSlotsAsync(cancellationToken);
        var caps = slotRows.ToDictionary(slot => slot.BotId);
        var openSymbols = book
            .Where(position => position.Quantity > 0m)
            .Select(position => position.Symbol)
            .Concat(live.OpenPositions.Where(row => row.Quantity > 0m).Select(row => row.Symbol))
            .ToArray();
        var claims = await _store.GetStrategyEntryClaimsAsync(openSymbols, cancellationToken);
        var entryBySymbol = claims
            .Where(claim => claim.Filled)
            .GroupBy(claim => IsolatedOccupancy.CoinKey(claim.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(claim => claim.CreatedAt).First().BotId,
                StringComparer.OrdinalIgnoreCase);
        var lastEntryBySymbol = claims
            .GroupBy(claim => IsolatedOccupancy.CoinKey(claim.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(claim => claim.CreatedAt).First().BotId,
                StringComparer.OrdinalIgnoreCase);
        var returned = 0;
        foreach (var position in book.Where(position => position.Quantity > 0m).ToList())
        {
            if (!entryBySymbol.TryGetValue(IsolatedOccupancy.CoinKey(position.Symbol), out var entryId)
                || entryId == position.BotId)
            {
                continue;
            }

            var entry = running.FirstOrDefault(bot => bot.Id == entryId);
            if (entry is null)
            {
                continue;
            }

            if (IsolatedOccupancy.MoveRowToEntryBot(position, entry, book))
            {
                returned++;
                _logger.LogWarning(
                    "Returned {Symbol} to the bot that placed the entry. The other strategy stays within its position limit.",
                    position.Symbol);
                continue;
            }

            var entryHolds = book.Any(row =>
                row.Id != position.Id
                && row.Quantity > 0m
                && row.BotId == entry.Id
                && string.Equals(row.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase));
            if (!entryHolds)
            {
                continue;
            }

            position.Quantity = 0m;
            position.UnrealizedPnL = 0m;
            position.ClosedAt = now;
            returned++;
        }

        var used = new Dictionary<Guid, HashSet<string>>();
        foreach (var position in book.Where(position => position.Quantity > 0m))
        {
            if (caps.TryGetValue(position.BotId, out var slot))
            {
                IsolatedOccupancy.AddSlot(used, slot.StrategyId, position.Symbol);
            }
        }

        var adopted = 0;
        foreach (var remote in live.OpenPositions.Where(row => row.Quantity > 0m))
        {
            var known = book.Any(position =>
                position.Quantity > 0m
                && string.Equals(position.Symbol, remote.Symbol, StringComparison.OrdinalIgnoreCase));
            if (known)
            {
                continue;
            }
            lastEntryBySymbol.TryGetValue(IsolatedOccupancy.CoinKey(remote.Symbol), out var entryId);
            var owner = IsolatedOccupancy.PickOwnerForNewRow(
                remote.Symbol,
                running,
                book,
                caps,
                used,
                entryId == Guid.Empty ? null : entryId);
            if (owner is null)
            {
                continue;
            }

            var side = IsolatedOccupancy.NormalizeSide(remote.Side) == "Short" ? PositionSide.Short : PositionSide.Long;
            var position = new Position
            {
                Bot = owner,
                BotId = owner.Id,
                Symbol = remote.Symbol,
                Side = side,
                Quantity = remote.Quantity,
                AverageEntryPrice = remote.EntryPrice,
                CurrentPrice = remote.MarkPrice,
                UnrealizedPnL = remote.UnrealizedPnL,
                RealizedPnL = 0m,
                OpenedAt = now
            };
            await _store.AddPositionAsync(position, cancellationToken);
            book.Add(position);
            if (caps.TryGetValue(owner.Id, out var ownerSlot))
            {
                IsolatedOccupancy.AddSlot(used, ownerSlot.StrategyId, remote.Symbol);
            }

            adopted++;
            owner.LastError =
                $"Exchange position {remote.Symbol} had no local row. It was recorded from the snapshot. No fill or mark-price PnL was created.";
            _logger.LogWarning(
                "Recorded exchange position {Symbol} for bot {BotId}. No fill was written.",
                remote.Symbol,
                owner.Id);
        }

        var collapsed = 0;
        foreach (var remote in live.OpenPositions.Where(row => row.Quantity > 0m))
        {
            var holders = book
                .Where(position =>
                    position.Quantity > 0m
                    && string.Equals(position.Symbol, remote.Symbol, StringComparison.OrdinalIgnoreCase))
                .OrderBy(position => position.OpenedAt)
                .ThenBy(position => position.BotId)
                .ToList();
            if (holders.Count < 2 || holders.All(position => position.Quantity != remote.Quantity))
            {
                continue;
            }

            foreach (var extra in holders.Skip(1))
            {
                extra.Quantity = 0m;
                extra.UnrealizedPnL = 0m;
                extra.ClosedAt = now;
                collapsed++;
            }
        }

        var retiredOrders = 0;
        foreach (var order in await _store.GetRestingLiveProtectionAsync(cancellationToken))
        {
            if (now - order.CreatedAt < IsolatedOccupancy.OverlayGrace)
            {
                continue;
            }

            var onBook = live.OpenOrders.Any(row =>
                OrderLedger.Same(order.ClientOrderId, order.ExchangeOrderId, row.ClientOrderId, row.ExchangeOrderId));
            if (onBook)
            {
                continue;
            }

            order.Status = OrderStatus.Cancelled;
            order.RemainingQuantity = 0m;
            order.RejectReason = "Not on a fresh Binance book. The local row was closed. No fill was created.";
            retiredOrders++;
        }

        if (missing > 0 || adopted > 0 || returned > 0 || collapsed > 0 || retiredOrders > 0)
        {
            await _store.SaveChangesAsync(cancellationToken);
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

        if (recovering.Count > 0)
        {
            var reason = $"{string.Join(", ", recovering)} is flat on Binance while an order is still being looked up. The local position is kept until the lookup books the real fill. New live entries are blocked.";
            _state.Fail(reason, _clock.UtcNow);
            _logger.LogWarning("Reconciliation waiting on order recovery. {Reason}", reason);
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

    private async Task RecordMissingPositionAsync(Bot? bot, Position position, CancellationToken cancellationToken)
    {
        var closedAt = _clock.UtcNow;
        position.Quantity = 0m;
        position.UnrealizedPnL = 0m;
        position.ClosedAt = closedAt;
        if (bot is not null)
        {
            var trade = await _store.GetOpenTradeAsync(bot.Id, cancellationToken);
            if (trade is not null
                && string.Equals(trade.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                trade.ClosedAt = closedAt;
                trade.FeeStatus = FeeKnowledge.Uncertain;
                trade.FeeAsset = null;
                trade.NetPnL = null;
            }

            bot.LastError =
                $"Local position {position.Symbol} was absent from a fresh exchange snapshot. The local row was closed. No exchange order, fill, or mark-price PnL was created. PnL is uncertain until the Binance trade history sync rebuilds this trip.";
        }

        _logger.LogWarning(
            "Retired local position {Symbol} absent from a fresh exchange snapshot. No fill or mark-price PnL was written.",
            position.Symbol);
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
