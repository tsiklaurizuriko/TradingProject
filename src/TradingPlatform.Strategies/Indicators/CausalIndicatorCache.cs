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

    public IReadOnlyList<decimal?> Atr(int period) => Get($"atr:{period}", () => new AtrIndicator(period).Compute(_candles));

    public IReadOnlyList<decimal?> SessionVwap() => Get("vwap:session", () => ResearchIndicatorSeries.SessionVwap(_candles));

    public IReadOnlyList<decimal?> Adx(int period) => Get($"adx:{period}", () => ResearchIndicatorSeries.Adx(_candles, period));

    public IReadOnlyList<decimal?> SupertrendDirection(int period, decimal multiplier) =>
        Get($"st:{period}:{multiplier}", () => ResearchIndicatorSeries.SupertrendDirection(_candles, period, multiplier));

    public IReadOnlyList<decimal?> SupertrendLine(int period, decimal multiplier) =>
        Get($"stline:{period}:{multiplier}", () => ResearchIndicatorSeries.SupertrendLine(_candles, period, multiplier));

    public IReadOnlyList<decimal?> BollingerWidth(int period, decimal stdDev) =>
        Get($"bbw:{period}:{stdDev}", () => ResearchIndicatorSeries.BollingerWidth(_candles, period, stdDev));

    public IReadOnlyList<decimal?> BollingerWidthPercentile(int period, decimal stdDev, int lookback) =>
        Get($"bbwp:{period}:{stdDev}:{lookback}", () =>
            ResearchIndicatorSeries.PercentileRank(BollingerWidth(period, stdDev), lookback));

    public IReadOnlyList<decimal?> AtrSma(int atrPeriod, int smaPeriod) =>
        Get($"atrsma:{atrPeriod}:{smaPeriod}", () => ResearchIndicatorSeries.Sma(Atr(atrPeriod), smaPeriod));

    public IReadOnlyList<decimal?> AtrPercentile(int period, int lookback) =>
        Get($"atrpctl:{period}:{lookback}", () => ResearchIndicatorSeries.AtrPercentile(_candles, period, lookback));

    public IReadOnlyList<decimal?> RelativeVolume(int lookback) =>
        Get($"relvol:{lookback}", () => ResearchIndicatorSeries.RelativeVolume(_candles, lookback));

    public IReadOnlyList<decimal?> EmaSlope(int period) => Get($"emaslope:{period}", () =>
    {
        var ema = Ema(period);
        var slope = new decimal?[ema.Count];
        for (var i = 1; i < ema.Count; i++)
        {
            if (ema[i] is { } now && ema[i - 1] is { } prev)
            {
                slope[i] = now - prev;
            }
        }

        return slope;
    });

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

    public (decimal?[] Poc, decimal?[] Vah, decimal?[] Val) VolumeProfile(int window, decimal valueAreaPercent)
    {
        var key = $"vp:{window}:{valueAreaPercent}";
        if (_triples.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var computed = AlphaIndicatorSeries.VolumeProfile(_candles, window, valueAreaPercent);
        _triples[key] = computed;
        return computed;
    }

    public (decimal?[] Mid, decimal?[] Upper, decimal?[] Lower) Keltner(int period, decimal multiplier)
    {
        var key = $"kc:{period}:{multiplier}";
        if (_triples.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var computed = AlphaIndicatorSeries.Keltner(_candles, period, multiplier);
        _triples[key] = computed;
        return computed;
    }

    public IReadOnlyList<decimal?> ConfirmedSwingLow(int n) =>
        Get($"swinglo:{n}", () => AlphaIndicatorSeries.ConfirmedSwingLow(_candles, n));

    public IReadOnlyList<decimal?> ConfirmedSwingHigh(int n) =>
        Get($"swinghi:{n}", () => AlphaIndicatorSeries.ConfirmedSwingHigh(_candles, n));

    public IReadOnlyList<decimal?> StructureBias(int n) =>
        Get($"struct:{n}", () => AlphaIndicatorSeries.StructureBias(_candles, n));

    public IReadOnlyList<decimal?> CloseZScore(int period) =>
        Get($"zclose:{period}", () => AlphaIndicatorSeries.CloseZScore(_candles, period));

    public IReadOnlyList<decimal?> TakerImbalance() =>
        Get("takerimb", () => AlphaIndicatorSeries.TakerImbalance(_candles));

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
