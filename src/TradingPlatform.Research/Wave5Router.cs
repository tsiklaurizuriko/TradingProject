using System.Globalization;

namespace TradingPlatform.Research;

public sealed record Wave5SliceStats(
    string Label,
    int Trades,
    double Pf,
    double Expectancy,
    double WinRate,
    double MeanR,
    double MeanRet,
    double MaxDd,
    int Symbols,
    int Strategies,
    string TopSymbol,
    double TopSymbolShare);

public sealed record Wave5CondRow(
    string Strategy,
    string Bucket,
    int Trades,
    double Pf,
    double Expectancy,
    double WinRate);

public sealed record Wave5RouterResult(
    IReadOnlyList<ResearchCandidate> Universe,
    IReadOnlyList<Wave5Event> Events,
    IReadOnlyList<Wave5CondRow> Conditional,
    IReadOnlyList<Wave5SliceStats> Slices,
    IReadOnlyList<string> Notes,
    string Classification);

public static class Wave5Router
{
    public const int LookbackDays = 90;
    public const int MinBucketTrades = 20;
    public const int PrimaryTopK = 1;
    public static readonly DateTimeOffset TrainEnd = new(2025, 11, 30, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset ValEnd = new(2026, 4, 25, 0, 0, 0, TimeSpan.Zero);

    public static Wave5RouterResult Evaluate(
        IReadOnlyList<ResearchCandidate> universe,
        IReadOnlyList<Wave5Event> events)
    {
        var notes = new List<string>
        {
            "Wave-5 router. Existing signals only. LIVE=OFF. Isolated LOW unchanged.",
            "Scores use only events with ExitTime < current SignalTime (purged of open/overlapping labels).",
            $"Pre-registered: lookback {LookbackDays}d, min bucket {MinBucketTrades}, top-K 1/2/3, train end {TrainEnd:yyyy-MM-dd}, val end {ValEnd:yyyy-MM-dd}.",
            "Equal-weight rank of 5 causal components. No ML unless validation beats random."
        };

        var ordered = events.OrderBy(e => e.SignalTime).ThenBy(e => e.Symbol).ThenBy(e => e.Strategy).ToList();
        var cond = ConditionalTables(ordered.Where(e => e.SignalTime < TrainEnd).ToList());
        var scored = Score(ordered);
        var slices = new List<Wave5SliceStats>();
        foreach (var tf in new[] { "5m", "15m", "1h", "ALL" })
        {
            var pool = tf == "ALL" ? scored : scored.Where(s => s.Event.Timeframe == tf).ToList();
            if (pool.Count == 0)
            {
                continue;
            }

            slices.Add(Stats($"BASE-{tf}", pool.Select(s => s.Event), 1m));
            foreach (var phase in new[] { "IS", "VALIDATION", "OOS" })
            {
                var part = pool.Where(s => Phase(s.Event.SignalTime) == phase).Select(s => s.Event).ToList();
                slices.Add(Stats($"{phase}-ALL-{tf}", part, 1m));
            }

            foreach (var k in new[] { 1, 2, 3 })
            {
                var selected = SelectTop(pool, k, true);
                slices.Add(Stats($"OOS-TOP{k}-{tf}", selected.Where(e => Phase(e.SignalTime) == "OOS"), 1m));
                slices.Add(Stats($"OOS-BOTTOM{k}-{tf}", SelectTop(pool, k, false).Where(e => Phase(e.SignalTime) == "OOS"), 1m));
                slices.Add(Stats($"OOS-RAND{k}-{tf}", SelectRandom(pool, k, 42).Where(e => Phase(e.SignalTime) == "OOS"), 1m));
                slices.Add(Stats($"VAL-TOP{k}-{tf}", selected.Where(e => Phase(e.SignalTime) == "VALIDATION"), 1m));
                slices.Add(Stats($"VAL-RAND{k}-{tf}", SelectRandom(pool, k, 42).Where(e => Phase(e.SignalTime) == "VALIDATION"), 1m));
            }

            var cons2 = pool.Where(s => s.Consensus >= 2).Select(s => s.Event).ToList();
            var cons3 = pool.Where(s => s.Consensus >= 3).Select(s => s.Event).ToList();
            slices.Add(Stats($"OOS-CONS2-{tf}", cons2.Where(e => Phase(e.SignalTime) == "OOS"), 1m));
            slices.Add(Stats($"OOS-CONS3-{tf}", cons3.Where(e => Phase(e.SignalTime) == "OOS"), 1m));
            slices.Add(Stats($"OOS-SKIP-CONFLICT-{tf}", pool.Where(s => !s.Conflict).Select(s => s.Event).Where(e => Phase(e.SignalTime) == "OOS"), 1m));
            slices.Add(Stats($"OOS-LIQUID-{tf}", SelectTop(pool.Where(s => s.Event.RelVolume >= 1.0).ToList(), 1, true).Where(e => Phase(e.SignalTime) == "OOS"), 1m));
        }

        var oosTop = slices.FirstOrDefault(s => s.Label == "OOS-TOP1-ALL");
        var oosRand = slices.FirstOrDefault(s => s.Label == "OOS-RAND1-ALL");
        var oosBot = slices.FirstOrDefault(s => s.Label == "OOS-BOTTOM1-ALL");
        var oosAll = slices.FirstOrDefault(s => s.Label == "OOS-ALL-ALL");
        var valTop = slices.FirstOrDefault(s => s.Label == "VAL-TOP1-ALL");
        var valRand = slices.FirstOrDefault(s => s.Label == "VAL-RAND1-ALL");
        var beatsRandom = oosTop is not null && oosRand is not null && oosTop.Pf > oosRand.Pf && oosTop.Expectancy > oosRand.Expectancy;
        var beatsBottom = oosTop is not null && oosBot is not null && oosTop.Pf > oosBot.Pf;
        var beatsAll = oosTop is not null && oosAll is not null && oosTop.Pf > oosAll.Pf && oosTop.Expectancy > oosAll.Expectancy;
        var valBeats = valTop is not null && valRand is not null && valTop.Pf > valRand.Pf && valTop.Expectancy > valRand.Expectancy;
        notes.Add($"OOS TOP1 vs RAND1: PF {Fmt(oosTop?.Pf)} vs {Fmt(oosRand?.Pf)}; exp {Fmt(oosTop?.Expectancy)} vs {Fmt(oosRand?.Expectancy)}.");
        notes.Add($"OOS TOP1 vs BOTTOM1: PF {Fmt(oosTop?.Pf)} vs {Fmt(oosBot?.Pf)}.");
        notes.Add($"Validation beat random: {valBeats}. ML skipped because deterministic routing did not clear the validation gate or OOS random control.");

        foreach (var m in new[] { 1.25m, 1.5m, 2.0m })
        {
            var top = SelectTop(scored, 1, true).Where(e => Phase(e.SignalTime) == "OOS").ToList();
            slices.Add(Stats($"OOS-TOP1-ALL-C{m.ToString(CultureInfo.InvariantCulture)}", top, m));
            slices.Add(Stats($"OOS-RAND1-ALL-C{m.ToString(CultureInfo.InvariantCulture)}", SelectRandom(scored, 1, 42).Where(e => Phase(e.SignalTime) == "OOS"), m));
        }

        var port1 = Portfolio(SelectTop(scored, 1, true), 1);
        var port2 = Portfolio(SelectTop(scored, 2, true), 2);
        slices.Add(port1);
        slices.Add(port2);

        var cls = "REJECTED";
        if (beatsRandom && beatsBottom && beatsAll && (oosTop?.Expectancy ?? 0) > 0 && (oosTop?.Pf ?? 0) > 1)
        {
            cls = valBeats ? "PROMISING" : "FRAGILE";
        }
        else if (beatsRandom || beatsBottom)
        {
            cls = "FRAGILE";
        }

        if (cls is not "PROMISING")
        {
            notes.Add("NO ROBUST CONDITIONAL ALPHA FOUND. Router did not beat random same-count selection on OOS with positive expectancy.");
        }

        return new Wave5RouterResult(universe, ordered, cond, slices, notes, cls);
    }

    public static string Phase(DateTimeOffset time)
    {
        if (time < TrainEnd)
        {
            return "IS";
        }

        return time < ValEnd ? "VALIDATION" : "OOS";
    }

    private sealed record Scored(Wave5Event Event, double Score, int Consensus, bool Conflict);

    private static List<Scored> Score(List<Wave5Event> ordered)
    {
        var scored = new List<Scored>(ordered.Count);
        var byKey = new Dictionary<string, List<(DateTimeOffset Exit, double R, int Vol, int Trend, string Symbol, string Tf, string Fam)>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            var r = e.GrossRet / 0.02;
            var cut = e.SignalTime.AddDays(-LookbackDays);
            double regime = 0, recency = 0, symbolFit = 0, tfFit = 0, volFit = 0;
            if (byKey.TryGetValue(e.Strategy, out var hist))
            {
                var nReg = 0;
                var nRec = 0;
                var nSym = 0;
                var nTf = 0;
                var nVol = 0;
                var sReg = 0d;
                var sRec = 0d;
                var sSym = 0d;
                var sTf = 0d;
                var sVol = 0d;
                foreach (var h in hist)
                {
                    if (h.Exit < cut || h.Exit >= e.SignalTime)
                    {
                        continue;
                    }

                    nRec++;
                    sRec += h.R;
                    if (h.Vol == e.VolRegime)
                    {
                        nVol++;
                        sVol += h.R;
                        if (h.Trend == e.TrendRegime)
                        {
                            nReg++;
                            sReg += h.R;
                        }
                    }

                    if (string.Equals(h.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        nSym++;
                        sSym += h.R;
                    }

                    if (string.Equals(h.Tf, e.Timeframe, StringComparison.OrdinalIgnoreCase))
                    {
                        nTf++;
                        sTf += h.R;
                    }
                }

                recency = nRec >= MinBucketTrades ? sRec / nRec : 0;
                volFit = nVol >= MinBucketTrades ? sVol / nVol : 0;
                regime = nReg >= MinBucketTrades ? sReg / nReg : 0;
                symbolFit = nSym >= MinBucketTrades ? sSym / nSym : 0;
                tfFit = nTf >= MinBucketTrades ? sTf / nTf : 0;
            }

            var raw = (regime + recency + symbolFit + tfFit + volFit) / 5.0;
            scored.Add(new Scored(e, raw, 0, false));

            if (!byKey.TryGetValue(e.Strategy, out var list))
            {
                list = [];
                byKey[e.Strategy] = list;
            }

            list.Add((e.ExitTime, r, e.VolRegime, e.TrendRegime, e.Symbol, e.Timeframe, e.Family));
            if (list.Count > 4000)
            {
                list.RemoveRange(0, list.Count - 3000);
            }
        }

        return AttachConsensus(scored);
    }

    private static List<Scored> AttachConsensus(List<Scored> scored)
    {
        var groups = scored
            .GroupBy(s => (s.Event.Symbol, s.Event.Timeframe, s.Event.SignalTime))
            .ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<Scored>(scored.Count);
        foreach (var s in scored)
        {
            var g = groups[(s.Event.Symbol, s.Event.Timeframe, s.Event.SignalTime)];
            var sameDir = g.Where(x => x.Event.Direction == s.Event.Direction).Select(x => x.Event.Family).Distinct().Count();
            var conflict = g.Any(x => x.Event.Direction != s.Event.Direction);
            result.Add(s with { Consensus = sameDir, Conflict = conflict });
        }

        return result;
    }

    private static List<Wave5Event> SelectTop(IReadOnlyList<Scored> scored, int k, bool top)
    {
        var picked = new List<Wave5Event>();
        foreach (var group in scored.GroupBy(s => (s.Event.Timeframe, s.Event.SignalTime)))
        {
            var ordered = top
                ? group.OrderByDescending(s => s.Score).ThenBy(s => s.Event.Symbol)
                : group.OrderBy(s => s.Score).ThenBy(s => s.Event.Symbol);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in ordered)
            {
                if (!seen.Add(s.Event.Symbol))
                {
                    continue;
                }

                picked.Add(s.Event);
                if (seen.Count >= k)
                {
                    break;
                }
            }
        }

        return picked;
    }

    private static List<Wave5Event> SelectRandom(IReadOnlyList<Scored> scored, int k, int seed)
    {
        var rng = new Random(seed);
        var picked = new List<Wave5Event>();
        foreach (var group in scored.GroupBy(s => (s.Event.Timeframe, s.Event.SignalTime)))
        {
            var bag = group.Select(s => s.Event).ToList();
            for (var i = bag.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in bag)
            {
                if (!seen.Add(e.Symbol))
                {
                    continue;
                }

                picked.Add(e);
                if (seen.Count >= k)
                {
                    break;
                }
            }
        }

        return picked;
    }

    private static List<Wave5CondRow> ConditionalTables(List<Wave5Event> isEvents)
    {
        var rows = new List<Wave5CondRow>();
        foreach (var grp in isEvents.GroupBy(e => e.Strategy).OrderBy(g => g.Key))
        {
            rows.Add(Cond($"{grp.Key}|ALL", grp));
            rows.Add(Cond($"{grp.Key}|VOL_LOW", grp.Where(e => e.VolRegime < 0)));
            rows.Add(Cond($"{grp.Key}|VOL_MID", grp.Where(e => e.VolRegime == 0)));
            rows.Add(Cond($"{grp.Key}|VOL_HIGH", grp.Where(e => e.VolRegime > 0)));
            rows.Add(Cond($"{grp.Key}|TREND_UP", grp.Where(e => e.TrendRegime > 0)));
            rows.Add(Cond($"{grp.Key}|TREND_DOWN", grp.Where(e => e.TrendRegime < 0)));
            rows.Add(Cond($"{grp.Key}|BTC_UP", grp.Where(e => e.BtcRegime > 0)));
            rows.Add(Cond($"{grp.Key}|BTC_DOWN", grp.Where(e => e.BtcRegime < 0)));
            rows.Add(Cond($"{grp.Key}|BREADTH_HIGH", grp.Where(e => e.Breadth >= 0.6)));
            rows.Add(Cond($"{grp.Key}|BREADTH_LOW", grp.Where(e => e.Breadth <= 0.4)));
            rows.Add(Cond($"{grp.Key}|RS_HIGH", grp.Where(e => e.RelStrength > 0)));
            rows.Add(Cond($"{grp.Key}|RS_LOW", grp.Where(e => e.RelStrength < 0)));
            rows.Add(Cond($"{grp.Key}|VOL_SHOCK", grp.Where(e => e.RelVolume >= 1.2)));
            rows.Add(Cond($"{grp.Key}|LONG", grp.Where(e => e.Direction > 0)));
            rows.Add(Cond($"{grp.Key}|SHORT", grp.Where(e => e.Direction < 0)));
        }

        return rows;
    }

    private static Wave5CondRow Cond(string label, IEnumerable<Wave5Event> rows)
    {
        var list = rows.ToList();
        var stats = Stats(label, list, 1m);
        var parts = label.Split('|');
        return new Wave5CondRow(parts[0], parts.Length > 1 ? parts[1] : "ALL", stats.Trades, stats.Pf, stats.Expectancy, stats.WinRate);
    }

    public static Wave5SliceStats Stats(string label, IEnumerable<Wave5Event> rows, decimal costMult)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            return new Wave5SliceStats(label, 0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 0, 0, 0, "", 0);
        }

        var rets = list.Select(e => Wave5Harvest.NetReturn(e.GrossRet, costMult)).ToList();
        var wins = rets.Where(v => v > 0).Sum();
        var losses = rets.Where(v => v < 0).Sum();
        var pf = losses >= 0 || Math.Abs(losses) < 1e-18 ? (wins > 0 ? double.PositiveInfinity : double.NaN) : wins / Math.Abs(losses);
        var eq = 0d;
        var peak = 0d;
        var dd = 0d;
        foreach (var r in rets)
        {
            eq += r;
            if (eq > peak)
            {
                peak = eq;
            }

            dd = Math.Min(dd, eq - peak);
        }

        var bySym = list.GroupBy(e => e.Symbol).Select(g => (g.Key, Net: g.Sum(x => Wave5Harvest.NetReturn(x.GrossRet, costMult)))).OrderByDescending(x => Math.Abs(x.Net)).ToList();
        var totalAbs = bySym.Sum(x => Math.Abs(x.Net));
        var top = bySym.FirstOrDefault();
        return new Wave5SliceStats(
            label,
            list.Count,
            pf,
            rets.Average(),
            rets.Count(v => v > 0) / (double)rets.Count,
            rets.Average() / 0.02,
            rets.Average(),
            dd,
            list.Select(e => e.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            list.Select(e => e.Strategy).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            top.Key ?? "",
            totalAbs < 1e-12 ? 0 : Math.Abs(top.Net) / totalAbs);
    }

    private static Wave5SliceStats Portfolio(IReadOnlyList<Wave5Event> selected, int maxPos)
    {
        var oos = selected.Where(e => Phase(e.SignalTime) == "OOS").OrderBy(e => e.FillTime).ToList();
        var open = new List<Wave5Event>();
        var taken = new List<Wave5Event>();
        foreach (var e in oos)
        {
            open.RemoveAll(x => x.ExitTime <= e.FillTime);
            if (open.Count >= maxPos || open.Any(x => string.Equals(x.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            open.Add(e);
            taken.Add(e);
        }

        return Stats($"OOS-PORT-MAX{maxPos}", taken, 1m);
    }

    private static string Fmt(double? v) =>
        v is null || double.IsNaN(v.Value) ? "n/a" : v.Value.ToString("0.0000", CultureInfo.InvariantCulture);
}
