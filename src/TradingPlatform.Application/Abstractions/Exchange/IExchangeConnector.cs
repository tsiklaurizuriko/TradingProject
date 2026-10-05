using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Positions;
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
    decimal Fee = 0m,
    bool FeeKnown = false,
    decimal? CumulativeQuote = null,
    string? FeeAsset = null);

/// <summary>
/// Result of a read-only order query. <see cref="OrderLookupKind.ConfirmedAbsent"/> is used only when the
/// exchange states that the client id was never accepted. A null body, timeout, 429, or 5xx is
/// <see cref="OrderLookupKind.Unavailable"/> and must not release an entry lock.
/// </summary>
public enum OrderLookupKind
{
    Found,
    ConfirmedAbsent,
    Unavailable
}

public sealed record OrderLookup(OrderLookupKind Kind, ExchangeOrder? Order, string Reason)
{
    public static OrderLookup Found(ExchangeOrder order) =>
        new(OrderLookupKind.Found, order, "Exchange returned the order.");

    public static OrderLookup Absent(string reason) =>
        new(OrderLookupKind.ConfirmedAbsent, null, reason);

    public static OrderLookup Unavailable(string reason) =>
        new(OrderLookupKind.Unavailable, null, reason);
}

public sealed record PlaceOrderRequest(
    string ClientOrderId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? Price,
    TimeSpan? Timeout,
    bool ReduceOnly = false);

/// <param name="StopRestored">The new stop failed but the stop that was working before the replace is back on the book.</param>
public sealed record ProtectiveStopsResult(
    bool StopPlaced,
    bool TakePlaced,
    string? StopError = null,
    string? TakeError = null,
    bool StopRestored = false)
{
    /// <summary>A stop is working on the exchange: either the requested one or the previous one.</summary>
    public bool HasWorkingStop => StopPlaced || StopRestored;
}

/// <summary>One open USD-M position as the exchange reports it. Quantity is unsigned; Side says the direction.</summary>
public sealed record ExchangePosition(string Symbol, PositionSide Side, decimal Quantity, decimal EntryPrice, decimal MarkPrice);

public static class ProtectiveOrderMath
{
    public static bool IsStopOrder(string? type) =>
        Contains(type, "STOP") && !Contains(type, "TAKE");

    public static bool IsTakeOrder(string? type) => Contains(type, "TAKE");

    public static bool IsExistingProtectiveOrder(string? message) =>
        Contains(message, "-4130")
        || Contains(message, "-4116")
        || Contains(message, "duplicat");

    /// <summary>Binance -4509: a closePosition stop needs an open position. Retrying cannot succeed until one exists.</summary>
    public static bool IsNoOpenPosition(string? message) =>
        Contains(message, "-4509");

    public static decimal RestingTrigger(decimal mark, decimal tickSize, bool closingShort, bool stop)
    {
        var tick = tickSize > 0m ? tickSize : 0.00000001m;
        if (mark <= 0m)
        {
            return 0m;
        }

        var above = closingShort == stop;
        var raw = above ? mark + tick : Math.Max(tick, mark - tick);
        var steps = raw / tick;
        var rounded = (above ? Math.Ceiling(steps) : Math.Floor(steps)) * tick;
        return rounded > 0m ? rounded : tick;
    }

    private static bool Contains(string? value, string needle) =>
        value?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;
}

public interface IExchangeConnector
{
    string Name { get; }
    TradingMode Mode { get; }
    Task<ExchangeAccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExchangeBalance>> GetBalancesAsync(CancellationToken cancellationToken = default);
    Task<SymbolFilters> GetSymbolInformationAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken = default);
    Task<OrderLookup> GetOrderAsync(string? clientOrderId, string? exchangeOrderId, string symbol, CancellationToken cancellationToken = default);
    Task PrepareSymbolRiskAsync(string symbol, MarginMode marginMode, int leverage, CancellationToken cancellationToken = default);
    /// <summary>Highest Isolated leverage the exchange allows. Zero means the cap could not be read.</summary>
    Task<int> GetMaxIsolatedLeverageAsync(string symbol, CancellationToken cancellationToken = default);
    Task<ExchangeOrder> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default);
    Task<ProtectiveStopsResult> PlaceClosePositionStopsAsync(
        string symbol,
        OrderSide closeSide,
        decimal stopLossPrice,
        decimal takeProfitPrice,
        string stopClientOrderId,
        string takeProfitClientOrderId,
        CancellationToken cancellationToken = default,
        bool placeStop = true,
        bool placeTake = true,
        bool acceptExisting = true);
    Task CancelOrderAsync(string symbol, string? clientOrderId, string? exchangeOrderId, CancellationToken cancellationToken = default);
    Task CancelAllOrdersAsync(string symbol, CancellationToken cancellationToken = default);
    Task SubscribeMarketDataAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default);
    Task SubscribeUserDataAsync(CancellationToken cancellationToken = default);

    /// <summary>Every non-zero position on the account, read from the exchange (not from the local book).</summary>
    Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExchangePosition>>([]);

    /// <summary>
    /// True when the account uses hedge (dual-side) position mode. Order payloads in this project assume
    /// one-way mode, so anything other than <c>false</c> must block new entries. Null means it could not be read.
    /// </summary>
    Task<bool?> IsHedgeModeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<bool?>(false);

    /// <summary>This account's taker commission on the coin, in percent (0.04 = 0.04%). Null when it could not be read.</summary>
    Task<decimal?> GetTakerFeePercentAsync(string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult<decimal?>(null);

    /// <summary>Maintenance margin of the leverage bracket that holds <paramref name="notional"/>. Null when it could not be read.</summary>
    Task<MaintenanceBracket?> GetMaintenanceBracketAsync(string symbol, decimal notional, CancellationToken cancellationToken = default) =>
        Task.FromResult<MaintenanceBracket?>(null);
}

/// <summary>Rate is a fraction (0.004 = 0.4%). Amount is Binance's "cum" maintenance amount in USDT.</summary>
public sealed record MaintenanceBracket(decimal Rate, decimal Amount);

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
    public bool FuturesBookFresh { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public interface ILiveAccountCache
{
    LiveAccountSnapshot Current { get; }
    void Set(LiveAccountSnapshot snapshot);
}
