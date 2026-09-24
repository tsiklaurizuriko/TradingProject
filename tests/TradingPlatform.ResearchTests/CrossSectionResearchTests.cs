using FluentAssertions;
using TradingPlatform.Research;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class CrossSectionResearchTests
{
    [Fact]
    public void Decile_of_thirty_names_uses_three_names_in_each_tail()
    {
        var (bottom, top) = CrossSectionMath.TailCount(30);
        bottom.Should().Be(3);
        top.Should().Be(3);
        CrossSectionMath.QuintileCount(30).Bottom.Should().Be(6);
    }

    [Fact]
    public void Stablecoin_and_index_names_are_excluded_before_ranking()
    {
        CrossSectionMath.AcceptedName("BTCUSDT").Should().BeTrue();
        CrossSectionMath.AcceptedName("SOLUSDT").Should().BeTrue();
        CrossSectionMath.AcceptedName("BTCDOMUSDT").Should().BeFalse();
        CrossSectionMath.AcceptedName("USDCUSDT").Should().BeFalse();
        CrossSectionMath.AcceptedName("ETHBTC").Should().BeFalse();
    }

    [Fact]
    public void Repeatable_requires_three_horizons_regimes_and_broad_participation()
    {
        var positive = new[] { 1, 1, 1, 1, 0 };
        var blocks = new[] { 3, 4, 3, 3, 0 };
        CrossSectionMath.Classify(positive, positive, positive, blocks, positive, positive, 40, 0.10).Should().Be("REPEATABLE");
        CrossSectionMath.Classify(positive, positive, positive, blocks, positive, positive, 4, 0.80).Should().Be("UNIVERSE_SPECIFIC");
        var oos = new[] { 1, 1, 1, 1, 1 };
        var flat = new[] { 0, 0, 0, 0, 0 };
        CrossSectionMath.Classify(flat, flat, oos, blocks, flat, flat, 40, 0.10).Should().Be("OOS_ONLY");
        CrossSectionMath.Classify(flat, flat, flat, flat, flat, flat, 40, 0.10).Should().Be("NO_EVIDENCE");
    }

    [Fact]
    public void A_later_close_does_not_change_the_return_already_known()
    {
        var close = new float[8];
        for (var i = 0; i < close.Length; i++)
        {
            close[i] = 100f + i;
        }

        var destination = new float[8];
        CrossSectionMath.FillReturn(close, 1, 0, close.Length, 1, destination);
        var known = destination[3];
        close[4] = 999f;
        CrossSectionMath.FillReturn(close, 1, 0, close.Length, 1, destination);
        destination[3].Should().Be(known);
        destination[4].Should().NotBe(known);
    }

    [Fact]
    public void One_basis_point_is_the_pre_registered_noise_floor()
    {
        CrossSectionMath.PassSign(300, 0.0002, 0.0001, 200).Should().Be(1);
        CrossSectionMath.PassSign(300, 0.00005, 0.00005, 200).Should().Be(0);
        CrossSectionMath.PassSign(300, 0.001, -0.001, 200).Should().Be(0);
        CrossSectionMath.PassSign(50, 0.01, 0.01, 200).Should().Be(0);
    }
}
