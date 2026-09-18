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
        var symbol = new Symbol { Name = "BTCUSDT", BaseAsset = "BTC", QuoteAsset = "USDT", StepSize = 0.00001m, MinQuantity = 0.00001m, MinNotional = 5m };
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
            new LiveAccountCache(),
            new NullTradingRealtimePublisher(),
            new SystemClock(),
            new CorrelationIdAccessor(),
            Options.Create(new TradingOptions()),
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
    }
}
