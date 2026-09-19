namespace TradingPlatform.Application.Abstractions.MarketData;

/// <summary>
/// Optional pretty labels for well-known bases. This is NOT the trading universe.
/// Discovery comes from Binance USDⓈ-M exchangeInfo.
/// </summary>
public static class UsdtSpotUniverse
{
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTCUSDT"] = "Bitcoin",
        ["ETHUSDT"] = "Ethereum",
        ["BNBUSDT"] = "BNB",
        ["SOLUSDT"] = "Solana",
        ["XRPUSDT"] = "XRP",
        ["DOGEUSDT"] = "Dogecoin",
        ["SUIUSDT"] = "Sui",
        ["ADAUSDT"] = "Cardano",
        ["LINKUSDT"] = "Chainlink",
        ["AVAXUSDT"] = "Avalanche",
        ["TRXUSDT"] = "TRON",
        ["TONUSDT"] = "Toncoin",
        ["DOTUSDT"] = "Polkadot",
        ["LTCUSDT"] = "Litecoin",
        ["BCHUSDT"] = "Bitcoin Cash"
    };

    public static string DisplayNameOf(string symbol)
    {
        if (DisplayNames.TryGetValue(symbol, out var name))
        {
            return name;
        }

        if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) && symbol.Length > 4)
        {
            return symbol[..^4];
        }

        return symbol;
    }

    public static IReadOnlyList<T> OrderBySymbol<T>(IEnumerable<T> items, Func<T, string> symbolSelector) =>
        items.OrderBy(item => symbolSelector(item), StringComparer.OrdinalIgnoreCase).ToList();
}
