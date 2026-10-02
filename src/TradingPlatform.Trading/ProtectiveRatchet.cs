using TradingPlatform.Domain.Positions;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Trading;

public readonly record struct ProtectiveRatchetDecision(
    decimal StopLoss,
    decimal TakeProfit,
    bool StopMoved,
    bool TakeMoved);

/// <summary>
/// Tightens a working stop after the trade is already in profit, and extends take profit
/// only where the take is a cap in front of the strategy's own exit. The stop never moves
/// back out. The take never moves closer.
/// </summary>
public static class ProtectiveRatchet
{
    public const decimal FeeBuffer = 0.002m;
    public const decimal MinimumStep = 0.01m;

    public static ProtectiveRatchetDecision? TryAdvance(
        string? templateKey,
        PositionSide side,
        decimal entry,
        decimal currentStop,
        decimal currentTake,
        decimal mark,
        decimal tickSize)
    {
        if (PolicyFor(templateKey) is not { } policy || entry <= 0m || mark <= 0m || currentStop <= 0m)
        {
            return null;
        }

        var shortSide = side == PositionSide.Short;
        var profit = shortSide ? (entry - mark) / entry : (mark - entry) / entry;
        var stop = currentStop;
        var stopMoved = false;
        if (profit >= policy.Arm)
        {
            var candidate = CandidateStop(policy, shortSide, entry, mark);
            candidate = LiveProtectivePrices.RoundToTick(candidate, tickSize, down: !shortSide);
            var clearance = Clearance(entry, tickSize);
            var safe = shortSide ? candidate > mark + clearance : candidate < mark - clearance;
            var tighter = shortSide ? candidate < currentStop : candidate > currentStop;
            if (safe && tighter && Math.Abs(candidate - currentStop) >= entry * MinimumStep)
            {
                stop = candidate;
                stopMoved = true;
            }
        }

        var take = currentTake;
        var takeMoved = false;
        if (policy.ExtendTake && currentTake > 0m)
        {
            var distance = shortSide ? 0.50m : 1.00m;
            var target = shortSide ? entry * (1m - distance) : entry * (1m + distance);
            target = LiveProtectivePrices.RoundToTick(target, tickSize, down: shortSide);
            var clearance = Clearance(entry, tickSize);
            var beyondMark = shortSide ? target < mark - clearance : target > mark + clearance;
            var further = shortSide ? target < currentTake : target > currentTake;
            if (beyondMark && further && Math.Abs(target - currentTake) >= entry * MinimumStep)
            {
                take = target;
                takeMoved = true;
            }
        }

        if (!stopMoved && !takeMoved)
        {
            return null;
        }

        return new ProtectiveRatchetDecision(stop, take, stopMoved, takeMoved);
    }

    public static decimal OpeningTake(
        string? templateKey,
        PositionSide side,
        decimal entry,
        decimal stop,
        decimal bookTake,
        decimal mark,
        decimal tickSize)
    {
        if (entry <= 0m || stop <= 0m || bookTake <= 0m)
        {
            return bookTake;
        }

        var price = mark > 0m ? mark : entry;
        return TryAdvance(templateKey, side, entry, stop, bookTake, price, tickSize) is { TakeMoved: true } moved
            ? moved.TakeProfit
            : bookTake;
    }

    public static decimal KeepTighterStop(
        PositionSide side,
        decimal mark,
        decimal profileStop,
        decimal storedStop,
        decimal tickSize)
    {
        if (mark <= 0m)
        {
            return profileStop > 0m ? profileStop : storedStop;
        }

        var clearance = Clearance(0m, tickSize);
        var best = 0m;
        foreach (var price in new[] { profileStop, storedStop })
        {
            if (!SafeStop(side, mark, price, clearance))
            {
                continue;
            }

            best = best == 0m ? price : side == PositionSide.Short ? Math.Min(best, price) : Math.Max(best, price);
        }

        return best;
    }

    public static decimal KeepFurtherTake(
        PositionSide side,
        decimal mark,
        decimal profileTake,
        decimal storedTake,
        decimal tickSize)
    {
        if (mark <= 0m)
        {
            return profileTake > 0m ? profileTake : storedTake;
        }

        var clearance = Clearance(0m, tickSize);
        var best = 0m;
        foreach (var price in new[] { profileTake, storedTake })
        {
            if (!SafeTake(side, mark, price, clearance))
            {
                continue;
            }

            best = best == 0m ? price : side == PositionSide.Short ? Math.Min(best, price) : Math.Max(best, price);
        }

        return best;
    }

    private static decimal CandidateStop(Policy policy, bool shortSide, decimal entry, decimal mark)
    {
        var breakeven = shortSide ? entry * (1m - FeeBuffer) : entry * (1m + FeeBuffer);
        if (!policy.Trail)
        {
            return breakeven;
        }

        var trailed = shortSide ? mark * (1m + 0.08m) : mark * (1m - 0.08m);
        return shortSide ? Math.Min(breakeven, trailed) : Math.Max(breakeven, trailed);
    }

    private static decimal Clearance(decimal entry, decimal tickSize)
    {
        var tick = tickSize > 0m ? tickSize : 0.00000001m;
        var step = entry > 0m ? entry * 0.001m : 0m;
        return Math.Max(tick, step);
    }

    private static bool SafeStop(PositionSide side, decimal mark, decimal price, decimal clearance) =>
        price > 0m && (side == PositionSide.Short ? price > mark + clearance : price < mark - clearance);

    private static bool SafeTake(PositionSide side, decimal mark, decimal price, decimal clearance) =>
        price > 0m && (side == PositionSide.Short ? price < mark - clearance : price > mark + clearance);

    private static Policy? PolicyFor(string? templateKey) => StrategyTemplateKeys.Normalize(templateKey) switch
    {
        StrategyTemplateKeys.ImpulseCatch => new Policy(0.10m, Trail: true, ExtendTake: true),
        StrategyTemplateKeys.FAdxSma => new Policy(0.03m, Trail: false, ExtendTake: true),
        StrategyTemplateKeys.TripleSupertrend => new Policy(0.10m, Trail: false, ExtendTake: true),
        StrategyTemplateKeys.BtcEma20Ema50Long => new Policy(0.015m, Trail: false, ExtendTake: false),
        StrategyTemplateKeys.FlowZone => new Policy(0.08m, Trail: false, ExtendTake: false),
        StrategyTemplateKeys.SqueezeWatch => new Policy(0.04m, Trail: false, ExtendTake: false),
        StrategyTemplateKeys.ZigZagFade => new Policy(0.04m, Trail: false, ExtendTake: false),
        _ => null
    };

    private readonly record struct Policy(decimal Arm, bool Trail, bool ExtendTake);
}
