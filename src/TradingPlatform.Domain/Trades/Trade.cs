using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Strategies;

namespace TradingPlatform.Domain.Trades;

public sealed class Trade : Entity
{
    public Guid BotId { get; set; }
    public Bot Bot { get; set; } = null!;
    public Guid StrategyId { get; set; }
    public Strategy Strategy { get; set; } = null!;
    public Guid StrategyVersionId { get; set; }
    public StrategyVersion StrategyVersion { get; set; } = null!;
    public Guid? EntryOrderId { get; set; }
    public Order? EntryOrder { get; set; }
    public Guid? ExitOrderId { get; set; }
    public Order? ExitOrder { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal PnL { get; set; }
    public decimal PnLPercent { get; set; }
    public decimal Fees { get; set; }
    public string? HypothesisId { get; set; }
    public string? StrategyFamily { get; set; }
    public DateTimeOffset? SignalAt { get; set; }
    public decimal MaxFavorableExcursion { get; set; }
    public decimal MaxAdverseExcursion { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public TimeSpan? Duration => ClosedAt is null ? null : ClosedAt - OpenedAt;
    public string CorrelationId { get; set; } = string.Empty;
}
