using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.News;

public static class NewsRejection
{
    public const string NewsTooOld = "NEWS_TOO_OLD";
    public const string LowConfidence = "LOW_CONFIDENCE";
    public const string UnverifiedSource = "UNVERIFIED_SOURCE";
    public const string DuplicateEvent = "DUPLICATE_EVENT";
    public const string AlreadyPricedIn = "ALREADY_PRICED_IN";
    public const string MarketConflict = "MARKET_CONFLICT";
    public const string InsufficientMarketData = "INSUFFICIENT_MARKET_DATA";
    public const string StaleMarketData = "STALE_MARKET_DATA";
    public const string LowLiquidity = "LOW_LIQUIDITY";
    public const string WideSpread = "WIDE_SPREAD";
    public const string ExpectedEdgeTooSmall = "EXPECTED_EDGE_TOO_SMALL";
    public const string RiskLimit = "RISK_LIMIT";
    public const string ExistingPosition = "EXISTING_POSITION";
    public const string Cooldown = "COOLDOWN";
    public const string UnknownAsset = "UNKNOWN_ASSET";
    public const string AiAnalysisFailed = "AI_ANALYSIS_FAILED";
    public const string InvalidAiResponse = "INVALID_AI_RESPONSE";
    public const string NoClearDirection = "NO_CLEAR_DIRECTION";
    public const string EventAlreadyTraded = "EVENT_ALREADY_TRADED";
    public const string LookAhead = "LOOKAHEAD";
    public const string LowImpact = "LOW_IMPACT";

    public static bool IsRetryable(string? code) =>
        code is AiAnalysisFailed or InsufficientMarketData or StaleMarketData;

    public static string FromRisk(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return RiskLimit;
        }

        if (reason.Contains("already has an open", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("open position", StringComparison.OrdinalIgnoreCase))
        {
            return ExistingPosition;
        }

        if (reason.Contains("cooldown", StringComparison.OrdinalIgnoreCase))
        {
            return Cooldown;
        }

        if (reason.Contains("stale", StringComparison.OrdinalIgnoreCase))
        {
            return StaleMarketData;
        }

        return RiskLimit;
    }

    public static string FromConfirmation(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return NoClearDirection;
        }

        if (reason.Contains("Future news", StringComparison.Ordinal))
        {
            return LookAhead;
        }

        if (reason.Contains("market conflict", StringComparison.OrdinalIgnoreCase))
        {
            return MarketConflict;
        }

        if (reason.Contains("market data", StringComparison.OrdinalIgnoreCase))
        {
            return StaleMarketData;
        }

        if (reason.Contains("age", StringComparison.OrdinalIgnoreCase))
        {
            return NewsTooOld;
        }

        if (reason.Contains("confidence", StringComparison.OrdinalIgnoreCase))
        {
            return LowConfidence;
        }

        if (reason.Contains("impact", StringComparison.OrdinalIgnoreCase))
        {
            return LowImpact;
        }

        if (reason.Contains("direction", StringComparison.OrdinalIgnoreCase))
        {
            return NoClearDirection;
        }

        if (reason.Contains("relevance", StringComparison.OrdinalIgnoreCase))
        {
            return UnknownAsset;
        }

        return NoClearDirection;
    }
}

public static class NewsLatency
{
    public static long Milliseconds(DateTimeOffset from, DateTimeOffset to) =>
        (long)Math.Max(0, (to - from).TotalMilliseconds);

    /// <summary>A later stage stamped earlier than an earlier stage is look-ahead. Equal timestamps are allowed.</summary>
    public static bool HasLookAhead(
        DateTimeOffset published,
        DateTimeOffset detected,
        DateTimeOffset classified,
        DateTimeOffset decision,
        DateTimeOffset? order = null) =>
        detected < published
        || classified < detected
        || decision < classified
        || (order is { } at && at < decision);
}

public sealed class NewsAffectedAsset
{
    public string Symbol { get; init; } = string.Empty;
    public string Role { get; init; } = "PRIMARY";
    public string Direction { get; init; } = "UNCERTAIN";
    public int Impact { get; init; }
    public int Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record NewsDeepAnalysis
{
    public string Status { get; init; } = "Ok";
    public string Error { get; init; } = string.Empty;
    public string EventType { get; init; } = "Other";
    public NewsEventType ParsedType { get; init; } = NewsEventType.Other;
    public string VerificationStatus { get; init; } = string.Empty;
    public int SourceReliability { get; init; }
    public int OverallConfidence { get; init; }
    public int Impact { get; init; }
    public int Novelty { get; init; }
    public int AlreadyPricedIn { get; init; }
    public string ExpectedHorizon { get; init; } = string.Empty;
    public int ExpectedHorizonMinutes { get; init; }
    public string MarketMechanism { get; init; } = string.Empty;
    public IReadOnlyList<NewsAffectedAsset> AffectedAssets { get; init; } = [];
    public IReadOnlyList<string> RiskFlags { get; init; } = [];
    public bool ShouldConsiderTrading { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;
    public string RawJson { get; init; } = string.Empty;
    public bool UsedStrongModel { get; init; }

    public static NewsDeepAnalysis Failed(string status, string error, string provider, string model, string promptVersion) =>
        new()
        {
            Status = status,
            Error = error,
            Provider = provider,
            Model = model,
            PromptVersion = promptVersion
        };
}

public sealed record NewsDecisionContext(
    bool EventAlreadyTraded = false,
    bool SymbolOccupied = false,
    bool InCooldown = false,
    bool DuplicateEvent = false);

public static class NewsAnalysisPrompt
{
    public static (string System, string User) Build(NewsEvent item, NewsOptions options, IReadOnlyList<string> allowedSymbols)
    {
        var system = new StringBuilder();
        system.AppendLine("promptVersion: " + options.Ai.PromptVersion);
        system.AppendLine("You analyze crypto and financial news for a Binance USD-M futures desk.");
        system.AppendLine("Explain the event. Do not classify it from a word list and do not recommend an order.");
        system.AppendLine("Answer: what happened, why it happened, who is affected, why the market should care, how important it is, whether it is confirmed or a rumor, whether it is new, whether the article says the market already reacted, the expected reaction, and the expected time horizon.");
        system.AppendLine("Use only symbols from the allowed Binance USD-M list. Never invent a symbol.");
        system.AppendLine("Mark one primary asset when a single coin is clearly affected. Secondary assets need direct evidence. Market-wide effects are not orders.");
        system.AppendLine("Scores are integers from 0 to 100. alreadyPricedIn is high when the article says the move has already happened.");
        system.AppendLine("Return one JSON object and nothing else, with this shape:");
        system.AppendLine("""
            {"eventType":"","verificationStatus":"","sourceReliability":0,"overallConfidence":0,"impact":0,"novelty":0,"alreadyPricedIn":0,"expectedHorizon":"","marketMechanism":"","affectedAssets":[{"symbol":"","role":"PRIMARY|SECONDARY|MARKET_WIDE","direction":"LONG|SHORT|NEUTRAL|UNCERTAIN","impact":0,"confidence":0,"reason":""}],"riskFlags":[],"shouldConsiderTrading":false}
            """);
        system.AppendLine("eventType is one of: listing, delisting, hack, exploit, security incident, regulatory, etf, institutional adoption, partnership, acquisition, protocol upgrade, mainnet, token unlock, tokenomics, governance, funding, financing, exchange announcement, legal, macro, market structure, liquidation, bankruptcy, product launch, major integration, staking, chain outage, bridge incident, stablecoin event, other.");
        system.AppendLine("verificationStatus is confirmed, official, verified, rumor, speculative, or unverified.");
        system.AppendLine("Prefer official sources, Binance announcements, regulators, and project foundations over aggregators and social posts.");
        system.AppendLine("Do not include price, volume, open interest, or funding figures. You do not have a live market feed.");

        var user = new StringBuilder();
        user.AppendLine("Allowed Binance USD-M symbols:");
        user.AppendLine(allowedSymbols.Count == 0 ? "(none)" : string.Join(", ", allowedSymbols));
        user.AppendLine();
        user.AppendLine("Articles in this event, oldest first:");
        foreach (var article in item.OriginalArticles.OrderBy(article => article.PublishedAtUtc))
        {
            user.AppendLine("source: " + article.Source);
            user.AppendLine("provider: " + article.Provider);
            user.AppendLine("url: " + article.SourceUrl);
            user.AppendLine("publishedAt: " + article.PublishedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            user.AppendLine("title: " + article.Title);
            var body = string.IsNullOrWhiteSpace(article.Content) ? article.Summary : article.Content;
            user.AppendLine("body: " + body);
            user.AppendLine();
        }

        return (system.ToString(), user.ToString());
    }
}

public static class NewsAnalysisParser
{
    private static readonly Dictionary<string, NewsEventType> Types = new(StringComparer.Ordinal)
    {
        ["listing"] = NewsEventType.Listing,
        ["delisting"] = NewsEventType.Delisting,
        ["hack"] = NewsEventType.Hack,
        ["exploit"] = NewsEventType.Exploit,
        ["securityincident"] = NewsEventType.SecurityIncident,
        ["regulatory"] = NewsEventType.Regulation,
        ["regulation"] = NewsEventType.Regulation,
        ["etf"] = NewsEventType.Etf,
        ["institutionaladoption"] = NewsEventType.Adoption,
        ["adoption"] = NewsEventType.Adoption,
        ["partnership"] = NewsEventType.Partnership,
        ["acquisition"] = NewsEventType.Acquisition,
        ["protocolupgrade"] = NewsEventType.ProtocolUpgrade,
        ["upgrade"] = NewsEventType.ProtocolUpgrade,
        ["mainnet"] = NewsEventType.ProtocolUpgrade,
        ["tokenunlock"] = NewsEventType.TokenUnlock,
        ["unlock"] = NewsEventType.TokenUnlock,
        ["tokenomics"] = NewsEventType.Tokenomics,
        ["governance"] = NewsEventType.Governance,
        ["funding"] = NewsEventType.Funding,
        ["financing"] = NewsEventType.Financing,
        ["exchangeannouncement"] = NewsEventType.Exchange,
        ["exchange"] = NewsEventType.Exchange,
        ["legal"] = NewsEventType.Legal,
        ["macro"] = NewsEventType.Macro,
        ["marketstructure"] = NewsEventType.MarketStructure,
        ["liquidation"] = NewsEventType.Liquidation,
        ["bankruptcy"] = NewsEventType.Bankruptcy,
        ["productlaunch"] = NewsEventType.ProductLaunch,
        ["majorintegration"] = NewsEventType.Integration,
        ["integration"] = NewsEventType.Integration,
        ["staking"] = NewsEventType.Staking,
        ["chainoutage"] = NewsEventType.ChainOutage,
        ["bridgeincident"] = NewsEventType.BridgeIncident,
        ["stablecoinevent"] = NewsEventType.Stablecoin,
        ["stablecoin"] = NewsEventType.Stablecoin,
        ["other"] = NewsEventType.Other
    };

    public static bool TryParse(string? json, IReadOnlyCollection<string> allowedSymbols, out NewsDeepAnalysis analysis, out string error)
    {
        analysis = NewsDeepAnalysis.Failed("Invalid", "Classifier output was not valid JSON.", "", "", "");
        error = "Classifier output was not valid JSON.";
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        var text = StripFence(json);
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Classifier output was not a JSON object.";
                return false;
            }

            if (!root.TryGetProperty("eventType", out _) && root.TryGetProperty("analysis", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                root = nested;
            }

            if (!root.TryGetProperty("eventType", out var eventType) || eventType.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("verificationStatus", out var verification) || verification.ValueKind != JsonValueKind.String
                || !Score(root, "sourceReliability", out var reliability)
                || !Score(root, "overallConfidence", out var confidence)
                || !Score(root, "impact", out var impact)
                || !Score(root, "novelty", out var novelty)
                || !Score(root, "alreadyPricedIn", out var priced)
                || !root.TryGetProperty("expectedHorizon", out var horizon) || horizon.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("marketMechanism", out var mechanism) || mechanism.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("affectedAssets", out var assets) || assets.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("riskFlags", out var flags) || flags.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("shouldConsiderTrading", out var consider) || consider.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                error = "Classifier output missed required fields.";
                return false;
            }

            var typeName = eventType.GetString() ?? string.Empty;
            if (!Types.TryGetValue(Key(typeName), out var parsedType))
            {
                parsedType = NewsEventType.Other;
            }

            var minutes = ParseHorizon(horizon.GetString() ?? string.Empty);
            if (minutes <= 0)
            {
                error = "Classifier horizon was not recognized.";
                return false;
            }

            var allowed = new HashSet<string>(allowedSymbols, StringComparer.OrdinalIgnoreCase);
            var parsedAssets = new List<NewsAffectedAsset>();
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object
                    || !asset.TryGetProperty("symbol", out var symbolEl) || symbolEl.ValueKind != JsonValueKind.String
                    || !asset.TryGetProperty("role", out var roleEl) || roleEl.ValueKind != JsonValueKind.String
                    || !asset.TryGetProperty("direction", out var directionEl) || directionEl.ValueKind != JsonValueKind.String
                    || !Score(asset, "impact", out var assetImpact)
                    || !Score(asset, "confidence", out var assetConfidence)
                    || !asset.TryGetProperty("reason", out var reasonEl) || reasonEl.ValueKind != JsonValueKind.String)
                {
                    error = "An affected asset was incomplete.";
                    return false;
                }

                var role = (roleEl.GetString() ?? string.Empty).Trim().ToUpperInvariant();
                var direction = (directionEl.GetString() ?? string.Empty).Trim().ToUpperInvariant();
                if (role is not ("PRIMARY" or "SECONDARY" or "MARKET_WIDE") || direction is not ("LONG" or "SHORT" or "NEUTRAL" or "UNCERTAIN"))
                {
                    error = "An affected asset used an unknown role or direction.";
                    return false;
                }

                var resolved = Resolve(symbolEl.GetString() ?? string.Empty, allowed);
                if (string.IsNullOrEmpty(resolved))
                {
                    continue;
                }

                if (parsedAssets.Any(row => string.Equals(row.Symbol, resolved, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                parsedAssets.Add(new NewsAffectedAsset
                {
                    Symbol = resolved,
                    Role = role,
                    Direction = direction,
                    Impact = assetImpact,
                    Confidence = assetConfidence,
                    Reason = reasonEl.GetString() ?? string.Empty
                });
            }

            var riskFlags = new List<string>();
            foreach (var flag in flags.EnumerateArray())
            {
                if (flag.ValueKind != JsonValueKind.String)
                {
                    error = "Risk flags must be strings.";
                    return false;
                }

                var value = flag.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    riskFlags.Add(value);
                }
            }

            analysis = new NewsDeepAnalysis
            {
                Status = "Ok",
                Error = string.Empty,
                EventType = typeName,
                ParsedType = parsedType,
                VerificationStatus = (verification.GetString() ?? string.Empty).Trim().ToLowerInvariant(),
                SourceReliability = reliability,
                OverallConfidence = confidence,
                Impact = impact,
                Novelty = novelty,
                AlreadyPricedIn = priced,
                ExpectedHorizon = horizon.GetString() ?? string.Empty,
                ExpectedHorizonMinutes = minutes,
                MarketMechanism = mechanism.GetString() ?? string.Empty,
                AffectedAssets = parsedAssets,
                RiskFlags = riskFlags,
                ShouldConsiderTrading = consider.GetBoolean(),
                RawJson = text
            };
            error = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            error = "Classifier output was not valid JSON.";
            return false;
        }
    }

    public static string Resolve(string raw, IReadOnlySet<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(raw) || allowed.Count == 0)
        {
            return string.Empty;
        }

        var token = raw.Trim();
        var match = allowed.FirstOrDefault(symbol => string.Equals(symbol, token, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        var withQuote = token.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) ? token : token + "USDT";
        return allowed.FirstOrDefault(symbol => string.Equals(symbol, withQuote, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    public static int ParseHorizon(string text)
    {
        var value = text.Trim().ToLowerInvariant();
        if (value is "minutes" or "minute")
        {
            return 30;
        }

        if (value is "hours" or "hour")
        {
            return 240;
        }

        if (value is "days" or "day")
        {
            return 1440;
        }

        var digits = new string(value.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var count) || count <= 0)
        {
            return 0;
        }

        var unit = value[digits.Length..].Trim();
        if (unit is "m" or "min" or "mins" or "minute" or "minutes")
        {
            return (int)Math.Round(count);
        }

        if (unit is "h" or "hr" or "hrs" or "hour" or "hours")
        {
            return (int)Math.Round(count * 60);
        }

        if (unit is "d" or "day" or "days")
        {
            return (int)Math.Round(count * 1440);
        }

        return 0;
    }

    private static bool Score(JsonElement root, string name, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var number))
        {
            return false;
        }

        if (number is < 0 or > 100 || double.IsNaN(number))
        {
            return false;
        }

        value = (int)Math.Round(number);
        return true;
    }

    private static string Key(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    public static string StripFence(string json)
    {
        var text = json.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var first = text.IndexOf('\n');
        var last = text.LastIndexOf("```", StringComparison.Ordinal);
        if (first < 0 || last <= first)
        {
            return text;
        }

        return text[(first + 1)..last].Trim();
    }
}
