using TradingPlatform.Application.Trading;

namespace TradingPlatform.Strategies.Engine;

public static class ProductionNewGate
{
    public static string? EntryBlock(ProductionNewOptions? options, string? template, bool liveMode)
    {
        if (!StrategyTemplateKeys.IsProductionNew(template))
        {
            return null;
        }

        var id = StrategyTemplateKeys.CanonicalId(template);
        var settings = options ?? new ProductionNewOptions();
        var enabled = settings.Strategies.TryGetValue(id, out var on) && on;
        if (!enabled)
        {
            return $"NO_TRADE: {id} is disabled. Set Trading:ProductionNew:Strategies:{id} to true only after review. The strategy is not validated.";
        }

        if (liveMode && !settings.LiveEnabled)
        {
            return "NO_TRADE: Trading:ProductionNew:LiveEnabled is false. Live entries stay off.";
        }

        if (!liveMode && !settings.PaperEnabled)
        {
            return "NO_TRADE: Trading:ProductionNew:PaperEnabled is false. Shadow and testnet entries stay off.";
        }

        return null;
    }
}
