using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Risk;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class RiskEngineTests
{
    private static RiskProfile Book() => new()
    {
        RiskPerTradePercent = 1m,
        MaxPositionPercent = 100m,
        MaxDailyLossPercent = 5m,
        MaxOpenPositions = 8,
        MaxDailyTrades = 20,
        MaxLeverage = 2m,
        MinFreeMarginPercent = 0m,
        MaxPortfolioHeatPercent = 6m,
        MaxTotalExposurePercent = 200m,
        CorrelationFactor = 0.75m,
        MarginMode = MarginMode.Isolated
    };

    private static RiskSnapshot Clear(decimal equity = 10_000m, decimal price = 50_000m, decimal stop = 1.5m) => new()
    {
        Equity = equity,
        AvailableBalance = equity,
        Symbol = "BTCUSDT",
        Price = price,
        StopLossPercent = stop
    };

    [Fact]
    public void Rejects_new_entries_when_daily_loss_exceeds_limit()
    {
        var result = new RiskEngine().Evaluate(SignalType.Buy, Book(), Clear() with { DailyRealizedPnL = -500m }, DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Rejected);
        result.Reason.Should().Contain("daily loss");
        result.HaltAccount.Should().BeTrue();
    }

    [Fact]
    public void Sizes_to_van_tharp_r_when_stop_is_known()
    {
        var result = new RiskEngine().Evaluate(SignalType.Buy, Book(), Clear(), DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Approved);
        result.ApprovedQuantity.Should().BeApproximately(10_000m * 0.01m / (50_000m * 0.015m), 0.0000001m);
    }

    [Fact]
    public void Caps_notional_before_r_when_max_position_binds()
    {
        var profile = Book();
        profile.MaxPositionPercent = 10m;
        var result = new RiskEngine().Evaluate(SignalType.Buy, profile, Clear(), DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Approved);
        result.ApprovedQuantity.Should().BeApproximately(10_000m * 0.10m / 50_000m, 0.0000001m);
    }

    [Fact]
    public void Rejects_when_symbol_already_open_in_the_book()
    {
        var result = new RiskEngine().Evaluate(SignalType.Buy, Book(), Clear() with { SymbolAlreadyOpen = true }, DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Rejected);
        result.Reason.Should().Contain("already has an open position");
    }

    [Fact]
    public void Cross_book_cannot_exceed_hard_cap()
    {
        var profile = Book();
        profile.MarginMode = MarginMode.Cross;
        profile.MaxOpenPositions = 20;
        var result = new RiskEngine().Evaluate(
            SignalType.Buy,
            profile,
            Clear() with { AccountOpenPositions = RiskEngine.MaxCrossOpenPositions },
            DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Rejected);
        result.Reason.Should().Contain("Cross");
    }

    [Fact]
    public void Correlated_alts_consume_one_heat_budget()
    {
        var profile = Book();
        profile.MaxPortfolioHeatPercent = 2m;
        profile.CorrelationFactor = 1m;
        var open = Enumerable.Repeat(0.01m, 2).ToList();
        var result = new RiskEngine().Evaluate(SignalType.Buy, profile, Clear() with { OpenRiskFractions = open }, DateTimeOffset.UtcNow);

        result.Decision.Should().Be(RiskDecision.Rejected);
        result.Reason.Should().Contain("heat");
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
}
