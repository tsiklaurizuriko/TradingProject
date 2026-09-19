using System.Globalization;
using System.Text.Json;
using TradingPlatform.Domain.Errors;

namespace TradingPlatform.Strategies.Engine;

public sealed record EmaRsiParameters(
    int EmaFast = 20,
    int EmaSlow = 50,
    int RsiPeriod = 14,
    decimal RsiMinimum = 50,
    decimal StopLossPercent = 1.5m,
    decimal TakeProfitPercent = 3m);

public static class EmaRsiTemplate
{
    public static EmaRsiParameters Defaults { get; } = new();

    public static EmaRsiParameters Validate(EmaRsiParameters parameters)
    {
        StrategyTemplates.Validate(new StrategyTemplateParams(
            EmaFast: parameters.EmaFast,
            EmaSlow: parameters.EmaSlow,
            RsiPeriod: parameters.RsiPeriod,
            RsiMinimum: parameters.RsiMinimum));
        if (parameters.StopLossPercent <= 0 || parameters.TakeProfitPercent <= 0)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Stop loss and take profit belong on the Isolated risk book and must stay positive in legacy JSON.");
        }

        return parameters;
    }

    public static string Build(string name, int version, string timeframe, EmaRsiParameters parameters)
    {
        var p = Validate(parameters);
        return StrategyTemplates.Build(
            name,
            version,
            new StrategyTemplateParams(
                StrategyTemplateKeys.EmaRsiTrend,
                StrategySides.Long,
                timeframe,
                p.EmaFast,
                p.EmaSlow,
                p.RsiPeriod,
                p.RsiMinimum,
                Quality: new StrategyQualityParams()));
    }

    public static EmaRsiParameters Read(string? json)
    {
        var parsed = StrategyTemplates.Read(json);
        var legacy = ReadLegacy(json);
        return new EmaRsiParameters(
            parsed.EmaFast,
            parsed.EmaSlow,
            parsed.RsiPeriod,
            parsed.RsiMinimum,
            legacy.StopLossPercent,
            legacy.TakeProfitPercent);
    }

    internal static EmaRsiParameters ReadLegacy(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Defaults;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var entry = FirstConditions(root, "entry");
            var exit = FirstConditions(root, "exit");
            var ema = entry.FirstOrDefault(IsEma);
            if (ema.ValueKind == JsonValueKind.Undefined)
            {
                ema = exit.FirstOrDefault(IsEma);
            }

            var rsi = entry.FirstOrDefault(IsRsi);
            return new EmaRsiParameters(
                Number(ema, "period") is { } fast and > 0 ? (int)fast : Defaults.EmaFast,
                NestedPeriod(ema) is { } slow and > 0 ? (int)slow : Defaults.EmaSlow,
                Number(rsi, "period") is { } rsiPeriod and > 0 ? (int)rsiPeriod : Defaults.RsiPeriod,
                Number(rsi, "value") ?? Defaults.RsiMinimum,
                Number(exit.FirstOrDefault(n => TypeIs(n, "STOP_LOSS")), "percent") ?? Defaults.StopLossPercent,
                Number(exit.FirstOrDefault(n => TypeIs(n, "TAKE_PROFIT")), "percent") ?? Defaults.TakeProfitPercent);
        }
        catch (JsonException)
        {
            return Defaults;
        }
    }

    private static IReadOnlyList<JsonElement> FirstConditions(JsonElement root, string group)
    {
        if (!root.TryGetProperty(group, out var node) || !node.TryGetProperty("conditions", out var conditions) || conditions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return conditions.EnumerateArray().ToArray();
    }

    private static bool IsEma(JsonElement node) =>
        node.TryGetProperty("indicator", out var indicator) &&
        string.Equals(indicator.GetString(), "EMA", StringComparison.OrdinalIgnoreCase);

    private static bool IsRsi(JsonElement node) =>
        node.TryGetProperty("indicator", out var indicator) &&
        string.Equals(indicator.GetString(), "RSI", StringComparison.OrdinalIgnoreCase);

    private static bool TypeIs(JsonElement node, string type) =>
        node.TryGetProperty("type", out var value) &&
        string.Equals(value.GetString(), type, StringComparison.OrdinalIgnoreCase);

    private static decimal? NestedPeriod(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return Number(value, "period");
    }

    private static decimal? Number(JsonElement node, string property)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }
}
