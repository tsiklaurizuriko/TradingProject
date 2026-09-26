using Microsoft.EntityFrameworkCore;
using TradingPlatform.Domain.News;

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

    public async Task<IReadOnlyList<NewsFeedRow>> RecentFeedAsync(int take, CancellationToken cancellationToken)
    {
        var articles = await _db.NewsArticles.AsNoTracking()
            .OrderByDescending(row => row.PublishedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
        var events = await _db.NewsEvents.AsNoTracking()
            .OrderByDescending(row => row.PublishedAtUtc)
            .Take(80)
            .ToListAsync(cancellationToken);
        var signals = await _db.NewsTradingSignals.AsNoTracking()
            .OrderByDescending(row => row.SignalTimeUtc)
            .Take(80)
            .ToListAsync(cancellationToken);
        var rows = new List<NewsFeedRow>(articles.Count);
        foreach (var article in articles)
        {
            var matched = string.IsNullOrEmpty(article.ProviderArticleId)
                ? null
                : events.FirstOrDefault(item =>
                    item.ArticleIds.Contains(article.ProviderArticleId, StringComparison.Ordinal));
            var signal = matched is null
                ? null
                : signals.FirstOrDefault(item => item.StoredNewsEventId == matched.Id);
            var coin = matched?.PrimaryAsset;
            var outcome = signal is null
                ? matched is null
                    ? "Read. Not matched to a coin."
                    : matched.Direction + ". Impact " + matched.Impact.ToString("0.00") + ". Not sent to risk."
                : signal.Direction + ". " + (string.IsNullOrWhiteSpace(signal.Reason) ? signal.RiskReason : signal.Reason);
            rows.Add(new NewsFeedRow(
                article.PublishedAtUtc,
                string.IsNullOrWhiteSpace(article.Source) ? article.Provider : article.Source,
                article.Title,
                article.CanonicalUrl,
                string.IsNullOrWhiteSpace(coin) ? null : coin,
                outcome));
        }

        return rows;
    }
}

public sealed record NewsFeedRow(
    DateTimeOffset PublishedAt,
    string Source,
    string Title,
    string Url,
    string? Coin,
    string Outcome);
