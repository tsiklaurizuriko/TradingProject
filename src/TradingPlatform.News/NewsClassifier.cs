using System.Text.Json;

namespace TradingPlatform.News;

public interface INewsClassifier
{
    NewsEvent Classify(NewsEvent clustered);
}

public static class NewsText
{
    public static string Excerpt(string? value)
    {
        var plain = StripHtml(value);
        return plain.Length <= 2000 ? plain : plain[..2000];
    }

    public static string Readable(NewsEvent item) =>
        StripHtml(string.Join('\n', item.OriginalArticles.Select(article => article.Title + "\n" + article.Summary)));

    public static string StripHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", " ");
        text = text.Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
            .Replace("&#39;", "'", StringComparison.Ordinal)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
    }
}

public sealed class RuleNewsClassifier : INewsClassifier
{
    private static readonly (string[] Words, NewsEventType Type, EventDirection Direction, double Weight)[] Rules =
    [
        (["etf"], NewsEventType.Etf, EventDirection.Bullish, 0.9),
        (["sec", "securities and exchange"], NewsEventType.Sec, EventDirection.Unknown, 0.8),
        (["hack", "exploit", "stolen", "drained"], NewsEventType.Hack, EventDirection.Bearish, 0.9),
        (["security incident", "breach", "outage", "suspends withdrawals", "withdrawal halt"], NewsEventType.SecurityIncident, EventDirection.Bearish, 0.9),
        (["listing"], NewsEventType.Listing, EventDirection.Bullish, 0.9),
        (["delisting"], NewsEventType.Delisting, EventDirection.Bearish, 0.9),
        (["trading halt", "halts trading", "halted trading"], NewsEventType.Regulation, EventDirection.Bearish, 0.85),
        (["depeg", "de-peg", "lost its peg"], NewsEventType.Stablecoin, EventDirection.Bearish, 0.9),
        (["partnership"], NewsEventType.Partnership, EventDirection.Bullish, 0.55),
        (["upgrade", "hard fork"], NewsEventType.ProtocolUpgrade, EventDirection.Unknown, 0.5),
        (["unlock"], NewsEventType.TokenUnlock, EventDirection.Bearish, 0.6),
        (["bankruptcy", "insolvent"], NewsEventType.Bankruptcy, EventDirection.Bearish, 0.9),
        (["lawsuit", "sues", "charged"], NewsEventType.Legal, EventDirection.Bearish, 0.7),
        (["acquisition", "acquires"], NewsEventType.Acquisition, EventDirection.Unknown, 0.55),
        (["fomc", "federal reserve", "fed "], NewsEventType.Fed, EventDirection.Unknown, 0.85),
        (["interest rate", "rate hike", "rate cut"], NewsEventType.InterestRate, EventDirection.Unknown, 0.8),
        (["cpi", "inflation"], NewsEventType.Inflation, EventDirection.Unknown, 0.8),
        (["ppi"], NewsEventType.Inflation, EventDirection.Unknown, 0.7),
        (["payroll", "employment", "nonfarm"], NewsEventType.Employment, EventDirection.Unknown, 0.75),
        (["regulation", "regulatory", "ban"], NewsEventType.Regulation, EventDirection.Bearish, 0.7),
        (["stablecoin"], NewsEventType.Stablecoin, EventDirection.Unknown, 0.6),
        (["adoption", "integrates"], NewsEventType.Adoption, EventDirection.Bullish, 0.55)
    ];

    private static readonly string[] BullishWords = ["approval", "approved", "surge", "inflow", "bullish", "rate cut", "beats", "rallies", "rally", "soars", "surges"];
    private static readonly string[] BearishWords = ["hack", "ban", "lawsuit", "bankruptcy", "outflow", "bearish", "rate hike", "exploit", "stolen", "rejected", "denied", "plunges", "plunge", "crashes", "crash", "tumbles"];
    private static readonly string[] RejectionWords = ["rejected", "denied", "not approved", "delayed", "postponed", "blocked"];

    public NewsEvent Classify(NewsEvent clustered)
    {
        var text = NewsText.Readable(clustered).ToLowerInvariant();
        var matched = Rules.Where(rule => rule.Words.Any(word => Has(text, word))).ToList();
        var bullish = BullishWords.Count(word => Has(text, word));
        var bearish = BearishWords.Count(word => Has(text, word));
        if (matched.Count == 0 && bullish == 0 && bearish == 0)
        {
            clustered.EventType = NewsEventType.Other;
            clustered.Direction = EventDirection.Unknown;
            clustered.Sentiment = 0;
            clustered.ImpactScore = 0;
            clustered.ConfidenceScore = 0.2;
            clustered.SourceQualityScore = SourceQuality(clustered);
            clustered.Reason = "No classification evidence.";
            return clustered;
        }

        var type = matched.Count == 0 ? NewsEventType.Other : matched.OrderByDescending(rule => rule.Weight).First().Type;
        var direction = DirectionOf(bullish, bearish, matched);
        if (direction == EventDirection.Bullish && RejectionWords.Any(word => Has(text, word)))
        {
            direction = EventDirection.Bearish;
        }
        var quality = SourceQuality(clustered);
        var weight = matched.Count == 0 ? 0.4 : matched.Max(rule => rule.Weight);
        clustered.EventType = type;
        clustered.Direction = direction;
        clustered.Sentiment = Math.Clamp((bullish - bearish) / 3d, -1, 1);
        clustered.ImpactScore = Math.Clamp(weight * quality, 0, 1);
        clustered.ConfidenceScore = Math.Clamp(0.5 + (0.1 * matched.Count) + (direction == EventDirection.Mixed ? -0.25 : 0.2), 0, 0.95);
        clustered.SourceQualityScore = quality;
        clustered.ExpectedHorizon = type is NewsEventType.Hack or NewsEventType.SecurityIncident
            ? ExpectedHorizon.Hours
            : type is NewsEventType.Fed or NewsEventType.Inflation or NewsEventType.Employment or NewsEventType.Macro
                ? ExpectedHorizon.Days
                : ExpectedHorizon.Hours;
        clustered.ExpectedHorizonMinutes = clustered.ExpectedHorizon == ExpectedHorizon.Days ? 1440 : 240;
        clustered.Reason = "Rule classification from the article text.";
        return clustered;
    }

    private static bool Has(string text, string term)
    {
        var index = 0;
        while ((index = text.IndexOf(term, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 ? ' ' : text[index - 1];
            var afterIndex = index + term.Length;
            var after = afterIndex >= text.Length ? ' ' : text[afterIndex];
            if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after) && !Negated(text, index))
            {
                return true;
            }

            index += Math.Max(1, term.Length);
        }

        return false;
    }

    private static bool Negated(string text, int index)
    {
        var start = Math.Max(0, index - 24);
        var window = text[start..index];
        return window.Contains("not ", StringComparison.Ordinal)
            || window.Contains("no ", StringComparison.Ordinal)
            || window.Contains("n't ", StringComparison.Ordinal)
            || window.Contains("without ", StringComparison.Ordinal)
            || window.Contains("never ", StringComparison.Ordinal);
    }

    private static EventDirection DirectionOf(
        int bullish,
        int bearish,
        List<(string[] Words, NewsEventType Type, EventDirection Direction, double Weight)> matched)
    {
        if (bullish > 0 && bearish > 0)
        {
            return EventDirection.Mixed;
        }

        if (bullish > bearish)
        {
            return EventDirection.Bullish;
        }

        if (bearish > bullish)
        {
            return EventDirection.Bearish;
        }

        var hinted = matched.Select(rule => rule.Direction).Where(direction => direction is EventDirection.Bullish or EventDirection.Bearish).Distinct().ToList();
        if (hinted.Count > 1)
        {
            return EventDirection.Mixed;
        }

        return hinted.Count == 1 ? hinted[0] : EventDirection.Neutral;
    }

    public static double SourceQuality(NewsEvent item)
    {
        var sources = item.OriginalArticles.Select(article => article.Source.ToLowerInvariant()).ToList();
        if (sources.Any(source => source.Contains("fred") || source.Contains("federalreserve") || source.Contains("sec.gov")))
        {
            return 0.95;
        }

        if (sources.Any(source => source.Contains("coindesk")
            || source.Contains("bloomberg")
            || source.Contains("the block")
            || source.Contains("cointelegraph")
            || source.Contains("decrypt")
            || source.Contains("dl news")
            || source.Contains("bitcoin magazine")
            || source.Contains("blockworks")
            || source.Contains("defiant")
            || source.Contains("unchained")
            || source.Contains("protos")
            || source.Contains("crypto briefing")
            || source.Contains("a16z")
            || source.Contains("binance")
            || source.Contains("ethereum")
            || source.Contains("solana")
            || source.Contains("blockstream")
            || source.Contains("bitcoin core")
            || source.Contains("bitcoin.org")))
        {
            return 0.85;
        }

        return Math.Clamp(0.35 + (0.05 * item.SourceCount), 0.35, 0.7);
    }
}

public sealed class SchemaNewsClassifier : INewsClassifier
{
    private readonly RuleNewsClassifier _rules = new();

    /// <summary>
    /// No model client produces schema JSON yet, so this classifies with the rules instead of leaving every event
    /// unscored. Model output goes through <see cref="TryApply"/> once a client exists.
    /// </summary>
    public NewsEvent Classify(NewsEvent clustered)
    {
        _rules.Classify(clustered);
        clustered.Reason = "AI classification has no model client; " + clustered.Reason;
        return clustered;
    }

    public static bool TryApply(string json, NewsEvent clustered, out string error)
    {
        error = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("action", out _)
                || root.TryGetProperty("signal", out _)
                || root.TryGetProperty("side", out _))
            {
                error = "Classifier output included a trade instruction.";
                MarkUnknown(clustered);
                return false;
            }

            if (!root.TryGetProperty("eventType", out _)
                || !root.TryGetProperty("direction", out _)
                || !root.TryGetProperty("sentiment", out var sentiment)
                || !root.TryGetProperty("impact", out var impact)
                || !root.TryGetProperty("confidence", out var confidence)
                || !root.TryGetProperty("novelty", out var novelty))
            {
                error = "Classifier output missed required fields.";
                MarkUnknown(clustered);
                return false;
            }

            if (!InUnit(sentiment) || !InUnit(impact) || !InUnit(confidence) || !InUnit(novelty))
            {
                error = "Classifier scores were outside the allowed range.";
                MarkUnknown(clustered);
                return false;
            }

            if (!Enum.TryParse<NewsEventType>(root.GetProperty("eventType").GetString(), ignoreCase: true, out var type)
                || !Enum.TryParse<EventDirection>(root.GetProperty("direction").GetString(), ignoreCase: true, out var direction))
            {
                error = "Classifier enums were not recognized.";
                MarkUnknown(clustered);
                return false;
            }

            clustered.EventType = type;
            clustered.Direction = direction;
            clustered.Sentiment = sentiment.GetDouble();
            clustered.ImpactScore = impact.GetDouble();
            clustered.ConfidenceScore = confidence.GetDouble();
            clustered.NoveltyScore = novelty.GetDouble();
            clustered.Reason = root.TryGetProperty("reason", out var reason) ? reason.GetString() ?? string.Empty : string.Empty;
            if (root.TryGetProperty("expectedHorizonMinutes", out var horizon) && horizon.TryGetInt32(out var minutes) && minutes > 0)
            {
                clustered.ExpectedHorizonMinutes = minutes;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Classifier output was not valid JSON.";
            MarkUnknown(clustered);
            return false;
        }
    }

    private static bool InUnit(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.GetDouble() is >= -1 and <= 1;

    private static void MarkUnknown(NewsEvent clustered)
    {
        clustered.Direction = EventDirection.Unknown;
        clustered.EventType = NewsEventType.Other;
        clustered.Sentiment = 0;
        clustered.ImpactScore = 0;
        clustered.ConfidenceScore = 0;
        clustered.Reason = "Classification rejected.";
    }
}
