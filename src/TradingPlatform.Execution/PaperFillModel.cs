using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Execution;

public static class PaperFillModel
{
    public static decimal ApplySlippage(decimal lastPrice, OrderSide side, decimal slippageBps)
    {
        var slip = lastPrice * (slippageBps / 10_000m);
        return side == OrderSide.Buy ? lastPrice + slip : lastPrice - slip;
    }

    public static decimal Fee(decimal notional, decimal feeBps) => notional * (feeBps / 10_000m);

    public static decimal FloorToStep(decimal quantity, decimal stepSize)
    {
        if (quantity <= 0m)
        {
            return 0m;
        }

        if (stepSize <= 0m)
        {
            return decimal.Round(quantity, 8, MidpointRounding.ToZero);
        }

        return Math.Floor(quantity / stepSize) * stepSize;
    }

    public static string NewPaperOrderId() => $"PAPER-{Guid.NewGuid():N}";

    public static string NewPaperFillId() => $"PAPER-FILL-{Guid.NewGuid():N}";
}
