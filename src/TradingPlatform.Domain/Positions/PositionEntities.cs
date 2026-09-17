using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Positions;

public enum PositionSide
{
    Long = 0,
    Short = 1
}

public sealed class Position : Entity
{
    public Guid BotId { get; set; }
    public Bot Bot { get; set; } = null!;
    public string Symbol { get; set; } = string.Empty;
    public PositionSide Side { get; set; } = PositionSide.Long;
    public decimal Quantity { get; set; }
    public decimal AverageEntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal RealizedPnL { get; set; }
    public decimal Fees { get; set; }
    public decimal StopLossPercent { get; set; }
    public decimal InitialRiskUsdt { get; set; }
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }
    public bool IsOpen => ClosedAt is null && Quantity > 0m;
    public uint RowVersion { get; set; }
    public ICollection<PositionEvent> Events { get; set; } = new List<PositionEvent>();
}

public sealed class PositionEvent : Entity
{
    public Guid PositionId { get; set; }
    public Position Position { get; set; } = null!;
    public string EventType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal RealizedPnLDelta { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}
