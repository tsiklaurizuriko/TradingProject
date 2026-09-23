using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.PriceAction;

/// <summary>
/// Wick beyond a confirmed swing, close back through the level. Detection time is the reclaim close.
/// Subsequent reclaim/continuation is a later event, not rewritten onto the sweep bar.
/// </summary>
public static class LiquiditySweepEngine
{
    public static List<PatternOccurrence> Detect(
        IReadOnlyList<MarketCandle> candles,
        IReadOnlyList<SwingPoint> highs,
        IReadOnlyList<SwingPoint> lows)
    {
        var found = new List<PatternOccurrence>();
        var hiPtr = 0;
        var loPtr = 0;
        SwingPoint? lastHi = null;
        SwingPoint? lastLo = null;
        for (var i = 0; i < candles.Count; i++)
        {
            while (hiPtr < highs.Count && highs[hiPtr].ConfirmationIndex <= i)
            {
                lastHi = highs[hiPtr++];
            }

            while (loPtr < lows.Count && lows[loPtr].ConfirmationIndex <= i)
            {
                lastLo = lows[loPtr++];
            }

            var c = candles[i];
            if (lastHi is { } hi && c.High > hi.Price && c.Close < hi.Price && i > hi.ConfirmationIndex)
            {
                found.Add(new PatternOccurrence(
                    PatternKinds.LiquiditySweepHigh,
                    "v1",
                    hi.PivotIndex,
                    i,
                    i,
                    i,
                    hi.Price,
                    hi.Price,
                    "BEARISH",
                    PatternKinds.Confirmed,
                    [new PatternPoint(hi.PivotIndex, hi.Price, "level"), new PatternPoint(i, c.Close, "sweep")]));
            }

            if (lastLo is { } lo && c.Low < lo.Price && c.Close > lo.Price && i > lo.ConfirmationIndex)
            {
                found.Add(new PatternOccurrence(
                    PatternKinds.LiquiditySweepLow,
                    "v1",
                    lo.PivotIndex,
                    i,
                    i,
                    i,
                    lo.Price,
                    lo.Price,
                    "BULLISH",
                    PatternKinds.Confirmed,
                    [new PatternPoint(lo.PivotIndex, lo.Price, "level"), new PatternPoint(i, c.Close, "sweep")]));
            }
        }

        return found;
    }
}
