using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Strategies;

public sealed class Strategy : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public bool AppliesToAllSymbols { get; set; } = true;
    public string? AllowedSymbolsCsv { get; set; }
    public string TemplateKey { get; set; } = "ema_rsi_trend";
    public string AllowedSide { get; set; } = "Long";
    public bool IsEnabled { get; set; } = true;
    public string ValidationStatus { get; set; } = StrategyValidationStatuses.ValidationPending;
    public Guid? RiskProfileId { get; set; }
    public RiskProfile? RiskProfile { get; set; }
    public ICollection<StrategyVersion> Versions { get; set; } = new List<StrategyVersion>();
}

public sealed class StrategyVersion : Entity
{
    public Guid StrategyId { get; set; }
    public Strategy Strategy { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    public string Symbol { get; set; } = "BTCUSDT";
    public Timeframe Timeframe { get; set; } = Timeframe.FiveMinutes;
    public bool IsImmutable { get; set; }
    public DateTimeOffset? FirstUsedAt { get; set; }
}
