using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Backtesting;

public sealed class Backtest : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid StrategyVersionId { get; set; }
    public StrategyVersion StrategyVersion { get; set; } = null!;
    public string Symbol { get; set; } = string.Empty;
    public Timeframe Timeframe { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public decimal InitialBalance { get; set; }
    public decimal FeeBps { get; set; }
    public decimal SlippageBps { get; set; }
    public string Status { get; set; } = "Pending";
    public ICollection<BacktestRun> Runs { get; set; } = new List<BacktestRun>();
}

public sealed class BacktestRun : Entity
{
    public Guid BacktestId { get; set; }
    public Backtest Backtest { get; set; } = null!;
    public decimal InitialBalance { get; set; }
    public decimal FinalBalance { get; set; }
    public decimal NetProfit { get; set; }
    public decimal ReturnPercent { get; set; }
    public int NumberOfTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal MaximumDrawdown { get; set; }
    public decimal? SharpeRatio { get; set; }
    public decimal FeesPaid { get; set; }
    public decimal LargestWinningTrade { get; set; }
    public decimal LargestLosingTrade { get; set; }
    public string? AssumptionsJson { get; set; }
    public ICollection<BacktestTrade> Trades { get; set; } = new List<BacktestTrade>();
}

public sealed class BacktestTrade : Entity
{
    public Guid BacktestRunId { get; set; }
    public BacktestRun BacktestRun { get; set; } = null!;
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset ClosedAt { get; set; }
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal PnL { get; set; }
    public decimal Fees { get; set; }
    public string Side { get; set; } = "Long";
    public string Reason { get; set; } = string.Empty;
}
