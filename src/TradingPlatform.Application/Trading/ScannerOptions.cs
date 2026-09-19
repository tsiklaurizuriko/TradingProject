namespace TradingPlatform.Application.Trading;

public sealed class ScannerOptions
{
    public const string SectionName = "Trading:Scanner";

    /// <summary>24h quote volume floor in USDT. Not a top-N coin list.</summary>
    public decimal MinQuoteVolumeUsdt { get; set; } = 10_000_000m;

    public decimal MaxSpreadBps { get; set; } = 8m;

    public int MinTrades24h { get; set; } = 5_000;

    public decimal MaxAbsFundingRate { get; set; } = 0.01m;

    public decimal MinScanScore { get; set; } = 0.25m;

    public decimal MinLastPrice { get; set; } = 0.00000001m;

    public ScannerWeights Weights { get; set; } = new();
}

public sealed class ScannerWeights
{
    public decimal QuoteVolume { get; set; } = 0.25m;
    public decimal Trades24h { get; set; } = 0.15m;
    public decimal Volatility { get; set; } = 0.10m;
    public decimal Spread { get; set; } = 0.15m;
    public decimal OpenInterest { get; set; } = 0.05m;
    public decimal Funding { get; set; } = 0.10m;
    public decimal TrendQuality { get; set; } = 0.10m;
    public decimal DataQuality { get; set; } = 0.10m;
}
