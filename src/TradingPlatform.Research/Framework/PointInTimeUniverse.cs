using System.Text.Json;

namespace TradingPlatform.Research.Framework;

public sealed record ListedContract(string Symbol, DateTimeOffset? OnboardDate, DateTimeOffset? DeliveryDate, string Status);

public sealed record UniverseBias(
    bool CurrentListingsOnly,
    int Eligible,
    int ExcludedNotYetListed,
    int ExcludedDelistedInWindow,
    string Note);

/// <summary>
/// Picks coins that were tradable at the start of a research window. Binance exchangeInfo lists only current
/// contracts, so coins that delisted before today are invisible unless an archive list is supplied.
/// </summary>
public static class PointInTimeUniverse
{
    public const int DefaultMinimumListingDays = 30;
    private static readonly DateTimeOffset PerpetualDelivery = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<ListedContract> ParseExchangeInfo(JsonElement info)
    {
        var rows = new List<ListedContract>();
        if (!info.TryGetProperty("symbols", out var symbols) || symbols.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var s in symbols.EnumerateArray())
        {
            var symbol = s.TryGetProperty("symbol", out var sym) ? sym.GetString() : null;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            if (s.TryGetProperty("contractType", out var ct) && ct.GetString() is { } type && type != "PERPETUAL")
            {
                continue;
            }

            if (s.TryGetProperty("quoteAsset", out var qa) && qa.GetString() is { } quote && quote != "USDT")
            {
                continue;
            }

            rows.Add(new ListedContract(
                symbol,
                Millis(s, "onboardDate"),
                Millis(s, "deliveryDate") is { } delivery && delivery < PerpetualDelivery ? delivery : null,
                s.TryGetProperty("status", out var st) ? st.GetString() ?? "" : ""));
        }

        return rows;
    }

    public static IReadOnlyList<string> EligibleAt(
        IEnumerable<ListedContract> contracts,
        DateTimeOffset windowStart,
        int minimumListingDays = DefaultMinimumListingDays) =>
        contracts
            .Where(c => c.OnboardDate is { } onboard && onboard <= windowStart.AddDays(-minimumListingDays))
            .Where(c => c.DeliveryDate is null || c.DeliveryDate > windowStart)
            .Select(c => c.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    public static UniverseBias Assess(
        IReadOnlyCollection<ListedContract> contracts,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<string>? delistedArchive = null,
        int minimumListingDays = DefaultMinimumListingDays)
    {
        var eligible = EligibleAt(contracts, windowStart, minimumListingDays);
        var notYet = contracts.Count(c => c.OnboardDate is null || c.OnboardDate > windowStart.AddDays(-minimumListingDays));
        var delistedInWindow = contracts.Count(c => c.DeliveryDate is { } d && d > windowStart && d <= windowEnd);
        var currentOnly = delistedArchive is null || delistedArchive.Count == 0;
        var note = currentOnly
            ? $"SURVIVORSHIP: universe built from current exchangeInfo only. Coins delisted before {DateTimeOffset.UtcNow:yyyy-MM-dd} are missing, which flatters long-side and breadth results."
            : $"Universe includes {delistedArchive!.Count} archived delisted coins.";
        return new UniverseBias(currentOnly, eligible.Count, notYet, delistedInWindow, note);
    }

    private static DateTimeOffset? Millis(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ms) && ms > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
}
