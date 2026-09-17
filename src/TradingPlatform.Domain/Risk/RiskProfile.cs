using TradingPlatform.Domain.Identity;

namespace TradingPlatform.Domain.Risk;

public enum MarginMode
{
    Isolated = 0,
    Cross = 1
}

public sealed class RiskProfile : SoftDeletableEntity
{
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Van Tharp R: percent of equity lost if the strategy stop hits.</summary>
    public decimal RiskPerTradePercent { get; set; } = 0.5m;

    /// <summary>Hard cap on a single name's notional as percent of equity.</summary>
    public decimal MaxPositionPercent { get; set; } = 20m;

    /// <summary>Elder daily stop: halt new entries when account MTM loss hits this percent of equity.</summary>
    public decimal MaxDailyLossPercent { get; set; } = 3m;

    /// <summary>Account-wide (this paper/live book), not per bot.</summary>
    public int MaxOpenPositions { get; set; } = 8;

    public int MaxDailyTrades { get; set; } = 24;
    public int CooldownAfterLossMinutes { get; set; } = 15;
    public int MaxConsecutiveLosses { get; set; } = 4;

    /// <summary>Sent to Binance before a live order. Isolated liquidation should stay beyond the stop.</summary>
    public decimal MaxLeverage { get; set; } = 2m;

    public decimal MinAvailableBalance { get; set; } = 0m;
    public bool StopBotOnDailyLoss { get; set; } = true;

    /// <summary>When the daily loss halt fires, stop every running bot in this paper/live book.</summary>
    public bool StopAccountOnDailyLoss { get; set; } = true;

    public MarginMode MarginMode { get; set; } = MarginMode.Isolated;

    /// <summary>Elder/Tharp heat: max correlated open risk as percent of equity.</summary>
    public decimal MaxPortfolioHeatPercent { get; set; } = 4m;

    /// <summary>Sum of open notionals as percent of equity.</summary>
    public decimal MaxTotalExposurePercent { get; set; } = 60m;

    /// <summary>USDT-M alts vs BTC typically 0.7–0.85. Inflates heat so 8 alts are not 8 independent bets.</summary>
    public decimal CorrelationFactor { get; set; } = 0.75m;

    /// <summary>Cash buffer — never size using the last slice of available margin (Carver/prop book).</summary>
    public decimal MinFreeMarginPercent { get; set; } = 20m;

    public string? AllowedSymbolsCsv { get; set; }
    public bool IsSystem { get; set; }
}
