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

        return (stop, take);
    }

    public static string StopClientOrderId(Guid botId) => $"sl{botId:N}"[..18];

    public static string TakeClientOrderId(Guid botId) => $"tp{botId:N}"[..18];

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
