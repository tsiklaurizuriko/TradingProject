namespace TradingPlatform.Backtesting;

public static class ReplayMetrics
{
    public static ReplaySideMetrics ForSide(IReadOnlyList<ReplayTrade> trades, string side)
    {
        var rows = trades
            .Where(t => string.Equals(t.Side, side, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.ClosedAt)
            .ToList();
        var totals = PnlTotals.FromTrades(rows);
        var maxDd = ClosedPnlDrawdown(rows, 10_000m);
        return FromTotals(totals, maxDd);
    }

    public static ReplaySideMetrics FromTotals(PnlTotals totals, decimal maximumDrawdown)
    {
        var pf = totals.ProfitFactor;
        return new ReplaySideMetrics(
            totals.Trades,
            Math.Round(totals.NetPnl, 8, MidpointRounding.AwayFromZero),
            totals.WinRate,
            pf.IsFinite ? pf.Ratio : 0m,
            totals.Expectancy,
            Math.Round(maximumDrawdown, 8, MidpointRounding.AwayFromZero),
            totals.PositivePnlSum,
            totals.AbsoluteNegativePnlSum,
            totals.WinningTrades,
            totals.LosingTrades,
            totals.ZeroPnlTrades,
            totals.Fees);
    }

    public static decimal ClosedPnlDrawdown(IReadOnlyList<ReplayTrade> trades, decimal initial)
    {
        var equity = initial;
        var peak = equity;
        var maxDd = 0m;
        foreach (var row in trades)
        {
            equity += row.PnL;
            if (equity > peak)
            {
                peak = equity;
            }

            if (peak > 0m)
            {
                var dd = (peak - equity) / peak * 100m;
                if (dd > maxDd)
                {
                    maxDd = dd;
                }
            }
        }

        return maxDd;
    }

    public static ReplaySideMetrics CombineSides(ReplaySideMetrics left, ReplaySideMetrics right)
    {
        var totals = PnlTotals.Combine(
            SideTotals(left),
            SideTotals(right));
        return FromTotals(totals, Math.Max(left.MaximumDrawdown, right.MaximumDrawdown));
    }

    public static PnlTotals SideTotals(ReplaySideMetrics side) =>
        new(
            side.Trades,
            side.WinningTrades,
            side.LosingTrades,
            side.ZeroPnlTrades,
            side.PositivePnlSum,
            side.AbsoluteNegativePnlSum,
            side.NetPnL,
            side.Fees);
}
