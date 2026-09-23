using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.PriceAction;

/// <summary>
/// Research-only forward labels. Must never be called from live/signal evaluation.
/// </summary>
public static class PatternOutcomeEngine
{
    public static readonly int[] Horizons = [1, 2, 3, 5, 10, 20];
    public static readonly decimal[] TargetPct = [0.0025m, 0.005m, 0.0075m, 0.01m, 0.015m, 0.02m];
    public static readonly decimal[] StopPct = [0.0025m, 0.005m, 0.0075m, 0.01m, 0.015m];

    public sealed record HorizonStat(int Horizon, decimal ForwardReturn, decimal Mfe, decimal Mae);

    public sealed record HitStat(decimal Level, bool Hit, int? Bars);

    public sealed record PatternOutcome(
        int FromIndex,
        string Side,
        IReadOnlyList<HorizonStat> Horizons,
        IReadOnlyList<HitStat> LongTargets,
        IReadOnlyList<HitStat> ShortTargets,
        IReadOnlyList<HitStat> Stops,
        int? HoldToTargetBars,
        int? HoldToStopBars);

    public static PatternOutcome Measure(IReadOnlyList<MarketCandle> candles, int fromIndex, string side)
    {
        var close = candles[fromIndex].Close;
        var horizons = new List<HorizonStat>();
        foreach (var h in Horizons)
        {
            var j = Math.Min(candles.Count - 1, fromIndex + h);
            if (j <= fromIndex)
            {
                horizons.Add(new HorizonStat(h, 0, 0, 0));
                continue;
            }

            var fwd = (candles[j].Close - close) / close;
            var mfe = 0m;
            var mae = 0m;
            for (var k = fromIndex + 1; k <= j; k++)
            {
                var up = (candles[k].High - close) / close;
                var dn = (candles[k].Low - close) / close;
                if (up > mfe)
                {
                    mfe = up;
                }

                if (dn < mae)
                {
                    mae = dn;
                }
            }

            horizons.Add(new HorizonStat(h, fwd, mfe, mae));
        }

        var longT = TargetPct.Select(p => FirstHit(candles, fromIndex, close * (1 + p), true)).ToArray();
        var shortT = TargetPct.Select(p => FirstHit(candles, fromIndex, close * (1 - p), false)).ToArray();
        var stops = StopPct.Select(p =>
            string.Equals(side, "SHORT", StringComparison.OrdinalIgnoreCase)
                ? FirstHit(candles, fromIndex, close * (1 + p), true)
                : FirstHit(candles, fromIndex, close * (1 - p), false)).ToArray();
        return new PatternOutcome(fromIndex, side, horizons, longT, shortT, stops, longT.FirstOrDefault(x => x.Hit)?.Bars, stops.FirstOrDefault(x => x.Hit)?.Bars);
    }

    private static HitStat FirstHit(IReadOnlyList<MarketCandle> candles, int from, decimal level, bool above)
    {
        for (var i = from + 1; i < candles.Count; i++)
        {
            if (above && candles[i].High >= level)
            {
                return new HitStat(level, true, i - from);
            }

            if (!above && candles[i].Low <= level)
            {
                return new HitStat(level, true, i - from);
            }
        }

        return new HitStat(level, false, null);
    }
}
