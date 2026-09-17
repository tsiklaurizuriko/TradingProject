namespace TradingPlatform.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICorrelationIdAccessor
{
    string GetOrCreate();
    void Set(string correlationId);
}

public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private static readonly AsyncLocal<string?> Current = new();

    public string GetOrCreate() => Current.Value ??= Guid.NewGuid().ToString("N");

    public void Set(string correlationId) => Current.Value = correlationId;
}

public sealed record AuditEntry(
    string Action,
    string Entity,
    string? EntityId,
    Guid? UserId,
    string? IpAddress,
    string CorrelationId,
    string? MetadataJson);

public interface IAuditService
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
