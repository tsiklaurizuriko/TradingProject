using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.PriceAction;

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
    private readonly Dictionary<int, PriceActionBook> _priceAction = new();

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

    /// <summary>Pandas rolling std (sample, ddof=1), matching qtpylib bollinger_bands.</summary>
    public (decimal?[] Mid, decimal?[] Upper, decimal?[] Lower) SampleBollinger(int period, decimal stdDev, bool typicalPrice)
    {
        var key = $"bb-sample:{(typicalPrice ? "tp" : "c")}:{period}:{stdDev}";
        if (_triples.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var prices = new decimal[_candles.Count];
        for (var i = 0; i < _candles.Count; i++)
        {
            var candle = _candles[i];
            prices[i] = typicalPrice ? (candle.High + candle.Low + candle.Close) / 3m : candle.Close;
        }

        var mid = new decimal?[prices.Length];
        var upper = new decimal?[prices.Length];
        var lower = new decimal?[prices.Length];
        for (var i = period - 1; i < prices.Length; i++)
        {
            decimal sum = 0m;
            for (var j = i - period + 1; j <= i; j++)
            {
                sum += prices[j];
            }

            var mean = sum / period;
            decimal variance = 0m;
            for (var j = i - period + 1; j <= i; j++)
            {
                var delta = prices[j] - mean;
                variance += delta * delta;
            }

            var sd = period > 1 ? (decimal)Math.Sqrt((double)(variance / (period - 1))) : 0m;
            mid[i] = mean;
            upper[i] = mean + stdDev * sd;
            lower[i] = mean - stdDev * sd;
        }

        var computed = (mid, upper, lower);
        _triples[key] = computed;
        return computed;
    }

    /// <summary>Mean of the previous <paramref name="period"/> volumes, excluding the current bar.</summary>
    public IReadOnlyList<decimal?> PriorVolumeMean(int period) =>
        Get($"volprior:{period}", () =>
        {
            var result = new decimal?[_candles.Count];
            decimal sum = 0m;
            for (var i = 0; i < _candles.Count; i++)
            {
                if (i >= period)
                {
                    result[i] = sum / period;
                    sum -= _candles[i - period].Volume;
                }

                sum += _candles[i].Volume;
            }

            return result;
        });

    /// <summary>Wilder RSI of (open + close) / 2. Freqtrade hlhb names that series hl2.</summary>
    public IReadOnlyList<decimal?> OpenCloseMidRsi(int period) =>
        Get($"ocmidrsi:{period}", () =>
        {
            var result = new decimal?[_candles.Count];
            if (period < 1 || _candles.Count <= period)
            {
                return result;
            }

            decimal gain = 0m, loss = 0m;
            decimal Prev(int index) => (_candles[index].Open + _candles[index].Close) / 2m;
            for (var i = 1; i <= period; i++)
            {
                var change = Prev(i) - Prev(i - 1);
                if (change >= 0m) gain += change;
                else loss -= change;
            }

            var avgGain = gain / period;
            var avgLoss = loss / period;
            result[period] = avgLoss == 0m ? 100m : 100m - 100m / (1m + avgGain / avgLoss);
            for (var i = period + 1; i < _candles.Count; i++)
            {
                var change = Prev(i) - Prev(i - 1);
                var g = change > 0m ? change : 0m;
                var l = change < 0m ? -change : 0m;
                avgGain = (avgGain * (period - 1) + g) / period;
                avgLoss = (avgLoss * (period - 1) + l) / period;
                result[i] = avgLoss == 0m ? 100m : 100m - 100m / (1m + avgGain / avgLoss);
            }

            return result;
        });

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

    public IReadOnlyList<decimal?> ContrarianSmaPosition(int fast, int slow, decimal bandwidth) =>
        Get($"mac:{fast}:{slow}:{bandwidth}", () => ContrarianSma(_candles, fast, slow, bandwidth));

    private static IReadOnlyList<decimal?> ContrarianSma(IReadOnlyList<MarketCandle> candles, int fast, int slow, decimal bandwidth)
    {
        var result = new decimal?[candles.Count];
        decimal carried = 0m;
        for (var i = 0; i < candles.Count; i++)
        {
            var fastMa = SmaClose(candles, i, fast);
            var slowMa = SmaClose(candles, i, slow);
            if (fastMa is { } f && slowMa is { } s && s != 0m)
            {
                if (f > s * (1m + bandwidth))
                {
                    carried = 1m;
                }
                else if (f < s * (1m - bandwidth))
                {
                    carried = -1m;
                }
            }

            result[i] = carried == 0m ? 0m : -carried;
        }

        return result;
    }

    private static decimal? SmaClose(IReadOnlyList<MarketCandle> candles, int index, int period)
    {
        if (period < 1 || index < period - 1)
        {
            return null;
        }

        decimal sum = 0m;
        for (var j = index - period + 1; j <= index; j++)
        {
            sum += candles[j].Close;
        }

        return sum / period;
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

    public PriceActionBook PriceAction(int swingN = PriceActionBook.DefaultSwing)
    {
        if (_priceAction.TryGetValue(swingN, out var cached))
        {
            return cached;
        }

        var computed = PriceActionBook.Build(_candles, swingN);
        _priceAction[swingN] = computed;
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
