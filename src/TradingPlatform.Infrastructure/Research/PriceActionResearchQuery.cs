using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TradingPlatform.Application.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.PriceAction;

namespace TradingPlatform.Infrastructure.Research;

public sealed class PriceActionResearchQuery : IPriceActionResearchQuery
{
    public const string Confirmation = PriceActionReportConfirmation.Text;

    private readonly string _dir;
    private readonly string _alphaDir;
    private readonly string _contextualDir;

    public PriceActionResearchQuery(IConfiguration configuration)
    {
        var configured = configuration["Trading:PriceAction:ArtifactDirectory"];
        _dir = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(FindRepoRoot(), "artifacts", "strategy-research", "price-action");
        var parent = Directory.GetParent(_dir)?.FullName ?? _dir;
        _alphaDir = Path.Combine(parent, "price-action-alpha");
        _contextualDir = Path.Combine(parent, "contextual-price-action-alpha");
    }

    public Task<PriceActionResearchSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadSummary());
    }

    public Task<IReadOnlyList<PriceActionOccurrenceDto>> GetOccurrencesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadSummary().Occurrences);
    }

    public Task<ContextualPriceActionSummaryDto> GetContextualSummaryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadContextual());
    }

    private ContextualPriceActionSummaryDto ReadContextual()
    {
        const string confirmation =
            "LIVE = OFF. SCALPING LIVE = OFF. PRICE ACTION LIVE = OFF. PAPER promotion = OFF. No VALIDATED_FOR_PAPER. Research-only contextual Price Action.";
        var path = Path.Combine(_contextualDir, "summary.json");
        if (!File.Exists(path))
        {
            return new ContextualPriceActionSummaryDto(
                confirmation, true, true, true, true, false, 0, 8, 0, [],
                "DATA_UNAVAILABLE: funding, OI, taker flow, liquidations, order book, basis. OHLCV only.");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var hypotheses = new List<string>();
        if (root.TryGetProperty("hypotheses", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            hypotheses.AddRange(rows.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
        }

        return new ContextualPriceActionSummaryDto(
            confirmation,
            true,
            true,
            true,
            true,
            false,
            root.TryGetProperty("hypothesisCount", out var count) ? count.GetInt32() : hypotheses.Count,
            root.TryGetProperty("families", out var families) ? families.GetInt32() : 8,
            root.TryGetProperty("survivors", out var survivors) ? survivors.GetInt32() : 0,
            hypotheses,
            root.TryGetProperty("futuresData", out var futures) ? futures.GetString() ?? "" : "DATA_UNAVAILABLE");
    }

    private PriceActionResearchSummaryDto ReadSummary()
    {
        var alphaSummaryPath = Path.Combine(_alphaDir, "summary.json");
        var summaryPath = File.Exists(alphaSummaryPath)
            ? alphaSummaryPath
            : Path.Combine(_dir, "summary.json");
        if (!File.Exists(summaryPath))
        {
            return EmptyShell() with { DataExpansion = LoadDataExpansion() };
        }

        var runId = string.Equals(summaryPath, alphaSummaryPath, StringComparison.OrdinalIgnoreCase)
            ? null
            : LastRunId();
        return MapArtifact(File.ReadAllText(summaryPath), runId) with
        {
            DataExpansion = LoadDataExpansion()
        };
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

    private PriceActionResearchSummaryDto MapArtifact(string json, string? runId)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = doc.RootElement;
        var coverage = MapArray(root, "coverage", MapCoverage);
        var books = MapArray(root, "books", MapBook);
        var sequences = MapArray(root, "sequences", MapSequence);
        var patterns = MapArray(root, "patterns", MapPattern);
        var occurrences = MapArray(root, "occurrences", MapOccurrence);
        var hypotheses = new List<string>();
        if (Prop(root, "hypotheses") is { ValueKind: JsonValueKind.Array } hy)
        {
            hypotheses.AddRange(hy.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
        }

        var rejects = MapArray(root, "occupancyRejects", MapReject);
        return new PriceActionResearchSummaryDto(
            Confirmation,
            true,
            true,
            true,
            true,
            true,
            false,
            PatternKinds.NotImplemented,
            runId ?? (string.IsNullOrWhiteSpace(Str(root, "id")) ? null : Str(root, "id")),
            StrategyRows(),
            coverage,
            books,
            sequences,
            patterns,
            occurrences,
            hypotheses,
            [],
            rejects,
            Int(root, "sameCoinRejects"),
            Int(root, "slotRejects"),
            Int(root, "heatRejects"));
    }

    private PriceActionResearchSummaryDto EmptyShell() =>
        new(
            Confirmation,
            true, true, true, true, true, false,
            PatternKinds.NotImplemented,
            null,
            StrategyRows(),
            [], [], [], [], [], [],
            [],
            [], 0, 0, 0);

    private IReadOnlyList<PriceActionDataCoverageDto> LoadDataExpansion()
    {
        var alphaPath = Path.Combine(_alphaDir, "coverage-post.json");
        if (File.Exists(alphaPath))
        {
            try
            {
                using var alpha = JsonDocument.Parse(File.ReadAllText(alphaPath));
                return MapArray(alpha.RootElement, "coverage", MapAlphaDataCoverage);
            }
            catch
            {
                // Preserve the established Phase-7 fallback if the newer artifact is incomplete.
            }
        }

        var path = Path.Combine(_dir, "phase7-data-expansion.json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return MapArray(doc.RootElement, "coverage", MapDataCoverage);
        }
        catch
        {
            return [];
        }
    }

    private static ScalpingStrategyStatusDto[] StrategyRows() =>
        StrategyTemplateKeys.PriceAction.Select(key => new ScalpingStrategyStatusDto(
            key,
            StrategyTemplates.DisplayName(key),
            StrategyTemplateKeys.Family(key),
            StrategyTemplates.ResearchStatus(key),
            StrategyTemplates.Blurb(key),
            false,
            false)).ToArray();

    private static ScalpingCoverageDto MapCoverage(JsonElement row) =>
        new(Str(row, "symbol", "coin"), Str(row, "timeframe"), Time(row, "start"), Time(row, "end"), Str(row, "source"), Int(row, "gaps"), Int(row, "bars"), Dbl(row, "takerCoverage"), Str(row, "status"), Str(row, "notes"));

    private static ScalpingBookDto MapBook(JsonElement row) =>
        new(Str(row, "candidateId"), Str(row, "symbol", "coin"), Str(row, "timeframe"), Str(row, "phase"), Str(row, "costLabel"), Str(row, "status"), Int(row, "tradeCount"), Dec(row, "profitFactor"), Dec(row, "medianHoldingMinutes"), Dec(row, "p25HoldingMinutes"), Dec(row, "p75HoldingMinutes"), Dec(row, "netPnl") ?? 0m);

    private static PriceActionSequenceDto MapSequence(JsonElement row) =>
        new(Str(row, "symbol", "coin"), Str(row, "timeframe"), Str(row, "name"), Int(row, "occurrences"), Dec(row, "meanFwd1") ?? 0, Dec(row, "meanFwd3") ?? 0, Dec(row, "meanFwd5") ?? 0, Dec(row, "medianMfe") ?? 0, Dec(row, "medianMae") ?? 0, Dec(row, "hitPos50") ?? 0, Dec(row, "hitNeg50") ?? 0);

    private static PriceActionPatternStatDto MapPattern(JsonElement row) =>
        new(Str(row, "symbol", "coin"), Str(row, "timeframe"), Str(row, "patternType"), Str(row, "status"), Int(row, "occurrences"), Dec(row, "meanFwd3") ?? 0, Dec(row, "medianMfe") ?? 0, Dec(row, "medianMae") ?? 0);

    private static PriceActionDataCoverageDto MapDataCoverage(JsonElement row) =>
        new(
            Str(row, "symbol", "coin"),
            Str(row, "timeframe"),
            Time(row, "requestedFrom") ?? DateTimeOffset.UnixEpoch,
            Time(row, "requestedTo") ?? DateTimeOffset.UnixEpoch,
            Time(row, "actualFirstBar"),
            Time(row, "actualLastBar"),
            Int(row, "barCount"),
            Int(row, "expectedBarCount"),
            Int(row, "gapCount"),
            Long(row, "missingBarCount"),
            Int(row, "duplicateCount"),
            Dec(row, "coveragePercent") ?? 0m,
            Int(row, "downloadedPages"),
            Int(row, "cacheHits"),
            Int(row, "cacheMisses"),
            Bool(row, "continuous"),
            Bool(row, "qualityPassed"),
            Str(row, "status"));

    private static PriceActionDataCoverageDto MapAlphaDataCoverage(JsonElement row) =>
        new(
            Str(row, "symbol", "coin"),
            Str(row, "timeframe"),
            Time(row, "requestedFrom") ?? DateTimeOffset.UnixEpoch,
            Time(row, "requestedTo") ?? DateTimeOffset.UnixEpoch,
            Time(row, "firstTimestamp"),
            Time(row, "lastTimestamp"),
            Int(row, "barCount"),
            Int(row, "expectedBarCount"),
            Int(row, "gapCount"),
            Long(row, "missingBarCount"),
            Int(row, "duplicateCount"),
            Dec(row, "coveragePercent") ?? 0m,
            Int(row, "downloadedPages"),
            Int(row, "cacheHits"),
            Int(row, "cacheMisses"),
            Int(row, "gapCount") == 0
                && Int(row, "duplicateCount") == 0
                && Int(row, "outOfOrderCount") == 0,
            Bool(row, "qualityPassed"),
            Str(row, "status"));

    private static ScalpingRejectDto MapReject(JsonElement row) =>
        new(Time(row, "time") ?? DateTimeOffset.UnixEpoch, Str(row, "strategyKey"), Str(row, "symbol", "coin"), Str(row, "reason"));

    private static PriceActionOccurrenceDto MapOccurrence(JsonElement row)
    {
        var points = MapArray(row, "points", p => new PriceActionPointDto(Str(p, "role"), Dec(p, "price") ?? 0, Time(p, "time")));
        var bars = MapArray(row, "bars", b => new PriceActionBarDto(Long(b, "time"), Dec(b, "open") ?? 0, Dec(b, "high") ?? 0, Dec(b, "low") ?? 0, Dec(b, "close") ?? 0, Dec(b, "volume") ?? 0));
        return new PriceActionOccurrenceDto(
            Str(row, "symbol", "coin"),
            Str(row, "timeframe"),
            Str(row, "patternType"),
            Str(row, "version"),
            Time(row, "start"),
            Time(row, "detection"),
            Time(row, "confirmation"),
            Time(row, "entry"),
            Dec(row, "neckline"),
            Dec(row, "level"),
            Str(row, "direction"),
            Str(row, "status"),
            points,
            bars);
    }

    private static IReadOnlyList<T> MapArray<T>(JsonElement root, string name, Func<JsonElement, T> map)
    {
        if (Prop(root, name) is not { ValueKind: JsonValueKind.Array } arr)
        {
            return [];
        }

        return arr.EnumerateArray().Select(map).ToArray();
    }

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

    private static int Int(JsonElement row, string name) =>
        Prop(row, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var n) ? n : 0;

    private static long Long(JsonElement row, string name) =>
        Prop(row, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var n) ? n : 0;

    private static bool Bool(JsonElement row, string name) =>
        Prop(row, name) is { ValueKind: JsonValueKind.True };

    private static double Dbl(JsonElement row, string name) =>
        Prop(row, name) is { ValueKind: JsonValueKind.Number } number ? number.GetDouble() : 0;

    private static decimal? Dec(JsonElement row, string name) =>
        Prop(row, name) is { ValueKind: JsonValueKind.Number } number ? number.GetDecimal() : null;

    private static DateTimeOffset? Time(JsonElement row, string name)
    {
        var value = Prop(row, name);
        if (value is { ValueKind: JsonValueKind.String } text && DateTimeOffset.TryParse(text.GetString(), out var parsed))
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
            if (File.Exists(Path.Combine(dir.FullName, "TradingPlatform.slnx")) || File.Exists(Path.Combine(dir.FullName, "TradingPlatform.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}

internal static class PriceActionReportConfirmation
{
    public const string Text =
        "LIVE = OFF. SCALPING LIVE = OFF. PRICE ACTION LIVE = OFF. Isolated = enforced. Risk Engine = authoritative. No VALIDATED_FOR_PAPER. No future leakage.";
}
