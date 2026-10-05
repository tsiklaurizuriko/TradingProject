using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Execution.Shadow;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.MarketData;
using TradingPlatform.Trading;
using TradingPlatform.Trading.Shadow;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class ShadowVenueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 7, 0, 0, TimeSpan.Zero);
    private static readonly FuturesBookTicker Book = new("BTCUSDT", 49_990m, 50_010m);
    private static readonly IReadOnlyDictionary<string, decimal> Marks = new Dictionary<string, decimal> { ["BTCUSDT"] = 50_000m };

    [Fact]
    public void Market_buy_fills_at_the_ask_plus_slippage_and_pays_the_taker_fee()
    {
        var exchange = Exchange();
        exchange.SetLeverage("BTCUSDT", 5);

        var order = exchange.PlaceMarket(Buy("E1", 0.1m), Book, Marks, Now);

        order.Status.Should().Be(OrderStatus.Filled);
        order.AverageFillPrice.Should().Be(50_010m * 1.0002m);
        order.FeeKnown.Should().BeTrue();
        order.FeeAsset.Should().Be("USDT");
        order.Fee.Should().Be(0.1m * 50_010m * 1.0002m * 0.0005m);
        exchange.Positions(Marks).Should().ContainSingle(p => p.Side == PositionSide.Long && p.Quantity == 0.1m);
        exchange.Account(Marks).Wallet.Should().Be(10_000m - order.Fee);
    }

    [Fact]
    public void Reduce_only_close_books_realized_pnl_and_flattens()
    {
        var exchange = Exchange();
        exchange.SetLeverage("BTCUSDT", 5);
        var entry = exchange.PlaceMarket(Buy("E1", 0.1m), Book, Marks, Now);
        var up = new FuturesBookTicker("BTCUSDT", 51_000m, 51_020m);

        var exit = exchange.PlaceMarket(new PlaceOrderRequest("X1", "BTCUSDT", OrderSide.Sell, OrderType.Market, 0.1m, null, null, ReduceOnly: true), up, Marks, Now);

        exit.Status.Should().Be(OrderStatus.Filled);
        var exitPrice = 51_000m * (1m - 0.0002m);
        exchange.Positions(Marks).Should().BeEmpty();
        exchange.Account(Marks).Wallet.Should().Be(10_000m - entry.Fee - exit.Fee + 0.1m * (exitPrice - entry.AverageFillPrice!.Value));
    }

    [Fact]
    public void Reduce_only_without_a_position_is_rejected()
    {
        var order = Exchange().PlaceMarket(
            new PlaceOrderRequest("X1", "BTCUSDT", OrderSide.Sell, OrderType.Market, 0.1m, null, null, ReduceOnly: true), Book, Marks, Now);

        order.Status.Should().Be(OrderStatus.Rejected);
    }

    [Fact]
    public void An_entry_larger_than_the_margin_is_rejected()
    {
        var exchange = Exchange();
        exchange.SetLeverage("BTCUSDT", 2);

        exchange.PlaceMarket(Buy("E1", 1m), Book, Marks, Now).Status.Should().Be(OrderStatus.Rejected);
        exchange.Positions(Marks).Should().BeEmpty();
    }

    [Fact]
    public void A_resent_client_id_returns_the_first_fill_instead_of_a_second_position()
    {
        var exchange = Exchange();
        exchange.SetLeverage("BTCUSDT", 5);

        var first = exchange.PlaceMarket(Buy("E1", 0.1m), Book, Marks, Now);
        var again = exchange.PlaceMarket(Buy("E1", 0.1m), Book, Marks, Now);

        again.ExchangeOrderId.Should().Be(first.ExchangeOrderId);
        exchange.Positions(Marks).Single().Quantity.Should().Be(0.1m);
    }

    [Fact]
    public void Missing_book_does_not_accept_the_order()
    {
        var exchange = Exchange();
        var act = () => exchange.PlaceMarket(Buy("E1", 0.1m), null, Marks, Now);

        act.Should().Throw<InvalidOperationException>();
        exchange.Lookup("E1", null).Kind.Should().Be(OrderLookupKind.ConfirmedAbsent);
    }

    [Fact]
    public void Stop_triggers_on_mark_fills_at_the_bid_and_cancels_the_take()
    {
        var exchange = OpenLong();
        exchange.PlaceStops("BTCUSDT", OrderSide.Sell, 49_000m, 52_000m, "SL", "TP", 50_000m, true, true, true, Now)
            .Should().Be(new ProtectiveStopsResult(true, true));

        exchange.Tick(Mark(49_500m), NoFunding, _ => Book, Now).Should().Be(0);
        var gapBook = new FuturesBookTicker("BTCUSDT", 48_900m, 48_920m);
        exchange.Tick(Mark(48_950m), NoFunding, _ => gapBook, Now.AddMinutes(1)).Should().Be(1);

        exchange.Positions(Marks).Should().BeEmpty();
        var stop = exchange.Lookup("SL", null).Order!;
        stop.Status.Should().Be(OrderStatus.Filled);
        stop.AverageFillPrice.Should().Be(48_900m * (1m - 0.0002m));
        exchange.Lookup("TP", null).Order!.Status.Should().Be(OrderStatus.Cancelled);
        exchange.PendingFills().Should().ContainSingle(fill => fill.Reason == "Stop loss" && fill.Quantity == 0.1m);
    }

    [Fact]
    public void A_stop_already_through_the_mark_is_rejected_like_binance()
    {
        var exchange = OpenLong();

        var result = exchange.PlaceStops("BTCUSDT", OrderSide.Sell, 50_100m, 52_000m, "SL", "TP", 50_000m, true, false, true, Now);

        result.StopPlaced.Should().BeFalse();
        result.StopError.Should().Contain("-2021");
    }

    [Fact]
    public void Funding_settles_at_the_eight_hour_mark_and_longs_pay_a_positive_rate()
    {
        var exchange = OpenLong();
        var rates = new Dictionary<string, decimal> { ["BTCUSDT"] = 0.0001m };
        var before = exchange.Account(Marks).Wallet;

        exchange.Tick(Marks, rates, _ => Book, Now.AddMinutes(30)).Should().Be(0);
        exchange.Tick(Marks, rates, _ => Book, new DateTimeOffset(2026, 10, 5, 8, 0, 5, TimeSpan.Zero)).Should().Be(1);
        exchange.Tick(Marks, rates, _ => Book, new DateTimeOffset(2026, 10, 5, 8, 0, 10, TimeSpan.Zero)).Should().Be(0);

        var paid = -0.1m * 50_000m * 0.0001m;
        exchange.Account(Marks).Wallet.Should().Be(before + paid);
        exchange.PendingFills().Should().ContainSingle(fill => fill.Kind == ShadowFillKind.Funding && fill.Funding == paid);
    }

    [Fact]
    public void The_account_survives_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shadow-{Guid.NewGuid():N}.json");
        try
        {
            var first = new ShadowExchange(new ShadowExchangeOptions { StatePath = path });
            first.SetLeverage("BTCUSDT", 5);
            first.PlaceMarket(Buy("E1", 0.1m), Book, Marks, Now);
            first.PlaceStops("BTCUSDT", OrderSide.Sell, 49_000m, 52_000m, "SL", "TP", 50_000m, true, true, true, Now);

            var restarted = new ShadowExchange(new ShadowExchangeOptions { StatePath = path });

            restarted.Positions(Marks).Should().ContainSingle(p => p.Quantity == 0.1m);
            restarted.Lookup("E1", null).Kind.Should().Be(OrderLookupKind.Found);
            restarted.OpenOrders("BTCUSDT").Should().HaveCount(2);
            restarted.Account(Marks).Wallet.Should().Be(first.Account(Marks).Wallet);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_shadow_stop_is_booked_into_the_trade_once_with_known_net_pnl()
    {
        var (db, bot, trade) = await SeedOpenTradeAsync();
        var exchange = OpenLong();
        var entryPrice = exchange.Positions(Marks).Single().EntryPrice;
        trade.EntryPrice = entryPrice;
        db.Positions.Single().AverageEntryPrice = entryPrice;
        db.Orders.Add(new Order
        {
            BotId = bot.Id,
            StrategyId = bot.StrategyVersion.StrategyId,
            StrategyVersionId = bot.StrategyVersionId,
            Symbol = "BTCUSDT",
            Side = OrderSide.Sell,
            Type = OrderType.StopMarket,
            Status = OrderStatus.Submitted,
            Quantity = 0.1m,
            ClientOrderId = "SL",
            IdempotencyKey = "SL"
        });
        await db.SaveChangesAsync();
        exchange.PlaceStops("BTCUSDT", OrderSide.Sell, 49_000m, 52_000m, "SL", "TP", 50_000m, true, true, true, Now);
        exchange.Tick(Mark(48_990m), NoFunding, _ => new FuturesBookTicker("BTCUSDT", 48_980m, 49_000m), Now.AddMinutes(5));
        var service = Service(exchange, db);

        (await service.BookPendingAsync()).Should().Be(1);
        (await service.BookPendingAsync()).Should().Be(0);

        var fill = 48_980m * (1m - 0.0002m);
        var closed = await db.Trades.SingleAsync();
        closed.ClosedAt.Should().NotBeNull();
        closed.ExitPrice.Should().Be(fill);
        closed.PnL.Should().Be(0.1m * (fill - entryPrice));
        closed.FeeStatus.Should().Be(FeeKnowledge.Known);
        closed.NetPnL.Should().Be(closed.PnL - closed.Fees);
        (await db.Positions.SingleAsync()).IsOpen.Should().BeFalse();
        var stop = await db.Orders.Include(o => o.Executions).SingleAsync(o => o.ClientOrderId == "SL");
        stop.Status.Should().Be(OrderStatus.Filled);
        stop.Executions.Should().ContainSingle();
        exchange.PendingFills().Should().BeEmpty();
    }

    [Fact]
    public async Task The_account_snapshot_comes_from_the_shadow_book_and_is_fresh()
    {
        var (db, _, _) = await SeedOpenTradeAsync();
        var exchange = OpenLong();
        var cache = new LiveAccountCache();
        var service = Service(exchange, db, cache);

        var status = await service.GetStatusAsync(Guid.Empty);

        status.HasKeys.Should().BeTrue();
        cache.Current.FuturesBookFresh.Should().BeTrue();
        cache.Current.OpenPositions.Should().ContainSingle(p => p.Symbol == "BTCUSDT" && p.Side == "Long");
        cache.Current.ApiKeyHint.Should().Be("shadow");
    }

    [Theory]
    [InlineData(null, TradingVenueKind.Live)]
    [InlineData("shadow", TradingVenueKind.Shadow)]
    [InlineData("Testnet", TradingVenueKind.Testnet)]
    public void Venue_parses(string? value, TradingVenueKind expected) => TradingVenue.Parse(value).Should().Be(expected);

    [Fact]
    public void Unknown_venue_stops_the_process() =>
        FluentActions.Invoking(() => TradingVenue.Parse("Paper")).Should().Throw<InvalidOperationException>();

    [Theory]
    [InlineData("Shadow", "Host=x;Database=TradingProject", null)]
    [InlineData("Testnet", "Host=x;Database=TradingProject_testnet", "https://fapi.binance.com")]
    [InlineData("Testnet", "Host=x;Database=TradingProject_testnet", null)]
    public void A_shadow_or_testnet_process_on_the_live_database_or_mainnet_orders_does_not_start(string venue, string connection, string? signedUrl)
    {
        var configuration = Config(venue, connection, signedUrl, "wss://stream.binancefuture.com");

        FluentActions.Invoking(() => TradingVenue.Validate(configuration)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_testnet_process_with_its_own_database_and_testnet_hosts_starts() =>
        FluentActions.Invoking(() => TradingVenue.Validate(Config(
            "Testnet",
            "Host=x;Database=TradingProject_testnet",
            "https://testnet.binancefuture.com",
            "wss://stream.binancefuture.com"))).Should().NotThrow();

    [Theory]
    [InlineData("Live", false, true, true, false)]
    [InlineData("Live", true, false, false, true)]
    [InlineData("Shadow", true, false, false, false)]
    [InlineData("Shadow", false, true, false, true)]
    [InlineData("Testnet", true, true, false, false)]
    [InlineData("Testnet", false, false, true, true)]
    public void Only_the_venue_own_switch_opens_entries(string venue, bool live, bool shadow, bool testnet, bool expected)
    {
        var options = new TradingOptions { Venue = venue, LiveTradingEnabled = live, ShadowTradingEnabled = shadow, TestnetTradingEnabled = testnet };

        options.EntriesEnabled.Should().Be(expected);
    }

    [Fact]
    public void The_entry_gate_names_the_switch_of_the_venue()
    {
        var facts = new LiveEntryFacts(TradingMode.Live, false, false, true, false, true, true, true, false, TradingVenueKind.Shadow);

        LiveEntryGate.Block(facts).Should().Contain("ShadowTradingEnabled");
    }

    private static readonly IReadOnlyDictionary<string, decimal> NoFunding = new Dictionary<string, decimal>();

    private static IReadOnlyDictionary<string, decimal> Mark(decimal price) => new Dictionary<string, decimal> { ["BTCUSDT"] = price };

    private static ShadowExchange Exchange() => new(new ShadowExchangeOptions { StatePath = "" });

    private static ShadowExchange OpenLong()
    {
        var exchange = Exchange();
        exchange.SetLeverage("BTCUSDT", 5);
        exchange.PlaceMarket(Buy("E1", 0.1m), Book, Marks, Now);
        return exchange;
    }

    private static PlaceOrderRequest Buy(string id, decimal quantity) =>
        new(id, "BTCUSDT", OrderSide.Buy, OrderType.Market, quantity, null, null);

    private static IConfiguration Config(string venue, string connection, string? signedUrl, string socketUrl) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Trading:Venue"] = venue,
            ["ConnectionStrings:TradingPlatform"] = connection,
            ["Binance:FuturesSignedRestBaseUrl"] = signedUrl,
            ["Binance:FuturesWebSocketBaseUrl"] = socketUrl
        }).Build();

    private static ShadowExchangeAccountService Service(ShadowExchange exchange, TradingDbContext db, LiveAccountCache? cache = null) =>
        new(
            exchange,
            new FixedFeed(),
            new TradingStore(db),
            new MemoryCredentials(),
            cache ?? new LiveAccountCache(),
            new FixedClock(Now),
            NullLogger<ShadowExchangeAccountService>.Instance);

    private static async Task<(TradingDbContext Db, Bot Bot, Trade Trade)> SeedOpenTradeAsync()
    {
        var db = new TradingDbContext(new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase($"shadow-{Guid.NewGuid():N}")
            .Options);
        var user = new User { Email = "admin@localhost", NormalizedEmail = "ADMIN@LOCALHOST", DisplayName = "Admin", PasswordHash = "x" };
        var strategy = new Strategy { User = user, UserId = user.Id, Name = "S" };
        var version = new StrategyVersion { Strategy = strategy, VersionNumber = 1, DefinitionJson = "{}", Symbol = "BTCUSDT", Timeframe = Timeframe.FiveMinutes };
        strategy.Versions.Add(version);
        var account = new ExchangeAccount { User = user, UserId = user.Id, Name = "Binance Live", ApiKeyFingerprint = "none" };
        var bot = new Bot
        {
            User = user,
            UserId = user.Id,
            ExchangeAccount = account,
            StrategyVersion = version,
            Name = "BTCUSDT Shadow",
            Status = BotStatus.Running,
            Mode = TradingMode.Live,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.FiveMinutes,
            StartedAt = Now.AddHours(-1)
        };
        var trade = new Trade
        {
            Bot = bot,
            Strategy = strategy,
            StrategyVersion = version,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Quantity = 0.1m,
            EntryPrice = 50_000m,
            Fees = 2.5m,
            FeeStatus = FeeKnowledge.Known,
            FeeAsset = "USDT",
            OpenedAt = Now.AddMinutes(-30)
        };
        db.Users.Add(user);
        db.Strategies.Add(strategy);
        db.ExchangeAccounts.Add(account);
        db.Bots.Add(bot);
        db.Positions.Add(new Position
        {
            Bot = bot,
            Symbol = "BTCUSDT",
            Side = PositionSide.Long,
            Quantity = 0.1m,
            AverageEntryPrice = 50_000m,
            CurrentPrice = 50_000m,
            OpenedAt = Now.AddMinutes(-30)
        });
        db.Trades.Add(trade);
        await db.SaveChangesAsync();
        return (db, bot, trade);
    }

    private sealed class FixedFeed : IShadowPriceFeed
    {
        public Task<FuturesBookTicker?> GetBookAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult<FuturesBookTicker?>(Book);

        public Task<IReadOnlyDictionary<string, FuturesPremiumIndex>> GetPremiumAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, FuturesPremiumIndex>>(
                new Dictionary<string, FuturesPremiumIndex> { ["BTCUSDT"] = new("BTCUSDT", 50_000m, 0.0001m) });
    }

    internal sealed class MemoryCredentials : IExchangeCredentialStore
    {
        private readonly Dictionary<Guid, (string, string)> _keys = new();
        private readonly Dictionary<Guid, ExchangeAccount> _accounts = new();

        public Task StoreAsync(Guid exchangeAccountId, string apiKey, string apiSecret, CancellationToken cancellationToken = default)
        {
            _keys[exchangeAccountId] = (apiKey, apiSecret);
            return Task.CompletedTask;
        }

        public Task<(string ApiKey, string ApiSecret)?> GetAsync(Guid exchangeAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<(string ApiKey, string ApiSecret)?>(_keys.TryGetValue(exchangeAccountId, out var keys) ? keys : null);

        public Task<ExchangeAccount?> GetLiveAccountAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.GetValueOrDefault(userId));

        public Task<ExchangeAccount> GetOrCreateLiveAccountAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (!_accounts.TryGetValue(userId, out var account))
            {
                account = new ExchangeAccount { UserId = userId, Name = "Binance Live", ApiKeyFingerprint = "none" };
                _accounts[userId] = account;
            }

            return Task.FromResult(account);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
