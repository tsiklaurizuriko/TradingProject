using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingPlatform.Application.Abstractions.Exchange;

namespace TradingPlatform.News;

/// <summary>
/// One model call judges the whole headline batch. The same batch is never sent again.
/// A failed call waits <see cref="RetryAfter"/> before one more try. A new headline is a new batch.
/// </summary>
public static class PumpRiseReview
{
    public const int MaxHeadlines = 12;

    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

    public static IReadOnlyList<PumpHeadline> ForModel(IReadOnlyList<PumpHeadline> headlines, string baseAsset, string symbol)
    {
        var named = new List<PumpHeadline>();
        var google = new List<PumpHeadline>();
        foreach (var item in headlines)
        {
            if (PumpCatalyst.Mentions(item.Title + " " + item.Body, baseAsset, symbol))
            {
                named.Add(item);
            }
            else if (item.Source.Equals("google-news", StringComparison.OrdinalIgnoreCase))
            {
                google.Add(item);
            }
        }

        return named.Concat(google).Take(MaxHeadlines).ToList();
    }

    public static string Fingerprint(IReadOnlyList<PumpHeadline> headlines)
    {
        var lines = headlines
            .Select(item => item.Source.Trim() + "|" + item.Title.Trim())
            .Where(line => line.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(line => line, StringComparer.OrdinalIgnoreCase);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public static bool ShouldAsk(PumpAskRecord? prior, string fingerprint, DateTimeOffset now)
    {
        if (prior is null || !string.Equals(prior.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return true;
        }

        if (prior.Succeeded)
        {
            return false;
        }

        return now - prior.AskedAt >= RetryAfter;
    }

    public static PumpNewsVerdict FromMemory(PumpAskRecord prior) =>
        prior.Succeeded
            ? prior.CatalystFound ? PumpNewsVerdict.Catalyst(prior.Reason) : PumpNewsVerdict.Clear(prior.Reason)
            : PumpNewsVerdict.Incomplete(prior.Reason);

    public static void Remember(List<PumpAskRecord> asks, PumpAskRecord next)
    {
        asks.RemoveAll(item => item.Symbol.Equals(next.Symbol, StringComparison.OrdinalIgnoreCase));
        asks.Add(next);
        if (asks.Count > 40)
        {
            asks.RemoveRange(0, asks.Count - 40);
        }
    }

    public static (string System, string User) Prompt(
        string symbol,
        string baseAsset,
        decimal rise,
        IReadOnlyList<PumpHeadline> headlines)
    {
        const string system = """
            You decide whether a large crypto price rise has a real public cause. Reply with one JSON object and nothing else.
            {"explainsRise": boolean, "reason": "one or two sentences"}
            explainsRise is true only when at least one item is real news about THIS coin that can explain the rise: an exchange listing of this coin, a partnership, a mainnet or product launch, a hack, an acquisition, or an official announcement.
            explainsRise is false when items only describe the price move, when this coin is named only to advertise another token or a presale, when "listing" refers to a different token, when the item is an unlock or vesting schedule, or when nothing is about this coin.
            """;
        var percent = (rise * 100m).ToString("0", CultureInfo.InvariantCulture);
        var user = new StringBuilder();
        user.Append("Coin: ").Append(baseAsset).Append(" (").Append(symbol).AppendLine(")");
        user.Append("Rise: ").Append(percent).AppendLine("% in the last 24 hours");
        user.AppendLine("Items:");
        var index = 1;
        foreach (var item in headlines)
        {
            user.Append(index.ToString(CultureInfo.InvariantCulture))
                .Append(". ")
                .Append(item.Source)
                .Append(" | ")
                .Append(item.PublishedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                .Append("Z | ")
                .AppendLine(item.Title.Trim());
            var body = item.Body.Trim();
            if (body.Length > 280)
            {
                body = body[..280];
            }

            if (!string.IsNullOrWhiteSpace(body) && !body.Equals(item.Title.Trim(), StringComparison.Ordinal))
            {
                user.AppendLine(body);
            }

            index++;
        }

        return (system, user.ToString());
    }

    public static bool TryParse(string content, out PumpNewsVerdict verdict)
    {
        verdict = PumpNewsVerdict.Incomplete("The model reply could not be read. No short.");
        var json = Extract(content);
        if (json is null)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryBool(doc.RootElement, "explainsRise", out var explains) || !TryString(doc.RootElement, "reason", out var reason))
            {
                return false;
            }

            if (reason.Length > 400)
            {
                reason = reason[..400];
            }

            verdict = explains ? PumpNewsVerdict.Catalyst(reason) : PumpNewsVerdict.Clear(reason);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Extract(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    private static bool TryBool(JsonElement root, string name, out bool value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.Value.GetBoolean();
                return true;
            }
        }

        value = false;
        return false;
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString()?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = string.Empty;
        return false;
    }
}

public readonly record struct PumpClosePnl(decimal Realized, DateTimeOffset ClosedAt);

public static class PumpRealizedPnl
{
    /// <summary>
    /// Realized PnL of the buy fills that close this short. Later trades on the same coin are not included.
    /// </summary>
    public static bool TryClose(IReadOnlyList<ExchangeFillPnl> fills, DateTimeOffset from, decimal quantity, out PumpClosePnl close)
    {
        close = default;
        if (quantity <= 0m)
        {
            return false;
        }

        var buys = fills
            .Where(fill => fill.Time >= from && fill.RealizedPnl != 0m && fill.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase))
            .OrderBy(fill => fill.Time)
            .ToList();
        decimal covered = 0m;
        decimal realized = 0m;
        var closedAt = from;
        foreach (var fill in buys)
        {
            covered += fill.Quantity;
            realized += fill.RealizedPnl;
            closedAt = fill.Time;
            if (covered >= quantity * 0.99m)
            {
                close = new PumpClosePnl(realized, closedAt);
                return true;
            }
        }

        return false;
    }

    public static bool TrySum(IReadOnlyList<ExchangeFillPnl> fills, DateTimeOffset from, DateTimeOffset until, out decimal realized)
    {
        var slice = fills.Where(fill => fill.Time >= from && fill.Time < until).ToList();
        if (!slice.Any(fill => fill.RealizedPnl != 0m))
        {
            realized = 0m;
            return false;
        }

        realized = slice.Sum(fill => fill.RealizedPnl);
        return true;
    }

    public static decimal EntryQuantity(IReadOnlyList<ExchangeFillPnl> fills, DateTimeOffset from)
    {
        var until = from.AddMinutes(3);
        return fills
            .Where(fill => fill.Time >= from && fill.Time < until && fill.Side.Equals("SELL", StringComparison.OrdinalIgnoreCase) && fill.RealizedPnl == 0m)
            .Sum(fill => fill.Quantity);
    }
}

public sealed class PumpAskRecord
{
    public string Symbol { get; set; } = string.Empty;

    public string Fingerprint { get; set; } = string.Empty;

    public DateTimeOffset AskedAt { get; set; }

    public bool Succeeded { get; set; }

    public bool CatalystFound { get; set; }

    public string Reason { get; set; } = string.Empty;
}
