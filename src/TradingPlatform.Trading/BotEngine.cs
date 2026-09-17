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
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Signals;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
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
    private readonly ILogger<BotEngine> _logger;

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
        ILogger<BotEngine> logger)
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
        _logger = logger;
    }

    public async Task EvaluateRunningBotsAsync(CancellationToken cancellationToken = default)
    {
        if (_options.KillSwitchEnabled)
        {
            await _store.StopAllRunningBotsAsync("Kill switch is active.", cancellationToken);
            await _store.SaveChangesAsync(cancellationToken);
            return;
        }

        var bots = await _store.GetRunningBotsAsync(cancellationToken);
        foreach (var bot in bots)
        {
            try
            {
                await EvaluateBotAsync(bot, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bot {BotId} cycle failed", bot.Id);
                bot.LastError = ex.Message;
            }
        }

        await _store.SaveChangesAsync(cancellationToken);
        await _publisher.PublishOverviewAsync(cancellationToken);
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

        var market = await _store.GetSymbolAsync(position.Symbol, cancellationToken);
        var step = market?.StepSize ?? 0.00001m;
        var quantity = PaperFillModel.FloorToStep(position.Quantity, step);
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
        var existing = liveBook.FirstOrDefault(row =>
            string.Equals(row.Symbol, overlay.Symbol, StringComparison.OrdinalIgnoreCase) &&
            row.Side == overlaySide);
        if (existing is not null)
        {
            var owned = await _store.GetBotAsync(existing.BotId, cancellationToken)
                ?? throw new DomainException(ErrorCodes.BotNotFound, "Bot for this position was not found.");
            return (owned, existing, overlay);
        }

        var bots = await _store.ListBotsAsync(cancellationToken);
        var bot = bots.FirstOrDefault(b =>
            b.Mode == TradingMode.Live &&
            string.Equals(b.Symbol, overlay.Symbol, StringComparison.OrdinalIgnoreCase));
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

    private async Task EvaluateBotAsync(Bot bot, CancellationToken cancellationToken)
    {
        if (bot.Status is not BotStatus.Running)
        {
            return;
        }

        if (bot.Mode is not TradingMode.Paper and not TradingMode.Live)
        {
            throw new DomainException(ErrorCodes.LiveTradingDisabled, "This bot mode cannot run.");
        }

        var candles = await _market.GetClosedKlinesAsync(bot.Symbol, bot.Timeframe, _options.KlineLimit, cancellationToken);
        var lastPrice = await _market.GetLastPriceAsync(bot.Symbol, cancellationToken);
        var now = _clock.UtcNow;
        _cache.SetKlines(bot.Symbol, bot.Timeframe, candles);
        _cache.SetTicker(bot.Symbol, lastPrice, now);
        await _publisher.PublishTickerAsync(bot.Symbol, lastPrice, now, cancellationToken);

        var symbol = await _store.GetSymbolAsync(bot.Symbol);
        if (symbol is not null && candles.Count > 0)
        {
            await _store.UpsertClosedCandleAsync(symbol.Id, bot.Timeframe, candles[^1], cancellationToken);
        }

        var position = await _store.GetOpenPositionAsync(bot.Id, bot.Symbol, cancellationToken);
        if (position is not null)
        {
            position.CurrentPrice = lastPrice;
            position.UnrealizedPnL = (lastPrice - position.AverageEntryPrice) * position.Quantity;
        }

        if (candles.Count == 0)
        {
            bot.LastError = "Waiting for closed candles from Binance public market data.";
            return;
        }

        var definition = _validator.Parse(bot.StrategyVersion.DefinitionJson);
        var signalType = _strategy.Evaluate(
            definition,
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = lastPrice,
                HasOpenPosition = position is not null,
                AverageEntryPrice = position?.AverageEntryPrice
            },
            out var reason);

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
        decimal equityUsdt;
        decimal availableUsdt;
        if (bot.Mode == TradingMode.Live)
        {
            var liveBalances = await connector.GetBalancesAsync(cancellationToken);
            availableUsdt = liveBalances.FirstOrDefault(b => b.Asset == "USDT")?.Free ?? 0m;
            equityUsdt = availableUsdt;
            foreach (var held in liveBalances.Where(b => !string.Equals(b.Asset, "USDT", StringComparison.OrdinalIgnoreCase)))
            {
                var qtyHeld = held.Free + held.Locked;
                if (qtyHeld > 0m && _cache.TryGetTicker($"{held.Asset}USDT", out var px))
                {
                    equityUsdt += qtyHeld * px;
                }
            }
        }
        else
        {
            availableUsdt = usdt.Free;
            equityUsdt = usdt.Free + usdt.Locked + (btc.Free + btc.Locked) * lastPrice;
        }

        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var (consecutiveLosses, lastLossAt) = await _store.GetLossStreakAsync(bot.Id, cancellationToken);
        var parameters = EmaRsiTemplate.Read(bot.StrategyVersion.DefinitionJson);
        var book = await _store.GetOpenPositionsForModeAsync(bot.Mode, cancellationToken);
        var unrealized = book.Sum(p => p.UnrealizedPnL);
        var accountDaily = await _store.SumClosedPnLSinceForModeAsync(bot.Mode, dayStart, cancellationToken) + unrealized;
        var openRisk = book
            .Select(p =>
            {
                var stop = p.StopLossPercent > 0m
                    ? p.StopLossPercent
                    : p.Bot.StrategyVersion is not null
                        ? EmaRsiTemplate.Read(p.Bot.StrategyVersion.DefinitionJson).StopLossPercent
                        : parameters.StopLossPercent;
                return PortfolioRisk.RiskFraction(p.Quantity, p.AverageEntryPrice, stop, equityUsdt);
            })
            .Where(r => r > 0m)
            .ToList();
        var snapshot = new RiskSnapshot
        {
            Equity = equityUsdt,
            AvailableBalance = availableUsdt,
            DailyRealizedPnL = await _store.SumClosedPnLSinceAsync(bot.Id, dayStart, cancellationToken),
            OpenPositions = await _store.CountOpenPositionsAsync(bot.Id, cancellationToken),
            DailyTrades = await _store.CountOrdersSinceAsync(bot.Id, dayStart, cancellationToken),
            ConsecutiveLosses = consecutiveLosses,
            LastLossAt = lastLossAt,
            Symbol = bot.Symbol,
            Price = lastPrice,
            StopLossPercent = parameters.StopLossPercent,
            AccountDailyPnL = accountDaily,
            AccountOpenPositions = book.Select(p => p.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            AccountDailyTrades = await _store.CountOrdersSinceForModeAsync(bot.Mode, dayStart, cancellationToken),
            AccountOpenNotional = book.Sum(p => p.Quantity * p.AverageEntryPrice),
            SymbolAlreadyOpen = book.Any(p =>
                p.BotId != bot.Id && string.Equals(p.Symbol, bot.Symbol, StringComparison.OrdinalIgnoreCase)),
            OpenRiskFractions = openRisk
        };

        if (signalType == SignalType.Buy && position is null)
        {
            var risk = _risk.Evaluate(signalType, bot.RiskProfile, snapshot, now);
            if (risk.Decision != RiskDecision.Approved)
            {
                bot.LastError = risk.Reason;
                if (risk.HaltAccount)
                {
                    await _store.StopRunningBotsForModeAsync(
                        bot.Mode,
                        "Account daily loss halt. Open positions were not closed.",
                        cancellationToken);
                }
                else if (bot.RiskProfile.StopBotOnDailyLoss &&
                         risk.Reason.Contains("daily loss", StringComparison.OrdinalIgnoreCase))
                {
                    bot.Status = BotStatus.Stopped;
                    bot.StoppedAt = now;
                }

                return;
            }

            var step = symbol?.StepSize ?? 0.00001m;
            var quantity = PaperFillModel.FloorToStep(risk.ApprovedQuantity, step);
            var minQty = symbol?.MinQuantity ?? 0.00001m;
            var minNotional = symbol?.MinNotional ?? 5m;
            if (quantity < minQty || quantity * lastPrice < minNotional)
            {
                bot.LastError = "Approved size is below the symbol minimum. Heat or exposure cap is likely binding.";
                return;
            }

            if (bot.Mode == TradingMode.Live)
            {
                var leverage = (int)Math.Max(1m, Math.Floor(bot.RiskProfile.MaxLeverage));
                await connector.PrepareSymbolRiskAsync(bot.Symbol, bot.RiskProfile.MarginMode, leverage, cancellationToken);
            }

            await PlaceAndFillAsync(
                bot,
                OrderSide.Buy,
                quantity,
                clientOrderId,
                correlationId,
                usdt,
                btc,
                position,
                lastPrice,
                parameters.StopLossPercent,
                cancellationToken);
            if (bot.Mode != TradingMode.Live)
            {
                bot.LastError = "Paper buy filled.";
            }

            return;
        }

        if (signalType is SignalType.Exit or SignalType.Sell && position is not null)
        {
            if (bot.Mode == TradingMode.Live)
            {
                await CancelLiveProtectiveOrdersAsync(bot, cancellationToken);
            }

            var closeSide = position.Side == PositionSide.Short ? OrderSide.Buy : OrderSide.Sell;
            await PlaceAndFillAsync(
                bot,
                closeSide,
                position.Quantity,
                clientOrderId,
                correlationId,
                usdt,
                btc,
                position,
                lastPrice,
                0m,
                cancellationToken,
                flatten: true);
            bot.LastError = bot.Mode == TradingMode.Live ? "Live exit submitted to Binance." : "Paper exit filled.";
        }
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
        bool flatten = false)
    {
        var connector = _connectors.Create(bot.Mode, bot.ExchangeAccountId);
        var orderSymbol = flatten && position is not null ? position.Symbol : bot.Symbol;
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
        Record(order, OrderStatus.Submitted, source);
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
            throw;
        }

        if (fill.Status is not OrderStatus.Filled and not OrderStatus.PartiallyFilled)
        {
            Record(order, fill.Status == OrderStatus.Rejected ? OrderStatus.Rejected : OrderStatus.Failed, source);
            order.RejectReason = $"Binance status {fill.Status}";
            order.ExchangeOrderId = fill.ExchangeOrderId;
            await _store.AddOrderAsync(order, cancellationToken);
            throw new DomainException(ErrorCodes.OrderRejected, $"Live order was not filled ({fill.Status}).");
        }

        Record(order, OrderStatus.Filled, source);
        order.FilledQuantity = fill.FilledQuantity;
        order.RemainingQuantity = 0m;
        order.Price = fill.Price;
        order.AverageFillPrice = fill.AverageFillPrice;
        order.ExchangeOrderId = fill.ExchangeOrderId;
        order.ExchangeTimestamp = fill.ExchangeTimestamp;
        await _store.AddOrderAsync(order, cancellationToken);

        var fillPrice = fill.AverageFillPrice ?? lastPrice;
        if (bot.Mode != TradingMode.Live)
        {
            if (lastPrice <= 0m)
            {
                throw new DomainException(ErrorCodes.ExchangeUnavailable, "Paper simulator has no last price yet.");
            }

            fillPrice = PaperFillModel.ApplySlippage(lastPrice, side, _options.PaperSlippageBps);
            order.Price = fillPrice;
            order.AverageFillPrice = fillPrice;
        }
        var notional = fillPrice * quantity;
        var fee = bot.Mode == TradingMode.Live
            ? 0m
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
            return;
        }

        if (side == OrderSide.Buy)
        {
            if (bot.Mode != TradingMode.Live)
            {
                usdt.Free -= notional + fee;
                baseAsset.Free += quantity;
            }
            var opened = new Position
            {
                BotId = bot.Id,
                Symbol = bot.Symbol,
                Side = PositionSide.Long,
                Quantity = quantity,
                AverageEntryPrice = fillPrice,
                CurrentPrice = fillPrice,
                UnrealizedPnL = 0m,
                Fees = fee,
                StopLossPercent = stopLossPercent,
                InitialRiskUsdt = quantity * fillPrice * (stopLossPercent / 100m),
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
                Side = OrderSide.Buy,
                Quantity = quantity,
                EntryPrice = fillPrice,
                Fees = fee,
                OpenedAt = _clock.UtcNow,
                CorrelationId = correlationId
            }, cancellationToken);

            if (bot.Mode == TradingMode.Live)
            {
                await AttachLiveProtectiveStopsAsync(bot, connector, fillPrice, cancellationToken);
            }
            else
            {
                bot.LastError = "Paper buy filled.";
            }
        }
        else if (position is not null)
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
            if (position.Side == PositionSide.Long)
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
            CorrelationId = correlationId
        }, cancellationToken);
    }

    private async Task AttachLiveProtectiveStopsAsync(
        Bot bot,
        IExchangeConnector connector,
        decimal fillPrice,
        CancellationToken cancellationToken)
    {
        await CancelLiveProtectiveOrdersAsync(bot, cancellationToken);
        try
        {
            var parameters = EmaRsiTemplate.Read(bot.StrategyVersion.DefinitionJson);
            var symbol = await _store.GetSymbolAsync(bot.Symbol, cancellationToken);
            var (stop, take) = LiveProtectivePrices.FromEntry(
                fillPrice,
                parameters.StopLossPercent,
                parameters.TakeProfitPercent,
                symbol?.TickSize ?? 0m);
            await connector.PlaceClosePositionStopsAsync(
                bot.Symbol,
                OrderSide.Sell,
                stop,
                take,
                LiveProtectivePrices.StopClientOrderId(bot.Id),
                LiveProtectivePrices.TakeClientOrderId(bot.Id),
                cancellationToken);
            bot.LastError =
                $"Live buy filled. Binance SL {parameters.StopLossPercent:0.##}% / TP {parameters.TakeProfitPercent:0.##}% placed.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Binance SL/TP failed after live fill for bot {BotId}", bot.Id);
            bot.LastError = $"Live buy filled. Binance SL/TP FAILED: {ex.Message}";
        }
    }

    private async Task CancelLiveProtectiveOrdersAsync(Bot bot, CancellationToken cancellationToken)
    {
        var connector = _connectors.Create(bot.Mode, bot.ExchangeAccountId);
        await connector.CancelOrderAsync(bot.Symbol, LiveProtectivePrices.StopClientOrderId(bot.Id), null, cancellationToken);
        await connector.CancelOrderAsync(bot.Symbol, LiveProtectivePrices.TakeClientOrderId(bot.Id), null, cancellationToken);
    }

    private static decimal RelativeDrift(decimal left, decimal right)
    {
        var basis = Math.Max(Math.Abs(left), Math.Abs(right));
        return basis <= 0m ? 0m : Math.Abs(left - right) / basis;
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
