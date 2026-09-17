using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Signals;

public sealed class Signal : Entity
{
    public Guid StrategyId { get; set; }
    public Strategy Strategy { get; set; } = null!;
    public Guid StrategyVersionId { get; set; }
    public StrategyVersion StrategyVersion { get; set; } = null!;
    public Guid BotId { get; set; }
    public Bot Bot { get; set; } = null!;
    public string Symbol { get; set; } = string.Empty;
    public Timeframe Timeframe { get; set; }
    public SignalType SignalType { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}
