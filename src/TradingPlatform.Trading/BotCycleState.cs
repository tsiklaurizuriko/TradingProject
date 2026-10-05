using System.Collections.Concurrent;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Trading;

/// <summary>
/// Per-bot memory that must survive between bot cycles. <see cref="BotEngine"/> is scoped and is rebuilt
/// for every cycle and every API request, so anything kept on the engine instance is forgotten each time.
/// Registered as a singleton; all members are safe for concurrent use.
/// </summary>
public sealed class BotCycleState
{
    private readonly ConcurrentDictionary<Guid, (decimal Stop, decimal Take, DateTimeOffset At)> _ratchetBackoff = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _flatCandleClose = new();
    private readonly ConcurrentDictionary<Guid, StrategyDefinition> _definitions = new();
    private readonly ConcurrentDictionary<Guid, long> _lastSignaledCandle = new();
    private readonly ConcurrentDictionary<Guid, (int Count, long Cycle)> _protectionFailures = new();
    private long _cycle;

    /// <summary>Called once at the start of every bot cycle.</summary>
    public long BeginCycle() => Interlocked.Increment(ref _cycle);

    public bool TryGetRatchetBackoff(Guid botId, out (decimal Stop, decimal Take, DateTimeOffset At) held) =>
        _ratchetBackoff.TryGetValue(botId, out held);

    public void SetRatchetBackoff(Guid botId, decimal stop, decimal take, DateTimeOffset at) =>
        _ratchetBackoff[botId] = (stop, take, at);

    public DateTimeOffset? FlatCandleDecided(Guid botId) =>
        _flatCandleClose.TryGetValue(botId, out var seen) ? seen : null;

    public void SetFlatCandleDecided(Guid botId, DateTimeOffset closeTime) => _flatCandleClose[botId] = closeTime;

    public StrategyDefinition Definition(Guid strategyVersionId, Func<StrategyDefinition> parse) =>
        _definitions.GetOrAdd(strategyVersionId, _ => parse());

    /// <summary>True the first time a bot signals on this candle. Candles only move forward, so one value per bot is enough.</summary>
    public bool TryMarkSignaled(Guid botId, DateTimeOffset candleOpen)
    {
        var ticks = candleOpen.UtcTicks;
        while (true)
        {
            if (_lastSignaledCandle.TryGetValue(botId, out var last))
            {
                if (ticks <= last)
                {
                    return false;
                }

                if (_lastSignaledCandle.TryUpdate(botId, ticks, last))
                {
                    return true;
                }
            }
            else if (_lastSignaledCandle.TryAdd(botId, ticks))
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Counts cycles in which a live position had no working stop after a placement attempt.
    /// Several failed attempts inside the same cycle count once.
    /// </summary>
    public int RecordProtectionFailure(Guid positionId)
    {
        var cycle = Interlocked.Read(ref _cycle);
        return _protectionFailures.AddOrUpdate(
            positionId,
            (1, cycle),
            (_, seen) => seen.Cycle == cycle ? seen : (seen.Count + 1, cycle)).Count;
    }

    public void ClearProtectionFailure(Guid positionId) => _protectionFailures.TryRemove(positionId, out _);
}
