namespace TradingPlatform.Application.Abstractions;

/// <summary>
/// One process at a time may run a named loop that sends orders (bot engine, news trading).
/// Call <see cref="HoldAsync"/> before every cycle; false means another process is the leader, so skip the cycle.
/// </summary>
public interface IWorkerLease
{
    Task<bool> HoldAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>Single-process default. The Postgres lease replaces it whenever a database is configured.</summary>
public sealed class InProcessWorkerLease : IWorkerLease
{
    public Task<bool> HoldAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public static class WorkerLeaseNames
{
    public const string BotEngine = "bot-engine";
    public const string NewsTrading = "news-trading";
}
