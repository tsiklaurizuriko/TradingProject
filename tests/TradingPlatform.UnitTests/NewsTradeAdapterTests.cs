using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Domain.News;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.News;
using TradingPlatform.Risk;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class NewsTradeAdapterTests
{
    [Fact]
    public void Long_reaches_the_risk_engine_and_does_not_send_a_live_order()
    {
        var handoff = NewsTradeAdapter.Handoff(
            "LONG",
            100m,
            0.01m,
            Profile(),
            Snapshot("SOLUSDT"),
            new RiskEngine(),
            DateTimeOffset.UtcNow,
            liveTradingEnabled: false,
            sessionRunning: true);

        handoff.RiskDecision.Should().Be("Approved");
        handoff.Quantity.Should().BeGreaterThan(0);
        handoff.StopLossPrice.Should().BeLessThan(100m);
        handoff.TakeProfitPrice.Should().BeGreaterThan(100m);
        handoff.OrderDecision.Should().Be("NOT_SENT");
        handoff.RiskReason.Should().Contain("LiveTradingEnabled=false");
        handoff.Request.Should().NotBeNull();
        handoff.Request!.Symbol.Should().Be("SOLUSDT");
    }

    [Fact]
    public void Short_reaches_the_risk_engine()
    {
        var handoff = NewsTradeAdapter.Handoff("SHORT", 100m, 0.01m, Profile(), Snapshot("ETHUSDT"), new RiskEngine(), DateTimeOffset.UtcNow, false, true);
        handoff.RiskDecision.Should().Be("Approved");
        handoff.Request!.Side.Should().Be(OrderSide.Sell);
        handoff.StopLossPrice.Should().BeGreaterThan(100m);
    }

    [Fact]
    public void No_trade_does_not_reach_the_risk_engine()
    {
        var calls = 0;
        var handoff = NewsTradeAdapter.Handoff("NO_TRADE", 100m, 0.01m, Profile(), Snapshot("BTCUSDT"), new CountingRisk(ref calls), DateTimeOffset.UtcNow, true, true);
        handoff.RiskDecision.Should().Be("NotEvaluated");
        handoff.Request.Should().BeNull();
        calls.Should().Be(0);
    }

    [Fact]
    public void An_open_coin_is_rejected_by_the_existing_risk_engine()
    {
        var snapshot = Snapshot("SOLUSDT") with { SymbolAlreadyOpen = true };
        var handoff = NewsTradeAdapter.Handoff("LONG", 100m, 0.01m, Profile(), snapshot, new RiskEngine(), DateTimeOffset.UtcNow, false, true);
        handoff.RiskDecision.Should().Be("Rejected");
        handoff.Request.Should().BeNull();
    }

    [Fact]
    public async Task Duplicate_article_is_not_stored_twice_and_survives_a_new_context()
    {
        var name = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TradingDbContext>().UseInMemoryDatabase(name).Options;
        await using (var db = new TradingDbContext(options))
        {
            var store = new NewsDatabase(db);
            var article = Article();
            (await store.AddArticleIfNewAsync(article, CancellationToken.None)).Should().BeTrue();
            (await store.AddArticleIfNewAsync(Article(), CancellationToken.None)).Should().BeFalse();
            (await store.ArticleCountAsync(CancellationToken.None)).Should().Be(1);
        }

        await using var restarted = new TradingDbContext(options);
        (await restarted.NewsArticles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Submit_does_not_call_the_exchange_when_live_trading_is_off()
    {
        var connector = new ExplodingConnector();
        var request = new PlaceOrderRequest("news-1", "SOLUSDT", OrderSide.Buy, OrderType.Market, 1m, null, TimeSpan.FromSeconds(5));
        var fill = await NewsTradeAdapter.SubmitAsync(connector, request, liveTradingEnabled: false, CancellationToken.None);
        fill.Should().BeNull();
        connector.Called.Should().BeFalse();
    }

    [Fact]
    public void Candidate_names_map_to_trade_directions()
    {
        NewsTradeAdapter.Decision(NewsMarketSignals.LongCandidate).Should().Be("LONG");
        NewsTradeAdapter.Decision(NewsMarketSignals.ShortCandidate).Should().Be("SHORT");
        NewsTradeAdapter.Decision(NewsMarketSignals.NoTrade).Should().Be("NO_TRADE");
    }

    private static RiskProfile Profile() => new()
    {
        Name = "News",
        RiskPerTradePercent = 0.5m,
        StopLossPercent = 2m,
        TakeProfitPercent = 4m,
        MaxLeverage = 3m,
        MaxPortfolioRiskPercent = 4m,
        MaxSimultaneousPositions = 2,
        IsActive = true
    };

    private static RiskSnapshot Snapshot(string symbol) => new()
    {
        Equity = 10_000m,
        AvailableBalance = 10_000m,
        Symbol = symbol,
        Price = 100m,
        Sizing = new RiskSizingHints
        {
            StepSize = 0.001m,
            MinQuantity = 0.001m,
            MinNotional = 5m,
            TakerFeePercent = RiskEngine.DefaultTakerFeePercent,
            SlippagePercent = RiskEngine.DefaultSlippagePercent
        }
    };

    private static NewsArticle Article() => new()
    {
        Provider = "gdelt",
        ProviderArticleId = "article-1",
        CanonicalUrl = "https://example.com/a",
        Title = "Solana upgrade",
        Summary = "Solana upgrade",
        Source = "example",
        PublishedAtUtc = DateTimeOffset.UtcNow,
        ReceivedAtUtc = DateTimeOffset.UtcNow
    };

    private sealed class CountingRisk : IRiskEngine
    {
        private readonly int _calls;
        public CountingRisk(ref int calls) => _calls = calls;
        public RiskEvaluation Evaluate(SignalType signal, RiskProfile profile, RiskSnapshot snapshot, DateTimeOffset utcNow)
        {
            throw new InvalidOperationException("NO_TRADE must not call the risk engine.");
        }
    }

    private sealed class ExplodingConnector : IExchangeConnector
    {
        public string Name => "fake";
        public TradingMode Mode => TradingMode.Paper;
        public bool Called { get; private set; }
        public Task<ExchangeAccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExchangeBalance>> GetBalancesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SymbolFilters> GetSymbolInformationAsync(string symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExchangeOrder?> GetOrderAsync(string? clientOrderId, string? exchangeOrderId, string symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PrepareSymbolRiskAsync(string symbol, MarginMode marginMode, int leverage, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> GetMaxIsolatedLeverageAsync(string symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExchangeOrder> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
        {
            Called = true;
            throw new InvalidOperationException("Live order must not be sent.");
        }
        public Task<ProtectiveStopsResult> PlaceClosePositionStopsAsync(string symbol, OrderSide closeSide, decimal stopLossPrice, decimal takeProfitPrice, string stopClientOrderId, string takeProfitClientOrderId, CancellationToken cancellationToken = default, bool placeStop = true, bool placeTake = true, bool acceptExisting = true) => throw new NotSupportedException();
        public Task CancelOrderAsync(string symbol, string? clientOrderId, string? exchangeOrderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CancelAllOrdersAsync(string symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SubscribeMarketDataAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SubscribeUserDataAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
