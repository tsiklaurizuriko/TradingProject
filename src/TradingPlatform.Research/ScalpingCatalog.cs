using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Research;

public sealed record ScalpingCoverageRow(
    string Symbol,
    string Timeframe,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string Source,
    string Resolution,
    int Gaps,
    int Bars,
    int Downloaded,
    bool CacheHit,
    double TakerCoverage,
    string Status,
    string Notes);

public static class ScalpingCatalog
{
    public static readonly string[] Universe =
    [
        "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "ADAUSDT", "AVAXUSDT", "LINKUSDT", "LTCUSDT",
        "DOTUSDT", "NEARUSDT", "UNIUSDT", "ATOMUSDT", "APTUSDT", "SUIUSDT", "TONUSDT", "FILUSDT", "ARBUSDT", "OPUSDT"
    ];

    public static readonly string[] Timeframes = StrategyTemplateKeys.ScalpingTimeframes;

    public static int MaxHoldBars(string timeframe) => timeframe switch
    {
        "1m" => 15,
        "3m" => 12,
        "5m" => 8,
        "15m" => 6,
        _ => 8
    };
}
