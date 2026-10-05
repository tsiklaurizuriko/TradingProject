using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TradingPlatform.Domain.News;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.News;

namespace TradingPlatform.Api.Controllers;

[ApiController]
[Route("api/trading/research/news")]
[Authorize]
public sealed class NewsDeskController : ControllerBase
{
    private readonly IHostEnvironment _environment;
    private readonly NewsOptions _news;
    private readonly TradingDbContext _db;

    public NewsDeskController(IHostEnvironment environment, IOptions<NewsOptions> news, TradingDbContext db)
    {
        _environment = environment;
        _news = news.Value;
        _db = db;
    }

    [HttpGet]
    public async Task<NewsTradingDeskDto> Get(CancellationToken cancellationToken)
    {
        var desk = NewsDesk.Read(_environment.ContentRootPath, _news.Enabled);
        var store = new NewsDatabase(_db);
        var session = await store.GetOrCreateSessionAsync(cancellationToken);
        var latestEvent = await store.LatestEventAsync(cancellationToken);
        var latest = await store.LatestSignalAsync(cancellationToken);
        return new NewsTradingDeskDto(
            desk.Enabled,
            session.Running,
            session.Mode,
            desk.UniverseCount,
            await store.ArticleCountAsync(cancellationToken),
            await store.EventCountAsync(cancellationToken),
            latestEvent is null ? null : latestEvent.PrimaryAsset + " " + latestEvent.Direction + " impact " + latestEvent.Impact.ToString("0.00"),
            latest is null ? null : latest.Symbol + " " + latest.Direction,
            latest?.NewsScore,
            latest?.MarketScore,
            latest?.FinalScore,
            latest?.RiskDecision,
            latest?.OrderDecision,
            latest?.EntryPrice,
            latest?.Quantity,
            latest?.StopLossPrice,
            latest?.TakeProfitPrice,
            latest is null ? session.LastStatus : NewsStop.Explain(latest),
            await store.RecentFeedAsync(30, _news.Strategy, DateTimeOffset.UtcNow, cancellationToken),
            HealthRows(_news, await store.ProviderHealthAsync(cancellationToken), DateTimeOffset.UtcNow));
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        var store = new NewsDatabase(_db);
        var session = await store.GetOrCreateSessionAsync(cancellationToken);
        session.Running = true;
        session.Mode = "Live";
        session.StartedAt = DateTimeOffset.UtcNow;
        session.StoppedAt = null;
        session.UniverseCount = NewsAssetCatalog.LoadUniverse(RepoRoot(_environment.ContentRootPath)).Identities.Count;
        await store.SaveAsync(cancellationToken);
        return Ok(new { running = true, mode = "Live", liveTrading = true });
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(CancellationToken cancellationToken)
    {
        var store = new NewsDatabase(_db);
        var session = await store.GetOrCreateSessionAsync(cancellationToken);
        session.Running = false;
        session.StoppedAt = DateTimeOffset.UtcNow;
        await store.SaveAsync(cancellationToken);
        return Ok(new { running = false });
    }

    private static List<NewsProviderHealthRow> HealthRows(NewsOptions options, IReadOnlyList<NewsProviderHealth> stored, DateTimeOffset now)
    {
        var wanted = new List<(string Name, string? Disabled)>();
        foreach (var source in NewsProviderCatalog.ScheduledSources(options))
        {
            wanted.Add((source.Name, null));
        }

        foreach (var report in NewsProviderCatalog.DisabledCredentialReports(options, now))
        {
            if (wanted.All(item => item.Name != report.Provider))
            {
                wanted.Add((report.Provider, report.Error));
            }
        }

        wanted.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        var rows = new List<NewsProviderHealthRow>(wanted.Count);
        foreach (var item in wanted)
        {
            var row = stored.FirstOrDefault(health => health.Provider == item.Name);
            if (row is null)
            {
                var enabled = item.Disabled is null;
                rows.Add(new NewsProviderHealthRow(Label(item.Name), enabled, enabled ? "Pending" : "Disabled", null, null, item.Disabled, null, null, 0, 0, 0, 0));
                continue;
            }

            rows.Add(new NewsProviderHealthRow(
                Label(row.Provider),
                row.Enabled,
                NewsProviderHealthRow.Describe(row, now),
                row.LastAttemptUtc,
                row.LastSuccessUtc,
                row.LastError,
                row.LastErrorUtc,
                row.NextEligibleUtc,
                row.FetchedCount,
                row.InsertedCount,
                row.DeduplicatedCount,
                row.RejectedCount));
        }

        return rows;
    }

    private static string Label(string name) => name.ToLowerInvariant() switch
    {
        "coindesk" => "CoinDesk API",
        "coingecko" => "CoinGecko",
        "cryptopanic" => "CryptoPanic",
        "fred" => "FRED",
        _ => name
    };

    private static string RepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "src", "TradingPlatform.Api", "data", "futures-universe.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return start;
    }
}

public sealed record NewsTradingDeskDto(
    bool Enabled,
    bool Running,
    string Mode,
    int UniverseCount,
    int Articles,
    int Events,
    string? LatestNews,
    string? Signal,
    double? NewsScore,
    double? MarketScore,
    double? FinalScore,
    string? Risk,
    string? Order,
    decimal? Entry,
    decimal? Quantity,
    decimal? StopLoss,
    decimal? TakeProfit,
    string? Reason,
    IReadOnlyList<NewsFeedRow> Items,
    IReadOnlyList<NewsProviderHealthRow> Providers);
