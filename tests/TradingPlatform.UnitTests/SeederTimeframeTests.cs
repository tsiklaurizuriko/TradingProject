using FluentAssertions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.Strategies.Engine;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class SeederTimeframeTests
{
    public static TheoryData<string> CatalogKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in StrategyTemplateKeys.OperatorCatalog)
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogKeys))]
    public void Every_catalog_row_is_seeded_on_a_timeframe_the_template_accepts(string key)
    {
        var seeded = DatabaseSeeder.SeedTimeframe(key);

        TimeframeExtensions.TryParseInterval(seeded, out _).Should().BeTrue();
        StrategyTemplateKeys.TimeframesFor(key).Should().Contain(seeded);
    }

    [Theory]
    [InlineData(StrategyTemplateKeys.FlowZone)]
    [InlineData(StrategyTemplateKeys.BinHv45)]
    [InlineData(StrategyTemplateKeys.ZigZagFade)]
    [InlineData(StrategyTemplateKeys.ClucMay72018)]
    [InlineData(StrategyTemplateKeys.DonchianBreakout)]
    public void Canonical_rows_seed_on_the_canonical_timeframe_not_the_old_override(string key)
    {
        var frames = StrategyTemplateKeys.TimeframesFor(key);

        frames.Should().ContainSingle();
        DatabaseSeeder.SeedTimeframe(key).Should().Be(frames[0]);
    }
}
