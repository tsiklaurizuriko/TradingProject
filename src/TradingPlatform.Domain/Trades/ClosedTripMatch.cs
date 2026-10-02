namespace TradingPlatform.Domain.Trades;

/// <summary>
/// One Isolated round-trip is one row. Same coin, same size, overlapping open/close
/// from two bots is the same Binance position recorded twice.
/// </summary>
public static class ClosedTripMatch
{
    public static bool Same(
        string leftSymbol,
        decimal leftQty,
        DateTimeOffset leftOpened,
        DateTimeOffset? leftClosed,
        string rightSymbol,
        decimal rightQty,
        DateTimeOffset rightOpened,
        DateTimeOffset? rightClosed)
    {
        if (!string.Equals(Coin(leftSymbol), Coin(rightSymbol), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var scale = Math.Max(0.00000001m, Math.Abs(leftQty) * 0.0001m);
        if (Math.Abs(leftQty - rightQty) > scale)
        {
            return false;
        }

        if (leftClosed is null || rightClosed is null)
        {
            return false;
        }

        // A later trip on the same coin can overlap a stale ghost window.
        // Duplicates in this book open together. A new open more than 15 minutes later is a new trade.
        if ((leftOpened - rightOpened).Duration() > TimeSpan.FromMinutes(15))
        {
            return false;
        }

        return leftOpened <= rightClosed && rightOpened <= leftClosed;
    }

    public static IReadOnlyList<T> Unique<T>(
        IReadOnlyList<T> rows,
        Func<T, string> symbol,
        Func<T, decimal> quantity,
        Func<T, DateTimeOffset> openedAt,
        Func<T, DateTimeOffset?> closedAt,
        Func<T, string?> correlationId,
        Func<T, decimal>? fees = null,
        Func<T, string?>? strategy = null)
    {
        var chosen = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            if (closedAt(row) is null)
            {
                chosen.Add(row);
                continue;
            }

            var index = chosen.FindIndex(existing =>
                closedAt(existing) is not null
                && SameStrategy(strategy, existing, row)
                && Same(
                    symbol(existing),
                    quantity(existing),
                    openedAt(existing),
                    closedAt(existing),
                    symbol(row),
                    quantity(row),
                    openedAt(row),
                    closedAt(row)));
            if (index < 0)
            {
                chosen.Add(row);
                continue;
            }

            if (Prefer(row, chosen[index], correlationId, fees))
            {
                chosen[index] = row;
            }
        }

        return WithoutCoveredShards(chosen, symbol, quantity, closedAt);
    }

    public static (int Losses, DateTimeOffset? LastLossAt) ConsecutiveLosses<T>(
        IReadOnlyList<T> rows,
        Func<T, decimal> pnl,
        Func<T, DateTimeOffset?> closedAt)
    {
        var losses = 0;
        DateTimeOffset? last = null;
        foreach (var row in rows.Where(row => closedAt(row) is not null).OrderByDescending(row => closedAt(row)))
        {
            if (pnl(row) >= 0m)
            {
                break;
            }

            losses++;
            last ??= closedAt(row);
        }

        return (losses, last);
    }

    private static IReadOnlyList<T> WithoutCoveredShards<T>(
        List<T> chosen,
        Func<T, string> symbol,
        Func<T, decimal> quantity,
        Func<T, DateTimeOffset?> closedAt)
    {
        var kept = new List<T>(chosen.Count);
        foreach (var row in chosen)
        {
            if (closedAt(row) is not { } closed || Math.Abs(quantity(row)) > 0m)
            {
                kept.Add(row);
                continue;
            }

            var covered = chosen.Any(other =>
                !ReferenceEquals(other, row)
                && Math.Abs(quantity(other)) > 0m
                && closedAt(other) is { } otherClosed
                && string.Equals(Coin(symbol(other)), Coin(symbol(row)), StringComparison.OrdinalIgnoreCase)
                && (otherClosed - closed).Duration() <= TimeSpan.FromMinutes(2));
            if (!covered)
            {
                kept.Add(row);
            }
        }

        return kept;
    }

    private static bool SameStrategy<T>(Func<T, string?>? strategy, T left, T right)
    {
        if (strategy is null)
        {
            return true;
        }

        return string.Equals(strategy(left)?.Trim(), strategy(right)?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool Prefer<T>(
        T candidate,
        T existing,
        Func<T, string?> correlationId,
        Func<T, decimal>? fees)
    {
        var candidateRank = Rank(correlationId(candidate), fees?.Invoke(candidate) ?? 0m);
        var existingRank = Rank(correlationId(existing), fees?.Invoke(existing) ?? 0m);
        if (candidateRank != existingRank)
        {
            return candidateRank > existingRank;
        }

        var candidateFees = fees?.Invoke(candidate) ?? 0m;
        var existingFees = fees?.Invoke(existing) ?? 0m;
        return candidateFees > existingFees;
    }

    private static int Rank(string? correlationId, decimal fees)
    {
        if (!string.IsNullOrWhiteSpace(correlationId)
            && (correlationId.StartsWith("BNT", StringComparison.OrdinalIgnoreCase)
                || correlationId.StartsWith("BNI", StringComparison.OrdinalIgnoreCase)))
        {
            return 3;
        }

        if (!string.IsNullOrWhiteSpace(correlationId)
            && correlationId.StartsWith("binance-fill", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return fees > 0m ? 1 : 0;
    }

    private static string Coin(string symbol) => symbol.Trim().ToUpperInvariant();
}
