using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.MarketData;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class FillAccountingTests
{
    [Fact]
    public void Full_fill_books_the_executed_quantity_once()
    {
        var first = Apply(0m, 1m, OrderStatus.Filled, 1m, 100m, 0.4m);
        first.Status.Should().Be(OrderStatus.Filled);
        first.FilledQuantity.Should().Be(1m);
        first.RemainingQuantity.Should().Be(0m);
        first.NewFill!.Quantity.Should().Be(1m);
        first.NewFill.Fee.Should().Be(0.4m);
        first.BlocksNewEntries.Should().BeFalse();

        var repeat = FillAccounting.Apply(
            new BookedFill(first.FilledQuantity, 100m, 0.4m),
            1m,
            new ExchangeFillReport(OrderStatus.Filled, 1m, 100m, 0.4m, "ex-1", null));
        repeat.NewFill.Should().BeNull();
        repeat.AdditionalFee.Should().Be(0m);
        repeat.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void Cumulative_average_price_becomes_the_incremental_fill_price()
    {
        var first = FillAccounting.Apply(
            new BookedFill(0m, null, 0m),
            1m,
            new ExchangeFillReport(OrderStatus.PartiallyFilled, 0.4m, 100m, 0.1m, "ex-1", null));
        first.NewFill!.Quantity.Should().Be(0.4m);
        first.NewFill.Price.Should().Be(100m);

        var second = FillAccounting.Apply(
            new BookedFill(0.4m, 100m, 0.1m),
            1m,
            new ExchangeFillReport(OrderStatus.Filled, 1.0m, 110m, 0.25m, "ex-1", null));
        second.NewFill!.Quantity.Should().Be(0.6m);
        second.NewFill.Price.Should().BeApproximately(116.6666667m, 0.0000001m);
        second.NewFill.Fee.Should().Be(0.15m);
        second.AverageFillPrice.Should().Be(110m);

        var replay = FillAccounting.Apply(
            new BookedFill(1.0m, 110m, 0.25m),
            1m,
            new ExchangeFillReport(OrderStatus.Filled, 1.0m, 110m, 0.25m, "ex-1", null));
        replay.NewFill.Should().BeNull();
        replay.AdditionalFee.Should().Be(0m);
    }

    [Fact]
    public void Partial_fill_is_not_stored_as_filled()
    {
        var partial = Apply(0m, 1m, OrderStatus.PartiallyFilled, 0.4m, 100m, 0.1m);
        partial.Status.Should().Be(OrderStatus.PartiallyFilled);
        partial.FilledQuantity.Should().Be(0.4m);
        partial.RemainingQuantity.Should().Be(0.6m);
        partial.NewFill!.Quantity.Should().Be(0.4m);
        partial.BlocksNewEntries.Should().BeTrue();

        var labeledFull = Apply(0m, 1m, OrderStatus.Filled, 0.4m, 100m, 0.1m);
        labeledFull.Status.Should().Be(OrderStatus.PartiallyFilled);
        labeledFull.RemainingQuantity.Should().Be(0.6m);
    }

    [Fact]
    public void Cancel_after_a_partial_entry_keeps_only_the_executed_delta()
    {
        var partial = Apply(0m, 1m, OrderStatus.PartiallyFilled, 0.25m, 100m, 0.05m);
        var cancelled = Apply(partial.FilledQuantity, 1m, OrderStatus.Cancelled, 0.25m, 100m, 0.05m);
        cancelled.Status.Should().Be(OrderStatus.Cancelled);
        cancelled.NewFill.Should().BeNull();
        cancelled.FilledQuantity.Should().Be(0.25m);
        cancelled.RemainingQuantity.Should().Be(0.75m);
        cancelled.BlocksNewEntries.Should().BeFalse();
    }

    [Fact]
    public void Partial_exit_delta_is_the_new_executed_quantity_only()
    {
        var first = Apply(0m, 1m, OrderStatus.PartiallyFilled, 0.3m, 110m, 0.02m);
        var more = FillAccounting.Apply(
            new BookedFill(0.3m, 110m, 0.02m),
            1m,
            new ExchangeFillReport(OrderStatus.Filled, 1m, 112m, 0.08m, "ex-1", null));
        more.Status.Should().Be(OrderStatus.Filled);
        more.NewFill!.Quantity.Should().Be(0.7m);
        more.NewFill.Price.Should().BeApproximately(112.8571428m, 0.0000001m);
        more.AverageFillPrice.Should().Be(112m);
        more.NewFill.Fee.Should().Be(0.06m);
    }

    [Fact]
    public void Inconsistent_quote_and_average_and_a_backwards_fee_are_uncertain()
    {
        var quote = FillAccounting.Apply(
            new BookedFill(0m, null, 0m),
            1m,
            new ExchangeFillReport(OrderStatus.Filled, 1m, 100m, null, "ex-1", null, CumulativeQuote: 90m));
        quote.Uncertain.Should().BeTrue();
        quote.NewFill.Should().BeNull();

        var fee = FillAccounting.Apply(
            new BookedFill(1m, 100m, 0.4m),
            1m,
            new ExchangeFillReport(OrderStatus.Filled, 1m, 100m, 0.1m, "ex-1", null));
        fee.Uncertain.Should().BeTrue();

        var perFill = FillAccounting.Apply(
            new BookedFill(0.4m, 100m, 0m),
            1m,
            new ExchangeFillReport(OrderStatus.Filled, 1m, 110m, 0.05m, "ex-1", null, FeeIsCumulative: false));
        perFill.NewFill!.Fee.Should().Be(0.05m);
        perFill.NewFill.FeeKnown.Should().BeTrue();

        var missing = Apply(0m, 1m, OrderStatus.Filled, 1m, 100m, null);
        missing.NewFill!.FeeKnown.Should().BeFalse();
    }

    [Theory]
    [InlineData(OrderStatus.New, 0)]
    [InlineData(OrderStatus.Submitted, 0)]
    [InlineData(OrderStatus.Rejected, 0)]
    [InlineData(OrderStatus.Expired, 0)]
    [InlineData(OrderStatus.Failed, 0)]
    public void Resting_and_terminal_statuses_do_not_invent_a_fill(OrderStatus status, int executed)
    {
        var result = Apply(0m, 1m, status, executed, null, 0m);
        result.NewFill.Should().BeNull();
        result.Status.Should().Be(status);
    }

    [Fact]
    public void Unknown_or_contradictory_exchange_data_is_uncertain_and_does_not_move_the_position()
    {
        var unknown = Apply(0.2m, 1m, OrderStatus.Uncertain, 0.2m, 100m, 0m);
        unknown.Uncertain.Should().BeTrue();
        unknown.NewFill.Should().BeNull();
        unknown.FilledQuantity.Should().Be(0.2m);
        unknown.BlocksNewEntries.Should().BeTrue();

        var backwards = Apply(0.5m, 1m, OrderStatus.Filled, 0.2m, 100m, 0m);
        backwards.Uncertain.Should().BeTrue();
        backwards.NewFill.Should().BeNull();

        var over = Apply(0m, 1m, OrderStatus.Filled, 2m, 100m, 0m);
        over.Uncertain.Should().BeTrue();

        var noPrice = Apply(0m, 1m, OrderStatus.Filled, 1m, null, 0m);
        noPrice.Uncertain.Should().BeTrue();
        noPrice.NewFill.Should().BeNull();
    }

    private static FillApplication Apply(
        decimal previous,
        decimal requested,
        OrderStatus status,
        decimal executed,
        decimal? price,
        decimal? fee) =>
        FillAccounting.Apply(previous, requested, new ExchangeFillReport(status, executed, price, fee, "ex-1", null));
}

public sealed class OrderRecoveryTests
{
    [Fact]
    public void Timeout_before_acceptance_does_not_resubmit()
    {
        var ambiguous = OrderRecovery.Decide(OrderLookup.Unavailable("timeout"));
        ambiguous.Kind.Should().Be(RecoveryKind.LookupUnavailable);
        ambiguous.Order.Should().BeNull();

        var absent = OrderRecovery.Decide(OrderLookup.Absent("Binance confirmed this client id does not exist. The order was not sent again."));
        absent.Kind.Should().Be(RecoveryKind.ConfirmedAbsent);
        absent.Reason.Should().Contain("not sent again");
    }

    [Fact]
    public void Timeout_after_acceptance_uses_the_existing_order()
    {
        var existing = Sample(OrderStatus.Submitted, 0m);
        var decision = OrderRecovery.Decide(OrderLookup.Found(existing));
        decision.Kind.Should().Be(RecoveryKind.Confirmed);
        decision.Order.Should().BeSameAs(existing);
    }

    [Fact]
    public void Lost_response_after_a_partial_or_full_fill_is_confirmed_once()
    {
        var partial = OrderRecovery.Decide(OrderLookup.Found(Sample(OrderStatus.PartiallyFilled, 0.4m)));
        var applied = FillAccounting.Apply(0m, 1m, new ExchangeFillReport(
            partial.Order!.Status, partial.Order.FilledQuantity, 100m, 0.1m, partial.Order.ExchangeOrderId, null));
        applied.NewFill!.Quantity.Should().Be(0.4m);
        FillAccounting.Apply(applied.FilledQuantity, 1m, new ExchangeFillReport(
            partial.Order.Status, partial.Order.FilledQuantity, 100m, 0.1m, partial.Order.ExchangeOrderId, null))
            .NewFill.Should().BeNull();

        var full = OrderRecovery.Decide(OrderLookup.Found(Sample(OrderStatus.Filled, 1m)));
        var booked = FillAccounting.Apply(0m, 1m, new ExchangeFillReport(
            full.Order!.Status, full.Order.FilledQuantity, 101m, 0.2m, full.Order.ExchangeOrderId, null));
        booked.Status.Should().Be(OrderStatus.Filled);
        booked.NewFill!.Quantity.Should().Be(1m);
    }

    [Fact]
    public void Lookup_unavailable_stays_uncertain_and_a_later_fill_is_applied_once()
    {
        var missed = OrderRecovery.Decide(OrderLookup.Unavailable("429"));
        missed.Kind.Should().Be(RecoveryKind.LookupUnavailable);
        var uncertain = FillAccounting.Apply(0m, 1m, new ExchangeFillReport(OrderStatus.Uncertain, 0m, null, 0m, null, missed.Reason));
        uncertain.Uncertain.Should().BeTrue();
        uncertain.NewFill.Should().BeNull();

        var late = FillAccounting.Apply(uncertain.FilledQuantity, 1m, new ExchangeFillReport(OrderStatus.Filled, 1m, 99m, 0.3m, "ex-late", null));
        late.NewFill!.Quantity.Should().Be(1m);
        FillAccounting.Apply(late.FilledQuantity, 1m, new ExchangeFillReport(OrderStatus.Filled, 1m, 99m, 0.3m, "ex-late", null))
            .NewFill.Should().BeNull();

        OrderRecovery.Decide(OrderLookup.Unavailable("5xx")).Kind.Should().Be(missed.Kind);
        var created = DateTimeOffset.UnixEpoch;
        OrderRecovery.Due(created, created, created.AddSeconds(4)).Should().BeFalse();
        OrderRecovery.Due(created, created, created.AddSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public void Exit_booking_matches_for_a_partial_and_a_full_close()
    {
        var partial = PositionFillBook.Exit(PositionSide.Long, 1m, 100m, 0.4m, 110m, 0.1m);
        partial.Closed.Should().BeFalse();
        partial.RemainingQuantity.Should().Be(0.6m);
        partial.RealizedPnl.Should().Be(3.9m);
        partial.EventType.Should().Be("PARTIAL_CLOSE");

        var full = PositionFillBook.Exit(PositionSide.Long, 0.6m, 100m, 0.6m, 90m, 0.2m);
        full.Closed.Should().BeTrue();
        full.RemainingQuantity.Should().Be(0m);
        full.RealizedPnl.Should().Be(-6.2m);
    }

    private static ExchangeOrder Sample(OrderStatus status, decimal filled) =>
        new("client-1", "ex-1", "BTCUSDT", OrderSide.Buy, OrderType.Market, status, 1m, filled, 100m, 100m, DateTimeOffset.UnixEpoch);
}

public sealed class LiveEntryGateTests
{
    [Theory]
    [InlineData(false, false, true, false, true, true, true)]
    [InlineData(true, true, true, false, true, true, true)]
    [InlineData(true, false, false, false, true, true, true)]
    [InlineData(true, false, true, true, true, true, true)]
    [InlineData(true, false, true, false, false, true, true)]
    [InlineData(true, false, true, false, true, false, true)]
    [InlineData(true, false, true, false, true, true, false)]
    public void Each_failed_condition_blocks_a_new_live_entry(
        bool enabled,
        bool kill,
        bool fresh,
        bool blocked,
        bool strategy,
        bool risk,
        bool filters)
    {
        LiveEntryGate.Block(Facts(enabled, kill, fresh, blocked, strategy, risk, filters, false))
            .Should().NotBeNull();
    }

    [Fact]
    public void A_complete_live_entry_is_allowed_and_an_exit_is_allowed_while_entries_are_blocked()
    {
        LiveEntryGate.Block(Facts(true, false, true, false, true, true, true, false)).Should().BeNull();
        LiveEntryGate.Block(Facts(false, true, false, true, false, false, false, true)).Should().BeNull();
        LiveEntryGate.BlockNewEntry(TradingMode.Paper, false).Should().BeNull();
        LiveEntryGate.BlockNewEntry(TradingMode.Live, false).Should().NotBeNull();
    }

    private static LiveEntryFacts Facts(
        bool enabled,
        bool kill,
        bool fresh,
        bool blocked,
        bool strategy,
        bool risk,
        bool filters,
        bool reducing) =>
        new(TradingMode.Live, enabled, kill, fresh, blocked, strategy, risk, filters, reducing);
}

public sealed class LiveRiskBoundaryTests
{
    [Fact]
    public void Exact_limits_pass_and_the_next_step_fails()
    {
        var profile = Profile();
        Reject(profile, Facts()).Should().BeNull();
        Reject(profile, Facts() with { RequestedRiskPercent = 0.51m }).Should().Contain("Risk per trade");
        Reject(profile, Facts() with { OpenRiskPercent = 4m }).Should().Contain("exposure");
        Reject(profile, Facts() with { OpenPositions = 2 }).Should().Contain("Position count");
        Reject(profile, Facts() with { PositionsOnSymbol = 1 }).Should().Contain("already has");
        Reject(profile, Facts() with { DailyRealizedPnl = -30m }).Should().Contain("Daily realized");
        Reject(profile, Facts() with { DailyRealizedPnl = -29.99m }).Should().BeNull();
        Reject(profile, Facts() with { Leverage = 3.01m }).Should().Contain("Leverage");
        Reject(profile, Facts() with { Quantity = 0.0009m }).Should().Contain("Quantity");
        Reject(profile, Facts() with { Notional = 4.99m }).Should().Contain("Notional");
        Reject(profile, Facts() with { AvailableMargin = 9.99m }).Should().Contain("margin");
    }

    [Fact]
    public void Missing_inputs_and_invalid_profiles_fail_closed()
    {
        Reject(Profile(), Facts() with { Equity = 0m }).Should().Contain("Equity is missing");
        Reject(Profile(), Facts() with { AvailableMargin = null }).Should().Contain("Available margin is missing");
        Reject(Profile(), Facts() with { DrawdownKnown = false }).Should().Contain("drawdown");
        Reject(Profile(), Facts() with { KillSwitch = true }).Should().Contain("Kill switch");
        Reject(Profile(), Facts() with { StopDistancePercent = 0m }).Should().Contain("Stop distance");
        Reject(Profile(), Facts() with
        {
            ConsecutiveLosses = 5,
            LastLossAt = DateTimeOffset.UnixEpoch,
            UtcNow = DateTimeOffset.UnixEpoch.AddMinutes(10)
        }).Should().Contain("cooldown");

        RiskLiveGuard.ProfileProblem(TradingMode.Live, null).Should().Contain("missing");
        RiskLiveGuard.ProfileProblem(TradingMode.Live, new RiskProfile { AllowLive = false }).Should().Contain("not allowed");
        RiskLiveGuard.ProfileProblem(TradingMode.Live, new RiskProfile { AllowLive = true, RiskPerTradePercent = 0m })
            .Should().Contain("Risk per trade");
        RiskLiveGuard.ProfileProblem(TradingMode.Paper, new RiskProfile { AllowLive = false }).Should().BeNull();
    }

    private static RiskProfile Profile() => new()
    {
        Name = "Bounded",
        AllowLive = true,
        RiskPerTradePercent = 0.5m,
        StopLossPercent = 2m,
        MaxLeverage = 3m,
        MaxDailyLossPercent = 3m,
        MaxPortfolioRiskPercent = 4m,
        MaxSimultaneousPositions = 2,
        MaxConsecutiveLosses = 5,
        CooldownMinutes = 30
    };

    private static LiveRiskFacts Facts() => new(
        1_000m, 0m, 0, 0, 0m, 0.5m, 3m, 2m, 0.01m, 100m, 0.001m, 5m, 500m, 10m, false, 0, null, DateTimeOffset.UnixEpoch, true);

    private static string? Reject(RiskProfile profile, LiveRiskFacts facts) =>
        RiskLiveGuard.Reject(TradingMode.Live, profile, facts);
}

public sealed class ReconciliationSafetyTests
{
    [Fact]
    public void A_successful_snapshot_expires_and_a_failure_blocks_entries()
    {
        var state = new ReconciliationState();
        var at = DateTimeOffset.UnixEpoch.AddHours(1);
        state.IsFresh(at, TimeSpan.FromSeconds(90)).Should().BeFalse();
        state.Succeed("account-a", at);
        state.AccountId.Should().Be("account-a");
        state.IsFresh(at.AddSeconds(90), TimeSpan.FromSeconds(90)).Should().BeTrue();
        state.IsFresh(at.AddSeconds(91), TimeSpan.FromSeconds(90)).Should().BeFalse();
        state.Fail("snapshot incomplete", at.AddMinutes(1));
        state.IsFresh(at.AddMinutes(1), TimeSpan.FromMinutes(5)).Should().BeFalse();
        state.BlockReason.Should().Contain("incomplete");
    }

    [Fact]
    public async Task Stale_snapshot_does_not_close_a_local_position_or_invent_a_fill()
    {
        await using var db = await SeedLivePositionAsync();
        var live = new LiveAccountCache();
        var state = new ReconciliationState();
        var reconciler = Reconciler(db, live, state);
        await reconciler.ReconcileAsync();

        state.Succeeded.Should().BeFalse();
        state.BlockReason.Should().Contain("incomplete");
        (await db.Orders.CountAsync()).Should().Be(0);
        (await db.Positions.SingleAsync()).Quantity.Should().Be(0.01m);
    }

    [Fact]
    public async Task Unknown_exchange_position_and_order_block_entries_without_closing_them()
    {
        await using var db = await SeedLivePositionAsync(includePosition: false);
        var live = new LiveAccountCache();
        live.Set(FreshBook(
            [new LiveOpenPosition("ETHUSDT", "Long", 1m, 2_000m, 2_010m, 1m, "Futures")],
            [new LiveOpenOrder("ETHUSDT", "Buy", "LIMIT", "NEW", 1m, 0m, 2_000m, "99", "unknown-client", DateTimeOffset.UtcNow, "Futures")]));
        var state = new ReconciliationState();
        await Reconciler(db, live, state).ReconcileAsync();

        state.BlockReason.Should().Contain("position ETHUSDT");
        state.BlockReason.Should().Contain("order ETHUSDT");
        (await db.Orders.CountAsync()).Should().Be(0);
        (await db.Positions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Ghost_local_position_stays_open_and_is_not_given_an_exchange_fill()
    {
        await using var db = await SeedLivePositionAsync();
        var live = new LiveAccountCache();
        live.Set(FreshBook([], []));
        var state = new ReconciliationState();
        await Reconciler(db, live, state).ReconcileAsync();

        state.Succeeded.Should().BeFalse();
        state.BlockReason.Should().Contain("left open");
        (await db.Orders.CountAsync()).Should().Be(0);
        var position = await db.Positions.SingleAsync();
        position.Quantity.Should().Be(0.01m);
        position.RealizedPnL.Should().Be(0m);
        position.ClosedAt.Should().BeNull();
    }

    [Fact]
    public async Task An_unresolved_entry_blocks_the_same_bot_and_coin()
    {
        await using var db = await SeedLivePositionAsync(includePosition: false);
        var bot = await db.Bots.SingleAsync();
        db.Orders.Add(new Order
        {
            BotId = bot.Id,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Status = OrderStatus.Uncertain,
            Quantity = 0.01m,
            ClientOrderId = "client-uncertain",
            Mode = TradingMode.Live
        });
        await db.SaveChangesAsync();
        var store = new TradingStore(db);
        (await store.HasUnresolvedEntryAsync(bot.Id, "BTCUSDT")).Should().BeTrue();
        (await store.HasUnresolvedEntryAsync(bot.Id, "ETHUSDT")).Should().BeFalse();
    }

    private static LiveIsolatedReconciler Reconciler(
        TradingDbContext db,
        LiveAccountCache live,
        ReconciliationState state,
        MarketDataCache? cache = null) =>
        new(
            new TradingStore(db),
            live,
            cache ?? new MarketDataCache(),
            new SystemClock(),
            new CorrelationIdAccessor(),
            NullLogger<LiveIsolatedReconciler>.Instance,
            state);

    private static LiveAccountSnapshot FreshBook(
        IReadOnlyList<LiveOpenPosition> positions,
        IReadOnlyList<LiveOpenOrder> orders) => new()
    {
        HasKeys = true,
        FuturesBookFresh = true,
        UpdatedAt = DateTimeOffset.UtcNow,
        ApiKeyHint = "hint",
        OpenPositions = positions,
        OpenOrders = orders
    };

    private static async Task<TradingDbContext> SeedLivePositionAsync(bool includePosition = true)
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TradingDbContext(options);
        var user = new User { Email = "ops@localhost", NormalizedEmail = "OPS@LOCALHOST", DisplayName = "Ops", PasswordHash = "x" };
        var strategy = new Strategy { User = user, UserId = user.Id, Name = "Canonical", TemplateKey = "ema_rsi_trend", IsEnabled = true };
        var version = new StrategyVersion
        {
            Strategy = strategy,
            VersionNumber = 1,
            DefinitionJson = """{"template":"ema_rsi_trend","timeframe":"15m","params":{}}""",
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.FifteenMinutes
        };
        strategy.Versions.Add(version);
        var account = new ExchangeAccount { User = user, UserId = user.Id, Name = "Live", ApiKeyFingerprint = "hint" };
        var bot = new Bot
        {
            User = user,
            UserId = user.Id,
            ExchangeAccount = account,
            StrategyVersion = version,
            Name = "BTC",
            Status = BotStatus.Running,
            Mode = TradingMode.Live,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.FifteenMinutes
        };
        db.AddRange(user, strategy, account, bot);
        if (includePosition)
        {
            db.Positions.Add(new Position
            {
                Bot = bot,
                Symbol = "BTCUSDT",
                Side = PositionSide.Long,
                Quantity = 0.01m,
                AverageEntryPrice = 50_000m,
                CurrentPrice = 50_100m,
                OpenedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
        }

        await db.SaveChangesAsync();
        return db;
    }
}

public sealed class CanonicalDispatchTests
{
    [Fact]
    public void Canonical_and_obsolete_ids_do_not_use_the_imported_evaluator()
    {
        var candles = new List<MarketCandle>
        {
            Candle(100m),
            Candle(101m)
        };
        var context = new StrategyContext { ClosedCandles = candles };
        var cache = new CausalIndicatorCache(candles);
        var canonical = ImportedRuleEvaluator.Evaluate(
            StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatch, false),
            candles,
            1,
            context,
            cache);
        var alias = AdvancedStrategyEvaluator.Evaluate(
            StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ImpulseCatch, false) with { TemplateKey = "impulse_catch_v2" },
            candles,
            1,
            context,
            cache);

        canonical.Status.Should().NotBe("IMPORTED_RULE");
        alias.Reason.Should().Contain("retired implementation is not executed");
        alias.Signal.Should().Be(SignalType.NoAction);
    }

    private static MarketCandle Candle(decimal price) => new()
    {
        Open = price,
        High = price,
        Low = price,
        Close = price,
        Volume = 1,
        IsClosed = true,
        OpenTime = DateTimeOffset.UnixEpoch,
        CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(15)
    };
}
