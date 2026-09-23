using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Domain.Positions;

namespace TradingPlatform.Trading;

public static class LiveProtectivePrices
{
    public static (decimal StopLoss, decimal TakeProfit) FromEntry(
        decimal entryPrice,
        decimal stopLossPercent,
        decimal takeProfitPercent,
        decimal tickSize,
        PositionSide side = PositionSide.Long)
    {
        if (entryPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(entryPrice));
        }

        var tick = tickSize > 0m ? tickSize : 0.00000001m;
        var shortSide = side == PositionSide.Short;
        var stopRaw = shortSide
            ? entryPrice * (1m + stopLossPercent / 100m)
            : entryPrice * (1m - stopLossPercent / 100m);
        var takeRaw = shortSide
            ? entryPrice * (1m - takeProfitPercent / 100m)
            : entryPrice * (1m + takeProfitPercent / 100m);
        var stop = RoundToTick(stopRaw, tick, down: !shortSide);
        var take = RoundToTick(takeRaw, tick, down: shortSide);
        if (shortSide)
        {
            if (stop <= entryPrice)
            {
                stop = RoundToTick(entryPrice + tick, tick, down: false);
            }

            if (take >= entryPrice)
            {
                take = RoundToTick(entryPrice - tick, tick, down: true);
            }
        }
        else
        {
            if (stop >= entryPrice)
            {
                stop = RoundToTick(entryPrice - tick, tick, down: true);
            }

            if (take <= entryPrice)
            {
                take = RoundToTick(entryPrice + tick, tick, down: false);
            }
        }

        if (!IsValidTrigger(entryPrice, stop, take, side))
        {
            throw new ArgumentOutOfRangeException(
                nameof(entryPrice),
                $"Protective prices are not a valid Isolated SL/TP around entry {entryPrice}: SL {stop}, TP {take}.");
        }

        return (stop, take);
    }

    public static bool IsValidTrigger(decimal entryPrice, decimal stop, decimal take, PositionSide side)
    {
        if (entryPrice <= 0m || stop <= 0m || take <= 0m)
        {
            return false;
        }

        if (side == PositionSide.Short)
        {
            return stop > entryPrice && take < entryPrice;
        }

        return stop < entryPrice && take > entryPrice;
    }

    public static string StopClientOrderId(Guid botId) => $"sl{botId:N}"[..18];

    public static string TakeClientOrderId(Guid botId) => $"tp{botId:N}"[..18];

    public static bool IsStopOrder(string? type) => ProtectiveOrderMath.IsStopOrder(type);

    public static bool IsTakeOrder(string? type) => ProtectiveOrderMath.IsTakeOrder(type);

    public static bool IsExistingProtectiveOrder(string? message) =>
        ProtectiveOrderMath.IsExistingProtectiveOrder(message);

    public static decimal RestingTrigger(decimal mark, decimal tickSize, bool closingShort, bool stop) =>
        ProtectiveOrderMath.RestingTrigger(mark, tickSize, closingShort, stop);

    public static decimal RoundToTick(decimal price, decimal tick, bool down)
    {
        if (tick <= 0m)
        {
            return price;
        }

        var steps = price / tick;
        var rounded = down ? Math.Floor(steps) : Math.Ceiling(steps);
        return rounded * tick;
    }
}
