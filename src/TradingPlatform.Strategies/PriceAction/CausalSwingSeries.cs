using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.PriceAction;

/// <summary>
/// Causal swing points. A pivot at k is written only at confirmation index k+n,
/// matching <c>AlphaIndicatorSeries.ConfirmedSwingLow/High</c>.
/// </summary>
public static class CausalSwingSeries
{
    public static (List<SwingPoint> Highs, List<SwingPoint> Lows) Detect(IReadOnlyList<MarketCandle> candles, int n)
    {
        n = Math.Max(2, n);
        var highs = new List<SwingPoint>();
        var lows = new List<SwingPoint>();
        for (var i = 0; i < candles.Count; i++)
        {
            var k = i - n;
            if (k < n)
            {
                continue;
            }

            var lo = candles[k].Low;
            var hi = candles[k].High;
            var isLow = true;
            var isHigh = true;
            for (var j = k - n; j <= k + n; j++)
            {
                if (j == k)
                {
                    continue;
                }

                if (candles[j].Low < lo)
                {
                    isLow = false;
                }

                if (candles[j].High > hi)
                {
                    isHigh = false;
                }
            }

            if (isLow)
            {
                lows.Add(new SwingPoint(k, i, lo, false));
            }

            if (isHigh)
            {
                highs.Add(new SwingPoint(k, i, hi, true));
            }
        }

        return (highs, lows);
    }

    public static List<SwingPoint> KnownAt(IReadOnlyList<SwingPoint> all, int i)
    {
        var result = new List<SwingPoint>();
        foreach (var p in all)
        {
            if (p.ConfirmationIndex <= i)
            {
                result.Add(p);
            }
        }

        return result;
    }

    public static SwingPoint? LastKnown(IReadOnlyList<SwingPoint> all, int i)
    {
        for (var k = all.Count - 1; k >= 0; k--)
        {
            if (all[k].ConfirmationIndex <= i)
            {
                return all[k];
            }
        }

        return null;
    }
}
