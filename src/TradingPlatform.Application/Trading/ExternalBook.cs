namespace TradingPlatform.Application.Trading;

/// <summary>
/// News and pump-short orders are not a bot's position. A bot must not adopt, protect, or close them.
/// </summary>
public static class ExternalBook
{
    public const string StandDown =
        "This coin is open from news or pump shorts. This bot will not take it.";

    public readonly record struct OutsideMark(string Symbol, DateTimeOffset At, bool BotEntry);

    public static bool IsNewsOrPump(string? clientOrderId) =>
        !string.IsNullOrWhiteSpace(clientOrderId)
        && (clientOrderId.StartsWith("news-", StringComparison.OrdinalIgnoreCase)
            || clientOrderId.StartsWith("pump-", StringComparison.OrdinalIgnoreCase));

    public static void Note(
        ICollection<OutsideMark> marks,
        string symbol,
        string? clientOrderId,
        string? type,
        string? status,
        DateTimeOffset at)
    {
        if (IsNewsOrPump(clientOrderId))
        {
            marks.Add(new OutsideMark(symbol, at, false));
            return;
        }

        if (clientOrderId is not null
            && clientOrderId.StartsWith("L", StringComparison.Ordinal)
            && string.Equals(type, "MARKET", StringComparison.OrdinalIgnoreCase)
            && string.Equals(status, "FILLED", StringComparison.OrdinalIgnoreCase))
        {
            marks.Add(new OutsideMark(symbol, at, true));
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
        foreach (var group in marks.GroupBy(mark => mark.Symbol.Trim().ToUpperInvariant()))
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
    /// The trip stays out of the bot book when a news or pump order falls anywhere from the open to the close.
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
}
