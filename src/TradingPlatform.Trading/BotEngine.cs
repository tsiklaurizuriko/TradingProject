using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Balances;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Signals;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using ExecutionFill = TradingPlatform.Domain.Orders.Execution;
using PaperOrderStateMachine = TradingPlatform.Execution.OrderStateMachine;

namespace TradingPlatform.Trading;

public sealed class BotEngine : IBotEngine
{
    private readonly ITradingStore _store;
    private readonly IPublicMarketDataClient _market;
    private readonly IMarketDataCache _cache;
    private readonly IStrategyEngine _strategy;
    private readonly StrategyDefinitionValidator _validator;
    private readonly IRiskEngine _risk;
    private readonly IExchangeConnectorFactory _connectors;
    private readonly ILiveAccountCache _live;
    private readonly ITradingRealtimePublisher _publisher;
    private readonly IClock _clock;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly TradingOptions _options;
    private readonly LiveIsolatedReconciler _reconcile;
    private readonly ReconciliationState _reconciliation;
    private readonly ILogger<BotEngine> _logger;
    private readonly IExchangeAccountService? _accounts;
    private readonly Dictionary<Guid, (decimal Stop, decimal Take, DateTimeOffset At)> _ratchetBackoff = new();

    public BotEngine(
        ITradingStore store,
        IPublicMarketDataClient market,
        IMarketDataCache cache,
        IStrategyEngine strategy,
        StrategyDefinitionValidator validator,
        IRiskEngine risk,
        IExchangeConnectorFactory connectors,
        ILiveAccountCache live,
        ITradingRealtimePublisher publisher,
        IClock clock,
        ICorrelationIdAccessor correlation,
        IOptions<TradingOptions> options,
        LiveIsolatedReconciler reconcile,
        ILogger<BotEngine> logger,
        IExchangeAccountService? accounts = null,
        ReconciliationState? reconciliation = null)
    {
        _store = store;
        _market = market;
        _cache = cache;
        _strategy = strategy;
        _validator = validator;
        _risk = risk;
        _connectors = connectors;
        _live = live;
        _publisher = publisher;
        _clock = clock;
        _correlation = correlation;
        _options = options.Value;
        _reconcile = reconcile;
        _reconciliation = reconciliation ?? new ReconciliationState();
        _logger = logger;
        _accounts = accounts;
    }

    public async Task EvaluateRunningBotsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RefreshLiveIsolatedBookAsync(cancellationToken);
            await _reconcile.ReconcileAsync(cancellationToken);
            await RecoverUnresolvedOrdersAsync(cancellationToken);
            if (_options.KillSwitchEnabled)
            {
                await _store.StopAllRunningBotsAsync("Kill switch is active.", cancellationToken);
                await _store.SaveChangesAsync(cancellationToken);
                return;
            }
            var bots = await _store.GetRunningBotsAsync(cancellationToken);
            await HandOffStoppedSnapshotsAsync(cancellationToken);
            await WatchUnattendedLivePositionsAsync(bots, cancellationToken);
            var cycleKlines = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase);
            var cyclePrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var cycleFilters = await LoadCycleFiltersAsync(cancellationToken);
            await ProtectOpenPositionsAsync(bots, cycleFilters, cancellationToken);
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var maxReconcileAge = TimeSpan.FromSeconds(Math.Max(1, _options.ReconciliationMaxAgeSeconds));
            var lastReconcileAttempt = _clock.UtcNow;
            foreach (var bot in bots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_reconciliation.IsFresh(_clock.UtcNow, maxReconcileAge)
                    && _clock.UtcNow - lastReconcileAttempt >= TimeSpan.FromSeconds(20))
                {
                    lastReconcileAttempt = _clock.UtcNow;
                    await RefreshLiveIsolatedBookAsync(cancellationToken);
                    await _reconcile.ReconcileAsync(cancellationToken);
                    await ProtectOpenPositionsAsync(bots, cycleFilters, cancellationToken);
                }

                try
                {
                    await EvaluateBotAsync(bot, bots, cycleKlines, cyclePrices, cycleFilters, claimed, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bot {BotId} cycle failed", bot.Id);
                    bot.LastError = ex.Message;
                }
            }

            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private async Task RecoverUnresolvedOrdersAsync(CancellationToken cancellationToken)
    {
        var pending = await _store.GetUnresolvedLiveOrdersAsync(cancellationToken);
        _reconciliation.SetUnresolvedCount(pending.Count);
        var now = _clock.UtcNow;
        foreach (var order in pending)
        {
            if (!OrderRecovery.Due(order.CreatedAt, order.UpdatedAt, now))
            {
                continue;
            }

            var connector = _connectors.Create(TradingMode.Live, order.ExchangeAccountId);
            OrderLookup lookup;
            try
            {
                lookup = await connector.GetOrderAsync(order.ClientOrderId, order.ExchangeOrderId, order.Symbol, cancellationToken);
            }
            catch (Exception ex)
            {
                lookup = OrderLookup.Unavailable(ex.Message);
                _logger.LogError(
                    ex,
                    "Unresolved order lookup failed. ClientOrderId {ClientOrderId} CorrelationId {CorrelationId}. The order was not sent again.",
                    order.ClientOrderId,
                    order.CorrelationId);
            }

            order.UpdatedAt = now;
            var decision = OrderRecovery.Decide(lookup);
            _logger.LogWarning(
                "Order recovery {Kind} for {ClientOrderId} CorrelationId {CorrelationId}: {Reason}",
                decision.Kind,
                order.ClientOrderId,
                order.CorrelationId,
                decision.Reason);
            if (decision.Kind == RecoveryKind.LookupUnavailable)
            {
                Record(order, OrderStatus.Uncertain, "binance-live");
                order.RejectReason = decision.Reason;
                _reconciliation.Fail(decision.Reason, now);
                continue;
            }

            if (decision.Kind == RecoveryKind.ConfirmedAbsent || decision.Order is null)
            {
                if (order.FilledQuantity > 0m)
                {
                    Record(order, OrderStatus.Uncertain, "binance-live");
                    order.RejectReason = "The exchange no longer returns this partially filled order. The booked quantity was kept.";
                    _reconciliation.Fail(order.RejectReason, now);
                    continue;
                }

                Record(order, OrderStatus.Failed, "binance-live");
                order.RejectReason = decision.Reason;
                continue;
            }

            await ApplyRecoveredFillAsync(order, decision.Order, cancellationToken);
        }

        if (pending.Count > 0)
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ApplyRecoveredFillAsync(Order order, ExchangeOrder confirmed, CancellationToken cancellationToken)
    {
        var feeRows = order.Executions?.Where(item => item.Fee > 0m || !string.IsNullOrWhiteSpace(item.FeeAsset)).ToList() ?? [];
        var feeAssets = feeRows.Select(item => item.FeeAsset).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (feeRows.Count > 0 && (feeAssets.Count != 1 || feeRows.Any(item => string.IsNullOrWhiteSpace(item.FeeAsset))))
        {
            Record(order, OrderStatus.Uncertain, "binance-live");
            order.RejectReason = "Booked commission assets disagree or are missing. The fill was not applied.";
            _reconciliation.Fail(order.RejectReason, _clock.UtcNow);
            return;
        }

        var bookedFee = feeRows.Sum(item => item.Fee);
        var application = FillAccounting.Apply(
            new BookedFill(order.FilledQuantity, order.AverageFillPrice, bookedFee, feeAssets.SingleOrDefault()),
            order.Quantity,
            ReportOf(confirmed));
        Record(order, application.Status, "binance-live");
        order.FilledQuantity = application.FilledQuantity;
        order.RemainingQuantity = application.RemainingQuantity;
        order.ExchangeOrderId = confirmed.ExchangeOrderId ?? order.ExchangeOrderId;
        order.AverageFillPrice = application.AverageFillPrice ?? order.AverageFillPrice;
        order.RejectReason = application.Uncertain ? application.Reason : order.RejectReason;
        if (application.Uncertain)
        {
            _reconciliation.Fail(application.Reason, _clock.UtcNow);
            return;
        }

        if (application.AdditionalFeeKnown)
        {
            await _store.AddExecutionAsync(new ExecutionFill
            {
                OrderId = order.Id,
                ExchangeTradeId = "local-fee:" + order.ClientOrderId + ":" + (bookedFee + application.AdditionalFee).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + (confirmed.FeeAsset ?? ""),
                Price = order.AverageFillPrice ?? 0m,
                Quantity = 0m,
                Fee = application.AdditionalFee,
                FeeAsset = confirmed.FeeAsset ?? "",
                FeeStatus = FeeKnowledge.Known,
                ExchangeTimestamp = confirmed.ExchangeTimestamp ?? _clock.UtcNow,
                CorrelationId = order.CorrelationId
            }, cancellationToken);
        }

        if (application.NewFill is null)
        {
            return;
        }

        var price = application.NewFill.Price;
        var booked = application.NewFill.Quantity;
        var fee = application.NewFill.FeeKnown ? application.NewFill.Fee : 0m;
        var positions = (await _store.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken))
            .Where(item => item.BotId == order.BotId && string.Equals(item.Symbol, order.Symbol, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (positions.Count > 1)
        {
            Record(order, OrderStatus.Uncertain, "binance-live");
            order.RejectReason = "More than one open position matches this coin. The fill was not applied.";
            _reconciliation.Fail(order.RejectReason, _clock.UtcNow);
            return;
        }

        var position = positions.SingleOrDefault();
        var protective = order.Type is OrderType.StopMarket or OrderType.TakeProfitMarket;
        var closes = protective || (position is not null && (
            (position.Side == PositionSide.Long && order.Side == OrderSide.Sell)
            || (position.Side == PositionSide.Short && order.Side == OrderSide.Buy)));
        if (closes && position is null)
        {
            Record(order, OrderStatus.Uncertain, "binance-live");
            order.RejectReason = "The fill cannot be matched to one open position. It was not applied.";
            _reconciliation.Fail(order.RejectReason, _clock.UtcNow);
            return;
        }

        if (closes && position is not null && order.Bot is not null)
        {
            var accountId = order.ExchangeAccountId ?? Guid.Empty;
            var usdt = await _store.GetOrCreateBalanceAsync(accountId, null, "USDT", TradingMode.Live, 0m, cancellationToken);
            var asset = await _store.GetOrCreateBalanceAsync(accountId, null, "BASE", TradingMode.Live, 0m, cancellationToken);
            await CloseFilledPositionAsync(
                order.Bot,
                position,
                order,
                booked,
                price,
                price * booked,
                application.NewFill.FeeKnown ? fee : 0m,
                usdt,
                asset,
                order.CorrelationId,
                application.NewFill.FeeKnown && !string.IsNullOrWhiteSpace(confirmed.FeeAsset),
                confirmed.FeeAsset,
                cancellationToken);
        }
        else if (position is not null)
        {
            var total = position.Quantity + booked;
            position.AverageEntryPrice = total <= 0m
                ? price
                : ((position.AverageEntryPrice * position.Quantity) + (price * booked)) / total;
            position.Quantity = total;
            position.CurrentPrice = price;
            if (application.NewFill.FeeKnown)
            {
                position.Fees += fee;
            }
        }
            else if (order.Bot is not null && !protective)
            {
                var coinAlreadyOpen = (await _store.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken))
                    .Any(row => row.Quantity > 0m
                        && string.Equals(row.Symbol, order.Symbol, StringComparison.OrdinalIgnoreCase));
                if (coinAlreadyOpen)
                {
                    order.Bot.LastError = $"Isolated {order.Symbol} is already open. The fill was not booked as a second position.";
                }
                else
                {
                    await _store.AddPositionAsync(new Position
                    {
                        BotId = order.BotId,
                        Symbol = order.Symbol,
                        Side = order.Side == OrderSide.Sell ? PositionSide.Short : PositionSide.Long,
                        Quantity = booked,
                        AverageEntryPrice = price,
                        CurrentPrice = price,
                        Fees = application.NewFill.FeeKnown ? fee : 0m,
                        OpenedAt = _clock.UtcNow
                    }, cancellationToken);
                    order.Bot.LastError = "A late fill was booked from the exchange. Protective orders still have to be confirmed.";
                }
            }

        await _store.AddExecutionAsync(new ExecutionFill
        {
            OrderId = order.Id,
            ExchangeTradeId = "local-fill:" + order.ClientOrderId + ":" + application.FilledQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Price = price,
            Quantity = booked,
            Fee = application.NewFill.FeeKnown ? fee : 0m,
            FeeAsset = application.NewFill.FeeKnown ? confirmed.FeeAsset ?? "" : "",
            FeeStatus = application.NewFill.FeeKnown && !string.IsNullOrWhiteSpace(confirmed.FeeAsset)
                ? FeeKnowledge.Known
                : FeeKnowledge.Unknown,
            ExchangeTimestamp = confirmed.ExchangeTimestamp ?? _clock.UtcNow,
            CorrelationId = order.CorrelationId
        }, cancellationToken);
    }

    private static ExchangeFillReport ReportOf(ExchangeOrder order) =>
        new(
            order.Status,
            order.FilledQuantity,
            order.AverageFillPrice ?? order.Price,
            order.FeeKnown ? order.Fee : null,
            order.ExchangeOrderId,
            null,
            order.CumulativeQuote,
            true,
            order.FeeKnown ? order.FeeAsset : null);

    private async Task RefreshLiveIsolatedBookAsync(CancellationToken cancellationToken)
    {
        if (_accounts is null)
        {
            return;
        }

        var current = _live.Current;
        if (current.UpdatedAt is { } at && DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(15))
        {
            return;
        }

        try
        {
            await _accounts.GetStatusAsync(Guid.Empty, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LIVE Isolated snapshot skipped this cycle");
        }
    }

    public async Task ClosePositionAsync(Guid positionId, CancellationToken cancellationToken = default)
    {
        var (bot, position, liveOverlay) = await ResolveCloseTargetAsync(positionId, cancellationToken);
        var now = _clock.UtcNow;
        var correlationId = _correlation.GetOrCreate();
        var stamp = now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var clientOrderId = bot.Mode == TradingMode.Live
            ? $"MC{bot.Id:N}"[..12] + (stamp.Length <= 10 ? stamp : stamp[^10..])
            : $"p-c-{bot.Id:N}-{stamp}";

        var lastPrice = position.CurrentPrice > 0m ? position.CurrentPrice : position.AverageEntryPrice;
        try
        {
            var marketPx = await _market.GetLastPriceAsync(position.Symbol, cancellationToken);
            if (marketPx > 0m)
            {
                if (bot.Mode == TradingMode.Live || lastPrice <= 0m || RelativeDrift(marketPx, lastPrice) <= 0.02m)
                {
                    lastPrice = marketPx;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Using marked price to close {Symbol}", position.Symbol);
        }

        _cache.SetTicker(position.Symbol, lastPrice, now);

        var market = await ResolveSymbolFiltersAsync(position.Symbol, null, cancellationToken);
        var quantity = PortfolioRisk.FloorToStep(
            position.Quantity,
            market?.StepSize ?? 0m,
            PortfolioRisk.EffectiveQuantityPrecision(market?.QuantityPrecision ?? 0, market?.StepSize ?? 0m));
        if (quantity <= 0m)
        {
            throw new DomainException(ErrorCodes.InvalidQuantity, "Position size is below the coin step size.");
        }

        var usdt = await _store.GetOrCreateBalanceAsync(
            bot.ExchangeAccountId,
            null,
            "USDT",
            TradingMode.Live,
            0m,
            cancellationToken);
        var baseAsset = await _store.GetOrCreateBalanceAsync(
            bot.ExchangeAccountId,
            null,
            market?.BaseAsset ?? position.Symbol.Replace("USDT", "", StringComparison.OrdinalIgnoreCase),
            TradingMode.Live,
            0m,
            cancellationToken);

        if (bot.Mode == TradingMode.Live)
        {
            try
            {
                await CancelLiveProtectiveOrdersAsync(bot, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Protective cancel failed before manual close of {Symbol}", position.Symbol);
            }
        }

        var closeSide = position.Side == PositionSide.Short ? OrderSide.Buy : OrderSide.Sell;
        await PlaceAndFillAsync(
            bot,
            closeSide,
            quantity,
            clientOrderId,
            correlationId,
            usdt,
            baseAsset,
            position,
            lastPrice,
            0m,
            cancellationToken,
            flatten: true);

        await _store.AddSignalAsync(new Signal
        {
            BotId = bot.Id,
            StrategyId = bot.StrategyVersion.StrategyId,
            StrategyVersionId = bot.StrategyVersionId,
            Symbol = position.Symbol,
            Timeframe = bot.Timeframe,
            SignalType = SignalType.Exit,
            Price = lastPrice,
            Timestamp = now,
            Reason = "Manual close",
            CorrelationId = correlationId
        }, cancellationToken);

        bot.LastError = "Manual close submitted to Binance.";

        DropLiveOverlay(liveOverlay ?? new LiveOpenPosition(
            position.Symbol,
            position.Side == PositionSide.Short ? "Short" : "Long",
            0m,
            position.AverageEntryPrice,
            lastPrice,
            0m,
            "Futures"));

        await _store.SaveChangesAsync(cancellationToken);
        await _publisher.PublishOverviewAsync(cancellationToken);
    }

    private async Task<(Bot Bot, Position Position, LiveOpenPosition? Overlay)> ResolveCloseTargetAsync(
        Guid positionId,
        CancellationToken cancellationToken)
    {
        var position = await _store.GetOpenPositionByIdAsync(positionId, cancellationToken);
        if (position is not null)
        {
            var owned = await _store.GetBotAsync(position.BotId, cancellationToken)
                ?? throw new DomainException(ErrorCodes.BotNotFound, "Bot for this position was not found.");
            return (owned, position, null);
        }

        var overlay = _live.Current.OpenPositions.FirstOrDefault(row =>
            StableGuid($"pos:{row.Venue}:{row.Symbol}:{row.Side}") == positionId);
        if (overlay is null)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "This position is not open.");
        }

        var overlaySide = overlay.Side is "Short" or "Sell" ? PositionSide.Short : PositionSide.Long;
        var liveBook = await _store.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken);
        var existing = liveBook
            .Where(row =>
                string.Equals(row.Symbol, overlay.Symbol, StringComparison.OrdinalIgnoreCase) &&
                row.Side == overlaySide)
            .ToList();
        if (existing.Count > 0)
        {
            var owned = IsolatedOccupancy.IsolatedOwner(existing);
            var ownedBot = await _store.GetBotAsync(owned.BotId, cancellationToken)
                ?? throw new DomainException(ErrorCodes.BotNotFound, "Bot for this position was not found.");
            return (ownedBot, owned, overlay);
        }

        var bots = await _store.ListBotsAsync(cancellationToken);
        var matches = bots
            .Where(b =>
                b.Mode == TradingMode.Live &&
                string.Equals(b.Symbol, overlay.Symbol, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var running = matches.Where(b => b.Status == BotStatus.Running).ToList();
        var pool = running.Count > 0 ? running : matches;
        var bot = IsolatedOccupancy.PickLiveOwner(overlay.Symbol, pool, liveBook, TradingMode.Live)
            ?? pool.OrderBy(b => b.StartedAt ?? DateTimeOffset.MaxValue).FirstOrDefault();
        if (bot is null)
        {
            throw new DomainException(
                ErrorCodes.BotNotFound,
                "No live bot for this coin. Create one so the close can be stored.");
        }

        var opened = new Position
        {
            BotId = bot.Id,
            Symbol = overlay.Symbol,
            Side = overlaySide,
            Quantity = overlay.Quantity,
            AverageEntryPrice = overlay.EntryPrice,
            CurrentPrice = overlay.MarkPrice,
            UnrealizedPnL = overlay.UnrealizedPnL,
            Fees = 0m,
            StopLossPercent = 0m,
            InitialRiskUsdt = 0m,
            OpenedAt = _clock.UtcNow
        };
        opened.Events.Add(new PositionEvent
        {
            EventType = "OPEN",
            Quantity = overlay.Quantity,
            Price = overlay.EntryPrice,
            CorrelationId = _correlation.GetOrCreate()
        });
        await _store.AddPositionAsync(opened, cancellationToken);
        return (bot, opened, overlay);
    }

    private void DropLiveOverlay(LiveOpenPosition overlay)
    {
        var current = _live.Current;
        current.OpenPositions = current.OpenPositions
            .Where(row =>
                !(string.Equals(row.Symbol, overlay.Symbol, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(row.Side, overlay.Side, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        current.UpdatedAt = null;
        _live.Set(current);
    }

    private static Guid StableGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string CycleKlineKey(string symbol, Timeframe timeframe) =>
        $"{symbol.ToUpperInvariant()}|{timeframe}";

    private async Task<IReadOnlyList<MarketCandle>> GetCycleKlinesAsync(
        string symbol,
        Timeframe timeframe,
        Dictionary<string, IReadOnlyList<MarketCandle>> cycleKlines,
        CancellationToken cancellationToken,
        int? limit = null)
    {
        var key = limit is null ? CycleKlineKey(symbol, timeframe) : $"{CycleKlineKey(symbol, timeframe)}|{limit}";
        if (cycleKlines.TryGetValue(key, out var hit))
        {
            return hit;
        }

        var candles = await _market.GetClosedKlinesAsync(symbol, timeframe, limit ?? _options.KlineLimit, cancellationToken);
        cycleKlines[key] = candles;
        return candles;
    }

    private async Task<decimal> GetCycleLastPriceAsync(
        string symbol,
        IReadOnlyList<MarketCandle> candles,
        Dictionary<string, decimal> cyclePrices,
        CancellationToken cancellationToken)
    {
        if (cyclePrices.TryGetValue(symbol, out var hit))
        {
            return hit;
        }

        decimal lastPrice;
        try
        {
            lastPrice = await _market.GetLastPriceAsync(symbol, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (candles.Count > 0)
        {
            lastPrice = candles[^1].Close;
        }

        cyclePrices[symbol] = lastPrice;
        return lastPrice;
    }

    private async Task<Dictionary<string, RankedUsdtSpotSymbol>> LoadCycleFiltersAsync(CancellationToken cancellationToken)
    {
        var filters = new Dictionary<string, RankedUsdtSpotSymbol>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var row in await _market.GetPaperUniverseAsync(cancellationToken))
            {
                filters[row.Symbol] = row;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USD-M exchange filters were not refreshed this cycle.");
        }

        return filters;
    }

    private async Task<Symbol?> ResolveSymbolFiltersAsync(
        string name,
        IReadOnlyDictionary<string, RankedUsdtSpotSymbol>? cycleFilters,
        CancellationToken cancellationToken)
    {
        var stored = await _store.GetSymbolAsync(name, cancellationToken);
        RankedUsdtSpotSymbol? ranked = null;
        if (cycleFilters is not null)
        {
            cycleFilters.TryGetValue(name, out ranked);
        }
        else
        {
            try
            {
                ranked = (await _market.GetPaperUniverseAsync(cancellationToken))
                    .FirstOrDefault(row => string.Equals(row.Symbol, name, StringComparison.OrdinalIgnoreCase));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not refresh exchange filters for {Symbol}", name);
            }
        }

        if (ranked is null)
        {
            return stored;
        }

        if (stored is not null
            && stored.TickSize == ranked.TickSize
            && stored.StepSize == ranked.StepSize
            && stored.MinQuantity == ranked.MinQuantity
            && stored.MinNotional == ranked.MinNotional
            && stored.PricePrecision == ranked.PricePrecision
            && stored.QuantityPrecision == ranked.QuantityPrecision)
        {
            return stored;
        }

        return await _store.UpsertSymbolAsync(
            ranked.Symbol,
            ranked.BaseAsset,
            ranked.QuoteAsset,
            ranked.TickSize,
            ranked.StepSize,
            ranked.MinQuantity,
            ranked.MinNotional,
            ranked.PricePrecision,
            ranked.QuantityPrecision,
            cancellationToken);
    }

    private async Task EvaluateBotAsync(
        Bot bot,
        IReadOnlyList<Bot> running,
        Dictionary<string, IReadOnlyList<MarketCandle>> cycleKlines,
        Dictionary<string, decimal> cyclePrices,
        IReadOnlyDictionary<string, RankedUsdtSpotSymbol> cycleFilters,
        HashSet<string> claimed,
        CancellationToken cancellationToken)
    {
        if (bot.Status is not BotStatus.Running)
        {
            return;
        }

        if (bot.Mode != TradingMode.Live)
        {
            bot.Status = BotStatus.Stopped;
            bot.StoppedAt = _clock.UtcNow;
            bot.LastError = $"This bot is stored as {bot.Mode}. Only live bots can run. The historical record was not executed.";
            return;
        }

        var klineLimit = TemplateKey(bot) is StrategyTemplateKeys.TsMomentum285 or StrategyTemplateKeys.BtcDailyMax10 ? 500 : (int?)null;
        var candles = await GetCycleKlinesAsync(bot.Symbol, bot.Timeframe, cycleKlines, cancellationToken, klineLimit);
        var lastPrice = await GetCycleLastPriceAsync(bot.Symbol, candles, cyclePrices, cancellationToken);
        var now = _clock.UtcNow;
        _cache.SetKlines(bot.Symbol, bot.Timeframe, candles);
        _cache.SetTicker(bot.Symbol, lastPrice, now);
        await _publisher.PublishTickerAsync(bot.Symbol, lastPrice, now, cancellationToken);

        var symbol = await ResolveSymbolFiltersAsync(bot.Symbol, cycleFilters, cancellationToken);
        if (symbol is not null && candles.Count > 0)
        {
            try
            {
                await _store.UpsertClosedCandleAsync(symbol.Id, bot.Timeframe, candles[^1], cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Closed candle persist skipped for {Symbol} {Timeframe}", bot.Symbol, bot.Timeframe);
            }
        }

        var position = await _store.GetOpenPositionAsync(bot.Id, bot.Symbol, cancellationToken);
        if (position is not null)
        {
            position.CurrentPrice = lastPrice;
            var direction = position.Side == PositionSide.Short ? -1m : 1m;
            var openPnl = direction * (lastPrice - position.AverageEntryPrice) * position.Quantity;
            position.UnrealizedPnL = openPnl;
            if (openPnl > position.MaxFavorableExcursion)
            {
                position.MaxFavorableExcursion = openPnl;
            }

            if (-openPnl > position.MaxAdverseExcursion)
            {
                position.MaxAdverseExcursion = -openPnl;
            }
        }

        var book = await _store.GetOpenPositionsForModeAsync(bot.Mode, cancellationToken);
        var claimKey = bot.Mode + ":" + IsolatedOccupancy.CoinKey(bot.Symbol);
        var liveBook = bot.Mode == TradingMode.Live ? _live.Current.OpenPositions : null;
        var liveAuth = bot.Mode == TradingMode.Live && IsolatedOccupancy.HasFreshFuturesBook(_live.Current);
        var coinOpen = IsolatedOccupancy.IsCoinOpen(bot.Symbol, book, liveBook, liveAuth, now);
        if (position is not null || coinOpen)
        {
            claimed.Add(claimKey);
        }

        if (position is null && coinOpen && !IsolatedOccupancy.IsOwner(bot, running, book))
        {
            var owner = IsolatedOccupancy.PickLiveOwner(bot.Symbol, running, book, bot.Mode);
            bot.LastError = owner is null
                ? $"Isolated {bot.Symbol} is already occupied. This bot will not enter."
                : $"Isolated {bot.Symbol} is occupied by {owner.Name}. This bot will not enter.";
            return;
        }

        var overlayProtect = await EnsureLiveOverlayProtectionAsync(
            bot,
            running,
            book,
            position,
            lastPrice,
            symbol,
            cancellationToken);
        if (overlayProtect.Handled)
        {
            return;
        }

        if (overlayProtect.Position is not null)
        {
            position = overlayProtect.Position;
        }

        if (position is not null)
        {
            await RatchetOpenProtectionAsync(bot, running, book, position, lastPrice, symbol, cancellationToken);
        }

        if (position is null && StrategySlotsFull(bot, running, book, liveBook, liveAuth, now))
        {
            var cap = bot.RiskProfile?.MaxSimultaneousPositions ?? 0;
            bot.LastError = cap > 0
                ? $"This strategy already has {cap} open. Waiting for one to close."
                : "This strategy already has its open positions. Waiting for one to close.";
            return;
        }

        var stopBefore = position?.StopLossPrice ?? 0m;
        var stopPercentBefore = position?.StopLossPercent ?? 0m;
        var stopClamped = ClampStopToProfile(position, bot, lastPrice, symbol);
        if (position is not null &&
            HitsProtectiveExit(position, lastPrice, out var protectiveReason) &&
            (bot.Mode == TradingMode.Live || !StrategyTemplateKeys.IsImported(TemplateKey(bot))))
        {
            bot.LastError = $"Local {protectiveReason} was reached. That is not an exchange fill. The position stays open until Binance reports the protective order.";
            _reconciliation.Fail(bot.LastError, now);
            return;
        }

        if (stopClamped && bot.Mode == TradingMode.Live && position is not null && IsolatedOccupancy.IsOwner(bot, running, book))
        {
            try
            {
                var stopConnector = _connectors.Create(bot.Mode, bot.ExchangeAccountId);
                var placed = await AttachLiveProtectiveStopsAsync(
                    bot,
                    stopConnector,
                    position.StopLossPrice,
                    position.TakeProfitPrice,
                    position.Side,
                    cancellationToken,
                    replaceStop: true,
                    replaceTake: false);
                if (!placed.StopPlaced)
                {
                    position.StopLossPrice = stopBefore;
                    position.StopLossPercent = stopPercentBefore;
                }
            }
            catch (Exception ex)
            {
                position.StopLossPrice = stopBefore;
                position.StopLossPercent = stopPercentBefore;
                _logger.LogWarning(ex, "Could not tighten the live stop for {Symbol}", bot.Symbol);
            }
        }

        if (candles.Count == 0)
        {
            bot.LastError = "Waiting for closed candles from Binance public market data.";
            return;
        }

        var definition = _validator.Parse(bot.StrategyVersion.DefinitionJson);
        var nearMiss = StrategyTemplateKeys.IsNearMiss(definition.Template);
        SignalType signalType;
        string reason;
        decimal? describedStop = null;
        decimal? describedTake = null;
        if (nearMiss)
        {
            var block = NearMissGate.BlockReason(_options, definition.Template, bot.Mode);
            if (block is not null)
            {
                bot.LastError = block;
                return;
            }

            var books = await LoadNearMissBooksAsync(bot, cycleKlines, cancellationToken);
            signalType = ContextualPriceActionSignals.AtLastClosed(
                StrategyTemplateKeys.NearMissHypothesisId(definition.Template),
                books,
                out reason);
        }
        else if (StrategyTemplateKeys.IsCrossSectionalReversal(definition.Template))
        {
            var block = CrossSectionalReversalGate.BlockOrders(_options, definition.Template, bot.Mode);
            if (block is not null)
            {
                bot.LastError = block;
                return;
            }

            if (bot.Timeframe != Timeframe.FifteenMinutes)
            {
                bot.LastError = "Cross-sectional reversal ranks the BTC 15-minute clock.";
                return;
            }

            var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in book)
            {
                if (row.Quantity > 0m && !string.IsNullOrWhiteSpace(row.Symbol))
                {
                    occupied.Add(row.Symbol);
                }
            }

            if (liveBook is not null)
            {
                foreach (var row in liveBook)
                {
                    if (row.Quantity > 0m && !string.IsNullOrWhiteSpace(row.Symbol))
                    {
                        occupied.Add(row.Symbol);
                    }
                }
            }

            var universe = new List<(string Symbol, IReadOnlyList<MarketCandle> Candles)>();
            foreach (var name in _cache.GetKlineSymbols(Timeframe.FifteenMinutes))
            {
                var series = _cache.GetKlines(name, Timeframe.FifteenMinutes);
                if (series.Count > 0)
                {
                    universe.Add((name, series));
                }
            }

            var decision = CrossSectionalLiveBook.Decide(
                definition.Template,
                bot.Symbol,
                _options,
                universe,
                occupied,
                position is not null);
            if (decision.Signal is SignalType.NoAction)
            {
                bot.LastError = decision.Reason;
                return;
            }

            signalType = decision.Signal;
            reason = decision.Reason;
        }
        else
        {
            IReadOnlyList<decimal?>? openInterest = null;
            IReadOnlyList<decimal?>? funding = null;
            var templateKey = TemplateKey(bot);
            if (string.Equals(templateKey, StrategyTemplateKeys.FlowZone, StringComparison.OrdinalIgnoreCase))
            {
                var pair = await _market.GetOpenInterestPairAsync(bot.Symbol, cancellationToken);
                if (pair.Previous is { } previous && pair.Latest is { } latest)
                {
                    openInterest = new decimal?[] { previous, latest };
                }
            }
            else if (string.Equals(templateKey, StrategyTemplateKeys.SqueezeWatch, StringComparison.OrdinalIgnoreCase))
            {
                var day = await _market.GetOpenInterestDayAsync(bot.Symbol, cancellationToken);
                if (day.DayAgo is { } dayAgo && day.Latest is { } latest)
                {
                    openInterest = new decimal?[] { dayAgo, latest };
                }

                var rate = await _market.GetLastFundingRateAsync(bot.Symbol, cancellationToken);
                if (rate is { } fundingRate)
                {
                    funding = new decimal?[] { fundingRate };
                }
            }

            var context = new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = lastPrice,
                HasOpenPosition = position is not null,
                AverageEntryPrice = position?.AverageEntryPrice,
                PositionSide = position?.Side ?? PositionSide.Long,
                PositionOpenedAt = position?.OpenedAt,
                ProtectiveStopPrice = position?.StopLossPrice is > 0m ? position.StopLossPrice : null,
                Symbol = bot.Symbol,
                OpenInterest = openInterest,
                FundingRate = funding
            };
            var quote = _strategy.EvaluateDetailAt(definition, context, new CausalIndicatorCache(candles), candles.Count - 1);
            signalType = quote.Signal;
            reason = quote.Reason;
            describedStop = quote.SuggestedStop;
            describedTake = quote.SuggestedTakeProfit;
        }

        var lastCandle = candles[^1];
        var clientOrderId = CandleIdempotency.Key(bot.Id, lastCandle.OpenTime, bot.Mode == TradingMode.Live);
        var correlationId = _correlation.GetOrCreate();

        if (signalType is SignalType.NoAction or SignalType.Hold)
        {
            if (bot.LastError is null || !bot.LastError.Contains("protection is incomplete", StringComparison.Ordinal))
            {
                bot.LastError = reason;
            }

            return;
        }

        await _store.AddSignalAsync(new Signal
        {
            BotId = bot.Id,
            StrategyId = bot.StrategyVersion.StrategyId,
            StrategyVersionId = bot.StrategyVersionId,
            Symbol = bot.Symbol,
            Timeframe = bot.Timeframe,
            SignalType = signalType,
            Price = lastPrice,
            Timestamp = lastCandle.CloseTime,
            Reason = reason,
            MetadataJson = nearMiss ? NearMissMetadata(definition.Template ?? "", lastCandle.CloseTime) : null,
            CorrelationId = correlationId
        }, cancellationToken);

        if (await _store.HasClientOrderAsync(clientOrderId, cancellationToken))
        {
            bot.LastError = "Signal already executed for this candle.";
            return;
        }

        var usdt = await _store.GetOrCreateBalanceAsync(
            bot.ExchangeAccountId,
            null,
            "USDT",
            TradingMode.Live,
            0m,
            cancellationToken);
        var btc = await _store.GetOrCreateBalanceAsync(
            bot.ExchangeAccountId,
            null,
            symbol?.BaseAsset ?? "BTC",
            TradingMode.Live,
            0m,
            cancellationToken);

        var connector = _connectors.Create(bot.Mode, bot.ExchangeAccountId);
        book = await _store.GetOpenPositionsForModeAsync(bot.Mode, cancellationToken);
        var unrealized = book.Sum(p => p.UnrealizedPnL);
        decimal equityUsdt;
        decimal availableUsdt;
        if (bot.Mode == TradingMode.Live)
        {
            var live = _live.Current;
            availableUsdt = live.UsdtFree ?? live.FuturesUsdt;
            if (availableUsdt <= 0m)
            {
                var liveBalances = await connector.GetBalancesAsync(cancellationToken);
                availableUsdt = liveBalances.FirstOrDefault(b => b.Asset == "USDT")?.Free ?? 0m;
            }

            var liveUnrealized = live.OpenPositions.Sum(p => p.UnrealizedPnL);
            equityUsdt = live.FuturesEquity > 0m
                ? live.FuturesEquity
                : availableUsdt + liveUnrealized;
        }
        else
        {
            availableUsdt = usdt.Free;
            equityUsdt = usdt.Free + usdt.Locked + unrealized;
        }

        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var profile = bot.RiskProfile
            ?? await _store.GetConservativeRiskAsync(cancellationToken);
        if (StrategyTemplateKeys.IsCrossSectionalReversal(definition.Template))
        {
            profile = CrossSectionalRiskBook.Overlay(profile, _options.CrossSectionalReversal);
        }
        var accountDaily = await _store.SumClosedPnLSinceForModeAsync(bot.Mode, dayStart, cancellationToken) + unrealized;
        var strategyId = bot.StrategyVersion.StrategyId;
        var strategyBotIds = running
            .Where(peer => peer.StrategyVersion.StrategyId == strategyId)
            .Select(peer => peer.Id)
            .ToHashSet();
        var strategyVersionIds = running
            .Where(peer => peer.StrategyVersion.StrategyId == strategyId)
            .Select(peer => peer.StrategyVersionId)
            .ToHashSet();
        foreach (var row in book)
        {
            if (row.Bot?.StrategyVersion?.StrategyId != strategyId)
            {
                continue;
            }

            strategyBotIds.Add(row.BotId);
            strategyVersionIds.Add(row.Bot.StrategyVersionId);
        }

        var strategyBook = IsolatedOccupancy.OccupiedByStrategy(book, strategyId, strategyBotIds, strategyVersionIds);
        var openRisk = IsolatedOccupancy.PlannedRiskPercent(strategyBook, availableUsdt);
        var streak = await _store.GetLossStreakForModeAsync(bot.Mode, cancellationToken);
        var exchangeCap = 0m;
        try
        {
            exchangeCap = await connector.GetMaxIsolatedLeverageAsync(bot.Symbol, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Isolated leverage cap for {Symbol}", bot.Symbol);
        }

        var snapshot = new RiskSnapshot
        {
            Equity = equityUsdt,
            AvailableBalance = availableUsdt,
            DailyRealizedPnL = await _store.SumClosedPnLSinceAsync(bot.Id, dayStart, cancellationToken),
            Symbol = bot.Symbol,
            Price = lastPrice,
            AccountDailyPnL = accountDaily,
            SymbolAlreadyOpen = IsolatedOccupancy.IsCoinOpen(
                bot.Symbol,
                book,
                bot.Mode == TradingMode.Live ? _live.Current.OpenPositions : null,
                bot.Mode == TradingMode.Live && IsolatedOccupancy.HasFreshFuturesBook(_live.Current),
                now),
            OpenPositionCount = IsolatedOccupancy.UniqueCoinsForStrategy(
                book,
                strategyId,
                bot.Mode == TradingMode.Live ? _live.Current.OpenPositions : null,
                liveAuthoritative: false,
                now,
                strategyBotIds,
                strategyVersionIds),
            OpenRiskPercent = openRisk,
            ConsecutiveLosses = streak.ConsecutiveLosses,
            LastLossAt = streak.LastLossAt,
            MarketDataAgeMs = 0,
            Sizing = new RiskSizingHints
            {
                StepSize = symbol?.StepSize ?? 0m,
                MinQuantity = symbol?.MinQuantity ?? 0m,
                MinNotional = symbol?.MinNotional ?? 0m,
                QuantityPrecision = PortfolioRisk.EffectiveQuantityPrecision(
                    symbol?.QuantityPrecision ?? 0,
                    symbol?.StepSize ?? 0m),
                ExchangeMaxLeverage = exchangeCap,
                TakerFeePercent = bot.Mode == TradingMode.Live ? 0m : RiskEngine.DefaultTakerFeePercent,
                SlippagePercent = bot.Mode == TradingMode.Live ? 0m : RiskEngine.DefaultSlippagePercent
            }
        };

        if (position is not null)
        {
            var opposite = (position.Side == PositionSide.Long && signalType == SignalType.Sell)
                || (position.Side == PositionSide.Short && signalType == SignalType.Buy);
            var flatten = signalType is SignalType.Exit || opposite;
            if (flatten)
            {
                await ClosePositionAsync(position.Id, cancellationToken);
                if (signalType is SignalType.Exit)
                {
                    bot.LastError = reason;
                    return;
                }

                position = null;
            }
            else
            {
                bot.LastError = reason;
                return;
            }
        }

        if (signalType is not (SignalType.Buy or SignalType.Sell))
        {
            bot.LastError = reason;
            return;
        }

        if (bot.Mode == TradingMode.Live)
        {
            var maxAge = TimeSpan.FromSeconds(Math.Max(1, _options.ReconciliationMaxAgeSeconds));
            var filtersReady = symbol is { StepSize: > 0m, MinQuantity: > 0m, MinNotional: > 0m, TickSize: > 0m };
            var blocked = _reconciliation.BlockReason is not null
                || await _store.HasUnresolvedEntryAsync(bot.Id, bot.Symbol, cancellationToken);
            var gate = LiveEntryGate.Block(new LiveEntryFacts(
                bot.Mode,
                _options.LiveTradingEnabled,
                _options.KillSwitchEnabled,
                _reconciliation.IsFresh(_clock.UtcNow, maxAge) && IsolatedOccupancy.HasFreshFuturesBook(_live.Current),
                blocked,
                bot.StrategyVersion.Strategy?.IsEnabled == true,
                true,
                filtersReady,
                false));
            if (gate is not null)
            {
                bot.LastError = gate;
                return;
            }
        }

        var maxLosses = profile.MaxConsecutiveLosses > 0 ? profile.MaxConsecutiveLosses : 5;
        var coinCooldown = TimeSpan.FromMinutes(profile.CooldownMinutes > 0 ? profile.CooldownMinutes : 30);
        var (coinLosses, coinLastLoss) = await _store.GetSymbolLossStreakAsync(bot.Mode, bot.Symbol, cancellationToken);
        if (coinLosses >= maxLosses && coinLastLoss is { } coinLossAt && now < coinLossAt + coinCooldown)
        {
            bot.LastError = $"Isolated {bot.Symbol} lost {coinLosses} times in a row. New entries wait until the cooldown ends.";
            return;
        }

        if (!claimed.Add(claimKey))
        {
            bot.LastError = nearMiss
                ? $"RejectedSameSymbol Isolated {bot.Symbol} is already occupied. This bot will not enter."
                : $"Isolated {bot.Symbol} is already occupied. This bot will not enter.";
            return;
        }

        snapshot = snapshot with { Side = signalType == SignalType.Sell ? PositionSide.Short : PositionSide.Long };
        if (StrategyTemplateKeys.IsFlatRange(definition.Template) && describedStop is decimal stop && lastPrice > 0m)
        {
            var stopPct = Math.Abs(lastPrice - stop) / lastPrice * 100m;
            var takePct = describedTake is decimal take
                ? Math.Abs(take - lastPrice) / lastPrice * 100m
                : profile.TakeProfitPercent;
            var longSide = signalType == SignalType.Buy;
            var stopSide = longSide ? stop < lastPrice : stop > lastPrice;
            var takeSide = describedTake is not decimal lockedTake
                || (longSide ? lockedTake > lastPrice : lockedTake < lastPrice);
            if (!stopSide || !takeSide || stopPct <= 0m || takePct <= 0m)
            {
                bot.LastError = "The strategy stop and take profit do not sit on the right sides of price.";
                return;
            }

            if (StrategyTemplateKeys.IsFlatRange(definition.Template)
                && (stopPct < FlatRangeStrategy.MinStopPercent || takePct <= stopPct))
            {
                bot.LastError = "Flat range stop and take profit no longer sit on the right sides of price.";
                return;
            }

            var bookStop = profile.StopLossPercent;
            profile = FlatRangeRisk(profile, stopPct, takePct);
            var hints = snapshot.Sizing ?? new RiskSizingHints();
            if (bookStop > stopPct)
            {
                hints = hints with { SizingStopLossPercent = bookStop };
            }

            snapshot = snapshot with
            {
                Sizing = hints with { MaxMarginUsdt = FlatRangeStrategy.MaxEntryMarginUsdt }
            };
        }
        else if (StrategyTemplateKeys.IsFlatRange(definition.Template))
        {
            bot.LastError = "Flat range did not lock a stop and a take profit.";
            return;
        }

        var risk = _risk.Evaluate(signalType, profile, snapshot, now);
        if (risk.Decision != RiskDecision.Approved)
        {
            var detail = risk.HaltAccount ? $"Risk Lock. {risk.Reason}" : risk.Reason;
            bot.LastError = nearMiss ? $"{NearMissGate.RejectLabel(risk.Reason)} {detail}" : detail;
            return;
        }

        var quantity = risk.ApprovedQuantity;
        if (quantity <= 0m)
        {
            bot.LastError = "Calculated position size is below exchange minimum and cannot be traded within the configured risk.";
            return;
        }

        if (bot.Mode == TradingMode.Live)
        {
            var riskBlock = RiskLiveGuard.Reject(
                bot.Mode,
                profile,
                new LiveRiskFacts(
                    equityUsdt,
                    openRisk,
                    snapshot.OpenPositionCount,
                    snapshot.SymbolAlreadyOpen ? 1 : 0,
                    snapshot.AccountDailyPnL,
                    profile.RiskPerTradePercent,
                    risk.Plan?.Leverage ?? profile.MaxLeverage,
                    profile.StopLossPercent,
                    quantity,
                    quantity * lastPrice,
                    symbol?.MinQuantity ?? 0m,
                    symbol?.MinNotional ?? 0m,
                    availableUsdt,
                    risk.Plan?.IsolatedMargin ?? 0m,
                    _options.KillSwitchEnabled,
                    streak.ConsecutiveLosses,
                    streak.LastLossAt,
                    now,
                    true));
            if (riskBlock is not null)
            {
                bot.LastError = riskBlock;
                return;
            }

            var leverage = (int)Math.Max(1m, Math.Floor(risk.Plan?.Leverage ?? profile.MaxLeverage));
            await connector.PrepareSymbolRiskAsync(bot.Symbol, MarginMode.Isolated, leverage, cancellationToken);
        }

        await PlaceAndFillAsync(
            bot,
            signalType == SignalType.Sell ? OrderSide.Sell : OrderSide.Buy,
            quantity,
            clientOrderId,
            correlationId,
            usdt,
            btc,
            position,
            lastPrice,
            profile.StopLossPercent,
            cancellationToken,
            plan: risk.Plan);
    }

    private async Task PlaceAndFillAsync(
        Bot bot,
        OrderSide side,
        decimal quantity,
        string clientOrderId,
        string correlationId,
        Balance usdt,
        Balance baseAsset,
        Position? position,
        decimal lastPrice,
        decimal stopLossPercent,
        CancellationToken cancellationToken,
        bool flatten = false,
        RiskPlan? plan = null)
    {
        var connector = _connectors.Create(bot.Mode, bot.ExchangeAccountId);
        var orderSymbol = flatten && position is not null ? position.Symbol : bot.Symbol;
        var market = await ResolveSymbolFiltersAsync(orderSymbol, null, cancellationToken);
        quantity = PortfolioRisk.FloorToStep(
            quantity,
            market?.StepSize ?? 0m,
            PortfolioRisk.EffectiveQuantityPrecision(market?.QuantityPrecision ?? 0, market?.StepSize ?? 0m));
        if (quantity <= 0m)
        {
            throw new DomainException(ErrorCodes.InvalidQuantity, "Order size is below the coin step size.");
        }

        var order = new Order
        {
            BotId = bot.Id,
            ExchangeAccountId = bot.ExchangeAccountId,
            StrategyId = bot.StrategyVersion.StrategyId,
            StrategyVersionId = bot.StrategyVersionId,
            Symbol = orderSymbol,
            Side = side,
            Type = OrderType.Market,
            Status = OrderStatus.New,
            Quantity = quantity,
            RemainingQuantity = quantity,
            ClientOrderId = clientOrderId,
            IdempotencyKey = clientOrderId,
            Mode = bot.Mode,
            CorrelationId = correlationId
        };
        if (bot.Mode != TradingMode.Live)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Only a live bot can submit an order.");
        }

        var source = "binance-live";
        Record(order, OrderStatus.Submitting, source);
        order.SubmittedAt = _clock.UtcNow;
        await _store.AddOrderAsync(order, cancellationToken);
        await _store.SaveChangesAsync(cancellationToken);

        ExchangeOrder? fill = null;
        try
        {
            fill = await connector.PlaceOrderAsync(
                new PlaceOrderRequest(
                    clientOrderId,
                    orderSymbol,
                    side,
                    OrderType.Market,
                    quantity,
                    null,
                    TimeSpan.FromSeconds(5),
                    flatten),
                cancellationToken);
        }
        catch (Exception ex)
        {
            OrderLookup lookup;
            try
            {
                lookup = await connector.GetOrderAsync(clientOrderId, null, orderSymbol, cancellationToken);
            }
            catch (Exception lookupEx)
            {
                lookup = OrderLookup.Unavailable(lookupEx.Message);
                _logger.LogError(
                    lookupEx,
                    "Order lookup failed after an ambiguous submit. ClientOrderId {ClientOrderId} CorrelationId {CorrelationId}. The order was not sent again.",
                    clientOrderId,
                    correlationId);
            }

            var decision = OrderRecovery.Decide(lookup);
            _logger.LogWarning(
                "Order recovery {Kind} for {ClientOrderId} CorrelationId {CorrelationId}: {Reason}",
                decision.Kind,
                clientOrderId,
                correlationId,
                decision.Reason);
            if (decision.Kind != RecoveryKind.Confirmed || decision.Order is null)
            {
                var absent = decision.Kind == RecoveryKind.ConfirmedAbsent;
                Record(order, absent ? OrderStatus.Failed : OrderStatus.Uncertain, source);
                order.RejectReason = decision.Reason + " " + ex.Message;
                order.UpdatedAt = _clock.UtcNow;
                await _store.SaveChangesAsync(cancellationToken);
                if (!absent)
                {
                    bot.LastError = decision.Reason;
                    _reconciliation.Fail(decision.Reason, _clock.UtcNow);
                    return;
                }

                throw;
            }

            fill = decision.Order;
        }

        if (fill is null)
        {
            Record(order, OrderStatus.Uncertain, source);
            order.RejectReason = "The exchange response was empty. The order was not sent again.";
            await _store.SaveChangesAsync(cancellationToken);
            bot.LastError = order.RejectReason;
            return;
        }

        var confirmed = fill;
        var application = FillAccounting.Apply(
            new BookedFill(order.FilledQuantity, order.AverageFillPrice, 0m),
            quantity,
            ReportOf(confirmed));
        Record(order, application.Status, source);
        order.FilledQuantity = application.FilledQuantity;
        order.RemainingQuantity = application.RemainingQuantity;
        order.ExchangeOrderId = confirmed.ExchangeOrderId;
        order.ExchangeTimestamp = confirmed.ExchangeTimestamp;
        order.RejectReason = application.Uncertain ? application.Reason : order.RejectReason;
        if (application.NewFill is null)
        {
            await _store.SaveChangesAsync(cancellationToken);
            bot.LastError = application.Reason;
            if (application.Uncertain || application.Status is OrderStatus.Rejected or OrderStatus.Failed or OrderStatus.Expired or OrderStatus.Cancelled)
            {
                if (application.Status is not OrderStatus.Cancelled)
                {
                    throw new DomainException(
                        application.Uncertain ? ErrorCodes.ReconciliationRequired : ErrorCodes.OrderRejected,
                        application.Reason);
                }
            }

            return;
        }

        quantity = application.NewFill.Quantity;
        var fillPrice = application.NewFill.Price;
        order.Price = fillPrice;
        order.AverageFillPrice = application.AverageFillPrice ?? fillPrice;

        if (!flatten && fillPrice <= 0m)
        {
            await _store.SaveChangesAsync(cancellationToken);
            bot.LastError =
                "Live fill has no usable price. Automatic close is disabled; waiting for Binance reconciliation before attaching SL/TP.";
            _logger.LogCritical(
                "Live fill has no usable price for bot {BotId} {Symbol}. The project will not auto-close it; Binance reconciliation must recover and protect it.",
                bot.Id,
                bot.Symbol);
            return;
        }
        var notional = fillPrice * quantity;
        var feeKnown = application.NewFill.FeeKnown && !string.IsNullOrWhiteSpace(confirmed.FeeAsset);
        var fee = feeKnown ? application.NewFill.Fee : 0m;
        await _store.AddExecutionAsync(new ExecutionFill
        {
            OrderId = order.Id,
            Order = order,
            ExchangeTradeId = "local-fill:" + order.ClientOrderId + ":" + application.FilledQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Price = fillPrice,
            Quantity = quantity,
            Fee = fee,
            FeeAsset = feeKnown ? confirmed.FeeAsset! : "",
            FeeStatus = feeKnown ? FeeKnowledge.Known : FeeKnowledge.Unknown,
            IsMaker = false,
            ExchangeTimestamp = confirmed.ExchangeTimestamp ?? _clock.UtcNow,
            CorrelationId = correlationId
        }, cancellationToken);

        if (flatten && position is not null)
        {
            await CloseFilledPositionAsync(
                bot,
                position,
                order,
                quantity,
                fillPrice,
                notional,
                fee,
                usdt,
                baseAsset,
                correlationId,
                feeKnown,
                confirmed.FeeAsset,
                cancellationToken);
            await _store.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!flatten)
        {
            var openedSide = side == OrderSide.Sell ? PositionSide.Short : PositionSide.Long;
            var openedMargin = 0m;
            var leverage = plan?.Leverage ?? Math.Max(1m, bot.RiskProfile.MaxLeverage);
            var slPercent = plan?.StopLossPercent ?? stopLossPercent;
            var tpPercent = plan?.TakeProfitPercent ?? 0m;
            openedMargin = plan?.IsolatedMargin ?? PortfolioRisk.IsolatedMargin(notional, leverage);

            decimal slPrice;
            decimal tpPrice;
            try
            {
                (slPrice, tpPrice) = LiveProtectivePrices.FromEntry(
                    fillPrice,
                    slPercent,
                    tpPercent,
                    market?.TickSize ?? 0m,
                    openedSide);
                tpPrice = ProtectiveRatchet.OpeningTake(
                    TemplateKey(bot),
                    openedSide,
                    fillPrice,
                    slPrice,
                    tpPrice,
                    fillPrice,
                    market?.TickSize ?? 0m);
                if (fillPrice > 0m && tpPrice > 0m)
                {
                    tpPercent = Math.Abs(tpPrice - fillPrice) / fillPrice * 100m;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Could not compute Isolated SL/TP from fill {Fill} R {Stop}%/{Take}% tick {Tick} for {Symbol}",
                    fillPrice,
                    slPercent,
                    tpPercent,
                    market?.TickSize ?? 0m,
                    bot.Symbol);
                slPrice = 0m;
                tpPrice = 0m;
            }
            var opened = new Position
            {
                BotId = bot.Id,
                Symbol = bot.Symbol,
                Side = openedSide,
                Quantity = quantity,
                AverageEntryPrice = fillPrice,
                CurrentPrice = fillPrice,
                UnrealizedPnL = 0m,
                Fees = fee,
                StopLossPercent = slPercent,
                TakeProfitPercent = tpPercent,
                InitialRiskUsdt = plan is { ActualRiskAmount: > 0m }
                    ? plan.ActualRiskAmount
                    : quantity * fillPrice * (slPercent / 100m),
                MarginUsdt = openedMargin,
                AvailableBalanceAtEntry = plan?.AvailableBalance ?? 0m,
                RiskPerTradePercent = plan?.RiskPerTradePercent ?? 0m,
                StopLossPrice = slPrice,
                TakeProfitPrice = tpPrice,
                NotionalUsdt = notional,
                Leverage = leverage,
                EquityAtEntry = usdt.Free + usdt.Locked,
                LiquidationPrice = plan?.LiquidationPrice ?? 0m,
                EstimatedEntryFee = plan?.EstimatedEntryFee ?? fee,
                EstimatedExitFee = plan?.EstimatedExitFee ?? 0m,
                EstimatedSlippage = plan?.EstimatedSlippage ?? 0m,
                EstimatedTotalRisk = plan?.EstimatedTotalRisk ?? 0m,
                OpenedAt = _clock.UtcNow
            };
            opened.Events.Add(new PositionEvent
            {
                EventType = "OPEN",
                Quantity = quantity,
                Price = fillPrice,
                CorrelationId = correlationId
            });
            await _store.AddPositionAsync(opened, cancellationToken);
            await _store.AddTradeAsync(new Trade
            {
                BotId = bot.Id,
                StrategyId = bot.StrategyVersion.StrategyId,
                StrategyVersionId = bot.StrategyVersionId,
                EntryOrderId = order.Id,
                Symbol = bot.Symbol,
                Side = side,
                Quantity = quantity,
                EntryPrice = fillPrice,
                Fees = fee,
                FeeStatus = feeKnown ? FeeKnowledge.Known : FeeKnowledge.Unknown,
                FeeAsset = feeKnown ? confirmed.FeeAsset : null,
                OpenedAt = _clock.UtcNow,
                CorrelationId = correlationId,
                HypothesisId = NearMissHypothesis(bot),
                StrategyFamily = NearMissFamily(bot),
                SignalAt = opened.OpenedAt
            }, cancellationToken);
            await _store.SaveChangesAsync(cancellationToken);

            if (bot.Mode == TradingMode.Live)
            {
                var closeSide = openedSide == PositionSide.Short ? OrderSide.Buy : OrderSide.Sell;
                var stops = await AttachLiveProtectiveStopsAsync(
                    bot,
                    connector,
                    slPrice,
                    tpPrice,
                    openedSide,
                    cancellationToken);
                await PersistProtectiveOrdersAsync(
                    bot,
                    closeSide,
                    slPrice,
                    tpPrice,
                    quantity,
                    correlationId,
                    stops,
                    cancellationToken);
                await _store.SaveChangesAsync(cancellationToken);
                if (!stops.StopPlaced)
                {
                    bot.LastError =
                        $"Live {(openedSide == PositionSide.Short ? "sell" : "buy")} filled at {fillPrice}. STOP trigger {slPrice} failed. Automatic close is disabled; protection will be retried. {stops.StopError}";
                    _logger.LogCritical(
                        "Binance STOP_MARKET failed after live fill for bot {BotId} {Symbol} trigger {Stop}: {Error}. The project will not auto-close the position.",
                        bot.Id,
                        bot.Symbol,
                        slPrice,
                        stops.StopError);
                    return;
                }

                bot.LastError = stops.TakePlaced
                    ? $"Live {(openedSide == PositionSide.Short ? "short" : "buy")} filled at {fillPrice}. Isolated SL {slPrice} / TP {tpPrice} placed."
                    : $"Live {(openedSide == PositionSide.Short ? "short" : "buy")} filled at {fillPrice}. Isolated SL {slPrice} placed. TP {tpPrice} failed: {stops.TakeError}";
            }
            return;
        }

        if (position is not null)
        {
            await CloseFilledPositionAsync(
                bot,
                position,
                order,
                quantity,
                fillPrice,
                notional,
                fee,
                usdt,
                baseAsset,
                correlationId,
                feeKnown,
                confirmed.FeeAsset,
                cancellationToken);
        }

        await _store.SaveChangesAsync(cancellationToken);
    }

    private async Task CloseFilledPositionAsync(
        Bot bot,
        Position position,
        Order order,
        decimal quantity,
        decimal fillPrice,
        decimal notional,
        decimal fee,
        Balance usdt,
        Balance baseAsset,
        string correlationId,
        bool feeKnown,
        string? feeAsset,
        CancellationToken cancellationToken)
    {
        var commission = feeKnown && !string.IsNullOrWhiteSpace(feeAsset)
            ? FeeBook.Known(fee, feeAsset)
            : FeeBook.Unknown();
        if (!feeKnown)
        {
            fee = 0m;
        }

        var booking = PositionFillBook.Exit(
            position.Side,
            position.Quantity,
            position.AverageEntryPrice,
            quantity,
            fillPrice,
            fee);
        var direction = position.Side == PositionSide.Short ? -1m : 1m;
        var pnl = booking.RealizedPnl;
        if (!booking.Closed)
        {
            position.Quantity = booking.RemainingQuantity;
            position.CurrentPrice = fillPrice;
            position.RealizedPnL += pnl;
            if (feeKnown)
            {
                position.Fees += fee;
            }
            position.Events.Add(new PositionEvent
            {
                EventType = booking.EventType,
                Quantity = quantity,
                Price = fillPrice,
                RealizedPnLDelta = pnl,
                CorrelationId = correlationId
            });
            var partialTrade = await _store.GetOpenTradeAsync(bot.Id, cancellationToken);
            if (partialTrade is not null
                && string.Equals(partialTrade.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                partialTrade.PnL += pnl;
                TradeFee.Apply(partialTrade, commission, replace: false);
            }

            return;
        }

        if (bot.Mode != TradingMode.Live)
        {
            if (position.MarginUsdt > 0m)
            {
                var margin = Math.Min(position.MarginUsdt, usdt.Locked);
                usdt.Locked -= margin;
                usdt.Free += margin + pnl;
            }
            else if (position.Side == PositionSide.Long)
            {
                usdt.Free += notional - fee;
                baseAsset.Free = Math.Max(0m, baseAsset.Free - quantity);
            }
            else
            {
                usdt.Free += pnl;
            }
        }

        position.Quantity = 0m;
        position.CurrentPrice = fillPrice;
        position.UnrealizedPnL = 0m;
        position.RealizedPnL += pnl;
        if (feeKnown)
        {
            position.Fees += fee;
        }
        position.ClosedAt = _clock.UtcNow;
        position.Events.Add(new PositionEvent
        {
            EventType = "CLOSE",
            Quantity = quantity,
            Price = fillPrice,
            RealizedPnLDelta = pnl,
            CorrelationId = correlationId
        });

        var pnlPercent = position.AverageEntryPrice == 0m
            ? 0m
            : direction * (fillPrice - position.AverageEntryPrice) / position.AverageEntryPrice * 100m;
        var openTrade = await _store.GetOpenTradeAsync(bot.Id, cancellationToken);
        if (openTrade is not null &&
            !string.Equals(openTrade.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            openTrade = null;
        }

        var sibling = (await _store.FindClosedTradesAroundAsync(
                position.Symbol,
                position.OpenedAt,
                _clock.UtcNow,
                cancellationToken))
            .FirstOrDefault(item =>
                (openTrade is null || item.Id != openTrade.Id)
                && ClosedTripMatch.Same(
                    item.Symbol,
                    item.Quantity,
                    item.OpenedAt,
                    item.ClosedAt,
                    position.Symbol,
                    quantity,
                    position.OpenedAt,
                    _clock.UtcNow));
        if (sibling is not null)
        {
            if (openTrade is not null)
            {
                _store.RemoveTrade(openTrade);
            }

            return;
        }

        if (openTrade is not null)
        {
            openTrade.ExitOrderId = order.Id;
            openTrade.ExitPrice = fillPrice;
            openTrade.PnL += pnl;
            openTrade.PnLPercent = pnlPercent;
            TradeFee.Apply(openTrade, commission, replace: false);
            openTrade.ClosedAt = _clock.UtcNow;
            openTrade.MaxFavorableExcursion = position.MaxFavorableExcursion;
            openTrade.MaxAdverseExcursion = position.MaxAdverseExcursion;
            return;
        }

        await _store.AddTradeAsync(new Trade
        {
            BotId = bot.Id,
            StrategyId = bot.StrategyVersion.StrategyId,
            StrategyVersionId = bot.StrategyVersionId,
            ExitOrderId = order.Id,
            Symbol = position.Symbol,
            Side = position.Side == PositionSide.Short ? OrderSide.Sell : OrderSide.Buy,
            Quantity = quantity,
            EntryPrice = position.AverageEntryPrice,
            ExitPrice = fillPrice,
            PnL = pnl,
            PnLPercent = pnlPercent,
            Fees = commission.Status == FeeKnowledge.Known ? commission.Amount ?? 0m : 0m,
            FeeStatus = commission.Status,
            FeeAsset = commission.Status == FeeKnowledge.Known ? commission.Asset : null,
            OpenedAt = position.OpenedAt,
            ClosedAt = _clock.UtcNow,
            CorrelationId = correlationId,
            HypothesisId = NearMissHypothesis(bot),
            StrategyFamily = NearMissFamily(bot),
            SignalAt = position.OpenedAt,
            MaxFavorableExcursion = position.MaxFavorableExcursion,
            MaxAdverseExcursion = position.MaxAdverseExcursion
        }, cancellationToken);
    }

    private async Task<Dictionary<string, CausalIndicatorCache>> LoadNearMissBooksAsync(
        Bot bot,
        Dictionary<string, IReadOnlyList<MarketCandle>> cycleKlines,
        CancellationToken cancellationToken)
    {
        var books = new Dictionary<string, CausalIndicatorCache>(StringComparer.OrdinalIgnoreCase);
        foreach (var timeframe in new[]
        {
            Timeframe.OneMinute,
            Timeframe.ThreeMinutes,
            Timeframe.FiveMinutes,
            Timeframe.FifteenMinutes,
            Timeframe.OneHour
        })
        {
            var rows = await GetCycleKlinesAsync(bot.Symbol, timeframe, cycleKlines, cancellationToken, limit: 500);
            books[timeframe.ToBinanceInterval()] = new CausalIndicatorCache(rows);
        }

        return books;
    }

    private static string? NearMissHypothesis(Bot bot) => BlankToNull(StrategyTemplateKeys.NearMissHypothesisId(TemplateKey(bot)));

    private static string? NearMissFamily(Bot bot) => BlankToNull(StrategyTemplateKeys.NearMissFamily(TemplateKey(bot)));

    private static string TemplateKey(Bot bot)
    {
        var key = bot.StrategyVersion?.Strategy?.TemplateKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        return bot.StrategyVersion?.DefinitionJson is { Length: > 0 } json
            ? StrategyTemplates.Read(json).TemplateKey
            : "";
    }

    private static string? BlankToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string NearMissMetadata(string template, DateTimeOffset signalTime)
    {
        var hypothesis = StrategyTemplateKeys.NearMissHypothesisId(template);
        var family = StrategyTemplateKeys.NearMissFamily(template);
        return $"{{\"status\":\"NEAR_MISS\",\"hypothesisId\":\"{hypothesis}\",\"strategyFamily\":\"{family}\",\"signalTime\":\"{signalTime:O}\"}}";
    }

    private readonly record struct OverlayProtectResult(Position? Position, bool Handled);

    private static bool HasWorkingProtection(IReadOnlyList<LiveOpenOrder> orders, string symbol, bool stop) =>
        orders.Any(row =>
            string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
            && (stop ? LiveProtectivePrices.IsStopOrder(row.Type) : LiveProtectivePrices.IsTakeOrder(row.Type)));

    private async Task ProtectOpenPositionsAsync(
        IReadOnlyList<Bot> running,
        IReadOnlyDictionary<string, RankedUsdtSpotSymbol> cycleFilters,
        CancellationToken cancellationToken)
    {
        var live = _live.Current;
        if (!IsolatedOccupancy.HasFreshFuturesBook(live))
        {
            return;
        }

        var book = (await _store.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken)).ToList();
        foreach (var remote in live.OpenPositions.Where(row => row.Quantity > 0m))
        {
            if (HasWorkingProtection(live.OpenOrders, remote.Symbol, stop: true)
                && HasWorkingProtection(live.OpenOrders, remote.Symbol, stop: false))
            {
                continue;
            }

            var owner = IsolatedOccupancy.PickLiveOwner(remote.Symbol, running, book, TradingMode.Live);
            if (owner is null)
            {
                continue;
            }

            var position = book.FirstOrDefault(row =>
                row.BotId == owner.Id
                && row.Quantity > 0m
                && string.Equals(row.Symbol, remote.Symbol, StringComparison.OrdinalIgnoreCase));
            var market = await ResolveSymbolFiltersAsync(remote.Symbol, cycleFilters, cancellationToken);
            var price = remote.MarkPrice > 0m ? remote.MarkPrice : position?.CurrentPrice ?? 0m;
            try
            {
                await EnsureLiveOverlayProtectionAsync(
                    owner,
                    running,
                    book,
                    position,
                    price,
                    market,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                owner.LastError = ex.Message;
                _logger.LogError(ex, "Could not place protection for {Symbol}", remote.Symbol);
            }
        }
    }

    private async Task<OverlayProtectResult> EnsureLiveOverlayProtectionAsync(
        Bot bot,
        IReadOnlyList<Bot> running,
        IReadOnlyList<Position> book,
        Position? position,
        decimal lastPrice,
        Symbol? market,
        CancellationToken cancellationToken)
    {
        if (bot.Mode != TradingMode.Live || !IsolatedOccupancy.IsOwner(bot, running, book))
        {
            return new OverlayProtectResult(position, false);
        }

        var live = _live.Current;
        if (!IsolatedOccupancy.HasFreshFuturesBook(live))
        {
            return new OverlayProtectResult(position, false);
        }

        var overlay = live.OpenPositions.FirstOrDefault(row =>
            row.Quantity > 0m
            && string.Equals(row.Symbol, bot.Symbol, StringComparison.OrdinalIgnoreCase));
        if (overlay is null)
        {
            return new OverlayProtectResult(position, false);
        }

        var hasStop = HasWorkingProtection(live.OpenOrders, bot.Symbol, stop: true);
        var hasTake = HasWorkingProtection(live.OpenOrders, bot.Symbol, stop: false);
        if (position is not null && hasStop && hasTake)
        {
            return new OverlayProtectResult(position, false);
        }

        var overlaySide = overlay.Side is "Short" or "Sell" ? PositionSide.Short : PositionSide.Long;
        if (position is null)
        {
            position = new Position
            {
                Bot = bot,
                BotId = bot.Id,
                Symbol = bot.Symbol,
                Side = overlaySide,
                Quantity = overlay.Quantity,
                AverageEntryPrice = overlay.EntryPrice,
                CurrentPrice = overlay.MarkPrice > 0m ? overlay.MarkPrice : lastPrice,
                UnrealizedPnL = overlay.UnrealizedPnL,
                RealizedPnL = 0m,
                OpenedAt = _clock.UtcNow
            };
            await _store.AddPositionAsync(position, cancellationToken);
            _logger.LogWarning(
                "Recorded exchange position {Symbol} for bot {BotId} before placing protection. No fill was written.",
                bot.Symbol,
                bot.Id);
        }

        var profile = bot.RiskProfile ?? await _store.GetConservativeRiskAsync(cancellationToken);
        decimal slPrice;
        decimal tpPrice;
        try
        {
            (slPrice, tpPrice) = LiveProtectivePrices.FromEntry(
                position.AverageEntryPrice > 0m ? position.AverageEntryPrice : overlay.EntryPrice,
                profile.StopLossPercent,
                profile.TakeProfitPercent,
                market?.TickSize ?? 0m,
                overlaySide);
            var tick = market?.TickSize ?? 0m;
            var entry = position.AverageEntryPrice > 0m ? position.AverageEntryPrice : overlay.EntryPrice;
            var keptStop = ProtectiveRatchet.KeepTighterStop(overlaySide, lastPrice, slPrice, position.StopLossPrice, tick);
            if (keptStop > 0m)
            {
                slPrice = keptStop;
            }

            tpPrice = ProtectiveRatchet.OpeningTake(TemplateKey(bot), overlaySide, entry, slPrice, tpPrice, lastPrice, tick);
            if (!ProtectiveRatchet.RestoresBookTake(TemplateKey(bot)))
            {
                var keptTake = ProtectiveRatchet.KeepFurtherTake(overlaySide, lastPrice, tpPrice, position.TakeProfitPrice, tick);
                if (keptTake > 0m)
                {
                    tpPrice = keptTake;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not recompute Isolated SL/TP for overlay {Symbol}", bot.Symbol);
            return new OverlayProtectResult(position, false);
        }

        var entryPrice = position.AverageEntryPrice > 0m ? position.AverageEntryPrice : overlay.EntryPrice;
        position.StopLossPrice = slPrice;
        position.TakeProfitPrice = tpPrice;
        if (entryPrice > 0m)
        {
            position.StopLossPercent = Math.Abs(slPrice - entryPrice) / entryPrice * 100m;
            position.TakeProfitPercent = Math.Abs(tpPrice - entryPrice) / entryPrice * 100m;
        }
        else
        {
            position.StopLossPercent = profile.StopLossPercent;
            position.TakeProfitPercent = profile.TakeProfitPercent;
        }
        var connector = _connectors.Create(bot.Mode, bot.ExchangeAccountId);
        var correlationId = _correlation.GetOrCreate();
        var stops = await AttachLiveProtectiveStopsAsync(
            bot,
            connector,
            slPrice,
            tpPrice,
            overlaySide,
            cancellationToken,
            replaceStop: !hasStop,
            replaceTake: !hasTake);
        await PersistProtectiveOrdersAsync(
            bot,
            overlaySide == PositionSide.Short ? OrderSide.Buy : OrderSide.Sell,
            slPrice,
            tpPrice,
            position.Quantity,
            correlationId,
            stops,
            cancellationToken);
        await _store.SaveChangesAsync(cancellationToken);
        if (stops.StopPlaced && stops.TakePlaced)
        {
            bot.LastError = hasStop || hasTake
                ? $"Live Isolated {bot.Symbol} missing protection placed. SL {slPrice} / TP {tpPrice}."
                : $"Live Isolated {bot.Symbol} had no working SL/TP. Placed SL {slPrice} / TP {tpPrice}.";
            return new OverlayProtectResult(position, false);
        }

        bot.LastError =
            $"Live Isolated {bot.Symbol} protection is incomplete. SL {(stops.StopPlaced ? "working" : stops.StopError)} / TP {(stops.TakePlaced ? "working" : stops.TakeError)}. The missing order will be retried. The strategy can still close the position.";
        _logger.LogCritical(
            "Live Isolated {Symbol} has no working STOP for bot {BotId}: {Error}. Strategy exit still runs; the missing order will be retried while the position stays open.",
            bot.Symbol,
            bot.Id,
            stops.StopError);
        return new OverlayProtectResult(position, false);
    }

    private async Task<ProtectiveStopsResult> AttachLiveProtectiveStopsAsync(
        Bot bot,
        IExchangeConnector connector,
        decimal stop,
        decimal take,
        PositionSide side,
        CancellationToken cancellationToken,
        bool replaceStop = true,
        bool replaceTake = true,
        bool acceptExisting = true)
    {
        if (!replaceStop && !replaceTake)
        {
            return new ProtectiveStopsResult(true, true);
        }

        if (replaceStop)
        {
            await connector.CancelOrderAsync(bot.Symbol, LiveProtectivePrices.StopClientOrderId(bot.Id), null, cancellationToken);
            await MarkProtectiveCancelledAsync(LiveProtectivePrices.StopClientOrderId(bot.Id), cancellationToken);
        }

        if (replaceTake)
        {
            await connector.CancelOrderAsync(bot.Symbol, LiveProtectivePrices.TakeClientOrderId(bot.Id), null, cancellationToken);
            await MarkProtectiveCancelledAsync(LiveProtectivePrices.TakeClientOrderId(bot.Id), cancellationToken);
        }

        var placed = await connector.PlaceClosePositionStopsAsync(
            bot.Symbol,
            side == PositionSide.Short ? OrderSide.Buy : OrderSide.Sell,
            stop,
            take,
            LiveProtectivePrices.StopClientOrderId(bot.Id),
            LiveProtectivePrices.TakeClientOrderId(bot.Id),
            cancellationToken,
            placeStop: replaceStop && stop > 0m,
            placeTake: replaceTake && take > 0m,
            acceptExisting: acceptExisting);
        var stopPlaced = !replaceStop || (stop > 0m && placed.StopPlaced);
        var takePlaced = !replaceTake || (take > 0m && placed.TakePlaced);
        return new ProtectiveStopsResult(
            stopPlaced,
            takePlaced,
            stopPlaced ? null : placed.StopError ?? "Stop trigger is invalid.",
            takePlaced ? null : placed.TakeError ?? "Take-profit trigger is invalid.");
    }

    private async Task CancelLiveProtectiveOrdersAsync(Bot bot, CancellationToken cancellationToken)
    {
        var connector = _connectors.Create(bot.Mode, bot.ExchangeAccountId);
        await connector.CancelOrderAsync(bot.Symbol, LiveProtectivePrices.StopClientOrderId(bot.Id), null, cancellationToken);
        await connector.CancelOrderAsync(bot.Symbol, LiveProtectivePrices.TakeClientOrderId(bot.Id), null, cancellationToken);
        await MarkProtectiveCancelledAsync(LiveProtectivePrices.StopClientOrderId(bot.Id), cancellationToken);
        await MarkProtectiveCancelledAsync(LiveProtectivePrices.TakeClientOrderId(bot.Id), cancellationToken);
    }

    private async Task PersistProtectiveOrdersAsync(
        Bot bot,
        OrderSide closeSide,
        decimal stop,
        decimal take,
        decimal quantity,
        string correlationId,
        ProtectiveStopsResult stops,
        CancellationToken cancellationToken)
    {
        await UpsertProtectiveOrderAsync(
            bot,
            closeSide,
            OrderType.StopMarket,
            stop,
            quantity,
            LiveProtectivePrices.StopClientOrderId(bot.Id),
            correlationId,
            stops.StopPlaced,
            stops.StopError,
            cancellationToken);
        await UpsertProtectiveOrderAsync(
            bot,
            closeSide,
            OrderType.TakeProfitMarket,
            take,
            quantity,
            LiveProtectivePrices.TakeClientOrderId(bot.Id),
            correlationId,
            stops.TakePlaced,
            stops.TakeError,
            cancellationToken);
    }

    private async Task UpsertProtectiveOrderAsync(
        Bot bot,
        OrderSide closeSide,
        OrderType type,
        decimal triggerPrice,
        decimal quantity,
        string clientOrderId,
        string correlationId,
        bool placed,
        string? error,
        CancellationToken cancellationToken)
    {
        var existing = await _store.GetOrderByClientOrderIdAsync(clientOrderId, cancellationToken);
        var status = placed ? OrderStatus.Submitted : OrderStatus.Rejected;
        if (existing is not null)
        {
            existing.Side = closeSide;
            existing.Type = type;
            existing.Symbol = bot.Symbol;
            existing.Price = triggerPrice > 0m ? triggerPrice : existing.Price;
            existing.Quantity = quantity;
            existing.RemainingQuantity = placed ? quantity : 0m;
            existing.Status = status;
            existing.RejectReason = placed ? null : error;
            existing.SubmittedAt = _clock.UtcNow;
            existing.CorrelationId = correlationId;
            return;
        }

        await _store.AddOrderAsync(new Order
        {
            BotId = bot.Id,
            ExchangeAccountId = bot.ExchangeAccountId,
            StrategyId = bot.StrategyVersion.StrategyId,
            StrategyVersionId = bot.StrategyVersionId,
            Symbol = bot.Symbol,
            Side = closeSide,
            Type = type,
            Status = status,
            Price = triggerPrice > 0m ? triggerPrice : null,
            Quantity = quantity,
            RemainingQuantity = placed ? quantity : 0m,
            ClientOrderId = clientOrderId,
            IdempotencyKey = clientOrderId,
            Mode = bot.Mode,
            CorrelationId = correlationId,
            SubmittedAt = _clock.UtcNow,
            RejectReason = placed ? null : error
        }, cancellationToken);
    }

    private async Task MarkProtectiveCancelledAsync(string clientOrderId, CancellationToken cancellationToken)
    {
        var existing = await _store.GetOrderByClientOrderIdAsync(clientOrderId, cancellationToken);
        if (existing is null || existing.Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Failed)
        {
            return;
        }

        existing.Status = OrderStatus.Cancelled;
        existing.RemainingQuantity = 0m;
    }

    private static bool StrategySlotsFull(
        Bot bot,
        IReadOnlyList<Bot> running,
        IReadOnlyList<Position> book,
        IReadOnlyList<LiveOpenPosition>? liveBook,
        bool liveAuth,
        DateTimeOffset now)
    {
        var strategyId = bot.StrategyVersion?.StrategyId ?? Guid.Empty;
        if (strategyId == Guid.Empty)
        {
            return false;
        }

        var cap = bot.RiskProfile?.MaxSimultaneousPositions ?? 0;
        if (cap <= 0)
        {
            cap = 2;
        }

        var peers = running
            .Where(peer => peer.StrategyVersion?.StrategyId == strategyId)
            .Select(peer => peer.Id)
            .ToHashSet();
        var versions = running
            .Where(peer => peer.StrategyVersion?.StrategyId == strategyId)
            .Select(peer => peer.StrategyVersionId)
            .ToHashSet();
        var open = IsolatedOccupancy.UniqueCoinsForStrategy(book, strategyId, liveBook, liveAuth, now, peers, versions);
        return open >= cap;
    }

    private static RiskProfile FlatRangeRisk(RiskProfile source, decimal stopPercent, decimal takePercent) =>
        new()
        {
            Name = source.Name,
            RiskPerTradePercent = source.RiskPerTradePercent,
            StopLossPercent = stopPercent,
            TakeProfitPercent = takePercent,
            MaxLeverage = source.MaxLeverage,
            MaxDailyLossPercent = source.MaxDailyLossPercent,
            MaxPortfolioRiskPercent = source.MaxPortfolioRiskPercent,
            MaxSimultaneousPositions = source.MaxSimultaneousPositions,
            MaxConsecutiveLosses = source.MaxConsecutiveLosses,
            CooldownMinutes = source.CooldownMinutes,
            MinimumLiquidationSafetyBufferPercent = source.MinimumLiquidationSafetyBufferPercent,
            AllowLive = source.AllowLive
        };

    private static decimal? PositivePrice(decimal? value) => value is > 0m ? value : null;

    private static decimal RelativeDrift(decimal left, decimal right)
    {
        var basis = Math.Max(Math.Abs(left), Math.Abs(right));
        return basis <= 0m ? 0m : Math.Abs(left - right) / basis;
    }

    private async Task RatchetOpenProtectionAsync(
        Bot bot,
        IReadOnlyList<Bot> running,
        IReadOnlyList<Position> book,
        Position position,
        decimal mark,
        Symbol? market,
        CancellationToken cancellationToken)
    {
        if (position.Quantity <= 0m || position.AverageEntryPrice <= 0m || mark <= 0m)
        {
            return;
        }

        if (bot.Mode == TradingMode.Live)
        {
            if (!IsolatedOccupancy.HasFreshFuturesBook(_live.Current)
                || !IsolatedOccupancy.IsOwner(bot, running, book))
            {
                return;
            }
        }

        var tick = market?.TickSize ?? 0m;
        var stop = position.StopLossPrice;
        var take = position.TakeProfitPrice;
        if (bot.Mode == TradingMode.Live)
        {
            if (!TryWorkingTrigger(bot.Symbol, LiveProtectivePrices.StopClientOrderId(bot.Id), stop: true, position.Side, mark, tick, out var liveStop))
            {
                return;
            }

            var keptStop = ProtectiveRatchet.KeepTighterStop(position.Side, mark, stop, liveStop, tick);
            if (keptStop <= 0m)
            {
                return;
            }

            stop = keptStop;
            if (TryWorkingTrigger(bot.Symbol, LiveProtectivePrices.TakeClientOrderId(bot.Id), stop: false, position.Side, mark, tick, out var liveTake)
                && !ProtectiveRatchet.RestoresBookTake(TemplateKey(bot)))
            {
                var keptTake = ProtectiveRatchet.KeepFurtherTake(position.Side, mark, take, liveTake, tick);
                if (keptTake > 0m)
                {
                    take = keptTake;
                }
            }
        }

        var restoreTake = false;
        if (bot.RiskProfile is { StopLossPercent: > 0m, TakeProfitPercent: > 0m } profile
            && ProtectiveRatchet.RestoresBookTake(TemplateKey(bot)))
        {
            try
            {
                var (_, bookTake) = LiveProtectivePrices.FromEntry(
                    position.AverageEntryPrice,
                    profile.StopLossPercent,
                    profile.TakeProfitPercent,
                    tick,
                    position.Side);
                if (ProtectiveRatchet.TryRestoreBookTake(
                        TemplateKey(bot),
                        position.Side,
                        mark,
                        bookTake,
                        take,
                        tick,
                        out var restored,
                        out var reached))
                {
                    if (reached)
                    {
                        position.TakeProfitPrice = restored;
                        position.TakeProfitPercent = profile.TakeProfitPercent;
                        return;
                    }

                    take = restored;
                    restoreTake = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Book take restore skipped for {Symbol}", position.Symbol);
            }
        }

        var decision = ProtectiveRatchet.TryAdvance(
            TemplateKey(bot),
            position.Side,
            position.AverageEntryPrice,
            stop,
            take,
            mark,
            tick);
        if (restoreTake)
        {
            decision = decision is { } moved
                ? moved with { TakeProfit = take, TakeMoved = true }
                : new ProtectiveRatchetDecision(stop, take, StopMoved: false, TakeMoved: true);
        }
        else if (decision is null)
        {
            return;
        }

        if (_ratchetBackoff.TryGetValue(bot.Id, out var held) && _clock.UtcNow - held.At < TimeSpan.FromMinutes(10))
        {
            if (decision.Value.StopMoved && decision.Value.StopLoss == held.Stop)
            {
                decision = decision.Value with { StopMoved = false, StopLoss = stop };
            }

            if (decision.Value.TakeMoved && decision.Value.TakeProfit == held.Take)
            {
                decision = decision.Value with { TakeMoved = false, TakeProfit = take };
            }
        }

        if (decision is not { } advanced || (!advanced.StopMoved && !advanced.TakeMoved))
        {
            return;
        }
        if (bot.Mode != TradingMode.Live)
        {
            RememberProtection(
                position,
                position.AverageEntryPrice,
                advanced.StopMoved ? advanced.StopLoss : null,
                advanced.TakeMoved ? advanced.TakeProfit : null);
            return;
        }

        var connector = _connectors.Create(bot.Mode, bot.ExchangeAccountId);
        var stopOk = !advanced.StopMoved;
        var takeOk = !advanced.TakeMoved;
        if (advanced.TakeMoved)
        {
            var placed = await AttachLiveProtectiveStopsAsync(
                bot,
                connector,
                stop,
                advanced.TakeProfit,
                position.Side,
                cancellationToken,
                replaceStop: false,
                replaceTake: true,
                acceptExisting: false);
            takeOk = placed.TakePlaced;
            if (!takeOk)
            {
                await AttachLiveProtectiveStopsAsync(
                    bot,
                    connector,
                    stop,
                    take,
                    position.Side,
                    cancellationToken,
                    replaceStop: false,
                    replaceTake: true);
                _logger.LogWarning(
                    "Take-profit ratchet failed for {Symbol} bot {BotId}. Previous take {Take} was restored. {Error}",
                    bot.Symbol,
                    bot.Id,
                    take,
                    placed.TakeError);
            }
        }

        if (advanced.StopMoved)
        {
            var placed = await AttachLiveProtectiveStopsAsync(
                bot,
                connector,
                advanced.StopLoss,
                advanced.TakeMoved && takeOk ? advanced.TakeProfit : take,
                position.Side,
                cancellationToken,
                replaceStop: true,
                replaceTake: false,
                acceptExisting: false);
            stopOk = placed.StopPlaced;
            if (!stopOk)
            {
                var restored = await AttachLiveProtectiveStopsAsync(
                    bot,
                    connector,
                    stop,
                    advanced.TakeMoved && takeOk ? advanced.TakeProfit : take,
                    position.Side,
                    cancellationToken,
                    replaceStop: true,
                    replaceTake: false);
                _logger.LogCritical(
                    restored.StopPlaced
                        ? "Stop ratchet failed for {Symbol} bot {BotId}. Previous stop {Stop} was restored. {Error}"
                        : "Stop ratchet failed for {Symbol} bot {BotId} and the previous stop {Stop} could not be restored. {Error}",
                    bot.Symbol,
                    bot.Id,
                    stop,
                    placed.StopError);
            }
        }

        if (!stopOk || !takeOk)
        {
            _ratchetBackoff[bot.Id] = (
                advanced.StopMoved && !stopOk ? advanced.StopLoss : 0m,
                advanced.TakeMoved && !takeOk ? advanced.TakeProfit : 0m,
                _clock.UtcNow);
        }

        RememberProtection(
            position,
            position.AverageEntryPrice,
            stopOk && advanced.StopMoved ? advanced.StopLoss : null,
            takeOk && advanced.TakeMoved ? advanced.TakeProfit : null);
    }

    private bool TryWorkingTrigger(
        string symbol,
        string clientOrderId,
        bool stop,
        PositionSide side,
        decimal mark,
        decimal tick,
        out decimal price)
    {
        price = 0m;
        var found = false;
        foreach (var row in _live.Current.OpenOrders)
        {
            if (!string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.ClientOrderId, clientOrderId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = stop
                ? LiveProtectivePrices.IsStopOrder(row.Type)
                : LiveProtectivePrices.IsTakeOrder(row.Type);
            if (!match)
            {
                continue;
            }

            if (row.Price is not > 0m)
            {
                price = 0m;
                return false;
            }

            found = true;
            price = price == 0m
                ? row.Price.Value
                : stop
                    ? ProtectiveRatchet.KeepTighterStop(side, mark, price, row.Price.Value, tick)
                    : ProtectiveRatchet.KeepFurtherTake(side, mark, price, row.Price.Value, tick);
            if (price <= 0m)
            {
                return false;
            }
        }

        return found;
    }

    private static void RememberProtection(Position position, decimal entry, decimal? stop, decimal? take)
    {
        if (entry <= 0m)
        {
            return;
        }

        if (stop is > 0m)
        {
            position.StopLossPrice = stop.Value;
            position.StopLossPercent = Math.Abs(stop.Value - entry) / entry * 100m;
        }

        if (take is > 0m)
        {
            position.TakeProfitPrice = take.Value;
            position.TakeProfitPercent = Math.Abs(take.Value - entry) / entry * 100m;
        }
    }

    private async Task HandOffStoppedSnapshotsAsync(CancellationToken cancellationToken)
    {
        var book = await _store.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken);
        var known = await _store.ListBotsAsync(cancellationToken);
        var now = _clock.UtcNow;
        foreach (var group in book.Where(row => row.Quantity > 0m).GroupBy(row => IsolatedOccupancy.CoinKey(row.Symbol)))
        {
            var rows = group.ToList();
            var owner = IsolatedOccupancy.IsolatedOwner(rows);
            var droppedOrderIds = new List<string>();
            foreach (var extra in rows.Where(row => row.Id != owner.Id))
            {
                extra.Quantity = 0m;
                extra.UnrealizedPnL = 0m;
                extra.ClosedAt = now;
                if (extra.BotId == owner.BotId)
                {
                    continue;
                }

                var extraBot = extra.Bot ?? await _store.GetBotAsync(extra.BotId, cancellationToken);
                if (extraBot is null || extraBot.Mode != TradingMode.Live)
                {
                    continue;
                }

                try
                {
                    await CancelLiveProtectiveOrdersAsync(extraBot, cancellationToken);
                    droppedOrderIds.Add(LiveProtectivePrices.StopClientOrderId(extraBot.Id));
                    droppedOrderIds.Add(LiveProtectivePrices.TakeClientOrderId(extraBot.Id));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not cancel leftover protection for {Symbol} bot {BotId}", extra.Symbol, extra.BotId);
                }
            }

            if (droppedOrderIds.Count > 0)
            {
                var live = _live.Current;
                live.OpenOrders = live.OpenOrders
                    .Where(order => order.ClientOrderId is null || !droppedOrderIds.Contains(order.ClientOrderId))
                    .ToList();
            }

            var ownerBot = owner.Bot ?? known.FirstOrDefault(bot => bot.Id == owner.BotId)
                ?? await _store.GetBotAsync(owner.BotId, cancellationToken);
            if (ownerBot?.StrategyVersion is null || IsolatedOccupancy.BotExistedAtOpen(ownerBot.CreatedAt, owner.OpenedAt))
            {
                continue;
            }

            var prior = IsolatedOccupancy.PriorOwner(known, owner.Symbol, owner.OpenedAt, ownerBot.Id);
            if (prior?.StrategyVersion is null || prior.Mode != TradingMode.Live)
            {
                continue;
            }

            var openTrade = await _store.GetOpenTradeAsync(owner.BotId, cancellationToken);
            if (openTrade is not null
                && string.Equals(openTrade.Symbol, owner.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                openTrade.BotId = prior.Id;
                openTrade.StrategyId = prior.StrategyVersion.StrategyId;
                openTrade.StrategyVersionId = prior.StrategyVersionId;
            }

            _logger.LogInformation(
                "Isolated {Symbol} opened {OpenedAt:o} stays with {Strategy}, not {Adopter} which was created later.",
                owner.Symbol,
                owner.OpenedAt,
                prior.StrategyVersion.Strategy?.Name ?? prior.Name,
                ownerBot.StrategyVersion.Strategy?.Name ?? ownerBot.Name);
            owner.BotId = prior.Id;
            owner.Bot = prior;
        }
    }

    private async Task WatchUnattendedLivePositionsAsync(IReadOnlyList<Bot> running, CancellationToken cancellationToken)
    {
        var book = await _store.GetOpenPositionsForModeAsync(TradingMode.Live, cancellationToken);
        var runningIds = running.Select(bot => bot.Id).ToHashSet();
        foreach (var position in book.Where(row => row.Quantity > 0m && !runningIds.Contains(row.BotId)))
        {
            var bot = position.Bot ?? await _store.GetBotAsync(position.BotId, cancellationToken);
            if (bot is null)
            {
                continue;
            }

            var mark = position.CurrentPrice;
            try
            {
                var price = await _market.GetLastPriceAsync(position.Symbol, cancellationToken);
                if (price > 0m)
                {
                    mark = price;
                    position.CurrentPrice = price;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Mark skipped for unattended {Symbol}", position.Symbol);
            }

            var market = await ResolveSymbolFiltersAsync(position.Symbol, null, cancellationToken);
            ClampStopToProfile(position, bot, mark, market);
            if (HitsProtectiveExit(position, mark, out var reason))
            {
                bot.LastError = $"Local {reason} was reached on unattended {position.Symbol}. That is not an exchange fill. The position stays open.";
                _reconciliation.Fail(bot.LastError, _clock.UtcNow);
                continue;
            }

            if (position.StopLossPrice <= 0m || !IsolatedOccupancy.HasFreshFuturesBook(_live.Current))
            {
                continue;
            }

            if (HasWorkingProtection(_live.Current.OpenOrders, position.Symbol, stop: true))
            {
                continue;
            }

            try
            {
                var connector = _connectors.Create(TradingMode.Live, bot.ExchangeAccountId);
                await AttachLiveProtectiveStopsAsync(
                    bot,
                    connector,
                    position.StopLossPrice,
                    position.TakeProfitPrice,
                    position.Side,
                    cancellationToken,
                    replaceStop: true,
                    replaceTake: position.TakeProfitPrice > 0m);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not restore protection for unattended {Symbol}", position.Symbol);
            }
        }
    }

    private bool ClampStopToProfile(Position? position, Bot bot, decimal mark, Symbol? market)
    {
        if (position is null
            || position.AverageEntryPrice <= 0m
            || bot.RiskProfile is not { StopLossPercent: > 0m } profile)
        {
            return false;
        }

        if (position.StopLossPrice > 0m
            && position.StopLossPercent > 0m
            && position.StopLossPercent <= profile.StopLossPercent + 0.05m)
        {
            return false;
        }

        var takePercent = position.TakeProfitPercent > 0m ? position.TakeProfitPercent : profile.TakeProfitPercent;
        if (takePercent <= 0m)
        {
            return false;
        }

        try
        {
            var (stop, _) = LiveProtectivePrices.FromEntry(
                position.AverageEntryPrice,
                profile.StopLossPercent,
                takePercent,
                market?.TickSize ?? 0m,
                position.Side);
            var kept = ProtectiveRatchet.KeepTighterStop(
                position.Side,
                mark > 0m ? mark : position.CurrentPrice,
                stop,
                position.StopLossPrice,
                market?.TickSize ?? 0m);
            // A profile stop that is already through the mark must not be written.
            // Writing it makes the next check market-flatten a position the old stop still covers.
            if (kept <= 0m || kept == position.StopLossPrice)
            {
                return false;
            }

            position.StopLossPrice = kept;
            position.StopLossPercent = Math.Abs(kept - position.AverageEntryPrice) / position.AverageEntryPrice * 100m;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stop clamp skipped for {Symbol}", position.Symbol);
            return false;
        }
    }

    private static bool HitsProtectiveExit(Position position, decimal lastPrice, out string reason)
    {
        reason = string.Empty;
        if (lastPrice <= 0m)
        {
            return false;
        }

        if (position.StopLossPrice > 0m)
        {
            if (position.Side == PositionSide.Long && lastPrice <= position.StopLossPrice)
            {
                reason = "stop loss";
                return true;
            }

            if (position.Side == PositionSide.Short && lastPrice >= position.StopLossPrice)
            {
                reason = "stop loss";
                return true;
            }
        }

        if (position.TakeProfitPrice > 0m)
        {
            if (position.Side == PositionSide.Long && lastPrice >= position.TakeProfitPrice)
            {
                reason = "take profit";
                return true;
            }

            if (position.Side == PositionSide.Short && lastPrice <= position.TakeProfitPrice)
            {
                reason = "take profit";
                return true;
            }
        }

        return false;
    }

    private static void Record(Order order, OrderStatus to, string source)
    {
        var from = order.Status;
        order.Status = PaperOrderStateMachine.Transition(from, to);
        order.Events.Add(new OrderEvent
        {
            FromStatus = from,
            ToStatus = order.Status,
            Source = source,
            CorrelationId = order.CorrelationId
        });
    }
}
