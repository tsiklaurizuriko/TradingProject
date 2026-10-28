using FluentAssertions;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class ProtectiveRatchetTests
{
    [Fact]
    public void Impulse_keeps_the_original_stop_until_the_trade_is_up_twenty_percent()
    {
        var early = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 88m, 400m, 119m, 0.01m);
        early.Should().BeNull();
    }

    [Fact]
    public void Impulse_locks_breakeven_at_twenty_percent_and_keeps_the_take()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 88m, 400m, 120m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopMoved.Should().BeTrue();
        decision.Value.StopLoss.Should().Be(100.20m);
        decision.Value.TakeMoved.Should().BeFalse();
        decision.Value.TakeProfit.Should().Be(400m);
    }

    [Fact]
    public void Impulse_trails_thirty_percent_under_price_on_a_big_move()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 100.20m, 400m, 200m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopLoss.Should().Be(140m);
    }

    [Fact]
    public void Impulse_does_not_step_the_stop_for_a_small_grind()
    {
        ProtectiveRatchet.TryAdvance(StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 140m, 400m, 201m, 0.01m)
            .Should().BeNull();
    }

    [Fact]
    public void Impulse_never_loosens_a_tighter_stop_or_pulls_a_farther_take()
    {
        ProtectiveRatchet.TryAdvance(StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 147m, 250m, 150m, 0.01m)
            .Should().BeNull();
    }

    [Fact]
    public void Adx_moves_the_stop_to_breakeven_and_keeps_the_five_percent_take()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.FAdxSma, PositionSide.Long, 100m, 95m, 105m, 103m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopLoss.Should().Be(100.20m);
        decision.Value.TakeMoved.Should().BeFalse();
        decision.Value.TakeProfit.Should().Be(105m);
    }

    [Fact]
    public void Adx_short_mirrors_the_ratchet()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.FAdxSma, PositionSide.Short, 100m, 105m, 95m, 97m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopLoss.Should().Be(99.80m);
        decision.Value.TakeMoved.Should().BeFalse();
        decision.Value.TakeProfit.Should().Be(95m);
    }

    [Fact]
    public void Ema_cross_locks_breakeven_only_after_ten_percent_and_keeps_the_take()
    {
        ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.EmaRsiTrend, PositionSide.Long, 100m, 95m, 115m, 109m, 0.01m)
            .Should().BeNull();

        var armed = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.EmaRsiTrend, PositionSide.Long, 100m, 95m, 115m, 110m, 0.01m);
        armed.Should().NotBeNull();
        armed!.Value.StopLoss.Should().Be(100.20m);
        armed.Value.TakeMoved.Should().BeFalse();
        armed.Value.TakeProfit.Should().Be(115m);
    }

    [Fact]
    public void Triple_supertrend_locks_breakeven_only_after_ten_percent_and_keeps_the_take()
    {
        var early = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.TripleSupertrend, PositionSide.Long, 100m, 73.5m, 110m, 109m, 0.01m);
        early.Should().BeNull();

        var armed = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.TripleSupertrend, PositionSide.Long, 100m, 73.5m, 110m, 110m, 0.01m);
        armed.Should().NotBeNull();
        armed!.Value.StopLoss.Should().Be(100.20m);
        armed.Value.TakeMoved.Should().BeFalse();
        armed.Value.TakeProfit.Should().Be(110m);
    }

    [Fact]
    public void Ema_cross_keeps_the_one_percent_stop_and_the_three_percent_take()
    {
        ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.BtcEma20Ema50Long, PositionSide.Long, 100m, 99m, 103m, 101.5m, 0.01m)
            .Should().BeNull();
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
    public void Squeeze_and_zigzag_keep_the_configured_stop_and_take()
    {
        ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.SqueezeWatch, PositionSide.Long, 100m, 96m, 108m, 104m, 0.01m)
            .Should().BeNull();
        ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ZigZagFade, PositionSide.Long, 100m, 96m, 108m, 104m, 0.01m)
            .Should().BeNull();
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
    public void Extended_take_comes_back_to_the_configured_target()
    {
        ProtectiveRatchet.TryRestoreBookTake(
            StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 105m, 120m, 200m, 0.01m, out var take, out var reached)
            .Should().BeTrue();
        take.Should().Be(120m);
        reached.Should().BeFalse();

        ProtectiveRatchet.TryRestoreBookTake(
            StrategyTemplateKeys.FAdxSma, PositionSide.Short, 98m, 95m, 50m, 0.01m, out var shortTake, out var shortReached)
            .Should().BeTrue();
        shortTake.Should().Be(95m);
        shortReached.Should().BeFalse();

        ProtectiveRatchet.TryRestoreBookTake(
            StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 125m, 120m, 200m, 0.01m, out var filled, out var already)
            .Should().BeTrue();
        filled.Should().Be(120m);
        already.Should().BeTrue();

        ProtectiveRatchet.TryRestoreBookTake(
            StrategyTemplateKeys.FlowZone, PositionSide.Long, 105m, 115m, 200m, 0.01m, out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Opening_take_stays_at_the_configured_target()
    {
        ProtectiveRatchet.OpeningTake(StrategyTemplateKeys.ImpulseCatch, PositionSide.Long, 100m, 94m, 120m, 100m, 0.01m)
            .Should().Be(120m);
        ProtectiveRatchet.OpeningTake(StrategyTemplateKeys.FAdxSma, PositionSide.Short, 100m, 105m, 95m, 100m, 0.01m)
            .Should().Be(95m);
        ProtectiveRatchet.OpeningTake(StrategyTemplateKeys.BtcEma20Ema50Long, PositionSide.Long, 100m, 99m, 103m, 100m, 0.01m)
            .Should().Be(103m);
        ProtectiveRatchet.OpeningTake(StrategyTemplateKeys.BinHv45, PositionSide.Long, 100m, 95m, 101.25m, 100m, 0.01m)
            .Should().Be(101.25m);
        ProtectiveRatchet.OpeningTake(StrategyTemplateKeys.FlowZone, PositionSide.Long, 100m, 92m, 130m, 100m, 0.01m)
            .Should().Be(130m);
    }

    [Fact]
    public void Shock_fade_keeps_the_original_stop_until_four_percent()
    {
        ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ObsShockFade, PositionSide.Short, 100m, 108m, 76m, 97m, 0.01m)
            .Should().BeNull();
    }

    [Fact]
    public void Shock_fade_locks_breakeven_at_four_percent_and_keeps_the_far_take()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ObsShockFade, PositionSide.Short, 100m, 108m, 76m, 96m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopMoved.Should().BeTrue();
        decision.Value.StopLoss.Should().Be(99.80m);
        decision.Value.TakeMoved.Should().BeFalse();
        decision.Value.TakeProfit.Should().Be(76m);
    }

    [Fact]
    public void Shock_fade_short_trails_the_drop_and_walks_the_take_further_down()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ObsShockFade, PositionSide.Short, 100m, 99.80m, 76m, 80m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopMoved.Should().BeTrue();
        decision.Value.StopLoss.Should().Be(86.40m);
        decision.Value.TakeMoved.Should().BeTrue();
        decision.Value.TakeProfit.Should().Be(73.60m);
    }

    [Fact]
    public void Shock_fade_does_not_pull_a_further_take_back_toward_price()
    {
        var decision = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ObsShockFade, PositionSide.Short, 100m, 99.80m, 70m, 80m, 0.01m);

        decision.Should().NotBeNull();
        decision!.Value.StopLoss.Should().Be(86.40m);
        decision.Value.TakeMoved.Should().BeFalse();
        decision.Value.TakeProfit.Should().Be(70m);
    }

    [Fact]
    public void Shock_fade_long_mirrors_the_trail()
    {
        var armed = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ObsShockFade, PositionSide.Long, 100m, 92m, 124m, 104m, 0.01m);
        armed.Should().NotBeNull();
        armed!.Value.StopLoss.Should().Be(100.20m);
        armed.Value.TakeMoved.Should().BeFalse();
        armed.Value.TakeProfit.Should().Be(124m);

        var riding = ProtectiveRatchet.TryAdvance(
            StrategyTemplateKeys.ObsShockFade, PositionSide.Long, 100m, 100.20m, 124m, 130m, 0.01m);
        riding.Should().NotBeNull();
        riding!.Value.StopLoss.Should().Be(119.60m);
        riding.Value.TakeMoved.Should().BeTrue();
        riding.Value.TakeProfit.Should().Be(140.40m);
    }
}
