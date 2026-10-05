namespace TradingPlatform.Application.Abstractions.Exchange;

public sealed record UserDataStreamStatus(bool Connected, DateTimeOffset? LastEventAt, DateTimeOffset? LastChangeAt, string Message);

/// <summary>
/// Push channel from the Binance user-data stream to the bot engine. An event only wakes the engine early;
/// the engine still reads positions and orders over REST, so a dropped stream degrades to polling.
/// </summary>
public interface IExchangeEventSignal
{
    UserDataStreamStatus Status { get; }

    void Raise(string reason, DateTimeOffset at);

    void SetStatus(bool connected, string message, DateTimeOffset at);

    /// <summary>True when an event arrived before the timeout.</summary>
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class ExchangeEventSignal : IExchangeEventSignal
{
    private readonly SemaphoreSlim _raised = new(0, 1);
    private readonly object _gate = new();
    private UserDataStreamStatus _status = new(false, null, null, "User-data stream not started. Polling only.");

    public UserDataStreamStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public void Raise(string reason, DateTimeOffset at)
    {
        lock (_gate)
        {
            _status = _status with { LastEventAt = at };
        }

        if (_raised.CurrentCount == 0)
        {
            try
            {
                _raised.Release();
            }
            catch (SemaphoreFullException)
            {
                // already raised
            }
        }
    }

    public void SetStatus(bool connected, string message, DateTimeOffset at)
    {
        lock (_gate)
        {
            _status = _status with { Connected = connected, Message = message, LastChangeAt = at };
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        _raised.WaitAsync(timeout, cancellationToken);
}
