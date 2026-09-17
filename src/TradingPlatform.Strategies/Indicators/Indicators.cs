using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.Indicators;

public interface IIndicator
{
    string Name { get; }
    int Lookback { get; }
    IReadOnlyList<decimal?> Compute(IReadOnlyList<MarketCandle> candles);
}

public static class CandleSeries
{
    public static decimal[] Closes(IReadOnlyList<MarketCandle> candles) => candles.Select(c => c.Close).ToArray();
    public static decimal[] Highs(IReadOnlyList<MarketCandle> candles) => candles.Select(c => c.High).ToArray();
    public static decimal[] Lows(IReadOnlyList<MarketCandle> candles) => candles.Select(c => c.Low).ToArray();
    public static decimal[] Volumes(IReadOnlyList<MarketCandle> candles) => candles.Select(c => c.Volume).ToArray();
}

public sealed class SmaIndicator : IIndicator
{
    public SmaIndicator(int period) => Period = period > 0 ? period : throw new ArgumentOutOfRangeException(nameof(period));
    public string Name => $"SMA({Period})";
    public int Period { get; }
    public int Lookback => Period;

    public IReadOnlyList<decimal?> Compute(IReadOnlyList<MarketCandle> candles)
    {
        var closes = CandleSeries.Closes(candles);
        var result = new decimal?[closes.Length];
        decimal sum = 0;
        for (var i = 0; i < closes.Length; i++)
        {
            sum += closes[i];
            if (i >= Period)
            {
                sum -= closes[i - Period];
            }
            if (i >= Period - 1)
            {
                result[i] = sum / Period;
            }
        }
        return result;
    }
}

public sealed class EmaIndicator : IIndicator
{
    public EmaIndicator(int period) => Period = period > 0 ? period : throw new ArgumentOutOfRangeException(nameof(period));
    public string Name => $"EMA({Period})";
    public int Period { get; }
    public int Lookback => Period;

    public IReadOnlyList<decimal?> Compute(IReadOnlyList<MarketCandle> candles)
    {
        var closes = CandleSeries.Closes(candles);
        var result = new decimal?[closes.Length];
        if (closes.Length < Period)
        {
            return result;
        }

        decimal sum = 0;
        for (var i = 0; i < Period; i++)
        {
            sum += closes[i];
        }
        var ema = sum / Period;
        result[Period - 1] = ema;
        var k = 2m / (Period + 1);
        for (var i = Period; i < closes.Length; i++)
        {
            ema = closes[i] * k + ema * (1 - k);
            result[i] = ema;
        }
        return result;
    }
}

public sealed class RsiIndicator : IIndicator
{
    public RsiIndicator(int period = 14) => Period = period > 0 ? period : throw new ArgumentOutOfRangeException(nameof(period));
    public string Name => $"RSI({Period})";
    public int Period { get; }
    public int Lookback => Period + 1;

    public IReadOnlyList<decimal?> Compute(IReadOnlyList<MarketCandle> candles)
    {
        var closes = CandleSeries.Closes(candles);
        var result = new decimal?[closes.Length];
        if (closes.Length <= Period)
        {
            return result;
        }

        decimal gain = 0, loss = 0;
        for (var i = 1; i <= Period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change >= 0) gain += change;
            else loss -= change;
        }
        var avgGain = gain / Period;
        var avgLoss = loss / Period;
        result[Period] = ToRsi(avgGain, avgLoss);

        for (var i = Period + 1; i < closes.Length; i++)
        {
            var change = closes[i] - closes[i - 1];
            var g = change > 0 ? change : 0;
            var l = change < 0 ? -change : 0;
            avgGain = (avgGain * (Period - 1) + g) / Period;
            avgLoss = (avgLoss * (Period - 1) + l) / Period;
            result[i] = ToRsi(avgGain, avgLoss);
        }
        return result;
    }

    private static decimal ToRsi(decimal avgGain, decimal avgLoss)
    {
        if (avgLoss == 0) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - 100m / (1m + rs);
    }
}

public sealed class IndicatorRegistry
{
    public IIndicator Create(string name, int period) => name.ToUpperInvariant() switch
    {
        "SMA" => new SmaIndicator(period),
        "EMA" => new EmaIndicator(period),
        "RSI" => new RsiIndicator(period),
        "WMA" => new WmaIndicator(period),
        "ATR" => new AtrIndicator(period),
        "VOLUME" => new VolumeIndicator(),
        "AVERAGEVOLUME" or "AVGVOLUME" => new AverageVolumeIndicator(period),
        _ => throw new ArgumentException($"Unknown indicator '{name}'.", nameof(name))
    };
}

public sealed class WmaIndicator : IIndicator
{
    public WmaIndicator(int period) => Period = period;
    public string Name => $"WMA({Period})";
    public int Period { get; }
    public int Lookback => Period;

    public IReadOnlyList<decimal?> Compute(IReadOnlyList<MarketCandle> candles)
    {
        var closes = CandleSeries.Closes(candles);
        var result = new decimal?[closes.Length];
        var denom = Period * (Period + 1) / 2m;
        for (var i = Period - 1; i < closes.Length; i++)
        {
            decimal sum = 0;
            for (var w = 1; w <= Period; w++)
            {
                sum += closes[i - Period + w] * w;
            }
            result[i] = sum / denom;
        }
        return result;
    }
}

public sealed class AtrIndicator : IIndicator
{
    public AtrIndicator(int period = 14) => Period = period;
    public string Name => $"ATR({Period})";
    public int Period { get; }
    public int Lookback => Period + 1;

    public IReadOnlyList<decimal?> Compute(IReadOnlyList<MarketCandle> candles)
    {
        var result = new decimal?[candles.Count];
        if (candles.Count <= Period)
        {
            return result;
        }
        decimal trSum = 0;
        for (var i = 1; i <= Period; i++)
        {
            trSum += TrueRange(candles[i], candles[i - 1]);
        }
        var atr = trSum / Period;
        result[Period] = atr;
        for (var i = Period + 1; i < candles.Count; i++)
        {
            atr = (atr * (Period - 1) + TrueRange(candles[i], candles[i - 1])) / Period;
            result[i] = atr;
        }
        return result;
    }

    private static decimal TrueRange(MarketCandle current, MarketCandle previous)
    {
        var hl = current.High - current.Low;
        var hc = Math.Abs(current.High - previous.Close);
        var lc = Math.Abs(current.Low - previous.Close);
        return Math.Max(hl, Math.Max(hc, lc));
    }
}

public sealed class VolumeIndicator : IIndicator
{
    public string Name => "VOLUME";
    public int Lookback => 1;
    public IReadOnlyList<decimal?> Compute(IReadOnlyList<MarketCandle> candles) =>
        candles.Select(c => (decimal?)c.Volume).ToArray();
}

public sealed class AverageVolumeIndicator : IIndicator
{
    public AverageVolumeIndicator(int period) => Period = period;
    public string Name => $"AVGVOLUME({Period})";
    public int Period { get; }
    public int Lookback => Period;

    public IReadOnlyList<decimal?> Compute(IReadOnlyList<MarketCandle> candles)
    {
        var volumes = CandleSeries.Volumes(candles);
        var result = new decimal?[volumes.Length];
        decimal sum = 0;
        for (var i = 0; i < volumes.Length; i++)
        {
            sum += volumes[i];
            if (i >= Period) sum -= volumes[i - Period];
            if (i >= Period - 1) result[i] = sum / Period;
        }
        return result;
    }
}
