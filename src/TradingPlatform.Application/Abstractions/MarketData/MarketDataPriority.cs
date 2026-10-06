namespace TradingPlatform.Application.Abstractions.MarketData;

/// <summary>
/// Marks public market-data calls made by background refresh jobs. They wait for weight instead of being
/// skipped, and leave room for bot cycles and order handling.
/// </summary>
public static class MarketDataPriority
{
    private static readonly AsyncLocal<bool> Current = new();

    public static bool IsBackground => Current.Value;

    public static IDisposable Background()
    {
        var previous = Current.Value;
        Current.Value = true;
        return new Scope(previous);
    }

    private sealed class Scope(bool previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
