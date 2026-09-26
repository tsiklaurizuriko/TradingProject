namespace TradingPlatform.News;

public sealed record NewsDeskRow(string Name, string Status);

public sealed record NewsDeskDto(
    bool Enabled,
    string DatasetScope,
    int UniverseCount,
    int Articles,
    int Events,
    int SymbolsWithNews,
    string Status,
    string Note,
    IReadOnlyList<NewsDeskRow> Strategies);

public static class NewsDesk
{
    public static NewsDeskDto Read(string startDirectory, bool enabled = false)
    {
        var root = FindRepo(startDirectory);
        var catalog = NewsAssetCatalog.LoadUniverse(root);
        var store = new FileNewsStore(Path.Combine(root, "artifacts", "data", "news"));
        var raw = store.LoadRawAsync(CancellationToken.None).GetAwaiter().GetResult();
        var events = store.LoadEventsAsync(CancellationToken.None).GetAwaiter().GetResult();
        var covered = events
            .SelectMany(item => item.AffectedAssets.Select(asset => asset.Symbol).Where(symbol => !string.IsNullOrWhiteSpace(symbol)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(symbol => catalog.FindSymbol(symbol!) is not null);
        var scope = events.Count == 0
            ? "EMPTY"
            : events.SelectMany(item => item.Assets).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1
                && events.All(item => item.MarketScope != MarketScope.Global)
                && events.SelectMany(item => item.Assets).Any(asset => asset.Equals("BTC", StringComparison.OrdinalIgnoreCase))
                    ? "BTC_ONLY"
                    : "MULTI_ASSET";
        var status = events.Count == 0 ? "DATA_UNAVAILABLE" : "RESEARCHING";
        var note = events.Count == 0
            ? "News collection can run while the API is up. No article is stored yet. A bot does not place an order from news."
            : "Articles are stored as files. A candidate is a signal only. A bot does not place an order from news.";
        return new NewsDeskDto(
            enabled,
            scope,
            catalog.Identities.Count,
            raw.Count,
            events.Count,
            covered,
            status,
            note,
            NewsHypotheses.All.Select(name => new NewsDeskRow(name, status)).ToList());
    }

    private static string FindRepo(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NEWS_INTEGRATION_PLAN.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return start;
    }
}
