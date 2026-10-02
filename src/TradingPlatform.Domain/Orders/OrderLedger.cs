using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Orders;

public static class OrderLedger
{
    public const string KindFill = "Fill";
    public const string KindStop = "Stop";
    public const string KindTake = "TakeProfit";

    public static bool IsProtection(string? type)
    {
        var compact = Compact(type);
        return compact.Contains("STOP", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("TAKEPROFIT", StringComparison.OrdinalIgnoreCase);
    }

    public static string Kind(string? type)
    {
        var compact = Compact(type);
        if (compact.Contains("TAKEPROFIT", StringComparison.OrdinalIgnoreCase))
        {
            return KindTake;
        }

        if (compact.Contains("STOP", StringComparison.OrdinalIgnoreCase))
        {
            return KindStop;
        }

        return KindFill;
    }

    public static OrderType ParseType(string? type)
    {
        var kind = Kind(type);
        return kind switch
        {
            KindTake => OrderType.TakeProfitMarket,
            KindStop => OrderType.StopMarket,
            _ when Compact(type).Contains("LIMIT", StringComparison.OrdinalIgnoreCase) => OrderType.Limit,
            _ => OrderType.Market
        };
    }

    public static OrderStatus ParseStatus(string? status)
    {
        var compact = Compact(status);
        if (compact.Contains("PARTIAL", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.PartiallyFilled;
        }

        if (compact.Contains("FILL", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("FINISH", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("TRIGGER", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Filled;
        }

        if (compact.Contains("CANCEL", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Cancelled;
        }

        if (compact.Contains("REJECT", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Rejected;
        }

        if (compact.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Failed;
        }

        if (compact.Contains("EXPIRE", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Expired;
        }

        if (compact is "NEW")
        {
            return OrderStatus.New;
        }

        if (compact is "WORKING" or "OPEN" or "PENDING" or "SUBMITTED")
        {
            return OrderStatus.Submitted;
        }

        return OrderStatus.Uncertain;
    }

    public static OrderSide ParseSide(string? side)
    {
        var compact = Compact(side);
        return compact is "SELL" or "SHORT" ? OrderSide.Sell : OrderSide.Buy;
    }

    public static bool Same(
        string? clientOrderId,
        string? exchangeOrderId,
        string? otherClientOrderId,
        string? otherExchangeOrderId)
    {
        if (!string.IsNullOrWhiteSpace(clientOrderId)
            && string.Equals(clientOrderId, otherClientOrderId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(exchangeOrderId)
            && string.Equals(exchangeOrderId, otherExchangeOrderId, StringComparison.OrdinalIgnoreCase);
    }

    public static string ClientKey(string? clientOrderId, string? exchangeOrderId)
    {
        if (!string.IsNullOrWhiteSpace(clientOrderId))
        {
            return clientOrderId.Trim();
        }

        return string.IsNullOrWhiteSpace(exchangeOrderId)
            ? string.Empty
            : "BX" + exchangeOrderId.Trim();
    }

    private static string Compact(string? value) =>
        (value ?? string.Empty)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
}
