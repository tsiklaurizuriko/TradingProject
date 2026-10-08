using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.News;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.News;
using TradingPlatform.Risk;
using TradingPlatform.Trading;

namespace TradingPlatform.Api.Hosting;

/// <summary>
/// Polls each news provider on its own interval, then scores new events on the trading cadence.
/// A provider failure is recorded and does not stop the others. It does not place orders unless news trading is running.
/// </summary>
public sealed class NewsLiveWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _http;
    private readonly IHostEnvironment _environment;
    private readonly IOptionsMonitor<NewsOptions> _options;
    private readonly ILogger<NewsLiveWorker> _logger;
    private readonly NewsPollSchedule _schedule = new();
    private readonly List<NewsEvent> _pending = [];
    private bool _scheduleSeeded;
    private DateTimeOffset _nextTradeUtc = DateTimeOffset.MinValue;

    public NewsLiveWorker(
        IServiceScopeFactory scopes,
        IHttpClientFactory http,
        IHostEnvironment environment,
        IOptionsMonitor<NewsOptions> options,
        ILogger<NewsLiveWorker> logger)
    {
        _scopes = scopes;
        _http = http;
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (!options.Enabled)
            {
                await Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            try
            {
                await CycleAsync(options, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "News collection failed. No order was sent.");
            }

            await Delay(NextWait(options), stoppingToken);
        }
    }

    private async Task CycleAsync(NewsOptions options, CancellationToken cancellationToken)
    {
        var root = FindRepo(_environment.ContentRootPath);
        var catalog = NewsAssetCatalog.LoadUniverse(root);
        var store = new FileNewsStore(Path.Combine(root, options.StorePath));
        var now = DateTimeOffset.UtcNow;
        var from = now.AddHours(-6);
        var http = _http.CreateClient("news");
        http.Timeout = TimeSpan.FromSeconds(30);
        using var scope = _scopes.CreateScope();
        var database = new NewsDatabase(scope.ServiceProvider.GetRequiredService<TradingDbContext>());
        if (!_scheduleSeeded)
        {
            foreach (var health in await database.ProviderHealthAsync(cancellationToken))
            {
                if (health.LastAttemptUtc is DateTimeOffset last)
                {
                    _schedule.Remember(health.Provider, last);
                }

                if (health.NextEligibleUtc is DateTimeOffset next)
                {
                    _schedule.DelayUntil(health.Provider, next);
                }
            }

            _scheduleSeeded = true;
        }

        var due = new List<INewsProvider>();
        foreach (var provider in NewsProviderCatalog.Create(options, http))
        {
            var interval = TimeSpan.FromMinutes(NewsProviderCatalog.PollIntervalMinutes(options, provider.ScheduleKey));
            if (_schedule.TryBegin(provider.Name, now, interval))
            {
                due.Add(provider);
            }
        }

        NewsCollectionResult collected;
        try
        {
            var collector = new NewsCollector(options, due, store, catalog);
            collected = await collector.CollectDetailedAsync(from, now, cancellationToken);
        }
        finally
        {
            var finished = DateTimeOffset.UtcNow;
            foreach (var provider in due)
            {
                _schedule.Complete(provider.Name, finished);
            }
        }

        foreach (var failure in collected.Providers.Where(item => !item.Succeeded))
        {
            _logger.LogWarning("News provider {Provider} failed: {Error}", failure.Provider, failure.Error);
        }

        var schedules = due.ToDictionary(provider => provider.Name, provider => provider.ScheduleKey, StringComparer.Ordinal);
        var reports = collected.Providers.Select(item => WithNextPoll(options, item, schedules.GetValueOrDefault(item.Provider, item.Provider))).Concat(NewsProviderCatalog.DisabledCredentialReports(options, now)).ToList();
        if (collected.Events.Count > 0 || reports.Count > 0)
        {
            await database.SaveIngestionAsync(collected.Events, reports, cancellationToken);
            _pending.AddRange(collected.Events);
            if (collected.Providers.Count > 0)
            {
                var sessionStatus = await database.GetOrCreateSessionAsync(cancellationToken);
                sessionStatus.LastStatus = string.Join("; ", collected.Providers.Select(report => report.Succeeded
                    ? report.Provider + " fetched " + report.Fetched + ", rejected " + report.Rejected
                    : report.Provider + " failed: " + report.Error));
                if (sessionStatus.LastStatus.Length > 500)
                {
                    sessionStatus.LastStatus = sessionStatus.LastStatus[..500];
                }

                await database.SaveAsync(cancellationToken);
            }
        }

        if (now < _nextTradeUtc)
        {
            return;
        }

        if (!await scope.ServiceProvider.GetRequiredService<IWorkerLease>().HoldAsync(WorkerLeaseNames.NewsTrading, cancellationToken))
        {
            _pending.Clear();
            _nextTradeUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, options.PollMinutes));
            _logger.LogInformation("Another host holds the news trading lease. Scoring and signals are left to that host.");
            return;
        }

        var remembered = new NewsPipeline(options, catalog: catalog).Build(
            await database.RecentArticlesAsync(now.AddMinutes(-options.Strategy.MaxNewsAgeMinutes), cancellationToken));
        if (remembered.Count > 0)
        {
            await database.SaveIngestionAsync(remembered, [], cancellationToken);
            _pending.AddRange(remembered);
        }

        var events = _pending
            .GroupBy(item => item.EventId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (options.Ai.Enabled && events.Count > 0)
        {
            var fresh = new List<NewsEvent>(events.Count);
            var alreadyDecided = 0;
            foreach (var item in events)
            {
                if (await database.HasFinalSignalAsync(item.EventId, cancellationToken))
                {
                    alreadyDecided++;
                    continue;
                }

                fresh.Add(item);
            }

            if (alreadyDecided > 0)
            {
                _logger.LogInformation(
                    "NewsDeduplicated {Count} events already have a decision and were not analyzed again.",
                    alreadyDecided);
            }

            events = fresh;
        }
        var session = await database.GetOrCreateSessionAsync(cancellationToken);
        var trading = scope.ServiceProvider.GetRequiredService<IOptions<TradingOptions>>().Value;
        var risk = scope.ServiceProvider.GetRequiredService<IRiskEngine>();
        var profile = await scope.ServiceProvider.GetRequiredService<ITradingStore>().GetNewsRiskAsync(cancellationToken);
        var scoped = events
            .Where(item => item.MarketScope != MarketScope.Global || item.AffectedAssets.Count > 0)
            .ToList();
        if (options.Ai.Enabled)
        {
            await AnalyzeNewsAsync(options, catalog, events, scope.ServiceProvider, database, cancellationToken);
        }

        var decisionTime = DateTimeOffset.UtcNow;
        var market = await LoadMarketAsync(options, scoped, cancellationToken);
        IReadOnlyDictionary<string, NewsMarketFacts> facts = new Dictionary<string, NewsMarketFacts>(StringComparer.OrdinalIgnoreCase);
        if (options.Ai.Enabled)
        {
            facts = await BuildFactsAsync(options, scope.ServiceProvider.GetRequiredService<IPublicMarketDataClient>(), market, events, decisionTime, cancellationToken);
        }

        var report = NewsLiveAnalyzer.Analyze(options, catalog, scoped, market, options.Ai.Enabled ? decisionTime : now);
        if (options.Ai.Enabled)
        {
            foreach (var item in events)
            {
                if (report.Decisions.Any(row => (row.EventId ?? row.Record?.EventId) == item.EventId))
                {
                    continue;
                }

                report.Decisions.Add(NewsTradeDecisionEngine.Decide(
                    item,
                    new NewsAssetContext("UNKNOWN", string.Empty),
                    decisionTime,
                    new Dictionary<string, IReadOnlyList<MarketCandle>>(),
                    NewsMarketFacts.Missing("UNKNOWN"),
                    options,
                    new NewsDecisionContext()));
            }
        }

        foreach (var item in events.Where(item => item.MarketScope == MarketScope.Global && item.AffectedAssets.Count == 0))
        {
            report.Errors.Add("Global event " + item.EventId + " was stored. It was not scored against every coin in this cycle.");
        }

        if (session.Running)
        {
            session.Mode = "Live";
            await database.SaveAsync(cancellationToken);
        }

        IExchangeConnector? connector = null;
        Guid? liveAccountId = null;
        if (session.Running)
        {
            var admin = await scope.ServiceProvider.GetRequiredService<ITradingStore>().GetFirstAdminAsync(cancellationToken);
            var account = await scope.ServiceProvider.GetRequiredService<IExchangeCredentialStore>().GetLiveAccountAsync(admin.Id, cancellationToken);
            if (account is null)
            {
                report.Errors.Add("News trading is running but no live Binance account is saved. No order was sent.");
            }
            else
            {
                liveAccountId = account.Id;
                connector = scope.ServiceProvider.GetRequiredService<IExchangeConnectorFactory>().Create(TradingMode.Live, account.Id);
            }
        }

        var reconciliation = scope.ServiceProvider.GetRequiredService<ReconciliationState>();
        var tradingStore = scope.ServiceProvider.GetRequiredService<ITradingStore>();
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var accountSnapshot = scope.ServiceProvider.GetRequiredService<ILiveAccountCache>().Current;
        var openLoss = Math.Min(0m, accountSnapshot.OpenPositions.Sum(row => row.UnrealizedPnL));
        decimal? drawdown = null;
        if (session.Running && liveAccountId is { } accountId)
        {
            var (peak, lastPointAt) = await tradingStore.GetEquityPeakAsync(accountId, TradingMode.Live, now.AddDays(-Math.Max(1, trading.EquityPeakLookbackDays)), cancellationToken);
            drawdown = BotEngine.DrawdownPercent(peak, lastPointAt, accountSnapshot.FuturesEquity, now);
        }

        var live = new NewsLiveContext(
            accountSnapshot,
            scope.ServiceProvider.GetRequiredService<IPublicMarketDataClient>(),
            session.Running ? await tradingStore.SumClosedPnLSinceForModeAsync(TradingMode.Live, dayStart, cancellationToken) + openLoss : 0m,
            session.Running ? await tradingStore.GetLossStreakForModeAsync(TradingMode.Live, cancellationToken) : (0, null),
            session.Running ? await tradingStore.SumClosedPnLSinceForModeAsync(TradingMode.Live, BotEngine.WeekStart(now), cancellationToken) + openLoss : 0m,
            drawdown);
        await PersistSignalsAsync(database, events, report, session, profile, risk, connector, live, options.Ai.Enabled ? decisionTime : now, options, trading, reconciliation, market, facts, cancellationToken);
        if (options.Ai.Enabled && connector is not null)
        {
            await CloseExpiredHorizonsAsync(database, connector, accountSnapshot, decisionTime, cancellationToken);
        }

        report.SessionRunning = session.Running;
        await NewsLiveAnalyzer.WriteAsync(root, report, options.Strategy.Timeframes.Execution, cancellationToken);
        var candidates = report.Decisions.Count(item => item.Signal != NewsMarketSignals.NoTrade);
        _pending.Clear();
        _nextTradeUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, options.PollMinutes));
        _logger.LogInformation(
            "News cycle stored {Events} events and {Candidates} signals in the database. Approved live signals are sent to Binance USD-M.",
            events.Count,
            candidates);
    }

    private async Task PersistSignalsAsync(
        NewsDatabase database,
        IReadOnlyList<NewsEvent> events,
        NewsLiveReport report,
        NewsTradingSession session,
        RiskProfile profile,
        IRiskEngine risk,
        IExchangeConnector? connector,
        NewsLiveContext live,
        DateTimeOffset now,
        NewsOptions options,
        TradingOptions trading,
        ReconciliationState reconciliation,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>> marketBooks,
        IReadOnlyDictionary<string, NewsMarketFacts> facts,
        CancellationToken cancellationToken)
    {
        var eventsById = events.ToDictionary(item => item.EventId, StringComparer.Ordinal);
        var (equity, available, equityBlock) = NewsTradeAdapter.LiveEquity(live.Account, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        var filters = new Dictionary<string, RankedUsdtSpotSymbol>(StringComparer.OrdinalIgnoreCase);
        if (session.Running)
        {
            foreach (var row in await live.Market.GetPaperUniverseAsync(cancellationToken))
            {
                filters[row.Symbol] = row;
            }
        }
        var approved = await database.ApprovedOpenCountAsync(cancellationToken);
        var analyses = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var finals = new List<NewsMarketDecision>();
        foreach (var initial in report.Decisions)
        {
            var decision = initial;
            var eventId = decision.EventId ?? decision.Record?.EventId ?? "";
            if (!eventsById.TryGetValue(eventId, out var source)
                && !events.Any())
            {
                continue;
            }

            source ??= events.FirstOrDefault(item => item.AffectedAssets.Any(asset =>
                string.Equals(asset.Symbol, decision.Symbol, StringComparison.OrdinalIgnoreCase)
                || string.Equals(asset.BaseAsset + "USDT", decision.Symbol, StringComparison.OrdinalIgnoreCase)));
            if (source is null)
            {
                continue;
            }

            var stored = await database.AddEventAsync(new StoredNewsEvent
            {
                DedupKey = source.EventId,
                PrimaryAsset = source.PrimaryAsset,
                MarketScope = source.MarketScope.ToString(),
                Direction = source.Direction.ToString(),
                Impact = source.ImpactScore,
                Confidence = source.ConfidenceScore,
                EventType = source.EventType.ToString(),
                PublishedAtUtc = source.PublishedAtUtc,
                ArticleIds = string.Join(',', source.OriginalArticles.Select(article => article.Id))
            }, cancellationToken);
            if (await database.HasCommittedSignalAsync(stored.Id, decision.Symbol, cancellationToken))
            {
                LogNews(source, decision.Symbol, "NO_TRADE", NewsRejection.EventAlreadyTraded, "NewsTradeRejected");
                continue;
            }

            var positionOpen = live.Account.OpenPositions.Any(p => string.Equals(p.Symbol, decision.Symbol, StringComparison.OrdinalIgnoreCase))
                || live.Account.OpenOrders.Any(p => string.Equals(p.Symbol, decision.Symbol, StringComparison.OrdinalIgnoreCase));
            var cooldown = await database.SymbolOccupiedAsync(
                decision.Symbol,
                NewsTradeAdapter.Decision(decision.Signal),
                now.AddMinutes(-options.Strategy.SignalCooldownMinutes),
                cancellationToken);
            if (options.Ai.Enabled)
            {
                marketBooks.TryGetValue(decision.Symbol, out var book);
                facts.TryGetValue(decision.Symbol, out var fact);
                decision = NewsTradeDecisionEngine.Decide(
                    source,
                    new NewsAssetContext(decision.Symbol, NewsAssetCatalog.BaseFromSymbol(decision.Symbol)),
                    now,
                    book ?? new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase),
                    fact ?? NewsMarketFacts.Missing(decision.Symbol),
                    options,
                    new NewsDecisionContext(false, positionOpen, cooldown, false));
                LogNews(source, decision.Symbol, NewsTradeAdapter.Decision(decision.Signal), decision.RejectionCode, decision.Signal == NewsMarketSignals.NoTrade ? "NewsTradeRejected" : "NewsDecisionCreated");
            }

            finals.Add(decision);
            var proposed = NewsTradeAdapter.Decision(decision.Signal);
            var direction = proposed;
            var occupied = positionOpen || cooldown;
            var price = decision.Record?.ReferencePrice ?? 0m;
            DateTimeOffset? priceAt = null;
            if (session.Running && direction != "NO_TRADE")
            {
                try
                {
                    var fresh = await live.Market.GetLastPriceAsync(decision.Symbol, cancellationToken);
                    if (fresh > 0m)
                    {
                        price = fresh;
                        priceAt = DateTimeOffset.UtcNow;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Age below stays at the candle close, which the risk check treats as stale.
                }
            }

            filters.TryGetValue(decision.Symbol, out var filter);
            var snapshot = new RiskSnapshot
            {
                Equity = equity,
                AvailableBalance = available,
                Symbol = decision.Symbol,
                Price = price,
                AccountDailyPnL = live.DailyRealizedPnl,
                AccountWeeklyPnL = live.WeeklyRealizedPnl,
                DrawdownPercent = live.DrawdownPercent,
                SymbolAlreadyOpen = occupied,
                OpenPositionCount = approved,
                MarketDataAgeMs = decision.Record?.ReferencePriceAt is { } candleClose
                    && TimeframeExtensions.TryParseInterval(options.Strategy.Timeframes.Execution, out var executionTimeframe)
                        ? MarketDataAge.Milliseconds(DateTimeOffset.UtcNow, priceAt, candleClose, executionTimeframe.ToDuration())
                        : int.MaxValue,
                Sizing = new RiskSizingHints
                {
                    StepSize = filter?.StepSize ?? 0m,
                    MinQuantity = filter?.MinQuantity ?? 0m,
                    MinNotional = filter?.MinNotional ?? 5m,
                    QuantityPrecision = filter?.QuantityPrecision ?? 0,
                    TakerFeePercent = connector is null
                        ? RiskEngine.DefaultTakerFeePercent
                        : await connector.GetTakerFeePercentAsync(decision.Symbol, cancellationToken) ?? RiskEngine.DefaultTakerFeePercent,
                    SlippagePercent = RiskEngine.DefaultSlippagePercent
                }
            };
            var liveOn = trading.EntriesEnabled && session.Running && string.Equals(session.Mode, "Live", StringComparison.OrdinalIgnoreCase);
            var handoff = direction == "NO_TRADE"
                ? NewsTradeAdapter.Handoff(direction, 1m, 0.01m, profile, snapshot, risk, now, false, false)
                : price <= 0m
                    ? NewsTradeAdapter.Rejected(direction, "No reference price.")
                    : session.Running && equityBlock is not null
                        ? NewsTradeAdapter.Rejected(direction, equityBlock)
                        : session.Running && filter is null
                            ? NewsTradeAdapter.Rejected(direction, $"{decision.Symbol} has no Binance USD-M filters. No order.")
                            : NewsTradeAdapter.Handoff(
                                direction,
                                price,
                                filter?.TickSize ?? 0.01m,
                                profile,
                                snapshot,
                                risk,
                                now,
                                liveOn,
                                session.Running,
                                NewsTradeAdapter.ClientOrderId(source.EventId, decision.Symbol));

            if (session.Running && handoff.RiskDecision == "Approved")
            {
                var liveBlock = RiskLiveGuard.Reject(
                    TradingMode.Live,
                    profile,
                    new LiveRiskFacts(
                        equity,
                        0m,
                        Math.Max(approved, live.Account.OpenPositions.Count),
                        occupied ? 1 : 0,
                        live.DailyRealizedPnl,
                        profile.RiskPerTradePercent,
                        handoff.Leverage,
                        profile.StopLossPercent,
                        handoff.Quantity,
                        handoff.Notional,
                        filter?.MinQuantity ?? 0m,
                        filter?.MinNotional ?? 0m,
                        equityBlock is null ? available : null,
                        handoff.Margin,
                        trading.KillSwitchEnabled,
                        live.Streak.ConsecutiveLosses,
                        live.Streak.LastLossAt,
                        now,
                        profile.MaxDrawdownPercent <= 0m || live.DrawdownPercent is not null,
                        live.WeeklyRealizedPnl,
                        live.DrawdownPercent));
                if (liveBlock is not null)
                {
                    handoff = NewsTradeAdapter.Rejected(direction, liveBlock);
                }
            }

            if (handoff.RiskDecision == "Approved")
            {
                approved++;
            }

            var rejection = decision.RejectionCode;
            if (options.Ai.Enabled && handoff.RiskDecision == "Rejected")
            {
                rejection = NewsRejection.FromRisk(handoff.RiskReason);
                decision = decision with { RejectionCode = rejection, Signal = NewsMarketSignals.NoTrade };
                LogNews(source, decision.Symbol, "NO_TRADE", rejection, "NewsTradeRejected");
                direction = "NO_TRADE";
            }
            else if (options.Ai.Enabled && handoff.RiskDecision == "Approved" && direction is "LONG" or "SHORT")
            {
                LogNews(source, decision.Symbol, direction, null, "NewsTradeApproved");
            }

            string? exchangeId = null;
            DateTimeOffset? orderRequested = null;
            DateTimeOffset? orderAccepted = null;
            DateTimeOffset? orderFilled = null;
            decimal fees = 0m;
            decimal filledQuantity = handoff.Quantity;
            if (handoff.Request is not null && connector is not null && handoff.OrderDecision == "READY")
            {
                var leverage = (int)Math.Max(1m, Math.Floor(handoff.Leverage));
                await connector.PrepareSymbolRiskAsync(decision.Symbol, MarginMode.Isolated, leverage, cancellationToken);
                orderRequested = DateTimeOffset.UtcNow;
                LogNews(source, decision.Symbol, proposed, null, "OrderSubmitted");
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
                orderAccepted = DateTimeOffset.UtcNow;
                exchangeId = fill?.ExchangeOrderId;
                LogNews(source, decision.Symbol, proposed, exchangeId, fill is null ? "NewsTradeRejected" : "OrderAccepted");
                if (fill is not null && fill.FilledQuantity > 0m)
                {
                    filledQuantity = fill.FilledQuantity;
                    fees = fill.FeeKnown ? fill.Fee : 0m;
                    orderFilled = fill.ExchangeTimestamp ?? orderAccepted;
                    if (NewsLatency.HasLookAhead(source.PublishedAtUtc, source.DetectedAtUtc, source.ClassifiedAtUtc ?? now, now, orderFilled))
                    {
                        orderFilled = orderAccepted;
                    }

                    LogNews(source, decision.Symbol, proposed, null, "OrderFilled");
                    LogNews(source, decision.Symbol, proposed, null, "PositionOpened");
                    var closeSide = proposed == "SHORT" ? OrderSide.Buy : OrderSide.Sell;
                    var stops = handoff.StopLossPrice > 0m
                        ? await connector.PlaceClosePositionStopsAsync(
                            decision.Symbol,
                            closeSide,
                            handoff.StopLossPrice,
                            handoff.TakeProfitPrice,
                            handoff.Request.ClientOrderId + "-sl",
                            handoff.Request.ClientOrderId + "-tp",
                            cancellationToken)
                        : new ProtectiveStopsResult(false, false, "No stop price.");
                    if (!stops.HasWorkingStop)
                    {
                        await connector.PlaceOrderAsync(
                            new PlaceOrderRequest(
                                handoff.Request.ClientOrderId + "-x",
                                decision.Symbol,
                                closeSide,
                                OrderType.Market,
                                fill.FilledQuantity,
                                null,
                                TimeSpan.FromSeconds(5),
                                ReduceOnly: true),
                            cancellationToken);
                        handoff = handoff with { RiskReason = $"Stop failed ({stops.StopError}). The fill was closed with a reduce-only order." };
                        LogNews(source, decision.Symbol, proposed, stops.StopError, "PositionClosed");
                    }
                    else
                    {
                        LogNews(source, decision.Symbol, proposed, null, "PositionProtected");
                    }
                }

                handoff = handoff with { OrderDecision = fill is null ? "NOT_SENT" : fill.Status.ToString() };
            }

            var why = NewsStop.Explain(direction, decision.Reason, handoff.RiskReason, handoff.RiskDecision, handoff.OrderDecision, exchangeId is not null);
            if (options.Ai.Enabled)
            {
                await SaveAiDecisionAsync(database, analyses, stored, source, decision, facts, proposed, direction, rejection, handoff, price, filledQuantity, fees, orderRequested, orderAccepted, orderFilled, exchangeId, now, cancellationToken);
            }

            if (options.Ai.Enabled && NewsRejection.IsRetryable(rejection))
            {
                continue;
            }

            await database.AddSignalAsync(new NewsTradingSignal
            {
                StoredNewsEventId = stored.Id,
                Symbol = decision.Symbol,
                SignalTimeUtc = now,
                Direction = direction,
                NewsScore = decision.NewsScore,
                MarketScore = decision.MarketScore,
                FinalScore = decision.FinalScore,
                NewsImpact = source.ImpactScore,
                NewsConfidence = source.ConfidenceScore,
                Relevance = source.AffectedAssets.FirstOrDefault(asset => string.Equals(asset.Symbol, decision.Symbol, StringComparison.OrdinalIgnoreCase))?.Relevance
                    ?? source.AffectedAssets.FirstOrDefault(asset => string.Equals(asset.BaseAsset, decision.Symbol.Replace("USDT", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase))?.Relevance
                    ?? 0,
                StrategyName = NewsTradeAdapter.StrategyName,
                Reason = why,
                MarketDetail = decision.Reason,
                RiskDecision = handoff.RiskDecision,
                RiskReason = why,
                EntryPrice = price,
                Quantity = handoff.Quantity,
                Notional = handoff.Notional,
                Leverage = handoff.Leverage,
                Margin = handoff.Margin,
                StopLossPrice = handoff.StopLossPrice,
                TakeProfitPrice = handoff.TakeProfitPrice,
                OrderClientId = handoff.Request?.ClientOrderId,
                OrderDecision = handoff.OrderDecision,
                ExchangeOrderId = exchangeId,
                BecameTrade = exchangeId is not null
            }, cancellationToken);
        }

        if (options.Ai.Enabled)
        {
            report.Decisions = finals;
        }
    }

    private async Task<Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>>> LoadMarketAsync(
        NewsOptions options,
        IReadOnlyList<NewsEvent> events,
        CancellationToken cancellationToken)
    {
        var symbols = events
            .SelectMany(item => item.AffectedAssets.Select(asset => asset.Symbol)
                .Concat(item.Assets.Select(asset => asset + "USDT")))
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var frames = new[]
        {
            options.Strategy.Timeframes.Execution,
            options.Strategy.Timeframes.Flow,
            options.Strategy.Timeframes.Structure,
            options.Strategy.Timeframes.Trend,
            options.Ai.Enabled ? "1m" : string.Empty
        }.Where(frame => !string.IsNullOrWhiteSpace(frame)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (options.Ai.Enabled && symbols.Count > 0 && !symbols.Contains("BTCUSDT", StringComparer.OrdinalIgnoreCase))
        {
            symbols.Add("BTCUSDT");
        }
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>>(StringComparer.OrdinalIgnoreCase);
        if (symbols.Count == 0)
        {
            return result;
        }

        using var scope = _scopes.CreateScope();
        var market = scope.ServiceProvider.GetRequiredService<IPublicMarketDataClient>();
        foreach (var symbol in symbols)
        {
            var book = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase);
            foreach (var timeframe in frames)
            {
                if (!TimeframeExtensions.TryParseInterval(timeframe, out var parsed))
                {
                    continue;
                }

                try
                {
                    var candles = await market.GetClosedKlinesAsync(symbol, parsed, 80, cancellationToken);
                    if (candles.Count > 0)
                    {
                        book[timeframe] = candles;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "News market candles were unavailable for {Symbol} {Timeframe}", symbol, timeframe);
                }
            }

            result[symbol] = book;
        }

        return result;
    }

    private NewsProviderReport WithNextPoll(NewsOptions options, NewsProviderReport report, string scheduleKey)
    {
        var interval = TimeSpan.FromMinutes(NewsProviderCatalog.PollIntervalMinutes(options, scheduleKey));
        var wait = !report.Succeeded && NewsBackoff.IsRateLimited(report.Error)
            ? NewsBackoff.Delay(report.Provider, interval)
            : interval;
        var next = report.AttemptedAtUtc.Add(wait);
        if (wait > interval)
        {
            _schedule.DelayUntil(report.Provider, next);
        }

        return report with { Enabled = true, NextEligibleUtc = next };
    }

    private TimeSpan NextWait(NewsOptions options)
    {
        var now = DateTimeOffset.UtcNow;
        var untilTrade = _nextTradeUtc <= now ? TimeSpan.Zero : _nextTradeUtc - now;
        var providers = NewsProviderCatalog.ScheduledSources(options)
            .Select(source => (source.Name, TimeSpan.FromMinutes(NewsProviderCatalog.PollIntervalMinutes(options, source.ScheduleKey))));
        var untilProvider = _schedule.TimeUntilNext(now, providers);
        var wait = untilTrade < untilProvider ? untilTrade : untilProvider;
        if (wait < TimeSpan.FromSeconds(15))
        {
            wait = TimeSpan.FromSeconds(15);
        }

        if (wait > TimeSpan.FromMinutes(5))
        {
            wait = TimeSpan.FromMinutes(5);
        }

        return wait;
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

    private static void MarkNotACoin(NewsEvent item)
    {
        item.AnalysisStatus = "NoCoin";
        item.AnalysisError = "This news is not about a Binance coin. It was not sent to the model.";
        item.ShouldConsiderTrading = false;
    }

    private async Task AnalyzeNewsAsync(NewsOptions options, NewsAssetCatalog catalog, IReadOnlyList<NewsEvent> events, IServiceProvider services, NewsDatabase database, CancellationToken cancellationToken)
    {
        var errors = options.Ai.Validate();
        if (errors.Count > 0 || !options.Ai.HasKey())
        {
            var reason = errors.Count > 0 ? string.Join(" ", errors) : "News AI is enabled but News:Ai:ApiKey is empty.";
            foreach (var item in events)
            {
                if (!NewsAssetCatalog.NamesListedCoin(item))
                {
                    MarkNotACoin(item);
                    LogNews(item, string.Empty, "NO_TRADE", NewsRejection.UnknownAsset, "NewsNotACoin");
                    continue;
                }

                DeepNewsAnalyzer.MarkFailed(item, "Failed", reason, DateTimeOffset.UtcNow, options.Ai.Provider, options.Ai.FastModel, options.Ai.PromptVersion);
                LogNews(item, item.PrimaryAsset ?? string.Empty, "NO_TRADE", reason, "NewsAiFailed");
            }

            return;
        }

        var analyzer = new DeepNewsAnalyzer(options, services.GetRequiredService<INewsLanguageModel>(), _logger);
        var keys = events.Select(item => item.EventId).Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal).ToList();
        var completed = await database.CompletedAnalysisRawAsync(keys, cancellationToken);
        var clock = DateTimeOffset.UtcNow;
        foreach (var item in events)
        {
            if (await database.HasFinalSignalAsync(item.EventId, cancellationToken))
            {
                continue;
            }

            if (!NewsAssetCatalog.NamesListedCoin(item))
            {
                MarkNotACoin(item);
                LogNews(item, string.Empty, "NO_TRADE", NewsRejection.UnknownAsset, "NewsNotACoin");
                continue;
            }

            if (!NewsAiGate.ShouldAnalyze(item.PublishedAtUtc, clock, options.Strategy.MaxNewsAgeMinutes, alreadyAnalyzed: false))
            {
                item.AnalysisStatus = "Skipped";
                item.AnalysisError = "The story is outside the age limit. It was not sent to the model.";
                LogNews(item, item.PrimaryAsset ?? string.Empty, "NO_TRADE", NewsRejection.NewsTooOld, "NewsDeduplicated");
                continue;
            }

            if (completed.TryGetValue(item.EventId, out var raw)
                && DeepNewsAnalyzer.TryRestore(item, raw, catalog, item.ClassifiedAtUtc ?? clock, options))
            {
                continue;
            }

            LogNews(item, item.PrimaryAsset ?? string.Empty, string.Empty, null, "NewsReceived");
            try
            {
                await analyzer.AnalyzeAsync(item, catalog, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                DeepNewsAnalyzer.MarkFailed(item, "Failed", ex.Message, DateTimeOffset.UtcNow, options.Ai.Provider, options.Ai.FastModel, options.Ai.PromptVersion);
                LogNews(item, item.PrimaryAsset ?? string.Empty, "NO_TRADE", ex.Message, "NewsAiFailed");
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, NewsMarketFacts>> BuildFactsAsync(
        NewsOptions options,
        IPublicMarketDataClient market,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>> books,
        IReadOnlyList<NewsEvent> events,
        DateTimeOffset decisionTime,
        CancellationToken cancellationToken)
    {
        var symbols = books.Keys.ToList();
        var tickers = new Dictionary<string, FuturesBookTicker>(StringComparer.OrdinalIgnoreCase);
        var funding = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var volume = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var row in await market.GetBookTickersAsync(cancellationToken))
            {
                tickers[row.Symbol] = row;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "News market book was unavailable.");
        }

        try
        {
            foreach (var row in await market.GetPremiumIndexAsync(cancellationToken))
            {
                funding[row.Symbol] = row.LastFundingRate;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "News funding was unavailable.");
        }

        try
        {
            foreach (var row in await market.GetPaperUniverseAsync(cancellationToken))
            {
                volume[row.Symbol] = row.QuoteVolume;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "News liquidity was unavailable.");
        }

        var oi = new ConcurrentDictionary<string, (decimal? Previous, decimal? Latest)>(StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(symbols, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (symbol, token) =>
        {
            try
            {
                oi[symbol] = await market.GetOpenInterestPairAsync(symbol, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Open interest was unavailable for {Symbol}", symbol);
            }
        });

        books.TryGetValue("BTCUSDT", out var btcBook);
        var btc = NewsMarketFactsBuilder.FromCandles("BTCUSDT", btcBook, decisionTime, decisionTime, options);
        var result = new Dictionary<string, NewsMarketFacts>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            tickers.TryGetValue(symbol, out var book);
            oi.TryGetValue(symbol, out var interest);
            var extras = new NewsMarketExtras(
                book?.Bid,
                book?.Ask,
                volume.TryGetValue(symbol, out var quote) ? quote : null,
                interest.Latest,
                interest.Previous,
                funding.TryGetValue(symbol, out var rate) ? rate : null,
                null,
                true,
                true,
                true);
            var detected = events
                .Where(item => item.AffectedAssets.Any(asset => string.Equals(asset.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    || item.Assets.Any(asset => string.Equals(asset + "USDT", symbol, StringComparison.OrdinalIgnoreCase)))
                .Select(item => item.DetectedAtUtc)
                .DefaultIfEmpty(decisionTime)
                .Min();
            result[symbol] = NewsMarketFactsBuilder.FromCandles(symbol, books[symbol], decisionTime, detected, options, extras, btc);
            _logger.LogInformation(
                "{Stage} {EventId} {Symbol} {Decision} {Confidence} {Impact} {Latency} {Model} {Reason}",
                "MarketContextCreated",
                string.Empty,
                symbol,
                string.Empty,
                0,
                0,
                0,
                options.Ai.FastModel,
                result[symbol].MissingRequired ? "missing" : "ready");
        }

        return result;
    }

    private async Task SaveAiDecisionAsync(
        NewsDatabase database,
        Dictionary<string, Guid> analyses,
        StoredNewsEvent stored,
        NewsEvent source,
        NewsMarketDecision decision,
        IReadOnlyDictionary<string, NewsMarketFacts> facts,
        string proposed,
        string direction,
        string? rejection,
        NewsRiskHandoff handoff,
        decimal price,
        decimal quantity,
        decimal fees,
        DateTimeOffset? orderRequested,
        DateTimeOffset? orderAccepted,
        DateTimeOffset? orderFilled,
        string? exchangeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!analyses.TryGetValue(source.EventId, out var analysisId))
        {
            var classified = source.ClassifiedAtUtc ?? now;
            var analysis = new NewsAnalysis
            {
                StoredNewsEventId = stored.Id,
                EventDedupKey = source.EventId,
                Provider = source.AiProvider ?? string.Empty,
                Model = source.AiModel ?? string.Empty,
                PromptVersion = source.PromptVersion ?? string.Empty,
                Status = string.IsNullOrWhiteSpace(source.AnalysisStatus) ? "Failed" : source.AnalysisStatus,
                EventType = source.EventType.ToString(),
                VerificationStatus = source.VerificationStatus,
                SourceReliability = source.SourceReliability,
                OverallConfidence = (int)Math.Round(source.ConfidenceScore * 100),
                Impact = (int)Math.Round(source.ImpactScore * 100),
                Novelty = (int)Math.Round(source.NoveltyScore * 100),
                AlreadyPricedIn = source.AlreadyPricedIn,
                ExpectedHorizon = source.ExpectedHorizonMinutes + "m",
                MarketMechanism = source.MarketMechanism,
                AffectedAssetsJson = JsonSerializer.Serialize(source.AffectedAssets),
                RiskFlagsJson = JsonSerializer.Serialize(source.RiskFlags),
                ShouldConsiderTrading = source.ShouldConsiderTrading,
                RawJson = source.Reason,
                ArticleIds = string.Join(',', source.OriginalArticles.Select(article => article.Id)),
                PublishedAtUtc = source.PublishedAtUtc,
                DetectedAtUtc = source.DetectedAtUtc,
                ClassifiedAtUtc = classified,
                DetectionLatencyMs = NewsLatency.Milliseconds(source.PublishedAtUtc, source.DetectedAtUtc),
                ClassificationLatencyMs = NewsLatency.Milliseconds(source.DetectedAtUtc, classified)
            };
            await database.AddAnalysisAsync(analysis, cancellationToken);
            analysisId = analysis.Id;
            analyses[source.EventId] = analysisId;
        }

        facts.TryGetValue(decision.Symbol, out var fact);
        var row = new NewsTradingDecision
        {
            StoredNewsEventId = stored.Id,
            NewsAnalysisId = analysisId,
            EventDedupKey = source.EventId,
            Symbol = decision.Symbol,
            Decision = direction,
            ProposedDirection = proposed,
            RejectionReason = rejection,
            Confidence = (int)Math.Round(source.ConfidenceScore * 100),
            Impact = (int)Math.Round(source.ImpactScore * 100),
            Novelty = (int)Math.Round(source.NoveltyScore * 100),
            AlreadyPricedIn = source.AlreadyPricedIn,
            ExpectedHorizon = source.ExpectedHorizonMinutes + "m",
            MarketContextJson = JsonSerializer.Serialize(fact),
            ArticleIds = string.Join(',', source.OriginalArticles.Select(article => article.Id)),
            RiskDecision = handoff.RiskDecision,
            RiskReason = handoff.RiskReason,
            PublishedAtUtc = source.PublishedAtUtc,
            DetectedAtUtc = source.DetectedAtUtc,
            ClassifiedAtUtc = source.ClassifiedAtUtc,
            DecisionAtUtc = now
        };
        await database.AddTradingDecisionAsync(row, cancellationToken);
        await database.AddDecisionAuditAsync(new NewsDecisionAudit
        {
            EventDedupKey = source.EventId,
            Symbol = decision.Symbol,
            Decision = direction,
            RejectionReason = rejection,
            Provider = source.AiProvider ?? string.Empty,
            Model = source.AiModel ?? string.Empty,
            PromptVersion = source.PromptVersion ?? string.Empty,
            DecisionAtUtc = now,
            PayloadJson = JsonSerializer.Serialize(new
            {
                source.EventId,
                articleIds = source.OriginalArticles.Select(article => article.Id).ToArray(),
                source.PublishedAtUtc,
                source.DetectedAtUtc,
                source.ClassifiedAtUtc,
                decisionAt = now,
                orderRequested,
                orderAccepted,
                orderFilled,
                source.AiProvider,
                source.AiModel,
                source.PromptVersion,
                eventType = source.EventType.ToString(),
                source.VerificationStatus,
                source.AffectedAssets,
                proposed,
                direction,
                source.ConfidenceScore,
                source.ImpactScore,
                source.NoveltyScore,
                source.AlreadyPricedIn,
                source.ExpectedHorizonMinutes,
                market = fact,
                rejection,
                handoff.RiskDecision,
                exchangeId,
                entry = price,
                quantity,
                fees,
                newsToFillLatencyMs = orderFilled is { } filled ? NewsLatency.Milliseconds(source.PublishedAtUtc, filled) : (long?)null
            })
        }, cancellationToken);

        if (handoff.Request is null)
        {
            return;
        }

        await database.AddTradeExecutionAsync(new NewsTradeExecution
        {
            NewsTradingDecisionId = row.Id,
            EventDedupKey = source.EventId,
            Symbol = decision.Symbol,
            Side = proposed,
            ClientOrderId = handoff.Request.ClientOrderId,
            ExchangeOrderId = exchangeId,
            EntryPrice = price,
            Quantity = quantity,
            Fees = fees,
            FundingRate = fact?.FundingRate,
            StopLossPrice = handoff.StopLossPrice,
            TakeProfitPrice = handoff.TakeProfitPrice,
            RiskDecision = handoff.RiskDecision,
            RiskReason = handoff.RiskReason,
            ExpectedHorizonMinutes = source.ExpectedHorizonMinutes,
            OrderRequestedAtUtc = orderRequested,
            OrderAcceptedAtUtc = orderAccepted,
            OrderFilledAtUtc = orderFilled,
            NewsToFillLatencyMs = orderFilled is { } filledAt ? NewsLatency.Milliseconds(source.PublishedAtUtc, filledAt) : null,
            Status = handoff.OrderDecision
        }, cancellationToken);
    }

    private async Task CloseExpiredHorizonsAsync(
        NewsDatabase database,
        IExchangeConnector connector,
        LiveAccountSnapshot account,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var row in await database.ListHorizonExitsAsync(now, cancellationToken))
        {
            var position = account.OpenPositions.FirstOrDefault(item => string.Equals(item.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase));
            if (position is null || position.Quantity <= 0m)
            {
                row.ClosedAtUtc = now;
                row.ExitReason = "flat";
                row.HoldingSeconds = row.OrderFilledAtUtc is { } filled ? (long)(now - filled).TotalSeconds : null;
                continue;
            }

            var closeSide = row.Side == "SHORT" ? OrderSide.Buy : OrderSide.Sell;
            var clientId = string.IsNullOrWhiteSpace(row.ClientOrderId) ? "news-hz" : row.ClientOrderId + "-hz";
            try
            {
                await connector.PlaceOrderAsync(
                    new PlaceOrderRequest(clientId, row.Symbol, closeSide, OrderType.Market, position.Quantity, null, TimeSpan.FromSeconds(5), ReduceOnly: true),
                    cancellationToken);
                row.ClosedAtUtc = now;
                row.ExitReason = "horizon";
                row.HoldingSeconds = row.OrderFilledAtUtc is { } filled ? (long)(now - filled).TotalSeconds : null;
                _logger.LogInformation(
                    "{Stage} {EventId} {Symbol} {Decision} {Confidence} {Impact} {Latency} {Model} {Reason}",
                    "PositionClosed",
                    row.EventDedupKey,
                    row.Symbol,
                    row.Side,
                    0,
                    0,
                    row.HoldingSeconds ?? 0,
                    string.Empty,
                    "horizon");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "News horizon close failed for {Symbol}", row.Symbol);
            }
        }

        await database.SaveAsync(cancellationToken);
    }

    private void LogNews(NewsEvent item, string symbol, string decision, string? reason, string stage) =>
        _logger.LogInformation(
            "{Stage} {EventId} {Symbol} {Decision} {Confidence} {Impact} {Latency} {Model} {Reason}",
            stage,
            item.EventId,
            symbol,
            decision,
            item.ConfidenceScore,
            item.ImpactScore,
            item.ClassifiedAtUtc is { } classified ? NewsLatency.Milliseconds(item.DetectedAtUtc, classified) : 0,
            item.AiModel ?? string.Empty,
            reason ?? string.Empty);

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

    /// <summary>Daily and weekly figures are realized PnL plus open losses, matching the bot engine.</summary>
    private sealed record NewsLiveContext(
        LiveAccountSnapshot Account,
        IPublicMarketDataClient Market,
        decimal DailyRealizedPnl,
        (int ConsecutiveLosses, DateTimeOffset? LastLossAt) Streak,
        decimal WeeklyRealizedPnl = 0m,
        decimal? DrawdownPercent = null);
}
