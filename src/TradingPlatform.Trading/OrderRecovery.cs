using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Trading;

public enum RecoveryKind
{
    Confirmed,
    ConfirmedAbsent,
    LookupUnavailable
}

public sealed record RecoveryDecision(RecoveryKind Kind, ExchangeOrder? Order, string Reason);

/// <summary>
/// A lost or empty lookup is not proof the exchange rejected the order, and it is not a reason to send it again.
/// ConfirmedAbsent is used only when the connector reports an authoritative not-found.
/// </summary>
public static class OrderRecovery
{
    public static RecoveryDecision Decide(OrderLookup lookup)
    {
        if (lookup.Kind == OrderLookupKind.Found && lookup.Order is not null)
        {
            return new RecoveryDecision(
                RecoveryKind.Confirmed,
                lookup.Order,
                "Exchange confirmed the existing client order id. The order was not sent again.");
        }

        if (lookup.Kind == OrderLookupKind.ConfirmedAbsent)
        {
            return new RecoveryDecision(
                RecoveryKind.ConfirmedAbsent,
                null,
                lookup.Reason);
        }

        return new RecoveryDecision(
            RecoveryKind.LookupUnavailable,
            null,
            string.IsNullOrWhiteSpace(lookup.Reason)
                ? "Exchange lookup is ambiguous. The order was not sent again."
                : lookup.Reason);
    }

    /// <summary>How long a "not found" after an ambiguous submit is re-checked before the order is called Failed.</summary>
    public static readonly TimeSpan AbsentVerifyWindow = TimeSpan.FromMinutes(3);

    /// <summary>
    /// A not-found lookup can race the exchange accepting a timed-out submit. It is final only when the submit itself
    /// was a clear rejection, or the verify window has passed.
    /// </summary>
    public static bool AbsentIsFinal(DateTimeOffset submittedAt, DateTimeOffset now, Exception? submitError) =>
        IsDefinitiveRejection(submitError) || now - submittedAt >= AbsentVerifyWindow;

    /// <summary>
    /// A MARKET order never rests on the book. A partial that is still reported after the verify window is final:
    /// a reduce-only close larger than the position fills the position and the remainder never executes.
    /// </summary>
    public static bool MarketRemainderExpired(OrderType type, OrderStatus status, DateTimeOffset submittedAt, DateTimeOffset now) =>
        type == OrderType.Market && status == OrderStatus.PartiallyFilled && now - submittedAt >= AbsentVerifyWindow;

    /// <summary>Binance answered with a business rejection, so the order cannot be resting or filled.</summary>
    public static bool IsDefinitiveRejection(Exception? error) =>
        error is DomainException { Code: ErrorCodes.OrderRejected };

    /// <summary>Backoff after the last lookup so a stuck order is polled without a tight loop.</summary>
    public static bool Due(DateTimeOffset createdAt, DateTimeOffset updatedAt, DateTimeOffset now)
    {
        var age = now - createdAt;
        var wait = age.TotalMinutes switch
        {
            < 1 => TimeSpan.FromSeconds(5),
            < 5 => TimeSpan.FromSeconds(15),
            < 30 => TimeSpan.FromSeconds(60),
            _ => TimeSpan.FromMinutes(5)
        };
        return now - updatedAt >= wait;
    }
}
