using TradingPlatform.Research.Framework;

namespace TradingPlatform.Research.Alpha;

/// <summary>
/// Point-in-time universe. A coin is tradable during UTC day d when, using days d-30..d-1 only, it has at least
/// <see cref="MinHistoryDays"/> days since its first bar and a median daily quote volume ≥ <see cref="MinMedianDailyQuoteVolume"/>,
/// and it has a bar at the hour itself. The same trailing window supplies the liquidity bucket and daily volatility for costs.
/// </summary>
public sealed class PanelUniverse
{
    public const int MinHistoryDays = 30;
    public const int WindowDays = 30;
    public const int MinBarsPerDay = 20;
    public const double MinMedianDailyQuoteVolume = 5_000_000d;

    public PanelUniverse(HourlyPanel panel, double minMedianDailyQuoteVolume = MinMedianDailyQuoteVolume)
    {
        Panel = panel;
        var n = panel.Coins;
        var days = panel.Days;
        DailyQuoteVolume = new double[n][];
        MedianQuoteVolume = new double[n][];
        DailyVolatilityPercent = new double[n][];
        DayEligible = new bool[n][];
        FirstDay = new int[n];
        Parallel.For(0, n, c =>
        {
            var qv = new double[days];
            var dayClose = new double[days];
            var first = -1;
            for (var d = 0; d < days; d++)
            {
                var sum = 0d;
                var bars = 0;
                var last = double.NaN;
                for (var h = d * 24; h < d * 24 + 24; h++)
                {
                    var close = panel.Close[c][h];
                    if (float.IsNaN(close))
                    {
                        continue;
                    }

                    bars++;
                    last = close;
                    var v = panel.QuoteVolume[c][h];
                    if (float.IsFinite(v))
                    {
                        sum += v;
                    }
                }

                qv[d] = bars >= MinBarsPerDay ? sum : double.NaN;
                dayClose[d] = bars > 0 ? last : double.NaN;
                if (first < 0 && bars > 0)
                {
                    first = d;
                }
            }

            var median = new double[days];
            var vol = new double[days];
            var eligible = new bool[days];
            var window = new List<double>(WindowDays);
            var rets = new List<double>(WindowDays);
            for (var d = 0; d < days; d++)
            {
                median[d] = double.NaN;
                vol[d] = double.NaN;
                if (first < 0 || d - first < MinHistoryDays)
                {
                    continue;
                }

                window.Clear();
                rets.Clear();
                for (var k = d - WindowDays; k < d; k++)
                {
                    if (k < 0)
                    {
                        continue;
                    }

                    if (!double.IsNaN(qv[k]))
                    {
                        window.Add(qv[k]);
                    }

                    if (k > 0 && dayClose[k] > 0 && dayClose[k - 1] > 0)
                    {
                        rets.Add(dayClose[k] / dayClose[k - 1] - 1d);
                    }
                }

                if (window.Count < 20 || rets.Count < 20)
                {
                    continue;
                }

                window.Sort();
                median[d] = window.Count % 2 == 1 ? window[window.Count / 2] : (window[window.Count / 2 - 1] + window[window.Count / 2]) / 2d;
                vol[d] = Math.Sqrt(ResearchStatistics.Variance(rets)) * 100d;
                eligible[d] = median[d] >= minMedianDailyQuoteVolume;
            }

            DailyQuoteVolume[c] = qv;
            MedianQuoteVolume[c] = median;
            DailyVolatilityPercent[c] = vol;
            DayEligible[c] = eligible;
            FirstDay[c] = first;
        });
    }

    public HourlyPanel Panel { get; }
    public double[][] DailyQuoteVolume { get; }
    public double[][] MedianQuoteVolume { get; }
    public double[][] DailyVolatilityPercent { get; }
    public bool[][] DayEligible { get; }
    public int[] FirstDay { get; }

    public bool Eligible(int coin, int hour) =>
        hour >= 0 && hour < Panel.Hours && DayEligible[coin][hour / 24] && !float.IsNaN(Panel.Close[coin][hour]);

    public int EligibleCount(int hour)
    {
        var count = 0;
        for (var c = 0; c < Panel.Coins; c++)
        {
            if (Eligible(c, hour))
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>Double mirror of <see cref="CostProfiles"/> for the panel loop. Percent per side.</summary>
public static class AlphaCosts
{
    public static double PerSidePercent(CostProfileKind kind, double medianDailyQuoteVolume, double dailyVolatilityPercent, double orderNotional)
    {
        var bucket = CostProfiles.Bucket(double.IsFinite(medianDailyQuoteVolume) ? (decimal)medianDailyQuoteVolume : 0m);
        var halfSpread = (double)CostProfiles.DefaultHalfSpreadPercent(bucket);
        var impact = Impact(orderNotional, medianDailyQuoteVolume, dailyVolatilityPercent);
        return kind switch
        {
            CostProfileKind.Base => (double)CostProfiles.TakerFeePercent + halfSpread + impact,
            CostProfileKind.Conservative => (double)CostProfiles.TakerFeePercent + 2d * (halfSpread + impact) + 0.01d,
            _ => (double)CostProfiles.StressFeePercent + 3d * (halfSpread + impact) + 0.02d
        };
    }

    public static double FeePercent(CostProfileKind kind) =>
        (double)(kind == CostProfileKind.Stress ? CostProfiles.StressFeePercent : CostProfiles.TakerFeePercent);

    public static int DelayBars(CostProfileKind kind) => kind == CostProfileKind.Stress ? 1 : 0;

    public static double Impact(double orderNotional, double dailyQuoteVolume, double dailyVolatilityPercent)
    {
        if (orderNotional <= 0d)
        {
            return 0d;
        }

        if (!(dailyQuoteVolume > 0d) || !(dailyVolatilityPercent > 0d))
        {
            return (double)CostProfiles.MaxImpactPercent;
        }

        return Math.Min((double)CostProfiles.MaxImpactPercent, dailyVolatilityPercent * Math.Sqrt(orderNotional / dailyQuoteVolume));
    }
}
