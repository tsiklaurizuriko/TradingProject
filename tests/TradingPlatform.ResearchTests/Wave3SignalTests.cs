using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class Wave3SignalTests
{
    [Fact]
    public void Panel_inner_join_drops_hours_missing_any_coin()
    {
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var btc = Enumerable.Range(0, 10).Select(i => Bar("BTCUSDT", t0, i, 100m + i)).ToList();
        var eth = Enumerable.Range(0, 10).Where(i => i != 4).Select(i => Bar("ETHUSDT", t0, i, 50m + i)).ToList();
        var panel = Wave3Panel.Align(
            new Dictionary<string, IReadOnlyList<MarketCandle>> { ["BTCUSDT"] = btc, ["ETHUSDT"] = eth },
            ["BTCUSDT", "ETHUSDT"]);
        panel.Length.Should().Be(9);
        panel.OpenTimes.Should().NotContain(t0.AddHours(4));
    }

    [Fact]
    public void Lookback_and_rank_at_t_do_not_change_when_future_bars_are_appended()
    {
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<MarketCandle> Prefix(int n, decimal start) =>
            Enumerable.Range(0, n).Select(i => Bar("BTCUSDT", t0, i, start + i)).ToList();
        var shortPanel = Wave3Panel.Align(
            new Dictionary<string, IReadOnlyList<MarketCandle>>
            {
                ["BTCUSDT"] = Prefix(60, 100m),
                ["ETHUSDT"] = Prefix(60, 50m)
            },
            ["BTCUSDT", "ETHUSDT"]);
        var longPanel = Wave3Panel.Align(
            new Dictionary<string, IReadOnlyList<MarketCandle>>
            {
                ["BTCUSDT"] = Prefix(80, 100m),
                ["ETHUSDT"] = Prefix(80, 50m)
            },
            ["BTCUSDT", "ETHUSDT"]);
        var t = 40;
        Wave3Math.LookbackReturn(shortPanel, t, 1, 24).Should().Be(Wave3Math.LookbackReturn(longPanel, t, 1, 24));
        var a = new[] { Wave3Math.LookbackReturn(shortPanel, t, 0, 24), Wave3Math.LookbackReturn(shortPanel, t, 1, 24) };
        var b = new[] { Wave3Math.LookbackReturn(longPanel, t, 0, 24), Wave3Math.LookbackReturn(longPanel, t, 1, 24) };
        Wave3Math.PercentileRanks(a).Should().Equal(Wave3Math.PercentileRanks(b));
    }

    [Fact]
    public void Beta_window_ending_at_t_minus_one_ignores_return_at_t()
    {
        var y = Enumerable.Range(0, 200).Select(i => 0.01 + i * 0.0001).ToArray();
        var x = Enumerable.Range(0, 200).Select(i => 0.005 + i * 0.00005).ToArray();
        y[199] = 9.0;
        x[199] = 9.0;
        var past = Wave3Math.Beta(y, x, 199 - 168, 198);
        var withNow = Wave3Math.Beta(y, x, 199 - 168, 199);
        past.Should().NotBe(withNow);
    }

    [Fact]
    public void Spearman_is_one_on_a_strictly_increasing_pair()
    {
        var x = Enumerable.Range(1, 20).Select(i => (double)i).ToArray();
        var y = Enumerable.Range(1, 20).Select(i => (double)i * 2).ToArray();
        Wave3Math.Spearman(x, y).Should().BeApproximately(1.0, 1e-9);
    }

    private static MarketCandle Bar(string _, DateTimeOffset t0, int hour, decimal close) => new()
    {
        Open = close - 0.5m,
        High = close + 1m,
        Low = close - 1m,
        Close = close,
        Volume = 100,
        TakerBuyVolume = 40,
        IsClosed = true,
        OpenTime = t0.AddHours(hour),
        CloseTime = t0.AddHours(hour + 1).AddMilliseconds(-1),
        ExchangeTimestamp = t0.AddHours(hour + 1).AddMilliseconds(-1)
    };
}
