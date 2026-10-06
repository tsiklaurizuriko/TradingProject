using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TradingPlatform.Domain.News;
using TradingPlatform.News;

namespace TradingPlatform.Infrastructure.Persistence;

public sealed class NewsDatabase
{
    private readonly TradingDbContext _db;

    public NewsDatabase(TradingDbContext db) => _db = db;

    public async Task<bool> AddArticleIfNewAsync(NewsArticle article, CancellationToken cancellationToken)
    {
        var exists = await _db.NewsArticles.AnyAsync(
            row => row.Provider == article.Provider && row.ProviderArticleId == article.ProviderArticleId,
            cancellationToken);
        if (exists)
        {
            return false;
        }

        _db.NewsArticles.Add(article);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> EventExistsAsync(string dedupKey, CancellationToken cancellationToken) =>
        _db.NewsEvents.AnyAsync(row => row.DedupKey == dedupKey, cancellationToken);

    public async Task<StoredNewsEvent> AddEventAsync(StoredNewsEvent item, CancellationToken cancellationToken)
    {
        var existing = await _db.NewsEvents.Include(row => row.Assets)
            .FirstOrDefaultAsync(row => row.DedupKey == item.DedupKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        _db.NewsEvents.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public Task<bool> SignalExistsAsync(Guid eventId, string symbol, CancellationToken cancellationToken) =>
        _db.NewsTradingSignals.AnyAsync(row => row.StoredNewsEventId == eventId && row.Symbol == symbol, cancellationToken);

    public Task<bool> HasFinalSignalAsync(string dedupKey, CancellationToken cancellationToken) =>
        _db.NewsTradingSignals.AnyAsync(row => row.StoredNewsEvent != null && row.StoredNewsEvent.DedupKey == dedupKey, cancellationToken);

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> EventCreatedAtAsync(IReadOnlyCollection<string> dedupKeys, CancellationToken cancellationToken)
    {
        if (dedupKeys.Count == 0)
        {
            return new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        }

        var rows = await _db.NewsEvents.AsNoTracking()
            .Where(row => dedupKeys.Contains(row.DedupKey))
            .Select(row => new { row.DedupKey, row.CreatedAt })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.DedupKey, row => row.CreatedAt, StringComparer.Ordinal);
    }

    public async Task<HashSet<string>> AnalyzedEventKeysAsync(IReadOnlyCollection<string> dedupKeys, CancellationToken cancellationToken)
    {
        if (dedupKeys.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var rows = await _db.NewsAnalyses.AsNoTracking()
            .Where(row => dedupKeys.Contains(row.EventDedupKey))
            .Select(row => row.EventDedupKey)
            .Distinct()
            .ToListAsync(cancellationToken);
        return rows.ToHashSet(StringComparer.Ordinal);
    }

    public async Task AddSignalAsync(NewsTradingSignal signal, CancellationToken cancellationToken)
    {
        if (await SignalExistsAsync(signal.StoredNewsEventId, signal.Symbol, cancellationToken))
        {
            return;
        }

        _db.NewsTradingSignals.Add(signal);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SymbolOccupiedAsync(string symbol, string direction, DateTimeOffset since, CancellationToken cancellationToken) =>
        await _db.NewsTradingSignals.AnyAsync(
            row => row.Symbol == symbol
                && row.Direction == direction
                && row.RiskDecision == "Approved"
                && row.SignalTimeUtc >= since,
            cancellationToken);

    public async Task<int> ApprovedOpenCountAsync(CancellationToken cancellationToken) =>
        await _db.NewsTradingSignals.CountAsync(row => row.RiskDecision == "Approved" && row.OrderDecision != "REJECTED", cancellationToken);

    public async Task<NewsTradingSession> GetOrCreateSessionAsync(CancellationToken cancellationToken)
    {
        var session = await _db.NewsTradingSessions.OrderBy(row => row.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (session is not null)
        {
            return session;
        }

        session = new NewsTradingSession { Mode = "Live", Running = false };
        _db.NewsTradingSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public Task SaveAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);

    public Task<int> ArticleCountAsync(CancellationToken cancellationToken) => _db.NewsArticles.CountAsync(cancellationToken);

    public Task<int> EventCountAsync(CancellationToken cancellationToken) => _db.NewsEvents.CountAsync(cancellationToken);

    public async Task<NewsTradingSignal?> LatestSignalAsync(CancellationToken cancellationToken) =>
        await _db.NewsTradingSignals.OrderByDescending(row => row.SignalTimeUtc).FirstOrDefaultAsync(cancellationToken);

    public async Task<StoredNewsEvent?> LatestEventAsync(CancellationToken cancellationToken) =>
        await _db.NewsEvents.OrderByDescending(row => row.PublishedAtUtc).FirstOrDefaultAsync(cancellationToken);

    public async Task SaveIngestionAsync(
        IReadOnlyList<NewsEvent> events,
        IReadOnlyList<NewsProviderReport> reports,
        CancellationToken cancellationToken)
    {
        var inserted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var deduplicated = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in events)
        {
            var existing = await _db.NewsEvents.Include(row => row.Assets).FirstOrDefaultAsync(row => row.DedupKey == item.EventId, cancellationToken);
            if (existing is null)
            {
                existing = new StoredNewsEvent
                {
                    DedupKey = item.EventId,
                    PrimaryAsset = item.PrimaryAsset,
                    MarketScope = item.MarketScope.ToString(),
                    Direction = item.Direction.ToString(),
                    Impact = item.ImpactScore,
                    Confidence = item.ConfidenceScore,
                    EventType = item.EventType.ToString(),
                    PublishedAtUtc = item.PublishedAtUtc,
                    ArticleIds = string.Join(',', item.OriginalArticles.Select(article => Fit(article.Id, 512))),
                    Assets = item.AffectedAssets.Select(asset => new NewsEventAsset
                    {
                        Asset = asset.BaseAsset,
                        Symbol = asset.Symbol ?? asset.BaseAsset + "USDT",
                        Relevance = asset.Relevance,
                        IsPrimary = asset.IsPrimary
                    }).ToList()
                };
                _db.NewsEvents.Add(existing);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (Richer(existing, item))
            {
                existing.Direction = item.Direction.ToString();
                existing.Impact = item.ImpactScore;
                existing.Confidence = item.ConfidenceScore;
                existing.EventType = item.EventType.ToString();
                existing.PrimaryAsset = item.PrimaryAsset;
                existing.MarketScope = item.MarketScope.ToString();
                if (existing.Assets.Count == 0)
                {
                    foreach (var asset in item.AffectedAssets)
                    {
                        existing.Assets.Add(new NewsEventAsset
                        {
                            Asset = asset.BaseAsset,
                            Symbol = asset.Symbol ?? asset.BaseAsset + "USDT",
                            Relevance = asset.Relevance,
                            IsPrimary = asset.IsPrimary
                        });
                    }
                }
            }

            foreach (var article in item.OriginalArticles)
            {
                var provider = string.IsNullOrWhiteSpace(article.Provider) ? "unknown" : article.Provider;
                var canonical = NewsEventClusterer.NavigableUrl(article.SourceUrl);
                if (string.IsNullOrWhiteSpace(canonical))
                {
                    canonical = article.SourceUrl ?? string.Empty;
                }

                canonical = Fit(canonical, 1024);
                var providerArticleId = Fit(article.Id, 512);
                var sightingExists = await _db.NewsArticleSightings.AnyAsync(
                    row => row.Provider == provider && row.ProviderArticleId == providerArticleId,
                    cancellationToken);
                var stored = string.IsNullOrWhiteSpace(canonical)
                    ? null
                    : await _db.NewsArticles.FirstOrDefaultAsync(row => row.CanonicalUrl == canonical, cancellationToken);
                if (stored is null && !string.IsNullOrWhiteSpace(article.SourceUrl))
                {
                    stored = await _db.NewsArticles.FirstOrDefaultAsync(row => row.CanonicalUrl == article.SourceUrl, cancellationToken);
                }

                if (stored is null)
                {
                    var publisher = string.IsNullOrWhiteSpace(article.Source) ? provider : article.Source;
                    stored = new NewsArticle
                    {
                        Provider = provider,
                        Publisher = publisher,
                        ProviderArticleId = providerArticleId,
                        CanonicalUrl = canonical,
                        Title = Fit(article.Title, 1024),
                        Summary = Fit(string.IsNullOrWhiteSpace(article.Summary) ? article.Title : article.Summary, 4000),
                        Source = publisher,
                        PublishedAtUtc = article.PublishedAtUtc,
                        ReceivedAtUtc = item.DetectedAtUtc,
                        StoredNewsEventId = existing.Id
                    };
                    _db.NewsArticles.Add(stored);
                    await _db.SaveChangesAsync(cancellationToken);
                    AddCount(inserted, provider);
                }
                else
                {
                    stored.StoredNewsEventId ??= existing.Id;
                    if (string.IsNullOrWhiteSpace(stored.Publisher))
                    {
                        stored.Publisher = article.Source;
                    }

                    if (!string.IsNullOrWhiteSpace(article.Summary)
                        && (string.IsNullOrWhiteSpace(stored.Summary) || stored.Summary == stored.Title))
                    {
                        stored.Summary = Fit(article.Summary, 4000);
                    }

                    AddCount(deduplicated, provider);
                }

                if (!sightingExists)
                {
                    _db.NewsArticleSightings.Add(new NewsArticleSighting
                    {
                        NewsArticleId = stored.Id,
                        Provider = provider,
                        ProviderArticleId = providerArticleId,
                        ReceivedAtUtc = item.DetectedAtUtc
                    });
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        foreach (var report in reports)
        {
            var row = await _db.NewsProviderHealth.FirstOrDefaultAsync(
                item => item.Provider == report.Provider,
                cancellationToken);
            if (row is null)
            {
                row = new NewsProviderHealth { Provider = report.Provider };
                _db.NewsProviderHealth.Add(row);
            }

            if (!report.Enabled)
            {
                row.Enabled = false;
                var message = Fit(report.Error ?? NewsProviderCatalog.CredentialsMissing, 1000);
                if (!string.Equals(row.LastError, message, StringComparison.Ordinal))
                {
                    row.LastError = message;
                    row.LastErrorUtc = report.AttemptedAtUtc;
                }

                continue;
            }

            row.Enabled = true;
            row.LastAttemptUtc = report.AttemptedAtUtc;
            row.NextEligibleUtc = report.NextEligibleUtc;
            if (report.Succeeded)
            {
                row.LastSuccessUtc = report.AttemptedAtUtc;
                row.FetchedCount = report.Fetched;
                row.InsertedCount = inserted.GetValueOrDefault(report.Provider);
                row.DeduplicatedCount = deduplicated.GetValueOrDefault(report.Provider);
                row.RejectedCount = report.Rejected;
            }

            if (!report.Succeeded || !string.IsNullOrWhiteSpace(report.Error))
            {
                row.LastError = Fit(report.Error ?? "Provider failed.", 1000);
                row.LastErrorUtc = report.AttemptedAtUtc;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NewsProviderHealth>> ProviderHealthAsync(CancellationToken cancellationToken) =>
        await _db.NewsProviderHealth.AsNoTracking().OrderBy(row => row.Provider).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NewsActivityRow>> RecentActivityAsync(int take, CancellationToken cancellationToken)
    {
        var decisions = await _db.NewsTradingDecisions.AsNoTracking()
            .OrderByDescending(row => row.DecisionAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
        var signals = await _db.NewsTradingSignals.AsNoTracking()
            .OrderByDescending(row => row.SignalTimeUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
        var eventIds = decisions
            .Where(row => row.StoredNewsEventId.HasValue)
            .Select(row => row.StoredNewsEventId!.Value)
            .Concat(signals.Select(row => row.StoredNewsEventId))
            .Distinct()
            .ToList();
        var events = eventIds.Count == 0
            ? []
            : await _db.NewsEvents.AsNoTracking().Where(row => eventIds.Contains(row.Id)).ToListAsync(cancellationToken);
        var articles = eventIds.Count == 0
            ? []
            : await _db.NewsArticles.AsNoTracking()
                .Where(row => row.StoredNewsEventId != null && eventIds.Contains(row.StoredNewsEventId.Value))
                .ToListAsync(cancellationToken);
        var keys = events.Select(row => row.DedupKey)
            .Concat(decisions.Select(row => row.EventDedupKey))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct()
            .ToList();
        var executions = keys.Count == 0
            ? []
            : await _db.NewsTradeExecutions.AsNoTracking().Where(row => keys.Contains(row.EventDedupKey)).ToListAsync(cancellationToken);

        var rows = new List<NewsActivityRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var decision in decisions)
        {
            var identity = (decision.StoredNewsEventId?.ToString() ?? decision.EventDedupKey) + "|" + decision.Symbol;
            if (!seen.Add(identity))
            {
                continue;
            }

            var matched = events.FirstOrDefault(item => item.Id == decision.StoredNewsEventId);
            var article = articles.Where(item => item.StoredNewsEventId == decision.StoredNewsEventId).OrderBy(item => item.PublishedAtUtc).FirstOrDefault();
            var signal = signals.FirstOrDefault(item => item.StoredNewsEventId == decision.StoredNewsEventId && item.Symbol == decision.Symbol);
            var execution = executions
                .Where(item => item.EventDedupKey == decision.EventDedupKey && item.Symbol == decision.Symbol)
                .OrderByDescending(item => item.OrderRequestedAtUtc)
                .FirstOrDefault();
            rows.Add(Describe(decision.DecisionAtUtc, decision.Symbol, decision.Decision, decision.RejectionReason, decision.RiskDecision, decision.Confidence, decision.Impact, decision.AlreadyPricedIn, article, matched, signal, execution));
        }

        foreach (var signal in signals)
        {
            var matched = events.FirstOrDefault(item => item.Id == signal.StoredNewsEventId);
            var identity = signal.StoredNewsEventId + "|" + signal.Symbol;
            if (!seen.Add(identity))
            {
                continue;
            }

            var article = articles.Where(item => item.StoredNewsEventId == signal.StoredNewsEventId).OrderBy(item => item.PublishedAtUtc).FirstOrDefault();
            var execution = matched is null
                ? null
                : executions
                    .Where(item => item.EventDedupKey == matched.DedupKey && item.Symbol == signal.Symbol)
                    .OrderByDescending(item => item.OrderRequestedAtUtc)
                    .FirstOrDefault();
            rows.Add(Describe(signal.SignalTimeUtc, signal.Symbol, signal.Direction, null, signal.RiskDecision, NewsActivityCopy.Percent(signal.NewsConfidence), NewsActivityCopy.Percent(signal.NewsImpact), null, article, matched, signal, execution));
        }

        return rows.OrderByDescending(row => row.At).Take(take).ToList();
    }

    private static NewsActivityRow Describe(
        DateTimeOffset at,
        string symbol,
        string verdict,
        string? rejection,
        string? riskDecision,
        int? confidence,
        int? impact,
        int? alreadyPricedIn,
        NewsArticle? article,
        StoredNewsEvent? matched,
        NewsTradingSignal? signal,
        NewsTradeExecution? execution)
    {
        var side = verdict is "LONG" or "SHORT" ? verdict : "NO_TRADE";
        var orderState = NewsActivityCopy.OrderState(
            signal?.BecameTrade == true || execution?.OrderFilledAtUtc is not null,
            execution?.ExchangeOrderId ?? signal?.ExchangeOrderId,
            execution?.OrderFilledAtUtc,
            execution?.ClosedAtUtc,
            riskDecision ?? signal?.RiskDecision,
            signal?.OrderDecision);
        var entry = execution?.EntryPrice ?? signal?.EntryPrice ?? 0m;
        var quantity = execution?.Quantity ?? signal?.Quantity ?? 0m;
        var stop = execution?.StopLossPrice ?? signal?.StopLossPrice ?? 0m;
        var target = execution?.TakeProfitPrice ?? signal?.TakeProfitPrice ?? 0m;
        var headline = string.IsNullOrWhiteSpace(article?.Title) ? "No headline stored for this decision." : article!.Title;
        return new NewsActivityRow(
            at,
            string.IsNullOrWhiteSpace(symbol) ? matched?.PrimaryAsset ?? "" : symbol,
            headline,
            string.IsNullOrWhiteSpace(article?.CanonicalUrl) ? null : article!.CanonicalUrl,
            string.IsNullOrWhiteSpace(article?.Publisher) ? article?.Source ?? "" : article!.Publisher,
            side,
            orderState,
            NewsActivityCopy.Why(side, rejection, signal?.Reason, orderState),
            NewsActivityCopy.OrderLine(orderState, entry, quantity, stop, target, execution?.ExchangeOrderId ?? signal?.ExchangeOrderId),
            confidence is > 0 ? confidence : null,
            impact is > 0 ? impact : null,
            alreadyPricedIn is > 0 ? alreadyPricedIn : null);
    }

    public Task<IReadOnlyList<NewsFeedRow>> RecentFeedAsync(int take, CancellationToken cancellationToken) =>
        RecentFeedAsync(take, new NewsMarketStrategyOptions(), DateTimeOffset.UtcNow, cancellationToken);

    public async Task<IReadOnlyList<NewsFeedRow>> RecentFeedAsync(int take, NewsMarketStrategyOptions strategy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var articles = await _db.NewsArticles.AsNoTracking()
            .OrderByDescending(row => row.PublishedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
        var articleIds = articles.Select(row => row.Id).ToList();
        var sightings = await _db.NewsArticleSightings.AsNoTracking()
            .Where(row => articleIds.Contains(row.NewsArticleId))
            .ToListAsync(cancellationToken);
        var eventIds = articles
            .Where(row => row.StoredNewsEventId.HasValue)
            .Select(row => row.StoredNewsEventId!.Value)
            .Distinct()
            .ToList();
        var events = await _db.NewsEvents.AsNoTracking()
            .Where(row => eventIds.Contains(row.Id))
            .ToListAsync(cancellationToken);
        var signals = await _db.NewsTradingSignals.AsNoTracking()
            .Where(row => eventIds.Contains(row.StoredNewsEventId))
            .ToListAsync(cancellationToken);
        var rows = new List<NewsFeedRow>(articles.Count);
        foreach (var article in articles)
        {
            var matched = article.StoredNewsEventId is Guid eventId
                ? events.FirstOrDefault(item => item.Id == eventId)
                : null;
            matched ??= string.IsNullOrEmpty(article.ProviderArticleId)
                ? null
                : await _db.NewsEvents.AsNoTracking().FirstOrDefaultAsync(
                    item => item.ArticleIds.Contains(article.ProviderArticleId),
                    cancellationToken);
            var signal = matched is null
                ? null
                : signals.FirstOrDefault(item => item.StoredNewsEventId == matched.Id);
            var providers = sightings
                .Where(item => item.NewsArticleId == article.Id)
                .Select(item => item.Provider)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (providers.Count == 0 && !string.IsNullOrWhiteSpace(article.Provider))
            {
                providers.Add(article.Provider);
            }

            var publisher = string.IsNullOrWhiteSpace(article.Publisher) ? article.Source : article.Publisher;
            var evaluated = signal is not null;
            var detail = evaluated
                ? NewsStop.Explain(signal!)
                : NewsStop.Pending(matched, strategy, now);
            rows.Add(new NewsFeedRow(
                article.PublishedAtUtc,
                publisher,
                string.Join(", ", providers),
                article.Title,
                article.CanonicalUrl,
                string.IsNullOrWhiteSpace(matched?.PrimaryAsset) ? null : matched!.PrimaryAsset,
                matched is null ? null : matched.EventType + " · " + matched.Direction,
                matched?.Impact,
                matched?.Confidence,
                evaluated,
                detail));
        }

        return rows;
    }

    public async Task<IReadOnlyList<RawNewsItem>> RecentArticlesAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var rows = await _db.NewsArticles.AsNoTracking()
            .Where(row => row.PublishedAtUtc >= since)
            .OrderByDescending(row => row.PublishedAtUtc)
            .Take(80)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new RawNewsItem
        {
            Id = row.ProviderArticleId,
            Provider = row.Provider,
            Source = string.IsNullOrWhiteSpace(row.Publisher) ? row.Source : row.Publisher,
            SourceUrl = row.CanonicalUrl,
            PublishedAtUtc = row.PublishedAtUtc,
            RetrievedAtUtc = row.ReceivedAtUtc,
            Title = row.Title,
            Summary = row.Summary == row.Title ? string.Empty : row.Summary
        }).ToList();
    }

    public async Task AddAnalysisAsync(NewsAnalysis row, CancellationToken cancellationToken)
    {
        _db.NewsAnalyses.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddTradingDecisionAsync(NewsTradingDecision row, CancellationToken cancellationToken)
    {
        _db.NewsTradingDecisions.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddTradeExecutionAsync(NewsTradeExecution row, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(row.ClientOrderId)
            && await _db.NewsTradeExecutions.AnyAsync(item => item.ClientOrderId == row.ClientOrderId, cancellationToken))
        {
            return;
        }

        _db.NewsTradeExecutions.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddDecisionAuditAsync(NewsDecisionAudit row, CancellationToken cancellationToken)
    {
        _db.NewsDecisionAudits.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NewsTradeExecution>> ListHorizonExitsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = await _db.NewsTradeExecutions
            .Where(row => row.ClosedAtUtc == null && row.OrderFilledAtUtc != null && row.ExpectedHorizonMinutes > 0)
            .ToListAsync(cancellationToken);
        return open.Where(row => row.OrderFilledAtUtc!.Value.AddMinutes(row.ExpectedHorizonMinutes) <= now).ToList();
    }

    private static bool Richer(StoredNewsEvent existing, NewsEvent item)
    {
        var actionable = item.Direction is EventDirection.Bullish or EventDirection.Bearish;
        if (!actionable)
        {
            return false;
        }

        var weak = existing.Direction is "Unknown" or "Neutral" or "Mixed" or "";
        return weak || item.ImpactScore > existing.Impact + 0.05;
    }

    private static void AddCount(Dictionary<string, int> counts, string provider)
    {
        counts[provider] = counts.GetValueOrDefault(provider) + 1;
    }

    private static string Fit(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value ?? string.Empty;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
        var keep = Math.Max(0, max - 13);
        return value[..keep] + "-" + hash;
    }
}

public sealed record NewsActivityRow(
    DateTimeOffset At,
    string Coin,
    string Headline,
    string? Url,
    string Source,
    string Verdict,
    string OrderState,
    string Why,
    string? OrderLine,
    int? Confidence,
    int? Impact,
    int? AlreadyPricedIn);

public sealed record NewsFeedRow(
    DateTimeOffset PublishedAt,
    string Publisher,
    string Providers,
    string Title,
    string Url,
    string? Coin,
    string? Classification,
    double? Impact,
    double? Confidence,
    bool Evaluated,
    string Detail);

public sealed record NewsProviderHealthRow(
    string Provider,
    bool Enabled,
    string Status,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    string? LastError,
    DateTimeOffset? LastErrorUtc,
    DateTimeOffset? NextEligibleUtc,
    int Fetched,
    int Inserted,
    int Deduplicated,
    int Rejected)
{
    public static string Describe(NewsProviderHealth row, DateTimeOffset now)
    {
        if (!row.Enabled)
        {
            return "Disabled";
        }

        if (row.LastErrorUtc is not null && (row.LastSuccessUtc is null || row.LastErrorUtc > row.LastSuccessUtc))
        {
            return "Failing";
        }

        if (row.NextEligibleUtc is DateTimeOffset next && next > now)
        {
            return "Waiting";
        }

        return row.LastSuccessUtc is null ? "Pending" : "Working";
    }
}
