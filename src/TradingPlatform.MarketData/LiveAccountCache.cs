using TradingPlatform.Application.Abstractions.Exchange;

namespace TradingPlatform.MarketData;

public sealed class LiveAccountCache : ILiveAccountCache
{
    private LiveAccountSnapshot _current = new();
    private readonly object _gate = new();

    public LiveAccountSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Set(LiveAccountSnapshot snapshot)
    {
        lock (_gate)
        {
            _current = snapshot;
        }
    }
}
