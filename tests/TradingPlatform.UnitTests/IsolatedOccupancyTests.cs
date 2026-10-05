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
    public void A_strategy_at_its_written_limit_does_not_receive_another_coin()
    {
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        var flat = LiveBot("AKEUSDT", "Flat Range", started);
        var impulse = LiveBot("AKEUSDT", "Impulse Catch", started.AddMinutes(1));
        var caps = Caps(flat, 5, impulse, 8);
        var used = new Dictionary<Guid, HashSet<string>>();
        for (var coin = 0; coin < 5; coin++)
        {
            IsolatedOccupancy.AddSlot(used, flat.StrategyVersion.StrategyId, "C" + coin);
        }

        IsolatedOccupancy.UsedSlots(used, flat.StrategyVersion.StrategyId).Should().Be(5);
        IsolatedOccupancy.TryReserveSlot(used, flat.StrategyVersion.StrategyId, "NEWUSDT", 5).Should().BeFalse();
        IsolatedOccupancy.PickOwnerForNewRow("AKEUSDT", [flat, impulse], [], caps, used, null)!.Id
            .Should().Be(impulse.Id);
    }

    [Fact]
    public void The_bot_that_placed_the_entry_keeps_the_coin_when_its_strategy_is_already_full()
    {
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        var flat = LiveBot("AKEUSDT", "Flat Range", started);
        var impulse = LiveBot("AKEUSDT", "Impulse Catch", started.AddMinutes(1));
        var caps = Caps(flat, 5, impulse, 1);
        var used = new Dictionary<Guid, HashSet<string>>();
        IsolatedOccupancy.AddSlot(used, impulse.StrategyVersion.StrategyId, "OTHERUSDT");

        IsolatedOccupancy.PickOwnerForNewRow("AKEUSDT", [flat, impulse], [], caps, used, impulse.Id)!.Id
            .Should().Be(impulse.Id);
        for (var coin = 0; coin < 5; coin++)
        {
            IsolatedOccupancy.AddSlot(used, flat.StrategyVersion.StrategyId, "F" + coin);
        }

        IsolatedOccupancy.PickOwnerForNewRow("AKEUSDT", [flat, impulse], [], caps, used, null)
            .Should().BeNull();
    }

    [Fact]
    public void An_adopted_row_returns_to_the_bot_that_placed_the_entry()
    {
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        var flat = LiveBot("AKEUSDT", "Flat Range", started);
        var impulse = LiveBot("AKEUSDT", "Impulse Catch", started.AddMinutes(1));
        var row = new Position
        {
            BotId = flat.Id,
            Bot = flat,
            Symbol = "AKEUSDT",
            Quantity = 10m,
            OpenedAt = started
        };

        IsolatedOccupancy.MoveRowToEntryBot(row, impulse, [row]).Should().BeTrue();
        row.BotId.Should().Be(impulse.Id);
    }

    private static Bot LiveBot(string symbol, string strategy, DateTimeOffset started)
    {
        var id = Guid.NewGuid();
        return new Bot
        {
            Name = strategy,
            Symbol = symbol,
            Status = BotStatus.Running,
            Mode = TradingMode.Live,
            StartedAt = started,
            StrategyVersion = new StrategyVersion
            {
                StrategyId = id,
                Strategy = new Strategy { Id = id, Name = strategy }
            },
            RiskProfile = new TradingPlatform.Domain.Risk.RiskProfile()
        };
    }

    private static Dictionary<Guid, LiveBotSlot> Caps(Bot first, int firstCap, Bot second, int secondCap) =>
        new()
        {
            [first.Id] = new LiveBotSlot(first.Id, first.Symbol, first.StrategyVersion.StrategyId, firstCap, first.StartedAt),
            [second.Id] = new LiveBotSlot(second.Id, second.Symbol, second.StrategyVersion.StrategyId, secondCap, second.StartedAt)
        };

    [Fact]
    public void PriorOwner_returns_the_bot_that_existed_when_the_fill_opened()
    {
        var opened = DateTimeOffset.Parse("2026-09-20T12:00:00Z");
        var adx = new Bot
        {
            Name = "AKE ADX",
            Symbol = "AKEUSDT",
            Status = BotStatus.Running,
            Mode = TradingMode.Live,
            CreatedAt = opened.AddDays(-2),
            StrategyVersion = new StrategyVersion { Strategy = new Strategy { Name = "ADX SMA Cross" } }
        };
        var flat = new Bot
        {
            Name = "AKE Flat Range",
            Symbol = "AKEUSDT",
            Status = BotStatus.Running,
            Mode = TradingMode.Live,
            CreatedAt = opened.AddDays(1),
            StrategyVersion = new StrategyVersion { Strategy = new Strategy { Name = "Flat Range" } }
        };

        IsolatedOccupancy.BotExistedAtOpen(flat.CreatedAt, opened).Should().BeFalse();
        IsolatedOccupancy.BotExistedAtOpen(adx.CreatedAt, opened).Should().BeTrue();
        IsolatedOccupancy.PriorOwner([flat, adx], "AKEUSDT", opened, flat.Id)!.Id.Should().Be(adx.Id);
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

    [Fact]
    public void UniqueClosedTrips_keeps_a_later_same_size_trip_that_only_overlaps_a_stale_window()
    {
        var firstOpen = DateTimeOffset.Parse("2026-09-26T08:00:00Z");
        var rows = new[]
        {
            new Trip("XPLUSDT", 210m, 0.12m, 0.11m, 2m, 0.02m, firstOpen, firstOpen.AddHours(4), "BNT-ghost"),
            new Trip("XPLUSDT", 210m, 0.11m, 0.12m, -1m, 0.02m, firstOpen.AddHours(1), firstOpen.AddHours(2), "BNT-next")
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

    [Fact]
    public void UniqueClosedTrips_drops_zero_quantity_shards_when_a_sized_fill_covers_them()
    {
        var opened = DateTimeOffset.Parse("2026-09-26T10:54:35Z");
        var closed = opened.AddHours(10);
        var rows = new[]
        {
            new Trip("XPLUSDT", 210m, 0.12104m, 0.11135m, 2.0349m, 0.0244m, opened, closed, "BNT325094579"),
            new Trip("XPLUSDT", 210m, 0.12104m, 0.11135m, 2.0349m, 0.0244m, opened, closed, "BNT325094579"),
            new Trip("XPLUSDT", 0m, 0m, 0m, 1.44381m, 0m, closed, closed, "BNT325094577"),
            new Trip("XPLUSDT", 0m, 0m, 0m, 0.44574m, 0m, closed, closed, "BNT325094578")
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
        unique[0].PnL.Should().Be(2.0349m);
        unique[0].Quantity.Should().Be(210m);
    }

    [Fact]
    public void UniqueClosedTrips_keeps_both_strategies_when_each_recorded_the_same_close()
    {
        var opened = DateTimeOffset.Parse("2026-09-27T02:50:08Z");
        var rows = new[]
        {
            new StrategyTrip("ESPUSDT", 10m, -0.35m, opened, opened.AddMinutes(90), "BNT-flow", "Flow Zone"),
            new StrategyTrip("ESPUSDT", 10m, -0.30m, opened.AddSeconds(1), opened.AddMinutes(92), "BNT-squeeze", "Squeeze Watch")
        };

        var unique = IsolatedOccupancy.UniqueClosedTrips(
            rows,
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => 0m,
            t => t.Strategy);

        unique.Select(row => row.Strategy).Should().BeEquivalentTo(["Flow Zone", "Squeeze Watch"]);
    }

    private sealed record StrategyTrip(
        string Symbol,
        decimal Quantity,
        decimal PnL,
        DateTimeOffset OpenedAt,
        DateTimeOffset ClosedAt,
        string CorrelationId,
        string Strategy);

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
