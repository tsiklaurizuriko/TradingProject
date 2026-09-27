using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        var session = await database.GetOrCreateSessionAsync(cancellationToken);
        var risk = scope.ServiceProvider.GetRequiredService<IRiskEngine>();
        var profile = await scope.ServiceProvider.GetRequiredService<ITradingStore>().GetConservativeRiskAsync(cancellationToken);
        var scoped = events
            .Where(item => item.MarketScope != MarketScope.Global || item.AffectedAssets.Count > 0)
            .ToList();
        var market = await LoadMarketAsync(options, scoped, cancellationToken);
        var report = NewsLiveAnalyzer.Analyze(options, catalog, scoped, market, now);
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
        if (session.Running)
        {
            connector = scope.ServiceProvider.GetRequiredService<IExchangeConnectorFactory>().Create(TradingMode.Live, null);
        }

        await PersistSignalsAsync(database, events, report, session, profile, risk, connector, now, options, cancellationToken);

        await NewsLiveAnalyzer.WriteAsync(root, report, options.Strategy.Timeframes.Execution, cancellationToken);
        var candidates = report.Decisions.Count(item => item.Signal != NewsMarketSignals.NoTrade);
        _pending.Clear();
        _nextTradeUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, options.PollMinutes));
        _logger.LogInformation(
            "News cycle stored {Events} events and {Candidates} signals in the database. Approved live signals are sent to Binance USD-M.",
            events.Count,
            candidates);
    }

    private static async Task PersistSignalsAsync(
        NewsDatabase database,
        IReadOnlyList<NewsEvent> events,
        NewsLiveReport report,
        NewsTradingSession session,
        RiskProfile profile,
        IRiskEngine risk,
        IExchangeConnector? connector,
        DateTimeOffset now,
        NewsOptions options,
        CancellationToken cancellationToken)
    {
        var eventsById = events.ToDictionary(item => item.EventId, StringComparer.Ordinal);
        var approved = await database.ApprovedOpenCountAsync(cancellationToken);
        foreach (var decision in report.Decisions)
        {
            if (!eventsById.TryGetValue(decision.Record?.EventId ?? "", out var source)
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

            var stored = await database.AddEventAsync(new StoredNewsEvent { DedupKey = source.EventId }, cancellationToken);
            if (await database.SignalExistsAsync(stored.Id, decision.Symbol, cancellationToken))
            {
                continue;
            }

            var direction = NewsTradeAdapter.Decision(decision.Signal);
            var occupied = await database.SymbolOccupiedAsync(
                decision.Symbol,
                direction,
                now.AddMinutes(-options.Strategy.SignalCooldownMinutes),
                cancellationToken);
            var price = decision.Record?.ReferencePrice ?? 0m;
            var snapshot = new RiskSnapshot
            {
                Equity = 10_000m,
                AvailableBalance = 10_000m,
                Symbol = decision.Symbol,
                Price = price,
                SymbolAlreadyOpen = occupied,
                OpenPositionCount = approved,
                MarketDataAgeMs = 0,
                Sizing = new RiskSizingHints { StepSize = 0.001m, MinQuantity = 0.001m, MinNotional = 5m, TakerFeePercent = RiskEngine.DefaultTakerFeePercent, SlippagePercent = RiskEngine.DefaultSlippagePercent }
            };
            var handoff = price <= 0m && direction != "NO_TRADE"
                ? new NewsRiskHandoff(direction, "Rejected", "No reference price.", 0, 0, 0, 0, 0, 0, "REJECTED", null)
                : NewsTradeAdapter.Handoff(direction, price <= 0m ? 1m : price, 0.01m, profile, snapshot, risk, now, session.Running && string.Equals(session.Mode, "Live", StringComparison.OrdinalIgnoreCase), session.Running);
            if (direction == "NO_TRADE")
            {
                handoff = NewsTradeAdapter.Handoff(direction, 1m, 0.01m, profile, snapshot, risk, now, false, false);
            }

            if (handoff.RiskDecision == "Approved")
            {
                approved++;
            }

            string? exchangeId = null;
            if (handoff.Request is not null && connector is not null && handoff.OrderDecision == "READY")
            {
                var leverage = (int)Math.Max(1m, Math.Floor(handoff.Leverage));
                await connector.PrepareSymbolRiskAsync(decision.Symbol, MarginMode.Isolated, leverage, cancellationToken);
                var fill = await NewsTradeAdapter.SubmitAsync(connector, handoff.Request, liveTradingEnabled: true, cancellationToken);
                exchangeId = fill?.ExchangeOrderId;
                if (fill is not null && handoff.StopLossPrice > 0m && handoff.TakeProfitPrice > 0m)
                {
                    var closeSide = direction == "SHORT" ? OrderSide.Buy : OrderSide.Sell;
                    await connector.PlaceClosePositionStopsAsync(
                        decision.Symbol,
                        closeSide,
                        handoff.StopLossPrice,
                        handoff.TakeProfitPrice,
                        handoff.Request.ClientOrderId + "-sl",
                        handoff.Request.ClientOrderId + "-tp",
                        cancellationToken);
                }

                handoff = handoff with { OrderDecision = fill is null ? "NOT_SENT" : fill.Status.ToString() };
            }

            var why = NewsStop.Explain(direction, decision.Reason, handoff.RiskReason, handoff.RiskDecision, handoff.OrderDecision, exchangeId is not null);
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
            options.Strategy.Timeframes.Trend
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
}
