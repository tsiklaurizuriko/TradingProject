using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Trading;

public sealed record ExchangeFillReport(
    OrderStatus Status,
    decimal ExecutedQuantity,
    decimal? AveragePrice,
    decimal Fee,
    string? ExchangeOrderId,
    string? RejectReason);

public sealed record FillDelta(decimal Quantity, decimal Price, decimal Fee);

public sealed record FillApplication(
    OrderStatus Status,
    decimal FilledQuantity,
    decimal RemainingQuantity,
    decimal? AverageFillPrice,
    FillDelta? NewFill,
    bool Uncertain,
    bool BlocksNewEntries,
    string Reason);

/// <summary>
/// Applies one exchange report to the quantity already booked. A second report with the same
/// cumulative quantity produces no new fill. Filled is used only when the executed quantity
/// equals the requested quantity.
/// </summary>
public static class FillAccounting
{
    public static FillApplication Apply(decimal previouslyFilled, decimal requestedQuantity, ExchangeFillReport report)
    {
        if (requestedQuantity <= 0m)
        {
            return Uncertain(previouslyFilled, "Requested quantity is missing. No position change.");
        }

        if (report.ExecutedQuantity < 0m || report.ExecutedQuantity > requestedQuantity)
        {
            return Uncertain(previouslyFilled, "Executed quantity contradicts the requested quantity. No position change.");
        }

        if (report.ExecutedQuantity < previouslyFilled)
        {
            return Uncertain(previouslyFilled, "Executed quantity moved backwards. No position change.");
        }

        var status = Normalize(report.Status, report.ExecutedQuantity, requestedQuantity);
        if (status == OrderStatus.Uncertain)
        {
            return Uncertain(previouslyFilled, report.RejectReason ?? "Exchange status is unknown. No position change.");
        }

        if (report.ExecutedQuantity > previouslyFilled && (report.AveragePrice is null or <= 0m))
        {
            return Uncertain(previouslyFilled, "A new fill has no average price. No position change.");
        }

        var delta = report.ExecutedQuantity - previouslyFilled;
        var remaining = requestedQuantity - report.ExecutedQuantity;
        var average = report.AveragePrice is > 0m ? report.AveragePrice : null;
        FillDelta? fill = delta > 0m && average is > 0m
            ? new FillDelta(delta, average.Value, report.Fee)
            : null;
        var blocks = status is OrderStatus.New or OrderStatus.Submitting or OrderStatus.Submitted
            or OrderStatus.PartiallyFilled or OrderStatus.Uncertain or OrderStatus.CancelRequested;
        return new FillApplication(
            status,
            report.ExecutedQuantity,
            remaining,
            average,
            fill,
            false,
            blocks,
            ReasonFor(status, report));
    }

    public static OrderStatus Normalize(OrderStatus reported, decimal executed, decimal requested)
    {
        switch (reported)
        {
            case OrderStatus.Filled when executed == requested && executed > 0m:
                return OrderStatus.Filled;
            case OrderStatus.Filled when executed > 0m && executed < requested:
                return OrderStatus.PartiallyFilled;
            case OrderStatus.Filled:
                return OrderStatus.Uncertain;
            case OrderStatus.PartiallyFilled when executed <= 0m:
                return OrderStatus.Uncertain;
            case OrderStatus.PartiallyFilled when executed >= requested:
                return OrderStatus.Filled;
            case OrderStatus.PartiallyFilled:
                return OrderStatus.PartiallyFilled;
            case OrderStatus.New:
            case OrderStatus.Submitting:
            case OrderStatus.Submitted:
            case OrderStatus.CancelRequested:
                if (executed >= requested && executed > 0m)
                {
                    return OrderStatus.Filled;
                }

                return executed > 0m ? OrderStatus.PartiallyFilled : reported;
            case OrderStatus.Cancelled:
            case OrderStatus.Rejected:
            case OrderStatus.Expired:
            case OrderStatus.Failed:
                return reported;
            default:
                return OrderStatus.Uncertain;
        }
    }

    private static FillApplication Uncertain(decimal previouslyFilled, string reason) =>
        new(OrderStatus.Uncertain, previouslyFilled, 0m, null, null, true, true, reason);

    private static string ReasonFor(OrderStatus status, ExchangeFillReport report) => status switch
    {
        OrderStatus.Filled => "Exchange confirmed a full fill.",
        OrderStatus.PartiallyFilled => "Exchange confirmed a partial fill. The remainder is still open.",
        OrderStatus.Cancelled => "Exchange cancelled the order. Only the confirmed executed quantity is booked.",
        OrderStatus.Rejected => report.RejectReason ?? "Exchange rejected the order.",
        OrderStatus.Expired => "Exchange expired the order.",
        OrderStatus.Failed => report.RejectReason ?? "Exchange failed the order.",
        _ => report.RejectReason ?? $"Exchange status {status}."
    };
}
