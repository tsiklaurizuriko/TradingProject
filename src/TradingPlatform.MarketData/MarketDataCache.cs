using System.Collections.Concurrent;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.MarketData;

public sealed class MarketDataCache : IMarketDataCache
{
    private readonly ConcurrentDictionary<string, (decimal Price, DateTimeOffset Timestamp)> _tickers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<MarketCandle>> _klines = new(StringComparer.OrdinalIgnoreCase);

    public void SetTicker(string symbol, decimal price, DateTimeOffset timestamp) =>
        _tickers[symbol.ToUpperInvariant()] = (price, timestamp);

    public bool TryGetTicker(string symbol, out decimal price)
    {
        if (_tickers.TryGetValue(symbol.ToUpperInvariant(), out var ticker))
        {
            price = ticker.Price;
            return true;
        }

        price = 0m;
        return false;
    }

    public IReadOnlyList<CachedTicker> GetTickers() =>
        _tickers.Select(kv => new CachedTicker(kv.Key, kv.Value.Price, kv.Value.Timestamp))
            .OrderBy(t => t.Symbol)
            .ToList();

    public void SetKlines(string symbol, Timeframe timeframe, IReadOnlyList<MarketCandle> candles) =>
        _klines[Key(symbol, timeframe)] = candles;

    public IReadOnlyList<MarketCandle> GetKlines(string symbol, Timeframe timeframe) =>
        _klines.TryGetValue(Key(symbol, timeframe), out var candles) ? candles : [];

    private static string Key(string symbol, Timeframe timeframe) => $"{symbol.ToUpperInvariant()}:{timeframe}";
}
