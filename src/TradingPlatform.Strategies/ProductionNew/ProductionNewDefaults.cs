using TradingPlatform.Domain.Strategies;

namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Research defaults for the six <c>-new</c> strategies. These are starting values, not optimized or validated parameters.
/// </summary>
public static class ProductionNewDefaults
{
    public const string Evidence = StrategyValidationStatuses.InsufficientEvidence;

    public static StrategyTemplateParams Apply(StrategyTemplateParams core)
    {
        var shared = core with
        {
            AllowedSide = StrategySides.Both,
            Quality = new StrategyQualityParams(),
            VolumeFilterEnabled = false,
            UseFuturesFilter = false,
            AtrPeriod = 14,
            AtrStopMultiplier = 2m,
            AdxPeriod = 14,
            MinimumAdx = 20m,
            SwingLength = 2,
            VolatilityLookback = 100,
            FundingExtremePercentile = 0.90m,
            MaxImpulseAgeBars = 0,
            EmaSlopeFilter = false,
            AtrTrailEnabled = false,
            OppositeTrendExit = true,
            EmaTrendBreakExit = false,
            MaxHoldBars = 0,
            TakeProfitR = 0m,
            ReentryConfirmation = true,
            RangeInvalidationExit = true,
            RequireBenchmark = false,
            HedgedMode = false,
            TopRankCount = 5,
            BottomRankCount = 5,
            BasisZEntry = 2m,
            FundingZEntry = 1.5m,
            MomentumWeight24h = 0.5m,
            MomentumWeight7d = 0.5m
        };

        return shared.TemplateKey switch
        {
            StrategyTemplateKeys.TrendFollowingNew => shared with { EmaFast = 50, EmaSlow = 200 },
            StrategyTemplateKeys.BreakoutVolumeNew => shared with
            {
                DonchianLength = 20,
                RelativeVolumePeriod = 20,
                BreakoutRelativeVolume = 1.5m,
                PriceDisplacementAtr = 1.5m
            },
            StrategyTemplateKeys.MeanReversionRsiBollingerNew => shared with
            {
                BbPeriod = 20,
                BbStdDev = 2m,
                RsiPeriod = 14,
                RsiOversold = 30m,
                RsiOverbought = 70m,
                ExitLookback = 8
            },
            StrategyTemplateKeys.MomentumRelativeStrengthNew => shared,
            StrategyTemplateKeys.FundingBasisNew => shared with { FundingLookback = 48 },
            _ => shared
        };
    }

    public static string Datasets(string key) => StrategyTemplateKeys.CanonicalId(key) switch
    {
        StrategyTemplateKeys.MomentumRelativeStrengthNew =>
            "Closed OHLCV for every symbol in the supplied universe, aligned by close time. Missing peers are not filled. Listing history is not invented.",
        StrategyTemplateKeys.FundingBasisNew =>
            "Closed OHLCV plus settled funding and timestamp-aligned mark and index prices. Missing funding is not treated as zero. Hedged spot execution is disabled.",
        StrategyTemplateKeys.MarketRegimeNew =>
            "Closed OHLCV. ADX(14) is trend strength, not direction. NATR percentile is symbol and timeframe specific. This module does not place orders.",
        _ => "Closed OHLCV only. Decisions use the completed candle. The next modeled fill is the following bar open."
    };
}
