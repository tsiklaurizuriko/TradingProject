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

    /// <summary>Percent of current available futures planned as loss if the stop hits.</summary>
    public decimal RiskPerTradePercent { get; set; } = 0.5m;

    /// <summary>Protective stop distance from entry.</summary>
    public decimal StopLossPercent { get; set; } = 2m;

    /// <summary>Protective take-profit distance from entry.</summary>
    public decimal TakeProfitPercent { get; set; } = 4m;

    /// <summary>Application ceiling. Isolated margin = notional / leverage. Not the planned risk.</summary>
    public decimal MaxLeverage { get; set; } = 3m;

    /// <summary>New entries halt when today's realized loss plus open losses (UTC day) reaches this percent of equity.</summary>
    public decimal MaxDailyLossPercent { get; set; } = 3m;

    /// <summary>Same as the daily halt over the UTC week (Monday start). Zero turns it off.</summary>
    public decimal MaxWeeklyLossPercent { get; set; } = 8m;

    /// <summary>New entries halt when equity is this far below its recorded peak. Zero turns it off.</summary>
    public decimal MaxDrawdownPercent { get; set; } = 15m;

    /// <summary>Cap on sum of planned risk across this strategy's open Isolated positions, as percent of available.</summary>
    public decimal MaxPortfolioRiskPercent { get; set; } = 4m;

    /// <summary>Max open Isolated coins per running strategy. One position per coin still applies globally.</summary>
    public int MaxSimultaneousPositions { get; set; } = 2;

    /// <summary>Losing streak that activates Risk Lock.</summary>
    public int MaxConsecutiveLosses { get; set; } = 5;

    /// <summary>Minutes new entries stay locked after the consecutive-loss limit.</summary>
    public int CooldownMinutes { get; set; } = 30;

    /// <summary>Stop must sit at least this many percent of price away from estimated Isolated liquidation.</summary>
    public decimal MinimumLiquidationSafetyBufferPercent { get; set; } = 1m;

    /// <summary>Only one system book is active. New trades use this profile.</summary>
    public bool IsActive { get; set; }

    /// <summary>HIGH stays paper-only unless this is true.</summary>
    public bool AllowLive { get; set; }

    public bool IsSystem { get; set; }
}
