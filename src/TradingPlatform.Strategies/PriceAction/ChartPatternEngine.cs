using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.PriceAction;

/// <summary>
/// Objective chart-pattern detection. DetectionIndex is when the structure was knowable.
/// ConfirmationIndex is the first close that actually breaks the neckline/range (never earlier).
/// Cup &amp; Handle is not implemented.
/// </summary>
public static class ChartPatternEngine
{
    public const string Version = "v1";
    private const decimal Similarity = 0.006m;
    private const int MinSeparation = 6;
    private const int FlagImpulseMin = 3;
    private const int FlagConsolMin = 3;
    private const int FlagConsolMax = 18;

    public static List<PatternOccurrence> Detect(
        IReadOnlyList<MarketCandle> candles,
        CandleGeom[] geoms,
        IReadOnlyList<SwingPoint> highs,
        IReadOnlyList<SwingPoint> lows)
    {
        var found = new List<PatternOccurrence>();
        DetectWm(candles, highs, lows, found);
        DetectHs(candles, highs, lows, found);
        DetectFlagsAndPennants(candles, geoms, found);
        DetectRanges(candles, highs, lows, found);
        DetectBreakoutRetest(candles, found);
        return found;
    }

    private static void DetectWm(
        IReadOnlyList<MarketCandle> candles,
        IReadOnlyList<SwingPoint> highs,
        IReadOnlyList<SwingPoint> lows,
        List<PatternOccurrence> found)
    {
        var pendingW = new List<PatternOccurrence>();
        var pendingM = new List<PatternOccurrence>();
        var hiPtr = 0;
        var loPtr = 0;
        var knownHi = new List<SwingPoint>();
        var knownLo = new List<SwingPoint>();
        for (var i = 0; i < candles.Count; i++)
        {
            while (hiPtr < highs.Count && highs[hiPtr].ConfirmationIndex <= i)
            {
                knownHi.Add(highs[hiPtr++]);
                if (knownHi.Count >= 2)
                {
                    TryM(knownHi, knownLo, i, pendingM);
                }
            }

            while (loPtr < lows.Count && lows[loPtr].ConfirmationIndex <= i)
            {
                knownLo.Add(lows[loPtr++]);
                if (knownLo.Count >= 2)
                {
                    TryW(knownLo, knownHi, i, pendingW);
                }
            }

            ConfirmPending(pendingW, candles, i, found, true);
            ConfirmPending(pendingM, candles, i, found, false);
        }

        foreach (var row in pendingW.Concat(pendingM))
        {
            found.Add(row);
        }
    }

    private static void TryW(List<SwingPoint> lows, List<SwingPoint> highs, int i, List<PatternOccurrence> pending)
    {
        var a = lows[^2];
        var b = lows[^1];
        if (b.PivotIndex - a.PivotIndex < MinSeparation)
        {
            return;
        }

        var mid = (a.Price + b.Price) / 2m;
        if (mid <= 0 || Math.Abs(a.Price - b.Price) / mid > Similarity)
        {
            return;
        }

        SwingPoint? neck = null;
        for (var index = highs.Count - 1; index >= 0; index--)
        {
            var h = highs[index];
            if (h.PivotIndex <= a.PivotIndex)
            {
                break;
            }

            if (h.PivotIndex > a.PivotIndex && h.PivotIndex < b.PivotIndex)
            {
                if (neck is null || h.Price > neck.Value.Price)
                {
                    neck = h;
                }
            }
        }

        if (neck is null)
        {
            return;
        }

        pending.Add(new PatternOccurrence(
            PatternKinds.WDoubleBottom,
            Version,
            a.PivotIndex,
            b.ConfirmationIndex,
            null,
            null,
            neck.Value.Price,
            neck.Value.Price,
            "BULLISH",
            PatternKinds.Detected,
            [
                new PatternPoint(a.PivotIndex, a.Price, "low1"),
                new PatternPoint(neck.Value.PivotIndex, neck.Value.Price, "neckline"),
                new PatternPoint(b.PivotIndex, b.Price, "low2")
            ]));
    }

    private static void TryM(List<SwingPoint> highs, List<SwingPoint> lows, int i, List<PatternOccurrence> pending)
    {
        var a = highs[^2];
        var b = highs[^1];
        if (b.PivotIndex - a.PivotIndex < MinSeparation)
        {
            return;
        }

        var mid = (a.Price + b.Price) / 2m;
        if (mid <= 0 || Math.Abs(a.Price - b.Price) / mid > Similarity)
        {
            return;
        }

        SwingPoint? neck = null;
        for (var index = lows.Count - 1; index >= 0; index--)
        {
            var l = lows[index];
            if (l.PivotIndex <= a.PivotIndex)
            {
                break;
            }

            if (l.PivotIndex > a.PivotIndex && l.PivotIndex < b.PivotIndex)
            {
                if (neck is null || l.Price < neck.Value.Price)
                {
                    neck = l;
                }
            }
        }

        if (neck is null)
        {
            return;
        }

        pending.Add(new PatternOccurrence(
            PatternKinds.MDoubleTop,
            Version,
            a.PivotIndex,
            b.ConfirmationIndex,
            null,
            null,
            neck.Value.Price,
            neck.Value.Price,
            "BEARISH",
            PatternKinds.Detected,
            [
                new PatternPoint(a.PivotIndex, a.Price, "high1"),
                new PatternPoint(neck.Value.PivotIndex, neck.Value.Price, "neckline"),
                new PatternPoint(b.PivotIndex, b.Price, "high2")
            ]));
    }

    private static void ConfirmPending(
        List<PatternOccurrence> pending,
        IReadOnlyList<MarketCandle> candles,
        int i,
        List<PatternOccurrence> found,
        bool bullish)
    {
        for (var p = pending.Count - 1; p >= 0; p--)
        {
            var row = pending[p];
            if (i <= row.DetectionIndex || row.Neckline is not { } neck)
            {
                continue;
            }

            var hit = bullish ? candles[i].Close > neck : candles[i].Close < neck;
            var fail = bullish ? candles[i].Close < row.Points[0].Price : candles[i].Close > row.Points[0].Price;
            if (hit)
            {
                found.Add(row with
                {
                    ConfirmationIndex = i,
                    EntryTriggerIndex = i,
                    Status = PatternKinds.Confirmed
                });
                pending.RemoveAt(p);
            }
            else if (fail)
            {
                found.Add(row with { Status = PatternKinds.Failed });
                pending.RemoveAt(p);
            }
        }
    }

    private static void DetectHs(
        IReadOnlyList<MarketCandle> candles,
        IReadOnlyList<SwingPoint> highs,
        IReadOnlyList<SwingPoint> lows,
        List<PatternOccurrence> found)
    {
        var pending = new List<(PatternOccurrence Occ, bool Inverse)>();
        var hiPtr = 0;
        var loPtr = 0;
        var knownHi = new List<SwingPoint>();
        var knownLo = new List<SwingPoint>();
        for (var i = 0; i < candles.Count; i++)
        {
            while (hiPtr < highs.Count && highs[hiPtr].ConfirmationIndex <= i)
            {
                knownHi.Add(highs[hiPtr++]);
                if (knownHi.Count >= 3 && knownLo.Count >= 2)
                {
                    TryHs(knownHi, knownLo, false, pending);
                }
            }

            while (loPtr < lows.Count && lows[loPtr].ConfirmationIndex <= i)
            {
                knownLo.Add(lows[loPtr++]);
                if (knownLo.Count >= 3 && knownHi.Count >= 2)
                {
                    TryHs(knownLo, knownHi, true, pending);
                }
            }

            for (var p = pending.Count - 1; p >= 0; p--)
            {
                var (occ, inverse) = pending[p];
                if (i <= occ.DetectionIndex || occ.Neckline is not { } neck)
                {
                    continue;
                }

                var hit = inverse ? candles[i].Close > neck : candles[i].Close < neck;
                if (hit)
                {
                    found.Add(occ with { ConfirmationIndex = i, EntryTriggerIndex = i, Status = PatternKinds.Confirmed });
                    pending.RemoveAt(p);
                }
            }
        }

        foreach (var (occ, _) in pending)
        {
            found.Add(occ);
        }
    }

    private static void TryHs(
        List<SwingPoint> extremes,
        List<SwingPoint> opposite,
        bool inverse,
        List<(PatternOccurrence, bool)> pending)
    {
        var ls = extremes[^3];
        var head = extremes[^2];
        var rs = extremes[^1];
        if (rs.PivotIndex - ls.PivotIndex < MinSeparation * 2)
        {
            return;
        }

        if (!inverse && !(head.Price > ls.Price && head.Price > rs.Price))
        {
            return;
        }

        if (inverse && !(head.Price < ls.Price && head.Price < rs.Price))
        {
            return;
        }

        var mid = (ls.Price + rs.Price) / 2m;
        if (mid <= 0 || Math.Abs(ls.Price - rs.Price) / mid > 0.02m)
        {
            return;
        }

        SwingPoint? v1 = null;
        SwingPoint? v2 = null;
        for (var index = opposite.Count - 1; index >= 0; index--)
        {
            var o = opposite[index];
            if (o.PivotIndex <= ls.PivotIndex)
            {
                break;
            }

            if (o.PivotIndex > head.PivotIndex && o.PivotIndex < rs.PivotIndex)
            {
                v2 ??= o;
            }
            else if (o.PivotIndex > ls.PivotIndex && o.PivotIndex < head.PivotIndex)
            {
                v1 ??= o;
            }

            if (v1 is not null && v2 is not null)
            {
                break;
            }
        }

        if (v1 is null || v2 is null)
        {
            return;
        }

        var neck = inverse ? Math.Max(v1.Value.Price, v2.Value.Price) : Math.Min(v1.Value.Price, v2.Value.Price);
        var kind = inverse ? PatternKinds.InverseHeadShoulders : PatternKinds.HeadShoulders;
        pending.Add((new PatternOccurrence(
            kind,
            Version,
            ls.PivotIndex,
            rs.ConfirmationIndex,
            null,
            null,
            neck,
            neck,
            inverse ? "BULLISH" : "BEARISH",
            PatternKinds.Detected,
            [
                new PatternPoint(ls.PivotIndex, ls.Price, "left"),
                new PatternPoint(head.PivotIndex, head.Price, "head"),
                new PatternPoint(rs.PivotIndex, rs.Price, "right"),
                new PatternPoint(v1.Value.PivotIndex, v1.Value.Price, "neck1"),
                new PatternPoint(v2.Value.PivotIndex, v2.Value.Price, "neck2")
            ]), inverse));
    }

    private static void DetectFlagsAndPennants(
        IReadOnlyList<MarketCandle> candles,
        CandleGeom[] geoms,
        List<PatternOccurrence> found)
    {
        for (var i = FlagImpulseMin + FlagConsolMin; i < candles.Count; i++)
        {
            var median = PriceActionMath.MedianRange(geoms, i, 20);
            if (median <= 0)
            {
                continue;
            }

            for (var consol = FlagConsolMin; consol <= FlagConsolMax && i - consol - FlagImpulseMin >= 0; consol++)
            {
                var impulseEnd = i - consol;
                var impulseStart = impulseEnd - FlagImpulseMin;
                while (impulseStart > 0 && impulseEnd - impulseStart < 12 && geoms[impulseStart].Bullish == geoms[impulseEnd - 1].Bullish)
                {
                    impulseStart--;
                }

                impulseStart = Math.Max(0, impulseStart);
                var impulseBars = impulseEnd - impulseStart;
                if (impulseBars < FlagImpulseMin)
                {
                    continue;
                }

                var impulseNet = candles[impulseEnd - 1].Close - candles[impulseStart].Close;
                var impulseRange = PriceActionMath.RollingHigh(candles, impulseEnd - 1, impulseBars)
                    - PriceActionMath.RollingLow(candles, impulseEnd - 1, impulseBars);
                if (impulseRange < median * 2m)
                {
                    continue;
                }

                var consolHi = PriceActionMath.RollingHigh(candles, i - 1, consol);
                var consolLo = PriceActionMath.RollingLow(candles, i - 1, consol);
                var consolRange = consolHi - consolLo;
                if (consolRange <= 0 || consolRange > impulseRange * 0.55m)
                {
                    continue;
                }

                var firstHalf = Math.Max(2, consol / 2);
                var early = PriceActionMath.RollingHigh(candles, impulseEnd + firstHalf - 1, firstHalf)
                    - PriceActionMath.RollingLow(candles, impulseEnd + firstHalf - 1, firstHalf);
                var late = PriceActionMath.RollingHigh(candles, i - 1, consol - firstHalf)
                    - PriceActionMath.RollingLow(candles, i - 1, consol - firstHalf);
                var contracting = late < early * 0.85m;
                var bullishImpulse = impulseNet > 0;
                var brokeUp = candles[i].Close > consolHi && candles[i - 1].Close <= consolHi;
                var brokeDn = candles[i].Close < consolLo && candles[i - 1].Close >= consolLo;
                if (!brokeUp && !brokeDn)
                {
                    continue;
                }

                string kind;
                string dir;
                if (contracting)
                {
                    kind = PatternKinds.Pennant;
                    dir = brokeUp ? "BULLISH" : "BEARISH";
                }
                else if (bullishImpulse && brokeUp)
                {
                    kind = PatternKinds.BullFlag;
                    dir = "BULLISH";
                }
                else if (!bullishImpulse && brokeDn)
                {
                    kind = PatternKinds.BearFlag;
                    dir = "BEARISH";
                }
                else
                {
                    continue;
                }

                found.Add(new PatternOccurrence(
                    kind,
                    Version,
                    impulseStart,
                    i,
                    i,
                    i,
                    brokeUp ? consolHi : consolLo,
                    brokeUp ? consolHi : consolLo,
                    dir,
                    PatternKinds.Confirmed,
                    [
                        new PatternPoint(impulseStart, candles[impulseStart].Close, "impulse"),
                        new PatternPoint(impulseEnd - 1, candles[impulseEnd - 1].Close, "flagpole"),
                        new PatternPoint(i, candles[i].Close, "breakout")
                    ]));
                break;
            }
        }
    }

    private static void DetectRanges(
        IReadOnlyList<MarketCandle> candles,
        IReadOnlyList<SwingPoint> highs,
        IReadOnlyList<SwingPoint> lows,
        List<PatternOccurrence> found)
    {
        var hiPtr = 0;
        var loPtr = 0;
        var knownHi = new List<SwingPoint>();
        var knownLo = new List<SwingPoint>();
        for (var i = 0; i < candles.Count; i++)
        {
            while (hiPtr < highs.Count && highs[hiPtr].ConfirmationIndex <= i)
            {
                knownHi.Add(highs[hiPtr++]);
            }

            while (loPtr < lows.Count && lows[loPtr].ConfirmationIndex <= i)
            {
                knownLo.Add(lows[loPtr++]);
            }

            if (knownHi.Count < 3 || knownLo.Count < 3 || i < 1)
            {
                continue;
            }

            var hs = knownHi.TakeLast(3).ToArray();
            var ls = knownLo.TakeLast(3).ToArray();
            var start = Math.Min(hs[0].PivotIndex, ls[0].PivotIndex);
            if (i - start < 12)
            {
                continue;
            }

            var hiSlope = Slope(hs);
            var loSlope = Slope(ls);
            var hiFlat = Flat(hs);
            var loFlat = Flat(ls);
            var hiMax = hs.Max(p => p.Price);
            var hiMin = hs.Min(p => p.Price);
            var loMax = ls.Max(p => p.Price);
            var loMin = ls.Min(p => p.Price);
            var brokeUp = candles[i].Close > hiMax && candles[i - 1].Close <= hiMax;
            var brokeDn = candles[i].Close < loMin && candles[i - 1].Close >= loMin;
            if (!brokeUp && !brokeDn)
            {
                continue;
            }

            string? kind = null;
            if (hiFlat && loFlat)
            {
                kind = PatternKinds.Rectangle;
            }
            else if (hiFlat && loSlope > 0)
            {
                kind = PatternKinds.AscendingTriangle;
            }
            else if (loFlat && hiSlope < 0)
            {
                kind = PatternKinds.DescendingTriangle;
            }
            else if (hiSlope < 0 && loSlope > 0)
            {
                kind = PatternKinds.SymmetricalTriangle;
            }
            else if (hiSlope > 0 && loSlope > 0 && loSlope > hiSlope)
            {
                kind = PatternKinds.RisingWedge;
            }
            else if (hiSlope < 0 && loSlope < 0 && hiSlope < loSlope)
            {
                kind = PatternKinds.FallingWedge;
            }

            if (kind is null)
            {
                continue;
            }

            found.Add(new PatternOccurrence(
                kind,
                Version,
                start,
                i,
                i,
                i,
                brokeUp ? hiMax : loMin,
                brokeUp ? hiMax : loMin,
                brokeUp ? "BULLISH" : "BEARISH",
                PatternKinds.Confirmed,
                [
                    new PatternPoint(hs[0].PivotIndex, hs[0].Price, "high1"),
                    new PatternPoint(hs[^1].PivotIndex, hs[^1].Price, "highN"),
                    new PatternPoint(ls[0].PivotIndex, ls[0].Price, "low1"),
                    new PatternPoint(ls[^1].PivotIndex, ls[^1].Price, "lowN"),
                    new PatternPoint(i, candles[i].Close, "breakout")
                ],
                $"hiSlope={hiSlope:0.###} loSlope={loSlope:0.###}"));
        }
    }

    private static void DetectBreakoutRetest(IReadOnlyList<MarketCandle> candles, List<PatternOccurrence> found)
    {
        const int look = 20;
        for (var i = look + 2; i < candles.Count; i++)
        {
            var prevHi = PriceActionMath.RollingHigh(candles, i - 2, look);
            var prevLo = PriceActionMath.RollingLow(candles, i - 2, look);
            var brokeUp = candles[i - 1].Close > prevHi && candles[i - 2].Close <= prevHi;
            var brokeDn = candles[i - 1].Close < prevLo && candles[i - 2].Close >= prevLo;
            if (brokeUp)
            {
                var level = prevHi;
                var retest = candles[i].Low <= level * 1.001m && candles[i].Close > level;
                var failed = candles[i].Close < level;
                if (retest)
                {
                    found.Add(Occ(PatternKinds.BreakoutRetest, i - 1, i, level, "BULLISH", candles));
                }
                else if (failed)
                {
                    found.Add(Occ(PatternKinds.FailedBreakout, i - 1, i, level, "BEARISH", candles));
                }
            }
            else if (brokeDn)
            {
                var level = prevLo;
                var retest = candles[i].High >= level * 0.999m && candles[i].Close < level;
                var failed = candles[i].Close > level;
                if (retest)
                {
                    found.Add(Occ(PatternKinds.BreakoutRetest, i - 1, i, level, "BEARISH", candles));
                }
                else if (failed)
                {
                    found.Add(Occ(PatternKinds.FailedBreakout, i - 1, i, level, "BULLISH", candles));
                }
            }
        }
    }

    private static PatternOccurrence Occ(
        string kind,
        int start,
        int i,
        decimal level,
        string dir,
        IReadOnlyList<MarketCandle> candles) =>
        new(
            kind,
            Version,
            start,
            i,
            i,
            i,
            level,
            level,
            dir,
            PatternKinds.Confirmed,
            [new PatternPoint(start, level, "level"), new PatternPoint(i, candles[i].Close, "trigger")]);

    private static decimal Slope(SwingPoint[] pts)
    {
        var dx = pts[^1].PivotIndex - pts[0].PivotIndex;
        if (dx == 0)
        {
            return 0;
        }

        var mid = (pts[0].Price + pts[^1].Price) / 2m;
        if (mid == 0)
        {
            return 0;
        }

        return (pts[^1].Price - pts[0].Price) / mid / dx;
    }

    private static bool Flat(SwingPoint[] pts)
    {
        var mid = pts.Average(p => p.Price);
        return mid > 0 && pts.All(p => Math.Abs(p.Price - mid) / mid <= Similarity * 1.5m);
    }
}
