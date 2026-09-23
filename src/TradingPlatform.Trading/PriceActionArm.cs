using TradingPlatform.Application.Trading;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Trading;

public static class PriceActionArm
{
    public const string EnabledKey = "Trading.PriceAction.Enabled";
    public const string PaperKey = "Trading.PriceAction.PaperEnabled";
    public const string LiveKey = "Trading.PriceAction.LiveEnabled";

    public static string CandidateKey(string template) =>
        "Trading.PriceAction.Candidate." + StrategyTemplateKeys.Normalize(template);

    public static string? RejectLive(bool globalLive, bool? requestedLive) =>
        requestedLive == true && !globalLive
            ? "Global LIVE trading is off. Near-miss LIVE was rejected."
            : null;

    public static void Apply(PriceActionOptions price, IReadOnlyDictionary<string, string> settings)
    {
        if (TryBool(settings, EnabledKey, out var enabled))
        {
            price.Enabled = enabled;
        }

        if (TryBool(settings, PaperKey, out var paper))
        {
            price.PaperEnabled = paper;
        }

        if (TryBool(settings, LiveKey, out var live))
        {
            price.LiveEnabled = live;
        }

        price.Candidates ??= new Dictionary<string, NearMissCandidateOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in StrategyTemplateKeys.NearMiss)
        {
            if (!TryBool(settings, CandidateKey(key), out var on))
            {
                continue;
            }

            price.Candidates[key] = new NearMissCandidateOptions { Enabled = on };
        }
    }

    private static bool TryBool(IReadOnlyDictionary<string, string> settings, string key, out bool value)
    {
        value = false;
        if (!settings.TryGetValue(key, out var raw))
        {
            return false;
        }

        if (bool.TryParse(raw, out value))
        {
            return true;
        }

        if (raw == "1")
        {
            value = true;
            return true;
        }

        if (raw == "0")
        {
            value = false;
            return true;
        }

        return false;
    }
}
