using TradingPlatform.Domain.Positions;

namespace TradingPlatform.Trading;

public sealed record ExitBooking(decimal RealizedPnl, decimal RemainingQuantity, bool Closed, string EventType);

/// <summary>
/// Exit math shared by a normal fill and a recovered fill. The remainder keeps the original average entry.
/// </summary>
public static class PositionFillBook
{
    public const decimal Dust = 0.00000001m;

    public static ExitBooking Exit(
        PositionSide side,
        decimal positionQuantity,
        decimal averageEntryPrice,
        decimal fillQuantity,
        decimal fillPrice,
        decimal fee)
    {
        var direction = side == PositionSide.Short ? -1m : 1m;
        var pnl = direction * (fillPrice - averageEntryPrice) * fillQuantity - fee;
        var remaining = positionQuantity - fillQuantity;
        if (remaining > Dust)
        {
            return new ExitBooking(pnl, remaining, false, "PARTIAL_CLOSE");
        }

        return new ExitBooking(pnl, 0m, true, "CLOSE");
    }
}
