using FluentAssertions;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class MarketScannerScoringTests
{
    [Fact]
    public void Ranks_the_whole_set_without_a_hardcoded_top_15()
    {
        var thin = Row("THINUSDT", volume: 1_000m, trades: 10, spread: 20m);
        var liquid = Row("LIQUIDUSDT", volume: 80_000_000m, trades: 90_000, spread: 1m);
        var ranked = MarketScanScoring.Rank([thin, liquid]);
        ranked.Should().HaveCount(2);
        ranked[0].Contract.Symbol.Should().Be("LIQUIDUSDT");
        ranked[0].ScanRank.Should().Be(1);
        ranked[1].Contract.Symbol.Should().Be("THINUSDT");
    }

    [Fact]
    public void Eligibility_is_independent_of_watch_and_not_a_top_n_cutoff()
    {
        var options = new ScannerOptions { MinQuoteVolumeUsdt = 10_000_000m, MinTrades24h = 5_000, MaxSpreadBps = 8m, MinScanScore = 0.01m };
        var watched = Row("THINUSDT", volume: 1000m, trades: 10, spread: 1m) with { ScanScore = 0.9m, DataQuality = 1m };
        var eligible = Row("LIQUIDUSDT", volume: 50_000_000m, trades: 20_000, spread: 2m) with { ScanScore = 0.9m, DataQuality = 1m, LastPrice = 10m };
        TradeEligibilityRules.Evaluate(watched with { LastPrice = 1m }, options).Watchable.Should().BeTrue();
        TradeEligibilityRules.Evaluate(watched with { LastPrice = 1m }, options).Eligible.Should().BeFalse();
        TradeEligibilityRules.Evaluate(eligible, options).Eligible.Should().BeTrue();
    }

    private static MarketScanRow Row(string symbol, decimal volume, int trades, decimal spread) =>
        new(
            new DiscoveredFuturesContract(symbol, symbol[..^4], "USDT", "USDT", "PERPETUAL", "TRADING", 0.01m, 0.001m, 0.001m, 5m, 2, 3),
            10m,
            volume,
            2m,
            11m,
            9m,
            trades,
            9.99m,
            10.01m,
            spread,
            0.0001m,
            10m,
            0m,
            20m,
            2m,
            1m,
            0m,
            0);
}
