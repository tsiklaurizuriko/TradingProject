using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Binance;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Execution;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.MarketData;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

/// <summary>Fault injection around live protective stops and the flatten-all path. No real exchange is called.</summary>
public sealed class ProtectionFailureTests
{
    private const decimal Mark = 50_100m;

    [Fact]
    public async Task Stop_that_keeps_failing_on_an_existing_position_is_flattened_after_the_configured_cycles()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        harness.Connector.FailStopsAt = _ => true;
        var options = harness.Options(o => o.UnprotectedCyclesBeforeFlatten = 2);

        await harness.Engine(options).EvaluateRunningBotsAsync();

        harness.Connector.Placed.Should().BeEmpty("one failed cycle is not enough to close an existing position");
        harness.Connector.StopAttempts.Should().BeGreaterThanOrEqualTo(3, "the stop is retried within the cycle");

        await harness.Engine(options).EvaluateRunningBotsAsync();

        harness.Connector.Placed.Should().ContainSingle();
        var close = harness.Connector.Placed[0];
        close.ReduceOnly.Should().BeTrue();
        close.Side.Should().Be(OrderSide.Sell);
        close.Quantity.Should().Be(0.01m);
        (await harness.Db.Signals.SingleAsync()).Reason.Should().StartWith("No working stop");
        (await harness.Db.Positions.CountAsync(p => p.ClosedAt == null)).Should().Be(0);
    }

    [Fact]
    public async Task Stop_that_succeeds_on_a_retry_does_not_close_the_position()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var calls = 0;
        harness.Connector.FailStopsAt = _ => ++calls < 3;
        var options = harness.Options(o => o.UnprotectedCyclesBeforeFlatten = 1);

        await harness.Engine(options).EvaluateRunningBotsAsync();

        harness.Connector.StopAttempts.Should().Be(3);
        harness.Connector.Placed.Should().BeEmpty();
        (await harness.Db.Positions.CountAsync(p => p.ClosedAt == null)).Should().Be(1);
    }

    [Fact]
    public async Task Flatten_on_protection_failure_can_be_turned_off()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        harness.Connector.FailStopsAt = _ => true;
        var options = harness.Options(o =>
        {
            o.UnprotectedCyclesBeforeFlatten = 1;
            o.FlattenOnProtectionFailure = false;
        });

        await harness.Engine(options).EvaluateRunningBotsAsync();

        harness.Connector.Placed.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_stop_tighten_puts_the_previous_stop_back_and_keeps_the_position()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 5m, listedStop: 47_500m);
        harness.Connector.FailStopsAt = trigger => trigger != 47_500m;
        var options = harness.Options(o => o.UnprotectedCyclesBeforeFlatten = 1);

        await harness.Engine(options).EvaluateRunningBotsAsync();

        harness.Connector.StopTriggers.Should().Contain(47_500m, "the old stop is restored after the new one fails");
        harness.Connector.Placed.Should().BeEmpty();
        var position = await harness.Db.Positions.SingleAsync();
        position.ClosedAt.Should().BeNull();
        position.StopLossPrice.Should().Be(47_500m);
    }

    [Fact]
    public async Task Failed_stop_tighten_that_cannot_restore_the_old_stop_closes_at_once()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 5m, listedStop: 47_500m);
        harness.Connector.FailStopsAt = _ => true;
        var options = harness.Options(o => o.UnprotectedCyclesBeforeFlatten = 5);

        await harness.Engine(options).EvaluateRunningBotsAsync();

        harness.Connector.Placed.Should().ContainSingle(order => order.ReduceOnly && order.Side == OrderSide.Sell);
    }

    [Fact]
    public async Task Flatten_all_stops_bots_closes_local_and_exchange_only_positions_and_verifies()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        harness.Connector.ExchangePositions.Add(new ExchangePosition("BTCUSDT", PositionSide.Long, 0.01m, 50_000m, Mark));
        harness.Connector.ExchangePositions.Add(new ExchangePosition("ETHUSDT", PositionSide.Short, 0.5m, 3_000m, 3_010m));

        var report = await harness.Engine(harness.Options()).FlattenAllAsync("test");

        report.BotsStopped.Should().Be(1);
        report.LocalPositionsClosed.Should().Be(1);
        report.ExchangeClosesSent.Should().Be(1, "only ETHUSDT was left after the local close");
        report.Flat.Should().BeTrue();
        harness.Connector.Placed.Should().OnlyContain(order => order.ReduceOnly);
        harness.Connector.Placed.Should().Contain(order => order.Symbol == "ETHUSDT" && order.Side == OrderSide.Buy && order.Quantity == 0.5m);
        harness.Connector.CancelledAll.Should().Contain("ETHUSDT");
        harness.Bot.Status.Should().Be(BotStatus.Stopped);
        (await harness.Db.Positions.CountAsync(p => p.ClosedAt == null)).Should().Be(0);

        var again = await harness.Engine(harness.Options()).FlattenAllAsync("test again");

        again.Flat.Should().BeTrue();
        again.LocalPositionsClosed.Should().Be(0);
        again.ExchangeClosesSent.Should().Be(0);
        harness.Connector.Placed.Should().HaveCount(2, "a second flatten has nothing left to send");
    }

    [Fact]
    public async Task Flatten_all_reports_what_is_still_open_when_a_close_fails()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        harness.Connector.ExchangePositions.Add(new ExchangePosition("BTCUSDT", PositionSide.Long, 0.01m, 50_000m, Mark));
        harness.Connector.ExchangePositions.Add(new ExchangePosition("ETHUSDT", PositionSide.Short, 0.5m, 3_000m, 3_010m));
        harness.Connector.RejectOrdersFor.Add("ETHUSDT");

        var report = await harness.Engine(harness.Options()).FlattenAllAsync("test");

        report.Flat.Should().BeFalse();
        report.LocalPositionsClosed.Should().Be(1);
        report.Failures.Should().Contain(row => row.Contains("ETHUSDT"));
        report.Remaining.Should().ContainSingle(row => row.StartsWith("ETHUSDT"));
    }

    [Theory]
    [InlineData(0.2, "USDT", 0.8)]
    [InlineData(0.0, "BNB", 1.0)]
    public async Task Manual_close_books_gross_pnl_and_a_usdt_net(double fee, string asset, double expectedNet)
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        harness.Connector.FillFee = ((decimal)fee, asset);
        var position = await harness.Db.Positions.SingleAsync();

        await harness.Engine(harness.Options()).ClosePositionAsync(position.Id);

        var trade = await harness.Db.Trades.SingleAsync();
        trade.PnL.Should().Be(1m, "gross is 0.01 BTC x (50,100 - 50,000)");
        trade.NetPnL.Should().Be((decimal)expectedNet);
        (await harness.Db.Positions.SingleAsync()).RealizedPnL.Should().Be(1m);
    }

    [Fact]
    public async Task Manual_close_with_a_bnb_fee_keeps_gross_and_leaves_net_pending()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        harness.Connector.FillFee = (0.0004m, "BNB");
        var position = await harness.Db.Positions.SingleAsync();

        await harness.Engine(harness.Options()).ClosePositionAsync(position.Id);

        var trade = await harness.Db.Trades.SingleAsync();
        trade.PnL.Should().Be(1m, "a BNB fee is never subtracted from a USDT figure");
        trade.NetPnL.Should().BeNull();
        trade.FeeAsset.Should().Be("BNB");
        trade.Fees.Should().Be(0.0004m);
    }

    [Fact]
    public async Task Manual_close_before_the_commission_is_reported_leaves_net_pending()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var position = await harness.Db.Positions.SingleAsync();

        await harness.Engine(harness.Options()).ClosePositionAsync(position.Id);

        var trade = await harness.Db.Trades.SingleAsync();
        trade.PnL.Should().Be(1m);
        trade.NetPnL.Should().BeNull("an unreported fee is not a zero fee");
        trade.FeeStatus.Should().Be(FeeKnowledge.Unknown);
    }

    [Fact]
    public void Flatten_client_order_id_is_stable_within_a_minute_and_fits_binance()
    {
        var account = Guid.NewGuid();
        var first = BotEngine.FlattenClientOrderId(account, "btcusdt", PositionSide.Long, 29_000_000);
        var again = BotEngine.FlattenClientOrderId(account, "BTCUSDT", PositionSide.Long, 29_000_000);
        var other = BotEngine.FlattenClientOrderId(account, "BTCUSDT", PositionSide.Short, 29_000_000);

        again.Should().Be(first);
        other.Should().NotBe(first);
        first.Length.Should().BeLessThanOrEqualTo(36);
        first.Should().MatchRegex("^[A-Za-z0-9]+$");
    }

    [Fact]
    public void Manual_close_client_order_id_is_stable_per_position_size_and_minute()
    {
        var position = Guid.NewGuid();
        var first = BotEngine.ManualCloseClientOrderId(position, 0.01m, 29_000_000);

        BotEngine.ManualCloseClientOrderId(position, 0.010m, 29_000_000).Should().Be(first);
        BotEngine.ManualCloseClientOrderId(position, 0.02m, 29_000_000).Should().NotBe(first);
        BotEngine.ManualCloseClientOrderId(position, 0.01m, 29_000_001).Should().NotBe(first);
        BotEngine.ManualCloseClientOrderId(Guid.NewGuid(), 0.01m, 29_000_000).Should().NotBe(first);
        (first + "R").Length.Should().BeLessThanOrEqualTo(36);
        first.Should().MatchRegex("^[A-Za-z0-9]+$");
    }

    [Fact]
    public async Task Manual_close_is_refused_while_an_earlier_close_is_unresolved()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var position = await harness.Db.Positions.SingleAsync();
        harness.Db.Orders.Add(harness.Order("MCearlier", OrderSide.Sell, OrderStatus.Uncertain, DateTimeOffset.UtcNow));
        await harness.Db.SaveChangesAsync();

        var act = () => harness.Engine(harness.Options()).ClosePositionAsync(position.Id);

        await act.Should().ThrowAsync<Domain.Errors.DomainException>().WithMessage("*already in flight*");
        harness.Connector.Placed.Should().BeEmpty("a second reduce-only market order would be sent blind");
        (await harness.Db.Positions.SingleAsync()).IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Manual_close_still_runs_when_only_an_entry_is_unresolved()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var position = await harness.Db.Positions.SingleAsync();
        harness.Db.Orders.Add(harness.Order("entry-1", OrderSide.Buy, OrderStatus.Uncertain, DateTimeOffset.UtcNow));
        await harness.Db.SaveChangesAsync();

        await harness.Engine(harness.Options()).ClosePositionAsync(position.Id);

        harness.Connector.Placed.Should().ContainSingle(order => order.ReduceOnly);
    }

    [Fact]
    public async Task Manual_close_after_a_rejected_close_in_the_same_minute_gets_a_fresh_id()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var position = await harness.Db.Positions.SingleAsync();
        var id = BotEngine.ManualCloseClientOrderId(position.Id, position.Quantity, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60);
        harness.Db.Orders.Add(harness.Order(id, OrderSide.Sell, OrderStatus.Failed, DateTimeOffset.UtcNow));
        await harness.Db.SaveChangesAsync();

        await harness.Engine(harness.Options()).ClosePositionAsync(position.Id);

        harness.Connector.Placed.Should().ContainSingle().Which.ClientOrderId.Should().Be(id + "R");
    }

    [Fact]
    public async Task Absent_order_inside_the_verify_window_stays_uncertain()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var order = await harness.SeedStaleOrderAsync(submittedAgo: TimeSpan.FromSeconds(40));

        await harness.Engine(harness.Options()).EvaluateRunningBotsAsync();

        order.Status.Should().Be(OrderStatus.Uncertain, "a timed-out submit can still appear on Binance");
        order.RejectReason.Should().Contain("Re-checking");
    }

    [Fact]
    public async Task Absent_order_after_the_verify_window_is_failed()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var order = await harness.SeedStaleOrderAsync(submittedAgo: TimeSpan.FromMinutes(4));

        await harness.Engine(harness.Options()).EvaluateRunningBotsAsync();

        order.Status.Should().Be(OrderStatus.Failed);
    }

    [Fact]
    public async Task Absent_order_is_not_failed_while_binance_holds_an_unbooked_position()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var order = await harness.SeedStaleOrderAsync(submittedAgo: TimeSpan.FromMinutes(4), symbol: "ETHUSDT");
        harness.Connector.ExchangePositions.Add(new ExchangePosition("ETHUSDT", PositionSide.Long, 1m, 3_000m, 3_000m));

        await harness.Engine(harness.Options()).EvaluateRunningBotsAsync();

        order.Status.Should().Be(OrderStatus.Uncertain);
        order.RejectReason.Should().Contain("not booked");
    }

    [Fact]
    public async Task Crash_after_binance_filled_a_close_books_it_on_restart_without_resending()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var order = await harness.SeedStaleOrderAsync(TimeSpan.FromSeconds(30), side: OrderSide.Sell, status: OrderStatus.Submitting);
        harness.Connector.OnExchange[order.ClientOrderId] = Filled(order, 0.01m, (0.2m, "USDT"));
        harness.GoFlatOnExchange();

        await harness.Engine(harness.Options()).EvaluateRunningBotsAsync();

        order.Status.Should().Be(OrderStatus.Filled);
        harness.Connector.Placed.Should().BeEmpty("recovery looks the order up; it never sends it again");
        var position = await harness.Db.Positions.SingleAsync();
        position.IsOpen.Should().BeFalse();
        var trade = await harness.Db.Trades.SingleAsync();
        trade.PnL.Should().Be(1m);
        trade.Fees.Should().Be(0.2m);
    }

    [Fact]
    public async Task Crash_recovery_run_twice_books_the_close_once()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var order = await harness.SeedStaleOrderAsync(TimeSpan.FromSeconds(30), side: OrderSide.Sell, status: OrderStatus.Submitting);
        harness.Connector.OnExchange[order.ClientOrderId] = Filled(order, 0.01m, (0.2m, "USDT"));
        harness.GoFlatOnExchange();

        await harness.Engine(harness.Options()).EvaluateRunningBotsAsync();
        order.Status = OrderStatus.Uncertain;
        order.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await harness.Engine(harness.Options()).EvaluateRunningBotsAsync();

        (await harness.Db.Trades.CountAsync()).Should().Be(1);
        harness.Connector.Placed.Should().BeEmpty();
    }

    [Fact]
    public async Task Crash_after_a_partial_close_keeps_the_rest_open()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var order = await harness.SeedStaleOrderAsync(TimeSpan.FromSeconds(30), side: OrderSide.Sell, status: OrderStatus.Submitting);
        harness.Connector.OnExchange[order.ClientOrderId] = Filled(order, 0.004m, (0.08m, "USDT")) with { Status = OrderStatus.PartiallyFilled };

        await harness.Engine(harness.Options()).EvaluateRunningBotsAsync();

        order.Status.Should().Be(OrderStatus.PartiallyFilled);
        order.FilledQuantity.Should().Be(0.004m);
        var position = await harness.Db.Positions.SingleAsync();
        position.IsOpen.Should().BeTrue();
        position.Quantity.Should().Be(0.006m);
        harness.Connector.Placed.Should().BeEmpty();
    }

    [Fact]
    public async Task Crash_before_the_submit_reached_binance_is_failed_after_the_window_and_not_resent()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var order = await harness.SeedStaleOrderAsync(TimeSpan.FromMinutes(4), side: OrderSide.Sell, status: OrderStatus.Submitting);

        await harness.Engine(harness.Options()).EvaluateRunningBotsAsync();

        order.Status.Should().Be(OrderStatus.Failed);
        harness.Connector.Placed.Should().BeEmpty();
        (await harness.Db.Positions.SingleAsync()).IsOpen.Should().BeTrue("the close never happened, so the position is still on");
    }

    private static ExchangeOrder Filled(Order order, decimal filled, (decimal Amount, string Asset) fee) => new(
        order.ClientOrderId,
        "LIVE-crash",
        order.Symbol,
        order.Side,
        order.Type,
        OrderStatus.Filled,
        order.Quantity,
        filled,
        Mark,
        Mark,
        DateTimeOffset.UtcNow,
        fee.Amount,
        true,
        null,
        fee.Asset);

    [Theory]
    [InlineData(10, false, false)]
    [InlineData(10, true, true)]
    [InlineData(181, false, true)]
    public void Absent_lookup_is_final_only_after_a_rejection_or_the_window(int secondsAgo, bool rejected, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        Exception? error = rejected
            ? new Domain.Errors.DomainException("ORDER_REJECTED", "Margin is insufficient.")
            : new TimeoutException();

        OrderRecovery.AbsentIsFinal(now.AddSeconds(-secondsAgo), now, error).Should().Be(expected);
    }

    [Fact]
    public async Task An_exchange_trade_id_is_booked_once()
    {
        await using var harness = await Harness.CreateAsync(stopLossPercent: 2m);
        var order = harness.Order("dup-1", OrderSide.Buy, OrderStatus.Filled, DateTimeOffset.UtcNow);
        harness.Db.Orders.Add(order);
        await harness.Db.SaveChangesAsync();
        var store = new TradingStore(harness.Db);

        await store.AddExecutionAsync(new Domain.Orders.Execution { OrderId = order.Id, ExchangeTradeId = "T-1", Price = 1m, Quantity = 1m });
        await store.AddExecutionAsync(new Domain.Orders.Execution { OrderId = order.Id, ExchangeTradeId = "T-1", Price = 1m, Quantity = 1m });
        await store.SaveChangesAsync();
        await store.AddExecutionAsync(new Domain.Orders.Execution { OrderId = order.Id, ExchangeTradeId = "T-1", Price = 1m, Quantity = 1m });
        await store.AddExecutionAsync(new Domain.Orders.Execution { OrderId = order.Id, ExchangeTradeId = null, Price = 1m, Quantity = 1m });
        await store.SaveChangesAsync();

        (await harness.Db.Executions.CountAsync(row => row.ExchangeTradeId == "T-1")).Should().Be(1);
        (await harness.Db.Executions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public void Bot_cycle_state_marks_each_candle_once_per_bot()
    {
        var state = new BotCycleState();
        var bot = Guid.NewGuid();
        var candle = DateTimeOffset.UnixEpoch.AddMinutes(5);

        state.TryMarkSignaled(bot, candle).Should().BeTrue();
        state.TryMarkSignaled(bot, candle).Should().BeFalse();
        state.TryMarkSignaled(bot, candle.AddMinutes(-5)).Should().BeFalse();
        state.TryMarkSignaled(bot, candle.AddMinutes(5)).Should().BeTrue();
        state.TryMarkSignaled(Guid.NewGuid(), candle).Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"dualSidePosition":true}""", true)]
    [InlineData("""{"dualSidePosition":false}""", false)]
    [InlineData("""{"dualSidePosition":"true"}""", true)]
    [InlineData("""{"other":1}""", null)]
    public void Position_mode_reader_fails_closed_on_unknown_payloads(string json, bool? expected)
    {
        BinanceLiveExchangeConnector.ReadDualSide(JsonSerializer.Deserialize<JsonElement>(json)).Should().Be(expected);
    }

    [Theory]
    [InlineData(503, """{"code":-1001,"msg":"Internal error"}""", "EXCHANGE_UNAVAILABLE")]
    [InlineData(400, """{"code":-1007,"msg":"Timeout waiting for response from backend server. Send status unknown; execution status unknown."}""", "EXCHANGE_UNAVAILABLE")]
    [InlineData(400, """{"code":-1006,"msg":"An unexpected response was received."}""", "EXCHANGE_UNAVAILABLE")]
    [InlineData(400, """{"code":-2019,"msg":"Margin is insufficient."}""", "ORDER_REJECTED")]
    public void Signed_failures_with_unknown_execution_are_not_called_rejections(int status, string body, string expected)
    {
        BinanceSignedRestClient.ClassifyFailure(status, body).Should().Be(expected);
    }

    [Fact]
    public void Binance_error_text_keeps_the_code_callers_match_on()
    {
        var message = BinanceSignedRestClient.TrimBinanceError("""{"code":-4130,"msg":"An open stop or take profit order with GTE and closePosition in the direction is existing."}""");

        message.Should().Contain("-4130");
        ProtectiveOrderMath.IsExistingProtectiveOrder(message).Should().BeTrue();
    }

    [Fact]
    public void Open_algo_ids_are_read_for_one_coin_only()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("""
            [
              {"symbol":"BTCUSDT","clientAlgoId":"slA"},
              {"symbol":"ETHUSDT","clientAlgoId":"slB"},
              {"symbol":"BTCUSDT","clientAlgoId":"tpA"}
            ]
            """);

        BinanceLiveExchangeConnector.OpenAlgoClientIds(payload, "BTCUSDT").Should().Equal("slA", "tpA");
    }

    [Fact]
    public void Exchange_positions_skip_flat_rows_and_keep_direction()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("""
            [
              {"symbol":"BTCUSDT","positionAmt":"-0.010","entryPrice":"50000","markPrice":"50100"},
              {"symbol":"ETHUSDT","positionAmt":"0","entryPrice":"0","markPrice":"3000"}
            ]
            """);

        var rows = BinanceLiveExchangeConnector.ReadExchangePositions(payload);

        rows.Should().ContainSingle();
        rows[0].Side.Should().Be(PositionSide.Short);
        rows[0].Quantity.Should().Be(0.010m);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly BotCycleState _state = new();

        private Harness(TradingDbContext db, Bot bot, LiveAccountCache live, FaultyConnector connector)
        {
            Db = db;
            Bot = bot;
            Live = live;
            Connector = connector;
        }

        public TradingDbContext Db { get; }
        public Bot Bot { get; }
        public LiveAccountCache Live { get; }
        public FaultyConnector Connector { get; }

        public static async Task<Harness> CreateAsync(decimal stopLossPercent, decimal? listedStop = null)
        {
            var db = new TradingDbContext(new DbContextOptionsBuilder<TradingDbContext>()
                .UseInMemoryDatabase($"protect-{Guid.NewGuid():N}")
                .Options);
            var user = new User { Email = "admin@localhost", NormalizedEmail = "ADMIN@LOCALHOST", DisplayName = "Admin", PasswordHash = "x" };
            var strategy = new Strategy { User = user, UserId = user.Id, Name = "Never" };
            var version = new StrategyVersion
            {
                Strategy = strategy,
                VersionNumber = 1,
                DefinitionJson = """
                    {
                      "name": "Never",
                      "version": 1,
                      "symbol": "BTCUSDT",
                      "timeframe": "5m",
                      "entry": { "operator": "AND", "conditions": [ { "indicator": "SMA", "period": 2, "comparison": "LESS_THAN", "value": 0 } ] },
                      "exit": { "operator": "OR", "conditions": [ { "indicator": "SMA", "period": 2, "comparison": "LESS_THAN", "value": 0 } ] }
                    }
                    """,
                Symbol = "BTCUSDT",
                Timeframe = Timeframe.FiveMinutes
            };
            strategy.Versions.Add(version);
            var risk = new RiskProfile
            {
                Name = "LOW",
                RiskPerTradePercent = 0.5m,
                StopLossPercent = 2m,
                TakeProfitPercent = 4m,
                MaxLeverage = 3m,
                AllowLive = true,
                IsActive = true
            };
            var account = new ExchangeAccount { User = user, UserId = user.Id, Name = "Live", ApiKeyFingerprint = "live" };
            var bot = new Bot
            {
                User = user,
                UserId = user.Id,
                ExchangeAccount = account,
                StrategyVersion = version,
                RiskProfile = risk,
                Name = "BTCUSDT Live",
                Status = BotStatus.Running,
                Mode = TradingMode.Live,
                Symbol = "BTCUSDT",
                Timeframe = Timeframe.FiveMinutes,
                StartedAt = DateTimeOffset.UtcNow
            };
            db.Users.Add(user);
            db.Strategies.Add(strategy);
            db.RiskProfiles.Add(risk);
            db.ExchangeAccounts.Add(account);
            db.Symbols.Add(new Symbol { Name = "BTCUSDT", BaseAsset = "BTC", QuoteAsset = "USDT", TickSize = 0.1m, StepSize = 0.001m, MinQuantity = 0.001m, MinNotional = 5m, QuantityPrecision = 3 });
            db.Bots.Add(bot);
            db.Positions.Add(new Position
            {
                Bot = bot,
                Symbol = "BTCUSDT",
                Side = PositionSide.Long,
                Quantity = 0.01m,
                AverageEntryPrice = 50_000m,
                CurrentPrice = Mark,
                StopLossPercent = stopLossPercent,
                TakeProfitPercent = 4m,
                StopLossPrice = listedStop ?? 49_000m,
                TakeProfitPrice = 52_000m,
                OpenedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            });
            await db.SaveChangesAsync();

            var live = new LiveAccountCache();
            var orders = new List<LiveOpenOrder>
            {
                new("BTCUSDT", "Sell", "TAKE_PROFIT_MARKET", "NEW", 0m, 0m, 52_000m, null, LiveProtectivePrices.TakeClientOrderId(bot.Id), DateTimeOffset.UtcNow, "Futures")
            };
            if (listedStop is { } stop)
            {
                orders.Add(new("BTCUSDT", "Sell", "STOP_MARKET", "NEW", 0m, 0m, stop, null, LiveProtectivePrices.StopClientOrderId(bot.Id), DateTimeOffset.UtcNow, "Futures"));
            }

            live.Set(new LiveAccountSnapshot
            {
                HasKeys = true,
                CanTrade = true,
                FuturesBookFresh = true,
                FuturesUsdt = 1_000m,
                UsdtFree = 1_000m,
                OpenPositions = [new LiveOpenPosition("BTCUSDT", "Long", 0.01m, 50_000m, Mark, 1m, "Futures")],
                OpenOrders = orders,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            return new Harness(db, bot, live, new FaultyConnector());
        }

        public TradingOptions Options(Action<TradingOptions>? configure = null)
        {
            var options = new TradingOptions { ProtectionRetryDelayMs = 0 };
            configure?.Invoke(options);
            return options;
        }

        public BotEngine Engine(TradingOptions options)
        {
            var store = new TradingStore(Db);
            var cache = new MarketDataCache();
            var clock = new SystemClock();
            var correlation = new CorrelationIdAccessor();
            return new BotEngine(
                store,
                new StaticMarket(Candles(), Mark),
                cache,
                new StrategyEngine(),
                new StrategyDefinitionValidator(),
                new RiskEngine(),
                new ExchangeConnectorFactory([Connector]),
                Live,
                new NullTradingRealtimePublisher(),
                clock,
                correlation,
                Microsoft.Extensions.Options.Options.Create(options),
                new LiveIsolatedReconciler(store, Live, cache, clock, correlation, NullLogger<LiveIsolatedReconciler>.Instance),
                NullLogger<BotEngine>.Instance,
                cycleState: _state);
        }

        public Order Order(string clientOrderId, OrderSide side, OrderStatus status, DateTimeOffset submittedAt, string symbol = "BTCUSDT") => new()
        {
            BotId = Bot.Id,
            ExchangeAccountId = Bot.ExchangeAccountId,
            StrategyId = Bot.StrategyVersion.StrategyId,
            StrategyVersionId = Bot.StrategyVersionId,
            Symbol = symbol,
            Side = side,
            Type = OrderType.Market,
            Status = status,
            Quantity = 0.01m,
            RemainingQuantity = 0.01m,
            ClientOrderId = clientOrderId,
            IdempotencyKey = clientOrderId,
            Mode = TradingMode.Live,
            CorrelationId = "test",
            SubmittedAt = submittedAt
        };

        /// <summary>The context stamps CreatedAt/UpdatedAt on save, so they are back-dated on the tracked row the engine will read.</summary>
        public async Task<Order> SeedStaleOrderAsync(
            TimeSpan submittedAgo,
            string symbol = "BTCUSDT",
            OrderSide side = OrderSide.Buy,
            OrderStatus status = OrderStatus.Uncertain)
        {
            var submitted = DateTimeOffset.UtcNow - submittedAgo;
            var order = Order("stale-" + Guid.NewGuid().ToString("N")[..8], side, status, submitted, symbol);
            Db.Orders.Add(order);
            await Db.SaveChangesAsync();
            order.CreatedAt = submitted;
            order.UpdatedAt = submitted;
            return order;
        }

        public void GoFlatOnExchange() =>
            Live.Set(new LiveAccountSnapshot
            {
                HasKeys = true,
                CanTrade = true,
                FuturesBookFresh = true,
                FuturesUsdt = 1_000m,
                UsdtFree = 1_000m,
                OpenPositions = [],
                OpenOrders = [],
                UpdatedAt = DateTimeOffset.UtcNow
            });

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();

        private static List<MarketCandle> Candles()
        {
            var end = DateTimeOffset.UtcNow;
            var start = end.AddMinutes(-80 * 5);
            var candles = new List<MarketCandle>();
            for (var i = 0; i < 80; i++)
            {
                var open = start.AddMinutes(i * 5);
                candles.Add(new MarketCandle
                {
                    Open = Mark,
                    High = Mark,
                    Low = Mark,
                    Close = Mark,
                    Volume = 10,
                    OpenTime = open,
                    CloseTime = open.AddMinutes(5),
                    IsClosed = true,
                    ExchangeTimestamp = open.AddMinutes(5)
                });
            }

            return candles;
        }
    }

    private sealed class FaultyConnector : IExchangeConnector, ILiveExchangeConnectorFactory
    {
        public Func<decimal, bool> FailStopsAt { get; set; } = _ => false;
        public List<PlaceOrderRequest> Placed { get; } = [];
        public List<decimal> StopTriggers { get; } = [];
        public List<string> CancelledAll { get; } = [];
        public List<ExchangePosition> ExchangePositions { get; } = [];
        public HashSet<string> RejectOrdersFor { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int StopAttempts => StopTriggers.Count;
        public string Name => "FaultyLive";
        public TradingMode Mode => TradingMode.Live;
        public IExchangeConnector Create(Guid? exchangeAccountId) => this;

        public Task<ExchangeAccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExchangeAccountSnapshot("Futures", [], true));

        public Task<IReadOnlyList<ExchangeBalance>> GetBalancesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExchangeBalance>>([new("USDT", 1_000m, 0m)]);

        public Task<SymbolFilters> GetSymbolInformationAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SymbolFilters(symbol, "BTC", "USDT", 0.1m, 0.001m, 0.001m, 5m, 1, 3));

        public Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExchangeOrder>>([]);

        /// <summary>Orders Binance holds that this project may not have booked, keyed by client order id.</summary>
        public Dictionary<string, ExchangeOrder> OnExchange { get; } = new(StringComparer.Ordinal);

        public Task<OrderLookup> GetOrderAsync(string? clientOrderId, string? exchangeOrderId, string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult(clientOrderId is not null && OnExchange.TryGetValue(clientOrderId, out var order)
                ? OrderLookup.Found(order)
                : OrderLookup.Absent("Not sent."));

        public Task PrepareSymbolRiskAsync(string symbol, MarginMode marginMode, int leverage, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> GetMaxIsolatedLeverageAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult(20);

        public Task<ExchangeOrder> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
        {
            if (RejectOrdersFor.Contains(request.Symbol))
            {
                throw new Domain.Errors.DomainException("ORDER_REJECTED", "simulated rejection");
            }

            Placed.Add(request);
            if (request.ReduceOnly)
            {
                ExchangePositions.RemoveAll(row => string.Equals(row.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult(new ExchangeOrder(
                request.ClientOrderId,
                "LIVE-" + Placed.Count,
                request.Symbol,
                request.Side,
                request.Type,
                OrderStatus.Filled,
                request.Quantity,
                request.Quantity,
                Mark,
                Mark,
                DateTimeOffset.UtcNow,
                FillFee?.Amount ?? 0m,
                FillFee is not null,
                null,
                FillFee?.Asset));
        }

        public (decimal Amount, string Asset)? FillFee { get; set; }

        public Task<ProtectiveStopsResult> PlaceClosePositionStopsAsync(
            string symbol,
            OrderSide closeSide,
            decimal stopLossPrice,
            decimal takeProfitPrice,
            string stopClientOrderId,
            string takeProfitClientOrderId,
            CancellationToken cancellationToken = default,
            bool placeStop = true,
            bool placeTake = true,
            bool acceptExisting = true)
        {
            var stopFailed = false;
            if (placeStop)
            {
                StopTriggers.Add(stopLossPrice);
                stopFailed = FailStopsAt(stopLossPrice);
            }

            return Task.FromResult(new ProtectiveStopsResult(
                !placeStop || !stopFailed,
                true,
                stopFailed ? "simulated stop failure" : null));
        }

        public Task CancelOrderAsync(string symbol, string? clientOrderId, string? exchangeOrderId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CancelAllOrdersAsync(string symbol, CancellationToken cancellationToken = default)
        {
            CancelledAll.Add(symbol);
            return Task.CompletedTask;
        }

        public Task SubscribeMarketDataAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SubscribeUserDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExchangePosition>>(ExchangePositions.ToList());
    }

    private sealed class StaticMarket : IPublicMarketDataClient
    {
        private readonly IReadOnlyList<MarketCandle> _candles;
        private readonly decimal _last;

        public StaticMarket(IReadOnlyList<MarketCandle> candles, decimal last)
        {
            _candles = candles;
            _last = last;
        }

        public Task<IReadOnlyList<MarketCandle>> GetClosedKlinesAsync(string symbol, Timeframe timeframe, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult(_candles);

        public Task<IReadOnlyList<MarketCandle>> GetClosedKlinesRangeAsync(string symbol, Timeframe timeframe, DateTimeOffset start, DateTimeOffset end, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult(_candles);

        public Task<decimal> GetLastPriceAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult(_last);

        public Task<IReadOnlyDictionary<string, decimal>> GetLastPricesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["BTCUSDT"] = _last });

        public Task<IReadOnlyList<RankedUsdtSpotSymbol>> GetPaperUniverseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RankedUsdtSpotSymbol>>([]);

        public Task<IReadOnlyList<DiscoveredFuturesContract>> DiscoverUsdtPerpetualsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiscoveredFuturesContract>>([]);

        public Task<IReadOnlyList<FuturesBookTicker>> GetBookTickersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FuturesBookTicker>>([]);

        public Task<IReadOnlyList<FuturesPremiumIndex>> GetPremiumIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FuturesPremiumIndex>>([]);

        public Task<(decimal? Previous, decimal? Latest)> GetOpenInterestPairAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult<(decimal?, decimal?)>((null, null));

        public Task<(decimal? DayAgo, decimal? Latest)> GetOpenInterestDayAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult<(decimal?, decimal?)>((null, null));

        public Task<decimal?> GetLastFundingRateAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult<decimal?>(null);
    }
}
