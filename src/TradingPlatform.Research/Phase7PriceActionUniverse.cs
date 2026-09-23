namespace TradingPlatform.Research;

/// <summary>
/// Research-only subset of the existing volume-ranked ScalpingCatalog universe.
/// This is not a LIVE/PAPER eligibility or execution restriction.
/// </summary>
public static class Phase7PriceActionUniverse
{
    public const string Version = "phase7-v1";

    public static readonly string[] Symbols =
    [
        "BTCUSDT",
        "ETHUSDT",
        "BNBUSDT",
        "SOLUSDT",
        "XRPUSDT",
        "DOGEUSDT",
        "ADAUSDT",
        "AVAXUSDT",
        "LINKUSDT",
        "LTCUSDT",
        "DOTUSDT",
        "NEARUSDT",
        "UNIUSDT",
        "ATOMUSDT",
        "APTUSDT"
    ];

    public const string SelectionBasis =
        "First 15 symbols from the existing volume-ranked ScalpingCatalog universe, all with existing 1m/3m/5m/15m ResearchKlineCache coverage. BTCUSDT and ETHUSDT are mandatory.";
}
