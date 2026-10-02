using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Trading;

/// <summary>Quantity and fee already booked from earlier reports for the same order.</summary>
public readonly record struct BookedFill(decimal Quantity, decimal? AveragePrice, decimal BookedFee, string? FeeAsset = null);

/// <summary>
/// One exchange report. Executed quantity and average price are cumulative.
/// Fee is cumulative when <see cref="FeeIsCumulative"/> is true. A null fee means the exchange
/// did not provide one; it is not an actual zero.
/// </summary>
public sealed record ExchangeFillReport(
    OrderStatus Status,
    decimal ExecutedQuantity,
    decimal? AveragePrice,
    decimal? Fee,
    string? ExchangeOrderId,
    string? RejectReason,
    decimal? CumulativeQuote = null,
    bool FeeIsCumulative = true,
    string? FeeAsset = null);

public sealed record FillDelta(decimal Quantity, decimal Price, decimal Fee, bool FeeKnown);

public sealed record FillApplication(
    OrderStatus Status,
    decimal FilledQuantity,
    decimal RemainingQuantity,
    decimal? AverageFillPrice,
    FillDelta? NewFill,
    bool Uncertain,
    bool BlocksNewEntries,
    string Reason,
    decimal AdditionalFee = 0m,
    bool AdditionalFeeKnown = false);

/// <summary>
/// Turns a cumulative exchange report into the newly executed quantity and its own price.
/// The cumulative average is not used as the price of the delta. A repeated report books nothing.
/// </summary>
public static class FillAccounting
{
    public static FillApplication Apply(decimal previouslyFilled, decimal requestedQuantity, ExchangeFillReport report) =>
        Apply(new BookedFill(previouslyFilled, null, 0m), requestedQuantity, report);

    public static FillApplication Apply(BookedFill booked, decimal requestedQuantity, ExchangeFillReport report)
    {
        if (requestedQuantity <= 0m)
        {
            return Uncertain(booked, "Requested quantity is missing. No position change.");
        }

        if (report.ExecutedQuantity < 0m || report.ExecutedQuantity > requestedQuantity)
        {
            return Uncertain(booked, "Executed quantity contradicts the requested quantity. No position change.");
        }

        if (report.ExecutedQuantity < booked.Quantity)
        {
            return Uncertain(booked, "Executed quantity moved backwards. No position change.");
        }

        var status = Normalize(report.Status, report.ExecutedQuantity, requestedQuantity);
        if (status == OrderStatus.Uncertain)
        {
            return Uncertain(booked, report.RejectReason ?? "Exchange status is unknown. No position change.");
        }

        var delta = report.ExecutedQuantity - booked.Quantity;
        if (delta == 0m)
        {
            var extraFee = 0m;
            var extraKnown = false;
            if (report.FeeIsCumulative)
            {
                if (!TryFeeDelta(booked, report, out extraFee, out extraKnown, out var extraReason))
                {
                    return Uncertain(booked, extraReason);
                }
            }

            if (report.ExecutedQuantity > 0m && report.FeeIsCumulative && !extraKnown)
            {
                return new FillApplication(
                    OrderStatus.Uncertain,
                    report.ExecutedQuantity,
                    requestedQuantity - report.ExecutedQuantity,
                    report.AveragePrice is > 0m ? report.AveragePrice : booked.AveragePrice,
                    null,
                    true,
                    true,
                    "Commission amount is unknown. New entries stay blocked.");
            }

            return Resting(
                status,
                report,
                booked,
                requestedQuantity,
                extraFee > 0m ? "Cumulative fee increased. Quantity was already booked." : "This cumulative quantity was already booked.",
                extraFee,
                extraFee > 0m);
        }

        if (!TryIncrementalPrice(booked, report, delta, out var price, out var priceReason))
        {
            return Uncertain(booked, priceReason);
        }

        if (!TryFeeDelta(booked, report, out var fee, out var feeKnown, out var feeReason))
        {
            return Uncertain(booked, feeReason);
        }

        var remaining = requestedQuantity - report.ExecutedQuantity;
        var average = report.AveragePrice is > 0m ? report.AveragePrice : price;
        if (!feeKnown)
        {
            return new FillApplication(
                OrderStatus.Uncertain,
                report.ExecutedQuantity,
                remaining,
                average,
                new FillDelta(delta, price, 0m, false),
                false,
                true,
                "Commission amount is unknown. The quantity was booked and new entries stay blocked.");
        }

        var blocks = Blocks(status);
        return new FillApplication(
            status,
            report.ExecutedQuantity,
            remaining,
            average,
            new FillDelta(delta, price, fee, true),
            false,
            blocks,
            ReasonFor(status, report));
    }

    public static bool TryIncrementalPrice(
        BookedFill booked,
        ExchangeFillReport report,
        decimal delta,
        out decimal price,
        out string reason)
    {
        price = 0m;
        reason = "";
        if (delta <= 0m)
        {
            reason = "There is no new quantity.";
            return false;
        }

        decimal? fromAverage = null;
        if (report.AveragePrice is { } average && average > 0m)
        {
            if (booked.Quantity > 0m && booked.AveragePrice is not > 0m)
            {
                reason = "The previous cumulative average price is missing. No position change.";
                return false;
            }

            var oldAverage = booked.Quantity > 0m ? booked.AveragePrice!.Value : 0m;
            var quoteDelta = (report.ExecutedQuantity * average) - (booked.Quantity * oldAverage);
            if (quoteDelta <= 0m)
            {
                reason = "The cumulative average price implies a non-positive quote delta. No position change.";
                return false;
            }

            fromAverage = quoteDelta / delta;
        }

        decimal? fromQuote = null;
        if (report.CumulativeQuote is { } quote && quote > 0m)
        {
            var oldQuote = booked.Quantity > 0m && booked.AveragePrice is > 0m
                ? booked.Quantity * booked.AveragePrice.Value
                : 0m;
            var quoteDelta = quote - oldQuote;
            if (quoteDelta <= 0m)
            {
                reason = "Cumulative quote value moved backwards. No position change.";
                return false;
            }

            fromQuote = quoteDelta / delta;
            if (report.ExecutedQuantity > 0m && report.AveragePrice is { } averagePrice && averagePrice > 0m)
            {
                var implied = quote / report.ExecutedQuantity;
                var scale = Math.Max(1m, Math.Abs(averagePrice));
                if (Math.Abs(implied - averagePrice) / scale > 0.0001m)
                {
                    reason = "Cumulative quote and cumulative average price disagree. No position change.";
                    return false;
                }
            }
        }

        var chosen = fromQuote ?? fromAverage;
        if (chosen is not > 0m)
        {
            reason = "A new fill has no average price. No position change.";
            return false;
        }

        price = chosen.Value;
        return true;
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

    private static bool TryFeeDelta(
        BookedFill booked,
        ExchangeFillReport report,
        out decimal fee,
        out bool known,
        out string reason)
    {
        fee = 0m;
        known = false;
        reason = "";
        if (report.Fee is null)
        {
            return true;
        }

        if (report.Fee < 0m)
        {
            reason = "Exchange fee is negative. No position change.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(report.FeeAsset))
        {
            reason = "Commission asset is missing. The fee was not booked.";
            return false;
        }

        if (booked.BookedFee > 0m && string.IsNullOrWhiteSpace(booked.FeeAsset))
        {
            reason = "A booked fee has no asset. The new commission was not added.";
            return false;
        }

        if (booked.BookedFee > 0m
            && !string.Equals(booked.FeeAsset, report.FeeAsset, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Commission asset changed. Fees in different assets were not added together.";
            return false;
        }

        if (!report.FeeIsCumulative)
        {
            fee = report.Fee.Value;
            known = true;
            return true;
        }

        var delta = report.Fee.Value - booked.BookedFee;
        if (delta < 0m)
        {
            reason = "Cumulative fee moved backwards. No position change.";
            return false;
        }

        fee = delta;
        known = true;
        return true;
    }

    private static FillApplication Resting(
        OrderStatus status,
        ExchangeFillReport report,
        BookedFill booked,
        decimal requestedQuantity,
        string reason,
        decimal additionalFee = 0m,
        bool additionalFeeKnown = false)
    {
        var average = report.AveragePrice is > 0m ? report.AveragePrice : booked.AveragePrice;
        return new FillApplication(
            status,
            report.ExecutedQuantity,
            requestedQuantity - report.ExecutedQuantity,
            average,
            null,
            false,
            Blocks(status),
            reason,
            additionalFee,
            additionalFeeKnown);
    }

    private static bool Blocks(OrderStatus status) =>
        status is OrderStatus.New or OrderStatus.Submitting or OrderStatus.Submitted
            or OrderStatus.PartiallyFilled or OrderStatus.Uncertain or OrderStatus.CancelRequested;

    private static FillApplication Uncertain(BookedFill booked, string reason) =>
        new(OrderStatus.Uncertain, booked.Quantity, 0m, booked.AveragePrice, null, true, true, reason);

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
