using FluentAssertions;
using TradingPlatform.Research.Alpha;
using TradingPlatform.Research.Framework;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class AlphaEngineTests
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Kline_rows_land_on_their_open_hour_and_headers_are_skipped()
    {
        var panel = new HourlyPanel(Start, 48, ["BTCUSDT"]);
        var open = Start.AddHours(5).ToUnixTimeMilliseconds();
        VisionPanelLoader.ApplyKline(panel, 0, "open_time,open,high,low,close,volume,close_time,quote_volume,count,taker_buy_volume,taker_buy_quote_volume,ignore");
        VisionPanelLoader.ApplyKline(panel, 0, $"{open},100,110,90,105,10,{open + 3_599_999},1050,7,4,420,0");

        panel.Close[0][5].Should().Be(105f);
        panel.High[0][5].Should().Be(110f);
        panel.Low[0][5].Should().Be(90f);
        panel.QuoteVolume[0][5].Should().Be(1050f);
        panel.TakerBuyQuote[0][5].Should().Be(420f);
        float.IsNaN(panel.Close[0][4]).Should().BeTrue();
    }

    [Fact]
    public void Funding_settled_just_after_a_boundary_belongs_to_the_bar_that_opens_there()
    {
        var panel = new HourlyPanel(Start, 48, ["BTCUSDT"]);
        VisionPanelLoader.ApplyFunding(panel, 0, "calc_time,funding_interval_hours,last_funding_rate");
        VisionPanelLoader.ApplyFunding(panel, 0, $"{Start.AddHours(8).ToUnixTimeMilliseconds() + 5},8,0.00010000");
        VisionPanelLoader.ApplyFunding(panel, 0, $"{Start.AddHours(16).ToUnixTimeMilliseconds()},-0.00020000");

        panel.Funding[0][8].Should().BeApproximately(0.0001f, 1e-9f);
        panel.Funding[0][15].Should().BeApproximately(-0.0002f, 1e-9f);
        float.IsNaN(panel.Funding[0][16]).Should().BeTrue();
    }

    [Fact]
    public void Panel_round_trips_through_the_binary_cache()
    {
        var panel = Synthetic(3, 24 * 5, seed: 1);
        var path = Path.Combine(Path.GetTempPath(), $"panel-{Guid.NewGuid():N}.bin");
        try
        {
            panel.Save(path);
            var loaded = HourlyPanel.Load(path);
            loaded.Symbols.Should().Equal(panel.Symbols);
            loaded.Close[2].Should().Equal(panel.Close[2]);
            loaded.Funding[1].Should().Equal(panel.Funding[1]);
            loaded.Fingerprint().Should().Be(panel.Fingerprint());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(CostProfileKind.Base, 2_000_000_000d, 3d, 50_000d)]
    [InlineData(CostProfileKind.Conservative, 80_000_000d, 5d, 20_000d)]
    [InlineData(CostProfileKind.Stress, 6_000_000d, 9d, 5_000d)]
    [InlineData(CostProfileKind.Conservative, double.NaN, double.NaN, 5_000d)]
    public void Double_costs_mirror_CostProfiles(CostProfileKind kind, double qv, double vol, double notional)
    {
        var bucket = CostProfiles.Bucket(double.IsNaN(qv) ? 0m : (decimal)qv);
        var impact = CostProfiles.ImpactPercent((decimal)notional, double.IsNaN(qv) ? 0m : (decimal)qv, double.IsNaN(vol) ? 0m : (decimal)vol);
        var profile = CostProfiles.For(kind, bucket, impactPercent: impact);

        AlphaCosts.PerSidePercent(kind, qv, vol, notional)
            .Should().BeApproximately((double)(profile.FeePercent + profile.SlippagePercent), 1e-9);
        AlphaCosts.DelayBars(kind).Should().Be(profile.ExecutionDelayBars);
    }

    [Fact]
    public void Universe_for_a_day_ignores_that_day_and_later()
    {
        var panel = Synthetic(4, 24 * 60, seed: 2);
        var before = new PanelUniverse(panel, minMedianDailyQuoteVolume: 1_000d);
        var day = 45;
        for (var t = day * 24; t < panel.Hours; t++)
        {
            panel.QuoteVolume[1][t] = 0f;
            panel.Close[1][t] *= 3f;
        }

        var after = new PanelUniverse(panel, minMedianDailyQuoteVolume: 1_000d);
        after.DayEligible[1][..(day + 1)].Should().Equal(before.DayEligible[1][..(day + 1)]);
        after.MedianQuoteVolume[1][..(day + 1)].Should().Equal(before.MedianQuoteVolume[1][..(day + 1)]);
        after.DailyVolatilityPercent[1][..(day + 1)].Should().Equal(before.DailyVolatilityPercent[1][..(day + 1)]);
        before.DayEligible[1][29].Should().BeFalse("30 days of history are required");
        before.DayEligible[1][31].Should().BeTrue();
    }

    [Fact]
    public void Features_at_t_do_not_change_when_later_bars_change()
    {
        var panel = Synthetic(5, 24 * 70, seed: 3);
        var t = 24 * 50 + 7;
        var probes = Probe(new PanelFeatures(new PanelUniverse(panel, 1_000d)), t);
        for (var h = t + 1; h < panel.Hours; h++)
        {
            for (var c = 0; c < panel.Coins; c++)
            {
                panel.Close[c][h] *= 1.7f;
                panel.High[c][h] *= 2f;
                panel.Low[c][h] *= 0.5f;
                panel.QuoteVolume[c][h] *= 9f;
                panel.TakerBuyQuote[c][h] = 0f;
                panel.Funding[c][h] = 0.01f;
            }
        }

        Probe(new PanelFeatures(new PanelUniverse(panel, 1_000d)), t).Should().Equal(probes);
    }

    [Fact]
    public void A_long_position_earns_the_next_bar_and_pays_cost_and_positive_funding()
    {
        var panel = Flat(2, 24 * 40, 100f);
        var t = 24 * 35;
        for (var h = t + 1; h < panel.Hours; h++)
        {
            panel.Close[1][h] = 101f;
        }

        panel.Funding[1][t + 1] = 0.0001f;
        var universe = new PanelUniverse(panel, 1_000d);
        var model = new FixedModel(t, coin: 1, weight: 1d);

        var result = AlphaSimulator.Run(universe, model, Window(universe, t - 2, t + 3), new SimOptions(CostProfileKind.Base, Book: 10_000d));

        result.Gross.Should().BeApproximately(100d, 1e-6);
        result.Funding.Should().BeApproximately(1d, 1e-3);
        var entry = AlphaCosts.PerSidePercent(CostProfileKind.Base, universe.MedianQuoteVolume[1][t / 24], universe.DailyVolatilityPercent[1][t / 24], 10_000d);
        var exit = AlphaCosts.PerSidePercent(CostProfileKind.Base, universe.MedianQuoteVolume[1][t / 24], universe.DailyVolatilityPercent[1][t / 24], 10_000d);
        (result.Fees + result.Slippage).Should().BeApproximately(10_000d * (entry + exit) / 100d, 1e-6);
        result.Entries.Should().Be(1);
        result.DailyReturns.Sum().Should().BeApproximately(result.Net / 10_000d, 1e-9);
    }

    [Fact]
    public void Stress_executes_one_bar_later()
    {
        var panel = Flat(2, 24 * 40, 100f);
        var t = 24 * 35;
        panel.Close[1][t + 1] = 110f;
        for (var h = t + 2; h < panel.Hours; h++)
        {
            panel.Close[1][h] = 110f;
        }

        var universe = new PanelUniverse(panel, 1_000d);
        var baseRun = AlphaSimulator.Run(universe, new FixedModel(t, 1, 1d), Window(universe, t - 2, t + 6), new SimOptions(CostProfileKind.Base));
        var stress = AlphaSimulator.Run(universe, new FixedModel(t, 1, 1d), Window(universe, t - 2, t + 6), new SimOptions(CostProfileKind.Stress));

        baseRun.Gross.Should().BeApproximately(10_000d, 1e-6);
        stress.Gross.Should().BeApproximately(0d, 1e-6, "the jump happened before the delayed fill");
    }

    [Fact]
    public void A_held_coin_whose_bars_stop_is_closed_and_counted()
    {
        var panel = Flat(2, 24 * 40, 100f);
        var t = 24 * 35;
        for (var h = t + 3; h < panel.Hours; h++)
        {
            panel.Close[1][h] = float.NaN;
        }

        var universe = new PanelUniverse(panel, 1_000d);
        var result = AlphaSimulator.Run(universe, new FixedModel(t, 1, 1d), Window(universe, t - 2, t + 8), new SimOptions(CostProfileKind.Base));

        result.DelistExits.Should().Be(1);
        result.Turnover.Should().BeApproximately(2d, 1e-9);
    }

    [Fact]
    public void Cross_sectional_sleeves_are_dollar_neutral_and_average_over_the_hold()
    {
        var panel = Synthetic(30, 24 * 40, seed: 4);
        var universe = new PanelUniverse(panel, 1_000d);
        var model = new CrossSectionalModel(universe, (c, _) => c, step: 24, hold: 48, quantile: 0.2);
        var t = 24 * 35 - 1;
        var first = new double[panel.Coins];
        model.Targets(t, first);
        var second = new double[panel.Coins];
        model.Targets(t + 24, second);

        first.Sum().Should().BeApproximately(0d, 1e-12);
        first.Sum(Math.Abs).Should().BeApproximately(0.5d, 1e-12, "the first sleeve is half the book");
        second.Sum().Should().BeApproximately(0d, 1e-12);
        second.Sum(Math.Abs).Should().BeLessThanOrEqualTo(1d + 1e-12);
    }

    [Fact]
    public void Oos_windows_need_a_matching_ticket()
    {
        var splits = AlphaSplits.For(0, 1734);
        splits.InSampleEndDay.Should().Be(1040);
        splits.ValidationEndDay.Should().Be(1387);
        var vault = new OosVault(Path.Combine(Path.GetTempPath(), $"oos-{Guid.NewGuid():N}.jsonl"));
        var ticket = vault.Open("alpha:test", "hash", splits.Days, "unit test");

        SimWindow.Oos(splits, ticket).From.Should().Be(1387 * 24);
        var other = AlphaSplits.For(100, 1734);
        var act = () => SimWindow.Oos(other, ticket);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Ic_screen_never_reads_past_the_window()
    {
        var panel = Synthetic(25, 24 * 80, seed: 5);
        var universe = new PanelUniverse(panel, 1_000d);
        var features = new PanelFeatures(universe);
        var splits = AlphaSplits.For(0, 80);
        var window = SimWindow.InSample(splits);
        var before = InformationScreen.CrossSectional(universe, (c, t) => features.Ret(c, t, 24), window, horizon: 24);
        for (var h = window.To; h < panel.Hours; h++)
        {
            for (var c = 0; c < panel.Coins; c++)
            {
                panel.Close[c][h] = c * 1000f + 1f;
            }
        }

        var after = InformationScreen.CrossSectional(universe, (c, t) => features.Ret(c, t, 24), window, horizon: 24);
        after.Should().Be(before);
        before.Periods.Should().BeGreaterThan(5);
    }

    [Fact]
    public void Metrics_use_the_last_print_strictly_before_the_bar_close()
    {
        var panel = new HourlyPanel(Start, 48, ["BTCUSDT"]);
        var metrics = new MetricsPanel(panel);
        var stamp = Enumerable.Repeat((short)-1, panel.Hours).ToArray();
        metrics.Apply(0, "create_time,symbol,sum_open_interest,sum_open_interest_value,count_toptrader_long_short_ratio,sum_toptrader_long_short_ratio,count_long_short_ratio,sum_taker_long_short_vol_ratio", stamp);
        metrics.Apply(0, "2024-01-01 03:55:00,BTCUSDT,1,500,1,1.5,2,0.9", stamp);
        metrics.Apply(0, "2024-01-01 03:05:00,BTCUSDT,1,400,1,1.4,2,0.8", stamp);
        metrics.Apply(0, "2024-01-01 04:00:00,BTCUSDT,1,900,1,1.9,2,1.1", stamp);

        metrics.OiValue[0][3].Should().Be(500f, "03:55 is the last print before the 04:00 close, regardless of file order");
        metrics.TakerRatio[0][3].Should().BeApproximately(0.9f, 1e-6f);
        metrics.OiValue[0][4].Should().Be(900f, "a print stamped exactly at 04:00 is first usable by the bar closing at 05:00");
        metrics.OiChange(0, 4, 1).Should().BeApproximately(900d / 500d - 1d, 1e-9);
    }

    [Fact]
    public void Skip_one_moves_the_forward_window_by_one_bar()
    {
        var panel = Synthetic(25, 24 * 80, seed: 6);
        var universe = new PanelUniverse(panel, 1_000d);
        var window = SimWindow.InSample(AlphaSplits.For(0, 80));
        var plain = InformationScreen.CrossSectional(universe, (c, t) => c, window, horizon: 24, warmup: 24 * 31);
        var skipped = InformationScreen.CrossSectional(universe, (c, t) => c, window, horizon: 24, warmup: 24 * 31, skip: 1);

        skipped.Periods.Should().BeGreaterThan(0);
        skipped.MeanIc.Should().NotBe(plain.MeanIc);
    }

    [Fact]
    public void Spearman_is_one_for_monotone_and_minus_one_for_reversed()
    {
        var up = Enumerable.Range(0, 10).Select(i => ((double)i, i * 2d + 1)).ToList();
        var down = Enumerable.Range(0, 10).Select(i => ((double)i, -i * 3d)).ToList();
        InformationScreen.Spearman(up).Should().BeApproximately(1d, 1e-12);
        InformationScreen.Spearman(down).Should().BeApproximately(-1d, 1e-12);
    }

    private static List<double> Probe(PanelFeatures f, int t)
    {
        var values = new List<double>();
        for (var c = 0; c < f.Universe.Panel.Coins; c++)
        {
            values.Add(f.Ret(c, t, 24));
            values.Add(f.Vol(c, t, 72));
            values.Add(f.VolAdjustedRet(c, t, 24, 168));
            values.Add(f.Residual(c, t, 24));
            values.Add(f.Beta(c, t));
            values.Add(f.FundingSum(c, t, 24));
            values.Add(f.FundingZ(c, t, 10));
            values.Add(f.TakerImbalance(c, t, 24));
            values.Add(f.VolumeRatio(c, t, 24, 168));
            values.Add(f.AverageRange(c, t, 24));
            values.Add(f.PriorHigh(c, t, 48));
            values.Add(f.PriorLow(c, t, 48));
            values.Add(f.Sma(c, t, 50));
        }

        var m = f.Market(t, 24);
        values.AddRange([m.Breadth, m.Dispersion, m.MeanReturn, m.MedianFunding24h]);
        return values.Select(v => double.IsNaN(v) ? -999d : v).ToList();
    }

    private static SimWindow Window(PanelUniverse universe, int from, int to)
    {
        var splits = new AlphaSplits(0, universe.Panel.Days, from / 24, (to + 23) / 24);
        var w = SimWindow.Validation(splits);
        return w;
    }

    private static HourlyPanel Flat(int coins, int hours, float price)
    {
        var symbols = Enumerable.Range(0, coins).Select(i => i == 0 ? "BTCUSDT" : $"C{i}USDT").ToArray();
        var panel = new HourlyPanel(Start, hours, symbols);
        for (var c = 0; c < coins; c++)
        {
            for (var t = 0; t < hours; t++)
            {
                panel.Close[c][t] = price;
                panel.High[c][t] = price * 1.001f;
                panel.Low[c][t] = price * 0.999f;
                panel.QuoteVolume[c][t] = 1_000_000f;
                panel.TakerBuyQuote[c][t] = 500_000f;
            }
        }

        return panel;
    }

    private static HourlyPanel Synthetic(int coins, int hours, int seed)
    {
        var random = new Random(seed);
        var symbols = Enumerable.Range(0, coins).Select(i => i == 0 ? "BTCUSDT" : $"C{i}USDT").ToArray();
        var panel = new HourlyPanel(Start, hours, symbols);
        for (var c = 0; c < coins; c++)
        {
            var price = 10f + c;
            for (var t = 0; t < hours; t++)
            {
                price *= 1f + (float)(random.NextDouble() - 0.5) * 0.02f;
                panel.Close[c][t] = price;
                panel.High[c][t] = price * (1f + (float)random.NextDouble() * 0.01f);
                panel.Low[c][t] = price * (1f - (float)random.NextDouble() * 0.01f);
                panel.QuoteVolume[c][t] = 100_000f + random.Next(0, 50_000);
                panel.TakerBuyQuote[c][t] = panel.QuoteVolume[c][t] * (float)(0.4 + random.NextDouble() * 0.2);
                if (t % 8 == 0)
                {
                    panel.Funding[c][t] = (float)((random.NextDouble() - 0.4) * 0.0004);
                }
            }
        }

        return panel;
    }

    private sealed class FixedModel(int hour, int coin, double weight) : ITargetModel
    {
        public bool IsDecision(int h) => h == hour;

        public void Targets(int h, double[] target) => target[coin] = weight;
    }
}
