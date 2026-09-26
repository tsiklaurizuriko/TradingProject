using System.Text.Json;
using TradingPlatform.Application.Abstractions.MarketData;

namespace TradingPlatform.News;

public sealed record NewsAssetIdentity(
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    string? ProviderAssetId,
    string? Name = null);

public sealed class NewsAssetCatalog
{
    public const double SecondaryRelevance = 0.6;
    public const double GlobalRelevance = 0.35;

    private static readonly (string Name, string Ticker)[] NameAliases =
    [
        ("BITCOIN", "BTC"),
        ("ETHEREUM", "ETH"),
        ("SOLANA", "SOL"),
        ("RIPPLE", "XRP"),
        ("DOGECOIN", "DOGE"),
        ("CARDANO", "ADA"),
        ("AVALANCHE", "AVAX"),
        ("CHAINLINK", "LINK"),
        ("POLKADOT", "DOT"),
        ("LITECOIN", "LTC"),
        ("BINANCE", "BNB")
    ];

    private readonly Dictionary<string, NewsAssetIdentity> _bySymbol;
    private readonly Dictionary<string, NewsAssetIdentity> _byBase;
    private readonly Dictionary<string, NewsAssetIdentity> _byProviderId;
    private readonly Dictionary<string, NewsAssetIdentity> _byName;

    public NewsAssetCatalog(IReadOnlyList<NewsAssetIdentity> identities)
    {
        Identities = identities;
        _bySymbol = Index(identities, item => item.Symbol);
        _byBase = Index(identities, item => item.BaseAsset);
        _byProviderId = Index(identities.Where(item => !string.IsNullOrWhiteSpace(item.ProviderAssetId)), item => item.ProviderAssetId!);
        _byName = Index(identities.Where(item => !string.IsNullOrWhiteSpace(item.Name)), item => item.Name!);
    }

    public IReadOnlyList<NewsAssetIdentity> Identities { get; }

    public static NewsAssetCatalog Empty { get; } = new([]);

    public static NewsAssetCatalog FromContracts(
        IEnumerable<DiscoveredFuturesContract> contracts,
        IReadOnlyDictionary<string, string>? providerIdToBase = null,
        IReadOnlyDictionary<string, string>? providerIdToName = null)
    {
        var rows = new List<NewsAssetIdentity>();
        foreach (var contract in contracts)
        {
            if (!string.Equals(contract.QuoteAsset, "USDT", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(contract.ContractType, "PERPETUAL", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(contract.Status, "TRADING", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? providerId = null;
            string? name = null;
            if (providerIdToBase is not null)
            {
                var matches = providerIdToBase
                    .Where(pair => string.Equals(pair.Value, contract.BaseAsset, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Key)
                    .ToList();
                if (matches.Count == 1)
                {
                    providerId = matches[0];
                    if (providerIdToName is not null && providerIdToName.TryGetValue(providerId, out var mapped))
                    {
                        name = mapped;
                    }
                }
            }

            rows.Add(new NewsAssetIdentity(contract.Symbol, contract.BaseAsset, contract.QuoteAsset, providerId, name));
        }

        return new NewsAssetCatalog(rows);
    }

    public static NewsAssetCatalog LoadUniverse(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "src", "TradingPlatform.Api", "data", "futures-universe.json");
        if (!File.Exists(path))
        {
            return Empty;
        }

        var json = File.ReadAllText(path);
        var payload = JsonSerializer.Deserialize<UniverseFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return FromContracts(payload?.Contracts ?? []);
    }

    public NewsAssetIdentity? FindSymbol(string symbol) =>
        _bySymbol.TryGetValue(symbol, out var found) ? found : null;

    public NewsAssetContext ContextFor(string symbol)
    {
        if (_bySymbol.TryGetValue(symbol, out var found))
        {
            return new NewsAssetContext(found.Symbol, found.BaseAsset, found.ProviderAssetId);
        }

        return new NewsAssetContext(symbol, BaseFromSymbol(symbol));
    }

    public static string BaseFromSymbol(string symbol)
    {
        var upper = symbol.ToUpperInvariant();
        return upper.EndsWith("USDT", StringComparison.Ordinal) ? upper[..^4] : upper;
    }

    public void Bind(NewsEvent item, string? text = null)
    {
        var links = new List<AssetRelationship>();
        foreach (var providerId in item.ProviderAssetIds)
        {
            if (_byProviderId.TryGetValue(providerId, out var identity))
            {
                Add(links, identity, providerId);
            }
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var upper = text.ToUpperInvariant();
            foreach (var (name, ticker) in NameAliases)
            {
                if (ContainsToken(upper, name) && _byBase.TryGetValue(ticker, out var named))
                {
                    Add(links, named, named.ProviderAssetId);
                }
            }

            foreach (var identity in Identities)
            {
                if (ContainsToken(text, identity.BaseAsset) || ContainsToken(text, identity.Symbol))
                {
                    Add(links, identity, identity.ProviderAssetId);
                }
                else if (!string.IsNullOrWhiteSpace(identity.Name) && ContainsToken(upper, identity.Name.ToUpperInvariant()))
                {
                    Add(links, identity, identity.ProviderAssetId);
                }
            }
        }

        if (links.Count == 0 && item.Assets.Count > 0)
        {
            foreach (var asset in item.Assets)
            {
                if (_byBase.TryGetValue(asset, out var identity))
                {
                    Add(links, identity, identity.ProviderAssetId);
                }
                else
                {
                    links.Add(new AssetRelationship { BaseAsset = asset, Relevance = 1, IsPrimary = links.Count == 0 });
                }
            }
        }

        if (links.Count > 0)
        {
            links[0].IsPrimary = true;
            links[0].Relevance = 1;
            for (var i = 1; i < links.Count; i++)
            {
                links[i].IsPrimary = false;
                links[i].Relevance = SecondaryRelevance;
            }
        }

        item.AffectedAssets = links;
        item.Assets = links.Select(link => link.BaseAsset).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        item.PrimaryAsset = links.FirstOrDefault(link => link.IsPrimary)?.BaseAsset;
        item.MarketScope = links.Count > 1 ? MarketScope.MultiAsset : MarketScope.Asset;
    }

    public static double Relevance(NewsEvent item, string baseAsset)
    {
        if (item.AffectedAssets.Count > 0)
        {
            var link = item.AffectedAssets.FirstOrDefault(asset => string.Equals(asset.BaseAsset, baseAsset, StringComparison.OrdinalIgnoreCase));
            return link?.Relevance ?? 0;
        }

        if (item.Assets.Any(asset => string.Equals(asset, baseAsset, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        return item.MarketScope == MarketScope.Global ? GlobalRelevance : 0;
    }

    private static void Add(List<AssetRelationship> links, NewsAssetIdentity identity, string? providerId)
    {
        if (links.Any(link => string.Equals(link.BaseAsset, identity.BaseAsset, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        links.Add(new AssetRelationship
        {
            Symbol = identity.Symbol,
            BaseAsset = identity.BaseAsset,
            ProviderAssetId = providerId,
            Relevance = 1,
            IsPrimary = links.Count == 0
        });
    }

    private static Dictionary<string, NewsAssetIdentity> Index(
        IEnumerable<NewsAssetIdentity> identities,
        Func<NewsAssetIdentity, string> key)
    {
        var map = new Dictionary<string, NewsAssetIdentity>(StringComparer.OrdinalIgnoreCase);
        foreach (var identity in identities)
        {
            var value = key(identity);
            if (!string.IsNullOrWhiteSpace(value))
            {
                map.TryAdd(value, identity);
            }
        }

        return map;
    }

    private static bool ContainsToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var after = index + token.Length >= text.Length || !char.IsLetterOrDigit(text[index + token.Length]);
            if (before && after)
            {
                return true;
            }

            index += token.Length;
        }

        return false;
    }

    private sealed record UniverseFile(DateTimeOffset FetchedAt, List<DiscoveredFuturesContract> Contracts);
}
