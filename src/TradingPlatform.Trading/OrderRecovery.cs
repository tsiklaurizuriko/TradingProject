using TradingPlatform.Application.Abstractions.Exchange;

namespace TradingPlatform.Trading;

public enum RecoveryKind
{
    Confirmed,
    NotAccepted,
    LookupUnavailable
}

public sealed record RecoveryDecision(RecoveryKind Kind, ExchangeOrder? Order, string Reason);

/// <summary>A lost response is not a rejection and is not a reason to send the order again.</summary>
public static class OrderRecovery
{
    public static RecoveryDecision Decide(bool lookupFailed, ExchangeOrder? found)
    {
        if (lookupFailed)
        {
            return new RecoveryDecision(
                RecoveryKind.LookupUnavailable,
                null,
                "Exchange lookup failed. The order was not sent again.");
        }

        if (found is null)
        {
            return new RecoveryDecision(
                RecoveryKind.NotAccepted,
                null,
                "Exchange has no order for this client id. The order was not sent again.");
        }

        return new RecoveryDecision(
            RecoveryKind.Confirmed,
            found,
            "Exchange confirmed the existing client order id. The order was not sent again.");
    }
}
