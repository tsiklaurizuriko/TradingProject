using System.Text.Json.Nodes;

namespace TradingPlatform.Strategies.Engine;

public sealed record CanonicalStrategySnapshot(
    Guid Id,
    string TemplateKey,
    bool IsEnabled,
    bool IsArchived,
    IReadOnlyList<string> DefinitionJson);

public sealed record CanonicalStrategyUpdate(
    Guid Id,
    string TemplateKey,
    bool IsEnabled,
    bool IsArchived,
    IReadOnlyList<string> DefinitionJson);

public sealed record CanonicalMigrationPlan(
    IReadOnlyList<CanonicalStrategyUpdate> Updates,
    IReadOnlyList<string> Reviews,
    IReadOnlyList<string> Failures);

/// <summary>
/// Rewrites obsolete strategy ids to the one canonical id. Parameter numbers stay.
/// A duplicate alias row is disabled and kept. Trades are not part of this plan.
/// </summary>
public static class CanonicalStrategyMigration
{
    private static readonly HashSet<string> KnownParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "emaFast", "emaSlow", "rsiPeriod", "rsiMinimum", "rsiLongMax", "rsiOversold", "rsiOverbought",
        "macdFast", "macdSlow", "macdSignal", "bbPeriod", "bbStdDev", "donchianLength", "entryLookback",
        "exitLookback", "atrPeriod", "atrStopMultiplier", "trendEmaPeriod", "volumeFilterEnabled",
        "relativeVolumePeriod", "minimumRelativeVolume", "maxVwapDistanceAtr", "stopAtrMultiplier",
        "volatilityLookback", "compressionPercentile", "atrExpansionLookback", "breakoutRelativeVolume",
        "supertrendPeriod", "supertrendMultiplier", "adxPeriod", "minimumAdx", "oiLookback",
        "oiChangeThreshold", "priceChangeThreshold", "oiHypothesis", "fundingLookback",
        "fundingExtremePercentile", "fundingHypothesis", "zScoreEntry", "valueAreaPercent",
        "sweepDepthAtr", "swingLength", "useFuturesFilter", "priceDisplacementAtr",
        "oiExtremePercentile", "maxImpulseAgeBars"
    };

    public static CanonicalMigrationPlan Plan(IReadOnlyList<CanonicalStrategySnapshot> rows)
    {
        var updates = new List<CanonicalStrategyUpdate>();
        var reviews = new List<string>();
        var failures = new List<string>();
        var groups = rows.GroupBy(row => StrategyTemplateKeys.CanonicalId(row.TemplateKey), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var canonical = StrategyTemplateKeys.Normalize(group.Key);
            if (!StrategyTemplateKeys.IsCanonical(canonical))
            {
                continue;
            }

            var members = group.ToList();
            var canonicalRows = members.Where(row => !StrategyTemplateKeys.IsObsoleteAlias(row.TemplateKey)).ToList();
            var aliasRows = members.Where(row => StrategyTemplateKeys.IsObsoleteAlias(row.TemplateKey)).ToList();
            var enabledCanonical = canonicalRows.Where(row => row.IsEnabled).ToList();
            if (enabledCanonical.Count > 1)
            {
                failures.Add(
                    $"Two enabled strategies already use {canonical}: {string.Join(", ", enabledCanonical.Select(row => row.Id))}. Disable one manually. No row was deleted.");
                continue;
            }

            var keeper = enabledCanonical.FirstOrDefault()
                ?? aliasRows.FirstOrDefault(row => row.IsEnabled)
                ?? canonicalRows.FirstOrDefault()
                ?? aliasRows.FirstOrDefault();
            if (keeper is null)
            {
                continue;
            }

            foreach (var row in members)
            {
                var keep = row.Id == keeper.Id;
                var enabled = keep && row.IsEnabled;
                var archived = keep ? row.IsArchived : true;
                if (!keep)
                {
                    reviews.Add($"{row.Id}: duplicate of {canonical} was disabled. Its parameter numbers were not merged and its history was kept.");
                }

                var rewritten = RewriteDefinitions(row.Id, canonical, row.DefinitionJson, reviews, failures);
                if (rewritten.Failure)
                {
                    continue;
                }

                var keyChanged = !string.Equals(StrategyTemplateKeys.Normalize(row.TemplateKey), canonical, StringComparison.Ordinal);
                var jsonChanged = !rewritten.Json.SequenceEqual(row.DefinitionJson);
                var flagChanged = row.IsEnabled != enabled || row.IsArchived != archived;
                if (keyChanged || jsonChanged || flagChanged)
                {
                    updates.Add(new CanonicalStrategyUpdate(row.Id, canonical, enabled, archived, rewritten.Json));
                }
            }
        }

        return new CanonicalMigrationPlan(updates, reviews, failures);
    }

    public static void EnsureSafe(CanonicalMigrationPlan plan)
    {
        if (plan.Failures.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Canonical strategy migration cannot finish safely. Fix the rows below and restart. Nothing in this failed plan is applied."
            + Environment.NewLine
            + string.Join(Environment.NewLine, plan.Failures));
    }

    private static (IReadOnlyList<string> Json, bool Failure) RewriteDefinitions(
        Guid id,
        string canonical,
        IReadOnlyList<string> definitions,
        List<string> reviews,
        List<string> failures)
    {
        var json = new List<string>(definitions.Count);
        var failed = false;
        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition))
            {
                json.Add(definition);
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(definition);
            }
            catch (Exception ex)
            {
                failures.Add($"{id}: definition JSON could not be parsed ({ex.Message}). It was not changed.");
                failed = true;
                json.Add(definition);
                continue;
            }

            if (node is not JsonObject obj)
            {
                json.Add(definition);
                continue;
            }

            var templateChanged = false;
            if (obj["template"] is JsonValue templateValue && templateValue.TryGetValue<string>(out var template))
            {
                var next = StrategyTemplateKeys.CanonicalId(template);
                if (!string.Equals(next, template, StringComparison.OrdinalIgnoreCase) && StrategyTemplateKeys.IsCanonical(next))
                {
                    obj["template"] = next;
                    templateChanged = true;
                }

                var timeframe = obj["timeframe"] is JsonValue timeframeValue && timeframeValue.TryGetValue<string>(out var timeframeText)
                    ? timeframeText
                    : null;
                var expected = StrategyTemplateKeys.TimeframesFor(canonical);
                if (!string.IsNullOrWhiteSpace(timeframe)
                    && !expected.Contains(timeframe, StringComparer.OrdinalIgnoreCase))
                {
                    reviews.Add($"{id}: timeframe '{timeframe}' is not the canonical timeframe '{expected[0]}' for {canonical}. The stored timeframe was left unchanged.");
                }
            }

            if (obj["params"] is JsonObject parameters)
            {
                foreach (var property in parameters)
                {
                    if (!KnownParameters.Contains(property.Key))
                    {
                        reviews.Add($"{id}: parameter '{property.Key}' is not read by {canonical}. It was left unchanged and needs manual review.");
                    }
                }
            }

            json.Add(templateChanged ? obj.ToJsonString() : definition);
        }

        if (!failed && json.Count == definitions.Count)
        {
            var changed = false;
            for (var i = 0; i < json.Count; i++)
            {
                if (!string.Equals(json[i], definitions[i], StringComparison.Ordinal))
                {
                    changed = true;
                    break;
                }
            }

            if (!changed)
            {
                return (definitions, false);
            }
        }

        return (json, failed);
    }
}
