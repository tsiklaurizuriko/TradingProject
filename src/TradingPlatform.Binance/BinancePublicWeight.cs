namespace TradingPlatform.Binance;

/// <summary>
/// Shared USD-M IP weight budget for public and signed fapi calls.
/// The exchange allows 2400 weight per minute. This process stays at 1800.
/// </summary>
public static class BinancePublicWeight
{
    public const int MinuteBudget = 1800;

    /// <summary>Weight public market data must leave free, so account snapshots, orders and fill history are not starved by candle loads.</summary>
    public const int SignedReserve = 400;

    public static int ForRequest(string relativeUrl)
    {
        var path = relativeUrl;
        var query = "";
        var split = relativeUrl.IndexOf('?', StringComparison.Ordinal);
        if (split >= 0)
        {
            path = relativeUrl[..split];
            query = relativeUrl[(split + 1)..];
        }

        var hasSymbol = query.Contains("symbol=", StringComparison.OrdinalIgnoreCase);
        var limit = ReadLimit(query);
        if (path.Contains("klines", StringComparison.Ordinal))
        {
            if (limit <= 0)
            {
                return 5;
            }

            if (limit < 100)
            {
                return 1;
            }

            if (limit < 500)
            {
                return 2;
            }

            if (limit <= 1000)
            {
                return 5;
            }

            return 10;
        }

        if (path.Contains("ticker/24hr", StringComparison.Ordinal))
        {
            return hasSymbol ? 1 : 40;
        }

        if (path.Contains("ticker/price", StringComparison.Ordinal))
        {
            return hasSymbol ? 1 : 2;
        }

        if (path.Contains("bookTicker", StringComparison.Ordinal))
        {
            return hasSymbol ? 2 : 5;
        }

        if (path.Contains("premiumIndex", StringComparison.Ordinal))
        {
            return hasSymbol ? 1 : 10;
        }

        if (path.Contains("exchangeInfo", StringComparison.Ordinal))
        {
            return 1;
        }

        if (path.Contains("positionSide/dual", StringComparison.Ordinal))
        {
            return 30;
        }

        if (path.Contains("openAlgoOrders", StringComparison.Ordinal))
        {
            return hasSymbol ? 1 : 20;
        }

        if (path.Contains("allAlgoOrders", StringComparison.Ordinal) || path.Contains("allOrders", StringComparison.Ordinal))
        {
            return 5;
        }

        if (path.Contains("algoOrder", StringComparison.Ordinal) || path.Contains("/order", StringComparison.Ordinal))
        {
            return 1;
        }

        if (path.Contains("openOrders", StringComparison.Ordinal))
        {
            return hasSymbol ? 1 : 40;
        }

        if (path.Contains("userTrades", StringComparison.Ordinal))
        {
            return 5;
        }

        if (path.Contains("income", StringComparison.Ordinal))
        {
            return 30;
        }

        if (path.Contains("positionRisk", StringComparison.Ordinal) || path.Contains("/account", StringComparison.Ordinal))
        {
            return 5;
        }

        return 5;
    }

    private static int ReadLimit(string query)
    {
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("limit=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(part.AsSpan(6), out var limit))
            {
                return limit;
            }
        }

        return 0;
    }
}

public static class BinancePublicWeightGate
{
    private static readonly object Sync = new();
    private static readonly Queue<(DateTimeOffset At, int Weight)> Window = new();
    private static int _used;
    private static DateTimeOffset _blockedUntil;
    private static Func<DateTimeOffset> _clock = () => DateTimeOffset.UtcNow;

    public static void CoolDown(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        lock (Sync)
        {
            var until = _clock() + duration;
            if (until > _blockedUntil)
            {
                _blockedUntil = until;
            }
        }
    }

    public static async Task<bool> TryAcquireAsync(int weight, TimeSpan maxWait, CancellationToken cancellationToken, int headroom = 0)
    {
        if (weight <= 0)
        {
            return true;
        }

        var deadline = _clock() + maxWait;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wait = TimeSpan.Zero;
            lock (Sync)
            {
                Expire();
                if (_clock() < _blockedUntil)
                {
                    if (_clock() >= deadline)
                    {
                        return false;
                    }

                    var remaining = deadline - _clock();
                    var until = _blockedUntil - _clock();
                    wait = until < remaining ? until : remaining;
                }
                else if (_used + weight + headroom <= BinancePublicWeight.MinuteBudget)
                {
                    Window.Enqueue((_clock(), weight));
                    _used += weight;
                    return true;
                }
                else if (_clock() >= deadline)
                {
                    return false;
                }
                else if (Window.Count > 0)
                {
                    var releaseAt = Window.Peek().At + TimeSpan.FromMinutes(1);
                    var until = releaseAt - _clock();
                    if (until < TimeSpan.Zero)
                    {
                        until = TimeSpan.Zero;
                    }

                    var remaining = deadline - _clock();
                    wait = until < remaining ? until : remaining;
                }
            }

            if (wait < TimeSpan.FromMilliseconds(20))
            {
                wait = TimeSpan.FromMilliseconds(20);
            }

            await Task.Delay(wait, cancellationToken);
        }
    }

    internal static void UseClock(Func<DateTimeOffset> clock) => _clock = clock;

    internal static void Reset()
    {
        lock (Sync)
        {
            Window.Clear();
            _used = 0;
            _blockedUntil = default;
            _clock = () => DateTimeOffset.UtcNow;
        }
    }

    private static void Expire()
    {
        var now = _clock();
        while (Window.Count > 0 && now - Window.Peek().At >= TimeSpan.FromMinutes(1))
        {
            _used -= Window.Dequeue().Weight;
        }

        if (_used < 0)
        {
            _used = 0;
        }
    }
}
