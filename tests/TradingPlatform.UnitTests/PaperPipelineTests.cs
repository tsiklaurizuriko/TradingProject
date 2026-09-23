using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Balances;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Execution;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.MarketData;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Trading;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class PaperPipelineTests
{
    [Fact]
    public void Paper_fill_applies_slippage_and_uses_paper_ids()
    {
        PaperFillModel.ApplySlippage(100m, OrderSide.Buy, 5m).Should().Be(100.05m);
        PaperFillModel.ApplySlippage(100m, OrderSide.Sell, 5m).Should().Be(99.95m);
        PaperFillModel.Fee(1000m, 10m).Should().Be(1m);
        PaperFillModel.NewPaperOrderId().Should().StartWith("PAPER-");
        PaperFillModel.NewPaperFillId().Should().StartWith("PAPER-FILL-");
    }

    [Fact]
    public async Task Paper_connector_fills_immediately_without_binance_order_id()
    {
        var cache = new MarketDataCache();
        cache.SetTicker("BTCUSDT", 50_000m, DateTimeOffset.UtcNow);
        var connector = new PaperExchangeConnector(
            cache,
            new SystemClock(),
            Options.Create(new TradingOptions { PaperSlippageBps = 5m }));

        var fill = await connector.PlaceOrderAsync(new PlaceOrderRequest(
            "c1",
            "BTCUSDT",
            OrderSide.Buy,
            OrderType.Market,
            0.01m,
            null,
            null));

        fill.Status.Should().Be(OrderStatus.Filled);
        fill.ExchangeOrderId.Should().StartWith("PAPER-");
        fill.AverageFillPrice.Should().Be(50_025m);
        connector.Name.Should().Be("PaperSimulator");

        var protective = connector.PlaceClosePositionStopsAsync(
            "BTCUSDT",
            OrderSide.Sell,
            49_000m,
            52_000m,
            "sl-paper",
            "tp-paper");
        await protective;
        protective.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void Binance_hmac_matches_known_payload()
    {
        var signature = Binance.BinanceHmac.Sign("secret", "symbol=BTCUSDT&timestamp=1");
        signature.Should().HaveLength(64);
        signature.Should().MatchRegex("^[a-f0-9]+$");
    }

    [Fact]
    public void Live_connector_is_rejected_without_live_factory()
    {
        var factory = new ExchangeConnectorFactory(
            new PaperExchangeConnector(new MarketDataCache(), new SystemClock(), Options.Create(new TradingOptions())),
            Array.Empty<ILiveExchangeConnectorFactory>());
        var act = () => factory.Create(TradingMode.Live, null);
        act.Should().Throw<Domain.Errors.DomainException>().Which.Code.Should().Be("LIVE_TRADING_DISABLED");
    }

    [Fact]
    public async Task Bot_engine_turns_buy_signal_into_paper_fill_and_position()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase($"paper-{Guid.NewGuid():N}")
            .Options;
        await using var db = new TradingDbContext(options);

        var user = new User { Email = "admin@localhost", NormalizedEmail = "ADMIN@LOCALHOST", DisplayName = "Admin", PasswordHash = "x" };
        var strategy = new Strategy { User = user, UserId = user.Id, Name = "EMA RSI Strategy" };
        var version = new StrategyVersion
        {
            Strategy = strategy,
            VersionNumber = 1,
            DefinitionJson = """
                {
                  "name": "Always",
                  "version": 1,
                  "symbol": "BTCUSDT",
                  "timeframe": "5m",
                  "entry": {
                    "operator": "AND",
                    "conditions": [
                      { "indicator": "SMA", "period": 2, "comparison": "GREATER_THAN", "value": 0 }
                    ]
                  },
                  "exit": {
                    "operator": "OR",
                    "conditions": [
                      { "type": "STOP_LOSS", "percent": 50 }
                    ]
                  }
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
            MaxDailyLossPercent = 3m,
            AllowLive = true,
            IsActive = true
        };
        var account = new ExchangeAccount { User = user, UserId = user.Id, Name = "Paper Simulator", ApiKeyFingerprint = "paper" };
        var symbol = new Symbol { Name = "BTCUSDT", BaseAsset = "BTC", QuoteAsset = "USDT", StepSize = 0.00001m, MinQuantity = 0.00001m, MinNotional = 5m, QuantityPrecision = 5 };
        var bot = new Bot
        {
            User = user,
            UserId = user.Id,
            ExchangeAccount = account,
            StrategyVersion = version,
            RiskProfile = risk,
            Name = "BTCUSDT EMA RSI Paper",
            Status = BotStatus.Running,
            Mode = TradingMode.Paper,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.FiveMinutes,
            StartedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        db.Strategies.Add(strategy);
        db.RiskProfiles.Add(risk);
        db.ExchangeAccounts.Add(account);
        db.Symbols.Add(symbol);
        db.Bots.Add(bot);
        db.Balances.Add(new Balance { ExchangeAccount = account, Asset = "USDT", Free = 10_000m, Mode = TradingMode.Paper });
        await db.SaveChangesAsync();

        var candles = CrossingCandles();
        var last = candles[^1].Close;
        var cache = new MarketDataCache();
        var store = new TradingStore(db);
        var live = new LiveAccountCache();
        var clock = new SystemClock();
        var correlation = new CorrelationIdAccessor();
        var engine = new BotEngine(
            store,
            new FakeMarket(candles, last),
            cache,
            new StrategyEngine(),
            new StrategyDefinitionValidator(),
            new RiskEngine(),
            new ExchangeConnectorFactory(
                new PaperExchangeConnector(cache, new SystemClock(), Options.Create(new TradingOptions())),
                Array.Empty<ILiveExchangeConnectorFactory>()),
            live,
            new NullTradingRealtimePublisher(),
            clock,
            correlation,
            Options.Create(new TradingOptions()),
            new LiveIsolatedReconciler(store, live, cache, clock, correlation, NullLogger<LiveIsolatedReconciler>.Instance),
            NullLogger<BotEngine>.Instance);

        await engine.EvaluateRunningBotsAsync();

        (await db.Orders.CountAsync()).Should().Be(1);
        var order = await db.Orders.SingleAsync();
        order.Status.Should().Be(OrderStatus.Filled);
        order.ExchangeOrderId.Should().StartWith("PAPER-");
        order.Mode.Should().Be(TradingMode.Paper);
        (await db.Positions.CountAsync(p => p.ClosedAt == null)).Should().Be(1);
        (await db.Executions.SingleAsync()).ExchangeTradeId.Should().StartWith("PAPER-FILL-");
        var usdt = await db.Balances.SingleAsync(b => b.Asset == "USDT");
        var position = await db.Positions.SingleAsync(p => p.ClosedAt == null);
        position.MarginUsdt.Should().BeGreaterThan(0m);
        usdt.Locked.Should().Be(position.MarginUsdt);
        (usdt.Free + usdt.Locked).Should().BeApproximately(10_000m - (await db.Executions.SingleAsync()).Fee, 0.0001m);
        usdt.Free.Should().BeGreaterThan(10_000m - position.Quantity * position.AverageEntryPrice);
    }

    [Fact]
    public async Task Bot_engine_opens_an_isolated_short_on_sell_and_does_not_reverse()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase($"paper-short-{Guid.NewGuid():N}")
            .Options;
        await using var db = new TradingDbContext(options);

        var user = new User { Email = "admin@localhost", NormalizedEmail = "ADMIN@LOCALHOST", DisplayName = "Admin", PasswordHash = "x" };
        var strategy = new Strategy { User = user, UserId = user.Id, Name = "Donchian Breakout", TemplateKey = "donchian_breakout", AllowedSide = "Short" };
        var version = new StrategyVersion
        {
            Strategy = strategy,
            VersionNumber = 1,
            DefinitionJson = """
                {
                  "name": "Always short",
                  "version": 1,
                  "template": "donchian_breakout",
                  "timeframe": "5m",
                  "allowedSide": "Short",
                  "params": { "donchianLength": 5, "emaFast": 3, "emaSlow": 6 },
                  "quality": { "requireVolume": false, "volumeLookback": 20, "minAtrPercent": 0, "maxAtrPercent": 0 }
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
            MaxDailyLossPercent = 3m,
            AllowLive = true,
            IsActive = true
        };
        var account = new ExchangeAccount { User = user, UserId = user.Id, Name = "Paper Simulator", ApiKeyFingerprint = "paper" };
        var symbol = new Symbol { Name = "BTCUSDT", BaseAsset = "BTC", QuoteAsset = "USDT", StepSize = 0.00001m, MinQuantity = 0.00001m, MinNotional = 5m, QuantityPrecision = 5 };
        var bot = new Bot
        {
            User = user,
            UserId = user.Id,
            ExchangeAccount = account,
            StrategyVersion = version,
            RiskProfile = risk,
            Name = "BTCUSDT Donchian Paper",
            Status = BotStatus.Running,
            Mode = TradingMode.Paper,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.FiveMinutes,
            StartedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        db.Strategies.Add(strategy);
        db.RiskProfiles.Add(risk);
        db.ExchangeAccounts.Add(account);
        db.Symbols.Add(symbol);
        db.Bots.Add(bot);
        db.Balances.Add(new Balance { ExchangeAccount = account, Asset = "USDT", Free = 10_000m, Mode = TradingMode.Paper });
        await db.SaveChangesAsync();

        var candles = Enumerable.Range(0, 16).Select(i =>
        {
            var price = i == 15 ? 90m : 100m;
            return new MarketCandle
            {
                Open = price,
                High = price,
                Low = price,
                Close = price,
                Volume = 10,
                OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
                CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5),
                IsClosed = true,
                ExchangeTimestamp = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5)
            };
        }).ToList();
        var last = candles[^1].Close;
        var cache = new MarketDataCache();
        var store = new TradingStore(db);
        var live = new LiveAccountCache();
        var clock = new SystemClock();
        var correlation = new CorrelationIdAccessor();
        var engine = new BotEngine(
            store,
            new FakeMarket(candles, last),
            cache,
            new StrategyEngine(),
            new StrategyDefinitionValidator(),
            new RiskEngine(),
            new ExchangeConnectorFactory(
                new PaperExchangeConnector(cache, new SystemClock(), Options.Create(new TradingOptions())),
                Array.Empty<ILiveExchangeConnectorFactory>()),
            live,
            new NullTradingRealtimePublisher(),
            clock,
            correlation,
            Options.Create(new TradingOptions()),
            new LiveIsolatedReconciler(store, live, cache, clock, correlation, NullLogger<LiveIsolatedReconciler>.Instance),
            NullLogger<BotEngine>.Instance);

        await engine.EvaluateRunningBotsAsync();

        var position = await db.Positions.SingleAsync(p => p.ClosedAt == null);
        position.Side.Should().Be(TradingPlatform.Domain.Positions.PositionSide.Short);
        (await db.Orders.CountAsync()).Should().Be(1);
        var order = await db.Orders.SingleAsync();
        order.Side.Should().Be(OrderSide.Sell);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Live_open_position_is_never_automatically_flattened(bool protectiveStopPlaced)
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase($"live-exit-{Guid.NewGuid():N}")
            .Options;
        await using var db = new TradingDbContext(options);

        var user = new User { Email = "admin@localhost", NormalizedEmail = "ADMIN@LOCALHOST", DisplayName = "Admin", PasswordHash = "x" };
        var strategy = new Strategy { User = user, UserId = user.Id, Name = "EMA RSI Strategy" };
        var version = new StrategyVersion
        {
            Strategy = strategy,
            VersionNumber = 1,
            DefinitionJson = """
                {
                  "name": "AlwaysExit",
                  "version": 1,
                  "symbol": "BTCUSDT",
                  "timeframe": "5m",
                  "entry": {
                    "operator": "AND",
                    "conditions": [
                      { "indicator": "SMA", "period": 2, "comparison": "GREATER_THAN", "value": 0 }
                    ]
                  },
                  "exit": {
                    "operator": "OR",
                    "conditions": [
                      { "indicator": "SMA", "period": 2, "comparison": "GREATER_THAN", "value": 0 }
                    ]
                  }
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
        var symbol = new Symbol { Name = "BTCUSDT", BaseAsset = "BTC", QuoteAsset = "USDT", StepSize = 0.001m, MinQuantity = 0.001m, MinNotional = 5m, QuantityPrecision = 3 };
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
        db.Symbols.Add(symbol);
        db.Bots.Add(bot);
        db.Positions.Add(new TradingPlatform.Domain.Positions.Position
        {
            Bot = bot,
            Symbol = "BTCUSDT",
            Side = TradingPlatform.Domain.Positions.PositionSide.Long,
            Quantity = 0.01m,
            AverageEntryPrice = 50_000m,
            CurrentPrice = 50_100m,
            StopLossPercent = 2m,
            TakeProfitPercent = 4m,
            StopLossPrice = 49_000m,
            TakeProfitPrice = 52_000m,
            OpenedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        var candles = CrossingCandles();
        var last = candles[^1].Close;
        var cache = new MarketDataCache();
        var store = new TradingStore(db);
        var live = new LiveAccountCache();
        live.Set(new LiveAccountSnapshot
        {
            HasKeys = true,
            CanTrade = true,
            FuturesBookFresh = true,
            FuturesUsdt = 1_000m,
            UsdtFree = 1_000m,
            OpenPositions = [new LiveOpenPosition("BTCUSDT", "Long", 0.01m, 50_000m, last, 1m, "Futures")],
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var clock = new SystemClock();
        var correlation = new CorrelationIdAccessor();
        var liveOrders = new RecordingLiveConnector(protectiveStopPlaced);
        var engine = new BotEngine(
            store,
            new FakeMarket(candles, last),
            cache,
            new StrategyEngine(),
            new StrategyDefinitionValidator(),
            new RiskEngine(),
            new ExchangeConnectorFactory(
                new PaperExchangeConnector(cache, clock, Options.Create(new TradingOptions())),
                [liveOrders]),
            live,
            new NullTradingRealtimePublisher(),
            clock,
            correlation,
            Options.Create(new TradingOptions()),
            new LiveIsolatedReconciler(store, live, cache, clock, correlation, NullLogger<LiveIsolatedReconciler>.Instance),
            NullLogger<BotEngine>.Instance);

        await engine.EvaluateRunningBotsAsync();

        liveOrders.Placed.Should().BeEmpty();
        (await db.Positions.CountAsync(p => p.ClosedAt == null)).Should().Be(1);
        bot.LastError.Should().Contain(protectiveStopPlaced
            ? "Live Isolated SL/TP own the exit"
            : "Automatic close is disabled");
    }

    private static List<MarketCandle> CrossingCandles()
    {
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 80; i++)
        {
            var price = i < 50 ? 100m - i * 0.2m : 90m + (i - 50) * 0.8m;
            candles.Add(new MarketCandle
            {
                Open = price,
                High = price,
                Low = price,
                Close = price,
                Volume = 10,
                OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
                CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5),
                IsClosed = true,
                ExchangeTimestamp = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5)
            });
        }

        return candles;
    }

    private sealed class FakeMarket : IPublicMarketDataClient
    {
        private readonly IReadOnlyList<MarketCandle> _candles;
        private readonly decimal _last;

        public FakeMarket(IReadOnlyList<MarketCandle> candles, decimal last)
        {
            _candles = candles;
            _last = last;
        }

        public Task<IReadOnlyList<MarketCandle>> GetClosedKlinesAsync(
            string symbol,
            Timeframe timeframe,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_candles);

        public Task<IReadOnlyList<MarketCandle>> GetClosedKlinesRangeAsync(
            string symbol,
            Timeframe timeframe,
            DateTimeOffset start,
            DateTimeOffset end,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_candles);

        public Task<decimal> GetLastPriceAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult(_last);

        public Task<IReadOnlyList<RankedUsdtSpotSymbol>> GetPaperUniverseAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RankedUsdtSpotSymbol>>([]);

        public Task<IReadOnlyList<DiscoveredFuturesContract>> DiscoverUsdtPerpetualsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiscoveredFuturesContract>>([]);

        public Task<IReadOnlyList<FuturesBookTicker>> GetBookTickersAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FuturesBookTicker>>([]);

        public Task<IReadOnlyList<FuturesPremiumIndex>> GetPremiumIndexAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FuturesPremiumIndex>>([]);
    }

    private sealed class RecordingLiveConnector : IExchangeConnector, ILiveExchangeConnectorFactory
    {
        private readonly bool _protectiveStopPlaced;

        public RecordingLiveConnector(bool protectiveStopPlaced = true)
        {
            _protectiveStopPlaced = protectiveStopPlaced;
        }

        public List<PlaceOrderRequest> Placed { get; } = [];
        public string Name => "RecordingLive";
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

        public Task<ExchangeOrder?> GetOrderAsync(string? clientOrderId, string? exchangeOrderId, string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult<ExchangeOrder?>(null);

        public Task PrepareSymbolRiskAsync(string symbol, MarginMode marginMode, int leverage, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> GetMaxIsolatedLeverageAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult(20);

        public Task<ExchangeOrder> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
        {
            Placed.Add(request);
            return Task.FromResult(new ExchangeOrder(
                request.ClientOrderId,
                "LIVE-1",
                request.Symbol,
                request.Side,
                request.Type,
                OrderStatus.Filled,
                request.Quantity,
                request.Quantity,
                request.Price,
                50_000m,
                DateTimeOffset.UtcNow));
        }

        public Task<ProtectiveStopsResult> PlaceClosePositionStopsAsync(
            string symbol,
            OrderSide closeSide,
            decimal stopLossPrice,
            decimal takeProfitPrice,
            string stopClientOrderId,
            string takeProfitClientOrderId,
            CancellationToken cancellationToken = default,
            bool placeStop = true,
            bool placeTake = true) =>
            Task.FromResult(new ProtectiveStopsResult(
                !placeStop || _protectiveStopPlaced,
                !placeTake || _protectiveStopPlaced,
                placeStop && !_protectiveStopPlaced ? "simulated stop failure" : null,
                placeTake && !_protectiveStopPlaced ? "simulated take-profit failure" : null));

        public Task CancelOrderAsync(string symbol, string? clientOrderId, string? exchangeOrderId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CancelAllOrdersAsync(string symbol, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SubscribeMarketDataAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SubscribeUserDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
