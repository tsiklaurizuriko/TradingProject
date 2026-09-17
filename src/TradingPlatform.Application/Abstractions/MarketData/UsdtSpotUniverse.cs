namespace TradingPlatform.Application.Abstractions.MarketData;

public sealed record MarketCapCoin(int Rank, string Symbol, string DisplayName);

public static class UsdtSpotUniverse
{
    public static readonly IReadOnlyList<MarketCapCoin> ByMarketCap =
    [
        new(1, "BTCUSDT", "Bitcoin"),
        new(2, "ETHUSDT", "Ethereum"),
        new(3, "BNBUSDT", "BNB"),
        new(4, "SOLUSDT", "Solana"),
        new(5, "XRPUSDT", "XRP"),
        new(6, "DOGEUSDT", "Dogecoin"),
        new(7, "SUIUSDT", "Sui"),
        new(8, "ADAUSDT", "Cardano"),
        new(9, "LINKUSDT", "Chainlink"),
        new(10, "AVAXUSDT", "Avalanche"),
        new(11, "TRXUSDT", "TRON"),
        new(12, "TONUSDT", "Toncoin"),
        new(13, "DOTUSDT", "Polkadot"),
        new(14, "LTCUSDT", "Litecoin"),
        new(15, "BCHUSDT", "Bitcoin Cash")
    ];

    public static IReadOnlySet<string> Symbols { get; } =
        ByMarketCap.Select(c => c.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static int RankOf(string symbol)
    {
        var match = ByMarketCap.FirstOrDefault(c => c.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        return match?.Rank ?? int.MaxValue;
    }

    public static string DisplayNameOf(string symbol)
    {
        var match = ByMarketCap.FirstOrDefault(c => c.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match.DisplayName;
        }

        if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) && symbol.Length > 4)
        {
            return symbol[..^4];
        }

        return symbol;
    }

    public static IReadOnlyList<T> OrderByMarketCap<T>(IEnumerable<T> items, Func<T, string> symbolSelector) =>
        items.OrderBy(item => RankOf(symbolSelector(item))).ThenBy(item => symbolSelector(item)).ToList();
}
