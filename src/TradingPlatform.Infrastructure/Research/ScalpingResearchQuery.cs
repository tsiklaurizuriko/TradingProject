using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TradingPlatform.Application.Trading;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Infrastructure.Research;

public sealed class ScalpingResearchQuery : IScalpingResearchQuery
{
    public const string Confirmation =
        "LIVE = OFF. Scalping LIVE = OFF. Isolated = enforced. Risk Engine = authoritative. No VALIDATED_FOR_PAPER.";

    private readonly string _dir;

    public ScalpingResearchQuery(IConfiguration configuration)
    {
        var configured = configuration["Trading:Scalping:ArtifactDirectory"];
        _dir = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(FindRepoRoot(), "artifacts", "strategy-research", "scalping");
    }

    public Task<ScalpingResearchSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadSummary());
    }

    public Task<IReadOnlyList<ScalpingCoverageDto>> GetCoverageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadSummary().Coverage);
    }

    public Task<ScalpingResearchRunDto?> GetRunAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(_dir, "runs", $"{id}.json");
        if (!File.Exists(path))
        {
            return Task.FromResult<ScalpingResearchRunDto?>(null);
        }

        var summary = OverlayCoverage(MapArtifact(File.ReadAllText(path), id));
        return Task.FromResult<ScalpingResearchRunDto?>(new ScalpingResearchRunDto(id, summary));
    }

    private ScalpingResearchSummaryDto ReadSummary()
    {
        var summaryPath = Path.Combine(_dir, "summary.json");
        var mapped = File.Exists(summaryPath)
            ? MapArtifact(File.ReadAllText(summaryPath), LastRunId())
            : EmptyShell();
        return OverlayCoverage(mapped);
    }

    private string? LastRunId()
    {
        var runs = Path.Combine(_dir, "runs");
        if (!Directory.Exists(runs))
        {
            return null;
        }

        return Directory.GetFiles(runs, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderByDescending(x => x)
            .FirstOrDefault();
    }

    private ScalpingResearchSummaryDto OverlayCoverage(ScalpingResearchSummaryDto mapped)
    {
        var file = LoadCoverageFile();
        if (file.Count == 0)
        {
            return mapped;
        }

        var usable = mapped.Coverage.Count(row => !string.IsNullOrWhiteSpace(row.Coin) && !string.IsNullOrWhiteSpace(row.Status));
        return usable >= file.Count ? mapped : mapped with { Coverage = file };
    }

    private IReadOnlyList<ScalpingCoverageDto> LoadCoverageFile()
    {
        var coveragePath = Path.Combine(_dir, "coverage.json");
        if (!File.Exists(coveragePath))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(coveragePath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return doc.RootElement.EnumerateArray().Select(MapCoverage).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private ScalpingResearchSummaryDto MapArtifact(string json, string? runId)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = doc.RootElement;
        var coverage = new List<ScalpingCoverageDto>();
        if (Prop(root, "coverage") is { ValueKind: JsonValueKind.Array } cov)
        {
            coverage.AddRange(cov.EnumerateArray().Select(MapCoverage));
        }

        var books = new List<ScalpingBookDto>();
        if (Prop(root, "books") is { ValueKind: JsonValueKind.Array } bookEl)
        {
            foreach (var row in bookEl.EnumerateArray())
            {
                books.Add(new ScalpingBookDto(
                    Str(row, "candidateId"),
                    Str(row, "symbol", "coin"),
                    Str(row, "timeframe"),
                    Str(row, "phase"),
                    Str(row, "costLabel"),
                    Str(row, "status"),
                    Int(row, "tradeCount"),
                    Dec(row, "profitFactor"),
                    Dec(row, "medianHoldingMinutes"),
                    Dec(row, "p25HoldingMinutes"),
                    Dec(row, "p75HoldingMinutes"),
                    Dec(row, "netPnl") ?? 0m));
            }
        }

        var rejects = new List<ScalpingRejectDto>();
        if (Prop(root, "occupancyRejects") is { ValueKind: JsonValueKind.Array } rej)
        {
            foreach (var row in rej.EnumerateArray())
            {
                rejects.Add(new ScalpingRejectDto(
                    Time(row, "time") ?? DateTimeOffset.UnixEpoch,
                    Str(row, "strategyKey"),
                    Str(row, "symbol", "coin"),
                    Str(row, "reason")));
            }
        }

        return new ScalpingResearchSummaryDto(
            Confirmation,
            true,
            true,
            true,
            true,
            false,
            runId ?? (string.IsNullOrWhiteSpace(Str(root, "id")) ? null : Str(root, "id")),
            StrategyRows(),
            coverage,
            books,
            rejects,
            Int(root, "sameCoinRejects"),
            Int(root, "slotRejects"),
            Int(root, "heatRejects"));
    }

    private ScalpingResearchSummaryDto EmptyShell() =>
        new(
            Confirmation,
            true,
            true,
            true,
            true,
            false,
            null,
            StrategyRows(),
            [],
            [],
            [],
            0,
            0,
            0);

    private static ScalpingStrategyStatusDto[] StrategyRows() =>
        StrategyTemplateKeys.Scalping.Select(key => new ScalpingStrategyStatusDto(
            key,
            StrategyTemplates.DisplayName(key),
            StrategyTemplateKeys.Family(key),
            StrategyTemplates.ResearchStatus(key),
            StrategyTemplates.Blurb(key),
            OperatorCatalog: false,
            Enabled: false)).ToArray();

    private static ScalpingCoverageDto MapCoverage(JsonElement row) =>
        new(
            Str(row, "symbol", "coin"),
            Str(row, "timeframe"),
            Time(row, "start"),
            Time(row, "end"),
            Str(row, "source"),
            Int(row, "gaps"),
            Int(row, "bars"),
            Dbl(row, "takerCoverage"),
            Str(row, "status"),
            Str(row, "notes"));

    private static JsonElement? Prop(JsonElement row, string name)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in row.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string Str(JsonElement row, string name, string? alt = null)
    {
        if (Prop(row, name) is { ValueKind: JsonValueKind.String } value)
        {
            return value.GetString() ?? "";
        }

        if (alt is not null && Prop(row, alt) is { ValueKind: JsonValueKind.String } other)
        {
            return other.GetString() ?? "";
        }

        return "";
    }

    private static int Int(JsonElement row, string name)
    {
        if (Prop(row, name) is not { ValueKind: JsonValueKind.Number } value)
        {
            return 0;
        }

        return value.TryGetInt32(out var n) ? n : 0;
    }

    private static double Dbl(JsonElement row, string name)
    {
        var value = Prop(row, name);
        return value is { ValueKind: JsonValueKind.Number } number ? number.GetDouble() : 0;
    }

    private static decimal? Dec(JsonElement row, string name)
    {
        var value = Prop(row, name);
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.Value.ValueKind == JsonValueKind.Number ? value.Value.GetDecimal() : null;
    }

    private static DateTimeOffset? Time(JsonElement row, string name)
    {
        var value = Prop(row, name);
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.Value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.Value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TradingPlatform.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
