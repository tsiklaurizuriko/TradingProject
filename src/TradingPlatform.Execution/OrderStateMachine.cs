using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Execution;

public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, HashSet<OrderStatus>> Allowed = new()
    {
        [OrderStatus.New] = [OrderStatus.Submitting, OrderStatus.Rejected, OrderStatus.Failed],
        [OrderStatus.Submitting] = [OrderStatus.Submitted, OrderStatus.Rejected, OrderStatus.Failed, OrderStatus.Filled, OrderStatus.PartiallyFilled, OrderStatus.Cancelled, OrderStatus.Expired],
        [OrderStatus.Submitted] = [OrderStatus.PartiallyFilled, OrderStatus.Filled, OrderStatus.CancelRequested, OrderStatus.Cancelled, OrderStatus.Expired, OrderStatus.Rejected, OrderStatus.Failed],
        [OrderStatus.PartiallyFilled] = [OrderStatus.Filled, OrderStatus.CancelRequested, OrderStatus.Cancelled, OrderStatus.Expired],
        [OrderStatus.CancelRequested] = [OrderStatus.Cancelled, OrderStatus.Filled, OrderStatus.PartiallyFilled, OrderStatus.Expired],
        [OrderStatus.Filled] = [],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Rejected] = [],
        [OrderStatus.Expired] = [],
        [OrderStatus.Failed] = []
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        from == to || (Allowed.TryGetValue(from, out var next) && next.Contains(to));

    public static OrderStatus Transition(OrderStatus from, OrderStatus to)
    {
        if (CanTransition(from, to))
        {
            return to;
        }

        var fallback = Fallback(from, to);
        if (CanTransition(from, fallback))
        {
            return fallback;
        }

        return from;
    }

    private static OrderStatus Fallback(OrderStatus from, OrderStatus to)
    {
        if (to == OrderStatus.Failed && CanTransition(from, OrderStatus.Rejected))
        {
            return OrderStatus.Rejected;
        }

        if (to == OrderStatus.Failed && CanTransition(from, OrderStatus.Cancelled))
        {
            return OrderStatus.Cancelled;
        }

        return to;
    }
}
