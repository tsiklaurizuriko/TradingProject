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
            var text = string.Join(' ', item.OriginalArticles.Select(article => article.Title));
            _catalog.Bind(item, text);
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
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return [];
        }

        if (!string.Equals(_options.Mode, "Live", StringComparison.OrdinalIgnoreCase))
        {
            var stored = await _store.LoadEventsAsync(cancellationToken);
            return stored.Count > 0 ? stored : _pipeline.Build(await _store.LoadRawAsync(cancellationToken));
        }

        var raw = new List<RawNewsItem>();
        var errors = new List<string>();
        foreach (var provider in _providers)
        {
            if (_options.Providers.Length > 0
                && !_options.Providers.Contains(provider.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            IReadOnlyList<RawNewsItem> batch;
            try
            {
                batch = await provider.GetNewsAsync(from, to, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(provider.Name + ": " + ex.Message);
                continue;
            }

            if (batch.Count == 0)
            {
                errors.Add(provider.Name + " returned no articles.");
            }

            raw.AddRange(batch);
            if (_options.CacheEnabled)
            {
                await _store.SaveRawAsync(provider.Name, from, batch, cancellationToken);
            }
        }

        Errors = errors;
        var events = _pipeline.Build(raw);
        if (_options.CacheEnabled)
        {
            await _store.SaveEventsAsync(events, cancellationToken);
        }

        return events;
    }
}
