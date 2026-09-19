namespace TradingPlatform.Backtesting;

public static class ReplayMetrics
{
    public static ReplaySideMetrics ForSide(IReadOnlyList<ReplayTrade> trades, string side)
    {
        var rows = trades
            .Where(t => string.Equals(t.Side, side, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.ClosedAt)
            .ToList();
        if (rows.Count == 0)
        {
            return new ReplaySideMetrics(0, 0m, 0m, 0m, 0m, 0m);
        }

        var wins = rows.Where(t => t.PnL > 0m).ToList();
        var losses = rows.Where(t => t.PnL < 0m).ToList();
        var grossWin = wins.Sum(t => t.PnL);
        var grossLoss = Math.Abs(losses.Sum(t => t.PnL));
        var net = rows.Sum(t => t.PnL);
        var pf = grossLoss == 0m ? (grossWin > 0m ? 99m : 0m) : Math.Round(grossWin / grossLoss, 8, MidpointRounding.AwayFromZero);
        var winRate = Math.Round((decimal)wins.Count / rows.Count * 100m, 8, MidpointRounding.AwayFromZero);
        var expectancy = Math.Round(rows.Average(t => t.PnL), 8, MidpointRounding.AwayFromZero);
        var equity = 10_000m;
        var peak = equity;
        var maxDd = 0m;
        foreach (var row in rows)
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

        return new ReplaySideMetrics(
            rows.Count,
            Math.Round(net, 8, MidpointRounding.AwayFromZero),
            winRate,
            pf,
            expectancy,
            Math.Round(maxDd, 8, MidpointRounding.AwayFromZero));
    }
}
