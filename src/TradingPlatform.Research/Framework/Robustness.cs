namespace TradingPlatform.Research.Framework;

public sealed record BootstrapSummary(
    int Draws,
    int BlockLength,
    double MedianReturnPercent,
    double P5ReturnPercent,
    double P95ReturnPercent,
    double MedianMaxDrawdownPercent,
    double P95MaxDrawdownPercent,
    double ProbabilityOfLoss);

public sealed record ShuffleSummary(int Draws, double ObservedMaxDrawdownPercent, double MedianMaxDrawdownPercent, double P95MaxDrawdownPercent);

public sealed record StabilityScore(int Neighbours, double Score, bool Stable, decimal WorstProfitFactor);

public static class Robustness
{
    public const double PerturbationFraction = 0.20;
    public const double StableScore = 0.70;
    public const decimal MaxRelativePfDrop = 0.30m;

    /// <summary>Stationary-block bootstrap of daily returns (fractions). Blocks keep short-range autocorrelation.</summary>
    public static BootstrapSummary BlockBootstrap(IReadOnlyList<double> dailyReturns, int blockLength = 5, int draws = 1000, int seed = 7)
    {
        var n = dailyReturns.Count;
        if (n == 0 || draws <= 0)
        {
            return new BootstrapSummary(0, blockLength, 0, 0, 0, 0, 0, 0);
        }

        var block = Math.Clamp(blockLength, 1, n);
        var rng = new Random(seed);
        var totals = new double[draws];
        var drawdowns = new double[draws];
        var path = new double[n];
        for (var d = 0; d < draws; d++)
        {
            var filled = 0;
            while (filled < n)
            {
                var start = rng.Next(n);
                for (var k = 0; k < block && filled < n; k++)
                {
                    path[filled++] = dailyReturns[(start + k) % n];
                }
            }

            (totals[d], drawdowns[d]) = Compound(path);
        }

        Array.Sort(totals);
        Array.Sort(drawdowns);
        return new BootstrapSummary(
            draws,
            block,
            Percentile(totals, 0.50),
            Percentile(totals, 0.05),
            Percentile(totals, 0.95),
            Percentile(drawdowns, 0.50),
            Percentile(drawdowns, 0.95),
            totals.Count(t => t < 0d) / (double)draws);
    }

    /// <summary>Shuffles trade order to show how much of the observed drawdown is path luck.</summary>
    public static ShuffleSummary ShuffleDrawdown(IReadOnlyList<decimal> tradePnls, decimal initialEquity, int draws = 1000, int seed = 11)
    {
        if (tradePnls.Count == 0 || initialEquity <= 0m || draws <= 0)
        {
            return new ShuffleSummary(0, 0, 0, 0);
        }

        var observed = PnlDrawdown(tradePnls, initialEquity);
        var rng = new Random(seed);
        var work = tradePnls.ToArray();
        var drawdowns = new double[draws];
        for (var d = 0; d < draws; d++)
        {
            for (var i = work.Length - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (work[i], work[j]) = (work[j], work[i]);
            }

            drawdowns[d] = PnlDrawdown(work, initialEquity);
        }

        Array.Sort(drawdowns);
        return new ShuffleSummary(draws, observed, Percentile(drawdowns, 0.50), Percentile(drawdowns, 0.95));
    }

    /// <summary>±20% neighbours for a numeric parameter. Integers are rounded and kept distinct from the base.</summary>
    public static IReadOnlyList<decimal> Neighbours(decimal value, bool integer, double fraction = PerturbationFraction)
    {
        var f = (decimal)fraction;
        var candidates = new[] { value * (1m - f), value * (1m + f) };
        var result = new List<decimal>();
        foreach (var c in candidates)
        {
            var v = c;
            if (integer)
            {
                v = Math.Round(v, MidpointRounding.AwayFromZero);
                if (v == value)
                {
                    v = c < value ? value - 1m : value + 1m;
                }

                if (v < 1m)
                {
                    continue;
                }
            }

            if (v != value && !result.Contains(v))
            {
                result.Add(v);
            }
        }

        return result;
    }

    /// <summary>
    /// A neighbour holds when its PF stays ≥ 1 and drops at most 30% from the base PF.
    /// Stable when at least 70% of neighbours hold.
    /// </summary>
    public static StabilityScore Stability(decimal baseProfitFactor, IReadOnlyList<decimal> neighbourProfitFactors)
    {
        if (neighbourProfitFactors.Count == 0)
        {
            return new StabilityScore(0, 0d, false, 0m);
        }

        var floor = baseProfitFactor * (1m - MaxRelativePfDrop);
        var held = neighbourProfitFactors.Count(pf => pf >= 1m && pf >= floor);
        var score = held / (double)neighbourProfitFactors.Count;
        return new StabilityScore(neighbourProfitFactors.Count, score, score >= StableScore, neighbourProfitFactors.Min());
    }

    /// <summary>Share of coins with trades whose net PnL is positive.</summary>
    public static decimal Breadth(IEnumerable<(int Trades, decimal NetPnl)> perCoin)
    {
        var traded = perCoin.Where(c => c.Trades > 0).ToList();
        return traded.Count == 0 ? 0m : traded.Count(c => c.NetPnl > 0m) / (decimal)traded.Count;
    }

    private static (double TotalPercent, double MaxDrawdownPercent) Compound(IReadOnlyList<double> returns)
    {
        var equity = 1d;
        var peak = 1d;
        var maxDd = 0d;
        foreach (var r in returns)
        {
            equity *= 1d + r;
            peak = Math.Max(peak, equity);
            if (peak > 0d)
            {
                maxDd = Math.Max(maxDd, (peak - equity) / peak);
            }
        }

        return ((equity - 1d) * 100d, maxDd * 100d);
    }

    private static double PnlDrawdown(IReadOnlyList<decimal> pnls, decimal initialEquity)
    {
        var equity = initialEquity;
        var peak = initialEquity;
        var maxDd = 0m;
        foreach (var p in pnls)
        {
            equity += p;
            peak = Math.Max(peak, equity);
            if (peak > 0m)
            {
                maxDd = Math.Max(maxDd, (peak - equity) / peak);
            }
        }

        return (double)(maxDd * 100m);
    }

    private static double Percentile(double[] sorted, double q)
    {
        if (sorted.Length == 0)
        {
            return 0d;
        }

        var idx = (int)Math.Round(q * (sorted.Length - 1), MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }
}
