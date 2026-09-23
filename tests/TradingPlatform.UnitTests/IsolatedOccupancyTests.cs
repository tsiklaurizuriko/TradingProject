using FluentAssertions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;
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
    public void UniqueCoinsForStrategy_does_not_count_a_later_strategy_claiming_the_same_coin()
    {
        var ema = Guid.NewGuid();
        var rsi = Guid.NewGuid();
        var first = DateTimeOffset.UtcNow.AddHours(-2);
        var later = DateTimeOffset.UtcNow.AddMinutes(-10);
        var book = new[]
        {
            Open("EIGENUSDT", ema, first),
            Open("EIGENUSDT", rsi, later),
            Open("FILUSDT", ema, first),
            Open("TLMUSDT", ema, first),
            Open("WAXPUSDT", ema, first),
            Open("ZKPUSDT", ema, first),
            Open("IDOLUSDT", Guid.NewGuid(), first)
        };

        IsolatedOccupancy.UniqueCoinsForStrategy(book, ema).Should().Be(5);
        IsolatedOccupancy.UniqueCoinsForStrategy(book, rsi).Should().Be(0);
        IsolatedOccupancy.IsolatedOwner(book.Where(row => row.Symbol == "EIGENUSDT")).Bot!.StrategyVersion!.StrategyId
            .Should().Be(ema);
    }

    [Fact]
    public void Merge_keeps_the_earliest_bot_fill_when_two_strategies_have_the_same_coin()
    {
        var emaBot = Guid.NewGuid();
        var rsiBot = Guid.NewGuid();
        var ema = new PositionDto(
            Guid.NewGuid(),
            emaBot,
            "EIGENUSDT",
            "Long",
            1m,
            0.24m,
            0.24m,
            0m,
            0m,
            0m,
            DateTimeOffset.UtcNow.AddHours(-2),
            "Bot");
        var rsi = new PositionDto(
            Guid.NewGuid(),
            rsiBot,
            "EIGENUSDT",
            "Long",
            1m,
            0.24m,
            0.24m,
            0m,
            0m,
            0m,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "Bot");
        var live = new LiveOpenPosition("EIGENUSDT", "Long", 1m, 0.238m, 0.236m, -0.07m, "Futures");

        var merged = IsolatedOccupancy.MergeBotAndExchange([rsi, ema], [live], Overlay);

        merged.Should().ContainSingle();
        merged[0].BotId.Should().Be(emaBot);
        merged[0].UnrealizedPnL.Should().Be(-0.07m);
    }

    [Fact]
    public void UniqueCoinsForStrategy_keeps_old_db_slots_when_live_overlay_is_empty()
    {
        var rsi = Guid.NewGuid();
        var opened = DateTimeOffset.UtcNow.AddMinutes(-10);
        var book = new[]
        {
            Open("BTCUSDT", rsi, opened),
            Open("ETHUSDT", rsi, opened),
            Open("SOLUSDT", rsi, opened),
            Open("XRPUSDT", rsi, opened),
            Open("ADAUSDT", rsi, opened)
        };

        IsolatedOccupancy.UniqueCoinsForStrategy(book, rsi, [], liveAuthoritative: true, DateTimeOffset.UtcNow)
            .Should().Be(5);
    }

    [Fact]
    public void UniqueCoinsForStrategy_matches_bot_id_when_strategy_version_is_not_loaded()
    {
        var rsi = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var botId = Guid.NewGuid();
        var book = new[]
        {
            new Position
            {
                BotId = botId,
                Symbol = "BTCUSDT",
                Quantity = 1m,
                AverageEntryPrice = 100m,
                OpenedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                Bot = new Bot { Id = botId, Symbol = "BTCUSDT", StrategyVersionId = versionId }
            }
        };

        IsolatedOccupancy.UniqueCoinsForStrategy(
                book,
                rsi,
                strategyBotIds: new HashSet<Guid> { botId },
                strategyVersionIds: new HashSet<Guid> { versionId })
            .Should().Be(1);
        IsolatedOccupancy.UniqueCoinsForStrategy(book, rsi).Should().Be(0);
    }

    [Fact]
    public void IsCoinOpen_uses_db_snapshot_when_overlay_omits_the_coin()
    {
        var opened = DateTimeOffset.UtcNow.AddHours(-8);
        var book = new[] { Open("CELOUSDT", Guid.NewGuid(), opened) };
        var overlay = new LiveOpenPosition[]
        {
            new("OPUSDT", "Long", 1m, 1m, 1m, 0m, "Futures")
        };

        IsolatedOccupancy.IsCoinOpen("CELOUSDT", book, overlay, liveAuthoritative: true, DateTimeOffset.UtcNow)
            .Should().BeTrue();
        IsolatedOccupancy.IsCoinOpen("CELOUSDT", book, [], liveAuthoritative: true, DateTimeOffset.UtcNow)
            .Should().BeTrue();
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

    [Fact]
    public void PickLiveOwner_uses_the_earliest_fill_not_the_latest_started_bot()
    {
        var first = DateTimeOffset.UtcNow.AddHours(-1);
        var bollinger = new Bot
        {
            Name = "AKE Bollinger Reversion LOW Live",
            Symbol = "AKEUSDT",
            Status = BotStatus.Running,
            Mode = TradingMode.Live,
            StartedAt = first
        };
        var rsi = new Bot
        {
            Name = "AKE RSI Pullback LOW Live",
            Symbol = "AKEUSDT",
            Status = BotStatus.Running,
            Mode = TradingMode.Live,
            StartedAt = first.AddMilliseconds(140)
        };
        var book = new[]
        {
            new Position
            {
                BotId = bollinger.Id,
                Symbol = "AKEUSDT",
                Quantity = 139m,
                AverageEntryPrice = 0.043409m,
                OpenedAt = first.AddMinutes(48),
                Bot = bollinger
            }
        };

        IsolatedOccupancy.PickLiveOwner("AKEUSDT", [bollinger, rsi], book, TradingMode.Live)!.Id
            .Should().Be(bollinger.Id);
        IsolatedOccupancy.IsOwner(rsi, [bollinger, rsi], book).Should().BeFalse();
        IsolatedOccupancy.IsOwner(bollinger, [bollinger, rsi], book).Should().BeTrue();
    }

    [Fact]
    public void PickLiveOwner_lets_the_earliest_started_bot_enter_when_the_coin_is_flat()
    {
        var first = DateTimeOffset.UtcNow.AddHours(-1);
        var bollinger = new Bot
        {
            Name = "AKE Bollinger",
            Symbol = "AKEUSDT",
            Status = BotStatus.Running,
            Mode = TradingMode.Live,
            StartedAt = first
        };
        var rsi = new Bot
        {
            Name = "AKE RSI",
            Symbol = "AKEUSDT",
            Status = BotStatus.Running,
            Mode = TradingMode.Live,
            StartedAt = first.AddSeconds(1)
        };

        IsolatedOccupancy.PickLiveOwner("AKEUSDT", [rsi, bollinger], [], TradingMode.Live)!.Id
            .Should().Be(bollinger.Id);
    }

    [Fact]
    public void UniqueClosedTrips_keeps_the_binance_fill_when_two_bots_recorded_the_same_isolated_close()
    {
        var opened = DateTimeOffset.Parse("2026-09-23T09:44:36Z");
        var rows = new[]
        {
            new Trip("AKEUSDT", 139m, 0.043409m, 0.043799m, -0.05421m, 0.003m, opened.AddSeconds(1), opened.AddMinutes(2.4), "1ddfb4cb"),
            new Trip("AKEUSDT", 139m, 0.043673m, 0.044308m, -0.088265m, 0.006m, opened, opened.AddMinutes(2), "BNT418072822")
        };

        var unique = IsolatedOccupancy.UniqueClosedTrips(
            rows,
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => t.Fees);

        unique.Should().ContainSingle();
        unique[0].CorrelationId.Should().Be("BNT418072822");
        unique[0].PnL.Should().Be(-0.088265m);
    }

    [Fact]
    public void UniqueClosedTrips_keeps_two_sequential_round_trips_on_the_same_coin()
    {
        var firstOpen = DateTimeOffset.Parse("2026-09-23T09:00:00Z");
        var rows = new[]
        {
            new Trip("AKEUSDT", 139m, 0.043m, 0.044m, -0.09m, 0.006m, firstOpen, firstOpen.AddMinutes(3), "BNT1"),
            new Trip("AKEUSDT", 139m, 0.044m, 0.043m, 0.08m, 0.006m, firstOpen.AddMinutes(10), firstOpen.AddMinutes(14), "BNT2")
        };

        IsolatedOccupancy.UniqueClosedTrips(
            rows,
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => t.Fees).Should().HaveCount(2);
    }

    private sealed record Trip(
        string Symbol,
        decimal Quantity,
        decimal Entry,
        decimal Exit,
        decimal PnL,
        decimal Fees,
        DateTimeOffset OpenedAt,
        DateTimeOffset ClosedAt,
        string CorrelationId);

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

    private static Position Open(string symbol, Guid strategyId, DateTimeOffset? openedAt = null) =>
        new()
        {
            Symbol = symbol,
            Quantity = 1m,
            AverageEntryPrice = 100m,
            InitialRiskUsdt = 0.5m,
            OpenedAt = openedAt ?? DateTimeOffset.UtcNow,
            Bot = new Bot
            {
                Symbol = symbol,
                StrategyVersion = new StrategyVersion { StrategyId = strategyId }
            }
        };
}
