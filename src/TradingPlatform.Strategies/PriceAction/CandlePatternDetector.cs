using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.PriceAction;

/// <summary>
/// Objective candle events. These are FEATURES, not LONG/SHORT signals.
/// Every test at index i uses only candles[0..i].
/// </summary>
public static class CandlePatternDetector
{
    public static List<string>[] Detect(IReadOnlyList<MarketCandle> candles, CandleGeom[] geoms)
    {
        var events = new List<string>[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            events[i] = At(geoms, i);
        }

        return events;
    }

    public static List<string> At(CandleGeom[] geoms, int i)
    {
        var found = new List<string>(8);
        if (i < 0 || i >= geoms.Length)
        {
            return found;
        }

        var g = geoms[i];
        var median = PriceActionMath.MedianRange(geoms, i, 20);
        if (g.BodyRange <= 0.10m)
        {
            found.Add(PatternKinds.Doji);
        }

        if (g.LowerWickBody >= 2m && g.UpperWick <= g.Body && g.Close >= g.Low + g.Range * 0.60m)
        {
            found.Add(PatternKinds.Hammer);
            if (PriorNet(geoms, i, 5) > 0)
            {
                found.Add(PatternKinds.HangingMan);
            }
        }

        if (g.UpperWickBody >= 2m && g.LowerWick <= g.Body && g.Close <= g.High - g.Range * 0.60m)
        {
            found.Add(PatternKinds.InvertedHammer);
            if (PriorNet(geoms, i, 5) > 0)
            {
                found.Add(PatternKinds.ShootingStar);
            }
        }

        if ((g.UpperWickBody >= 2m && g.UpperWick > g.LowerWick) || (g.LowerWickBody >= 2m && g.LowerWick > g.UpperWick))
        {
            found.Add(PatternKinds.PinBar);
        }

        if (g.UpperWickBody >= 2m && g.UpperWick > g.LowerWick * 1.5m)
        {
            found.Add(PatternKinds.UpperWickRejection);
            found.Add(PatternKinds.BearishRejection);
        }

        if (g.LowerWickBody >= 2m && g.LowerWick > g.UpperWick * 1.5m)
        {
            found.Add(PatternKinds.LowerWickRejection);
            found.Add(PatternKinds.BullishRejection);
        }

        if (g.BodyRange >= 0.90m)
        {
            found.Add(PatternKinds.Marubozu);
        }

        if (g.Expansion(median))
        {
            found.Add(PatternKinds.StrongImpulse);
        }

        if (i >= 1)
        {
            var prev = geoms[i - 1];
            if (g.Inside(prev))
            {
                found.Add(PatternKinds.InsideBar);
            }

            if (g.Outside(prev))
            {
                found.Add(PatternKinds.OutsideBar);
            }

            if (g.Bullish && prev.Bearish && g.Open <= prev.Close && g.Close >= prev.Open && g.Body > prev.Body)
            {
                found.Add(PatternKinds.BullishEngulfing);
            }

            if (g.Bearish && prev.Bullish && g.Open >= prev.Close && g.Close <= prev.Open && g.Body > prev.Body)
            {
                found.Add(PatternKinds.BearishEngulfing);
            }

            if (prev.Expansion(median) && !g.Bullish && prev.Bullish && g.Close < prev.Open)
            {
                found.Add(PatternKinds.FailedContinuation);
            }

            if (prev.Expansion(median) && !g.Bearish && prev.Bearish && g.Close > prev.Open)
            {
                found.Add(PatternKinds.FailedContinuation);
            }
        }

        if (i >= 2)
        {
            var a = geoms[i - 2];
            var b = geoms[i - 1];
            if (a.Bearish && b.Bearish && g.Bullish && g.Close > a.Open)
            {
                found.Add(PatternKinds.ThreeBarReversal);
            }

            if (a.Bullish && b.Bullish && g.Bearish && g.Close < a.Open)
            {
                found.Add(PatternKinds.ThreeBarReversal);
            }
        }

        return found;
    }

    private static decimal PriorNet(CandleGeom[] geoms, int i, int n)
    {
        var from = Math.Max(0, i - n);
        return geoms[i].Close - geoms[from].Close;
    }
}
