namespace TradingPlatform.Domain.Trading;

/// <summary>
/// Whether a commission figure can be shown and summed.
/// Unknown is the default for rows written before this flag existed, so a stored 0 is not a certified zero.
/// </summary>
public enum FeeKnowledge
{
    Unknown = 0,
    Known = 1,
    AssetMissing = 2,
    Uncertain = 3
}

/// <summary>
/// One commission amount in one asset, or an explicit statement that the amount cannot be certified.
/// Amounts in different assets are never added into one number.
/// </summary>
public readonly record struct FeeBook(decimal? Amount, string? Asset, FeeKnowledge Status)
{
    public static FeeBook Unknown() => new(null, null, FeeKnowledge.Unknown);

    public static FeeBook Uncertain() => new(null, null, FeeKnowledge.Uncertain);

    public static FeeBook Known(decimal amount, string asset)
    {
        if (amount < 0m || string.IsNullOrWhiteSpace(asset))
        {
            return Uncertain();
        }

        return new FeeBook(amount, asset.Trim().ToUpperInvariant(), FeeKnowledge.Known);
    }

    public static FeeBook AssetMissing(decimal amount) =>
        amount < 0m
            ? Uncertain()
            : new FeeBook(amount, null, FeeKnowledge.AssetMissing);

    public static FeeBook FromReport(decimal? amount, string? asset)
    {
        if (amount is null)
        {
            return Unknown();
        }

        if (amount < 0m)
        {
            return Uncertain();
        }

        return string.IsNullOrWhiteSpace(asset)
            ? AssetMissing(amount.Value)
            : Known(amount.Value, asset);
    }

    /// <summary>
    /// Reads a stored row. <see cref="FeeKnowledge.Unknown"/> does not turn the stored decimal into a certified zero or a certified amount.
    /// </summary>
    public static FeeBook FromStored(FeeKnowledge status, decimal storedAmount, string? asset) =>
        status switch
        {
            FeeKnowledge.Known when !string.IsNullOrWhiteSpace(asset) => Known(storedAmount, asset),
            FeeKnowledge.AssetMissing => AssetMissing(storedAmount),
            FeeKnowledge.Uncertain => Uncertain(),
            _ => Unknown()
        };

    public decimal? DisplayAmount => Status == FeeKnowledge.Known ? Amount ?? 0m : null;

    public static FeeBook Combine(IEnumerable<FeeBook> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0)
        {
            return Unknown();
        }

        if (list.Any(line => line.Status == FeeKnowledge.Uncertain))
        {
            return Uncertain();
        }

        var known = list.Where(line => line.Status == FeeKnowledge.Known).ToList();
        var missing = list.Where(line => line.Status == FeeKnowledge.AssetMissing).ToList();
        var unknown = list.Where(line => line.Status == FeeKnowledge.Unknown).ToList();
        if (unknown.Count == list.Count)
        {
            return Unknown();
        }

        if (unknown.Count > 0 || (known.Count > 0 && missing.Count > 0))
        {
            return Uncertain();
        }

        if (known.Count > 0)
        {
            var assets = known
                .Select(line => line.Asset ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (assets.Count != 1 || string.IsNullOrWhiteSpace(assets[0]))
            {
                return Uncertain();
            }

            return Known(known.Sum(line => line.Amount ?? 0m), assets[0]);
        }

        return AssetMissing(missing.Sum(line => line.Amount ?? 0m));
    }

    public static FeeBook Combine(FeeBook left, FeeBook right) => Combine([left, right]);
}
