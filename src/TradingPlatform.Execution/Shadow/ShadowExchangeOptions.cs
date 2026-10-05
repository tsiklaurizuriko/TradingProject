namespace TradingPlatform.Execution.Shadow;

public sealed class ShadowExchangeOptions
{
    public const string SectionName = "Trading:Shadow";

    public decimal StartingBalance { get; set; } = 10_000m;

    /// <summary>Taker commission in percent, charged in USDT on every fill. Binance VIP 0 USD-M taker is 0.05%.</summary>
    public decimal TakerFeePercent { get; set; } = 0.05m;

    /// <summary>Extra slippage beyond the touch, in basis points, on every market fill.</summary>
    public decimal SlippageBps { get; set; } = 2m;

    public int MaxLeverage { get; set; } = 20;

    /// <summary>Where the simulated account survives restarts. Empty keeps it in memory only.</summary>
    public string StatePath { get; set; } = "data/shadow-exchange.json";

    /// <summary>How often stops, takes and funding are checked against real mark prices.</summary>
    public int TickSeconds { get; set; } = 2;
}
