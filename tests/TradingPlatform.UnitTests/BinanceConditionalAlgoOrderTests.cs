using FluentAssertions;
using TradingPlatform.Binance;
using TradingPlatform.Domain.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class BinanceConditionalAlgoOrderTests
{
    [Fact]
    public void Place_fields_use_algo_endpoint_trigger_price_and_close_position()
    {
        var fields = BinanceConditionalAlgoOrder.PlaceFields(
            "ponsusdt",
            OrderSide.Sell,
            "STOP_MARKET",
            0.04748m,
            "slaaaaaaaaaaaaaaa",
            priceProtect: true);

        fields["algoType"].Should().Be("CONDITIONAL");
        fields["symbol"].Should().Be("PONSUSDT");
        fields["side"].Should().Be("SELL");
        fields["type"].Should().Be("STOP_MARKET");
        fields["triggerPrice"].Should().Be("0.04748");
        fields["closePosition"].Should().Be("true");
        fields["workingType"].Should().Be("MARK_PRICE");
        fields["priceProtect"].Should().Be("TRUE");
        fields["clientAlgoId"].Should().Be("slaaaaaaaaaaaaaaa");
        fields.Should().NotContainKey("quantity");
        fields.Should().NotContainKey("reduceOnly");
        fields.Should().NotContainKey("stopPrice");
        fields.Should().NotContainKey("newClientOrderId");
        BinanceConditionalAlgoOrder.PlacePath.Should().Be("fapi/v1/algoOrder");
        BinanceConditionalAlgoOrder.OpenPath.Should().Be("fapi/v1/openAlgoOrders");
        BinanceConditionalAlgoOrder.HistoryPath.Should().Be("fapi/v1/allAlgoOrders");
    }

    [Fact]
    public void FormatDecimal_keeps_alt_tick_precision()
    {
        BinanceHmac.FormatDecimal(0.04748m).Should().Be("0.04748");
        BinanceHmac.FormatDecimal(101.50m).Should().Be("101.5");
    }
}
