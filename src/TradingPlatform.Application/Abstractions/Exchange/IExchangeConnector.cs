using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Application.Abstractions.Exchange;

public sealed record ExchangeBalance(string Asset, decimal Free, decimal Locked);

public sealed record ExchangeAccountSnapshot(
    string AccountType,
    IReadOnlyList<ExchangeBalance> Balances,
    bool CanTrade);

public sealed record SymbolFilters(
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    decimal TickSize,
    decimal StepSize,
    decimal MinQuantity,
    decimal MinNotional,
    int PricePrecision,
    int QuantityPrecision);

public sealed record ExchangeOrder(
    string ClientOrderId,
    string? ExchangeOrderId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    OrderStatus Status,
    decimal Quantity,
    decimal FilledQuantity,
    decimal? Price,
    decimal? AverageFillPrice,
    DateTimeOffset? ExchangeTimestamp,
    decimal Fee = 0m);

public sealed record PlaceOrderRequest(
    string ClientOrderId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? Price,
    TimeSpan? Timeout,
    bool ReduceOnly = false);

public sealed record ProtectiveStopsResult(
    bool StopPlaced,
    bool TakePlaced,
    string? StopError = null,
    string? TakeError = null);

public interface IExchangeConnector
{
    string Name { get; }
    TradingMode Mode { get; }
    Task<ExchangeAccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExchangeBalance>> GetBalancesAsync(CancellationToken cancellationToken = default);
    Task<SymbolFilters> GetSymbolInformationAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken = default);
    Task<ExchangeOrder?> GetOrderAsync(string? clientOrderId, string? exchangeOrderId, string symbol, CancellationToken cancellationToken = default);
    Task PrepareSymbolRiskAsync(string symbol, MarginMode marginMode, int leverage, CancellationToken cancellationToken = default);
    Task<int> GetMaxIsolatedLeverageAsync(string symbol, CancellationToken cancellationToken = default);
    Task<ExchangeOrder> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default);
    Task<ProtectiveStopsResult> PlaceClosePositionStopsAsync(
        string symbol,
        OrderSide closeSide,
        decimal stopLossPrice,
        decimal takeProfitPrice,
        string stopClientOrderId,
        string takeProfitClientOrderId,
        CancellationToken cancellationToken = default);
    Task CancelOrderAsync(string symbol, string? clientOrderId, string? exchangeOrderId, CancellationToken cancellationToken = default);
    Task CancelAllOrdersAsync(string symbol, CancellationToken cancellationToken = default);
    Task SubscribeMarketDataAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default);
    Task SubscribeUserDataAsync(CancellationToken cancellationToken = default);
}

public interface IExchangeConnectorFactory
{
    IExchangeConnector Create(TradingMode mode, Guid? exchangeAccountId);
}

public interface ILiveExchangeConnectorFactory
{
    IExchangeConnector Create(Guid? exchangeAccountId);
}

public interface IExchangeCredentialStore
{
    Task StoreAsync(Guid exchangeAccountId, string apiKey, string apiSecret, CancellationToken cancellationToken = default);
    Task<(string ApiKey, string ApiSecret)?> GetAsync(Guid exchangeAccountId, CancellationToken cancellationToken = default);
    Task<ExchangeAccount?> GetLiveAccountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ExchangeAccount> GetOrCreateLiveAccountAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record LiveHolding(string Asset, decimal Quantity);

public sealed record LiveOpenOrder(
    string Symbol,
    string Side,
    string Type,
    string Status,
    decimal Quantity,
    decimal FilledQuantity,
    decimal? Price,
    string? ExchangeOrderId,
    string? ClientOrderId,
    DateTimeOffset CreatedAt,
    string Venue);

public sealed record LiveOpenPosition(
    string Symbol,
    string Side,
    decimal Quantity,
    decimal EntryPrice,
    decimal MarkPrice,
    decimal UnrealizedPnL,
    string Venue);

public sealed class LiveAccountSnapshot
{
    public bool HasKeys { get; set; }
    public bool CanTrade { get; set; }
    public string? ApiKeyHint { get; set; }
    public decimal? UsdtFree { get; set; }
    public decimal SpotUsdt { get; set; }
    public decimal FundingUsdt { get; set; }
    public decimal FuturesUsdt { get; set; }
    public decimal FuturesEquity { get; set; }
    public string? Message { get; set; }
    public IReadOnlyList<LiveHolding> Holdings { get; set; } = [];
    public IReadOnlyList<LiveOpenOrder> OpenOrders { get; set; } = [];
    public IReadOnlyList<LiveOpenPosition> OpenPositions { get; set; } = [];
    public DateTimeOffset? UpdatedAt { get; set; }
}

public interface ILiveAccountCache
{
    LiveAccountSnapshot Current { get; }
    void Set(LiveAccountSnapshot snapshot);
}
