using FluentAssertions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class StrategySignalRiskTests
{
    [Fact]
    public void Template_buy_is_sized_as_a_long_by_the_risk_engine()
    {
        var signal = SignalOf(StrategyTemplateKeys.EmaRsiTrend, FlatThen(110m));
        signal.Should().Be(SignalType.Buy);
        var evaluation = new RiskEngine().Evaluate(signal, Book(), Snap(), DateTimeOffset.UtcNow);
        evaluation.Decision.Should().Be(RiskDecision.Approved);
        evaluation.ApprovedQuantity.Should().BeGreaterThan(0m);
        evaluation.Plan!.StopLossPrice.Should().BeLessThan(50_000m);
        evaluation.Plan.TakeProfitPrice.Should().BeGreaterThan(50_000m);
    }

    [Fact]
    public void Template_sell_is_sized_as_a_short_by_the_risk_engine()
    {
        var signal = SignalOf(StrategyTemplateKeys.EmaRsiTrend, FlatThen(90m));
        signal.Should().Be(SignalType.Sell);
        var evaluation = new RiskEngine().Evaluate(signal, Book(), Snap(), DateTimeOffset.UtcNow);
        evaluation.Decision.Should().Be(RiskDecision.Approved);
        evaluation.ApprovedQuantity.Should().BeGreaterThan(0m);
        evaluation.Plan!.StopLossPrice.Should().BeGreaterThan(50_000m);
        evaluation.Plan.TakeProfitPrice.Should().BeLessThan(50_000m);
    }

    private static SignalType SignalOf(string template, IReadOnlyList<TradingPlatform.Domain.Market.MarketCandle> candles)
    {
        var json = StrategyTemplates.Build(template, 1, StrategyTemplates.DefaultsFor(template, false) with
        {
            AllowedSide = StrategySides.Both,
            EmaFast = 3,
            EmaSlow = 6,
            RsiPeriod = 3,
            RsiMinimum = 0m,
            RsiLongMax = 100m
        });
        return new StrategyEngine().Evaluate(new StrategyDefinitionValidator().Parse(json), new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = false
        }, out _);
    }

    private static List<TradingPlatform.Domain.Market.MarketCandle> FlatThen(decimal last)
    {
        var candles = Enumerable.Range(0, 30).Select(i => Bar(i, 100m)).ToList();
        candles.Add(Bar(30, last));
        return candles;
    }

    private static TradingPlatform.Domain.Market.MarketCandle Bar(int i, decimal price) => new()
    {
        Open = price,
        High = price,
        Low = price,
        Close = price,
        Volume = 10,
        OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
        CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5),
        IsClosed = true
    };

    private static RiskProfile Book() => new()
    {
        Name = "LOW",
        RiskPerTradePercent = 1m,
        StopLossPercent = 2m,
        TakeProfitPercent = 4m,
        MaxLeverage = 5m,
        MaxDailyLossPercent = 5m,
        MaxPortfolioRiskPercent = 4m,
        MaxSimultaneousPositions = 2,
        MaxConsecutiveLosses = 5,
        CooldownMinutes = 30,
        MinimumLiquidationSafetyBufferPercent = 1m,
        AllowLive = true
    };

    private static RiskSnapshot Snap() => new()
    {
        Equity = 10_000m,
        AvailableBalance = 10_000m,
        Symbol = "BTCUSDT",
        Price = 50_000m
    };
}
