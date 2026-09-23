using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.PriceAction;

/// <summary>Causal HH/HL/LH/LL, BOS, CHoCH. Bias at i uses only swings confirmed at or before i.</summary>
public static class MarketStructureEngine
{
    public static StructureBar[] Detect(
        IReadOnlyList<MarketCandle> candles,
        IReadOnlyList<SwingPoint> highs,
        IReadOnlyList<SwingPoint> lows)
    {
        var result = new StructureBar[candles.Count];
        var hiPtr = 0;
        var loPtr = 0;
        var knownHi = new List<SwingPoint>();
        var knownLo = new List<SwingPoint>();
        var bias = 0;
        decimal? lastRangeWidth = null;
        for (var i = 0; i < candles.Count; i++)
        {
            while (hiPtr < highs.Count && highs[hiPtr].ConfirmationIndex <= i)
            {
                knownHi.Add(highs[hiPtr++]);
            }

            while (loPtr < lows.Count && lows[loPtr].ConfirmationIndex <= i)
            {
                knownLo.Add(lows[loPtr++]);
            }

            var hh = false;
            var hl = false;
            var lh = false;
            var ll = false;
            if (knownHi.Count >= 2)
            {
                hh = knownHi[^1].Price > knownHi[^2].Price;
                lh = knownHi[^1].Price < knownHi[^2].Price;
            }

            if (knownLo.Count >= 2)
            {
                hl = knownLo[^1].Price > knownLo[^2].Price;
                ll = knownLo[^1].Price < knownLo[^2].Price;
            }

            var nextBias = bias;
            if (hh && hl)
            {
                nextBias = 1;
            }
            else if (lh && ll)
            {
                nextBias = -1;
            }

            var lastHi = knownHi.Count > 0 ? knownHi[^1].Price : (decimal?)null;
            var lastLo = knownLo.Count > 0 ? knownLo[^1].Price : (decimal?)null;
            var bosBull = lastHi is { } h && candles[i].Close > h && (i == 0 || candles[i - 1].Close <= h);
            var bosBear = lastLo is { } l && candles[i].Close < l && (i == 0 || candles[i - 1].Close >= l);
            var chochBull = bosBull && bias < 0;
            var chochBear = bosBear && bias > 0;
            if (bosBull)
            {
                nextBias = 1;
            }

            if (bosBear)
            {
                nextBias = -1;
            }

            var inRange = false;
            var contract = false;
            var expand = false;
            if (knownHi.Count >= 2 && knownLo.Count >= 2)
            {
                var bandHi = Math.Max(knownHi[^1].Price, knownHi[^2].Price);
                var bandLo = Math.Min(knownLo[^1].Price, knownLo[^2].Price);
                var width = bandHi - bandLo;
                if (width > 0 && candles[i].Close <= bandHi && candles[i].Close >= bandLo)
                {
                    inRange = true;
                }

                if (lastRangeWidth is { } prev && prev > 0)
                {
                    contract = width < prev * 0.85m;
                    expand = width > prev * 1.15m;
                }

                lastRangeWidth = width;
            }

            bias = nextBias;
            result[i] = new StructureBar(i, hh, hl, lh, ll, bias, bosBull, bosBear, chochBull, chochBear, inRange, contract, expand);
        }

        return result;
    }
}
