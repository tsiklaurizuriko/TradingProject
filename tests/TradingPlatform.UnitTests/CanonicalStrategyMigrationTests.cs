using FluentAssertions;
using TradingPlatform.Strategies.Engine;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class CanonicalStrategyMigrationTests
{
    [Fact]
    public void Alias_rewrites_to_the_canonical_id_and_keeps_parameter_numbers()
    {
        var id = Guid.NewGuid();
        var json = """{"name":"Impulse","version":1,"template":"impulse_catch_v2","timeframe":"5m","params":{"entryLookback":16,"maxImpulseAgeBars":32,"legacyBias":1}}""";
        var plan = CanonicalStrategyMigration.Plan(
        [
            new CanonicalStrategySnapshot(id, "impulse_catch_v2", true, false, [json])
        ]);

        plan.Failures.Should().BeEmpty();
        var update = plan.Updates.Should().ContainSingle().Subject;
        update.Id.Should().Be(id);
        update.TemplateKey.Should().Be(StrategyTemplateKeys.ImpulseCatch);
        update.IsEnabled.Should().BeTrue();
        update.DefinitionJson.Should().ContainSingle().Which.Should().Contain("\"entryLookback\":16");
        update.DefinitionJson[0].Should().Contain("\"template\":\"impulse_catch\"");
        update.DefinitionJson[0].Should().Contain("\"legacyBias\":1");
        plan.Reviews.Should().Contain(review => review.Contains("timeframe '5m'", StringComparison.Ordinal) && review.Contains(id.ToString(), StringComparison.Ordinal));
        plan.Reviews.Should().Contain(review => review.Contains("legacyBias", StringComparison.Ordinal));
    }

    [Fact]
    public void Second_plan_does_not_change_an_already_canonical_row()
    {
        var id = Guid.NewGuid();
        var first = CanonicalStrategyMigration.Plan(
        [
            new CanonicalStrategySnapshot(
                id,
                "cluc_may72018_v2",
                true,
                false,
                ["""{"template":"cluc_may72018_v2","timeframe":"15m","params":{"rsiOversold":35}}"""])
        ]);
        var applied = first.Updates.Single();
        var second = CanonicalStrategyMigration.Plan(
        [
            new CanonicalStrategySnapshot(applied.Id, applied.TemplateKey, applied.IsEnabled, applied.IsArchived, applied.DefinitionJson)
        ]);

        second.Updates.Should().BeEmpty();
        second.Failures.Should().BeEmpty();
        applied.DefinitionJson[0].Should().Contain("\"rsiOversold\":35");
    }

    [Fact]
    public void Duplicate_alias_is_disabled_and_its_history_id_is_kept()
    {
        var baseline = Guid.NewGuid();
        var alias = Guid.NewGuid();
        var plan = CanonicalStrategyMigration.Plan(
        [
            new CanonicalStrategySnapshot(baseline, "impulse_catch", true, false, ["""{"template":"impulse_catch","timeframe":"15m","params":{"entryLookback":16}}"""]),
            new CanonicalStrategySnapshot(alias, "impulse_catch_v2", true, false, ["""{"template":"impulse_catch_v2","timeframe":"15m","params":{"entryLookback":4}}"""])
        ]);

        plan.Failures.Should().BeEmpty();
        plan.Updates.Should().ContainSingle(update => update.Id == alias && update.IsEnabled == false && update.TemplateKey == "impulse_catch");
        plan.Updates.Single().DefinitionJson[0].Should().Contain("\"entryLookback\":4");
        plan.Reviews.Should().Contain(review => review.Contains(alias.ToString(), StringComparison.Ordinal) && review.Contains("history was kept", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_enabled_canonical_rows_fail_closed()
    {
        var plan = CanonicalStrategyMigration.Plan(
        [
            new CanonicalStrategySnapshot(Guid.NewGuid(), "flat_range", true, false, ["""{"template":"flat_range","timeframe":"1h","params":{}}"""]),
            new CanonicalStrategySnapshot(Guid.NewGuid(), "flat_range", true, false, ["""{"template":"flat_range","timeframe":"1h","params":{}}"""])
        ]);

        plan.Updates.Should().BeEmpty();
        plan.Failures.Should().ContainSingle().Which.Should().Contain("flat_range");
        var act = () => CanonicalStrategyMigration.EnsureSafe(plan);
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("cannot finish safely");
    }

    [Fact]
    public void Unreadable_definition_fails_and_does_not_invent_a_template()
    {
        var plan = CanonicalStrategyMigration.Plan(
        [
            new CanonicalStrategySnapshot(Guid.NewGuid(), "ema_rsi_trend_v2", true, false, ["{not-json"])
        ]);

        plan.Updates.Should().BeEmpty();
        plan.Failures.Should().ContainSingle().Which.Should().Contain("could not be parsed");
    }

    [Fact]
    public void Canonical_discovery_has_one_id_per_strategy_and_no_alias_in_the_runtime_list()
    {
        StrategyTemplateKeys.Canonical.Should().OnlyHaveUniqueItems();
        StrategyTemplateKeys.All.Should().OnlyHaveUniqueItems();
        foreach (var alias in StrategyTemplateKeys.ObsoleteAliases.Keys)
        {
            StrategyTemplateKeys.All.Should().NotContain(alias);
            StrategyTemplateKeys.Canonical.Should().NotContain(alias);
            StrategyTemplateKeys.IsCanonical(alias).Should().BeFalse();
        }
    }
}
