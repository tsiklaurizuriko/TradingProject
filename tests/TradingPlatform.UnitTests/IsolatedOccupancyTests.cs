using FluentAssertions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class IsolatedOccupancyTests
{
    [Fact]
    public void Merge_shows_one_filled_isolated_row_with_live_pnl_and_keeps_sl_tp_as_fields()
    {
        var bot = new PositionDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ONGUSDT",
            "Long",
            149m,
            0.0853m,
            0.0853m,
            0m,
            0m,
            0m,
            DateTimeOffset.UtcNow,
            "Bot",
            0.26m,
            4.24m,
            12.71m,
            3m,
            2m,
            4m,
            0.08359m,
            0.08872m);
        var live = new LiveOpenPosition("ONGUSDT", "Long", 149m, 0.08531m, 0.08488m, -0.06m, "Futures");
        var extra = new LiveOpenPosition("OPUSDT", "Long", 100.6m, 0.127m, 0.1269m, -0.02m, "Futures");

        var merged = IsolatedOccupancy.MergeBotAndExchange([bot], [live, extra], Overlay);

        merged.Should().HaveCount(2);
        var ong = merged.Single(row => row.Symbol == "ONGUSDT");
        ong.UnrealizedPnL.Should().Be(-0.06m);
        ong.CurrentPrice.Should().Be(0.08488m);
        ong.StopLossPrice.Should().Be(0.08359m);
        ong.TakeProfitPrice.Should().Be(0.08872m);
        ong.MarginUsdt.Should().Be(4.24m);
        merged.Single(row => row.Symbol == "OPUSDT").UnrealizedPnL.Should().Be(-0.02m);
        IsolatedOccupancy.UniqueCoins(["ONGUSDT", "ONGUSDT", "OPUSDT"]).Should().Be(2);
    }

    [Fact]
    public void UniqueCoinsForStrategy_does_not_consume_another_strategy_slots()
    {
        var rsi = Guid.NewGuid();
        var bollinger = Guid.NewGuid();
        var book = new[]
        {
            Open("BTCUSDT", rsi),
            Open("ETHUSDT", rsi),
            Open("SOLUSDT", bollinger),
            Open("XRPUSDT", bollinger),
            Open("ADAUSDT", bollinger)
        };

        IsolatedOccupancy.UniqueCoins(book).Should().Be(5);
        IsolatedOccupancy.UniqueCoinsForStrategy(book, rsi).Should().Be(2);
        IsolatedOccupancy.UniqueCoinsForStrategy(book, bollinger).Should().Be(3);
        IsolatedOccupancy.PlannedRiskPercent(IsolatedOccupancy.ForStrategy(book, rsi), 100m).Should().Be(1m);
        IsolatedOccupancy.PlannedRiskPercent(IsolatedOccupancy.ForStrategy(book, bollinger), 100m).Should().Be(1.5m);
    }

    [Fact]
    public void UniqueCoinsForStrategy_ignores_live_coins_owned_by_another_strategy()
    {
        var rsi = Guid.NewGuid();
        var book = new[] { Open("BTCUSDT", rsi) };
        var live = new LiveOpenPosition[]
        {
            new("BTCUSDT", "Long", 1m, 100m, 100m, 0m, "Futures"),
            new("ETHUSDT", "Long", 2m, 200m, 200m, 0m, "Futures")
        };

        IsolatedOccupancy.UniqueCoins(book, live, liveAuthoritative: true, DateTimeOffset.UtcNow).Should().Be(2);
        IsolatedOccupancy.UniqueCoinsForStrategy(book, rsi, live, liveAuthoritative: true, DateTimeOffset.UtcNow)
            .Should().Be(1);
    }

    [Fact]
    public void Merge_drops_stale_bot_snapshot_when_binance_no_longer_holds_the_coin()
    {
        var ghost = new PositionDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ONGUSDT",
            "Long",
            149m,
            0.0853m,
            0.0853m,
            0m,
            0m,
            0m,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            "Bot",
            0.26m,
            4.24m);
        var live = new LiveOpenPosition("ACEUSDT", "Long", 79.47m, 0.16095m, 0.159m, -0.15m, "Futures");

        var merged = IsolatedOccupancy.MergeBotAndExchange(
            [ghost],
            [live],
            Overlay,
            liveAuthoritative: true,
            DateTimeOffset.UtcNow);

        merged.Should().HaveCount(1);
        merged.Single().Symbol.Should().Be("ACEUSDT");
        IsolatedOccupancy.IsLiveGhost(
            new Position
            {
                Symbol = "ONGUSDT",
                Quantity = 149m,
                AverageEntryPrice = 0.0853m,
                OpenedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            [live],
            DateTimeOffset.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void Merge_keeps_a_fresh_fill_until_binance_overlay_catches_up()
    {
        var fresh = new PositionDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ONGUSDT",
            "Long",
            149m,
            0.0853m,
            0.0853m,
            0m,
            0m,
            0m,
            DateTimeOffset.UtcNow.AddSeconds(-5),
            "Bot");
        var live = new LiveOpenPosition("ACEUSDT", "Long", 79.47m, 0.16095m, 0.159m, -0.15m, "Futures");

        var merged = IsolatedOccupancy.MergeBotAndExchange(
            [fresh],
            [live],
            Overlay,
            liveAuthoritative: true,
            DateTimeOffset.UtcNow);

        merged.Select(row => row.Symbol).Should().BeEquivalentTo(["ACEUSDT", "ONGUSDT"]);
    }

    [Fact]
    public void StampProtection_fills_overlay_only_isolated_row_from_the_risk_book()
    {
        var overlay = Overlay(new LiveOpenPosition("OPUSDT", "Long", 100.6m, 0.12709m, 0.126694m, -0.04m, "Futures"));
        var botId = Guid.NewGuid();

        var stamped = IsolatedOccupancy.StampProtection(overlay, 2m, 4m, 0.5m, 3m, 0.00001m, botId);

        stamped.BotId.Should().Be(botId);
        stamped.StopLossPercent.Should().Be(2m);
        stamped.TakeProfitPercent.Should().Be(4m);
        stamped.Leverage.Should().Be(3m);
        stamped.MarginUsdt.Should().BeApproximately(stamped.NotionalUsdt / 3m, 0.01m);
        stamped.InitialRiskUsdt.Should().BeApproximately(stamped.NotionalUsdt * 0.02m, 0.01m);
        stamped.StopLossPrice.Should().BeLessThan(overlay.AverageEntryPrice);
        stamped.TakeProfitPrice.Should().BeGreaterThan(overlay.AverageEntryPrice);
        stamped.UnrealizedPnL.Should().Be(-0.04m);
    }

    private static PositionDto Overlay(LiveOpenPosition position) =>
        new(
            Guid.NewGuid(),
            Guid.Empty,
            position.Symbol,
            position.Side,
            position.Quantity,
            position.EntryPrice,
            position.MarkPrice,
            position.UnrealizedPnL,
            0m,
            0m,
            DateTimeOffset.UtcNow,
            "Binance");

    private static Position Open(string symbol, Guid strategyId) =>
        new()
        {
            Symbol = symbol,
            Quantity = 1m,
            AverageEntryPrice = 100m,
            InitialRiskUsdt = 0.5m,
            OpenedAt = DateTimeOffset.UtcNow,
            Bot = new Bot
            {
                Symbol = symbol,
                StrategyVersion = new StrategyVersion { StrategyId = strategyId }
            }
        };
}
