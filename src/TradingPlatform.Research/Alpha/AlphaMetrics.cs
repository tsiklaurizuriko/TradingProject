using TradingPlatform.Research.Framework;

namespace TradingPlatform.Research.Alpha;

public sealed record AlphaSummary(
    string Split,
    string Cost,
    int Days,
    double SharpePerDay,
    double SharpeAnnual,
    double Sortino,
    double ProfitFactorDaily,
    double TotalReturnPercent,
    double MaxDrawdownPercent,
    double Gross,
    double Fees,
    double Slippage,
    double Funding,
    double Net,
    double TurnoverPerDay,
    int Entries,
    int DelistExits,
    double AverageGross,
    double AverageNet,
    double BetaBtc,
    double CorrelationBtc,
    double Breadth,
    double TopCoinShare,
    string TopCoin,
    int CoinsTraded,
    SharpeMoments Moments,
    double GrossSharpeAnnual = double.NaN);

public sealed record EventSummary(int Trades, double MeanNetPercent, double TStat, double ProfitFactor, double WinRate);

public static class AlphaMetrics
{
    public static AlphaSummary Summarize(SimResult r, HourlyPanel panel)
    {
        var d = r.DailyReturns;
        var m = ResearchStatistics.Moments(d);
        var downside = d.Where(x => x < 0).ToList();
        var downDev = downside.Count > 1 ? Math.Sqrt(downside.Sum(x => x * x) / d.Length) : 0d;
        var mean = d.Length > 0 ? d.Average() : 0d;
        var pos = d.Where(x => x > 0).Sum();
        var neg = -d.Where(x => x < 0).Sum();
        var (total, maxDd) = Compound(d);
        var btc = BtcDailyReturns(panel, r.FromDay, d.Length);
        var (beta, corr) = Regression(d, btc);
        var traded = r.Coins.Where(c => c.Entries > 0).ToList();
        var breadth = traded.Count == 0 ? 0d : traded.Count(c => c.Net > 0) / (double)traded.Count;
        var top = traded.OrderByDescending(c => c.Net).FirstOrDefault();
        var net = r.Net;
        var topShare = top is null || net <= 0 ? double.NaN : top.Net / net;
        return new AlphaSummary(
            r.Split,
            r.Cost.ToString().ToUpperInvariant(),
            d.Length,
            m.Sharpe,
            m.Sharpe * Math.Sqrt(365d),
            downDev > 0 ? mean / downDev * Math.Sqrt(365d) : 0d,
            neg > 0 ? pos / neg : pos > 0 ? double.PositiveInfinity : 0d,
            total,
            maxDd,
            r.Gross,
            r.Fees,
            r.Slippage,
            r.Funding,
            net,
            d.Length > 0 ? r.Turnover / d.Length : 0d,
            r.Entries,
            r.DelistExits,
            r.AverageGross,
            r.AverageNet,
            beta,
            corr,
            breadth,
            topShare,
            top?.Symbol ?? "",
            traded.Count,
            m,
            ResearchStatistics.Moments(r.DailyGrossReturns).Sharpe * Math.Sqrt(365d));
    }

    /// <summary>
    /// Per-event net return in percent: direction × close-to-close move from entry to exit (both shifted by the
    /// profile's delay), minus the per-side cost twice and the funding settled in between.
    /// </summary>
    public static EventSummary Events(PanelUniverse universe, IReadOnlyList<EventTrade> trades, CostProfileKind kind, double unitNotional, int windowTo)
    {
        var panel = universe.Panel;
        var delay = AlphaCosts.DelayBars(kind);
        var nets = new List<double>();
        foreach (var e in trades)
        {
            var entry = e.EntryHour + delay;
            var exit = Math.Min(e.ExitHour + delay, windowTo - 1);
            if (exit <= entry)
            {
                continue;
            }

            var p0 = panel.Close[e.Coin][entry];
            var p1 = LastValid(panel.Close[e.Coin], entry, exit);
            if (float.IsNaN(p0) || double.IsNaN(p1))
            {
                continue;
            }

            var move = e.Direction * (p1 / p0 - 1d) * 100d;
            var funding = 0d;
            for (var t = entry + 1; t <= exit; t++)
            {
                var f = panel.Funding[e.Coin][t];
                if (!float.IsNaN(f))
                {
                    funding += e.Direction * f * 100d;
                }
            }

            var dayIn = entry / 24;
            var dayOut = exit / 24;
            var cost = AlphaCosts.PerSidePercent(kind, universe.MedianQuoteVolume[e.Coin][dayIn], universe.DailyVolatilityPercent[e.Coin][dayIn], unitNotional)
                + AlphaCosts.PerSidePercent(kind, universe.MedianQuoteVolume[e.Coin][dayOut], universe.DailyVolatilityPercent[e.Coin][dayOut], unitNotional);
            nets.Add(move - cost - funding);
        }

        if (nets.Count == 0)
        {
            return new EventSummary(0, 0, 0, 0, 0);
        }

        var mean = nets.Average();
        var sd = Math.Sqrt(ResearchStatistics.Variance(nets));
        var pos = nets.Where(x => x > 0).Sum();
        var neg = -nets.Where(x => x < 0).Sum();
        return new EventSummary(
            nets.Count,
            mean,
            sd > 0 ? mean / sd * Math.Sqrt(nets.Count) : 0d,
            neg > 0 ? pos / neg : pos > 0 ? double.PositiveInfinity : 0d,
            nets.Count(x => x > 0) / (double)nets.Count);
    }

    public static double[] BtcDailyReturns(HourlyPanel panel, int fromDay, int days)
    {
        var btc = panel.IndexOf("BTCUSDT");
        var result = new double[days];
        if (btc < 0)
        {
            return result;
        }

        var row = panel.Close[btc];
        for (var i = 0; i < days; i++)
        {
            var end = (fromDay + i + 1) * 24 - 1;
            var start = end - 24;
            if (start >= 0 && end < panel.Hours && row[start] > 0 && row[end] > 0)
            {
                result[i] = row[end] / (double)row[start] - 1d;
            }
        }

        return result;
    }

    public static (double Beta, double Correlation) Regression(IReadOnlyList<double> y, IReadOnlyList<double> x)
    {
        var n = Math.Min(y.Count, x.Count);
        if (n < 3)
        {
            return (0, 0);
        }

        double mx = 0, my = 0;
        for (var i = 0; i < n; i++)
        {
            mx += x[i];
            my += y[i];
        }

        mx /= n;
        my /= n;
        double sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < n; i++)
        {
            sxy += (x[i] - mx) * (y[i] - my);
            sxx += (x[i] - mx) * (x[i] - mx);
            syy += (y[i] - my) * (y[i] - my);
        }

        return (sxx > 0 ? sxy / sxx : 0, sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : 0);
    }

    public static (double TotalPercent, double MaxDrawdownPercent) Compound(IReadOnlyList<double> returns)
    {
        var equity = 1d;
        var peak = 1d;
        var maxDd = 0d;
        foreach (var r in returns)
        {
            equity *= 1d + r;
            peak = Math.Max(peak, equity);
            maxDd = Math.Max(maxDd, peak > 0 ? (peak - equity) / peak : 0);
        }

        return ((equity - 1d) * 100d, maxDd * 100d);
    }

    private static double LastValid(float[] row, int from, int to)
    {
        for (var t = to; t >= from; t--)
        {
            if (!float.IsNaN(row[t]))
            {
                return row[t];
            }
        }

        return double.NaN;
    }
}

public sealed record IcSummary(int Periods, double MeanIc, double IcTStat, double MeanSpreadBp, double SpreadTStat, double MeanCoins);

public sealed record EventScreen(int Events, double MeanBp, double TStat, double MeanExcessBp, double ExcessTStat);

/// <summary>Before-cost information screens. Forward returns never cross the window end.</summary>
public static class InformationScreen
{
    /// <summary>
    /// Spearman IC between the signal at close t and the forward return over (t, t+horizon], at non-overlapping
    /// decision closes. Spread is the top-minus-bottom quantile mean forward return in basis points.
    /// </summary>
    /// <param name="skip">Bars between the signal close and the start of the forward return; 1 removes bid-ask bounce in the shared close.</param>
    public static IcSummary CrossSectional(PanelUniverse universe, CoinSignal signal, SimWindow window, int horizon, double quantile = 0.2, int warmup = 0, int skip = 0)
    {
        var panel = universe.Panel;
        var ics = new List<double>();
        var spreads = new List<double>();
        var coins = 0d;
        for (var t = window.From + warmup + horizon - 1; t + skip + horizon < window.To; t += horizon)
        {
            var rows = new List<(double S, double F)>();
            for (var c = 0; c < panel.Coins; c++)
            {
                if (!universe.Eligible(c, t))
                {
                    continue;
                }

                var s = signal(c, t);
                var p0 = panel.Close[c][t + skip];
                var p1 = panel.Close[c][t + skip + horizon];
                if (!double.IsFinite(s) || float.IsNaN(p0) || float.IsNaN(p1))
                {
                    continue;
                }

                rows.Add((s, p1 / (double)p0 - 1d));
            }

            if (rows.Count < CrossSectionalModel.MinimumCoins)
            {
                continue;
            }

            coins += rows.Count;
            ics.Add(Spearman(rows));
            var sorted = rows.OrderBy(r => r.S).ToList();
            var k = Math.Max(1, (int)(sorted.Count * quantile));
            var bottom = sorted.Take(k).Average(r => r.F);
            var top = sorted.Skip(sorted.Count - k).Average(r => r.F);
            spreads.Add((top - bottom) * 10_000d);
        }

        return new IcSummary(ics.Count, Mean(ics), TStat(ics), Mean(spreads), TStat(spreads), ics.Count > 0 ? coins / ics.Count : 0);
    }

    /// <summary>
    /// Direction-adjusted forward return per event, raw and in excess of the equal-weight eligible universe.
    /// t-statistics are clustered by UTC day (events on the same day are averaged first) because simultaneous
    /// events share the market move.
    /// </summary>
    public static EventScreen Events(PanelUniverse universe, EventTrigger trigger, SimWindow window, int horizon, int step = 1, int warmup = 0)
    {
        var panel = universe.Panel;
        var raw = new List<double>();
        var excess = new List<double>();
        var byDay = new SortedDictionary<int, (double Raw, double Excess, int N)>();
        var busyUntil = new int[panel.Coins];
        for (var t = window.From + warmup; t + horizon < window.To; t++)
        {
            if ((t + 1) % step != 0)
            {
                continue;
            }

            double? market = null;
            for (var c = 0; c < panel.Coins; c++)
            {
                if (busyUntil[c] > t || !universe.Eligible(c, t))
                {
                    continue;
                }

                var d = trigger(c, t);
                if (d == 0)
                {
                    continue;
                }

                var p1 = panel.Close[c][t + horizon];
                if (float.IsNaN(p1))
                {
                    continue;
                }

                market ??= MarketReturn(universe, t, horizon);
                var r = Math.Sign(d) * (p1 / (double)panel.Close[c][t] - 1d) * 10_000d;
                var x = r - Math.Sign(d) * market.Value * 10_000d;
                raw.Add(r);
                excess.Add(x);
                var day = t / 24;
                var cell = byDay.TryGetValue(day, out var v) ? v : (0d, 0d, 0);
                byDay[day] = (cell.Item1 + r, cell.Item2 + x, cell.Item3 + 1);
                busyUntil[c] = t + horizon;
            }
        }

        var dayRaw = byDay.Values.Select(v => v.Raw / v.N).ToList();
        var dayExcess = byDay.Values.Select(v => v.Excess / v.N).ToList();
        return new EventScreen(raw.Count, Mean(raw), TStat(dayRaw), Mean(excess), TStat(dayExcess));
    }

    public static double MarketReturn(PanelUniverse universe, int t, int horizon)
    {
        var panel = universe.Panel;
        var sum = 0d;
        var count = 0;
        for (var c = 0; c < panel.Coins; c++)
        {
            if (!universe.Eligible(c, t))
            {
                continue;
            }

            var p1 = panel.Close[c][t + horizon];
            if (!float.IsNaN(p1))
            {
                sum += p1 / (double)panel.Close[c][t] - 1d;
                count++;
            }
        }

        return count > 0 ? sum / count : 0d;
    }

    public static double Spearman(IReadOnlyList<(double S, double F)> rows)
    {
        var rs = Ranks(rows.Select(r => r.S).ToArray());
        var rf = Ranks(rows.Select(r => r.F).ToArray());
        return AlphaMetrics.Regression(rf, rs).Correlation;
    }

    private static double[] Ranks(double[] values)
    {
        var order = Enumerable.Range(0, values.Length).OrderBy(i => values[i]).ToArray();
        var ranks = new double[values.Length];
        var i = 0;
        while (i < order.Length)
        {
            var j = i;
            while (j + 1 < order.Length && values[order[j + 1]] == values[order[i]])
            {
                j++;
            }

            var avg = (i + j) / 2d;
            for (var k = i; k <= j; k++)
            {
                ranks[order[k]] = avg;
            }

            i = j + 1;
        }

        return ranks;
    }

    private static double Mean(IReadOnlyList<double> v) => v.Count > 0 ? v.Average() : 0d;

    private static double TStat(IReadOnlyList<double> v)
    {
        if (v.Count < 3)
        {
            return 0d;
        }

        var sd = Math.Sqrt(ResearchStatistics.Variance(v));
        return sd > 0 ? v.Average() / sd * Math.Sqrt(v.Count) : 0d;
    }
}
