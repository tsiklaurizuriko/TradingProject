using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Trading;

/// <summary>
/// A flat bot is evaluated when its latest closed candle has not been decided yet.
/// An open position is not scheduled here; that bot is checked on every cycle.
/// </summary>
public static class BotCycleSchedule
{
    public static bool FlatCandleDue(DateTimeOffset now, Timeframe timeframe, DateTimeOffset? decidedClose)
    {
        if (decidedClose is null)
        {
            return true;
        }

        return now >= decidedClose.Value + timeframe.ToDuration();
    }
}
