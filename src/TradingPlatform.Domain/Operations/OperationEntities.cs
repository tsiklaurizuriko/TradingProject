using TradingPlatform.Domain.Identity;

namespace TradingPlatform.Domain.Operations;

public sealed class Notification : Entity
{
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string Channel { get; set; } = "InApp";
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string? PayloadJson { get; set; }
}

public sealed class AuditLog : Entity
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? IpAddress { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
}

public sealed class SystemSetting : Entity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
}
