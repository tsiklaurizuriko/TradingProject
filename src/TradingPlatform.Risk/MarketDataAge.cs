namespace TradingPlatform.Risk;

/// <summary>
/// Value for <see cref="RiskSnapshot.MarketDataAgeMs"/>. It is the larger of two delays:
/// how old the entry price is, and how far past the next expected candle close the newest closed candle is.
/// A late but complete candle is fine; a missing candle or an old price is stale.
/// </summary>
public static class MarketDataAge
{
    /// <param name="priceAt">When the entry price was read. Null means it came from the candle close.</param>
    public static int Milliseconds(DateTimeOffset now, DateTimeOffset? priceAt, DateTimeOffset lastCandleClose, TimeSpan timeframe)
    {
        var priceAge = now - (priceAt ?? lastCandleClose);
        var candleLag = now - (lastCandleClose + timeframe);
        var age = priceAge > candleLag ? priceAge : candleLag;
        if (age <= TimeSpan.Zero)
        {
            return 0;
        }

        return age.TotalMilliseconds >= int.MaxValue ? int.MaxValue : (int)age.TotalMilliseconds;
    }
}
