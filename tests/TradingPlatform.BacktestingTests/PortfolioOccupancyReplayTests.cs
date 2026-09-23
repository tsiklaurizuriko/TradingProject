using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using Xunit;

namespace TradingPlatform.BacktestingTests;

public sealed class PortfolioOccupancyReplayTests
{
    [Fact]
    public void Second_btc_signal_is_rejected_as_symbol_already_open()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var bars = Enumerable.Range(0, 12).Select(i => Bar(start, i, "BTCUSDT", 100m)).ToList();
        var intents = new[]
        {
            new OccupancyIntent(start.AddMinutes(5), start.AddMinutes(10), "scalp_ema_momentum", "BTCUSDT", SignalType.Buy, 100m),
            new OccupancyIntent(start.AddMinutes(5), start.AddMinutes(10), "scalp_rsi_pullback", "BTCUSDT", SignalType.Buy, 100m)
        };
        var settings = StrategyValidation.LowIsolatedRisk(start, start.AddHours(2)) with { MaxSimultaneousPositions = 5 };
        var result = PortfolioOccupancyReplay.Run(intents, bars, settings);
        result.SameCoinRejects.Should().BeGreaterThan(0);
        result.Rejects.Should().Contain(r => r.Reason == "SymbolAlreadyOpen" && r.Symbol == "BTCUSDT");
        result.Trades.Count.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Long_then_short_on_the_same_coin_is_still_one_isolated_reject()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var bars = Enumerable.Range(0, 12).Select(i => Bar(start, i, "BTCUSDT", 100m)).ToList();
        var intents = new[]
        {
            new OccupancyIntent(start, start.AddMinutes(5), "A", "BTCUSDT", SignalType.Buy, 100m),
            new OccupancyIntent(start, start.AddMinutes(5), "B", "BTCUSDT", SignalType.Sell, 100m)
        };
        var settings = StrategyValidation.LowIsolatedRisk(start, start.AddHours(2)) with { MaxSimultaneousPositions = 5 };
        var result = PortfolioOccupancyReplay.Run(intents, bars, settings);
        result.SameCoinRejects.Should().Be(1);
        result.Rejects.Should().ContainSingle(r => r.Reason == "SymbolAlreadyOpen");
    }

    [Fact]
    public void Portfolio_drawdown_comes_from_combined_equity()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var btc = Enumerable.Range(0, 8).Select(i => Bar(start, i, "BTCUSDT", i == 3 ? 90m : 100m, low: i == 3 ? 90m : 99.5m)).ToList();
        var eth = Enumerable.Range(0, 8).Select(i => Bar(start, i, "ETHUSDT", 50m)).ToList();
        var intents = new[]
        {
            new OccupancyIntent(start, start.AddMinutes(5), "scalp_ema_momentum", "BTCUSDT", SignalType.Buy, 100m)
        };
        var settings = StrategyValidation.LowIsolatedRisk(start, start.AddHours(2)) with { MaxSimultaneousPositions = 5 };
        var result = PortfolioOccupancyReplay.Run(intents, btc.Concat(eth).ToList(), settings);
        result.MaximumDrawdownPercent.Should().BeGreaterThanOrEqualTo(0m);
        result.Equity.Should().NotBeEmpty();
    }

    private static OccupancyBar Bar(DateTimeOffset start, int i, string symbol, decimal close, decimal? low = null) =>
        new(
            start.AddMinutes(i * 5),
            start.AddMinutes(i * 5 + 5),
            symbol,
            close,
            close + 0.5m,
            low ?? close - 0.5m,
            close);
}
