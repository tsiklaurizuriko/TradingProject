using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Shared candle and indicator conventions for the six -new strategies.
/// EMA, ATR, RSI, ADX, and Bollinger bands are the existing implementations:
/// EMA is seeded with an SMA then uses alpha = 2/(n+1); ATR and RSI and ADX use Wilder smoothing;
/// Bollinger standard deviation divides by N (population), not N-1.
/// ADX measures trend strength, not direction.
/// </summary>
public static class ProductionNewMath
{
    public const decimal DefaultTakerFeeRate = 0.0004m;
    public const decimal DefaultSlippageRate = 0.0005m;
    public static readonly decimal RoundTripCostRate = (DefaultTakerFeeRate + DefaultSlippageRate) * 2m;

    public static bool BarUsable(MarketCandle candle) =>
        candle.Open > 0m
        && candle.High > 0m
        && candle.Low > 0m
        && candle.Close > 0m
        && candle.High >= candle.Low
        && candle.Open <= candle.High
        && candle.Open >= candle.Low
        && candle.Close <= candle.High
        && candle.Close >= candle.Low
        && candle.Volume >= 0m;

    public static bool HasGap(IReadOnlyList<MarketCandle> candles, int index, string? timeframe)
    {
        if (index < 1)
        {
            return false;
        }

        if (!TimeframeExtensions.TryParseInterval(timeframe ?? "", out var parsed))
        {
            return true;
        }

        var expected = parsed.ToDuration();
        if (expected <= TimeSpan.Zero)
        {
            return true;
        }

        var openToOpen = candles[index].OpenTime - candles[index - 1].OpenTime;
        return openToOpen > TimeSpan.FromTicks((long)(expected.Ticks * 1.5));
    }

    public static decimal? RelativeVolume(IReadOnlyList<MarketCandle> candles, int index, int period)
    {
        if (period < 2 || index < period - 1)
        {
            return null;
        }

        decimal sum = 0m;
        for (var k = index - period + 1; k <= index; k++)
        {
            if (candles[k].Volume < 0m)
            {
                return null;
            }

            sum += candles[k].Volume;
        }

        var average = sum / period;
        return average > 0m ? candles[index].Volume / average : null;
    }

    public static decimal? PriorHigh(IReadOnlyList<MarketCandle> candles, int index, int lookback) =>
        PriorExtreme(candles, index, lookback, high: true);

    public static decimal? PriorLow(IReadOnlyList<MarketCandle> candles, int index, int lookback) =>
        PriorExtreme(candles, index, lookback, high: false);

    public static decimal? ReturnTo(IReadOnlyList<MarketCandle> candles, int index, TimeSpan horizon, TimeSpan maxGap)
    {
        if (index < 1 || horizon <= TimeSpan.Zero || candles[index].Close <= 0m)
        {
            return null;
        }

        var target = candles[index].CloseTime - horizon;
        var found = -1;
        for (var k = index - 1; k >= 0; k--)
        {
            if (candles[k].CloseTime <= target)
            {
                found = k;
                break;
            }
        }

        if (found < 0 || candles[found].Close <= 0m)
        {
            return null;
        }

        if (target - candles[found].CloseTime > maxGap)
        {
            return null;
        }

        return candles[index].Close / candles[found].Close - 1m;
    }

    /// <summary>Population z-score. Null when any window value is missing, the window is short, or sigma is zero.</summary>
    public static decimal? PopulationZScore(IReadOnlyList<decimal?> series, int index, int window)
    {
        if (window < 2 || index < window - 1 || index >= series.Count || series[index] is not { } current)
        {
            return null;
        }

        decimal sum = 0m;
        for (var k = index - window + 1; k <= index; k++)
        {
            if (series[k] is not { } value)
            {
                return null;
            }

            sum += value;
        }

        var mean = sum / window;
        decimal square = 0m;
        for (var k = index - window + 1; k <= index; k++)
        {
            var delta = series[k]!.Value - mean;
            square += delta * delta;
        }

        var variance = square / window;
        if (variance <= 0m)
        {
            return null;
        }

        var sigma = (decimal)Math.Sqrt((double)variance);
        return sigma == 0m ? null : (current - mean) / sigma;
    }

    public static decimal? FullPercentile(IReadOnlyList<decimal?> series, int index, int window)
    {
        if (window < 5 || index < window - 1 || index >= series.Count || series[index] is not { } current)
        {
            return null;
        }

        var atOrBelow = 0;
        for (var k = index - window + 1; k <= index; k++)
        {
            if (series[k] is not { } value)
            {
                return null;
            }

            if (value <= current)
            {
                atOrBelow++;
            }
        }

        return (decimal)atOrBelow / window;
    }

    public static decimal? Pearson(IReadOnlyList<decimal> left, IReadOnlyList<decimal> right)
    {
        var n = Math.Min(left.Count, right.Count);
        if (n < 3)
        {
            return null;
        }

        decimal sumX = 0m, sumY = 0m;
        for (var i = 0; i < n; i++)
        {
            sumX += left[^ (n - i)];
            sumY += right[^ (n - i)];
        }

        var meanX = sumX / n;
        var meanY = sumY / n;
        decimal cov = 0m, varX = 0m, varY = 0m;
        for (var i = 0; i < n; i++)
        {
            var dx = left[^ (n - i)] - meanX;
            var dy = right[^ (n - i)] - meanY;
            cov += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        if (varX <= 0m || varY <= 0m)
        {
            return null;
        }

        return cov / (decimal)Math.Sqrt((double)(varX * varY));
    }

    public static string SignalId(string strategyId, string symbol, DateTimeOffset closeTime, string side) =>
        $"{strategyId}|{symbol}|{closeTime:O}|{side}";

    /// <summary>
    /// Last closed candle whose close time is at or before <paramref name="closeTime"/>.
    /// Candles must be ordered by close time. Returns -1 when none qualify.
    /// </summary>
    public static int LastClosedIndex(IReadOnlyList<MarketCandle> candles, DateTimeOffset closeTime)
    {
        var lo = 0;
        var hi = candles.Count - 1;
        var best = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (candles[mid].CloseTime <= closeTime)
            {
                if (candles[mid].IsClosed)
                {
                    best = mid;
                }

                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best;
    }

    public static IReadOnlyList<MarketCandle> SliceAt(IReadOnlyList<MarketCandle> candles, DateTimeOffset closeTime)
    {
        var count = 0;
        for (var i = 0; i < candles.Count; i++)
        {
            if (candles[i].CloseTime <= closeTime && candles[i].IsClosed)
            {
                count = i + 1;
            }
            else if (candles[i].CloseTime > closeTime)
            {
                break;
            }
        }

        if (count == candles.Count)
        {
            return candles;
        }

        var copy = new MarketCandle[count];
        for (var i = 0; i < count; i++)
        {
            copy[i] = candles[i];
        }

        return copy;
    }

    private static decimal? PriorExtreme(IReadOnlyList<MarketCandle> candles, int index, int lookback, bool high)
    {
        if (lookback < 1 || index < lookback)
        {
            return null;
        }

        decimal extreme = high ? decimal.MinValue : decimal.MaxValue;
        for (var k = index - lookback; k <= index - 1; k++)
        {
            if (!BarUsable(candles[k]))
            {
                return null;
            }

            extreme = high ? Math.Max(extreme, candles[k].High) : Math.Min(extreme, candles[k].Low);
        }

        return extreme;
    }
}
