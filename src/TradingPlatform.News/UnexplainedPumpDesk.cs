using System.Text.Json;

namespace TradingPlatform.News;

public sealed class UnexplainedPumpDeskFile
{
    public bool Enabled { get; set; }
    public DateTimeOffset? LastScanUtc { get; set; }
    public List<UnexplainedPumpPositionRow> Positions { get; set; } = [];
    public List<UnexplainedPumpCheckRow> Decisions { get; set; } = [];
}

public sealed class UnexplainedPumpPositionRow
{
    public string Coin { get; set; } = string.Empty;
    public string Side { get; set; } = "Short";
    public decimal Entry { get; set; }
    public decimal PumpHigh { get; set; }
    public decimal? Mark { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public bool Open { get; set; }
    public string? CloseReason { get; set; }
    public decimal Quantity { get; set; }
    public decimal? Pnl { get; set; }
    public bool PnlKnown { get; set; }
}

public sealed class UnexplainedPumpCheckRow
{
    public DateTimeOffset At { get; set; }
    public string Coin { get; set; } = string.Empty;
    public string Signal { get; set; } = UnexplainedPumpSignals.NoTrade;
    public string OrderState { get; set; } = "NotSent";
    public string Reason { get; set; } = string.Empty;
    public decimal Rise { get; set; }
    public int DaySpan { get; set; }
    public decimal PumpHigh { get; set; }
    public decimal Entry { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Stop { get; set; }
    public decimal? TakeProfit { get; set; }
}

public static class UnexplainedPumpDeskStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string PathFor(string root) =>
        Path.Combine(root, "artifacts", "data", "pump-shorts", "desk.json");

    public static UnexplainedPumpDeskFile Read(string root)
    {
        var path = PathFor(root);
        if (!File.Exists(path))
        {
            return new UnexplainedPumpDeskFile();
        }

        try
        {
            return JsonSerializer.Deserialize<UnexplainedPumpDeskFile>(File.ReadAllText(path), Json) ?? new UnexplainedPumpDeskFile();
        }
        catch (JsonException)
        {
            return new UnexplainedPumpDeskFile();
        }
    }

    public static async Task WriteAsync(string root, UnexplainedPumpDeskFile desk, CancellationToken cancellationToken)
    {
        var path = PathFor(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(desk, Json), cancellationToken);
    }

    /// <summary>Filled and closed rows stay. Other coins keep only the latest check.</summary>
    public static List<UnexplainedPumpCheckRow> Merge(IEnumerable<UnexplainedPumpCheckRow> fresh, IEnumerable<UnexplainedPumpCheckRow> previous)
    {
        var kept = new List<UnexplainedPumpCheckRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in fresh.Concat(previous).OrderByDescending(row => row.At))
        {
            if (row.OrderState is "Filled" or "Closed")
            {
                kept.Add(row);
            }
            else if (seen.Add(row.Coin))
            {
                kept.Add(row);
            }

            if (kept.Count >= 200)
            {
                break;
            }
        }

        return kept;
    }
}
