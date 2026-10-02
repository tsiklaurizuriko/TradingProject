namespace TradingPlatform.Trading;

public sealed class ReconciliationState
{
    private readonly object _gate = new();
    private string? _accountId;
    private DateTimeOffset? _succeededAt;
    private string? _blockReason;

    public bool Succeeded { get; private set; }

    public DateTimeOffset? SucceededAt
    {
        get { lock (_gate) return _succeededAt; }
    }

    public string? AccountId
    {
        get { lock (_gate) return _accountId; }
    }

    public string? BlockReason
    {
        get { lock (_gate) return _blockReason; }
    }

    public int UnresolvedOrderCount
    {
        get { lock (_gate) return _unresolved; }
    }

    private int _unresolved;

    public void SetUnresolvedCount(int count)
    {
        lock (_gate)
        {
            _unresolved = count;
        }
    }

    public void Succeed(string accountId, DateTimeOffset at)
    {
        lock (_gate)
        {
            Succeeded = true;
            _accountId = accountId;
            _succeededAt = at;
            _blockReason = null;
        }
    }

    public void Fail(string reason, DateTimeOffset at)
    {
        lock (_gate)
        {
            Succeeded = false;
            _succeededAt = at;
            _blockReason = reason;
        }
    }

    public bool IsFresh(DateTimeOffset now, TimeSpan maxAge)
    {
        lock (_gate)
        {
            return Succeeded
                && _blockReason is null
                && _succeededAt is { } at
                && now - at <= maxAge
                && now >= at;
        }
    }
}
