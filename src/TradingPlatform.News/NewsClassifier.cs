using System.Text.Json;

namespace TradingPlatform.News;

public interface INewsClassifier
{
    NewsEvent Classify(NewsEvent clustered);
}

public sealed class RuleNewsClassifier : INewsClassifier
{
    private static readonly (string[] Words, NewsEventType Type, EventDirection Direction, double Weight)[] Rules =
    [
        (["etf"], NewsEventType.Etf, EventDirection.Bullish, 0.9),
        (["sec", "securities and exchange"], NewsEventType.Sec, EventDirection.Unknown, 0.8),
        (["hack", "exploit", "stolen"], NewsEventType.Hack, EventDirection.Bearish, 0.9),
        (["security incident", "breach"], NewsEventType.SecurityIncident, EventDirection.Bearish, 0.85),
        (["listing"], NewsEventType.Listing, EventDirection.Bullish, 0.6),
        (["delisting"], NewsEventType.Delisting, EventDirection.Bearish, 0.7),
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

    private static readonly string[] BullishWords = ["approval", "approved", "surge", "inflow", "bullish", "rate cut", "beats"];
    private static readonly string[] BearishWords = ["hack", "ban", "lawsuit", "bankruptcy", "outflow", "bearish", "rate hike", "exploit", "stolen"];

    public NewsEvent Classify(NewsEvent clustered)
    {
        var text = string.Join(' ', clustered.OriginalArticles.Select(article => article.Title)).ToLowerInvariant();
        var matched = Rules.Where(rule => rule.Words.Any(word => text.Contains(word, StringComparison.Ordinal))).ToList();
        var bullish = BullishWords.Count(word => text.Contains(word, StringComparison.Ordinal));
        var bearish = BearishWords.Count(word => text.Contains(word, StringComparison.Ordinal));
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
        clustered.Reason = "Rule classification from title evidence.";
        return clustered;
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

        if (sources.Any(source => source.Contains("coindesk") || source.Contains("bloomberg") || source.Contains("the block")))
        {
            return 0.8;
        }

        return Math.Clamp(0.35 + (0.05 * item.SourceCount), 0.35, 0.7);
    }
}

public sealed class SchemaNewsClassifier : INewsClassifier
{
    public NewsEvent Classify(NewsEvent clustered) => clustered;

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
