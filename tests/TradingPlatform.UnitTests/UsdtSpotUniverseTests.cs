using System.Text.Json;
using FluentAssertions;
using TradingPlatform.Application.Abstractions.MarketData;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class UsdtSpotUniverseTests
{
    [Fact]
    public void Display_names_are_labels_not_the_trading_universe()
    {
        UsdtSpotUniverse.DisplayNameOf("BCHUSDT").Should().Be("Bitcoin Cash");
        UsdtSpotUniverse.DisplayNameOf("PEPEUSDT").Should().Be("PEPE");
        UsdtSpotUniverse.DisplayNameOf("BTCUSDT").Should().Be("Bitcoin");
    }
}

public sealed class UsdtPerpetualContractRulesTests
{
    [Fact]
    public void Accepts_trading_usdt_perpetual_with_filters()
    {
        var json = """
            {"symbol":"AAAUSDT","status":"TRADING","contractType":"PERPETUAL","baseAsset":"AAA","quoteAsset":"USDT","marginAsset":"USDT","filters":[{"filterType":"PRICE_FILTER","tickSize":"0.01"},{"filterType":"LOT_SIZE","stepSize":"0.001","minQty":"0.001"},{"filterType":"MIN_NOTIONAL","notional":"5"}]}
            """;
        UsdtPerpetualContractRules.TryMap(JsonDocument.Parse(json).RootElement, out var contract, out var reason)
            .Should().BeTrue();
        contract!.Symbol.Should().Be("AAAUSDT");
        reason.Should().BeEmpty();
    }

    [Fact]
    public void Quantity_precision_never_refines_a_coarser_lot_size()
    {
        var json = """
            {"symbol":"DOTUSDT","status":"TRADING","contractType":"PERPETUAL","baseAsset":"DOT","quoteAsset":"USDT","marginAsset":"USDT","quantityPrecision":1,"pricePrecision":4,"filters":[{"filterType":"PRICE_FILTER","tickSize":"0.001"},{"filterType":"LOT_SIZE","stepSize":"0.1","minQty":"0.1"},{"filterType":"MARKET_LOT_SIZE","stepSize":"0.01","minQty":"0.01"},{"filterType":"MIN_NOTIONAL","notional":"5"}]}
            """;
        UsdtPerpetualContractRules.TryMap(JsonDocument.Parse(json).RootElement, out var contract, out _)
            .Should().BeTrue();
        contract!.QuantityPrecision.Should().Be(1);
        contract.StepSize.Should().Be(0.1m);
        contract.MinQuantity.Should().Be(0.1m);
    }

    [Fact]
    public void Rejects_delivery_dated_and_inactive_contracts()
    {
        Reject("""{"symbol":"BTCUSDT_250627","status":"TRADING","contractType":"CURRENT_QUARTER","baseAsset":"BTC","quoteAsset":"USDT","filters":[{"filterType":"PRICE_FILTER","tickSize":"0.1"},{"filterType":"LOT_SIZE","stepSize":"0.001","minQty":"0.001"}]}""", "delivery_or_dated_contract");
        Reject("""{"symbol":"ETHUSDT","status":"SETTLING","contractType":"PERPETUAL","baseAsset":"ETH","quoteAsset":"USDT","filters":[{"filterType":"PRICE_FILTER","tickSize":"0.01"},{"filterType":"LOT_SIZE","stepSize":"0.001","minQty":"0.001"}]}""", "inactive_or_not_trading");
        Reject("""{"symbol":"BTCUSDT","status":"TRADING","contractType":"CURRENT_QUARTER","baseAsset":"BTC","quoteAsset":"USDT","filters":[{"filterType":"PRICE_FILTER","tickSize":"0.1"},{"filterType":"LOT_SIZE","stepSize":"0.001","minQty":"0.001"}]}""", "unsupported_contract_type");
        Reject("""{"symbol":"ETHUSDT","status":"TRADING","contractType":"PERPETUAL","baseAsset":"ETH","quoteAsset":"USDC","filters":[{"filterType":"PRICE_FILTER","tickSize":"0.01"},{"filterType":"LOT_SIZE","stepSize":"0.001","minQty":"0.001"}]}""", "quote_not_usdt");
        Reject("""{"symbol":"ZZZUSDT","status":"TRADING","contractType":"PERPETUAL","baseAsset":"ZZZ","quoteAsset":"USDT","filters":[]}""", "invalid_or_missing_market_metadata");
    }

    private static void Reject(string json, string reason)
    {
        UsdtPerpetualContractRules.TryMap(JsonDocument.Parse(json).RootElement, out var contract, out var actual)
            .Should().BeFalse();
        contract.Should().BeNull();
        actual.Should().Be(reason);
    }
}
