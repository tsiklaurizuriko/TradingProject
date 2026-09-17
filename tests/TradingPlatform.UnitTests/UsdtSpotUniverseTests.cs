using TradingPlatform.Application.Abstractions.MarketData;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class UsdtSpotUniverseTests
{
    [Fact]
    public void Market_cap_universe_is_the_fixed_top_15_in_rank_order()
    {
        UsdtSpotUniverse.ByMarketCap.Select(c => c.Symbol).Should().Equal(
            "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT",
            "DOGEUSDT", "SUIUSDT", "ADAUSDT", "LINKUSDT", "AVAXUSDT",
            "TRXUSDT", "TONUSDT", "DOTUSDT", "LTCUSDT", "BCHUSDT");
        UsdtSpotUniverse.DisplayNameOf("BCHUSDT").Should().Be("Bitcoin Cash");
        UsdtSpotUniverse.RankOf("BTCUSDT").Should().Be(1);
        UsdtSpotUniverse.DisplayNameOf("PEPEUSDT").Should().Be("PEPE");
        UsdtSpotUniverse.RankOf("PEPEUSDT").Should().Be(int.MaxValue);
    }
}
