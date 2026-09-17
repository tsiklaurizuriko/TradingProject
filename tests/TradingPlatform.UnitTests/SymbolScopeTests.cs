using TradingPlatform.Application.Trading;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class SymbolScopeTests
{
    [Fact]
    public void All_coins_allow_any_symbol()
    {
        SymbolScope.Allows(true, "BTCUSDT", "ETHUSDT").Should().BeTrue();
        SymbolScope.Allows(true, null, "PEPEUSDT").Should().BeTrue();
    }

    [Fact]
    public void Assigned_list_only_allows_those_coins()
    {
        SymbolScope.Allows(false, "BTCUSDT, ethusdt", "ETHUSDT").Should().BeTrue();
        SymbolScope.Allows(false, "BTCUSDT, ETHUSDT", "SOLUSDT").Should().BeFalse();
        SymbolScope.Join([" btcusdt ", "ETHUSDT", "BTCUSDT"]).Should().Be("BTCUSDT,ETHUSDT");
    }
}
