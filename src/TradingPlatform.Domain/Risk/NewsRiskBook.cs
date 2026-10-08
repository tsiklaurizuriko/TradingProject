namespace TradingPlatform.Domain.Risk;

/// <summary>
/// Isolated book used only by news orders. Account risk stays small. The stop is wide enough
/// for the first news spike, and the target is twice that distance so a real move is not cut early.
/// At 3x the estimated liquidation is about 33% away, so a 6% stop remains well clear of it.
/// </summary>
public static class NewsRiskBook
{
    public const string Name = "NEWS";

    public const decimal RiskPerTradePercent = 0.5m;
    public const decimal StopLossPercent = 6m;
    public const decimal TakeProfitPercent = 12m;
    public const decimal MaxLeverage = 3m;
    public const decimal MaxDailyLossPercent = 3m;
    public const decimal MaxWeeklyLossPercent = 6m;
    public const decimal MaxDrawdownPercent = 15m;
    public const decimal MaxPortfolioRiskPercent = 1.5m;
    public const int MaxSimultaneousPositions = 2;
    public const int MaxConsecutiveLosses = 3;
    public const int CooldownMinutes = 60;
    public const decimal MinimumLiquidationSafetyBufferPercent = 1m;

    public static RiskProfile Create() =>
        new()
        {
            Name = Name,
            IsSystem = true,
            IsActive = false,
            AllowLive = true,
            RiskPerTradePercent = RiskPerTradePercent,
            StopLossPercent = StopLossPercent,
            TakeProfitPercent = TakeProfitPercent,
            MaxLeverage = MaxLeverage,
            MaxDailyLossPercent = MaxDailyLossPercent,
            MaxWeeklyLossPercent = MaxWeeklyLossPercent,
            MaxDrawdownPercent = MaxDrawdownPercent,
            MaxPortfolioRiskPercent = MaxPortfolioRiskPercent,
            MaxSimultaneousPositions = MaxSimultaneousPositions,
            MaxConsecutiveLosses = MaxConsecutiveLosses,
            CooldownMinutes = CooldownMinutes,
            MinimumLiquidationSafetyBufferPercent = MinimumLiquidationSafetyBufferPercent
        };
}
