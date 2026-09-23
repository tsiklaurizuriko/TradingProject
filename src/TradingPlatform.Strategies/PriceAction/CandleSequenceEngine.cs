namespace TradingPlatform.Strategies.PriceAction;

public static class CandleSequenceEngine
{
    public static SequenceBar[] Detect(CandleGeom[] geoms)
    {
        var result = new SequenceBar[geoms.Length];
        var compress = 0;
        var expand = 0;
        for (var i = 0; i < geoms.Length; i++)
        {
            var median = PriceActionMath.MedianRange(geoms, i, 20);
            var bull = PriceActionMath.Consecutive(geoms, i, true);
            var bear = PriceActionMath.Consecutive(geoms, i, false);
            if (geoms[i].Compression(median))
            {
                compress++;
                expand = 0;
            }
            else if (geoms[i].Expansion(median))
            {
                expand++;
                compress = 0;
            }
            else
            {
                compress = 0;
                expand = 0;
            }

            var alt = i >= 3
                && geoms[i].Bullish != geoms[i - 1].Bullish
                && geoms[i - 1].Bullish != geoms[i - 2].Bullish
                && geoms[i - 2].Bullish != geoms[i - 3].Bullish;
            var failed = i >= 1 && geoms[i - 1].Expansion(median) && geoms[i].BodyRange < 0.4m
                && ((geoms[i - 1].Bullish && geoms[i].Close < geoms[i - 1].Open)
                    || (geoms[i - 1].Bearish && geoms[i].Close > geoms[i - 1].Open));
            var follow = i >= 1 && geoms[i - 1].Expansion(median)
                && ((geoms[i - 1].Bullish && geoms[i].Bullish) || (geoms[i - 1].Bearish && geoms[i].Bearish));
            result[i] = new SequenceBar(i, bull, bear, compress, expand, alt, failed, follow);
        }

        return result;
    }
}
