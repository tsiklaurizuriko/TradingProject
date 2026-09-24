namespace TradingPlatform.Research;

public sealed record SymbolRobustnessRow(
    string Symbol,
    int Observations,
    int LongSignals,
    int ShortSignals,
    decimal GrossReturn,
    decimal NetReturn,
    decimal Expectancy,
    decimal WinRate,
    string ProfitFactor,
    decimal TopDecileAppearances,
    decimal BottomDecileAppearances,
    decimal ConcentrationShare,
    string Label);

public static class CrossSectionalRobustness
{
    public static string ProfitFactor(decimal wins, decimal losses)
    {
        if (losses == 0m)
        {
            return wins > 0m ? "NO_LOSSES" : "NO_TRADES";
        }

        return (wins / Math.Abs(losses)).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string LabelOf(int observations, int positiveSplits, int splits)
    {
        if (observations < 30 || splits < 2)
        {
            return "INSUFFICIENT_DATA";
        }

        if (positiveSplits == splits)
        {
            return "CONSISTENT";
        }

        if (positiveSplits == 0)
        {
            return "WEAK";
        }

        return "MIXED";
    }

    public static decimal Herfindahl(IReadOnlyList<decimal> weights)
    {
        decimal sum = 0m;
        foreach (var weight in weights)
        {
            sum += weight * weight;
        }

        return sum;
    }

    public static (decimal Gross, decimal Fees, decimal Slippage, decimal Funding, decimal Net) ApplyCosts(
        decimal gross,
        decimal feeRate,
        decimal slippageRate,
        decimal funding,
        decimal costMultiplier)
    {
        var fees = gross == 0m && feeRate == 0m ? 0m : feeRate * costMultiplier;
        var slip = slippageRate * costMultiplier;
        var net = gross - fees - slip - funding;
        return (gross, fees, slip, funding, net);
    }

    public static decimal? BreakEvenRoundTrip(decimal grossPerHold, decimal turnoverLegs)
    {
        if (turnoverLegs <= 0m || grossPerHold <= 0m)
        {
            return grossPerHold <= 0m ? 0m : null;
        }

        return grossPerHold / turnoverLegs;
    }
}
