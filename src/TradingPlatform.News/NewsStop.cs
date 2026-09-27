using System.Globalization;
using TradingPlatform.Domain.News;

namespace TradingPlatform.News;

public static class NewsStop
{
    public static string Explain(NewsTradingSignal signal) =>
        Explain(signal.Direction, signal.Reason, signal.RiskReason, signal.RiskDecision, signal.OrderDecision, signal.BecameTrade);

    public static string Explain(string direction, string? reason, string? riskReason, string? riskDecision, string? orderDecision, bool becameTrade)
    {
        if (direction is "LONG" or "SHORT")
        {
            if (becameTrade)
            {
                return "Order sent.";
            }

            if (riskDecision == "Rejected" || orderDecision == "REJECTED")
            {
                return Prefix("Stopped at risk: ", riskReason, orderDecision);
            }

            if (orderDecision == "NOT_RUNNING")
            {
                return Prefix("Stopped before the order: ", riskReason, "news trading is not running.");
            }

            if (orderDecision == "READY")
            {
                return "Passed every gate. Waiting for the order to be sent.";
            }

            return Prefix("Stopped at send: ", riskReason, orderDecision);
        }

        return Brief(reason, riskReason);
    }

    public static string Pending(StoredNewsEvent? matched, NewsMarketStrategyOptions strategy, DateTimeOffset now)
    {
        if (matched is null)
        {
            return "Stopped before scoring: the article was not matched to an event.";
        }

        if (string.Equals(matched.MarketScope, "Global", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(matched.PrimaryAsset))
        {
            return "Stopped at coin: no coin was mentioned, so it was not scored.";
        }

        if (matched.Direction is not ("Bullish" or "Bearish"))
        {
            return "Stopped at direction: " + (string.IsNullOrWhiteSpace(matched.Direction) ? "Unknown" : matched.Direction) + " is ignored.";
        }

        if (matched.Confidence < strategy.MinNewsConfidence)
        {
            return "Stopped at confidence: " + matched.Confidence.ToString("0.00", CultureInfo.InvariantCulture) + " is below " + strategy.MinNewsConfidence.ToString("0.00", CultureInfo.InvariantCulture) + ".";
        }

        if (matched.Impact < strategy.MinNewsImpact)
        {
            return "Stopped at impact: " + matched.Impact.ToString("0.00", CultureInfo.InvariantCulture) + " is below " + strategy.MinNewsImpact.ToString("0.00", CultureInfo.InvariantCulture) + ".";
        }

        var age = (now - matched.PublishedAtUtc).TotalMinutes;
        if (age > strategy.MaxNewsAgeMinutes)
        {
            return "Stopped at age: " + age.ToString("0", CultureInfo.InvariantCulture) + "m exceeds " + strategy.MaxNewsAgeMinutes.ToString(CultureInfo.InvariantCulture) + "m.";
        }

        return "Stopped before the market check: no trading decision was stored yet.";
    }

    public static string Brief(string? reason, string? riskReason = null)
    {
        var text = Collapse(reason);
        if (IsGeneric(text))
        {
            text = Collapse(riskReason);
        }

        if (string.IsNullOrWhiteSpace(text) || IsGeneric(text))
        {
            return "No trade.";
        }

        return text.Length <= 500 ? text : text[..500];
    }

    private static string Prefix(string label, string? reason, string? fallback)
    {
        var text = Collapse(reason);
        if (IsGeneric(text))
        {
            text = Collapse(fallback);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return label.Trim();
        }

        return text.StartsWith("Stopped", StringComparison.Ordinal) ? text : label + text;
    }

    private static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = value.Replace("\r", "", StringComparison.Ordinal);
        const string marker = "Reason:";
        var at = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (at >= 0)
        {
            text = text[(at + marker.Length)..];
        }

        text = text.Replace('\n', ' ').Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }

        return text;
    }

    private static bool IsGeneric(string text) =>
        string.IsNullOrWhiteSpace(text) || text is "No trade." or "News trading is not running.";
}
