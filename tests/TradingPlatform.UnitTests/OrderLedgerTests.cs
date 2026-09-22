using FluentAssertions;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class OrderLedgerTests
{
    [Fact]
    public void Kind_separates_isolated_fills_from_waiting_stops()
    {
        OrderLedger.Kind("Market").Should().Be(OrderLedger.KindFill);
        OrderLedger.Kind("STOP_MARKET").Should().Be(OrderLedger.KindStop);
        OrderLedger.Kind("TakeProfitMarket").Should().Be(OrderLedger.KindTake);
        OrderLedger.IsProtection("TAKE_PROFIT_MARKET").Should().BeTrue();
        OrderLedger.IsProtection("Market").Should().BeFalse();
    }

    [Fact]
    public void Parse_maps_binance_algo_working_status_to_submitted()
    {
        OrderLedger.ParseType("STOP_MARKET").Should().Be(OrderType.StopMarket);
        OrderLedger.ParseStatus("WORKING").Should().Be(OrderStatus.Submitted);
        OrderLedger.ParseStatus("FILLED").Should().Be(OrderStatus.Filled);
        OrderLedger.ParseStatus("TRIGGERED").Should().Be(OrderStatus.Filled);
        OrderLedger.ParseStatus("FINISHED").Should().Be(OrderStatus.Filled);
        OrderLedger.ClientKey(null, "970621956").Should().Be("BX970621956");
        OrderLedger.Same("slabc", null, "slabc", "99").Should().BeTrue();
        OrderLedger.Same("entry", "11", "other", "11").Should().BeTrue();
    }
}
