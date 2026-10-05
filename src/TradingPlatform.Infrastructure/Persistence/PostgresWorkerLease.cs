using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;
using TradingPlatform.Application.Abstractions;

namespace TradingPlatform.Infrastructure.Persistence;

/// <summary>
/// Leader election with a session-level Postgres advisory lock. The lock lives as long as one dedicated
/// connection, so a crashed or partitioned process loses it and another host can take over.
/// Each cycle pings that connection; a dead connection drops leadership before any order is sent.
/// </summary>
public sealed class PostgresWorkerLease : IWorkerLease, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresWorkerLease> _logger;
    private readonly ConcurrentDictionary<string, NpgsqlConnection> _held = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PostgresWorkerLease(string connectionString, ILogger<PostgresWorkerLease> logger)
    {
        // A pooled connection keeps its server session (and the lock) after Dispose.
        _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            KeepAlive = 15,
            ApplicationName = "TradingPlatform.WorkerLease"
        }.ConnectionString;
        _logger = logger;
    }

    public static long LockKey(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("TradingPlatform.WorkerLease:" + name));
        return BitConverter.ToInt64(hash, 0);
    }

    public async Task<bool> HoldAsync(string name, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_held.TryGetValue(name, out var existing))
            {
                if (await AliveAsync(existing, cancellationToken))
                {
                    return true;
                }

                _held.TryRemove(name, out _);
                await existing.DisposeAsync();
                _logger.LogWarning("Worker lease {Name} lost: the lock connection dropped. Skipping this cycle.", name);
            }

            var connection = new NpgsqlConnection(_connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
                command.Parameters.AddWithValue("key", LockKey(name));
                if (await command.ExecuteScalarAsync(cancellationToken) is true)
                {
                    _held[name] = connection;
                    _logger.LogInformation("Worker lease {Name} acquired. This process is the leader.", name);
                    return true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Worker lease {Name} could not be checked. Skipping this cycle.", name);
            }

            await connection.DisposeAsync();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<bool> AliveAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            command.CommandTimeout = 5;
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _held.Values)
        {
            await connection.DisposeAsync();
        }

        _held.Clear();
        _gate.Dispose();
    }
}
