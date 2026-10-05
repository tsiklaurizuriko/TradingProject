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

    [Fact]
    public void A_missing_client_id_is_not_cancelled_before_a_new_protective_order()
    {
        var orders = new[]
        {
            new TradingPlatform.Application.Abstractions.Exchange.LiveOpenOrder(
                "BTCUSDT", "Sell", "STOP_MARKET", "NEW", 0m, 0m, 100m, "1", "sl-other", DateTimeOffset.UtcNow, "Futures")
        };

        LiveProtectivePrices.ListedByClientId(orders, "slaaaaaaaaaaaaaa").Should().BeFalse();
        LiveProtectivePrices.ListedByClientId(orders, "sl-other").Should().BeTrue();
    }

    [Fact]
    public void Stop_and_take_types_do_not_count_as_each_other()
    {
        LiveProtectivePrices.IsStopOrder("STOP_MARKET").Should().BeTrue();
        LiveProtectivePrices.IsTakeOrder("TAKE_PROFIT_MARKET").Should().BeTrue();
        LiveProtectivePrices.IsStopOrder("TAKE_PROFIT_MARKET").Should().BeFalse();
        LiveProtectivePrices.IsTakeOrder("STOP_MARKET").Should().BeFalse();
    }

    [Fact]
    public void Existing_close_position_order_is_not_treated_as_a_new_failure()
    {
        LiveProtectivePrices.IsExistingProtectiveOrder(
            "Binance -4130: An open stop or take profit order with closePosition is existing.")
            .Should().BeTrue();
        LiveProtectivePrices.IsExistingProtectiveOrder("Order would immediately trigger. -2021")
            .Should().BeFalse();
    }

    [Fact]
    public void Resting_trigger_stays_on_the_side_binance_can_accept()
    {
        LiveProtectivePrices.RestingTrigger(100m, 0.1m, closingShort: false, stop: true).Should().Be(99.9m);
        LiveProtectivePrices.RestingTrigger(100m, 0.1m, closingShort: false, stop: false).Should().Be(100.1m);
        LiveProtectivePrices.RestingTrigger(100m, 0.1m, closingShort: true, stop: true).Should().Be(100.1m);
        LiveProtectivePrices.RestingTrigger(100m, 0.1m, closingShort: true, stop: false).Should().Be(99.9m);
    }
}
