namespace TradingPlatform.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public abstract class SoftDeletableEntity : Entity
{
    public DateTimeOffset? DeletedAt { get; set; }
    public bool IsDeleted => DeletedAt.HasValue;
}

public readonly record struct Money(decimal Amount)
{
    public static Money Zero { get; } = new(0m);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);
    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);
}

public static class DecimalConventions
{
    public const int PricePrecision = 28;
    public const int PriceScale = 8;
    public const int QuantityPrecision = 28;
    public const int QuantityScale = 8;
    public const int PercentPrecision = 18;
    public const int PercentScale = 8;
}
