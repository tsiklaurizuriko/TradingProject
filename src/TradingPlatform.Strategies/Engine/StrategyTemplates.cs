using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingPlatform.Domain.Errors;

namespace TradingPlatform.Strategies.Engine;

public static class StrategyTemplateKeys
{
    public const string EmaRsiTrend = "ema_rsi_trend";
    public const string MacdTrend = "macd_trend";
    public const string RsiPullback = "rsi_pullback";
    public const string BollingerReversion = "bollinger_reversion";
    public const string DonchianBreakout = "donchian_breakout";

    public static readonly string[] All =
    [
        EmaRsiTrend,
        MacdTrend,
        RsiPullback,
        BollingerReversion,
        DonchianBreakout
    ];

    public static readonly string[] SupportedTimeframes = ["5m", "15m", "1h"];
    public static readonly string[] SupportedDirections = ["LONG", "SHORT"];

    public static bool IsKnown(string? key) =>
        All.Contains((key ?? "").Trim(), StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? key)
    {
        var raw = (key ?? "").Trim().ToLowerInvariant();
        return All.FirstOrDefault(k => k == raw) ?? EmaRsiTrend;
    }
}

public static class StrategySides
{
    public const string Long = "Long";
    public const string Short = "Short";
    public const string Both = "Both";

    public static string Normalize(string? value)
    {
        var raw = (value ?? Long).Trim();
        if (raw.Equals(Short, StringComparison.OrdinalIgnoreCase)) return Short;
        if (raw.Equals(Both, StringComparison.OrdinalIgnoreCase)) return Both;
        return Long;
    }

    public static bool AllowsLong(string? value)
    {
        var side = Normalize(value);
        return side is Long or Both;
    }

    public static bool AllowsShort(string? value)
    {
        var side = Normalize(value);
        return side is Short or Both;
    }
}

public sealed record StrategyQualityParams(
    bool RequireVolume = false,
    int VolumeLookback = 20,
    decimal MinAtrPercent = 0m,
    decimal MaxAtrPercent = 0m);

public sealed record StrategyTemplateParams(
    string TemplateKey = StrategyTemplateKeys.EmaRsiTrend,
    string AllowedSide = StrategySides.Long,
    string Timeframe = "5m",
    int EmaFast = 20,
    int EmaSlow = 50,
    int RsiPeriod = 14,
    decimal RsiMinimum = 50m,
    decimal RsiLongMax = 68m,
    decimal RsiOversold = 30m,
    decimal RsiOverbought = 70m,
    int MacdFast = 12,
    int MacdSlow = 26,
    int MacdSignal = 9,
    int BbPeriod = 20,
    decimal BbStdDev = 2m,
    int DonchianLength = 20,
    StrategyQualityParams? Quality = null);

public static class StrategyTemplates
{
    public static StrategyTemplateParams DefaultsFor(string templateKey, bool qualityOn)
    {
        var key = StrategyTemplateKeys.Normalize(templateKey);
        var quality = qualityOn
            ? new StrategyQualityParams(true, 20, 0.15m, 4m)
            : new StrategyQualityParams();
        return new StrategyTemplateParams(TemplateKey: key, Quality: quality);
    }

    public static StrategyTemplateParams Validate(StrategyTemplateParams parameters)
    {
        var key = StrategyTemplateKeys.Normalize(parameters.TemplateKey);
        if (parameters.EmaFast < 2 || parameters.EmaSlow <= parameters.EmaFast)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "EMA slow period must be greater than the fast period.");
        }

        if (parameters.RsiPeriod < 2)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "RSI period must be at least 2.");
        }

        if (parameters.RsiMinimum is < 0 or > 100 || parameters.RsiLongMax is < 0 or > 100)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "RSI bounds must be between 0 and 100.");
        }

        if (parameters.RsiLongMax <= parameters.RsiMinimum)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "RSI long max must be greater than RSI minimum.");
        }

        if (parameters.RsiOversold is < 0 or > 50 || parameters.RsiOverbought is < 50 or > 100)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "RSI oversold must be 0–50 and overbought 50–100.");
        }

        if (parameters.MacdFast < 2 || parameters.MacdSlow <= parameters.MacdFast || parameters.MacdSignal < 2)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "MACD slow must be greater than fast, and signal at least 2.");
        }

        if (parameters.BbPeriod < 5 || parameters.BbStdDev <= 0m)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Bollinger period must be at least 5 and stddev greater than 0.");
        }

        if (parameters.DonchianLength < 5)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Donchian length must be at least 5.");
        }

        var quality = parameters.Quality ?? new StrategyQualityParams();
        if (quality.VolumeLookback < 2)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Volume lookback must be at least 2.");
        }

        if (quality.MinAtrPercent < 0m || quality.MaxAtrPercent < 0m)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "ATR percent bounds cannot be negative.");
        }

        if (quality.MaxAtrPercent > 0m && quality.MinAtrPercent > quality.MaxAtrPercent)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "ATR min percent must be at most ATR max percent.");
        }

        return parameters with
        {
            TemplateKey = key,
            AllowedSide = StrategySides.Normalize(parameters.AllowedSide),
            Quality = quality
        };
    }

    public static string Build(string name, int version, StrategyTemplateParams parameters)
    {
        var p = Validate(parameters);
        var q = p.Quality ?? new StrategyQualityParams();
        var json = $$"""
            {
              "name": {{JsonSerializer.Serialize(name)}},
              "version": {{version}},
              "template": {{JsonSerializer.Serialize(p.TemplateKey)}},
              "timeframe": {{JsonSerializer.Serialize(p.Timeframe)}},
              "allowedSide": {{JsonSerializer.Serialize(p.AllowedSide)}},
              "params": {
                "emaFast": {{p.EmaFast}},
                "emaSlow": {{p.EmaSlow}},
                "rsiPeriod": {{p.RsiPeriod}},
                "rsiMinimum": {{Invariant(p.RsiMinimum)}},
                "rsiLongMax": {{Invariant(p.RsiLongMax)}},
                "rsiOversold": {{Invariant(p.RsiOversold)}},
                "rsiOverbought": {{Invariant(p.RsiOverbought)}},
                "macdFast": {{p.MacdFast}},
                "macdSlow": {{p.MacdSlow}},
                "macdSignal": {{p.MacdSignal}},
                "bbPeriod": {{p.BbPeriod}},
                "bbStdDev": {{Invariant(p.BbStdDev)}},
                "donchianLength": {{p.DonchianLength}}
              },
              "quality": {
                "requireVolume": {{(q.RequireVolume ? "true" : "false")}},
                "volumeLookback": {{q.VolumeLookback}},
                "minAtrPercent": {{Invariant(q.MinAtrPercent)}},
                "maxAtrPercent": {{Invariant(q.MaxAtrPercent)}}
              }
            }
            """;
        new StrategyDefinitionValidator().Parse(json);
        return json;
    }

    public static StrategyTemplateParams Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return DefaultsFor(StrategyTemplateKeys.EmaRsiTrend, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var template = root.TryGetProperty("template", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(template) && LooksLegacyEmaRsi(root))
            {
                template = StrategyTemplateKeys.EmaRsiTrend;
            }

            var p = root.TryGetProperty("params", out var paramsNode) ? paramsNode : root;
            var q = root.TryGetProperty("quality", out var qualityNode) ? qualityNode : default;
            var legacy = EmaRsiTemplate.ReadLegacy(json);
            return new StrategyTemplateParams(
                StrategyTemplateKeys.Normalize(template),
                StrategySides.Normalize(root.TryGetProperty("allowedSide", out var side) ? side.GetString() : StrategySides.Long),
                root.TryGetProperty("timeframe", out var tf) ? tf.GetString() ?? "5m" : "5m",
                Int(p, "emaFast") ?? legacy.EmaFast,
                Int(p, "emaSlow") ?? legacy.EmaSlow,
                Int(p, "rsiPeriod") ?? legacy.RsiPeriod,
                Dec(p, "rsiMinimum") ?? legacy.RsiMinimum,
                Dec(p, "rsiLongMax") ?? 68m,
                Dec(p, "rsiOversold") ?? 30m,
                Dec(p, "rsiOverbought") ?? 70m,
                Int(p, "macdFast") ?? 12,
                Int(p, "macdSlow") ?? 26,
                Int(p, "macdSignal") ?? 9,
                Int(p, "bbPeriod") ?? 20,
                Dec(p, "bbStdDev") ?? 2m,
                Int(p, "donchianLength") ?? 20,
                new StrategyQualityParams(
                    Bool(q, "requireVolume"),
                    Int(q, "volumeLookback") ?? 20,
                    Dec(q, "minAtrPercent") ?? 0m,
                    Dec(q, "maxAtrPercent") ?? 0m));
        }
        catch (JsonException)
        {
            return DefaultsFor(StrategyTemplateKeys.EmaRsiTrend, false);
        }
    }

    public static string DisplayName(string templateKey) => StrategyTemplateKeys.Normalize(templateKey) switch
    {
        StrategyTemplateKeys.MacdTrend => "MACD Trend",
        StrategyTemplateKeys.RsiPullback => "RSI Pullback",
        StrategyTemplateKeys.BollingerReversion => "Bollinger Reversion",
        StrategyTemplateKeys.DonchianBreakout => "Donchian Breakout",
        _ => "EMA RSI Trend"
    };

    public static string Blurb(string templateKey) => StrategyTemplateKeys.Normalize(templateKey) switch
    {
        StrategyTemplateKeys.MacdTrend => "LONG: MACD სიგნალს ზემოთ კვეთს, ჰისტოგრამა დადებითია და ფასი ნელ EMA-ზე მაღალია. SHORT: პირიქით. გამოსვლა: საპირისპირო MACD გადაკვეთა.",
        StrategyTemplateKeys.RsiPullback => "LONG მხოლოდ აღმავალ ტრენდში (ფასი ნელ EMA-ზე მაღალია), როცა RSI oversold-ს ქვემოდან კვეთს. SHORT მხოლოდ დაღმავალ ტრენდში overbought-ზე. გამოსვლა: RSI ისევ 50-ს კვეთს.",
        StrategyTemplateKeys.BollingerReversion => "LONG: ფასი ქვედა ბოლინჯერის ზოლში ისევ იხურება და ნელ EMA-ზე მაღალი რჩება. SHORT: სარკისებურად. გამოსვლა: შუა ზოლზე.",
        StrategyTemplateKeys.DonchianBreakout => "LONG: დახურვა N-სანთლის მაქსიმუმს არღვევს. SHORT: დახურვა N-სანთლის მინიმუმს არღვევს. გამოსვლა: საპირისპირო ზოლზე.",
        _ => "LONG: სწრაფი EMA ნელს ზემოთ კვეთს, ფასი ნელ EMA-ზე მაღალია და RSI min–max შუალედშია (ნაგულისხმევი 50–68). SHORT: პირიქით. გამოსვლა: საპირისპირო EMA გადაკვეთა."
    };

    private static bool LooksLegacyEmaRsi(JsonElement root) =>
        root.TryGetProperty("entry", out _) && !root.TryGetProperty("template", out _);

    private static string Invariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static int? Int(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static decimal? Dec(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var n) => n,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool Bool(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True ||
               (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
    }
}
