namespace TradingPlatform.Application.Trading;

/// <summary>
/// News and pump-short orders are not a bot's position. A bot must not adopt, protect, or close them.
/// </summary>
public static class ExternalBook
{
    public const string StandDown =
        "This coin is open from news or pump shorts. This bot will not take it.";

    /// <summary>
    /// <paramref name="Manual"/> marks a fill placed by hand in the Binance app or web. It keeps a round trip out of
    /// every bot book, but does not make a bot stand down on the coin.
    /// </summary>
    public readonly record struct OutsideMark(
        string Symbol,
        DateTimeOffset At,
        bool BotEntry,
        string? ClientOrderId = null,
        string? Side = null,
        bool Manual = false);

    private static readonly string[] ManualPrefixes = ["ios_", "android_", "web_", "electron_"];

    public static bool IsNewsOrPump(string? clientOrderId) =>
        !string.IsNullOrWhiteSpace(clientOrderId)
        && (clientOrderId.StartsWith("news-", StringComparison.OrdinalIgnoreCase)
            || clientOrderId.StartsWith("pump-", StringComparison.OrdinalIgnoreCase));

    public static bool IsManual(string? clientOrderId) =>
        !string.IsNullOrWhiteSpace(clientOrderId)
        && ManualPrefixes.Any(prefix => clientOrderId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Live bot orders are <c>L</c> plus the first 11 hex digits of the bot id, then the candle time.</summary>
    public static string BotOrderPrefix(Guid botId) => $"L{botId:N}"[..12];

    public static bool IsOrderOfBot(string? clientOrderId, Guid botId) =>
        !string.IsNullOrWhiteSpace(clientOrderId)
        && clientOrderId.StartsWith(BotOrderPrefix(botId), StringComparison.OrdinalIgnoreCase);

    public static void Note(
        ICollection<OutsideMark> marks,
        string symbol,
        string? clientOrderId,
        string? type,
        string? status,
        DateTimeOffset at,
        string? side = null)
    {
        if (IsNewsOrPump(clientOrderId))
        {
            marks.Add(new OutsideMark(symbol, at, false, clientOrderId, side));
            return;
        }

        var filled = string.Equals(status, "FILLED", StringComparison.OrdinalIgnoreCase);
        var market = string.Equals(type, "MARKET", StringComparison.OrdinalIgnoreCase);
        if (filled && IsManual(clientOrderId) && (market || string.Equals(type, "LIMIT", StringComparison.OrdinalIgnoreCase)))
        {
            marks.Add(new OutsideMark(symbol, at, false, clientOrderId, side, Manual: true));
            return;
        }

        if (clientOrderId is not null
            && clientOrderId.StartsWith("L", StringComparison.Ordinal)
            && market
            && filled)
        {
            marks.Add(new OutsideMark(symbol, at, true, clientOrderId, side));
        }
    }

    /// <summary>
    /// Open coins whose latest relevant order is a news or pump-short order.
    /// A newer filled bot entry (client id starting with L) keeps the coin with that bot.
    /// </summary>
    public static IReadOnlySet<string> HeldSymbols(IEnumerable<string> openSymbols, IEnumerable<OutsideMark> marks)
    {
        var open = openSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in marks.Where(mark => !mark.Manual).GroupBy(mark => mark.Symbol.Trim().ToUpperInvariant()))
        {
            if (!open.Contains(group.Key))
            {
                continue;
            }

            DateTimeOffset? bot = null;
            DateTimeOffset? external = null;
            foreach (var mark in group)
            {
                if (mark.BotEntry)
                {
                    if (bot is null || mark.At > bot)
                    {
                        bot = mark.At;
                    }
                }
                else if (external is null || mark.At > external)
                {
                    external = mark.At;
                }
            }

            if (external is { } news && (bot is null || news >= bot))
            {
                held.Add(group.Key);
            }
        }

        return held;
    }

    /// <summary>A round trip that opened with a news or pump order is not that bot's trade.</summary>
    public static bool TripBelongsOutside(DateTimeOffset openedAt, IEnumerable<OutsideMark> marks) =>
        TripBelongsOutside(openedAt, openedAt, marks);

    /// <summary>
    /// The trip stays out of the bot book when a news, pump, or manual fill falls anywhere from the open to the close.
    /// A bot entry counts only next to the open, so a later bot fill cannot adopt the pump short.
    /// </summary>
    public static bool TripBelongsOutside(DateTimeOffset openedAt, DateTimeOffset closedAt, IEnumerable<OutsideMark> marks)
    {
        if (closedAt < openedAt)
        {
            closedAt = openedAt;
        }

        var window = TimeSpan.FromMinutes(5);
        var external = false;
        var bot = false;
        foreach (var mark in marks)
        {
            if (mark.BotEntry)
            {
                if ((mark.At - openedAt).Duration() <= window)
                {
                    bot = true;
                }

                continue;
            }

            if (mark.At >= openedAt - window && mark.At <= closedAt + window)
            {
                external = true;
            }
        }

        return external && !bot;
    }

    /// <summary>
    /// The bot order that opened the trip: a filled bot market order on the entry side within 15 minutes of the open.
    /// Bot exits use the same id shape, so the side must match. Null means no bot can be proven to own the trip.
    /// </summary>
    public static string? EntryOrderId(DateTimeOffset openedAt, string entrySide, IEnumerable<OutsideMark> marks)
    {
        var window = TimeSpan.FromMinutes(15);
        return marks
            .Where(mark =>
                mark.BotEntry
                && mark.ClientOrderId is not null
                && string.Equals(mark.Side, entrySide, StringComparison.OrdinalIgnoreCase)
                && (mark.At - openedAt).Duration() <= window)
            .OrderBy(mark => (mark.At - openedAt).Duration())
            .Select(mark => mark.ClientOrderId)
            .FirstOrDefault();
    }

    /// <summary>
    /// For a realized-PnL row with no fill: the latest order on the coin at or before the close must be a bot order.
    /// </summary>
    public static string? LatestBotOrderId(DateTimeOffset closedAt, IEnumerable<OutsideMark> marks)
    {
        var latest = marks
            .Where(mark => mark.At <= closedAt + TimeSpan.FromMinutes(5))
            .OrderByDescending(mark => mark.At)
            .FirstOrDefault();
        return latest.BotEntry ? latest.ClientOrderId : null;
    }
}
