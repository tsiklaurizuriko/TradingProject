using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Execution;

public sealed class PaperExchangeConnector : IExchangeConnector
{
    private readonly IMarketDataCache _cache;
    private readonly IClock _clock;
    private readonly TradingOptions _options;

    public PaperExchangeConnector(IMarketDataCache cache, IClock clock, IOptions<TradingOptions> options)
    {
        _cache = cache;
        _clock = clock;
        _options = options.Value;
    }

    public string Name => "PaperSimulator";
    public TradingMode Mode => TradingMode.Paper;

    public Task<ExchangeAccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ExchangeAccountSnapshot("PAPER", [], true));

    public Task<IReadOnlyList<ExchangeBalance>> GetBalancesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExchangeBalance>>([]);

    public Task<SymbolFilters> GetSymbolInformationAsync(string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SymbolFilters(
            symbol.ToUpperInvariant(),
            symbol.ToUpperInvariant().Replace("USDT", "", StringComparison.Ordinal),
            "USDT",
            0.01m,
            0.00001m,
            0.00001m,
            5m,
            2,
            5));

    public Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExchangeOrder>>([]);

    public Task<ExchangeOrder?> GetOrderAsync(string? clientOrderId, string? exchangeOrderId, string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult<ExchangeOrder?>(null);

    public Task PrepareSymbolRiskAsync(string symbol, MarginMode marginMode, int leverage, CancellationToken cancellationToken = default)
    {
        if (marginMode != MarginMode.Isolated)
        {
            throw new DomainException(ErrorCodes.RiskLimitExceeded, "Isolated margin only. Cross is not allowed.");
        }

        return Task.CompletedTask;
    }

    public Task<int> GetMaxIsolatedLeverageAsync(string symbol, CancellationToken cancellationToken = default) =>
        Task.FromResult(20);

    public Task<ExchangeOrder> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetTicker(request.Symbol, out var lastPrice) || lastPrice <= 0m)
        {
            throw new DomainException(ErrorCodes.ExchangeUnavailable, "Paper simulator has no last price yet.");
        }

        var fillPrice = PaperFillModel.ApplySlippage(lastPrice, request.Side, _options.PaperSlippageBps);
        var now = _clock.UtcNow;
        return Task.FromResult(new ExchangeOrder(
            request.ClientOrderId,
            PaperFillModel.NewPaperOrderId(),
            request.Symbol.ToUpperInvariant(),
            request.Side,
            request.Type,
            OrderStatus.Filled,
            request.Quantity,
            request.Quantity,
            fillPrice,
            fillPrice,
            now));
    }

    public Task<ProtectiveStopsResult> PlaceClosePositionStopsAsync(
        string symbol,
        OrderSide closeSide,
        decimal stopLossPrice,
        decimal takeProfitPrice,
        string stopClientOrderId,
        string takeProfitClientOrderId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProtectiveStopsResult(true, true));

    public Task CancelOrderAsync(string symbol, string? clientOrderId, string? exchangeOrderId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CancelAllOrdersAsync(string symbol, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SubscribeMarketDataAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SubscribeUserDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class ExchangeConnectorFactory : IExchangeConnectorFactory
{
    private readonly PaperExchangeConnector _paper;
    private readonly ILiveExchangeConnectorFactory? _live;

    public ExchangeConnectorFactory(PaperExchangeConnector paper, IEnumerable<ILiveExchangeConnectorFactory> live)
    {
        _paper = paper;
        _live = live.FirstOrDefault();
    }

    public IExchangeConnector Create(TradingMode mode, Guid? exchangeAccountId) =>
        mode switch
        {
            TradingMode.Paper => _paper,
            TradingMode.Live => _live?.Create(exchangeAccountId)
                ?? throw new DomainException(
                    ErrorCodes.LiveTradingDisabled,
                    "Live trading is not wired in this process."),
            TradingMode.Testnet => throw new DomainException(
                ErrorCodes.ExchangeUnavailable,
                "Testnet execution is not enabled yet. Use paper or live."),
            _ => throw new DomainException(ErrorCodes.ValidationFailed, $"Unknown trading mode '{mode}'.")
        };
}
