using TradingPlatform.Domain.Market;

namespace TradingPlatform.Research;

public sealed record KlineQualityResult(
    int Bars,
    int DuplicateTimestamps,
    int OutOfOrderTimestamps,
    int GapSegments,
    long MissingBars,
    int MisalignedTimestamps,
    int ImpossibleOhlc,
    int NegativeVolume,
    bool Continuous,
    bool QualityPassed);

/// <summary>
/// Data-only validation. It computes no indicators, signals, or future outcome labels.
/// </summary>
public static class PriceActionDataQuality
{
    public static KlineQualityResult Audit(IReadOnlyList<MarketCandle> candles, long intervalMs)
    {
        if (intervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalMs));
        }

        var duplicates = 0;
        var outOfOrder = 0;
        var gapSegments = 0;
        long missingBars = 0;
        var misaligned = 0;
        var impossible = 0;
        var negativeVolume = 0;
        var seen = new HashSet<long>();

        for (var i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];
            var openMs = candle.OpenTime.ToUnixTimeMilliseconds();
            if (!seen.Add(openMs))
            {
                duplicates++;
            }

            if (openMs % intervalMs != 0
                || candle.CloseTime.ToUnixTimeMilliseconds() != openMs + intervalMs - 1)
            {
                misaligned++;
            }

            if (candle.Volume < 0)
            {
                negativeVolume++;
            }

            if (candle.High < Math.Max(candle.Open, candle.Close)
                || candle.Low > Math.Min(candle.Open, candle.Close)
                || candle.High < candle.Low)
            {
                impossible++;
            }

            if (i == 0)
            {
                continue;
            }

            var previous = candles[i - 1].OpenTime.ToUnixTimeMilliseconds();
            var delta = openMs - previous;
            if (delta < 0)
            {
                outOfOrder++;
            }
            else if (delta > intervalMs)
            {
                gapSegments++;
                missingBars += Math.Max(0, delta / intervalMs - 1);
            }
        }

        var continuous = duplicates == 0 && outOfOrder == 0 && gapSegments == 0;
        return new KlineQualityResult(
            candles.Count,
            duplicates,
            outOfOrder,
            gapSegments,
            missingBars,
            misaligned,
            impossible,
            negativeVolume,
            continuous,
            continuous && misaligned == 0 && impossible == 0 && negativeVolume == 0);
    }
}
