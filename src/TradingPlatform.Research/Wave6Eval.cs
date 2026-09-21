using System.Globalization;
using TradingPlatform.Domain.Market;

namespace TradingPlatform.Research;

public sealed record Wave6Row(
    string Key,
    int N,
    double Mfe,
    double Mae,
    double Range,
    double Ret1,
    double Ret4,
    double P1R,
    double PAdv1R,
    double PNone,
    double Whip,
    double Cont1,
    double Delay);

public sealed record Wave6GridCell(
    string Label,
    double TpR,
    double SlR,
    string Phase,
    string Tf,
    string Kind,
    int N,
    double Pf,
    double Exp,
    double Wr);

public sealed record Wave6Result(
    int SignalCount,
    int RandomCount,
    IReadOnlyList<Wave6Row> Rows,
    IReadOnlyList<Wave6GridCell> Grid,
    IReadOnlyList<string> Notes,
    string Classification,
    IReadOnlyList<string> Answers,
    int[] OosBeforeSig,
    int OosN24);

public static class Wave6Eval
{
    public const double DirPp = 0.02;
    public const double ContR = 0.02;
    public const double VolRel = 0.10;
    public const double ExitPfDelta = 0.05;

    public static Wave6Result Evaluate(
        IReadOnlyList<Wave5Event> events,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series)
    {
        var notes = new List<string>
        {
            "Wave-6 trade-path. Existing 43-strategy signals only. No new entries. No router. LIVE=OFF.",
            "1R = LOW stop distance 2% of slipped next-open entry. 2%/4% is NOT the path label and is not the exit grid.",
            "Same-bar favorable+adverse: adverse first. Path values are labels only.",
            $"Pre-registered thresholds: dir {DirPp:0.00} pp vs random-dir; 4-bar cont {ContR:0.00}R vs random-entry; vol +{VolRel:0%} range; exit PF +{ExitPfDelta:0.00} vs random, IS-frozen.",
            $"IS < {Wave5Router.TrainEnd:yyyy-MM-dd}; VAL < {Wave5Router.ValEnd:yyyy-MM-dd}; then OOS. No OOS retune."
        };

        var acc = new Dictionary<string, Wave6Acc>(StringComparer.Ordinal);
        Wave6Acc Get(string key)
        {
            if (!acc.TryGetValue(key, out var a))
            {
                a = new Wave6Acc();
                acc[key] = a;
            }

            return a;
        }

        var scratch = new Wave6Scratch();
        var indexes = new Dictionary<(string, string), Dictionary<DateTimeOffset, int>>();
        foreach (var kv in series)
        {
            var map = new Dictionary<DateTimeOffset, int>(kv.Value.Count);
            for (var i = 0; i < kv.Value.Count; i++)
            {
                map[kv.Value[i].OpenTime] = i;
            }

            indexes[kv.Key] = map;
        }

        var fillsUsed = new Dictionary<(string, string), HashSet<int>>();
        var nSig = 0;
        foreach (var e in events)
        {
            if (!series.TryGetValue((e.Symbol, e.Timeframe), out var candles)
                || !indexes.TryGetValue((e.Symbol, e.Timeframe), out var map)
                || !map.TryGetValue(e.FillTime, out var fill))
            {
                continue;
            }

            if (!Wave6Path.Trace(candles, fill, e.Direction > 0, e.AtrPct, e.Timeframe, scratch) || !scratch.Complete)
            {
                continue;
            }

            if (!fillsUsed.TryGetValue((e.Symbol, e.Timeframe), out var used))
            {
                used = [];
                fillsUsed[(e.Symbol, e.Timeframe)] = used;
            }

            used.Add(fill);
            nSig++;
            var phase = Wave5Router.Phase(e.FillTime);
            Add(Get($"{phase}|{e.Timeframe}|sig"), scratch, flip: false, isLong: e.Direction > 0);
            Add(Get($"{phase}|ALL|sig"), scratch, flip: false, isLong: e.Direction > 0);
            Add(Get($"{phase}|{e.Timeframe}|sym:{e.Symbol}|sig"), scratch, flip: false, isLong: e.Direction > 0);
            Add(Get($"{phase}|{e.Timeframe}|st:{e.Strategy}|sig"), scratch, flip: false, isLong: e.Direction > 0);
            Add(Get($"{phase}|{e.Timeframe}|flip"), scratch, flip: true, isLong: e.Direction > 0);
            Add(Get($"{phase}|ALL|flip"), scratch, flip: true, isLong: e.Direction > 0);
        }

        notes.Add($"Signal paths with complete 24h: {nSig}.");
        var rng = new Random(42);
        var nRnd = 0;
        foreach (var tf in new[] { "5m", "15m", "1h" })
        {
            foreach (var symbol in series.Keys.Select(k => k.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            {
                if (!series.TryGetValue((symbol, tf), out var candles))
                {
                    continue;
                }

                fillsUsed.TryGetValue((symbol, tf), out var used);
                used ??= [];
                var h48 = Wave6Path.HorizonBarCount(tf, 48);
                foreach (var phase in new[] { "IS", "VALIDATION", "OOS" })
                {
                    var need = Get($"{phase}|{tf}|sym:{symbol}|sig").N;
                    if (need == 0)
                    {
                        continue;
                    }

                    var pool = new List<int>();
                    for (var i = Wave5Harvest.Warmup + 1; i < candles.Count - h48; i++)
                    {
                        if (used.Contains(i))
                        {
                            continue;
                        }

                        if (Wave5Router.Phase(candles[i].OpenTime) != phase)
                        {
                            continue;
                        }

                        pool.Add(i);
                    }

                    Shuffle(pool, rng);
                    var take = Math.Min(need, pool.Count);
                    var longNeed = Get($"{phase}|{tf}|sym:{symbol}|sig").NLong;
                    var longTake = Math.Min(longNeed, take);
                    for (var k = 0; k < take; k++)
                    {
                        var isLong = k < longTake;
                        if (!Wave6Path.Trace(candles, pool[k], isLong, double.NaN, tf, scratch) || !scratch.Complete)
                        {
                            continue;
                        }

                        nRnd++;
                        Add(Get($"{phase}|{tf}|rnd"), scratch, flip: false, isLong: isLong);
                        Add(Get($"{phase}|ALL|rnd"), scratch, flip: false, isLong: isLong);
                        Add(Get($"{phase}|{tf}|sym:{symbol}|rnd"), scratch, flip: false, isLong: isLong);
                    }
                }
            }
        }

        notes.Add($"Random-entry paths with complete 24h: {nRnd}.");
        var rows = acc
            .Where(kv => kv.Value.N24 >= 20)
            .Select(kv => kv.Value.Row(kv.Key))
            .OrderBy(r => r.Key)
            .ToList();
        var grid = new List<Wave6GridCell>();
        foreach (var kv in acc.Where(x => x.Key.Contains("|sig", StringComparison.Ordinal) || x.Key.Contains("|rnd", StringComparison.Ordinal)))
        {
            var parts = kv.Key.Split('|');
            if (parts.Length != 3 || parts[2] is not ("sig" or "rnd"))
            {
                continue;
            }

            if (parts[1] is not ("5m" or "15m" or "1h" or "ALL"))
            {
                continue;
            }

            kv.Value.AppendGrid(grid, parts[0], parts[1], parts[2]);
        }

        var cls = Classify(acc, notes, out var answers);
        acc.TryGetValue("OOS|ALL|sig", out var oosAll);
        return new Wave6Result(
            nSig,
            nRnd,
            rows,
            grid,
            notes,
            cls,
            answers,
            oosAll?.Before ?? new int[49],
            oosAll?.N24 ?? 0);
    }

    private static void Add(Wave6Acc a, Wave6Scratch s, bool flip, bool isLong)
    {
        a.N++;
        if (!s.Complete)
        {
            return;
        }

        a.N24++;
        var mfe = flip ? s.MaeR[Wave6Path.H24] : s.MfeR[Wave6Path.H24];
        var mae = flip ? s.MfeR[Wave6Path.H24] : s.MaeR[Wave6Path.H24];
        a.SumMfe += mfe;
        a.SumMae += mae;
        a.SumRange += s.RangeR[Wave6Path.H24];
        a.SumClose += flip ? -s.CloseR[Wave6Path.H24] : s.CloseR[Wave6Path.H24];
        a.SumRet1 += flip ? -s.BarCloseR[0] : s.BarCloseR[0];
        a.SumRet2 += flip ? -s.BarCloseR[1] : s.BarCloseR[1];
        a.SumRet4 += flip ? -s.BarCloseR[3] : s.BarCloseR[3];
        a.SumMfeBars += flip ? s.MaeBars[Wave6Path.H24] : s.MfeBars[Wave6Path.H24];
        a.SumMaeBars += flip ? s.MfeBars[Wave6Path.H24] : s.MaeBars[Wave6Path.H24];
        if (!flip && isLong)
        {
            a.NLong++;
        }

        var hold = s.HBars[Wave6Path.H24];
        var favB = flip ? s.AdvBar : s.FavBar;
        var advB = flip ? s.FavBar : s.AdvBar;
        var t = Wave6Path.FirstTouch(favB[Wave6Path.L1], advB[Wave6Path.L1], hold);
        if (t > 0)
        {
            a.Fav1++;
        }
        else if (t < 0)
        {
            a.Adv1++;
        }
        else
        {
            a.None1++;
        }

        var ret1 = flip ? -s.BarCloseR[0] : s.BarCloseR[0];
        if (ret1 > 0)
        {
            a.Cont1++;
        }
        else if (ret1 < 0)
        {
            a.Rev1++;
        }

        var whip = Wave6Path.FirstTouch(s.FavBar[Wave6Path.L05], s.AdvBar[Wave6Path.L05], hold) != 0
            && s.FavBar[Wave6Path.L05] != int.MaxValue
            && s.AdvBar[Wave6Path.L05] != int.MaxValue
            && s.FavBar[Wave6Path.L05] <= hold
            && s.AdvBar[Wave6Path.L05] <= hold;
        if (whip)
        {
            a.Whip++;
        }

        if (ret1 <= 0 && mfe >= 0.5)
        {
            a.Delay++;
        }

        if (flip)
        {
            return;
        }

        for (var f = 0; f < 7; f++)
        {
            for (var d = 0; d < 7; d++)
            {
                if (Wave6Path.FirstTouch(s.FavBar[f], s.AdvBar[d], hold) > 0)
                {
                    a.Before[f * 7 + d]++;
                }
            }
        }

        var close24 = s.CloseR[Wave6Path.H24];
        for (var ti = 0; ti < 6; ti++)
        {
            for (var si = 0; si < 6; si++)
            {
                var g = Wave6Path.GridGross(s, Wave6Path.GridR[ti], Wave6Path.GridR[si], hold, close24);
                var idx = ti * 6 + si;
                a.GridN[idx]++;
                a.GridSum[idx] += g;
                if (g > 0)
                {
                    a.GridWinSum[idx] += g;
                    a.GridWins[idx]++;
                }
                else if (g < 0)
                {
                    a.GridLossSum[idx] += g;
                }
            }
        }

        var h4 = s.HBars[2];
        var h12 = s.HBars[4];
        PushExtra(a, 0, s.BarCloseR[0] * Wave6Path.RUnit);
        PushExtra(a, 1, s.BarCloseR[1] * Wave6Path.RUnit);
        PushExtra(a, 2, s.BarCloseR[3] * Wave6Path.RUnit);
        PushExtra(a, 3, s.BarCloseR[7] * Wave6Path.RUnit);
        PushExtra(a, 4, s.BarCloseR[15] * Wave6Path.RUnit);
        PushExtra(a, 5, s.Be05);
        PushExtra(a, 6, s.Be1);
        PushExtra(a, 7, s.Trail);
        PushExtra(a, 8, s.Atr);
        PushExtra(a, 9, Wave6Path.GridGross(s, 1.0, 1.0, h4, s.CloseR[2]));
        PushExtra(a, 10, Wave6Path.GridGross(s, 1.0, 1.0, h12, s.CloseR[4]));
    }

    private static void PushExtra(Wave6Acc a, int i, double g)
    {
        if (double.IsNaN(g))
        {
            return;
        }

        a.ExtraN[i]++;
        a.ExtraSum[i] += g;
        if (g > 0)
        {
            a.ExtraWinSum[i] += g;
            a.ExtraWins[i]++;
        }
        else if (g < 0)
        {
            a.ExtraLossSum[i] += g;
        }
    }

    private static string Classify(Dictionary<string, Wave6Acc> acc, List<string> notes, out List<string> answers)
    {
        bool Pair(string phase, string tf, string kind, out Wave6Acc a)
        {
            return acc.TryGetValue($"{phase}|{tf}|{kind}", out a!) && a.N24 >= 50;
        }

        var dirTf = 0;
        var dirVal = 0;
        var timeTf = 0;
        var timeVal = 0;
        var volTf = 0;
        var volVal = 0;
        foreach (var tf in new[] { "5m", "15m", "1h" })
        {
            if (Pair("OOS", tf, "sig", out var sig) && Pair("OOS", tf, "flip", out var flip))
            {
                var d = sig.P1R() - 0.5 * (sig.P1R() + flip.P1R());
                if (d >= DirPp)
                {
                    dirTf++;
                }
            }

            if (Pair("VALIDATION", tf, "sig", out var vs) && Pair("VALIDATION", tf, "flip", out var vf) && vs.P1R() - 0.5 * (vs.P1R() + vf.P1R()) > 0)
            {
                dirVal++;
            }

            if (Pair("OOS", tf, "sig", out var s2) && Pair("OOS", tf, "rnd", out var r2) && s2.MeanRet4() - r2.MeanRet4() >= ContR)
            {
                timeTf++;
            }

            if (Pair("VALIDATION", tf, "sig", out var s3) && Pair("VALIDATION", tf, "rnd", out var r3) && s3.MeanRet4() - r3.MeanRet4() > 0)
            {
                timeVal++;
            }

            if (Pair("OOS", tf, "sig", out var s4) && Pair("OOS", tf, "rnd", out var r4) && r4.MeanRange() > 0 && s4.MeanRange() / r4.MeanRange() - 1.0 >= VolRel)
            {
                volTf++;
            }

            if (Pair("VALIDATION", tf, "sig", out var s5) && Pair("VALIDATION", tf, "rnd", out var r5) && r5.MeanRange() > 0 && s5.MeanRange() / r5.MeanRange() > 1.0)
            {
                volVal++;
            }
        }

        var dir = dirTf >= 2 && dirVal >= 2;
        var timing = timeTf >= 2 && timeVal >= 2;
        var vol = volTf >= 2 && volVal >= 2;

        var exitOk = false;
        var timeExit = false;
        var costOk = false;
        var coinsOk = 0;
        if (Pair("IS", "ALL", "sig", out var isSig) && Pair("IS", "ALL", "rnd", out var isRnd)
            && Pair("VALIDATION", "ALL", "sig", out var valSig) && Pair("VALIDATION", "ALL", "rnd", out var valRnd)
            && Pair("OOS", "ALL", "sig", out var oosSig) && Pair("OOS", "ALL", "rnd", out var oosRnd))
        {
            var best = 0;
            var bestPf = double.NegativeInfinity;
            for (var i = 0; i < 36; i++)
            {
                var pf = isSig.GridPf(i);
                if (pf > bestPf)
                {
                    bestPf = pf;
                    best = i;
                }
            }

            var valDelta = valSig.GridPf(best) - valRnd.GridPf(best);
            var oosDelta = oosSig.GridPf(best) - oosRnd.GridPf(best);
            notes.Add($"IS-best 24h grid cell {CellName(best)} PF {Fmt(bestPf)} vs random {Fmt(isRnd.GridPf(best))}. VAL delta vs random {Fmt(valDelta)}. OOS delta {Fmt(oosDelta)}.");
            exitOk = valDelta > 0 && oosDelta >= ExitPfDelta;
            timeExit = valSig.ExtraPf(2) > valRnd.ExtraPf(2) && oosSig.ExtraPf(2) - oosRnd.ExtraPf(2) >= ExitPfDelta;
            var c = 2.0 * 1.25 * (double)(Wave6Path.Fee + Wave6Path.Slip);
            costOk = exitOk
                && oosSig.GridSum[best] / Math.Max(1, oosSig.GridN[best]) - c
                > oosRnd.GridSum[best] / Math.Max(1, oosRnd.GridN[best]) - c;
        }

        notes.Add($"OOS TFs beating random-dir by ≥{DirPp:0.00}: {dirTf}/3 (VAL same sign {dirVal}/3).");
        notes.Add($"OOS TFs 4-bar cont ≥{ContR:0.00}R vs random-entry: {timeTf}/3 (VAL same sign {timeVal}/3).");
        notes.Add($"OOS TFs range ≥+{VolRel:0%} vs random-entry: {volTf}/3 (VAL same sign {volVal}/3).");
        foreach (var kv in acc)
        {
            if (!kv.Key.StartsWith("OOS|15m|sym:", StringComparison.Ordinal) || !kv.Key.EndsWith("|sig", StringComparison.Ordinal))
            {
                continue;
            }

            var rndKey = kv.Key[..^3] + "rnd";
            if (acc.TryGetValue(rndKey, out var rnd) && kv.Value.N24 >= 50 && rnd.N24 >= 50 && kv.Value.P1R() - rnd.P1R() >= DirPp)
            {
                coinsOk++;
            }
        }

        notes.Add($"OOS 15m coins with P(+1R) ≥ random+{DirPp:0.00}: {coinsOk}.");

        answers =
        [
            Q("1. Direction", dir, "P(+1R before -1R) vs random direction, OOS, ≥2 TFs, VAL same sign."),
            Q("2. Short-term continuation", timing, "4-bar close R vs random entry."),
            Q("3. Volatility", vol, "24h range vs random entry."),
            Q("4. MFE", Compare("OOS", "ALL", "sig", "rnd", acc, a => a.MeanMfe()) > 0, $"OOS mean MFE signal−random = {Fmt(Compare("OOS", "ALL", "sig", "rnd", acc, a => a.MeanMfe()))}R."),
            Q("5. MAE", Compare("OOS", "ALL", "sig", "rnd", acc, a => a.MeanMae()) < 0, $"OOS mean MAE signal−random = {Fmt(Compare("OOS", "ALL", "sig", "rnd", acc, a => a.MeanMae()))}R (lower MAE would be better)."),
            Q("6. Holding period", timing, "4-bar continuation is the pre-registered short-horizon test. Time-to-MFE/MAE are descriptive."),
            Q("7. TP/SL grid OOS", exitOk, "IS-best cell frozen; VAL then OOS vs random."),
            Q("8. Time exit OOS", timeExit, "TIME-4BAR OOS PF vs random, VAL same sign."),
            Q("9. Signal vs random entry", dir || timing || vol || exitOk, "Random times, same coins/TFs/long-short counts."),
            Q("10. Signal vs random direction", dir, "Flip/50-50 equivalent: 0.5×(P_sig+P_flip)."),
            Q("11. Robust across 5m/15m/1h", dirTf >= 2 || timeTf >= 2 || volTf >= 2, $"dir {dirTf} timing {timeTf} vol {volTf} TFs OOS."),
            Q("12. Robust across coins", coinsOk >= 6, $"OOS 15m coins beating random P(+1R) by ≥{DirPp:0.00}: {coinsOk}/10."),
            Q("13. Cost +25/+50/+100", costOk, "IS-best grid net expectancy still beats random at +25% costs.")
        ];

        if (dir)
        {
            return "DIRECTIONAL INFORMATION FOUND";
        }

        if (timing)
        {
            return "TIMING INFORMATION FOUND";
        }

        if (vol)
        {
            return "VOLATILITY INFORMATION FOUND";
        }

        if (exitOk)
        {
            return "EXIT/PATH INFORMATION FOUND";
        }

        notes.Add("NO MEASURABLE INFORMATION FOUND. Do not create another strategy family from this signal pool.");
        return "NO MEASURABLE INFORMATION FOUND";
    }

    private static string Q(string title, bool yes, string detail) =>
        $"{title}: {(yes ? "YES" : "NO")}. {detail}";

    private static double Compare(
        string phase,
        string tf,
        string a,
        string b,
        Dictionary<string, Wave6Acc> acc,
        Func<Wave6Acc, double> sel)
    {
        if (!acc.TryGetValue($"{phase}|{tf}|{a}", out var x) || !acc.TryGetValue($"{phase}|{tf}|{b}", out var y) || x.N24 < 20 || y.N24 < 20)
        {
            return double.NaN;
        }

        return sel(x) - sel(y);
    }

    public static string CellName(int idx)
    {
        var t = Wave6Path.GridR[idx / 6];
        var s = Wave6Path.GridR[idx % 6];
        return $"TP{t.ToString("0.00", CultureInfo.InvariantCulture)}/SL{s.ToString("0.00", CultureInfo.InvariantCulture)}";
    }

    public static string Fmt(double v) =>
        double.IsNaN(v) || double.IsInfinity(v) ? "n/a" : v.ToString("0.0000", CultureInfo.InvariantCulture);

    public static readonly string[] ExtraNames =
    [
        "TIME-1BAR", "TIME-2BAR", "TIME-4BAR", "TIME-8BAR", "TIME-16BAR",
        "BE-0.5R", "BE-1R", "TRAIL-0.5R-AFTER-1R", "ATR-TRAIL", "TP1/SL1-4H", "TP1/SL1-12H"
    ];

    private static void Shuffle(List<int> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

internal sealed class Wave6Acc
{
    public int N, N24, NLong, Fav1, Adv1, None1, Cont1, Rev1, Whip, Delay;
    public double SumMfe, SumMae, SumRange, SumClose, SumRet1, SumRet2, SumRet4, SumMfeBars, SumMaeBars;
    public readonly int[] Before = new int[49];
    public readonly int[] GridN = new int[36];
    public readonly int[] GridWins = new int[36];
    public readonly double[] GridSum = new double[36];
    public readonly double[] GridWinSum = new double[36];
    public readonly double[] GridLossSum = new double[36];
    public readonly int[] ExtraN = new int[11];
    public readonly int[] ExtraWins = new int[11];
    public readonly double[] ExtraSum = new double[11];
    public readonly double[] ExtraWinSum = new double[11];
    public readonly double[] ExtraLossSum = new double[11];

    public double P1R() => N24 == 0 ? double.NaN : Fav1 / (double)N24;
    public double PAdv() => N24 == 0 ? double.NaN : Adv1 / (double)N24;
    public double MeanMfe() => N24 == 0 ? double.NaN : SumMfe / N24;
    public double MeanMae() => N24 == 0 ? double.NaN : SumMae / N24;
    public double MeanRange() => N24 == 0 ? double.NaN : SumRange / N24;
    public double MeanRet4() => N24 == 0 ? double.NaN : SumRet4 / N24;
    public double MeanRet1() => N24 == 0 ? double.NaN : SumRet1 / N24;

    public double GridPf(int i)
    {
        var loss = Math.Abs(GridLossSum[i]);
        if (loss < 1e-18)
        {
            return GridWinSum[i] > 0 ? double.PositiveInfinity : double.NaN;
        }

        return GridWinSum[i] / loss;
    }

    public double ExtraPf(int i)
    {
        var loss = Math.Abs(ExtraLossSum[i]);
        if (loss < 1e-18)
        {
            return ExtraWinSum[i] > 0 ? double.PositiveInfinity : double.NaN;
        }

        return ExtraWinSum[i] / loss;
    }

    public Wave6Row Row(string key) => new(
        key,
        N24,
        MeanMfe(),
        MeanMae(),
        MeanRange(),
        MeanRet1(),
        MeanRet4(),
        P1R(),
        PAdv(),
        N24 == 0 ? double.NaN : None1 / (double)N24,
        N24 == 0 ? double.NaN : Whip / (double)N24,
        N24 == 0 ? double.NaN : Cont1 / (double)N24,
        N24 == 0 ? double.NaN : Delay / (double)N24);

    public void AppendGrid(List<Wave6GridCell> grid, string phase, string tf, string kind)
    {
        for (var i = 0; i < 36; i++)
        {
            if (GridN[i] < 20)
            {
                continue;
            }

            grid.Add(new Wave6GridCell(
                Wave6Eval.CellName(i),
                Wave6Path.GridR[i / 6],
                Wave6Path.GridR[i % 6],
                phase,
                tf,
                kind,
                GridN[i],
                GridPf(i),
                GridSum[i] / GridN[i],
                GridWins[i] / (double)GridN[i]));
        }

        for (var i = 0; i < 11; i++)
        {
            if (ExtraN[i] < 20)
            {
                continue;
            }

            grid.Add(new Wave6GridCell(
                Wave6Eval.ExtraNames[i],
                double.NaN,
                double.NaN,
                phase,
                tf,
                kind,
                ExtraN[i],
                ExtraPf(i),
                ExtraSum[i] / ExtraN[i],
                ExtraWins[i] / (double)ExtraN[i]));
        }
    }
}
