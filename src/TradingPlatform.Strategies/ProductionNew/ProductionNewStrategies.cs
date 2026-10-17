using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

public static class TrendFollowingNewStrategy
{
    public static StrategySignalDetail Evaluate(StrategyTemplateParams parameters, IReadOnlyList<MarketCandle> candles, int index, StrategyContext context, CausalIndicatorCache cache) =>
        ProductionNewEvaluator.Evaluate(parameters with { TemplateKey = StrategyTemplateKeys.TrendFollowingNew }, candles, index, context, cache);
}

public static class BreakoutVolumeNewStrategy
{
    public static StrategySignalDetail Evaluate(StrategyTemplateParams parameters, IReadOnlyList<MarketCandle> candles, int index, StrategyContext context, CausalIndicatorCache cache) =>
        ProductionNewEvaluator.Evaluate(parameters with { TemplateKey = StrategyTemplateKeys.BreakoutVolumeNew }, candles, index, context, cache);
}

public static class MarketRegimeNewStrategy
{
    public static StrategySignalDetail Evaluate(StrategyTemplateParams parameters, IReadOnlyList<MarketCandle> candles, int index, StrategyContext context, CausalIndicatorCache cache) =>
        ProductionNewEvaluator.Evaluate(parameters with { TemplateKey = StrategyTemplateKeys.MarketRegimeNew }, candles, index, context, cache);
}

public static class MomentumRelativeStrengthNewStrategy
{
    public static StrategySignalDetail Evaluate(StrategyTemplateParams parameters, IReadOnlyList<MarketCandle> candles, int index, StrategyContext context, CausalIndicatorCache cache) =>
        ProductionNewEvaluator.Evaluate(parameters with { TemplateKey = StrategyTemplateKeys.MomentumRelativeStrengthNew }, candles, index, context, cache);
}

public static class MeanReversionRsiBollingerNewStrategy
{
    public static StrategySignalDetail Evaluate(StrategyTemplateParams parameters, IReadOnlyList<MarketCandle> candles, int index, StrategyContext context, CausalIndicatorCache cache) =>
        ProductionNewEvaluator.Evaluate(parameters with { TemplateKey = StrategyTemplateKeys.MeanReversionRsiBollingerNew }, candles, index, context, cache);
}

public static class FundingBasisNewStrategy
{
    public static StrategySignalDetail Evaluate(StrategyTemplateParams parameters, IReadOnlyList<MarketCandle> candles, int index, StrategyContext context, CausalIndicatorCache cache) =>
        ProductionNewEvaluator.Evaluate(parameters with { TemplateKey = StrategyTemplateKeys.FundingBasisNew }, candles, index, context, cache);
}
