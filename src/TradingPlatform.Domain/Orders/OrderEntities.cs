using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Orders;

public sealed class Order : Entity
{
    public Guid BotId { get; set; }
    public Bot Bot { get; set; } = null!;
    public Guid? ExchangeAccountId { get; set; }
    public ExchangeAccount? ExchangeAccount { get; set; }
    public Guid StrategyId { get; set; }
    public Strategy Strategy { get; set; } = null!;
    public Guid StrategyVersionId { get; set; }
    public StrategyVersion StrategyVersion { get; set; } = null!;
    public string Symbol { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public decimal? Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal? AverageFillPrice { get; set; }
    public string ClientOrderId { get; set; } = string.Empty;
    public string? ExchangeOrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public TradingMode Mode { get; set; } = TradingMode.Paper;
    public string CorrelationId { get; set; } = string.Empty;
    public string? RejectReason { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ExchangeTimestamp { get; set; }
    public uint RowVersion { get; set; }
    public ICollection<OrderEvent> Events { get; set; } = new List<OrderEvent>();
    public ICollection<Execution> Executions { get; set; } = new List<Execution>();
}

public sealed class OrderEvent : Entity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public OrderStatus FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class Execution : Entity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public string? ExchangeTradeId { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal Fee { get; set; }
    public string FeeAsset { get; set; } = string.Empty;
    public bool IsMaker { get; set; }
    public DateTimeOffset ExchangeTimestamp { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}
