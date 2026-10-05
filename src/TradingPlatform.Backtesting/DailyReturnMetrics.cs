namespace TradingPlatform.Backtesting;

public sealed record DailyRatios(decimal? Sharpe, decimal? Sortino, int Days);

/// <summary>
/// Sharpe and Sortino from end-of-day mark-to-market equity, annualized with sqrt(365) (crypto trades every day).
/// A per-trade Sharpe scaled by sqrt(trade count) grows with trade count and is not comparable across strategies.
/// </summary>
public static class DailyReturnMetrics
{
    public const int MinimumDays = 3;
    private static readonly decimal Annualizer = (decimal)Math.Sqrt(365d);

    /// <summary>Simple daily returns; the first day is measured from <paramref name="initialEquity"/>.</summary>
    public static IReadOnlyList<decimal> Returns(IReadOnlyDictionary<DateOnly, decimal> closeByDay, decimal initialEquity)
    {
        var returns = new List<decimal>(closeByDay.Count);
        var previous = initialEquity;
        foreach (var (_, close) in closeByDay.OrderBy(pair => pair.Key))
        {
            if (previous > 0m)
            {
                returns.Add(close / previous - 1m);
            }

            previous = close;
        }

        return returns;
    }

    public static DailyRatios From(IReadOnlyList<decimal> returns)
    {
        if (returns.Count < MinimumDays)
        {
            return new DailyRatios(null, null, returns.Count);
        }

        var mean = returns.Average();
        var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
        var stdev = Sqrt(variance);
        var downside = Sqrt(returns.Sum(r => r < 0m ? r * r : 0m) / returns.Count);
        return new DailyRatios(
            stdev > 0m ? Math.Round(mean / stdev * Annualizer, 4) : null,
            downside > 0m ? Math.Round(mean / downside * Annualizer, 4) : null,
            returns.Count);
    }

    /// <summary>Annualized compound return divided by max drawdown (both percent). Null without a drawdown or a year fraction.</summary>
    public static decimal? Calmar(decimal totalReturnPercent, int days, decimal maxDrawdownPercent)
    {
        if (days <= 0 || maxDrawdownPercent <= 0m || totalReturnPercent <= -100m)
        {
            return null;
        }

        var growth = 1d + (double)totalReturnPercent / 100d;
        var annualized = (Math.Pow(growth, 365d / days) - 1d) * 100d;
        if (double.IsNaN(annualized) || double.IsInfinity(annualized) || Math.Abs(annualized) > 1e12)
        {
            return null;
        }

        return Math.Round((decimal)annualized / maxDrawdownPercent, 4);
    }

    private static decimal Sqrt(decimal value) => (decimal)Math.Sqrt((double)Math.Max(value, 0m));
}
