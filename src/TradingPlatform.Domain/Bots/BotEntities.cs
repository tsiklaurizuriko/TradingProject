using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Bots;

public sealed class Bot : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid ExchangeAccountId { get; set; }
    public ExchangeAccount ExchangeAccount { get; set; } = null!;
    public Guid StrategyVersionId { get; set; }
    public StrategyVersion StrategyVersion { get; set; } = null!;
    public Guid RiskProfileId { get; set; }
    public RiskProfile RiskProfile { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public BotStatus Status { get; set; } = BotStatus.Created;
    public TradingMode Mode { get; set; } = TradingMode.Paper;
    public string Symbol { get; set; } = "BTCUSDT";
    public Timeframe Timeframe { get; set; } = Timeframe.FiveMinutes;
    public bool CancelOpenOrdersOnEmergencyStop { get; set; } = true;
    public string? LastError { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public uint RowVersion { get; set; }
    public ICollection<BotRun> Runs { get; set; } = new List<BotRun>();
}

public sealed class BotRun : Entity
{
    public Guid BotId { get; set; }
    public Bot Bot { get; set; } = null!;
    public BotStatus Status { get; set; } = BotStatus.Starting;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StoppedAt { get; set; }
    public string? StopReason { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}
