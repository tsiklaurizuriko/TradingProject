using System.Text.Json;
using System.Text.Json.Serialization;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

public enum BooleanOperator
{
    And,
    Or,
    Not
}

public enum ComparisonKind
{
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
    Equals,
    CrossesAbove,
    CrossesBelow
}

public sealed class StrategyDefinition
{
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string Symbol { get; set; } = "BTCUSDT";
    public string Timeframe { get; set; } = "5m";
    public string? Template { get; set; }
    public string? AllowedSide { get; set; }
    public StrategyDefinitionParams? Params { get; set; }
    public StrategyDefinitionQuality? Quality { get; set; }
    public ConditionGroup? Entry { get; set; }
    public ConditionGroup? Exit { get; set; }
}

public sealed class StrategyDefinitionParams
{
    public int EmaFast { get; set; } = 20;
    public int EmaSlow { get; set; } = 50;
    public int RsiPeriod { get; set; } = 14;
    public decimal RsiMinimum { get; set; } = 50m;
    public decimal RsiLongMax { get; set; } = 68m;
    public decimal RsiOversold { get; set; } = 30m;
    public decimal RsiOverbought { get; set; } = 70m;
    public int MacdFast { get; set; } = 12;
    public int MacdSlow { get; set; } = 26;
    public int MacdSignal { get; set; } = 9;
    public int BbPeriod { get; set; } = 20;
    public decimal BbStdDev { get; set; } = 2m;
    public int DonchianLength { get; set; } = 20;
    public int EntryLookback { get; set; } = 20;
    public int ExitLookback { get; set; } = 10;
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrStopMultiplier { get; set; } = 2m;
    public int TrendEmaPeriod { get; set; } = 50;
    public bool VolumeFilterEnabled { get; set; } = true;
    public int RelativeVolumePeriod { get; set; } = 20;
    public decimal MinimumRelativeVolume { get; set; } = 1m;
    public decimal MaxVwapDistanceAtr { get; set; } = 0.75m;
    public decimal StopAtrMultiplier { get; set; } = 1.5m;
    public int VolatilityLookback { get; set; } = 100;
    public decimal CompressionPercentile { get; set; } = 0.20m;
    public int AtrExpansionLookback { get; set; } = 20;
    public decimal BreakoutRelativeVolume { get; set; } = 1.2m;
    public int SupertrendPeriod { get; set; } = 10;
    public decimal SupertrendMultiplier { get; set; } = 3m;
    public int AdxPeriod { get; set; } = 14;
    public decimal MinimumAdx { get; set; } = 20m;
    public int OiLookback { get; set; } = 20;
    public decimal OiChangeThreshold { get; set; } = 0.02m;
    public decimal PriceChangeThreshold { get; set; } = 0.01m;
    public string OiHypothesis { get; set; } = "continuation";
    public int FundingLookback { get; set; } = 24;
    public decimal FundingExtremePercentile { get; set; } = 0.90m;
    public string FundingHypothesis { get; set; } = "continuation";
    public decimal? ZScoreEntry { get; set; }
    public decimal? ValueAreaPercent { get; set; }
    public decimal? SweepDepthAtr { get; set; }
    public int? SwingLength { get; set; }
    public bool? UseFuturesFilter { get; set; }
    public decimal PriceDisplacementAtr { get; set; } = 1.5m;
    public decimal OiExtremePercentile { get; set; } = 0.90m;
}

public sealed class StrategyDefinitionQuality
{
    public bool RequireVolume { get; set; }
    public int VolumeLookback { get; set; } = 20;
    public decimal MinAtrPercent { get; set; }
    public decimal MaxAtrPercent { get; set; }
}

public sealed class ConditionGroup
{
    public BooleanOperator Operator { get; set; } = BooleanOperator.And;
    public List<ConditionNode> Conditions { get; set; } = [];
}

public sealed class ConditionNode
{
    public string? Type { get; set; }
    public decimal? Percent { get; set; }
    public string? Indicator { get; set; }
    public int? Period { get; set; }
    public ComparisonKind? Comparison { get; set; }
    public JsonElement? Value { get; set; }
    public ConditionGroup? Group { get; set; }
}

public sealed class StrategyContext
{
    public required IReadOnlyList<MarketCandle> ClosedCandles { get; init; }
    public decimal? AverageEntryPrice { get; init; }
    public decimal CurrentPrice { get; init; }
    public bool HasOpenPosition { get; init; }
    public PositionSide PositionSide { get; init; } = PositionSide.Long;
    public DateTimeOffset? PositionOpenedAt { get; init; }
    /// <summary>Binance contract id, for rules that differ by coin. Empty outside a live bot.</summary>
    public string Symbol { get; init; } = "";
    /// <summary>Optional higher-timeframe cache. LIVE and frozen templates leave this null.</summary>
    public CausalIndicatorCache? HigherTimeframeCache { get; init; }
    /// <summary>Open interest aligned to ClosedCandles. Null means historical OI is unavailable.</summary>
    public IReadOnlyList<decimal?>? OpenInterest { get; init; }
    /// <summary>Funding rate aligned to ClosedCandles. Null means historical funding is unavailable.</summary>
    public IReadOnlyList<decimal?>? FundingRate { get; init; }
    /// <summary>Mark price aligned to ClosedCandles. Null means historical mark is unavailable.</summary>
    public IReadOnlyList<decimal?>? MarkPrice { get; init; }
    /// <summary>Index price aligned to ClosedCandles. Null means historical index is unavailable.</summary>
    public IReadOnlyList<decimal?>? IndexPrice { get; init; }
    /// <summary>Normalized basis (Mark-Index)/Index. Null means basis cannot be reconstructed.</summary>
    public IReadOnlyList<decimal?>? NormalizedBasis { get; init; }
}

public sealed class StrategyFuturesSeries
{
    public IReadOnlyList<decimal?>? OpenInterest { get; init; }
    public IReadOnlyList<decimal?>? FundingRate { get; init; }
    public IReadOnlyList<decimal?>? MarkPrice { get; init; }
    public IReadOnlyList<decimal?>? IndexPrice { get; init; }
    public IReadOnlyList<decimal?>? NormalizedBasis { get; init; }
}

public sealed record StrategySignalDetail(
    SignalType Signal,
    string Reason,
    DateTimeOffset? TimestampUtc = null,
    decimal? SuggestedStop = null,
    decimal? SuggestedTakeProfit = null,
    IReadOnlyDictionary<string, decimal?>? Snapshot = null,
    string Status = "RESEARCHING");

public interface IStrategyEngine
{
    SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason);

    SignalType EvaluateAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index,
        out string reason)
    {
        var take = Math.Clamp(index + 1, 0, cache.Candles.Count);
        var prefix = new MarketCandle[take];
        for (var i = 0; i < take; i++)
        {
            prefix[i] = cache.Candles[i];
        }

        return Evaluate(
            definition,
            new StrategyContext
            {
                ClosedCandles = prefix,
                AverageEntryPrice = context.AverageEntryPrice,
                CurrentPrice = take == 0 ? context.CurrentPrice : prefix[take - 1].Close,
                HasOpenPosition = context.HasOpenPosition,
                PositionSide = context.PositionSide,
                PositionOpenedAt = context.PositionOpenedAt,
                Symbol = context.Symbol,
                HigherTimeframeCache = context.HigherTimeframeCache,
                OpenInterest = Prefix(context.OpenInterest, take),
                FundingRate = Prefix(context.FundingRate, take),
                MarkPrice = Prefix(context.MarkPrice, take),
                IndexPrice = Prefix(context.IndexPrice, take),
                NormalizedBasis = Prefix(context.NormalizedBasis, take)
            },
            out reason);
    }

    StrategySignalDetail EvaluateDetailAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index)
    {
        var signal = EvaluateAt(definition, context, cache, index, out var reason);
        return new StrategySignalDetail(signal, reason);
    }

    private static IReadOnlyList<decimal?>? Prefix(IReadOnlyList<decimal?>? series, int take)
    {
        if (series is null)
        {
            return null;
        }

        var n = Math.Min(take, series.Count);
        var copy = new decimal?[n];
        for (var i = 0; i < n; i++)
        {
            copy[i] = series[i];
        }

        return copy;
    }
}

public sealed class StrategyDefinitionValidator
{
    public void Validate(StrategyDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy name is required.");
        }

        var hasTemplate = !string.IsNullOrWhiteSpace(definition.Template);
        if (!hasTemplate && definition.Entry is null)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Entry conditions are required.");
        }

        if (!hasTemplate && definition.Exit is null)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Exit conditions are required.");
        }

        if (!TimeframeExtensions.TryParseInterval(definition.Timeframe, out _))
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, $"Unknown timeframe '{definition.Timeframe}'.");
        }
    }

    public StrategyDefinition Parse(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        StrategyDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<StrategyDefinition>(json, SerializerOptions());
        }
        catch (Exception ex)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy JSON could not be parsed.", new Dictionary<string, object?> { ["error"] = ex.Message });
        }
        if (definition is null)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy JSON is empty.");
        }
        Validate(definition);
        return definition;
    }

    public static JsonSerializerOptions SerializerOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new FlexibleEnumConverter<BooleanOperator>());
        options.Converters.Add(new FlexibleEnumConverter<ComparisonKind>());
        return options;
    }
}

internal sealed class FlexibleEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString() ?? string.Empty;
        var normalized = raw.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        foreach (var name in Enum.GetNames<T>())
        {
            if (name.Equals(raw, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<T>(name);
            }
        }
        if (raw.Equals("AND", StringComparison.OrdinalIgnoreCase) && typeof(T) == typeof(BooleanOperator)) return (T)(object)BooleanOperator.And;
        if (raw.Equals("OR", StringComparison.OrdinalIgnoreCase) && typeof(T) == typeof(BooleanOperator)) return (T)(object)BooleanOperator.Or;
        if (raw.Equals("NOT", StringComparison.OrdinalIgnoreCase) && typeof(T) == typeof(BooleanOperator)) return (T)(object)BooleanOperator.Not;
        if (raw.Equals("CROSSES_ABOVE", StringComparison.OrdinalIgnoreCase)) return (T)(object)ComparisonKind.CrossesAbove;
        if (raw.Equals("CROSSES_BELOW", StringComparison.OrdinalIgnoreCase)) return (T)(object)ComparisonKind.CrossesBelow;
        if (raw.Equals("GREATER_THAN", StringComparison.OrdinalIgnoreCase)) return (T)(object)ComparisonKind.GreaterThan;
        if (raw.Equals("LESS_THAN", StringComparison.OrdinalIgnoreCase)) return (T)(object)ComparisonKind.LessThan;
        throw new JsonException($"Cannot convert '{raw}' to {typeof(T).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public sealed class StrategyEngine : IStrategyEngine
{
    private readonly IndicatorRegistry _indicators = new();
    private readonly Dictionary<string, IReadOnlyList<decimal?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason)
    {
        _cache.Clear();
        if (context.ClosedCandles.Count == 0)
        {
            reason = "No closed candles yet.";
            return SignalType.NoAction;
        }

        if (!string.IsNullOrWhiteSpace(definition.Template) || definition.Params is not null)
        {
            return StrategyTemplateEvaluator.Evaluate(definition, context, _indicators, out reason);
        }

        if (context.HasOpenPosition)
        {
            if (EvaluateGroup(definition.Exit, context, requirePosition: true))
            {
                reason = "Exit conditions matched.";
                return SignalType.Exit;
            }
            reason = "Position open; exit not triggered.";
            return SignalType.Hold;
        }

        if (EvaluateGroup(definition.Entry, context, requirePosition: false))
        {
            reason = "Entry conditions matched.";
            return SignalType.Buy;
        }

        reason = "Entry conditions not matched.";
        return SignalType.NoAction;
    }

    public SignalType EvaluateAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index,
        out string reason)
    {
        if (!string.IsNullOrWhiteSpace(definition.Template) || definition.Params is not null)
        {
            return StrategyTemplateEvaluator.EvaluateAt(definition, context, cache, index, out reason);
        }

        return Evaluate(definition, context, out reason);
    }

    public StrategySignalDetail EvaluateDetailAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index)
    {
        if (!string.IsNullOrWhiteSpace(definition.Template) || definition.Params is not null)
        {
            return StrategyTemplateEvaluator.EvaluateDetailAt(definition, context, cache, index);
        }

        var signal = Evaluate(definition, context, out var reason);
        return new StrategySignalDetail(signal, reason);
    }

    StrategySignalDetail IStrategyEngine.EvaluateDetailAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index) =>
        EvaluateDetailAt(definition, context, cache, index);

    private bool EvaluateGroup(ConditionGroup? group, StrategyContext context, bool requirePosition)
    {
        if (group is null || group.Conditions.Count == 0)
        {
            return false;
        }

        var results = group.Conditions.Select(c => EvaluateNode(c, context, requirePosition)).ToArray();
        return group.Operator switch
        {
            BooleanOperator.And => results.All(x => x),
            BooleanOperator.Or => results.Any(x => x),
            BooleanOperator.Not => !results[0],
            _ => false
        };
    }

    private bool EvaluateNode(ConditionNode node, StrategyContext context, bool requirePosition)
    {
        if (node.Group is not null)
        {
            return EvaluateGroup(node.Group, context, requirePosition);
        }

        // Protective SL/TP live on the Isolated risk book (fill snapshot / Binance closes).
        // Strategy JSON may still contain leftover STOP_LOSS / TAKE_PROFIT nodes; ignore them.
        if (string.Equals(node.Type, "STOP_LOSS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.Type, "TAKE_PROFIT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (node.Indicator is null || node.Comparison is null)
        {
            return false;
        }

        var left = Series(node.Indicator, node.Period ?? 14, context.ClosedCandles);
        var right = ResolveValue(node.Value, context.ClosedCandles);
        return Compare(left, right, node.Comparison.Value);
    }

    private bool Compare(IReadOnlyList<decimal?> left, IReadOnlyList<decimal?> right, ComparisonKind comparison)
    {
        var i = left.Count - 1;
        var prev = i - 1;
        if (i < 0) return false;
        var l = left[i];
        var r = right.Count == left.Count ? right[i] : right.LastOrDefault();
        if (l is null || r is null) return false;

        return comparison switch
        {
            ComparisonKind.GreaterThan => l > r,
            ComparisonKind.LessThan => l < r,
            ComparisonKind.GreaterOrEqual => l >= r,
            ComparisonKind.LessOrEqual => l <= r,
            ComparisonKind.Equals => l == r,
            ComparisonKind.CrossesAbove => prev >= 0 && left[prev] is { } lp && right[Math.Min(prev, right.Count - 1)] is { } rp && lp <= rp && l > r,
            ComparisonKind.CrossesBelow => prev >= 0 && left[prev] is { } lp && right[Math.Min(prev, right.Count - 1)] is { } rp && lp >= rp && l < r,
            _ => false
        };
    }

    private IReadOnlyList<decimal?> ResolveValue(JsonElement? value, IReadOnlyList<MarketCandle> candles)
    {
        if (value is null)
        {
            return candles.Select(_ => (decimal?)null).ToArray();
        }
        var el = value.Value;
        if (el.ValueKind == JsonValueKind.Number)
        {
            var number = el.GetDecimal();
            return Enumerable.Repeat((decimal?)number, candles.Count).ToArray();
        }
        if (el.ValueKind == JsonValueKind.Object)
        {
            var indicator = el.TryGetProperty("indicator", out var n) ? n.GetString() : "SMA";
            var period = el.TryGetProperty("period", out var p) ? p.GetInt32() : 14;
            return Series(indicator ?? "SMA", period, candles);
        }
        return candles.Select(_ => (decimal?)null).ToArray();
    }

    private IReadOnlyList<decimal?> Series(string name, int period, IReadOnlyList<MarketCandle> candles)
    {
        var key = $"{name}:{period}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var computed = _indicators.Create(name, period).Compute(candles);
        _cache[key] = computed;
        return computed;
    }
}
