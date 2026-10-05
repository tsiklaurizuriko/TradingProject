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

        return QuantityFromRiskUsdt(equity * (riskPerTradePercent / 100m), price, stopLossPercent);
    }

    public static decimal QuantityFromRiskUsdt(decimal riskUsdt, decimal price, decimal stopLossPercent)
    {
        if (riskUsdt <= 0m || price <= 0m || stopLossPercent <= 0m)
        {
            return 0m;
        }

        var stopDistance = price * (stopLossPercent / 100m);
        return riskUsdt / stopDistance;
    }

    public static decimal IsolatedMargin(decimal notional, decimal leverage)
    {
        if (notional <= 0m)
        {
            return 0m;
        }

        return notional / Math.Max(1m, leverage);
    }

    /// <summary>
    /// Binance one-way Isolated liquidation price for a single position, with wallet balance equal to the initial margin:
    /// long  LP = (Q·EP·(1 − 1/L) − cum) / (Q·(1 − MMR)),
    /// short LP = (Q·EP·(1 + 1/L) + cum) / (Q·(1 + MMR)).
    /// MMR and cum come from the leverage bracket that holds the notional. With MMR = 0 this is the bankruptcy price.
    /// </summary>
    public static decimal IsolatedLiquidationPrice(
        decimal entryPrice,
        decimal quantity,
        decimal leverage,
        bool isShort,
        decimal maintenanceMarginRate,
        decimal maintenanceAmount)
    {
        if (entryPrice <= 0m || quantity <= 0m)
        {
            return 0m;
        }

        var lev = Math.Max(1m, leverage);
        var mmr = Math.Clamp(maintenanceMarginRate, 0m, 0.5m);
        var price = isShort
            ? (quantity * entryPrice * (1m + 1m / lev) + maintenanceAmount) / (quantity * (1m + mmr))
            : (quantity * entryPrice * (1m - 1m / lev) - maintenanceAmount) / (quantity * (1m - mmr));
        return Math.Max(0m, price);
    }

    /// <summary>Distance from entry to liquidation, in percent of entry.</summary>
    public static decimal LiquidationDistancePercent(decimal entryPrice, decimal liquidationPrice) =>
        entryPrice <= 0m ? 0m : Math.Abs(entryPrice - liquidationPrice) / entryPrice * 100m;

    public static decimal FloorToStep(decimal quantity, decimal stepSize, int? quantityPrecision = null)
    {
        if (quantity <= 0m)
        {
            return 0m;
        }

        decimal floored;
        if (stepSize <= 0m)
        {
            floored = decimal.Round(quantity, 8, MidpointRounding.ToZero);
        }
        else
        {
            floored = Math.Floor(quantity / stepSize) * stepSize;
        }

        if (quantityPrecision is >= 0)
        {
            floored = decimal.Round(floored, quantityPrecision.Value, MidpointRounding.ToZero);
        }

        return floored;
    }

    public static decimal CeilToStep(decimal quantity, decimal stepSize, int? quantityPrecision = null)
    {
        if (quantity <= 0m)
        {
            return 0m;
        }

        decimal ceiled;
        if (stepSize <= 0m)
        {
            ceiled = decimal.Round(quantity, 8, MidpointRounding.AwayFromZero);
        }
        else
        {
            ceiled = Math.Ceiling(quantity / stepSize) * stepSize;
        }

        if (quantityPrecision is >= 0)
        {
            var rounded = decimal.Round(ceiled, quantityPrecision.Value, MidpointRounding.AwayFromZero);
            if (rounded + 0m < ceiled && stepSize > 0m)
            {
                rounded = decimal.Round(ceiled + stepSize, quantityPrecision.Value, MidpointRounding.AwayFromZero);
            }

            ceiled = rounded;
        }

        return ceiled;
    }

    public static int? EffectiveQuantityPrecision(int quantityPrecision, decimal stepSize)
    {
        if (quantityPrecision > 0)
        {
            return quantityPrecision;
        }

        return quantityPrecision == 0 && stepSize >= 1m ? 0 : null;
    }
}
