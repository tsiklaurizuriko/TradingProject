namespace TradingPlatform.Risk;

public static class PortfolioRisk
{
    /// <summary>
    /// Variance-style portfolio heat: sqrt(Σ Σ ρ_ij r_i r_j) with ρ_ii = 1.
    /// Perfect correlation → sum of risks. Zero correlation → sqrt(sum of squares).
    /// </summary>
    public static decimal CorrelatedHeat(IReadOnlyList<decimal> riskFractions, decimal correlation)
    {
        if (riskFractions.Count == 0)
        {
            return 0m;
        }

        var rho = Math.Clamp(correlation, 0m, 1m);
        double sum = 0;
        for (var i = 0; i < riskFractions.Count; i++)
        {
            for (var j = 0; j < riskFractions.Count; j++)
            {
                var pair = i == j ? 1m : rho;
                sum += (double)(pair * riskFractions[i] * riskFractions[j]);
            }
        }

        return sum <= 0 ? 0m : (decimal)Math.Sqrt(sum);
    }

    public static decimal RiskFraction(decimal quantity, decimal price, decimal stopLossPercent, decimal equity)
    {
        if (equity <= 0m || price <= 0m || quantity <= 0m || stopLossPercent <= 0m)
        {
            return 0m;
        }

        return quantity * price * (stopLossPercent / 100m) / equity;
    }

    public static decimal QuantityFromR(decimal equity, decimal riskPerTradePercent, decimal price, decimal stopLossPercent)
    {
        if (equity <= 0m || price <= 0m || stopLossPercent <= 0m || riskPerTradePercent <= 0m)
        {
            return 0m;
        }

        var dollarsAtRisk = equity * (riskPerTradePercent / 100m);
        var stopDistance = price * (stopLossPercent / 100m);
        return dollarsAtRisk / stopDistance;
    }
}
