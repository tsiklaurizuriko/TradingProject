using FluentAssertions;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class ProtectiveRatchetTests
{
    [Fact]
    public void Impulse_keeps_the_original_stop_until_the_trade_is_up_ten_percent()
    {
        var early = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 94m, 200m, 109m, 0.01m);
        early.Should().BeNull();
    }

    [Fact]
    public void Impulse_trails_eight_percent_under_price_and_pushes_the_take_out()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 94m, 120m, 110m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopMoved.Should().BeTrue();
        decision.Value.StopLoss.Should().Be(101.20m);
        decision.Value.TakeMoved.Should().BeTrue();
        decision.Value.TakeProfit.Should().Be(200m);
    }

    [Fact]
    public void Impulse_does_not_step_the_stop_for_a_small_grind()
    {
        ProtectiveRatchet.TryAdvance(StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 101.20m, 200m, 111m, 0.01m)
            .Should().BeNull();
    }

    [Fact]
    public void Impulse_never_loosens_a_tighter_stop_or_pulls_a_farther_take()
    {
        ProtectiveRatchet.TryAdvance(StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 147m, 250m, 150m, 0.01m)
            .Should().BeNull();
    }

    [Fact]
    public void Adx_moves_the_stop_to_breakeven_and_extends_the_take_after_three_percent()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.FAdxSma, PositionSide.Long, 100m, 95m, 105m, 103m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopLoss.Should().Be(100.20m);
        decision.Value.TakeProfit.Should().Be(200m);
    }

    [Fact]
    public void Adx_short_mirrors_the_ratchet()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.FAdxSma, PositionSide.Short, 100m, 105m, 95m, 97m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopLoss.Should().Be(99.80m);
        decision.Value.TakeProfit.Should().Be(50m);
    }

    [Fact]
    public void Triple_supertrend_extends_the_take_but_does_not_trail_before_a_large_gain()
    {
        var early = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.TripleSupertrend, PositionSide.Long, 100m, 73.5m, 110m, 109m, 0.01m);
        early.Should().NotBeNull();
        early!.Value.StopMoved.Should().BeFalse();
        early.Value.StopLoss.Should().Be(73.5m);
        early.Value.TakeProfit.Should().Be(200m);

        var armed = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.TripleSupertrend, PositionSide.Long, 100m, 73.5m, 200m, 110m, 0.01m);
        armed.Should().NotBeNull();
        armed!.Value.StopLoss.Should().Be(100.20m);
        armed.Value.TakeMoved.Should().BeFalse();
    }

    [Fact]
    public void Ema_cross_locks_breakeven_and_keeps_the_three_percent_take()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.BtcEma20Ema50Long, PositionSide.Long, 100m, 99m, 103m, 101.5m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopLoss.Should().Be(100.20m);
        decision.Value.TakeMoved.Should().BeFalse();
        decision.Value.TakeProfit.Should().Be(103m);
    }

    [Fact]
    public void Flow_zone_waits_for_eight_percent_before_locking_breakeven()
    {
        ProtectiveRatchet.TryAdvance(StrategyTemplateKeys.FlowZone, PositionSide.Long, 100m, 92m, 130m, 107m, 0.01m)
            .Should().BeNull();

        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.FlowZone, PositionSide.Long, 100m, 92m, 130m, 108m, 0.01m);
        decision.Should().NotBeNull();
        decision!.Value.StopLoss.Should().Be(100.20m);
        decision.Value.TakeMoved.Should().BeFalse();
    }

    [Fact]
    public void Squeeze_and_zigzag_lock_breakeven_without_extending_the_take()
    {
        var squeeze = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.SqueezeWatch, PositionSide.Long, 100m, 96m, 108m, 104m, 0.01m);
        squeeze.Should().NotBeNull();
        squeeze!.Value.StopLoss.Should().Be(100.20m);
        squeeze.Value.TakeProfit.Should().Be(108m);

        var zigzag = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ZigZagFade, PositionSide.Long, 100m, 96m, 108m, 104m, 0.01m);
        zigzag.Should().NotBeNull();
        zigzag!.Value.TakeMoved.Should().BeFalse();
    }

    [Theory]
    [InlineData(StrategyTemplateKeys.BinHv45)]
    [InlineData(StrategyTemplateKeys.ClucMay72018)]
    [InlineData(StrategyTemplateKeys.CombinedBinHCluc)]
    [InlineData(StrategyTemplateKeys.DonchianV2)]
    public void Scalps_and_donchian_are_left_alone(string template)
    {
        ProtectiveRatchet.TryAdvance(template, PositionSide.Long, 100m, 95m, 101.25m, 110m, 0.01m)
            .Should().BeNull();
    }

    [Fact]
    public void Repair_keeps_the_tighter_stop_and_the_farther_take()
    {
        ProtectiveRatchet.KeepTighterStop(PositionSide.Long, 110m, 94m, 100.20m, 0.01m).Should().Be(100.20m);
        ProtectiveRatchet.KeepFurtherTake(PositionSide.Long, 110m, 120m, 200m, 0.01m).Should().Be(200m);
        ProtectiveRatchet.KeepTighterStop(PositionSide.Long, 100m, 94m, 101m, 0.01m).Should().Be(94m);
        ProtectiveRatchet.KeepFurtherTake(PositionSide.Long, 130m, 120m, 125m, 0.01m).Should().Be(0m);
    }

    [Fact]
    public void Opening_take_is_pushed_out_only_for_the_runners()
    {
        ProtectiveRatchet.OpeningTake(StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 94m, 120m, 100m, 0.01m)
            .Should().Be(200m);
        ProtectiveRatchet.OpeningTake(StrategyTemplateKeys.FAdxSma, PositionSide.Short, 100m, 105m, 95m, 100m, 0.01m)
            .Should().Be(50m);
        ProtectiveRatchet.OpeningTake(StrategyTemplateKeys.BtcEma20Ema50Long, PositionSide.Long, 100m, 99m, 103m, 100m, 0.01m)
            .Should().Be(103m);
        ProtectiveRatchet.OpeningTake(StrategyTemplateKeys.BinHv45, PositionSide.Long, 100m, 95m, 101.25m, 100m, 0.01m)
            .Should().Be(101.25m);
        ProtectiveRatchet.OpeningTake(StrategyTemplateKeys.FlowZone, PositionSide.Long, 100m, 92m, 130m, 100m, 0.01m)
            .Should().Be(130m);
    }
}
