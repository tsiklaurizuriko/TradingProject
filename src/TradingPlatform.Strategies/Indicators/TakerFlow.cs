using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.Indicators;

/// <summary>
/// Binance kline field 9 is taker buy base volume. Taker sell = total volume − taker buy.
/// Zero taker buy is treated as missing, not as a 0 imbalance.
/// </summary>
public static class TakerFlow
{
    public static decimal? Imbalance(MarketCandle candle)
    {
        var buy = candle.TakerBuyVolume;
        var volume = candle.Volume;
        if (buy <= 0m || volume <= 0m || buy > volume)
        {
            return null;
        }

        var sell = volume - buy;
        var den = buy + sell;
        if (den == 0m)
        {
            return null;
        }

        return (buy - sell) / den;
    }

    public static decimal? TakerSellVolume(MarketCandle candle)
    {
        if (candle.TakerBuyVolume <= 0m || candle.Volume <= 0m || candle.TakerBuyVolume > candle.Volume)
        {
            return null;
        }

        return candle.Volume - candle.TakerBuyVolume;
    }
}
