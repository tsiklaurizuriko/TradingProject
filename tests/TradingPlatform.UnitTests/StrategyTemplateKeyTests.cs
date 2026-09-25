using FluentAssertions;
using TradingPlatform.Strategies.Engine;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class StrategyTemplateKeyTests
{
    [Fact]
    public void Catalog_keys_fit_the_strategy_template_column()
    {
        StrategyTemplateKeys.All.Should().OnlyContain(key => key.Length <= 64);
        StrategyTemplateKeys.FundingExtremeMomentumExhaustion.Length.Should().BeGreaterThan(32);
        StrategyTemplateKeys.IsOperatorCatalog(StrategyTemplateKeys.ScalpEmaMomentum).Should().BeFalse();
        StrategyTemplateKeys.IsOperatorCatalog(StrategyTemplateKeys.EmaRsiTrend).Should().BeTrue();
        StrategyTemplateKeys.OperatorCatalog.Should().NotContain(key => key.StartsWith("scalp_", StringComparison.Ordinal) || key.StartsWith("pa_", StringComparison.Ordinal));
    }
}
