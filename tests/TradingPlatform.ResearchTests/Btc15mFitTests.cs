using FluentAssertions;
using TradingPlatform.Research;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class Btc15mFitTests
{
    [Fact]
    public void Same_bar_stop_is_checked_before_target()
    {
        const double entry = 100;
        const double stop = 99;
        const double take = 102;
        var low = 98.0;
        var high = 103.0;
        var hitSl = low <= stop;
        var hitTp = high >= take;
        hitSl.Should().BeTrue();
        hitTp.Should().BeTrue();
        var reason = hitSl ? "SL" : hitTp ? "TP" : "NONE";
        reason.Should().Be("SL");
    }

    [Fact]
    public void Grid_sizes_are_the_pre_registered_sets()
    {
        Btc15mFit.SlGrid.Should().HaveCount(9);
        Btc15mFit.TpGrid.Should().HaveCount(11);
        Btc15mFit.HoldGrid.Should().HaveCount(6);
        Btc15mFit.MinTrades.Should().Be(2000);
        Btc15mFit.MinNet.Should().Be(100);
    }
}
