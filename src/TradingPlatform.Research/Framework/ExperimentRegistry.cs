using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingPlatform.Domain.Market;

namespace TradingPlatform.Research.Framework;

public sealed record ExperimentRecord(
    string Id,
    DateTimeOffset At,
    string Family,
    string Strategy,
    string Hypothesis,
    IReadOnlyDictionary<string, string> Parameters,
    string DataHash,
    string CostProfile,
    string Split,
    double SharpePerObservation,
    int Observations,
    decimal? ProfitFactor,
    int Trades,
    decimal NetPnl,
    string Verdict);

/// <summary>
/// Append-only JSONL log of every evaluated configuration. Trial counts per family feed the deflated Sharpe,
/// so a configuration that was tried and dropped still counts.
/// </summary>
public sealed class ExperimentRegistry
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly object _gate = new();
    private readonly List<ExperimentRecord> _records = [];

    public ExperimentRegistry(string path)
    {
        Path = path;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(path))
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var record = JsonSerializer.Deserialize<ExperimentRecord>(line, Json);
                if (record is not null)
                {
                    _records.Add(record);
                }
            }
        }
    }

    public string Path { get; }

    public IReadOnlyList<ExperimentRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToList();
            }
        }
    }

    public ExperimentRecord Append(ExperimentRecord record)
    {
        lock (_gate)
        {
            _records.Add(record);
            File.AppendAllText(Path, JsonSerializer.Serialize(record, Json) + "\n");
            return record;
        }
    }

    /// <summary>Distinct configurations tried in the family on the given split (parameters + strategy).</summary>
    public int TrialCount(string family, string split = "Validation")
    {
        lock (_gate)
        {
            return _records
                .Where(r => Same(r.Family, family) && Same(r.Split, split))
                .Select(ConfigurationKey)
                .Distinct(StringComparer.Ordinal)
                .Count();
        }
    }

    public double SharpeVariance(string family, string split = "Validation")
    {
        lock (_gate)
        {
            var sharpes = _records
                .Where(r => Same(r.Family, family) && Same(r.Split, split) && r.Observations > 1)
                .GroupBy(ConfigurationKey, StringComparer.Ordinal)
                .Select(g => g.Last().SharpePerObservation)
                .ToList();
            return ResearchStatistics.Variance(sharpes);
        }
    }

    public static string ConfigurationKey(ExperimentRecord r) =>
        r.Strategy + "|" + r.CostProfile + "|" + string.Join(";", r.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public static class DataFingerprint
{
    /// <summary>Stable hash over coin, timeframe, bar count, first/last open and every close.</summary>
    public static string Of(IEnumerable<(string Symbol, string Timeframe, IReadOnlyList<MarketCandle> Candles)> series)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        foreach (var (symbol, timeframe, candles) in series.OrderBy(s => s.Symbol, StringComparer.Ordinal).ThenBy(s => s.Timeframe, StringComparer.Ordinal))
        {
            sb.Append(symbol).Append('|').Append(timeframe).Append('|').Append(candles.Count);
            if (candles.Count > 0)
            {
                sb.Append('|').Append(candles[0].OpenTime.ToUnixTimeMilliseconds())
                    .Append('|').Append(candles[^1].OpenTime.ToUnixTimeMilliseconds());
                foreach (var c in candles)
                {
                    sb.Append(',').Append(c.Close.ToString(CultureInfo.InvariantCulture));
                }
            }

            sb.Append('\n');
        }

        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}

public sealed record SealedSplitRanges(int InSampleEnd, int ValidationEnd, int Count)
{
    public (int From, int To) InSample => (0, InSampleEnd);
    public (int From, int To) Validation => (InSampleEnd, ValidationEnd);
}

public sealed record OosTicket(string Strategy, string DataHash, int From, int To, DateTimeOffset OpenedAt, string Reason);

public sealed record OosAccess(string Strategy, string DataHash, DateTimeOffset At, string Reason);

/// <summary>
/// IS 60% / Validation 20% / OOS last 20%. The OOS range is not exposed by <see cref="SealedSplitRanges"/>;
/// it is only handed out once per strategy and data hash, and every opening is logged.
/// </summary>
public sealed class OosVault
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly object _gate = new();
    private readonly List<OosAccess> _log = [];
    private readonly string _path;

    public OosVault(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(path))
        {
            foreach (var line in File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var access = JsonSerializer.Deserialize<OosAccess>(line, Json);
                if (access is not null)
                {
                    _log.Add(access);
                }
            }
        }
    }

    public IReadOnlyList<OosAccess> Accesses
    {
        get
        {
            lock (_gate)
            {
                return _log.ToList();
            }
        }
    }

    public static SealedSplitRanges Split(int count)
    {
        var inSampleEnd = Math.Max(1, count * 60 / 100);
        var validationEnd = Math.Max(inSampleEnd + 1, count * 80 / 100);
        return new SealedSplitRanges(inSampleEnd, Math.Min(validationEnd, count), count);
    }

    public bool HasBeenOpened(string strategy, string dataHash)
    {
        lock (_gate)
        {
            return _log.Any(a => Match(a, strategy, dataHash));
        }
    }

    public OosTicket Open(string strategy, string dataHash, int count, string reason, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An OOS opening needs a reason.", nameof(reason));
        }

        lock (_gate)
        {
            if (_log.Any(a => Match(a, strategy, dataHash)))
            {
                throw new InvalidOperationException(
                    $"OOS for {strategy} on data {dataHash} was already opened. Re-testing the holdout turns it into in-sample data.");
            }

            var split = Split(count);
            var at = now ?? DateTimeOffset.UtcNow;
            var access = new OosAccess(strategy, dataHash, at, reason);
            _log.Add(access);
            File.AppendAllText(_path, JsonSerializer.Serialize(access, Json) + "\n");
            return new OosTicket(strategy, dataHash, split.ValidationEnd, count, at, reason);
        }
    }

    private static bool Match(OosAccess a, string strategy, string dataHash) =>
        string.Equals(a.Strategy, strategy, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.DataHash, dataHash, StringComparison.OrdinalIgnoreCase);
}
