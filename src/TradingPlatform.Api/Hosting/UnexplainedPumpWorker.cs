using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.News;
using TradingPlatform.Risk;
using TradingPlatform.Trading;

namespace TradingPlatform.Api.Hosting;

/// <summary>
/// Scans USD-M perps for a 150% rise in the last 24 hours. The headline batch goes to the model once.
/// No real cause, and a closed turn off that high, is a short. The short follows the low and closes when the drop turns up.
/// The coin waits until that rise falls back under 150%. A stop in the first 15 minutes, with no move in favor, leaves the high open for one more short.
/// </summary>
public sealed class UnexplainedPumpWorker : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _http;
    private readonly IHostEnvironment _environment;
    private readonly IOptionsMonitor<NewsOptions> _options;
    private readonly INewsLanguageModel _model;
    private readonly ILogger<UnexplainedPumpWorker> _logger;
    private readonly Dictionary<string, (DateTimeOffset At, PumpNewsBundle Bundle)> _bundles = new(StringComparer.OrdinalIgnoreCase);
    private int _modelCallsThisCycle;

    public UnexplainedPumpWorker(
        IServiceScopeFactory scopes,
        IHttpClientFactory http,
        IHostEnvironment environment,
        IOptionsMonitor<NewsOptions> options,
        INewsLanguageModel model,
        ILogger<UnexplainedPumpWorker> logger)
    {
        _scopes = scopes;
        _http = http;
        _environment = environment;
        _options = options;
        _model = model;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pump = _options.CurrentValue.UnexplainedPump ?? new UnexplainedPumpOptions();
            if (!pump.Enabled)
            {
                await Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            var open = false;
            try
            {
                open = await CycleAsync(pump, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexplained-pump cycle failed. Open shorts were not changed by the failed cycle.");
            }

            var wait = TimeSpan.FromMinutes(Math.Max(1, pump.PollMinutes));
            if (!open)
            {
                await Delay(wait, stoppingToken);
                continue;
            }

            var guardUntil = DateTimeOffset.UtcNow + wait;
            while (open && DateTimeOffset.UtcNow < guardUntil && !stoppingToken.IsCancellationRequested)
            {
                await Delay(TimeSpan.FromSeconds(15), stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    open = await GuardOpenShortsAsync(pump, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexplained-pump exit check failed. The open short was left as it is.");
                }
            }
        }
    }

    private async Task<bool> CycleAsync(UnexplainedPumpOptions pump, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<IMarketScanner>();
        var market = scope.ServiceProvider.GetRequiredService<IPublicMarketDataClient>();
        var rows = await scanner.ScanAsync(cancellationToken);
        var root = FindRepo(_environment.ContentRootPath);
        var book = await LoadAsync(StatePath(root), cancellationToken);
        book.Asks ??= [];
        _modelCallsThisCycle = 0;
        var now = DateTimeOffset.UtcNow;
        var trading = scope.ServiceProvider.GetRequiredService<IOptions<TradingOptions>>().Value;
        var liveEntries = trading.VenueKind == TradingVenueKind.Live && trading.EntriesEnabled;
        IExchangeConnector? connector = null;
        LiveAccountSnapshot account = scope.ServiceProvider.GetRequiredService<ILiveAccountCache>().Current;
        if (liveEntries)
        {
            connector = await ConnectorAsync(scope.ServiceProvider, cancellationToken);
            if (connector is not null)
            {
                account = await FreshAccountAsync(scope.ServiceProvider, now, cancellationToken);
                await ManageExitsAsync(connector, account, book, pump, now, cancellationToken);
                await RememberPnlAsync(connector, book, now, cancellationToken);
                await SaveAsync(StatePath(root), book, cancellationToken);
            }
        }

        var candidates = rows
            .Where(row => row.QuoteVolume24h >= pump.MinQuoteVolume && row.LastPrice > 0m && row.High24h > 0m)
            .Where(row => row.PriceChangePercent >= 40m || (row.Low24h > 0m && row.High24h / row.Low24h >= 1.8m))
            .OrderByDescending(row => row.PriceChangePercent)
            .Take(8)
            .ToList();
        foreach (var symbol in NoteCooling(book, rows, pump.MinRise))
        {
            _logger.LogInformation("UnexplainedPump {Symbol} rise fell back under the pump line. A new pump can be shorted.", symbol);
        }

        var seen = new HashSet<string>(candidates.Select(row => row.Contract.Symbol), StringComparer.OrdinalIgnoreCase);
        var retrying = book.Tickets
            .Where(ticket => ticket.Closed && ticket.WickRetry)
            .Select(ticket => ticket.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (retrying.Contains(row.Contract.Symbol) && seen.Add(row.Contract.Symbol))
            {
                candidates.Add(row);
            }
        }

        var checks = new List<UnexplainedPumpCheckRow>();
        if (candidates.Count == 0)
        {
            _logger.LogInformation("Unexplained-pump scan found no coin up enough to check. No order.");
            await SaveAsync(StatePath(root), book, cancellationToken);
            await PublishAsync(root, book, account, checks, pump.Enabled, now, cancellationToken);
            return book.Tickets.Any(ticket => !ticket.Closed);
        }

        var store = new FileNewsStore(Path.Combine(root, _options.CurrentValue.StorePath));
        var events = await store.LoadEventsAsync(cancellationToken);
        var http = _http.CreateClient("news");
        http.Timeout = TimeSpan.FromSeconds(20);
        var filters = liveEntries
            ? (await market.GetPaperUniverseAsync(cancellationToken)).ToDictionary(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, RankedUsdtSpotSymbol>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in candidates)
        {
            try
            {
                await ConsiderAsync(row, market, http, events, book, checks, pump, filters, trading, connector, account, scope.ServiceProvider, now, liveEntries, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexplained-pump check failed for {Symbol}. That coin was skipped.", row.Contract.Symbol);
            }
        }

        await SaveAsync(StatePath(root), book, cancellationToken);
        await PublishAsync(root, book, account, checks, pump.Enabled, now, cancellationToken);
        return book.Tickets.Any(ticket => !ticket.Closed);
    }

    private async Task<bool> GuardOpenShortsAsync(UnexplainedPumpOptions pump, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var root = FindRepo(_environment.ContentRootPath);
        var book = await LoadAsync(StatePath(root), cancellationToken);
        if (!book.Tickets.Any(ticket => !ticket.Closed))
        {
            return false;
        }

        var trading = scope.ServiceProvider.GetRequiredService<IOptions<TradingOptions>>().Value;
        if (trading.VenueKind != TradingVenueKind.Live || !trading.EntriesEnabled)
        {
            return false;
        }

        var connector = await ConnectorAsync(scope.ServiceProvider, cancellationToken);
        if (connector is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var account = await FreshAccountAsync(scope.ServiceProvider, now, cancellationToken);
        await ManageExitsAsync(connector, account, book, pump, now, cancellationToken);
        await RememberPnlAsync(connector, book, now, cancellationToken);
        await SaveAsync(StatePath(root), book, cancellationToken);
        await PublishAsync(root, book, account, [], pump.Enabled, now, cancellationToken);
        return book.Tickets.Any(ticket => !ticket.Closed);
    }

    private static async Task<LiveAccountSnapshot> FreshAccountAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cache = services.GetRequiredService<ILiveAccountCache>();
        if (cache.Current.UpdatedAt is not { } at || now - at >= TimeSpan.FromSeconds(15))
        {
            await services.GetRequiredService<IExchangeAccountService>().GetStatusAsync(Guid.Empty, cancellationToken);
        }

        return cache.Current;
    }

    private async Task ConsiderAsync(
        MarketScanRow row,
        IPublicMarketDataClient market,
        HttpClient http,
        IReadOnlyList<NewsEvent> events,
        PumpBook book,
        List<UnexplainedPumpCheckRow> checks,
        UnexplainedPumpOptions pump,
        IReadOnlyDictionary<string, RankedUsdtSpotSymbol> filters,
        TradingOptions trading,
        IExchangeConnector? connector,
        LiveAccountSnapshot account,
        IServiceProvider services,
        DateTimeOffset now,
        bool liveEntries,
        CancellationToken cancellationToken)
    {
        var symbol = row.Contract.Symbol;
        var move = UnexplainedPumpShort.Measure(row.High24h, row.LastPrice, row.PriceChangePercent, pump.MinRise);
        if (move is null)
        {
            return;
        }

        var bars = await market.GetClosedKlinesAsync(symbol, Timeframe.FifteenMinutes, 97, cancellationToken);
        var dump = UnexplainedPumpShort.Reversal(move.PumpHigh, row.LastPrice, bars, pump.ReversalDrop, pump.MaxDropFromHigh);
        var news = await NewsAsync(http, symbol, row.Contract.BaseAsset, events, move, book, now, cancellationToken);
        var decision = UnexplainedPumpShort.Decide(symbol, move, dump, news, row.LastPrice, row.QuoteVolume24h, pump);
        var check = new UnexplainedPumpCheckRow
        {
            At = now,
            Coin = symbol,
            Signal = decision.Signal,
            OrderState = "NotSent",
            Reason = decision.Reason,
            Rise = decision.Rise,
            DaySpan = decision.DaySpan,
            PumpHigh = decision.PumpHigh,
            Entry = decision.EntryPrice
        };
        checks.Add(check);
        _logger.LogInformation("UnexplainedPump {Symbol} {Signal} {Reason}", symbol, decision.Signal, decision.Reason);
        if (decision.Signal != UnexplainedPumpSignals.Short)
        {
            return;
        }

        var day = now.UtcDateTime.ToString("yyyy-MM-dd");
        if (!MaySend(book, symbol, decision.PumpHigh, out var block))
        {
            check.OrderState = "Filled";
            check.Reason = block;
            return;
        }

        if (!liveEntries || connector is null)
        {
            check.Reason = decision.Reason + " Live entries are off, so no order was sent.";
            _logger.LogInformation("UnexplainedPump {Symbol} short is ready. Live entries are off, so no order was sent.", symbol);
            return;
        }

        if (account.OpenPositions.Any(position => position.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
        {
            check.OrderState = "Filled";
            check.Reason = "This coin already has an open position.";
            _logger.LogInformation("UnexplainedPump {Symbol} already has an open position. No second order.", symbol);
            return;
        }

        await SubmitAsync(services, connector, account, trading, filters, book, decision, check, now, day, cancellationToken);
    }

    private static bool MaySend(PumpBook book, string symbol, decimal pumpHigh, out string block)
    {
        var mine = book.Tickets
            .Where(ticket => ticket.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(ticket => ticket.EnteredAt)
            .ToList();
        var open = mine.Any(ticket => !ticket.Closed);
        var latest = mine.LastOrDefault();
        var wick = latest is { Closed: true, WickRetry: true };
        var consumed = wick
            ? mine.LastOrDefault(ticket => ticket.Closed && !ticket.WickRetry)
            : latest is { Closed: true } ? latest : null;
        var reason = UnexplainedPumpShort.ReentryBlock(open, pumpHigh, consumed?.PumpHigh, consumed?.Cooled == true, wick);
        block = reason ?? string.Empty;
        return reason is null;
    }

    /// <summary>Marks a closed short. A wick leaves the high open for one retry and does not count as cooled.</summary>
    private static bool Finish(PumpTicket ticket, DateTimeOffset now, string reason)
    {
        ticket.Closed = true;
        ticket.ClosedAt ??= now;
        ticket.CloseReason ??= reason;
        if (!ticket.FollowsWick && UnexplainedPumpShort.WickStop(ticket.Entry, ticket.BestMark, now - ticket.EnteredAt))
        {
            ticket.WickRetry = true;
        }

        return ticket.WickRetry;
    }

    /// <summary>A coin whose last finished short is still hot cools when its 24-hour rise prints under the pump line.</summary>
    private static List<string> NoteCooling(PumpBook book, IReadOnlyList<MarketScanRow> rows, decimal minRise)
    {
        var bySymbol = new Dictionary<string, MarketScanRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            bySymbol.TryAdd(row.Contract.Symbol, row);
        }

        var cooled = new List<string>();
        foreach (var ticket in book.Tickets)
        {
            if (!ticket.Closed || ticket.Cooled || ticket.WickRetry || string.IsNullOrWhiteSpace(ticket.Symbol))
            {
                continue;
            }

            if (!bySymbol.TryGetValue(ticket.Symbol, out var row))
            {
                continue;
            }

            if (!UnexplainedPumpShort.RiseHasEnded(row.High24h, row.LastPrice, row.PriceChangePercent, minRise))
            {
                continue;
            }

            ticket.Cooled = true;
            if (!cooled.Contains(ticket.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                cooled.Add(ticket.Symbol);
            }
        }

        return cooled;
    }

    private static int SameDayOrders(PumpBook book, string symbol, DateTimeOffset now) =>
        book.Tickets.Count(ticket =>
            ticket.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
            && ticket.Day == now.UtcDateTime.ToString("yyyy-MM-dd"));

    private async Task SubmitAsync(
        IServiceProvider services,
        IExchangeConnector connector,
        LiveAccountSnapshot account,
        TradingOptions trading,
        IReadOnlyDictionary<string, RankedUsdtSpotSymbol> filters,
        PumpBook book,
        UnexplainedPumpDecision decision,
        UnexplainedPumpCheckRow check,
        DateTimeOffset now,
        string day,
        CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<ITradingStore>();
        var source = await store.GetNewsRiskAsync(cancellationToken);
        var stopPercent = UnexplainedPumpShort.MaxStopPercent;
        if (stopPercent <= 0m)
        {
            check.OrderState = "Rejected";
            check.Reason = "Stop above the high does not fit Isolated margin. No order.";
            _logger.LogInformation("UnexplainedPump {Symbol} stop above the high does not fit Isolated margin. No order.", decision.Symbol);
            return;
        }

        // 20 USDT margin at 10x. The 50% band above the high still closes the short in software; the exchange stop is the 10x cap.
        var profile = Copy(source, stopPercent, 80m, UnexplainedPumpShort.OrderLeverage);
        profile.RiskPerTradePercent = UnexplainedPumpShort.RiskPerTradePercent;
        profile.MaxPortfolioRiskPercent = UnexplainedPumpShort.MaxPortfolioRiskPercent;
        var (equity, available, equityBlock) = NewsTradeAdapter.LiveEquity(account, now, TimeSpan.FromMinutes(2));
        if (equityBlock is not null)
        {
            check.OrderState = "Rejected";
            check.Reason = equityBlock;
            _logger.LogInformation("UnexplainedPump {Symbol} {Reason}", decision.Symbol, equityBlock);
            return;
        }

        filters.TryGetValue(decision.Symbol, out var filter);
        if (filter is null)
        {
            check.OrderState = "Rejected";
            check.Reason = "No Binance USD-M filters. No order.";
            _logger.LogInformation("UnexplainedPump {Symbol} has no Binance USD-M filters. No order.", decision.Symbol);
            return;
        }

        var openLoss = Math.Min(0m, account.OpenPositions.Sum(position => position.UnrealizedPnL));
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var dailyPnl = await store.SumClosedPnLSinceForModeAsync(TradingMode.Live, dayStart, cancellationToken) + openLoss;
        var weeklyPnl = await store.SumClosedPnLSinceForModeAsync(TradingMode.Live, BotEngine.WeekStart(now), cancellationToken) + openLoss;
        var streak = await store.GetLossStreakForModeAsync(TradingMode.Live, cancellationToken);
        var admin = await store.GetFirstAdminAsync(cancellationToken);
        var liveAccount = await services.GetRequiredService<IExchangeCredentialStore>().GetLiveAccountAsync(admin.Id, cancellationToken);
        decimal? drawdown = null;
        if (liveAccount is not null)
        {
            var (peak, lastPointAt) = await store.GetEquityPeakAsync(liveAccount.Id, TradingMode.Live, now.AddDays(-Math.Max(1, trading.EquityPeakLookbackDays)), cancellationToken);
            drawdown = BotEngine.DrawdownPercent(peak, lastPointAt, account.FuturesEquity, now);
        }

        var openPump = book.Tickets.Count(ticket =>
            !ticket.Closed
            && account.OpenPositions.Any(position => position.Symbol.Equals(ticket.Symbol, StringComparison.OrdinalIgnoreCase) && position.Side == "Short"));
        var snapshot = new RiskSnapshot
        {
            Equity = equity,
            AvailableBalance = available,
            Symbol = decision.Symbol,
            Price = decision.EntryPrice,
            Side = PositionSide.Short,
            AccountDailyPnL = dailyPnl,
            AccountWeeklyPnL = weeklyPnl,
            DrawdownPercent = drawdown,
            SymbolAlreadyOpen = false,
            OpenPositionCount = openPump,
            ConsecutiveLosses = streak.ConsecutiveLosses,
            LastLossAt = streak.LastLossAt,
            MarketDataAgeMs = 0,
            Sizing = new RiskSizingHints
            {
                StepSize = filter.StepSize,
                MinQuantity = filter.MinQuantity,
                MinNotional = filter.MinNotional,
                QuantityPrecision = filter.QuantityPrecision,
                MaxMarginUsdt = UnexplainedPumpShort.MaxEntryMarginUsdt,
                TakerFeePercent = await connector.GetTakerFeePercentAsync(decision.Symbol, cancellationToken) ?? RiskEngine.DefaultTakerFeePercent,
                SlippagePercent = RiskEngine.DefaultSlippagePercent
            }
        };
        var risk = services.GetRequiredService<IRiskEngine>();
        var handoff = NewsTradeAdapter.Handoff(
            "SHORT",
            decision.EntryPrice,
            filter.TickSize,
            profile,
            snapshot,
            risk,
            now,
            true,
            true,
            UnexplainedPumpShort.ClientOrderId(decision.Symbol, now, SameDayOrders(book, decision.Symbol, now)));
        if (handoff.RiskDecision == "Approved")
        {
            var block = RiskLiveGuard.Reject(
                TradingMode.Live,
                profile,
                new LiveRiskFacts(
                    equity,
                    0m,
                    openPump,
                    0,
                    dailyPnl,
                    profile.RiskPerTradePercent,
                    handoff.Leverage,
                    profile.StopLossPercent,
                    handoff.Quantity,
                    handoff.Notional,
                    filter.MinQuantity,
                    filter.MinNotional,
                    available,
                    handoff.Margin,
                    trading.KillSwitchEnabled,
                    streak.ConsecutiveLosses,
                    streak.LastLossAt,
                    now,
                    profile.MaxDrawdownPercent <= 0m || drawdown is not null,
                    weeklyPnl,
                    drawdown));
            if (block is not null)
            {
                handoff = NewsTradeAdapter.Rejected("SHORT", block);
            }
        }

        if (handoff.RiskDecision != "Approved" || handoff.Request is null)
        {
            check.OrderState = "Rejected";
            check.Reason = handoff.RiskReason;
            _logger.LogInformation("UnexplainedPump {Symbol} rejected. {Reason}", decision.Symbol, handoff.RiskReason);
            return;
        }

        var reconciliation = services.GetRequiredService<ReconciliationState>();
        var leverage = (int)Math.Max(1m, Math.Floor(handoff.Leverage));
        await connector.PrepareSymbolRiskAsync(decision.Symbol, MarginMode.Isolated, leverage, cancellationToken);
        var fill = await NewsTradeAdapter.SubmitAsync(
            connector,
            handoff.Request,
            new LiveEntryFacts(
                TradingMode.Live,
                trading.EntriesEnabled,
                trading.KillSwitchEnabled,
                reconciliation.IsFresh(now, TimeSpan.FromSeconds(Math.Max(1, trading.ReconciliationMaxAgeSeconds))),
                reconciliation.BlockReason is not null,
                true,
                true,
                true,
                false),
            cancellationToken);
        if (fill is null || fill.FilledQuantity <= 0m)
        {
            check.OrderState = "Rejected";
            check.Reason = "The order was not filled.";
            _logger.LogInformation("UnexplainedPump {Symbol} order was not filled.", decision.Symbol);
            return;
        }

        var fillPrice = fill.AverageFillPrice is > 0m ? fill.AverageFillPrice.Value : decision.EntryPrice;
        var stopPrice = UnexplainedPumpShort.DisasterStopPrice(fillPrice);
        try
        {
            var info = await connector.GetSymbolInformationAsync(decision.Symbol, cancellationToken);
            stopPrice = LiveProtectivePrices.RoundToTick(stopPrice, info.TickSize, down: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "UnexplainedPump {Symbol} tick size was not read. The stop uses the raw price.", decision.Symbol);
        }

        var stops = await connector.PlaceClosePositionStopsAsync(
            decision.Symbol,
            OrderSide.Buy,
            stopPrice,
            handoff.TakeProfitPrice,
            handoff.Request.ClientOrderId + "-sl",
            handoff.Request.ClientOrderId + "-tp",
            cancellationToken,
            placeTake: false);
        check.OrderState = "Filled";
        if (!stops.HasWorkingStop)
        {
            check.Reason = "Filled. The exchange stop was not accepted (" + stops.StopError + "). The short stays open.";
            _logger.LogWarning(
                "UnexplainedPump {Symbol} exchange stop was not accepted ({Error}). The short stays open.",
                decision.Symbol,
                stops.StopError);
        }
        check.Entry = fillPrice;
        check.Quantity = fill.FilledQuantity;
        check.Stop = stopPrice;
        var followsWick = book.Tickets
            .Where(ticket => ticket.Symbol.Equals(decision.Symbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(ticket => ticket.EnteredAt)
            .LastOrDefault() is { Closed: true, WickRetry: true };
        book.Tickets.Add(new PumpTicket
        {
            Symbol = decision.Symbol,
            Day = day,
            PumpHigh = decision.PumpHigh,
            Entry = fillPrice,
            EnteredAt = now,
            ClientOrderId = handoff.Request.ClientOrderId,
            Quantity = fill.FilledQuantity,
            DisasterStop = stopPrice,
            RestingStop = stops.HasWorkingStop ? stopPrice : 0m,
            FollowsWick = followsWick
        });
        _logger.LogInformation(
            "UnexplainedPump {Symbol} short filled at {Entry}. Stop is {Stop}.",
            decision.Symbol,
            fillPrice,
            stopPrice);
    }

    private async Task ManageExitsAsync(
        IExchangeConnector connector,
        LiveAccountSnapshot account,
        PumpBook book,
        UnexplainedPumpOptions pump,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var ticket in book.Tickets.Where(item => !item.Closed))
        {
            var position = account.OpenPositions.FirstOrDefault(item =>
                item.Symbol.Equals(ticket.Symbol, StringComparison.OrdinalIgnoreCase) && item.Side == "Short" && item.Quantity > 0m);
            if (position is null)
            {
                if (account.FuturesBookFresh && now - ticket.EnteredAt > TimeSpan.FromMinutes(2))
                {
                    var trailed = ticket.RestingStop > 0m && ticket.DisasterStop > ticket.RestingStop;
                    if (Finish(ticket, now, trailed ? UnexplainedPumpShort.TurnedUp : "The short is no longer open.") && ticket.WickRetry)
                    {
                        _logger.LogInformation(
                            "UnexplainedPump {Symbol} stopped before the drop started. That high stays open for one more short.",
                            ticket.Symbol);
                    }
                }

                continue;
            }

            if (position.MarkPrice > 0m && (ticket.BestMark <= 0m || position.MarkPrice < ticket.BestMark))
            {
                ticket.BestMark = position.MarkPrice;
                ticket.BestMarkAt = now;
            }

            if (UnexplainedPumpShort.PromoteClosedLow(ticket.BestMark, ticket.BestMarkAt, ticket.TrailMark, now, out var closedLow))
            {
                ticket.TrailMark = closedLow;
            }

            var mark = position.MarkPrice;
            var throughHigh = mark > ticket.PumpHigh * (1m + pump.StopBuffer);
            var started = UnexplainedPumpShort.DumpHasStarted(ticket.Entry, ticket.BestMark, pump.DumpStartedPercent);
            var expired = !started && now - ticket.EnteredAt >= TimeSpan.FromHours(Math.Max(1, pump.MaxHoldHours));
            var follow = UnexplainedPumpShort.Follow(
                ticket.Entry,
                ticket.TrailMark,
                ticket.DisasterStop,
                pump.TrailPercent,
                pump.TightTrailPercent,
                pump.TightenAfterPercent);
            if (follow.Following && follow.Stop > mark && (ticket.RestingStop <= 0m || follow.Stop < ticket.RestingStop))
            {
                await RestFollowStopAsync(connector, ticket, follow.Stop, mark, cancellationToken);
            }

            var bounce = UnexplainedPumpShort.TrailOverrun(follow, mark, ticket.RestingStop)
                ? UnexplainedPumpShort.TurnedUp
                : null;
            if (ticket.StopExposed && mark > 0m)
            {
                var guard = follow.Following && follow.Stop > 0m ? follow.Stop : ticket.DisasterStop;
                if (guard > 0m && mark >= guard)
                {
                    bounce ??= follow.Following ? UnexplainedPumpShort.TurnedUp : "The exchange stop was missing. Closed on the mark.";
                }
            }

            if (bounce is null && !throughHigh && !expired)
            {
                continue;
            }

            if (bounce is not null)
            {
                try
                {
                    await connector.CancelAllOrdersAsync(ticket.Symbol, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "UnexplainedPump {Symbol} old stop was left up while the drop is closed at the market.", ticket.Symbol);
                }
            }

            var clientId = ticket.ClientOrderId + (bounce is not null ? "-bn" : throughHigh ? "-hi" : "-tm");
            await connector.PlaceOrderAsync(
                new PlaceOrderRequest(clientId, ticket.Symbol, OrderSide.Buy, OrderType.Market, position.Quantity, null, TimeSpan.FromSeconds(5), ReduceOnly: true),
                cancellationToken);
            var closeReason = bounce ?? (throughHigh ? "Price went through the band above the pump." : "Hold time elapsed.");
            if (Finish(ticket, now, closeReason) && ticket.WickRetry)
            {
                _logger.LogInformation(
                    "UnexplainedPump {Symbol} stopped before the drop started. That high stays open for one more short.",
                    ticket.Symbol);
            }

            _logger.LogInformation(
                "UnexplainedPump {Symbol} short closed. {Reason}",
                ticket.Symbol,
                ticket.CloseReason);
        }
    }

    /// <summary>
    /// Pulls the resting buy stop down to <paramref name="stop"/>. The previous stop is put back if the new one is rejected,
    /// so a failed ratchet does not leave the short naked.
    /// </summary>
    private async Task RestFollowStopAsync(
        IExchangeConnector connector,
        PumpTicket ticket,
        decimal stop,
        decimal mark,
        CancellationToken cancellationToken)
    {
        var price = stop;
        try
        {
            var info = await connector.GetSymbolInformationAsync(ticket.Symbol, cancellationToken);
            price = LiveProtectivePrices.RoundToTick(stop, info.TickSize, down: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "UnexplainedPump {Symbol} tick size was not read. The follow stop uses the raw price.", ticket.Symbol);
        }

        if (price <= mark)
        {
            return;
        }

        var previous = ticket.RestingStop;
        if (previous > 0m && price >= previous)
        {
            return;
        }
        try
        {
            await connector.CancelAllOrdersAsync(ticket.Symbol, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "UnexplainedPump {Symbol} follow stop was left where it is.", ticket.Symbol);
            return;
        }

        ticket.RestingStop = 0m;
        if (await PlaceRestingStopAsync(connector, ticket, price, cancellationToken))
        {
            ticket.RestingStop = price;
            ticket.StopExposed = false;
            _logger.LogInformation("UnexplainedPump {Symbol} follow stop is {Stop}.", ticket.Symbol, price);
            return;
        }

        if (previous > mark && await PlaceRestingStopAsync(connector, ticket, previous, cancellationToken))
        {
            ticket.RestingStop = previous;
            _logger.LogWarning("UnexplainedPump {Symbol} follow stop was not accepted. The previous stop is back at {Stop}.", ticket.Symbol, previous);
            return;
        }

        ticket.StopExposed = true;
        _logger.LogWarning("UnexplainedPump {Symbol} has no resting stop. The next mark through the line closes it.", ticket.Symbol);
    }

    private async Task<bool> PlaceRestingStopAsync(
        IExchangeConnector connector,
        PumpTicket ticket,
        decimal price,
        CancellationToken cancellationToken)
    {
        ticket.StopRevision++;
        var id = ticket.ClientOrderId + "-s" + ticket.StopRevision.ToString(CultureInfo.InvariantCulture);
        var placed = await connector.PlaceClosePositionStopsAsync(
            ticket.Symbol,
            OrderSide.Buy,
            price,
            0m,
            id,
            id + "t",
            cancellationToken,
            placeTake: false);
        return placed.HasWorkingStop;
    }

    private async Task<PumpNewsVerdict> NewsAsync(
        HttpClient http,
        string symbol,
        string baseAsset,
        IReadOnlyList<NewsEvent> events,
        PumpMove move,
        PumpBook book,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_bundles.TryGetValue(symbol, out var cached) || now - cached.At >= TimeSpan.FromMinutes(15))
        {
            var bundle = await PumpNewsSearch.CollectAsync(http, symbol, baseAsset, events, now, cancellationToken);
            cached = (now, bundle);
            _bundles[symbol] = cached;
        }

        if (!cached.Bundle.InternetOk)
        {
            return PumpNewsVerdict.Incomplete(cached.Bundle.Failure);
        }

        var pack = PumpRiseReview.ForModel(cached.Bundle.Headlines, baseAsset, symbol);
        if (pack.Count == 0)
        {
            return PumpNewsVerdict.Clear("No headline in the window names " + baseAsset + ".");
        }

        var fingerprint = PumpRiseReview.Fingerprint(pack);
        var prior = book.Asks.LastOrDefault(item => item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (!PumpRiseReview.ShouldAsk(prior, fingerprint, now))
        {
            return PumpRiseReview.FromMemory(prior!);
        }

        var ai = _options.CurrentValue.Ai ?? new NewsAiOptions();
        if (!ai.Enabled || !ai.HasKey())
        {
            return PumpNewsVerdict.Incomplete("News AI has no key, so this rise was not judged. No short.");
        }

        if (_modelCallsThisCycle >= 1)
        {
            return PumpNewsVerdict.Incomplete("Waiting for the model. This coin is not sent again until the previous coin is finished.");
        }

        _modelCallsThisCycle++;
        var (system, user) = PumpRiseReview.Prompt(symbol, baseAsset, move.Rise, pack);
        var completion = await _model.CompleteAsync(
            new NewsModelRequest(ai.FastModel, system, user, TimeSpan.FromSeconds(Math.Max(5, ai.TimeoutSeconds))),
            cancellationToken);
        if (!completion.Succeeded || string.IsNullOrWhiteSpace(completion.Content) || !PumpRiseReview.TryParse(completion.Content, out var verdict))
        {
            var reason = completion.Succeeded
                ? "The model reply could not be read. No short."
                : completion.Error ?? "The model call failed. No short.";
            PumpRiseReview.Remember(book.Asks, new PumpAskRecord
            {
                Symbol = symbol,
                Fingerprint = fingerprint,
                AskedAt = now,
                Succeeded = false,
                Reason = reason
            });
            _logger.LogWarning("UnexplainedPump {Symbol} model was not used. {Reason}", symbol, reason);
            return PumpNewsVerdict.Incomplete(reason);
        }

        PumpRiseReview.Remember(book.Asks, new PumpAskRecord
        {
            Symbol = symbol,
            Fingerprint = fingerprint,
            AskedAt = now,
            Succeeded = true,
            CatalystFound = verdict.CatalystFound,
            Reason = verdict.Headline ?? verdict.Reason
        });
        _logger.LogInformation(
            "UnexplainedPump {Symbol} model {Verdict} {Reason}",
            symbol,
            verdict.CatalystFound ? "catalyst" : "no-catalyst",
            verdict.Reason);
        return verdict;
    }

    private static async Task<IExchangeConnector?> ConnectorAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var admin = await services.GetRequiredService<ITradingStore>().GetFirstAdminAsync(cancellationToken);
        var account = await services.GetRequiredService<IExchangeCredentialStore>().GetLiveAccountAsync(admin.Id, cancellationToken);
        return account is null
            ? null
            : services.GetRequiredService<IExchangeConnectorFactory>().Create(TradingMode.Live, account.Id);
    }

    private static RiskProfile Copy(RiskProfile source, decimal stopPercent, decimal takePercent, decimal leverage) =>
        new()
        {
            Name = source.Name,
            RiskPerTradePercent = source.RiskPerTradePercent,
            StopLossPercent = stopPercent,
            TakeProfitPercent = takePercent,
            MaxLeverage = leverage,
            MaxDailyLossPercent = source.MaxDailyLossPercent,
            MaxWeeklyLossPercent = source.MaxWeeklyLossPercent,
            MaxDrawdownPercent = source.MaxDrawdownPercent,
            MaxPortfolioRiskPercent = source.MaxPortfolioRiskPercent,
            MaxSimultaneousPositions = source.MaxSimultaneousPositions,
            MaxConsecutiveLosses = source.MaxConsecutiveLosses,
            CooldownMinutes = source.CooldownMinutes,
            MinimumLiquidationSafetyBufferPercent = source.MinimumLiquidationSafetyBufferPercent,
            AllowLive = source.AllowLive,
            IsActive = source.IsActive
        };

    private static async Task<PumpBook> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new PumpBook();
        }

        var book = JsonSerializer.Deserialize<PumpBook>(await File.ReadAllTextAsync(path, cancellationToken)) ?? new PumpBook();
        book.Asks ??= [];
        return book;
    }

    private static async Task SaveAsync(string path, PumpBook book, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(book, Json), cancellationToken);
    }

    private static string StatePath(string root) =>
        Path.Combine(root, "artifacts", "data", "pump-shorts", "state.json");

    private static string FindRepo(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "TradingPlatform.Api", "data", "futures-universe.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return start;
    }

    private static async Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed class PumpBook
    {
        public List<PumpTicket> Tickets { get; set; } = [];

        public List<PumpAskRecord> Asks { get; set; } = [];
    }

    private sealed class PumpTicket
    {
        public string Symbol { get; set; } = string.Empty;
        public string Day { get; set; } = string.Empty;
        public decimal PumpHigh { get; set; }
        public decimal Entry { get; set; }
        public DateTimeOffset EnteredAt { get; set; }
        public string ClientOrderId { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal BestMark { get; set; }
        public DateTimeOffset BestMarkAt { get; set; }

        /// <summary>Lowest mark whose 5-minute bar has closed. The follow stop is measured from this, not from a low that is still printing.</summary>
        public decimal TrailMark { get; set; }
        public decimal DisasterStop { get; set; }
        public decimal RestingStop { get; set; }
        public int StopRevision { get; set; }
        public bool StopExposed { get; set; }
        public decimal? RealizedPnl { get; set; }
        public bool Closed { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string? CloseReason { get; set; }

        /// <summary>The 24-hour rise has printed back under 150% since this short finished.</summary>
        public bool Cooled { get; set; }

        /// <summary>The stop hit before the drop started, so this high is still open for one more short.</summary>
        public bool WickRetry { get; set; }

        /// <summary>This short is that one retry. A second wick closes the high.</summary>
        public bool FollowsWick { get; set; }
    }

    private async Task RememberPnlAsync(IExchangeConnector connector, PumpBook book, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var symbols = book.Tickets
            .Where(ticket => ticket.Closed && (ticket.RealizedPnl is null || ticket.ClosedAt is null) && !string.IsNullOrWhiteSpace(ticket.Symbol))
            .Select(ticket => ticket.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        foreach (var symbol in symbols)
        {
            var fills = await connector.RecentFillPnlAsync(symbol, cancellationToken);
            if (fills is null)
            {
                continue;
            }

            var tickets = book.Tickets
                .Where(ticket => ticket.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                .OrderBy(ticket => ticket.EnteredAt)
                .ToList();
            for (var index = 0; index < tickets.Count; index++)
            {
                var ticket = tickets[index];
                if (!ticket.Closed || (ticket.RealizedPnl is not null && ticket.ClosedAt is not null))
                {
                    continue;
                }

                var from = ticket.EnteredAt;
                if (ticket.Quantity <= 0m)
                {
                    var quantity = PumpRealizedPnl.EntryQuantity(fills, from);
                    if (quantity > 0m)
                    {
                        ticket.Quantity = quantity;
                    }
                }

                if (PumpRealizedPnl.TryClose(fills, from, ticket.Quantity, out var close))
                {
                    if (ticket.RealizedPnl is not { } previous || previous != close.Realized)
                    {
                        _logger.LogInformation(
                            "UnexplainedPump {Symbol} result is {Pnl} from the fills that closed this short.",
                            ticket.Symbol,
                            close.Realized);
                    }

                    ticket.RealizedPnl = close.Realized;
                    ticket.ClosedAt = close.ClosedAt;
                }
                else if (ticket.RealizedPnl is not null)
                {
                    ticket.ClosedAt ??= now;
                }
            }
        }
    }

    private static async Task PublishAsync(
        string root,
        PumpBook book,
        LiveAccountSnapshot account,
        List<UnexplainedPumpCheckRow> fresh,
        bool enabled,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previous = UnexplainedPumpDeskStore.Read(root);
        var desk = new UnexplainedPumpDeskFile
        {
            Enabled = enabled,
            LastScanUtc = now,
            Positions = book.Tickets
                .Select(ticket =>
                {
                    var live = account.OpenPositions.FirstOrDefault(position =>
                        position.Symbol.Equals(ticket.Symbol, StringComparison.OrdinalIgnoreCase) && position.Side == "Short");
                    return new UnexplainedPumpPositionRow
                    {
                        Coin = ticket.Symbol,
                        Side = "Short",
                        Entry = ticket.Entry,
                        PumpHigh = ticket.PumpHigh,
                        Mark = live?.MarkPrice,
                        OpenedAt = ticket.EnteredAt,
                        Open = !ticket.Closed,
                        CloseReason = ticket.CloseReason,
                        Quantity = ticket.Quantity > 0m ? ticket.Quantity : live?.Quantity ?? 0m,
                        Pnl = ticket.Closed ? ticket.RealizedPnl : live?.UnrealizedPnL,
                        PnlKnown = ticket.Closed ? ticket.RealizedPnl is not null : live is not null
                    };
                })
                .OrderByDescending(row => row.Open)
                .ThenByDescending(row => row.OpenedAt)
                .ToList(),
            Decisions = UnexplainedPumpDeskStore.Merge(fresh, previous.Decisions)
        };
        await UnexplainedPumpDeskStore.WriteAsync(root, desk, cancellationToken);
    }
}
