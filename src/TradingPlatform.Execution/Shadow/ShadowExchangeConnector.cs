using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Execution.Shadow;

public interface IShadowPriceFeed
{
    Task<FuturesBookTicker?> GetBookAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, FuturesPremiumIndex>> GetPremiumAsync(CancellationToken cancellationToken = default);
}

/// <summary>Real Binance book and premium-index prices for the shadow exchange, cached briefly so a cycle makes one call.</summary>
public sealed class PublicShadowPriceFeed : IShadowPriceFeed
{
    private static readonly TimeSpan BookTtl = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PremiumTtl = TimeSpan.FromSeconds(2);
    private readonly IPublicMarketDataClient _market;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, Dictionary<string, FuturesBookTicker> Rows) _books = (DateTimeOffset.MinValue, new());
    private (DateTimeOffset At, Dictionary<string, FuturesPremiumIndex> Rows) _premium = (DateTimeOffset.MinValue, new());

    public PublicShadowPriceFeed(IPublicMarketDataClient market, IClock clock)
    {
        _market = market;
        _clock = clock;
    }

    public async Task<FuturesBookTicker?> GetBookAsync(string symbol, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_clock.UtcNow - _books.At > BookTtl)
            {
                var rows = await _market.GetBookTickersAsync(cancellationToken);
                _books = (_clock.UtcNow, rows
                    .GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase));
            }

            return _books.Rows.GetValueOrDefault(symbol);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, FuturesPremiumIndex>> GetPremiumAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_clock.UtcNow - _premium.At > PremiumTtl)
            {
                var rows = await _market.GetPremiumIndexAsync(cancellationToken);
                _premium = (_clock.UtcNow, rows
                    .GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase));
            }

            return _premium.Rows;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// <see cref="IExchangeConnector"/> over <see cref="ShadowExchange"/>. The engine, risk, sizing and reconciler run unchanged;
/// only fills are simulated. Nothing here holds or sends an API key.
/// </summary>
public sealed class ShadowExchangeConnector : IExchangeConnector
{
    private readonly ShadowExchange _exchange;
    private readonly IShadowPriceFeed _prices;
    private readonly IPublicMarketDataClient _market;
    private readonly IClock _clock;

    public ShadowExchangeConnector(ShadowExchange exchange, IShadowPriceFeed prices, IPublicMarketDataClient market, IClock clock)
    {
        _exchange = exchange;
        _prices = prices;
        _market = market;
        _clock = clock;
    }

    public string Name => "binance-shadow";

    public TradingMode Mode => TradingMode.Live;

    public async Task<ExchangeAccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default) =>
        new("USDM_SHADOW", await GetBalancesAsync(cancellationToken), true);

    public async Task<IReadOnlyList<ExchangeBalance>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        var account = _exchange.Account(await MarksAsync(cancellationToken));
        return [new ExchangeBalance(ShadowExchange.FeeAsset, account.Available, account.UsedMargin)];
    }

    public async Task<SymbolFilters> GetSymbolInformationAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var name = symbol.ToUpperInvariant();
        var ranked = (await _market.GetPaperUniverseAsync(cancellationToken))
            .FirstOrDefault(row => string.Equals(row.Symbol, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainException(ErrorCodes.InvalidSymbol, $"{name} is not a Binance USD-M USDT perpetual.");
        return new SymbolFilters(
            ranked.Symbol,
            ranked.BaseAsset,
            ranked.QuoteAsset,
            ranked.TickSize,
            ranked.StepSize,
            ranked.MinQuantity,
            ranked.MinNotional,
            ranked.PricePrecision,
            ranked.QuantityPrecision);
    }

    public Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult(_exchange.OpenOrders(symbol));

    public Task<OrderLookup> GetOrderAsync(string? clientOrderId, string? exchangeOrderId, string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult(_exchange.Lookup(clientOrderId, exchangeOrderId));

    public Task PrepareSymbolRiskAsync(string symbol, MarginMode marginMode, int leverage, CancellationToken cancellationToken = default)
    {
        _exchange.SetLeverage(symbol, leverage);
        return Task.CompletedTask;
    }

    public Task<int> GetMaxIsolatedLeverageAsync(string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult(Math.Max(1, _exchange.Options.MaxLeverage));

    public async Task<ExchangeOrder> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        var book = await _prices.GetBookAsync(request.Symbol, cancellationToken);
        return _exchange.PlaceMarket(request, book, await MarksAsync(cancellationToken), _clock.UtcNow);
    }

    public async Task<ProtectiveStopsResult> PlaceClosePositionStopsAsync(
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
        var marks = await MarksAsync(cancellationToken);
        decimal? mark = marks.TryGetValue(symbol, out var value) ? value : null;
        return _exchange.PlaceStops(
            symbol,
            closeSide,
            stopLossPrice,
            takeProfitPrice,
            stopClientOrderId,
            takeProfitClientOrderId,
            mark,
            placeStop,
            placeTake,
            acceptExisting,
            _clock.UtcNow);
    }

    public Task CancelOrderAsync(string symbol, string? clientOrderId, string? exchangeOrderId, CancellationToken cancellationToken = default)
    {
        _exchange.Cancel(clientOrderId, exchangeOrderId);
        return Task.CompletedTask;
    }

    public Task CancelAllOrdersAsync(string symbol, CancellationToken cancellationToken = default)
    {
        _exchange.CancelAll(symbol);
        return Task.CompletedTask;
    }

    public Task SubscribeMarketDataAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SubscribeUserDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) =>
        _exchange.Positions(await MarksAsync(cancellationToken));

    public Task<bool?> IsHedgeModeAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(false);

    public Task<decimal?> GetTakerFeePercentAsync(string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult<decimal?>(_exchange.Options.TakerFeePercent);

    private async Task<IReadOnlyDictionary<string, decimal>> MarksAsync(CancellationToken cancellationToken)
    {
        var premium = await _prices.GetPremiumAsync(cancellationToken);
        return premium.ToDictionary(pair => pair.Key, pair => pair.Value.MarkPrice, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ShadowExchangeConnectorFactory : ILiveExchangeConnectorFactory
{
    private readonly ShadowExchange _exchange;
    private readonly IShadowPriceFeed _prices;
    private readonly IPublicMarketDataClient _market;
    private readonly IClock _clock;

    public ShadowExchangeConnectorFactory(ShadowExchange exchange, IShadowPriceFeed prices, IPublicMarketDataClient market, IClock clock)
    {
        _exchange = exchange;
        _prices = prices;
        _market = market;
        _clock = clock;
    }

    public IExchangeConnector Create(Guid? exchangeAccountId) => new ShadowExchangeConnector(_exchange, _prices, _market, _clock);
}
