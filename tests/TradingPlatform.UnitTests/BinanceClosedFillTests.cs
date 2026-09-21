using FluentAssertions;
using TradingPlatform.Binance;
using TradingPlatform.Domain.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class BinanceClosedFillTests
{
    [Fact]
    public void Long_close_recovers_entry_from_realized_pnl()
    {
        var entry = BinanceClosedFill.EntryPrice(OrderSide.Sell, 0.9m, 10m, -1m);
        entry.Should().Be(1m);
        BinanceClosedFill.PnLPercent(entry, 10m, -1m).Should().Be(-10m);
    }

    [Fact]
    public void Short_close_recovers_entry_from_realized_pnl()
    {
        var entry = BinanceClosedFill.EntryPrice(OrderSide.Buy, 1.1m, 10m, -1m);
        entry.Should().Be(1m);
        BinanceClosedFill.IsClosing(-0.02m).Should().BeTrue();
        BinanceClosedFill.IsClosing(0m).Should().BeFalse();
    }

    [Fact]
    public void Round_trip_keeps_one_isolated_close_for_two_exit_fills()
    {
        var opened = DateTimeOffset.Parse("2026-09-21T00:33:53Z");
        var fills = new BinanceClosedFill.Fill[]
        {
            new(OrderSide.Buy, 0.1063m, 90.4m, 0.01m, opened, "1", 0m),
            new(OrderSide.Sell, 0.1105m, 40m, 0.002m, opened.AddHours(12), "2", 0.168m),
            new(OrderSide.Sell, 0.1105m, 50.4m, 0.003m, opened.AddHours(12).AddSeconds(1), "3", 0.192m),
        };

        var trips = BinanceClosedFill.RoundTrips("1000000MOGUSDT", fills);
        trips.Should().HaveCount(1);
        trips[0].Quantity.Should().Be(90.4m);
        trips[0].EntryPrice.Should().Be(0.1063m);
        trips[0].ExitPrice.Should().Be(0.1105m);
        trips[0].RealizedPnl.Should().Be(0.36m);
        trips[0].OpenedAt.Should().Be(opened);
        trips[0].EntrySide.Should().Be(OrderSide.Buy);
    }

    [Fact]
    public void Round_trip_keeps_isolated_short_as_one_close()
    {
        var opened = DateTimeOffset.Parse("2026-09-21T12:13:51Z");
        var fills = new BinanceClosedFill.Fill[]
        {
            new(OrderSide.Sell, 0.04407m, 215m, 0.004m, opened, "1", 0m),
            new(OrderSide.Buy, 0.04434m, 215m, 0.004m, opened.AddMinutes(17), "2", -0.05805m),
        };

        var trips = BinanceClosedFill.RoundTrips("RECALLUSDT", fills);
        trips.Should().HaveCount(1);
        trips[0].EntrySide.Should().Be(OrderSide.Sell);
        trips[0].Quantity.Should().Be(215m);
        trips[0].RealizedPnl.Should().Be(-0.05805m);
    }
}
