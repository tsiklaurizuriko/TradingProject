using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Research;

public static class Wave5Catalog
{
    public static IReadOnlyList<ResearchCandidate> RouterUniverse()
    {
        var tf = StrategyTemplateKeys.SupportedTimeframes;
        var sides = StrategyTemplateKeys.SupportedDirections;
        var rows = new List<ResearchCandidate>();
        foreach (var key in StrategyTemplateKeys.Frozen)
        {
            rows.Add(Catalog($"FROZEN-{key}", key, "Unmodified frozen catalog template.", tf, sides));
        }

        rows.AddRange(ResearchRegistry.All);
        rows.AddRange(ResearchRegistry.Wave2);
        foreach (var key in StrategyTemplateKeys.All)
        {
            if (StrategyTemplateKeys.IsFrozen(key)
                || StrategyTemplateKeys.IsHistoricallyFitted(key)
                || string.Equals(key, StrategyTemplateKeys.RegimeStrategyRouter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var required = StrategyTemplateKeys.RequiredDatasets(key);
            if (required.Count != 1 || !string.Equals(required[0], "OHLCV", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(Catalog($"ALPHA-{key}", key, "Existing advanced/alpha template, OHLCV-only, unmodified.", tf, sides));
        }

        return rows;
    }

    public static string Family(ResearchCandidate candidate)
    {
        if (candidate.CandidateId.StartsWith("W2-", StringComparison.OrdinalIgnoreCase))
        {
            return "WAVE2";
        }

        var key = candidate.ParentTemplateKey ?? candidate.NativeKey ?? candidate.ParentStrategyId;
        return StrategyTemplateKeys.Family(key);
    }

    private static ResearchCandidate Catalog(
        string id,
        string templateKey,
        string hypothesis,
        IReadOnlyList<string> tf,
        IReadOnlyList<string> sides) =>
        new(
            id,
            templateKey,
            1,
            hypothesis,
            ResearchKinds.ParentFilter,
            templateKey,
            "",
            [templateKey],
            "Existing catalog entry. No new logic.",
            "Isolated book SL 2% / TP 4%.",
            new ResearchFilters(),
            new ResearchNativeParams(),
            tf,
            sides,
            [],
            ResearchRegistry.CreatedAt,
            "wave5 router: existing signals only",
            ResearchStatuses.Researching);
}
