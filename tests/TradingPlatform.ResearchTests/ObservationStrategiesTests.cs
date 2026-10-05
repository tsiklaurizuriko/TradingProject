using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research.Alpha;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class ObservationStrategiesTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Live_features_match_the_research_panel()
    {
        var random = new Random(11);
        const int hours = 24 * 40;
        var panel = new HourlyPanel(Start, hours, ["BTCUSDT"]);
        var candles = new List<MarketCandle>(hours);
        var price = 100d;
        for (var t = 0; t < hours; t++)
        {
            price *= 1 + (random.NextDouble() - 0.5) * 0.02;
            var close = (float)price;
            var high = close * (1f + (float)random.NextDouble() * 0.01f);
            var low = close * (1f - (float)random.NextDouble() * 0.01f);
            var quote = 300_000f + random.Next(0, 100_000);
            panel.Close[0][t] = close;
            panel.High[0][t] = high;
            panel.Low[0][t] = low;
            panel.QuoteVolume[0][t] = quote;
            panel.TakerBuyQuote[0][t] = quote / 2;
            candles.Add(Bar(t, (decimal)close, (decimal)high, (decimal)low, (decimal)quote / (decimal)close));
        }

        var features = new PanelFeatures(new PanelUniverse(panel, 1_000d));
        foreach (var t in new[] { 760, 800, 901, hours - 1 })
        {
            ObservationStrategies.Vol(candles, t - 1, 720).Should().BeApproximately(features.Vol(0, t - 1, 720), 1e-6);
            ObservationStrategies.Vol(candles, t - 1, 24).Should().BeApproximately(features.Vol(0, t - 1, 24), 1e-6);
            ObservationStrategies.AverageRange(candles, t - 1, 168).Should().BeApproximately(features.AverageRange(0, t - 1, 168), 1e-6);
            ObservationStrategies.VolumeRatio(candles, t, 168).Should().BeApproximately(features.VolumeRatio(0, t, 1, 168), 1e-4);
            ((double)ObservationStrategies.PriorHigh(candles, t, 168)).Should().BeApproximately(features.PriorHigh(0, t, 168), 1e-3);
            ((double)ObservationStrategies.PriorLow(candles, t, 168)).Should().BeApproximately(features.PriorLow(0, t, 168), 1e-3);
        }
    }

    [Fact]
    public void Compression_breakout_buys_a_break_of_the_prior_week_high_after_quiet_hours()
    {
        var candles = Series(24 * 38, i => Quiet(i, 24 * 38 - 30));
        var last = candles.Count - 1;
        candles[last] = Bar(last, 104m, 104.2m, 100m, 10_000m);

        var quote = ObservationStrategies.CompressionBreakout(candles, last);

        quote.Signal.Should().Be(SignalType.Buy);
        quote.SuggestedStop.Should().BeLessThan(104m);
        quote.SuggestedTakeProfit.Should().BeGreaterThan(104m);
        (quote.SuggestedTakeProfit!.Value - 104m).Should().BeApproximately(3m * (104m - quote.SuggestedStop!.Value), 0.000001m);
    }

    [Fact]
    public void Compression_breakout_waits_for_a_four_hour_close()
    {
        var candles = Series(24 * 38 - 1, i => Quiet(i, 24 * 38 - 31));
        var last = candles.Count - 1;
        candles[last] = Bar(last, 104m, 104.2m, 100m, 10_000m);

        ObservationStrategies.CompressionBreakout(candles, last).Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Shock_fade_sells_a_huge_up_bar_on_a_volume_spike()
    {
        var candles = Series(24 * 38, i => i % 2 == 0 ? 1.002m : 1 / 1.002m);
        var last = candles.Count - 1;
        var prior = candles[last - 1].Close;
        candles[last] = Bar(last, prior * 1.06m, prior * 1.07m, prior * 0.995m, 60_000m);

        var quote = ObservationStrategies.ShockFade(candles, last);

        quote.Signal.Should().Be(SignalType.Sell);
        quote.SuggestedStop.Should().BeGreaterThan(candles[last].Close);
    }

    [Fact]
    public void Shock_fade_ignores_a_shock_without_volume()
    {
        var candles = Series(24 * 38, i => i % 2 == 0 ? 1.002m : 1 / 1.002m);
        var last = candles.Count - 1;
        var prior = candles[last - 1].Close;
        candles[last] = Bar(last, prior * 1.06m, prior * 1.07m, prior * 0.995m, 10_000m);

        ObservationStrategies.ShockFade(candles, last).Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Signals_at_a_bar_do_not_change_when_later_bars_arrive()
    {
        var random = new Random(5);
        var candles = Series(24 * 45, _ => 1m + (decimal)((random.NextDouble() - 0.5) * 0.03));
        for (var i = 760; i < 24 * 40; i++)
        {
            var prefix = candles.Take(i + 1).ToList();
            ObservationStrategies.CompressionBreakout(candles, i).Signal.Should().Be(ObservationStrategies.CompressionBreakout(prefix, i).Signal);
            ObservationStrategies.ShockFade(candles, i).Signal.Should().Be(ObservationStrategies.ShockFade(prefix, i).Signal);
        }
    }

    [Fact]
    public void Coins_below_five_million_daily_volume_are_outside_the_universe()
    {
        var candles = Series(24 * 38, i => i % 2 == 0 ? 1.01m : 1 / 1.01m, volume: 100m);

        ObservationStrategies.Universe(candles, candles.Count - 1).Should().Contain("below $5M");
        ObservationStrategies.ShockFade(candles, candles.Count - 1).Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Open_positions_exit_on_the_research_hold()
    {
        var bar = Bar(100, 100m, 101m, 99m, 1m);
        var opened = bar.CloseTime.AddHours(-71);

        ObservationStrategies.HoldOrExit(StrategyTemplateKeys.ObsCompressionBreakout, bar, opened).Signal.Should().Be(SignalType.Hold);
        ObservationStrategies.HoldOrExit(StrategyTemplateKeys.ObsCompressionBreakout, bar, bar.CloseTime.AddHours(-72)).Signal.Should().Be(SignalType.Exit);
        ObservationStrategies.HoldOrExit(StrategyTemplateKeys.ObsShockFade, bar, bar.CloseTime.AddHours(-24)).Signal.Should().Be(SignalType.Exit);
    }

    [Fact]
    public void Stop_distance_is_clamped()
    {
        ObservationStrategies.StopPercent(0.0001, 24).Should().Be(ObservationStrategies.MinStopPercent);
        ObservationStrategies.StopPercent(0.05, 72).Should().Be(ObservationStrategies.MaxStopPercent);
        ObservationStrategies.StopPercent(double.NaN, 72).Should().Be(ObservationStrategies.MaxStopPercent);
        ObservationStrategies.StopPercent(0.004, 72).Should().BeApproximately(2.5m * 0.004m * (decimal)Math.Sqrt(72) * 100m, 0.0001m);
    }

    [Fact]
    public void Top_trader_z_compares_the_print_with_thirty_daily_lags()
    {
        var at = Start.AddDays(40);
        var series = new List<(DateTimeOffset, decimal)>();
        for (var k = 1; k <= 30; k++)
        {
            series.Add((at.AddHours(-24 * k), k % 2 == 0 ? 1m : 3m));
            series.Add((at.AddHours(-24 * k + 1), 50m));
        }

        series.Add((at, 4m));

        var values = Enumerable.Range(1, 30).Select(k => k % 2 == 0 ? 1d : 3d).ToList();
        var mean = values.Average();
        var sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / 29);
        ObservationStrategies.TopTraderZ(series, at).Should().BeApproximately((4 - mean) / sd, 1e-9);
        double.IsNaN(ObservationStrategies.TopTraderZ(series.Take(40).Append((at, 4m)).ToList(), at)).Should().BeTrue();
    }

    [Fact]
    public void Ranking_longs_the_most_shorted_fifth_and_needs_twenty_coins()
    {
        var at = Start.AddDays(40);
        var z = Enumerable.Range(0, 25).ToDictionary(i => $"C{i:00}USDT", i => (double)i);

        var ranking = ObservationStrategies.RankTopTrader(at, at.AddHours(-1), z);

        ranking.Coins.Should().Be(25);
        ranking.Longs.Should().BeEquivalentTo(["C00USDT", "C01USDT", "C02USDT", "C03USDT", "C04USDT"]);
        ranking.Shorts.Should().BeEquivalentTo(["C20USDT", "C21USDT", "C22USDT", "C23USDT", "C24USDT"]);

        var thin = ObservationStrategies.RankTopTrader(at, at.AddHours(-1), z.Take(19).ToDictionary());
        thin.Longs.Should().BeEmpty();
        thin.Shorts.Should().BeEmpty();
    }

    [Fact]
    public void Top_trader_decision_acts_only_on_the_matching_midnight_ranking()
    {
        var candles = Series(24 * 38, i => i % 2 == 0 ? 1.004m : 1 / 1.004m);
        var close = candles[^1].CloseTime.AddMilliseconds(1);
        close.UtcDateTime.Hour.Should().Be(0);
        var longs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BTCUSDT" };
        var ranking = new TopTraderRanking(close, close.AddHours(-1), 30, longs, new HashSet<string>());

        ObservationStrategies.TopTraderDecision("BTCUSDT", candles, false, null, ranking).Signal.Should().Be(SignalType.Buy);
        ObservationStrategies.TopTraderDecision("ETHUSDT", candles, false, null, ranking).Signal.Should().Be(SignalType.NoAction);
        ObservationStrategies.TopTraderDecision("BTCUSDT", candles, false, null, ranking with { At = close.AddDays(-1) })
            .Reason.Should().Contain("not ready");
        ObservationStrategies.TopTraderDecision("BTCUSDT", candles.Take(candles.Count - 1).ToList(), false, null, ranking)
            .Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Worker_universe_uses_the_median_of_the_thirty_closed_days()
    {
        var now = Start.AddDays(40).AddHours(3);
        var days = Enumerable.Range(0, 32)
            .Select(d => new MarketCandle
            {
                OpenTime = Start.AddDays(8 + d),
                CloseTime = Start.AddDays(9 + d).AddMilliseconds(-1),
                Open = 1m,
                High = 1m,
                Low = 1m,
                Close = 1m,
                Volume = d == 31 ? 1_000_000_000m : 1_000_000m * (d + 1)
            })
            .ToList();

        TopTraderRankWorker.MedianDailyQuote(days, now).Should().Be(17_500_000m);
        TopTraderRankWorker.MedianDailyQuote(days.Take(10).ToList(), now).Should().Be(0m);
    }

    private static decimal Quiet(int i, int calmFrom) =>
        i < calmFrom ? (i % 2 == 0 ? 1.01m : 1 / 1.01m) : (i % 2 == 0 ? 1.0003m : 1 / 1.0003m);

    private static List<MarketCandle> Series(int count, Func<int, decimal> step, decimal volume = 10_000m)
    {
        var candles = new List<MarketCandle>(count);
        var price = 100m;
        for (var i = 0; i < count; i++)
        {
            var prev = price;
            price *= step(i);
            candles.Add(Bar(i, price, Math.Max(prev, price) * 1.001m, Math.Min(prev, price) * 0.999m, volume));
        }

        return candles;
    }

    private static MarketCandle Bar(int hour, decimal close, decimal high, decimal low, decimal volume) => new()
    {
        Timeframe = Timeframe.OneHour,
        OpenTime = Start.AddHours(hour),
        CloseTime = Start.AddHours(hour + 1).AddMilliseconds(-1),
        Open = close,
        High = high,
        Low = low,
        Close = close,
        Volume = volume
    };
}
