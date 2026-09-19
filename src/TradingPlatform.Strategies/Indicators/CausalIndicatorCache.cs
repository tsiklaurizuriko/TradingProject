using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.Indicators;

/// <summary>
/// Causal indicator series for one immutable candle list. Values at index i depend only on candles 0..i.
/// Safe to share across independent strategy execution states and evaluation windows.
/// Do not reuse a cache with a different candle list.
/// </summary>
public sealed class CausalIndicatorCache
{
    private readonly IReadOnlyList<MarketCandle> _candles;
    private readonly Dictionary<string, IReadOnlyList<decimal?>> _series = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (decimal?[] A, decimal?[] B, decimal?[] C)> _triples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (decimal?[] A, decimal?[] B)> _pairs = new(StringComparer.Ordinal);

    public CausalIndicatorCache(IReadOnlyList<MarketCandle> candles) =>
        _candles = candles ?? throw new ArgumentNullException(nameof(candles));

    public IReadOnlyList<MarketCandle> Candles => _candles;

    public IReadOnlyList<decimal?> Ema(int period) => Get($"ema:{period}", () => new EmaIndicator(period).Compute(_candles));

    public IReadOnlyList<decimal?> Rsi(int period) => Get($"rsi:{period}", () => new RsiIndicator(period).Compute(_candles));

    public IReadOnlyList<decimal?> AvgVolume(int period) => Get($"avgvol:{period}", () => new AverageVolumeIndicator(period).Compute(_candles));

    public IReadOnlyList<decimal?> AtrPercent(int period) => Get($"atrpct:{period}", () => new AtrPercentIndicator(period).Compute(_candles));

    public (decimal?[] Macd, decimal?[] Signal, decimal?[] Histogram) Macd(int fast, int slow, int signal)
    {
        var key = $"macd:{fast}:{slow}:{signal}";
        if (_triples.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var computed = MacdSeries.Compute(_candles, fast, slow, signal);
        _triples[key] = computed;
        return computed;
    }

    public (decimal?[] Mid, decimal?[] Upper, decimal?[] Lower) Bollinger(int period, decimal stdDev)
    {
        var key = $"bb:{period}:{stdDev}";
        if (_triples.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var computed = BollingerSeries.Compute(_candles, period, stdDev);
        _triples[key] = computed;
        return computed;
    }

    public (decimal?[] High, decimal?[] Low) Donchian(int length)
    {
        var key = $"dc:{length}";
        if (_pairs.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var computed = DonchianSeries.Compute(_candles, length);
        _pairs[key] = computed;
        return computed;
    }

    private IReadOnlyList<decimal?> Get(string key, Func<IReadOnlyList<decimal?>> factory)
    {
        if (_series.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var computed = factory();
        _series[key] = computed;
        return computed;
    }
}
