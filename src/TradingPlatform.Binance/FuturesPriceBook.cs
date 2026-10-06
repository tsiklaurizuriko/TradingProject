using System.Collections.Concurrent;

namespace TradingPlatform.Binance;

/// <summary>Recent USD-M last prices from REST and the mini-ticker stream, shared by every caller in the process.</summary>
public static class FuturesPriceBook
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(8);

    private static readonly ConcurrentDictionary<string, (decimal Price, DateTimeOffset Until)> Prices = new(StringComparer.OrdinalIgnoreCase);
    private static long _demandedAtTicks;

    /// <summary>Last time anyone asked for a single coin price.</summary>
    public static DateTimeOffset DemandedAt => new(Interlocked.Read(ref _demandedAtTicks), TimeSpan.Zero);

    public static bool TryGet(string symbol, DateTimeOffset now, out decimal price)
    {
        Interlocked.Exchange(ref _demandedAtTicks, now.UtcTicks);
        if (Prices.TryGetValue(symbol, out var hit) && now < hit.Until)
        {
            price = hit.Price;
            return true;
        }

        price = 0m;
        return false;
    }

    public static void Set(string symbol, decimal price, DateTimeOffset now)
    {
        if (price > 0m && !string.IsNullOrWhiteSpace(symbol))
        {
            Prices[symbol.ToUpperInvariant()] = (price, now + Lifetime);
        }
    }

    internal static void Clear()
    {
        Prices.Clear();
        Interlocked.Exchange(ref _demandedAtTicks, 0);
    }
}
