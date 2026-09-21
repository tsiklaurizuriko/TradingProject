using TradingPlatform.Domain.Market;

namespace TradingPlatform.Research;

/// <summary>
/// Causal trade-path labels. 1R = existing LOW stop distance (2% of slipped entry).
/// Future path is never used as a feature.
/// Same-bar favorable and adverse: adverse is first (Model B).
/// </summary>
public static class Wave6Path
{
    public const double RUnit = 0.02;
    public const decimal Slip = 0.0002m;
    public const decimal Fee = 0.0004m;
    public static readonly double[] Levels = [0.25, 0.50, 0.75, 1.00, 1.25, 1.50, 2.00];
    public static readonly int[] HorizonHours = [1, 2, 4, 8, 12, 24, 48];
    public static readonly int[] TimeBars = [1, 2, 4, 8, 16];
    public static readonly double[] GridR = [0.50, 0.75, 1.00, 1.25, 1.50, 2.00];
    public const int H24 = 5;
    public const int L1 = 3;
    public const int L05 = 1;
    public const int L2 = 6;

    public static int BarsPerHour(string timeframe) => timeframe switch
    {
        "5m" => 12,
        "15m" => 4,
        _ => 1
    };

    public static int HorizonBarCount(string timeframe, int hours) =>
        hours * BarsPerHour(timeframe);

    public static int LevelIndex(double r)
    {
        for (var i = 0; i < Levels.Length; i++)
        {
            if (Math.Abs(Levels[i] - r) < 1e-9)
            {
                return i;
            }
        }

        return -1;
    }

    public static double Net(double gross, decimal costMult) =>
        gross - 2.0 * (double)(costMult * (Fee + Slip));

    public static void Reset(Wave6Scratch s)
    {
        Array.Fill(s.FavBar, int.MaxValue);
        Array.Fill(s.AdvBar, int.MaxValue);
        Array.Clear(s.MfeR);
        Array.Clear(s.MaeR);
        Array.Clear(s.RangeR);
        Array.Clear(s.CloseR);
        Array.Clear(s.MfeBars);
        Array.Clear(s.MaeBars);
        Array.Clear(s.Ok);
        Array.Clear(s.BarCloseR);
        s.Be05 = s.Be1 = s.Trail = s.Atr = double.NaN;
        s.Complete = false;
    }

    public static bool Trace(
        IReadOnlyList<MarketCandle> candles,
        int fill,
        bool isLong,
        double atrPct,
        string timeframe,
        Wave6Scratch s)
    {
        Reset(s);
        if (fill < 0 || fill >= candles.Count)
        {
            return false;
        }

        var open = candles[fill].Open;
        if (open <= 0m)
        {
            return false;
        }

        var side = isLong ? 1m : -1m;
        var entry = open * (1m + side * Slip);
        var rPx = (double)entry * RUnit;
        if (rPx <= 0)
        {
            return false;
        }

        var atrAbs = !double.IsNaN(atrPct) && atrPct > 0
            ? (double)entry * (atrPct / 100.0)
            : rPx * 0.5;
        var maxBars = HorizonBarCount(timeframe, 48);
        var last = Math.Min(candles.Count - 1, fill + maxBars - 1);
        if (last < fill)
        {
            return false;
        }

        double maxFav = 0, maxAdv = 0, maxRange = 0;
        var tMfe = 0;
        var tMae = 0;
        var hBars = s.HBars;
        for (var h = 0; h < 7; h++)
        {
            hBars[h] = HorizonBarCount(timeframe, HorizonHours[h]);
        }

        var be05Arm = false;
        var be1Arm = false;
        var trailArm = false;
        var be05Done = false;
        var be1Done = false;
        var trailDone = false;
        var atrDone = false;
        var be05Gross = double.NaN;
        var be1Gross = double.NaN;
        var trailGross = double.NaN;
        var atrGross = double.NaN;
        var trailStop = (double)entry - (isLong ? rPx : -rPx);
        var atrHwm = (double)entry;
        var atrStop = isLong ? (double)entry - atrAbs : (double)entry + atrAbs;
        var trailHwm = (double)entry;
        var hold24 = hBars[H24];

        for (var j = fill; j <= last; j++)
        {
            var held = j - fill + 1;
            var bar = candles[j];
            var favExc = isLong
                ? (double)((bar.High - entry) / entry)
                : (double)((entry - bar.Low) / entry);
            var advExc = isLong
                ? (double)((entry - bar.Low) / entry)
                : (double)((bar.High - entry) / entry);
            var rangeExc = (double)((bar.High - bar.Low) / entry);
            var closeExc = (double)(side * (bar.Close - entry) / entry);
            if (favExc < 0)
            {
                favExc = 0;
            }

            if (advExc < 0)
            {
                advExc = 0;
            }

            if (favExc > maxFav)
            {
                maxFav = favExc;
                tMfe = held;
            }

            if (advExc > maxAdv)
            {
                maxAdv = advExc;
                tMae = held;
            }

            if (rangeExc > maxRange)
            {
                maxRange = rangeExc;
            }

            var favR = maxFav / RUnit;
            var advR = maxAdv / RUnit;
            for (var li = 0; li < 7; li++)
            {
                var lvl = Levels[li];
                if (s.FavBar[li] == int.MaxValue && favR >= lvl)
                {
                    s.FavBar[li] = held;
                }

                if (s.AdvBar[li] == int.MaxValue && advR >= lvl)
                {
                    s.AdvBar[li] = held;
                }
            }

            if (held <= 16)
            {
                s.BarCloseR[held - 1] = closeExc / RUnit;
            }

            if (!be05Done && held <= hold24)
            {
                if (!be05Arm && favR >= 0.5)
                {
                    be05Arm = true;
                }

                var sl05 = be05Arm ? 0.0 : 1.0;
                var hitSl = be05Arm ? advExc >= 0 && (isLong ? bar.Low <= entry : bar.High >= entry) : advR >= sl05;
                var hitTp = favR >= 2.0;
                if (hitSl)
                {
                    be05Gross = be05Arm ? 0.0 : -1.0 * RUnit;
                    be05Done = true;
                }
                else if (hitTp)
                {
                    be05Gross = 2.0 * RUnit;
                    be05Done = true;
                }
                else if (held == hold24 || j == last)
                {
                    be05Gross = closeExc;
                    be05Done = true;
                }
            }

            if (!be1Done && held <= hold24)
            {
                if (!be1Arm && favR >= 1.0)
                {
                    be1Arm = true;
                }

                var hitSl = be1Arm ? (isLong ? bar.Low <= entry : bar.High >= entry) : advR >= 1.0;
                var hitTp = favR >= 2.0;
                if (hitSl)
                {
                    be1Gross = be1Arm ? 0.0 : -1.0 * RUnit;
                    be1Done = true;
                }
                else if (hitTp)
                {
                    be1Gross = 2.0 * RUnit;
                    be1Done = true;
                }
                else if (held == hold24 || j == last)
                {
                    be1Gross = closeExc;
                    be1Done = true;
                }
            }

            if (!trailDone && held <= hold24)
            {
                if (!trailArm && favR >= 1.0)
                {
                    trailArm = true;
                    trailHwm = isLong ? (double)bar.High : (double)bar.Low;
                    trailStop = isLong ? trailHwm - 0.5 * rPx : trailHwm + 0.5 * rPx;
                }
                else if (trailArm)
                {
                    if (isLong && (double)bar.High > trailHwm)
                    {
                        trailHwm = (double)bar.High;
                        trailStop = trailHwm - 0.5 * rPx;
                    }
                    else if (!isLong && (double)bar.Low < trailHwm)
                    {
                        trailHwm = (double)bar.Low;
                        trailStop = trailHwm + 0.5 * rPx;
                    }
                }

                var hitTrailSl = trailArm
                    ? (isLong ? (double)bar.Low <= trailStop : (double)bar.High >= trailStop)
                    : advR >= 1.0;
                if (hitTrailSl)
                {
                    var px = trailArm ? trailStop : (double)entry * (1.0 - (isLong ? RUnit : -RUnit));
                    trailGross = (double)(side * ((decimal)px - entry) / entry);
                    trailDone = true;
                }
                else if (held == hold24 || j == last)
                {
                    trailGross = closeExc;
                    trailDone = true;
                }
            }

            if (!atrDone && held <= hold24)
            {
                if (isLong && (double)bar.High > atrHwm)
                {
                    atrHwm = (double)bar.High;
                    atrStop = atrHwm - atrAbs;
                }
                else if (!isLong && (double)bar.Low < atrHwm)
                {
                    atrHwm = (double)bar.Low;
                    atrStop = atrHwm + atrAbs;
                }

                var hitAtr = isLong ? (double)bar.Low <= atrStop : (double)bar.High >= atrStop;
                if (hitAtr)
                {
                    atrGross = (double)(side * ((decimal)atrStop - entry) / entry);
                    atrDone = true;
                }
                else if (held == hold24 || j == last)
                {
                    atrGross = closeExc;
                    atrDone = true;
                }
            }

            for (var h = 0; h < 7; h++)
            {
                if (!s.Ok[h] && (held == hBars[h] || (j == last && held >= hBars[h])))
                {
                    s.MfeR[h] = maxFav / RUnit;
                    s.MaeR[h] = maxAdv / RUnit;
                    s.RangeR[h] = maxRange / RUnit;
                    s.CloseR[h] = closeExc / RUnit;
                    s.MfeBars[h] = tMfe;
                    s.MaeBars[h] = tMae;
                    s.Ok[h] = true;
                }
            }
        }

        s.Be05 = be05Gross;
        s.Be1 = be1Gross;
        s.Trail = trailGross;
        s.Atr = atrGross;
        s.Complete = s.Ok[H24];
        return s.Ok[0];
    }

    public static int FirstTouch(int favBar, int advBar, int holdBars)
    {
        var f = favBar <= holdBars ? favBar : int.MaxValue;
        var a = advBar <= holdBars ? advBar : int.MaxValue;
        if (a == int.MaxValue && f == int.MaxValue)
        {
            return 0;
        }

        if (a <= f)
        {
            return -1;
        }

        return 1;
    }

    public static double GridGross(Wave6Scratch s, double tpR, double slR, int holdBars, double timeCloseR)
    {
        var tp = LevelIndex(tpR);
        var sl = LevelIndex(slR);
        var touch = FirstTouch(s.FavBar[tp], s.AdvBar[sl], holdBars);
        if (touch < 0)
        {
            return -slR * RUnit;
        }

        if (touch > 0)
        {
            return tpR * RUnit;
        }

        return timeCloseR * RUnit;
    }
}

public sealed class Wave6Scratch
{
    public readonly int[] FavBar = new int[7];
    public readonly int[] AdvBar = new int[7];
    public readonly double[] MfeR = new double[7];
    public readonly double[] MaeR = new double[7];
    public readonly double[] RangeR = new double[7];
    public readonly double[] CloseR = new double[7];
    public readonly int[] MfeBars = new int[7];
    public readonly int[] MaeBars = new int[7];
    public readonly bool[] Ok = new bool[7];
    public readonly int[] HBars = new int[7];
    public readonly double[] BarCloseR = new double[16];
    public double Be05, Be1, Trail, Atr;
    public bool Complete;
}
