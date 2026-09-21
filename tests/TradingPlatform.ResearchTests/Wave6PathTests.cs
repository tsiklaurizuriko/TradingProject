using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class Wave6PathTests
{
    [Fact]
    public void Same_bar_hits_both_sides_counts_as_adverse_first()
    {
        var t0 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = new List<MarketCandle>
        {
            Bar(t0, 0, 100m, 100m, 100m, 100m),
            Bar(t0, 1, 100m, 104m, 96m, 100m)
        };
        var s = new Wave6Scratch();
        Wave6Path.Trace(candles, 1, isLong: true, atrPct: 1, timeframe: "1h", s).Should().BeTrue();
        Wave6Path.FirstTouch(s.FavBar[Wave6Path.L1], s.AdvBar[Wave6Path.L1], 1).Should().Be(-1);
        s.MfeR[0].Should().BeGreaterThan(1);
        s.MaeR[0].Should().BeGreaterThan(1);
    }

    [Fact]
    public void Mfe_is_high_excursion_and_mae_is_low_excursion_for_a_long()
    {
        var t0 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 50).Select(i =>
            i == 2
                ? Bar(t0, i, 100m, 103m, 99m, 101m)
                : Bar(t0, i, 100m, 100.2m, 99.8m, 100m)).ToList();
        var s = new Wave6Scratch();
        Wave6Path.Trace(candles, 1, isLong: true, 1, "1h", s);
        s.MfeR[Wave6Path.H24].Should().BeApproximately(1.5, 0.15);
        s.MaeR[Wave6Path.H24].Should().BeApproximately(0.5, 0.15);
        s.FavBar[Wave6Path.L1].Should().Be(2);
    }

    [Fact]
    public void Trace_does_not_read_past_the_requested_horizon()
    {
        var t0 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 10).Select(i =>
            Bar(t0, i, 100m + i, 101m + i, 99m + i, 100m + i)).ToList();
        var s = new Wave6Scratch();
        Wave6Path.Trace(candles, 1, true, 1, "1h", s).Should().BeTrue();
        s.Complete.Should().BeFalse();
        s.Ok[Wave6Path.H24].Should().BeFalse();
    }

    [Fact]
    public void Grid_same_bar_stop_beats_target()
    {
        var t0 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 50)
            .Select(i => i == 1 ? Bar(t0, i, 100m, 103m, 97m, 100m) : Bar(t0, i, 100m, 100.1m, 99.9m, 100m))
            .ToList();
        var s = new Wave6Scratch();
        Wave6Path.Trace(candles, 1, true, 1, "1h", s);
        var g = Wave6Path.GridGross(s, 1.0, 1.0, 24, s.CloseR[Wave6Path.H24]);
        g.Should().BeNegative();
    }

    private static MarketCandle Bar(DateTimeOffset t0, int h, decimal o, decimal high, decimal low, decimal c) => new()
    {
        Open = o,
        High = high,
        Low = low,
        Close = c,
        Volume = 10,
        IsClosed = true,
        OpenTime = t0.AddHours(h),
        CloseTime = t0.AddHours(h + 1).AddMilliseconds(-1)
    };
}
