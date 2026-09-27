using System.Text.Json;

namespace TradingPlatform.News;

public sealed class FileNewsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _root;

    public FileNewsStore(string root) => _root = root;

    public async Task SaveRawAsync(string provider, DateTimeOffset day, IReadOnlyList<RawNewsItem> items, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, "raw", provider, day.UtcDateTime.ToString("yyyy-MM-dd") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(items, Json), cancellationToken);
    }

    public async Task<IReadOnlyList<RawNewsItem>> LoadRawAsync(CancellationToken cancellationToken)
    {
        var dir = Path.Combine(_root, "raw");
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var items = new List<RawNewsItem>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            var loaded = JsonSerializer.Deserialize<List<RawNewsItem>>(await File.ReadAllTextAsync(file, cancellationToken));
            if (loaded is not null)
            {
                items.AddRange(loaded);
            }
        }

        return items;
    }

    public async Task SaveEventsAsync(IReadOnlyList<NewsEvent> events, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, "events", "events.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(events, Json), cancellationToken);
    }

    public async Task<IReadOnlyList<NewsEvent>> LoadEventsAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, "events", "events.json");
        if (!File.Exists(path))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<NewsEvent>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? [];
    }
}

public sealed class NewsPipeline
{
    private readonly NewsOptions _options;
    private readonly INewsClassifier _classifier;
    private readonly NewsAssetCatalog _catalog;

    public NewsPipeline(NewsOptions options, INewsClassifier? classifier = null, NewsAssetCatalog? catalog = null)
    {
        _options = options;
        _classifier = classifier ?? new RuleNewsClassifier();
        _catalog = catalog ?? NewsAssetCatalog.Empty;
    }

    public IReadOnlyList<NewsEvent> Build(IReadOnlyList<RawNewsItem> items)
    {
        var clustered = _options.DeduplicationEnabled
            ? NewsEventClusterer.Cluster(items, _options)
            : items.Select(Single).ToList();
        foreach (var item in clustered)
        {
            _classifier.Classify(item);
            _catalog.Bind(item, NewsText.Readable(item));
            if (item.AffectedAssets.Count == 0 && IsMacro(item))
            {
                item.MarketScope = MarketScope.Global;
                item.PrimaryAsset = null;
            }
        }

        NewsEventClusterer.AssignNovelty(clustered);
        return clustered
            .Where(item => item.ImpactScore >= _options.MinimumImpact && item.ConfidenceScore >= _options.MinimumConfidence)
            .ToList();
    }

    private static bool IsMacro(NewsEvent item) =>
        item.EventType is NewsEventType.Macro
            or NewsEventType.InterestRate
            or NewsEventType.Inflation
            or NewsEventType.Employment
            or NewsEventType.Fed
            or NewsEventType.Sec
            or NewsEventType.Geopolitical
            or NewsEventType.Regulation
            or NewsEventType.Stablecoin;

    private static NewsEvent Single(RawNewsItem item) =>
        NewsEventClusterer.Cluster([item], new NewsOptions { DeduplicationEnabled = true })[0];
}

public sealed record NewsProviderReport(
    string Provider,
    bool Succeeded,
    string? Error,
    int Fetched,
    int Rejected,
    DateTimeOffset AttemptedAtUtc,
    bool Enabled = true,
    DateTimeOffset? NextEligibleUtc = null);

public sealed class NewsCollectionResult
{
    public IReadOnlyList<NewsEvent> Events { get; init; } = [];

    public IReadOnlyList<NewsProviderReport> Providers { get; init; } = [];

    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed class NewsCollector
{
    private readonly NewsOptions _options;
    private readonly IReadOnlyList<INewsProvider> _providers;
    private readonly FileNewsStore _store;
    private readonly NewsPipeline _pipeline;

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public NewsCollector(NewsOptions options, IEnumerable<INewsProvider> providers, FileNewsStore store, NewsAssetCatalog? catalog = null)
    {
        _options = options;
        _providers = providers.ToList();
        _store = store;
        _pipeline = new NewsPipeline(options, options.AiClassificationEnabled ? new SchemaNewsClassifier() : new RuleNewsClassifier(), catalog);
    }

    public async Task<IReadOnlyList<NewsEvent>> CollectAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        (await CollectDetailedAsync(from, to, cancellationToken)).Events;

    public async Task<NewsCollectionResult> CollectDetailedAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new NewsCollectionResult();
        }

        if (!string.Equals(_options.Mode, "Live", StringComparison.OrdinalIgnoreCase))
        {
            var stored = await _store.LoadEventsAsync(cancellationToken);
            var events = stored.Count > 0 ? stored : _pipeline.Build(await _store.LoadRawAsync(cancellationToken));
            return new NewsCollectionResult { Events = events };
        }

        var raw = new List<RawNewsItem>();
        var reports = new List<NewsProviderReport>();
        foreach (var provider in _providers)
        {
            if (_options.Providers.Length > 0
                && !_options.Providers.Contains(provider.Name, StringComparer.OrdinalIgnoreCase)
                && !_options.Providers.Contains(provider.ScheduleKey, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var attempted = DateTimeOffset.UtcNow;
            NewsProviderBatch batch;
            try
            {
                batch = await provider.FetchAsync(from, to, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                reports.Add(new NewsProviderReport(provider.Name, false, ex.Message, 0, 0, attempted));
                continue;
            }

            foreach (var item in batch.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Provider))
                {
                    item.Provider = provider.Name;
                }
            }

            raw.AddRange(batch.Items);
            reports.Add(new NewsProviderReport(
                provider.Name,
                true,
                batch.Warning,
                batch.Items.Count,
                batch.Rejected,
                attempted));
            if (_options.CacheEnabled)
            {
                await _store.SaveRawAsync(provider.Name, from, batch.Items, cancellationToken);
            }
        }

        var errors = reports
            .Where(report => !report.Succeeded || !string.IsNullOrWhiteSpace(report.Error))
            .Select(report => report.Provider + ": " + report.Error)
            .ToList();
        Errors = errors;
        var built = _pipeline.Build(raw);
        if (_options.CacheEnabled)
        {
            await _store.SaveEventsAsync(built, cancellationToken);
        }

        return new NewsCollectionResult { Events = built, Providers = reports, Errors = errors };
    }
}
