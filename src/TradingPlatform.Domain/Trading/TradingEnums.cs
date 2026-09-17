namespace TradingPlatform.Domain.Trading;

public enum TradingMode
{
    Paper = 0,
    Testnet = 1,
    Live = 2
}

public enum BotStatus
{
    Created = 0,
    Starting = 1,
    Running = 2,
    Pausing = 3,
    Paused = 4,
    Stopping = 5,
    Stopped = 6,
    Error = 7
}

public enum SignalType
{
    Buy = 0,
    Sell = 1,
    Hold = 2,
    Exit = 3,
    NoAction = 4
}

public enum OrderSide
{
    Buy = 0,
    Sell = 1
}

public enum OrderType
{
    Market = 0,
    Limit = 1
}

public enum OrderStatus
{
    New = 0,
    Submitting = 1,
    Submitted = 2,
    PartiallyFilled = 3,
    Filled = 4,
    CancelRequested = 5,
    Cancelled = 6,
    Rejected = 7,
    Expired = 8,
    Failed = 9
}

public enum Timeframe
{
    OneMinute,
    ThreeMinutes,
    FiveMinutes,
    FifteenMinutes,
    ThirtyMinutes,
    OneHour,
    TwoHours,
    FourHours,
    SixHours,
    EightHours,
    TwelveHours,
    OneDay,
    OneWeek
}

public static class TimeframeExtensions
{
    public static string ToBinanceInterval(this Timeframe timeframe) => timeframe switch
    {
        Timeframe.OneMinute => "1m",
        Timeframe.ThreeMinutes => "3m",
        Timeframe.FiveMinutes => "5m",
        Timeframe.FifteenMinutes => "15m",
        Timeframe.ThirtyMinutes => "30m",
        Timeframe.OneHour => "1h",
        Timeframe.TwoHours => "2h",
        Timeframe.FourHours => "4h",
        Timeframe.SixHours => "6h",
        Timeframe.EightHours => "8h",
        Timeframe.TwelveHours => "12h",
        Timeframe.OneDay => "1d",
        Timeframe.OneWeek => "1w",
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, null)
    };

    public static TimeSpan ToDuration(this Timeframe timeframe) => timeframe switch
    {
        Timeframe.OneMinute => TimeSpan.FromMinutes(1),
        Timeframe.ThreeMinutes => TimeSpan.FromMinutes(3),
        Timeframe.FiveMinutes => TimeSpan.FromMinutes(5),
        Timeframe.FifteenMinutes => TimeSpan.FromMinutes(15),
        Timeframe.ThirtyMinutes => TimeSpan.FromMinutes(30),
        Timeframe.OneHour => TimeSpan.FromHours(1),
        Timeframe.TwoHours => TimeSpan.FromHours(2),
        Timeframe.FourHours => TimeSpan.FromHours(4),
        Timeframe.SixHours => TimeSpan.FromHours(6),
        Timeframe.EightHours => TimeSpan.FromHours(8),
        Timeframe.TwelveHours => TimeSpan.FromHours(12),
        Timeframe.OneDay => TimeSpan.FromDays(1),
        Timeframe.OneWeek => TimeSpan.FromDays(7),
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, null)
    };

    public static bool TryParseInterval(string interval, out Timeframe timeframe)
    {
        timeframe = interval switch
        {
            "1m" => Timeframe.OneMinute,
            "3m" => Timeframe.ThreeMinutes,
            "5m" => Timeframe.FiveMinutes,
            "15m" => Timeframe.FifteenMinutes,
            "30m" => Timeframe.ThirtyMinutes,
            "1h" => Timeframe.OneHour,
            "2h" => Timeframe.TwoHours,
            "4h" => Timeframe.FourHours,
            "6h" => Timeframe.SixHours,
            "8h" => Timeframe.EightHours,
            "12h" => Timeframe.TwelveHours,
            "1d" => Timeframe.OneDay,
            "1w" => Timeframe.OneWeek,
            _ => (Timeframe)(-1)
        };
        return (int)timeframe >= 0;
    }
}
