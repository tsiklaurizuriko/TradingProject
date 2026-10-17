namespace TradingPlatform.Application.Trading;

/// <summary>
/// Live and paper switches for the six -new strategies. Defaults keep entries off.
/// Enabling a flag is not evidence that a strategy is profitable.
/// </summary>
public sealed class ProductionNewOptions
{
    public bool LiveEnabled { get; set; }
    public bool PaperEnabled { get; set; }
    public decimal RiskPerTradePercent { get; set; } = 0.35m;
    public decimal MaxAggregateOpenRiskPercent { get; set; } = 1.5m;
    public decimal DailyLossLimitPercent { get; set; } = 2m;

    /// <summary>
    /// The daily halt uses realized PnL since UTC midnight plus open losses. Open gains do not offset realized losses.
    /// </summary>
    public string DailyLossBasis { get; set; } = "UtcDayRealizedPlusOpenLosses";

    public Dictionary<string, bool> Strategies { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trend-following-new"] = false,
        ["breakout-volume-new"] = false,
        ["market-regime-new"] = false,
        ["momentum-relative-strength-new"] = false,
        ["mean-reversion-rsi-bollinger-new"] = false,
        ["funding-basis-new"] = false
    };
}
