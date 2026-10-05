using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Application.Abstractions.MarketData;

public sealed record RankedUsdtSpotSymbol(
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    decimal QuoteVolume,
    decimal LastPrice,
    decimal PriceChangePercent,
    decimal TickSize,
    decimal StepSize,
    decimal MinQuantity,
    decimal MinNotional,
    int PricePrecision,
    int QuantityPrecision,
    decimal HighPrice = 0,
    decimal LowPrice = 0,
    int Trades24h = 0);

public sealed record CachedTicker(string Symbol, decimal Price, DateTimeOffset Timestamp);

public interface IPublicMarketDataClient
{
    Task<IReadOnlyList<MarketCandle>> GetClosedKlinesAsync(
        string symbol,
        Timeframe timeframe,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketCandle>> GetClosedKlinesRangeAsync(
        string symbol,
        Timeframe timeframe,
        DateTimeOffset start,
        DateTimeOffset end,
        int limit,
        CancellationToken cancellationToken = default);

    Task<decimal> GetLastPriceAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>One ticker/price call for every symbol. Weight 2, not one request per coin.</summary>
    Task<IReadOnlyDictionary<string, decimal>> GetLastPricesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RankedUsdtSpotSymbol>> GetPaperUniverseAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscoveredFuturesContract>> DiscoverUsdtPerpetualsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FuturesBookTicker>> GetBookTickersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FuturesPremiumIndex>> GetPremiumIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>Last two hourly open-interest prints. Null when Binance does not return them.</summary>
    Task<(decimal? Previous, decimal? Latest)> GetOpenInterestPairAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Open interest about 24 hours ago and the latest hourly print. Null when the series is short.</summary>
    Task<(decimal? DayAgo, decimal? Latest)> GetOpenInterestDayAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Current 8-hour funding rate from the premium index. Null when the symbol is missing.</summary>
    Task<decimal?> GetLastFundingRateAsync(string symbol, CancellationToken cancellationToken = default);
}

public interface IMarketDataCache
{
    void SetTicker(string symbol, decimal price, DateTimeOffset timestamp);
    bool TryGetTicker(string symbol, out decimal price);
    IReadOnlyList<CachedTicker> GetTickers();
    void SetKlines(string symbol, Timeframe timeframe, IReadOnlyList<MarketCandle> candles);
    IReadOnlyList<MarketCandle> GetKlines(string symbol, Timeframe timeframe);
    IReadOnlyList<string> GetKlineSymbols(Timeframe timeframe);
}

public interface ITradingRealtimePublisher
{
    Task PublishTickerAsync(string symbol, decimal price, DateTimeOffset timestamp, CancellationToken cancellationToken = default);
    Task PublishOverviewAsync(CancellationToken cancellationToken = default);
    Task PublishBotAsync(Guid botId, string status, CancellationToken cancellationToken = default);
}
