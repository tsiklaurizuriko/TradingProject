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
using PaperFillModel = TradingPlatform.Execution.PaperFillModel;
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
    private readonly ILogger<BotEngine> _logger;
    private readonly IExchangeAccountService? _accounts;

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
        IExchangeAccountService? accounts = null)
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
        _logger = logger;
        _accounts = accounts;
    }

    public async Task EvaluateRunningBotsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_options.KillSwitchEnabled)
            {
                await _store.StopAllRunningBotsAsync("Kill switch is active.", cancellationToken);
                await _store.SaveChangesAsync(cancellationToken);
                return;
            }

            await RefreshLiveIsolatedBookAsync(cancellationToken);
            await _reconcile.ReconcileAsync(cancellationToken);
            var bots = await _store.GetRunningBotsAsync(cancellationToken);
            var cycleKlines = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase);
            var cyclePrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var cycleFilters = await LoadCycleFiltersAsync(cancellationToken);
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bot in bots)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            bot.Mode == TradingMode.Live ? TradingMode.Live : TradingMode.Paper,
            bot.Mode == TradingMode.Live ? 0m : _options.PaperDefaultBalance,
            cancellationToken);
        var baseAsset = await _store.GetOrCreateBalanceAsync(
            bot.ExchangeAccountId,
            null,
            market?.BaseAsset ?? position.Symbol.Replace("USDT", "", StringComparison.OrdinalIgnoreCase),
            bot.Mode == TradingMode.Live ? TradingMode.Live : TradingMode.Paper,
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

        bot.LastError = bot.Mode == TradingMode.Live
            ? "Manual close submitted to Binance."
            : "Paper position closed manually.";

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

        if (bot.Mode is not TradingMode.Paper and not TradingMode.Live)
        {
            throw new DomainException(ErrorCodes.LiveTradingDisabled, "This bot mode cannot run.");
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

        if (bot.Mode != TradingMode.Live &&
            position is not null &&
            !StrategyTemplateKeys.IsImported(TemplateKey(bot)) &&
            HitsProtectiveExit(position, lastPrice, out var protectiveReason))
        {
            var usdtProtect = await _store.GetOrCreateBalanceAsync(
                bot.ExchangeAccountId,
                null,
                "USDT",
                TradingMode.Paper,
                _options.PaperDefaultBalance,
                cancellationToken);
            var baseProtect = await _store.GetOrCreateBalanceAsync(
                bot.ExchangeAccountId,
                null,
                symbol?.BaseAsset ?? "BTC",
                TradingMode.Paper,
                0m,
                cancellationToken);
            var stamp = now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var flattenId = $"px{bot.Id:N}"[..12] + (stamp.Length <= 10 ? stamp : stamp[^10..]);
            var closeSide = position.Side == PositionSide.Short ? OrderSide.Buy : OrderSide.Sell;
            await PlaceAndFillAsync(
                bot,
                closeSide,
                position.Quantity,
                flattenId,
                _correlation.GetOrCreate(),
                usdtProtect,
                baseProtect,
                position,
                lastPrice,
                0m,
                cancellationToken,
                flatten: true);
            bot.LastError = $"Paper {protectiveReason} filled.";
            return;
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
            if (string.Equals(TemplateKey(bot), StrategyTemplateKeys.FlowZone, StringComparison.OrdinalIgnoreCase))
            {
                var pair = await _market.GetOpenInterestPairAsync(bot.Symbol, cancellationToken);
                if (pair.Previous is { } previous && pair.Latest is { } latest)
                {
                    openInterest = new decimal?[] { previous, latest };
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
                OpenInterest = openInterest
            };
            var quote = _strategy.EvaluateDetailAt(definition, context, new CausalIndicatorCache(candles), candles.Count - 1);
            signalType = quote.Signal;
            reason = quote.Reason;
            describedStop = quote.SuggestedStop;
            describedTake = quote.SuggestedTakeProfit;
        }

        var lastCandle = candles[^1];
        var candleKey = lastCandle.OpenTime.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var clientOrderId = bot.Mode == TradingMode.Live
            ? $"L{bot.Id:N}"[..12] + (candleKey.Length <= 10 ? candleKey : candleKey[^10..])
            : $"p-{bot.Id:N}-{candleKey}";
        var correlationId = _correlation.GetOrCreate();

        if (signalType is SignalType.NoAction or SignalType.Hold)
        {
            bot.LastError = reason;
            return;
        }

        if (bot.Mode == TradingMode.Live && position is not null && !StrategyTemplateKeys.IsImported(definition.Template))
        {
            bot.LastError =
                $"Live Isolated SL/TP own the exit. Strategy {signalType}: {reason}";
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
            bot.Mode == TradingMode.Live ? TradingMode.Live : TradingMode.Paper,
            bot.Mode == TradingMode.Live ? 0m : _options.PaperDefaultBalance,
            cancellationToken);
        var btc = await _store.GetOrCreateBalanceAsync(
            bot.ExchangeAccountId,
            null,
            symbol?.BaseAsset ?? "BTC",
            bot.Mode == TradingMode.Live ? TradingMode.Live : TradingMode.Paper,
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
            if (StrategyTemplateKeys.IsImported(definition.Template) && flatten)
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
                bot.LastError = flatten
                    ? $"Stop or take owns the exit. Strategy {signalType}: {reason}"
                    : reason;
                return;
            }
        }

        if (signalType is not (SignalType.Buy or SignalType.Sell))
        {
            bot.LastError = reason;
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
        if (describedStop is decimal stop && lastPrice > 0m)
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

            profile = FlatRangeRisk(profile, stopPct, takePct);
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
        var source = bot.Mode == TradingMode.Live ? "binance-live" : "paper-engine";
        Record(order, OrderStatus.Submitting, source);
        order.SubmittedAt = _clock.UtcNow;

        ExchangeOrder fill;
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
            Record(order, OrderStatus.Failed, source);
            order.RejectReason = ex.Message;
            await _store.AddOrderAsync(order, cancellationToken);
            await _store.SaveChangesAsync(cancellationToken);
            throw;
        }

        if (fill.Status is not OrderStatus.Filled and not OrderStatus.PartiallyFilled)
        {
            Record(order, fill.Status, source);
            order.RejectReason = $"Binance status {fill.Status}";
            order.ExchangeOrderId = fill.ExchangeOrderId;
            await _store.AddOrderAsync(order, cancellationToken);
            await _store.SaveChangesAsync(cancellationToken);
            throw new DomainException(ErrorCodes.OrderRejected, $"Live order was not filled ({fill.Status}).");
        }

        Record(order, OrderStatus.Filled, source);
        order.FilledQuantity = fill.FilledQuantity;
        order.RemainingQuantity = 0m;
        var fillPrice = PositivePrice(fill.AverageFillPrice) ?? PositivePrice(fill.Price) ?? lastPrice;
        if (bot.Mode != TradingMode.Live)
        {
            if (lastPrice <= 0m)
            {
                throw new DomainException(ErrorCodes.ExchangeUnavailable, "Paper simulator has no last price yet.");
            }

            fillPrice = PaperFillModel.ApplySlippage(lastPrice, side, _options.PaperSlippageBps);
        }

        order.Price = fillPrice;
        order.AverageFillPrice = fillPrice > 0m ? fillPrice : null;
        order.ExchangeOrderId = fill.ExchangeOrderId;
        order.ExchangeTimestamp = fill.ExchangeTimestamp;
        await _store.AddOrderAsync(order, cancellationToken);

        if (!flatten && bot.Mode == TradingMode.Live && fillPrice <= 0m)
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
        var fee = bot.Mode == TradingMode.Live
            ? fill.Fee
            : PaperFillModel.Fee(notional, _options.PaperFeeBps);
        await _store.AddExecutionAsync(new ExecutionFill
        {
            OrderId = order.Id,
            Order = order,
            ExchangeTradeId = PaperFillModel.NewPaperFillId(),
            Price = fillPrice,
            Quantity = quantity,
            Fee = fee,
            FeeAsset = "USDT",
            IsMaker = false,
            ExchangeTimestamp = fill.ExchangeTimestamp ?? _clock.UtcNow,
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
            if (bot.Mode != TradingMode.Live)
            {
                var margin = plan?.IsolatedMargin ?? PortfolioRisk.IsolatedMargin(notional, leverage);
                usdt.Free -= margin + fee;
                usdt.Locked += margin;
                openedMargin = margin;
            }
            else
            {
                openedMargin = plan?.IsolatedMargin ?? PortfolioRisk.IsolatedMargin(notional, leverage);
            }

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
                InitialRiskUsdt = plan?.RiskAmount ?? quantity * fillPrice * (slPercent / 100m),
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
            else
            {
                bot.LastError =
                    $"Paper {(openedSide == PositionSide.Short ? "short" : "buy")} filled. Risk {opened.InitialRiskUsdt:0.##} USDT on {notional:0.##} notional.";
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
        CancellationToken cancellationToken)
    {
        var direction = position.Side == PositionSide.Short ? -1m : 1m;
        var pnl = direction * (fillPrice - position.AverageEntryPrice) * quantity - fee;
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
        position.Fees += fee;
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

        if (openTrade is not null)
        {
            openTrade.ExitOrderId = order.Id;
            openTrade.ExitPrice = fillPrice;
            openTrade.PnL = pnl;
            openTrade.PnLPercent = pnlPercent;
            openTrade.Fees += fee;
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
            Fees = fee,
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
                BotId = bot.Id,
                Symbol = overlay.Symbol,
                Side = overlaySide,
                Quantity = overlay.Quantity,
                AverageEntryPrice = overlay.EntryPrice,
                CurrentPrice = overlay.MarkPrice > 0m ? overlay.MarkPrice : lastPrice,
                UnrealizedPnL = overlay.UnrealizedPnL,
                Fees = 0m,
                OpenedAt = _clock.UtcNow
            };
            position.Events.Add(new PositionEvent
            {
                EventType = "OPEN",
                Quantity = overlay.Quantity,
                Price = overlay.EntryPrice,
                CorrelationId = _correlation.GetOrCreate()
            });
            await _store.AddPositionAsync(position, cancellationToken);
        }

        if (hasStop && hasTake)
        {
            bot.LastError = $"Live Isolated overlay adopted for {bot.Symbol}. SL/TP already working on Binance.";
            return new OverlayProtectResult(position, false);
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not recompute Isolated SL/TP for overlay {Symbol}", bot.Symbol);
            return new OverlayProtectResult(position, false);
        }

        position.StopLossPercent = profile.StopLossPercent;
        position.TakeProfitPercent = profile.TakeProfitPercent;
        position.StopLossPrice = slPrice;
        position.TakeProfitPrice = tpPrice;
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
            $"Live Isolated {bot.Symbol} protection is incomplete. SL {(stops.StopPlaced ? "working" : stops.StopError)} / TP {(stops.TakePlaced ? "working" : stops.TakeError)}. Automatic close is disabled; the missing order will be retried.";
        _logger.LogCritical(
            "Live Isolated {Symbol} has no working STOP for bot {BotId}: {Error}. The project will not auto-close the position.",
            bot.Symbol,
            bot.Id,
            stops.StopError);
        return new OverlayProtectResult(position, true);
    }

    private async Task<ProtectiveStopsResult> AttachLiveProtectiveStopsAsync(
        Bot bot,
        IExchangeConnector connector,
        decimal stop,
        decimal take,
        PositionSide side,
        CancellationToken cancellationToken,
        bool replaceStop = true,
        bool replaceTake = true)
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
            placeTake: replaceTake && take > 0m);
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
