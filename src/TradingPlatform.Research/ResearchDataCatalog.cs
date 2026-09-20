using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Research;

public static class ResearchDatasets
{
    public const string Version = "futures-history-v1";
    public const string Ohlcv = "OHLCV";
    public const string TakerFlow = "TakerFlow";
    public const string Funding = "Funding";
    public const string OpenInterest = "OpenInterest";
    public const string MarkPrice = "MarkPrice";
    public const string IndexPrice = "IndexPrice";
    public const string Basis = "Basis";
    public const string PairUniverse = "CausalPairUniverse";
    public const string CrossSection = "CrossSectionUniverse";
    public const string CompletedHtf = "CompletedHtf";
}

public static class ResearchDataCatalog
{
    public static IReadOnlyList<string> Required(string templateKey) =>
        StrategyTemplateKeys.RequiredDatasets(templateKey);

    public static string SkipStatus(string templateKey, ResearchDataSnapshot snapshot)
    {
        if (string.Equals(templateKey, StrategyTemplateKeys.RegimeStrategyRouter, StringComparison.OrdinalIgnoreCase))
        {
            return ResearchStatuses.Researching;
        }

        foreach (var dataset in Required(templateKey))
        {
            if (!snapshot.Has(dataset))
            {
                return dataset is ResearchDatasets.PairUniverse or ResearchDatasets.CrossSection
                    ? ResearchStatuses.DataUnavailable
                    : ResearchStatuses.DataUnavailable;
            }

            if (snapshot.IsInsufficient(dataset))
            {
                return ResearchStatuses.InsufficientData;
            }
        }

        return ResearchStatuses.Researching;
    }
}

public sealed class ResearchDataSnapshot
{
    private readonly HashSet<string> _present;
    private readonly HashSet<string> _insufficient;

    public ResearchDataSnapshot(IEnumerable<string>? present = null, IEnumerable<string>? insufficient = null)
    {
        _present = new HashSet<string>(present ?? [], StringComparer.OrdinalIgnoreCase);
        _insufficient = new HashSet<string>(insufficient ?? [], StringComparer.OrdinalIgnoreCase);
        _present.Add(ResearchDatasets.Ohlcv);
    }

    public bool Has(string dataset) => _present.Contains(dataset);

    public bool IsInsufficient(string dataset) => _insufficient.Contains(dataset);
}
