using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

public static class BinanceClosedFill
{
    public const string IncomePrefix = "BNI";
    public const string TradePrefix = "BNT";

    public readonly record struct Fill(
        OrderSide Side,
        decimal Price,
        decimal Quantity,
        decimal Fee,
        DateTimeOffset Time,
        string TradeId,
        decimal RealizedPnl);

    public sealed record ClosedIsolated(
        string Symbol,
        OrderSide EntrySide,
        decimal Quantity,
        decimal EntryPrice,
        decimal ExitPrice,
        decimal RealizedPnl,
        decimal Fees,
        DateTimeOffset OpenedAt,
        DateTimeOffset ClosedAt,
        string CloseTradeId);

    public static string IncomeKey(long tranId) => IncomePrefix + tranId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string TradeKey(string tradeId) => TradePrefix + tradeId.Trim();

    public static bool IsClosing(decimal realizedPnl) => realizedPnl != 0m;

    public static decimal EntryPrice(OrderSide closeSide, decimal exitPrice, decimal quantity, decimal realizedPnl)
    {
        if (quantity <= 0m)
        {
            return exitPrice;
        }

        var delta = realizedPnl / quantity;
        return closeSide == OrderSide.Buy ? exitPrice + delta : exitPrice - delta;
    }

    public static decimal PnLPercent(decimal entryPrice, decimal quantity, decimal realizedPnl)
    {
        var notional = Math.Abs(entryPrice * quantity);
        return notional == 0m ? 0m : realizedPnl / notional * 100m;
    }

    public static IReadOnlyList<ClosedIsolated> RoundTrips(string symbol, IReadOnlyList<Fill> fills)
    {
        var ordered = fills
            .OrderBy(item => item.Time)
            .ThenBy(item => item.TradeId, StringComparer.Ordinal)
            .ToList();
        var trips = new List<ClosedIsolated>();
        decimal net = 0m;
        decimal openQty = 0m;
        decimal openNotional = 0m;
        decimal closeQty = 0m;
        decimal closeNotional = 0m;
        decimal fees = 0m;
        decimal realized = 0m;
        var opened = DateTimeOffset.MinValue;
        var entrySide = OrderSide.Buy;
        var lastId = "";

        void Reset()
        {
            net = 0m;
            openQty = 0m;
            openNotional = 0m;
            closeQty = 0m;
            closeNotional = 0m;
            fees = 0m;
            realized = 0m;
            opened = DateTimeOffset.MinValue;
            lastId = "";
        }

        foreach (var fill in ordered)
        {
            if (fill.Quantity <= 0m)
            {
                continue;
            }

            var signed = fill.Side == OrderSide.Buy ? fill.Quantity : -fill.Quantity;
            if (net == 0m)
            {
                opened = fill.Time;
                entrySide = fill.Side;
                fees = 0m;
                realized = 0m;
                openQty = 0m;
                openNotional = 0m;
                closeQty = 0m;
                closeNotional = 0m;
            }

            fees += fill.Fee;
            realized += fill.RealizedPnl;
            lastId = fill.TradeId;
            var increasing = net == 0m || (net > 0m && signed > 0m) || (net < 0m && signed < 0m);
            if (increasing)
            {
                openQty += fill.Quantity;
                openNotional += fill.Price * fill.Quantity;
            }
            else
            {
                closeQty += fill.Quantity;
                closeNotional += fill.Price * fill.Quantity;
            }

            net += signed;
            if (Math.Abs(net) > 0.00000001m)
            {
                continue;
            }

            var qty = openQty > 0m ? openQty : fill.Quantity;
            var entry = qty > 0m && openNotional > 0m ? openNotional / qty : fill.Price;
            var exit = closeQty > 0m ? closeNotional / closeQty : fill.Price;
            trips.Add(new ClosedIsolated(
                symbol.ToUpperInvariant(),
                entrySide,
                qty,
                entry,
                exit,
                realized,
                fees,
                opened,
                fill.Time,
                lastId));
            Reset();
        }

        return trips;
    }
}

