using FluentAssertions;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class LiveProtectivePricesTests
{
    [Fact]
    public void FromEntry_rounds_sl_down_and_tp_up_to_tick()
    {
        var (stop, take) = LiveProtectivePrices.FromEntry(100m, 1.5m, 3m, 0.01m);

        stop.Should().Be(98.50m);
        take.Should().Be(103.00m);
    }

    [Fact]
    public void FromEntry_short_places_stop_above_and_take_below()
    {
        var (stop, take) = LiveProtectivePrices.FromEntry(100m, 1.5m, 3m, 0.01m, TradingPlatform.Domain.Positions.PositionSide.Short);

        stop.Should().Be(101.50m);
        take.Should().Be(97.00m);
    }

    [Fact]
    public void FromEntry_is_entry_percent_not_margin_or_notional()
    {
        var (stop, take) = LiveProtectivePrices.FromEntry(0.04821m, 1.5m, 3m, 0.00001m);

        stop.Should().Be(0.04748m);
        take.Should().Be(0.04966m);
        stop.Should().BeLessThan(0.04821m);
        take.Should().BeGreaterThan(0.04821m);
        stop.Should().NotBe(5m);
        take.Should().NotBe(50m);
        LiveProtectivePrices.IsValidTrigger(0.04821m, stop, take, TradingPlatform.Domain.Positions.PositionSide.Long)
            .Should().BeTrue();
    }

    [Fact]
    public void FromEntry_short_tiny_alt_stays_around_entry_price()
    {
        var (stop, take) = LiveProtectivePrices.FromEntry(
            0.1847m,
            1.5m,
            3m,
            0.0001m,
            TradingPlatform.Domain.Positions.PositionSide.Short);

        stop.Should().Be(0.1875m);
        take.Should().Be(0.1791m);
    }

    [Fact]
    public void Client_order_ids_fit_binance_limit_and_are_stable()
    {
        var botId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var sl = LiveProtectivePrices.StopClientOrderId(botId);
        var tp = LiveProtectivePrices.TakeClientOrderId(botId);

        sl.Should().StartWith("sl");
        tp.Should().StartWith("tp");
        sl.Length.Should().Be(18);
        tp.Length.Should().Be(18);
        sl.Should().NotBe(tp);
        LiveProtectivePrices.StopClientOrderId(botId).Should().Be(sl);
    }
}
