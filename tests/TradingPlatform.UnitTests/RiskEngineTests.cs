using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Risk;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class RiskEngineTests
{
    private static RiskProfile Low() => new()
    {
        Name = "LOW",
        RiskPerTradePercent = 0.5m,
        StopLossPercent = 2m,
        TakeProfitPercent = 4m,
        MaxLeverage = 3m,
        MaxDailyLossPercent = 3m,
        MaxPortfolioRiskPercent = 4m,
        MaxSimultaneousPositions = 2,
        MaxConsecutiveLosses = 5,
        CooldownMinutes = 30,
        MinimumLiquidationSafetyBufferPercent = 1m,
        AllowLive = true
    };

    private static RiskProfile Medium() => new()
    {
        Name = "MEDIUM",
        RiskPerTradePercent = 1m,
        StopLossPercent = 2.5m,
        TakeProfitPercent = 5m,
        MaxLeverage = 5m,
        MaxDailyLossPercent = 5m,
        MaxPortfolioRiskPercent = 4m,
        MaxSimultaneousPositions = 2,
        MaxConsecutiveLosses = 5,
        CooldownMinutes = 30,
        MinimumLiquidationSafetyBufferPercent = 1m,
        AllowLive = true
    };

    private static RiskProfile High() => new()
    {
        Name = "HIGH",
        RiskPerTradePercent = 2m,
        StopLossPercent = 3m,
        TakeProfitPercent = 6m,
        MaxLeverage = 8m,
        MaxDailyLossPercent = 7m,
        MaxPortfolioRiskPercent = 4m,
        MaxSimultaneousPositions = 2,
        MaxConsecutiveLosses = 5,
        CooldownMinutes = 30,
        MinimumLiquidationSafetyBufferPercent = 1m,
        AllowLive = false
    };

    private static RiskSnapshot Clear(decimal available = 100m, decimal price = 50_000m) => new()
    {
        Equity = available,
        AvailableBalance = available,
        Symbol = "BTCUSDT",
        Price = price
    };

    [Theory]
    [InlineData(100, 0.5, 2, 3, 0.50, 25, 8.33333333)]
    [InlineData(100, 1, 2.5, 5, 1.00, 40, 8)]
    [InlineData(100, 2, 3, 8, 2.00, 66.66666667, 8.33333333)]
    [InlineData(1000, 0.5, 2, 3, 5.00, 250, 83.33333333)]
    [InlineData(1000, 1, 2.5, 5, 10.00, 400, 80)]
    [InlineData(1000, 2, 3, 8, 20.00, 666.66666667, 83.33333333)]
    public void Sizes_from_available_times_r_over_stop(
        decimal available,
        decimal riskPct,
        decimal sl,
        decimal leverage,
        decimal expectedRisk,
        decimal expectedNotional,
        decimal expectedMargin)
    {
        var profile = High();
        profile.RiskPerTradePercent = riskPct;
        profile.StopLossPercent = sl;
        profile.TakeProfitPercent = sl * 2m;
        profile.MaxLeverage = leverage;

        var plan = RiskEngine.Plan(profile, available, 50_000m);

        plan.Allowed.Should().BeTrue();
        plan.RiskAmount.Should().Be(expectedRisk);
        plan.PositionNotional.Should().BeApproximately(expectedNotional, 0.01m);
        plan.IsolatedMargin.Should().BeApproximately(expectedMargin, 0.01m);
    }

    [Fact]
    public void Available_increase_resizes_risk_amount()
    {
        RiskEngine.Plan(High(), 150m, 50_000m).RiskAmount.Should().Be(3m);
    }

    [Fact]
    public void Available_decrease_resizes_risk_amount()
    {
        var plan = RiskEngine.Plan(High(), 80m, 50_000m);
        plan.RiskAmount.Should().Be(1.60m);
        plan.PositionNotional.Should().BeApproximately(53.33333333m, 0.01m);
    }

    [Fact]
    public void Long_and_short_stop_and_take_prices()
    {
        var longPlan = RiskEngine.Plan(High(), 100m, 100m, PositionSide.Long);
        longPlan.StopLossPrice.Should().Be(97m);
        longPlan.TakeProfitPrice.Should().Be(106m);

        var shortPlan = RiskEngine.Plan(High(), 100m, 100m, PositionSide.Short);
        shortPlan.StopLossPrice.Should().Be(103m);
        shortPlan.TakeProfitPrice.Should().Be(94m);
    }

    [Fact]
    public void Exchange_leverage_cap_is_used_instead_of_profile_max()
    {
        var plan = RiskEngine.Plan(High(), 100m, 50_000m, PositionSide.Long, new RiskSizingHints { ExchangeMaxLeverage = 5m });
        plan.Allowed.Should().BeTrue();
        plan.Leverage.Should().Be(5m);
        plan.IsolatedMargin.Should().BeApproximately(13.33333333m, 0.01m);
    }

    [Fact]
    public void Rejects_when_quantity_rounds_below_exchange_minimum()
    {
        var plan = RiskEngine.Plan(
            High(),
            100m,
            50_000m,
            PositionSide.Long,
            new RiskSizingHints { StepSize = 1m, MinQuantity = 1m, MinNotional = 5m });
        plan.Allowed.Should().BeFalse();
        plan.Reason.Should().Contain("below exchange minimum");
    }

    [Fact]
    public void Rejects_when_isolated_margin_exceeds_available()
    {
        var tight = High();
        tight.RiskPerTradePercent = 10m;
        tight.StopLossPercent = 0.5m;
        tight.TakeProfitPercent = 1.5m;
        tight.MaxLeverage = 1m;
        tight.MaxPortfolioRiskPercent = 100m;

        var plan = RiskEngine.Plan(tight, 100m, 50_000m);

        plan.Allowed.Should().BeFalse();
        plan.Reason.Should().Contain("exceeds available");
    }

    [Fact]
    public void Rejects_stop_too_close_to_liquidation()
    {
        var profile = High();
        profile.StopLossPercent = 12m;
        profile.TakeProfitPercent = 20m;
        profile.MaxLeverage = 8m;
        profile.MinimumLiquidationSafetyBufferPercent = 1m;

        var plan = RiskEngine.Plan(profile, 100m, 50_000m);
        plan.Allowed.Should().BeFalse();
        plan.Reason.Should().Contain("liquidation");
    }

    [Fact]
    public void Daily_loss_does_not_block_new_entries()
    {
        var result = new RiskEngine().Evaluate(
            SignalType.Buy,
            High(),
            Clear() with { DailyRealizedPnL = -70m, AccountDailyPnL = -70m },
            DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Approved);
        result.HaltAccount.Should().BeFalse();
    }

    [Fact]
    public void Rejects_when_coin_already_open()
    {
        var result = new RiskEngine().Evaluate(
            SignalType.Buy,
            High(),
            Clear() with { SymbolAlreadyOpen = true },
            DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Rejected);
        result.Reason.Should().Contain("already has an open Isolated position");
    }

    [Fact]
    public void Rejects_when_max_simultaneous_positions_reached()
    {
        var result = new RiskEngine().Evaluate(
            SignalType.Buy,
            High(),
            Clear() with { OpenPositionCount = 2 },
            DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Rejected);
        result.Reason.Should().Contain("Maximum simultaneous Isolated positions reached for this strategy.");
    }

    [Fact]
    public void Rejects_when_projected_portfolio_risk_exceeds_cap()
    {
        var result = new RiskEngine().Evaluate(
            SignalType.Buy,
            High(),
            Clear() with { OpenRiskPercent = 3m },
            DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Rejected);
        result.Reason.Should().Contain("portfolio planned risk for this strategy");
    }

    [Fact]
    public void Rejects_consecutive_losses_during_cooldown()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new RiskEngine().Evaluate(
            SignalType.Buy,
            High(),
            Clear() with { ConsecutiveLosses = 5, LastLossAt = now.AddMinutes(-5) },
            now);

        result.Decision.Should().Be(RiskDecision.Rejected);
        result.HaltAccount.Should().BeTrue();
        result.Reason.Should().Contain("Consecutive loss");
    }

    [Fact]
    public void Independent_trades_do_not_double_after_a_loss()
    {
        var first = RiskEngine.Plan(High(), 100m, 50_000m);
        var second = RiskEngine.Plan(High(), 80m, 50_000m);
        second.RiskAmount.Should().Be(1.60m);
        second.RiskAmount.Should().NotBe(first.RiskAmount * 2m);
    }

    [Fact]
    public void Stale_market_data_locks_new_entries()
    {
        var result = new RiskEngine().Evaluate(
            SignalType.Buy,
            High(),
            Clear() with { MarketDataAgeMs = RiskEngine.MaxMarketDataAgeMs + 1 },
            DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Rejected);
        result.HaltAccount.Should().BeTrue();
    }

    [Fact]
    public void Low_medium_high_defaults_are_distinct()
    {
        Low().RiskPerTradePercent.Should().Be(0.5m);
        Medium().RiskPerTradePercent.Should().Be(1m);
        High().RiskPerTradePercent.Should().Be(2m);
        Low().MaxDailyLossPercent.Should().Be(3m);
        Medium().MaxDailyLossPercent.Should().Be(5m);
        High().MaxDailyLossPercent.Should().Be(7m);
        High().AllowLive.Should().BeFalse();
    }

    [Fact]
    public void Sell_while_flat_sizes_a_short()
    {
        var result = new RiskEngine().Evaluate(SignalType.Sell, High(), Clear() with { Price = 100m }, DateTimeOffset.UtcNow);
        result.Decision.Should().Be(RiskDecision.Approved);
        result.ApprovedQuantity.Should().BeGreaterThan(0m);
        result.Plan!.StopLossPrice.Should().BeGreaterThan(100m);
        result.Plan.TakeProfitPrice.Should().BeLessThan(100m);
    }

    [Fact]
    public void Exit_does_not_size()
    {
        var result = new RiskEngine().Evaluate(SignalType.Exit, High(), Clear(), DateTimeOffset.UtcNow);
        result.Decision.Should().Be(RiskDecision.Approved);
        result.ApprovedQuantity.Should().Be(0m);
        result.Reason.Should().Contain("Exit");
    }
}

public sealed class PortfolioRiskTests
{
    [Fact]
    public void Perfect_correlation_heat_equals_sum()
    {
        PortfolioRisk.CorrelatedHeat([0.01m, 0.01m, 0.01m], 1m).Should().BeApproximately(0.03m, 0.0000001m);
    }

    [Fact]
    public void Zero_correlation_heat_equals_rss()
    {
        PortfolioRisk.CorrelatedHeat([0.03m, 0.04m], 0m).Should().BeApproximately(0.05m, 0.0000001m);
    }

    [Fact]
    public void Isolated_margin_divides_notional_by_leverage()
    {
        PortfolioRisk.IsolatedMargin(2_000m, 2m).Should().Be(1_000m);
        PortfolioRisk.IsolatedMargin(2_000m, 0m).Should().Be(2_000m);
    }

    [Fact]
    public void Floor_to_step_never_rounds_up()
    {
        PortfolioRisk.FloorToStep(0.00133333m, 0.001m).Should().Be(0.001m);
    }

    [Fact]
    public void Floor_to_step_also_truncates_to_quantity_precision()
    {
        PortfolioRisk.FloorToStep(4.79m, 0.01m, 1).Should().Be(4.7m);
        PortfolioRisk.FloorToStep(0.00006m, 0.00001m, 3).Should().Be(0m);
        PortfolioRisk.FloorToStep(0.0024m, 0.001m, 3).Should().Be(0.002m);
        PortfolioRisk.EffectiveQuantityPrecision(0, 0.00001m).Should().BeNull();
        PortfolioRisk.EffectiveQuantityPrecision(0, 1m).Should().Be(0);
        PortfolioRisk.EffectiveQuantityPrecision(3, 0.001m).Should().Be(3);
    }
}

public sealed class RiskLiveGuardTests
{
    [Fact]
    public void High_cannot_start_live_when_allow_live_is_false()
    {
        var act = () => RiskLiveGuard.EnsureAllowed(
            TradingMode.Live,
            new RiskProfile { Name = "HIGH", AllowLive = false });
        act.Should().Throw<TradingPlatform.Domain.Errors.DomainException>().Which.Code.Should().Be("RISK_LIMIT_EXCEEDED");
    }

    [Fact]
    public void Low_can_start_live()
    {
        var act = () => RiskLiveGuard.EnsureAllowed(
            TradingMode.Live,
            new RiskProfile { Name = "LOW", AllowLive = true });
        act.Should().NotThrow();
    }
}
