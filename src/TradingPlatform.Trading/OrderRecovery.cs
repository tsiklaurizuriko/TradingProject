using TradingPlatform.Application.Abstractions.Exchange;

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
