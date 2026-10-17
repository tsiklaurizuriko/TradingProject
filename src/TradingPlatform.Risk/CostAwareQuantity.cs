namespace TradingPlatform.Risk;

/// <summary>
/// Sizes a linear USDT-margined contract so price risk, fees, slippage, and an optional funding loss
/// fit inside the planned loss. Quantity is floored to the step. It is never rounded up through a minimum.
/// </summary>
public static class CostAwareQuantity
{
    public static decimal? Solve(
        decimal equity,
        decimal riskFraction,
        decimal entryPrice,
        decimal stopDistanceFraction,
        decimal feeRate,
        decimal slippageRate,
        decimal fundingLossPerUnit,
        decimal stepSize,
        decimal minQuantity,
        decimal minNotional,
        int? quantityPrecision)
    {
        if (equity <= 0m || riskFraction <= 0m || entryPrice <= 0m || stopDistanceFraction <= 0m)
        {
            return null;
        }

        if (feeRate < 0m || slippageRate < 0m || fundingLossPerUnit < 0m)
        {
            return null;
        }

        var priceLoss = entryPrice * stopDistanceFraction;
        var feeLoss = entryPrice * feeRate * 2m;
        var slipLoss = entryPrice * slippageRate * 2m;
        var perUnit = priceLoss + feeLoss + slipLoss + fundingLossPerUnit;
        if (perUnit <= 0m)
        {
            return null;
        }

        var raw = equity * riskFraction / perUnit;
        var quantity = PortfolioRisk.FloorToStep(raw, stepSize, quantityPrecision);
        if (quantity <= 0m)
        {
            return null;
        }

        if (minQuantity > 0m && quantity < minQuantity)
        {
            return null;
        }

        if (minNotional > 0m && quantity * entryPrice < minNotional)
        {
            return null;
        }

        var used = quantity * perUnit;
        return used <= equity * riskFraction + 0.00000001m ? quantity : null;
    }
}
