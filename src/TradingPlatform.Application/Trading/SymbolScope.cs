namespace TradingPlatform.Application.Trading;

public static class SymbolScope
{
    public static IReadOnlyList<string> Parse(string? csv) =>
        (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(symbol => symbol.ToUpperInvariant())
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string? Join(IEnumerable<string>? symbols)
    {
        var list = Parse(string.Join(',', symbols ?? []));
        return list.Count == 0 ? null : string.Join(',', list);
    }

    public static bool Allows(bool appliesToAll, string? csv, string symbol)
    {
        if (appliesToAll)
        {
            return true;
        }

        var allowed = Parse(csv);
        return allowed.Contains(symbol.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
    }
}
