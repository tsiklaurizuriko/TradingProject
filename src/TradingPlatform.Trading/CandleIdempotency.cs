using System.Globalization;

namespace TradingPlatform.Trading;

/// <summary>
/// One order key per bot and closed-candle open time. A repeated candle does not create a second key.
/// </summary>
public static class CandleIdempotency
{
    public static string Key(Guid botId, DateTimeOffset candleOpen, bool live)
    {
        var candleKey = candleOpen.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        if (!live)
        {
            return $"p-{botId:N}-{candleKey}";
        }

        var suffix = candleKey.Length <= 10 ? candleKey : candleKey[^10..];
        return $"L{botId:N}"[..12] + suffix;
    }
}
