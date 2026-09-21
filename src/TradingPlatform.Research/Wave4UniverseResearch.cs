using TradingPlatform.Backtesting.Validation;

namespace TradingPlatform.Research;

public static class Wave4UniverseResearch
{
    public const int Warmup = 200;
    public const int RelLookback = 24;
    public const int VolLookback = 20;

    public static IReadOnlyList<Wave3IcRow> Evaluate(
        IReadOnlyDictionary<string, IReadOnlyList<(DateTimeOffset Open, decimal Close, decimal Volume)>> series)
    {
        if (!series.TryGetValue("BTCUSDT", out var btc) || btc.Count < Warmup + 48)
        {
            return [];
        }

        var maps = series.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.GroupBy(x => x.Open).ToDictionary(g => g.Key, g => g.Last()),
            StringComparer.OrdinalIgnoreCase);
        var clock = btc.Select(x => x.Open).Distinct().OrderBy(t => t).ToList();
        var n = clock.Count;
        var (isEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(n);
        var rows = new List<Wave3IcRow>();
        foreach (var hyp in new[] { "W4_U_REL24", "W4_U_VOLSHOCK" })
        {
            foreach (var horizon in new[] { 4, 24 })
            {
                foreach (var (phase, from, to) in new[]
                {
                    ("IS", 0, isEnd),
                    ("VALIDATION", isEnd, valEnd),
                    ("OOS", valEnd, n)
                })
                {
                    rows.Add(Eval(hyp, phase, horizon, maps, clock, Math.Max(Warmup, from), to));
                }
            }
        }

        return rows;
    }

    private static Wave3IcRow Eval(
        string hyp,
        string phase,
        int horizon,
        Dictionary<string, Dictionary<DateTimeOffset, (DateTimeOffset Open, decimal Close, decimal Volume)>> maps,
        IReadOnlyList<DateTimeOffset> clock,
        int from,
        int to)
    {
        var cs = new List<double>();
        var top = new List<double>();
        var bot = new List<double>();
        var btcMap = maps["BTCUSDT"];
        for (var t = from; t < to; t++)
        {
            var th = t + horizon;
            var tRel = t - RelLookback;
            if (th >= clock.Count || tRel < 0)
            {
                break;
            }

            var now = clock[t];
            var past = clock[tRel];
            var fut = clock[th];
            if (!btcMap.TryGetValue(now, out var btcNow) || !btcMap.TryGetValue(past, out var btcPast) || btcPast.Close <= 0m)
            {
                continue;
            }

            var btcRel = (double)((btcNow.Close - btcPast.Close) / btcPast.Close);
            var x = new List<double>();
            var y = new List<double>();
            foreach (var (symbol, map) in maps)
            {
                if (!map.TryGetValue(now, out var cur)
                    || !map.TryGetValue(past, out var prev)
                    || !map.TryGetValue(fut, out var ahead)
                    || prev.Close <= 0m
                    || cur.Close <= 0m)
                {
                    continue;
                }

                var fwd = (double)((ahead.Close - cur.Close) / cur.Close);
                double signal;
                if (hyp == "W4_U_REL24")
                {
                    signal = (double)((cur.Close - prev.Close) / prev.Close) - btcRel;
                }
                else
                {
                    var volT = t - VolLookback;
                    if (volT < 0)
                    {
                        continue;
                    }

                    decimal sum = 0;
                    var count = 0;
                    for (var i = volT; i < t; i++)
                    {
                        if (map.TryGetValue(clock[i], out var vb))
                        {
                            sum += vb.Volume;
                            count++;
                        }
                    }

                    if (count < 8 || sum <= 0m)
                    {
                        continue;
                    }

                    signal = (double)(cur.Volume / (sum / count));
                }

                x.Add(signal);
                y.Add(fwd);
            }

            if (x.Count < 30)
            {
                continue;
            }

            var ic = Wave3Math.Spearman(x, y);
            if (!double.IsNaN(ic))
            {
                cs.Add(ic);
            }

            var ranks = Wave3Math.PercentileRanks(x.ToArray());
            var hi = new List<double>();
            var lo = new List<double>();
            for (var i = 0; i < ranks.Length; i++)
            {
                if (double.IsNaN(ranks[i]))
                {
                    continue;
                }

                if (ranks[i] >= 2.0 / 3.0)
                {
                    hi.Add(y[i]);
                }
                else if (ranks[i] < 1.0 / 3.0)
                {
                    lo.Add(y[i]);
                }
            }

            var tm = Wave3Math.Mean(hi);
            var bm = Wave3Math.Mean(lo);
            if (!double.IsNaN(tm))
            {
                top.Add(tm);
            }

            if (!double.IsNaN(bm))
            {
                bot.Add(bm);
            }
        }

        var topMean = Wave3Math.Mean(top);
        var botMean = Wave3Math.Mean(bot);
        return new Wave3IcRow(
            hyp,
            phase,
            horizon,
            cs.Count,
            Wave3Math.Mean(cs),
            double.NaN,
            topMean,
            botMean,
            topMean - botMean,
            topMean,
            double.IsNaN(botMean) ? double.NaN : -botMean,
            Wave3Math.HitRate(top),
            Wave3Math.HitRate(bot.Select(v => -v)),
            double.NaN);
    }
}
