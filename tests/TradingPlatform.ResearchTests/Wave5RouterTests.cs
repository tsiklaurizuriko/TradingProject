using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class Wave5RouterTests
{
    [Fact]
    public void Simulate_hits_stop_before_take_on_the_same_bar()
    {
        var t0 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = new List<MarketCandle>
        {
            Bar(t0, 0, 100m, 100m, 100m, 100m),
            Bar(t0, 1, 100m, 110m, 90m, 105m)
        };
        var hit = Wave5Harvest.Simulate(candles, 0, isLong: true);
        hit.Should().NotBeNull();
        hit!.Value.Gross.Should().BeNegative();
    }

    [Fact]
    public void Score_does_not_use_trades_that_have_not_exited()
    {
        var t0 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var early = Ev("S", "BTCUSDT", t0, t0.AddHours(1), t0.AddHours(2), 0.04);
        var open = Ev("S", "ETHUSDT", t0.AddHours(3), t0.AddHours(4), t0.AddDays(10), 0.50);
        var later = Ev("S", "BTCUSDT", t0.AddHours(5), t0.AddHours(6), t0.AddHours(7), -0.02);
        var result = Wave5Router.Evaluate(Wave5Catalog.RouterUniverse().Take(1).ToList(), [early, open, later]);
        result.Events.Should().HaveCount(3);
        result.Classification.Should().NotBe("ROBUST CANDIDATE");
    }

    [Fact]
    public void Random_and_top_use_the_same_count_rule_per_timestamp()
    {
        var t0 = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var events = new List<Wave5Event>();
        foreach (var hour in Enumerable.Range(0, 40))
        {
            events.Add(Ev("A", "BTCUSDT", t0.AddHours(hour), t0.AddHours(hour + 1), t0.AddHours(hour + 2), 0.01, "1h"));
            events.Add(Ev("B", "ETHUSDT", t0.AddHours(hour), t0.AddHours(hour + 1), t0.AddHours(hour + 2), -0.01, "1h"));
        }

        var result = Wave5Router.Evaluate(Wave5Catalog.RouterUniverse().Take(1).ToList(), events);
        var top = result.Slices.First(s => s.Label == "OOS-TOP1-1h");
        var rand = result.Slices.First(s => s.Label == "OOS-RAND1-1h");
        top.Trades.Should().Be(rand.Trades);
    }

    private static Wave5Event Ev(
        string strategy,
        string symbol,
        DateTimeOffset signal,
        DateTimeOffset fill,
        DateTimeOffset exit,
        double gross,
        string tf = "1h") =>
        new()
        {
            Strategy = strategy,
            Family = "TEST",
            Symbol = symbol,
            Timeframe = tf,
            SignalTime = signal,
            FillTime = fill,
            ExitTime = exit,
            Direction = 1,
            Entry = 100m,
            Exit = 100m * (1m + (decimal)gross),
            GrossRet = gross,
            AtrPct = 0.01,
            VolRegime = 0,
            TrendRegime = 1,
            BtcRegime = 1,
            Breadth = 0.5,
            RelStrength = 0,
            RelVolume = 1,
            VwapDist = 0,
            RecentRet = 0,
            HoldBars = 1,
            Win = gross > 0
        };

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
