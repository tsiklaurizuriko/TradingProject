using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Closed 1h flat. The prior 24 hours, excluding the signal bar, lock the high and the low.
/// Long at the bottom fifth, short at the top fifth. Stop is that bound. Take profit is the other bound.
/// </summary>
public static class FlatRangeStrategy
{
    public const int Lookback = 24;
    public const int RankWindow = 100;
    public const int MinRankSamples = 40;
    public const int MaxHoldHours = 24;
    public const decimal MinWidth = 0.008m;
    public const decimal MaxWidth = 0.06m;
    public const decimal EntryFrac = 0.20m;
    public const decimal WidthRankMax = 0.30m;
    public const decimal MinStopPercent = 0.20m;

    public static StrategySignalDetail Evaluate(
        IReadOnlyList<MarketCandle> candles,
        int index,
        bool hasPosition,
        string allowedSide,
        DateTimeOffset? openedAt)
    {
        if (index < 1 || index >= candles.Count)
        {
            return new StrategySignalDetail(SignalType.NoAction, "Not enough closed candles.");
        }

        var bar = candles[index];
        if (hasPosition)
        {
            if (openedAt is { } open && bar.CloseTime >= open.AddHours(MaxHoldHours))
            {
                return new StrategySignalDetail(SignalType.Exit, "Flat range time stop after 24 hours.", bar.CloseTime);
            }

            return new StrategySignalDetail(
                SignalType.Hold,
                "Bounds stay locked. Stop is the entry bound. Take profit is the opposite bound.",
                bar.CloseTime);
        }

        if (!TryBand(candles, index, out var low, out var high, out var why))
        {
            return new StrategySignalDetail(SignalType.NoAction, why, bar.CloseTime);
        }

        var close = bar.Close;
        var span = high - low;
        var pos = (close - low) / span;
        if (pos <= EntryFrac && StrategySides.AllowsLong(allowedSide))
        {
            return Entry(SignalType.Buy, "LONG", close, low, high, low, high, bar.CloseTime, pos);
        }

        if (pos >= 1m - EntryFrac && StrategySides.AllowsShort(allowedSide))
        {
            return Entry(SignalType.Sell, "SHORT", close, low, high, high, low, bar.CloseTime, pos);
        }

        return new StrategySignalDetail(
            SignalType.NoAction,
            "Flat is locked, and the close is still inside the middle of the band.",
            bar.CloseTime,
            Snapshot: BandSnapshot(low, high, pos));
    }

    private static StrategySignalDetail Entry(
        SignalType signal,
        string side,
        decimal close,
        decimal low,
        decimal high,
        decimal stop,
        decimal take,
        DateTimeOffset time,
        decimal pos)
    {
        var stopPct = Math.Abs(close - stop) / close * 100m;
        var takePct = Math.Abs(take - close) / close * 100m;
        if (stopPct < MinStopPercent)
        {
            return new StrategySignalDetail(
                SignalType.NoAction,
                "Stop is closer than 0.20% of price, so the risk is not sized.",
                time,
                Snapshot: BandSnapshot(low, high, pos));
        }

        if (takePct <= stopPct)
        {
            return new StrategySignalDetail(
                SignalType.NoAction,
                "Take profit is not farther than the stop.",
                time,
                Snapshot: BandSnapshot(low, high, pos));
        }

        return new StrategySignalDetail(
            signal,
            $"{side} at the flat bound. Stop {stop.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)} ({stopPct.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}%). Take profit {take.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)} ({takePct.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}%).",
            time,
            stop,
            take,
            BandSnapshot(low, high, pos, stopPct, takePct));
    }

    public static bool TryBand(
        IReadOnlyList<MarketCandle> candles,
        int index,
        out decimal low,
        out decimal high,
        out string reason)
    {
        low = 0m;
        high = 0m;
        reason = "Not a flat.";
        if (index < Lookback + MinRankSamples)
        {
            reason = "Not enough closed hours to judge the flat.";
            return false;
        }

        low = decimal.MaxValue;
        high = decimal.MinValue;
        for (var k = index - Lookback; k < index; k++)
        {
            low = Math.Min(low, candles[k].Low);
            high = Math.Max(high, candles[k].High);
        }

        var mid = candles[index - 1].Close;
        if (mid <= 0m || high <= low)
        {
            reason = "The prior 24 hours have no usable range.";
            return false;
        }

        var width = (high - low) / mid;
        if (width < MinWidth || width > MaxWidth)
        {
            reason = "The 24h band is outside 0.8% to 6%.";
            return false;
        }

        var below = 0;
        var finite = 0;
        var start = Math.Max(Lookback, index - RankWindow);
        for (var k = start; k < index; k++)
        {
            var klo = decimal.MaxValue;
            var khi = decimal.MinValue;
            for (var t = k - Lookback; t < k; t++)
            {
                klo = Math.Min(klo, candles[t].Low);
                khi = Math.Max(khi, candles[t].High);
            }

            var kMid = candles[k - 1].Close;
            if (kMid <= 0m || khi <= klo)
            {
                continue;
            }

            finite++;
            if ((khi - klo) / kMid < width)
            {
                below++;
            }
        }

        if (finite < MinRankSamples || below / (decimal)finite > WidthRankMax)
        {
            reason = "This band is not among the narrowest 30%.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static Dictionary<string, decimal?> BandSnapshot(
        decimal low,
        decimal high,
        decimal pos,
        decimal? stopPct = null,
        decimal? takePct = null) =>
        new()
        {
            ["bandLow"] = low,
            ["bandHigh"] = high,
            ["position"] = pos,
            ["stopPercent"] = stopPct,
            ["takeProfitPercent"] = takePct
        };
}
