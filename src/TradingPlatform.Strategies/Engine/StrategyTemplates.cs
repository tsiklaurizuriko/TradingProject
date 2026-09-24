using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Strategies;

namespace TradingPlatform.Strategies.Engine;

public static class StrategyTemplateKeys
{
    public const string EmaRsiTrend = "ema_rsi_trend";
    public const string MacdTrend = "macd_trend";
    public const string RsiPullback = "rsi_pullback";
    public const string BollingerReversion = "bollinger_reversion";
    public const string DonchianBreakout = "donchian_breakout";
    public const string TurtleTsm = "turtle_tsm";
    public const string VwapPullbackTrend = "vwap_pullback_trend";
    public const string VolatilityBreakout = "volatility_breakout";
    public const string SupertrendEmaTrend = "supertrend_ema_trend";
    public const string OiPriceMomentum = "oi_price_momentum";
    public const string FundingOiRegime = "funding_oi_regime";
    public const string VpVwapReversion = "vp_vwap_reversion";
    public const string LiqSweepReversal = "liq_sweep_reversal";
    public const string LiqSweepContinuation = "liq_sweep_continuation";
    public const string FundingBasisRv = "funding_basis_rv";
    public const string FundingOiReversal = "funding_oi_reversal";
    public const string TakerFlowMomentum = "taker_flow_momentum";
    public const string OiPriceVolumeRegime = "oi_price_volume_regime";
    public const string VwapDeviationReversion = "vwap_deviation_reversion";
    public const string VwapBreakoutVolume = "vwap_breakout_volume";
    public const string FailedBreakoutReversal = "failed_breakout_reversal";
    public const string VolSqueezeStructure = "vol_squeeze_structure";
    public const string MarketStructureTrend = "market_structure_trend";
    public const string MarketStructurePullback = "market_structure_pullback";
    public const string AtrNormalizedMomentum = "atr_normalized_momentum";
    public const string MtfTrendStructure = "mtf_trend_structure";
    public const string ZscoreMeanReversion = "zscore_mean_reversion";
    public const string CryptoPairsArb = "crypto_pairs_arb";
    public const string XsRelativeStrength = "xs_relative_strength";
    public const string RegimeStrategyRouter = "regime_strategy_router";
    public const string FundingPriceMomentum = "funding_price_momentum";
    public const string FundingExtremeMomentumExhaustion = "funding_extreme_momentum_exhaustion";
    public const string BasisMeanReversion = "basis_mean_reversion";
    public const string FundingBasisVwap = "funding_basis_vwap";
    public const string OiBreakoutConfirmation = "oi_breakout_confirmation";
    public const string VolSpikeEmaTrend = "vol_spike_ema_trend";
    public const string Bb202Break = "bb20_2_break";
    public const string ScalpEmaMomentum = "scalp_ema_momentum";
    public const string ScalpVwapReclaim = "scalp_vwap_reclaim";
    public const string ScalpVwapReversion = "scalp_vwap_reversion";
    public const string ScalpVwapBreakout = "scalp_vwap_breakout";
    public const string ScalpBreakoutRetest = "scalp_breakout_retest";
    public const string ScalpLiqSweep = "scalp_liq_sweep";
    public const string ScalpRsiPullback = "scalp_rsi_pullback";
    public const string ScalpRsiReversion = "scalp_rsi_reversion";
    public const string ScalpMacdMicro = "scalp_macd_micro";
    public const string ScalpBbReversion = "scalp_bb_reversion";
    public const string ScalpBbSqueeze = "scalp_bb_squeeze";
    public const string ScalpAtrBreakout = "scalp_atr_breakout";
    public const string ScalpAdxTrend = "scalp_adx_trend";
    public const string ScalpRvolMomentum = "scalp_rvol_momentum";
    public const string ScalpMarketStructure = "scalp_market_structure";
    public const string ScalpStochMomentum = "scalp_stoch_momentum";
    public const string ScalpMtf = "scalp_mtf";
    public const string ScalpSession = "scalp_session";
    public const string ScalpTakerFlow = "scalp_taker_flow";
    public const string ScalpPriceOi = "scalp_price_oi";
    public const string ScalpFundingOi = "scalp_funding_oi";
    public const string ScalpBasis = "scalp_basis";
    public const string PaWDoubleBottom = "pa_w_double_bottom";
    public const string PaMDoubleTop = "pa_m_double_top";
    public const string PaBullFlag = "pa_bull_flag";
    public const string PaBearFlag = "pa_bear_flag";
    public const string PaPennant = "pa_pennant";
    public const string PaAscendingTriangle = "pa_ascending_triangle";
    public const string PaDescendingTriangle = "pa_descending_triangle";
    public const string PaSymmetricalTriangle = "pa_symmetrical_triangle";
    public const string PaRisingWedge = "pa_rising_wedge";
    public const string PaFallingWedge = "pa_falling_wedge";
    public const string PaRectangleBreakout = "pa_rectangle_breakout";
    public const string PaBreakoutRetest = "pa_breakout_retest";
    public const string PaLiquiditySweep = "pa_liquidity_sweep";
    public const string PaHeadShoulders = "pa_head_shoulders";
    public const string PaInverseHeadShoulders = "pa_inverse_head_shoulders";
    public const string PaCandleSequence = "pa_candle_sequence";
    public const string PaStructureBreak = "pa_structure_break";
    public const string PaFailedBreakout = "pa_failed_breakout";
    public const string CrossSectionalReversalReturn15m = "cross_sectional_reversal_return_15m";
    public const string CrossSectionalReversalReturn1h = "cross_sectional_reversal_return_1h";

    public static readonly string[] Frozen =
    [
        EmaRsiTrend,
        MacdTrend,
        RsiPullback,
        BollingerReversion,
        DonchianBreakout
    ];

    public static readonly string[] AdvancedSix =
    [
        TurtleTsm,
        VwapPullbackTrend,
        VolatilityBreakout,
        SupertrendEmaTrend,
        OiPriceMomentum,
        FundingOiRegime
    ];

    public static readonly string[] Alpha =
    [
        VpVwapReversion,
        LiqSweepReversal,
        LiqSweepContinuation,
        FundingBasisRv,
        FundingOiReversal,
        TakerFlowMomentum,
        OiPriceVolumeRegime,
        VwapDeviationReversion,
        VwapBreakoutVolume,
        FailedBreakoutReversal,
        VolSqueezeStructure,
        MarketStructureTrend,
        MarketStructurePullback,
        AtrNormalizedMomentum,
        MtfTrendStructure,
        ZscoreMeanReversion,
        CryptoPairsArb,
        XsRelativeStrength,
        RegimeStrategyRouter,
        FundingPriceMomentum,
        FundingExtremeMomentumExhaustion,
        BasisMeanReversion,
        FundingBasisVwap,
        OiBreakoutConfirmation
    ];

    public static readonly string[] HistoricallyFitted =
    [
        VolSpikeEmaTrend,
        Bb202Break
    ];

    public static readonly string[] Scalping =
    [
        ScalpEmaMomentum,
        ScalpVwapReclaim,
        ScalpVwapReversion,
        ScalpVwapBreakout,
        ScalpBreakoutRetest,
        ScalpLiqSweep,
        ScalpRsiPullback,
        ScalpRsiReversion,
        ScalpMacdMicro,
        ScalpBbReversion,
        ScalpBbSqueeze,
        ScalpAtrBreakout,
        ScalpAdxTrend,
        ScalpRvolMomentum,
        ScalpMarketStructure,
        ScalpStochMomentum,
        ScalpMtf,
        ScalpSession,
        ScalpTakerFlow,
        ScalpPriceOi,
        ScalpFundingOi,
        ScalpBasis
    ];

    public static readonly string[] PriceAction =
    [
        PaWDoubleBottom,
        PaMDoubleTop,
        PaBullFlag,
        PaBearFlag,
        PaPennant,
        PaAscendingTriangle,
        PaDescendingTriangle,
        PaSymmetricalTriangle,
        PaRisingWedge,
        PaFallingWedge,
        PaRectangleBreakout,
        PaBreakoutRetest,
        PaLiquiditySweep,
        PaHeadShoulders,
        PaInverseHeadShoulders,
        PaCandleSequence,
        PaStructureBreak,
        PaFailedBreakout
    ];

    public static readonly string[] CrossSectionalReversal =
    [
        CrossSectionalReversalReturn15m,
        CrossSectionalReversalReturn1h
    ];

    public static readonly string[] Research = [.. AdvancedSix, .. Alpha, .. HistoricallyFitted, .. Scalping, .. PriceAction];

    public static readonly string[] NearMiss = NearMissAudit.SelectedRows.Select(NearMissAudit.TemplateKey).ToArray();

    public static readonly string[] All = [.. Frozen, .. Research, .. NearMiss, .. CrossSectionalReversal];

    /// <summary>
    /// Operator catalog: PAPER or weak guidance only. Avoid/blocked templates stay in the engine
    /// for Frozen tests and any bots already running them.
    /// </summary>
    public static readonly string[] OperatorCatalog =
    [
        EmaRsiTrend,
        RsiPullback,
        BollingerReversion,
        SupertrendEmaTrend,
        LiqSweepContinuation,
        VolSqueezeStructure,
        VwapBreakoutVolume,
        MarketStructureTrend,
        VolSpikeEmaTrend,
        Bb202Break
    ];

    public static readonly string[] SupportedTimeframes = ["5m", "15m", "1h"];
    public static readonly string[] ScalpingTimeframes = ["1m", "3m", "5m", "15m"];
    public static readonly string[] SupportedDirections = ["LONG", "SHORT"];

    public static bool IsKnown(string? key) =>
        All.Contains((key ?? "").Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsFrozen(string? key) =>
        Frozen.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static bool IsHistoricallyFitted(string? key) =>
        HistoricallyFitted.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static bool IsOperatorCatalog(string? key)
    {
        var raw = (key ?? "").Trim();
        return OperatorCatalog.Contains(raw, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsScalping(string? key) =>
        Scalping.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static bool IsPriceAction(string? key) =>
        PriceAction.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static bool IsNearMiss(string? key) =>
        NearMiss.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static string NearMissHypothesisId(string? key)
    {
        var normalized = Normalize(key);
        foreach (var row in NearMissAudit.SelectedRows)
        {
            if (string.Equals(NearMissAudit.TemplateKey(row), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return row.HypothesisId;
            }
        }

        return "";
    }

    public static string NearMissFamily(string? key)
    {
        var normalized = Normalize(key);
        foreach (var row in NearMissAudit.SelectedRows)
        {
            if (string.Equals(NearMissAudit.TemplateKey(row), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return row.Family;
            }
        }

        return "";
    }

    public static bool IsCrossSectionalReversal(string? key) =>
        CrossSectionalReversal.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static bool IsResearchOnlyFamily(string? key) => IsScalping(key) || IsPriceAction(key) || IsCrossSectionalReversal(key);

    public static bool IsResearch(string? key) =>
        Research.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static bool IsAlpha(string? key) =>
        Alpha.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> RequiredDatasets(string templateKey) => Normalize(templateKey) switch
    {
        TakerFlowMomentum or ScalpTakerFlow => ["OHLCV", "TakerFlow"],
        OiPriceMomentum or OiPriceVolumeRegime or OiBreakoutConfirmation or ScalpPriceOi => ["OHLCV", "OpenInterest"],
        FundingOiRegime or FundingOiReversal or ScalpFundingOi => ["OHLCV", "Funding", "OpenInterest"],
        FundingBasisRv or FundingBasisVwap => ["OHLCV", "Funding", "MarkPrice", "IndexPrice", "Basis"],
        FundingPriceMomentum or FundingExtremeMomentumExhaustion => ["OHLCV", "Funding"],
        BasisMeanReversion or ScalpBasis => ["OHLCV", "MarkPrice", "IndexPrice", "Basis"],
        CryptoPairsArb => ["OHLCV", "CausalPairUniverse"],
        XsRelativeStrength or CrossSectionalReversalReturn15m or CrossSectionalReversalReturn1h => ["OHLCV", "CrossSectionUniverse"],
        MtfTrendStructure or ScalpMtf => ["OHLCV", "CompletedHtf"],
        _ => ["OHLCV"]
    };

    public static string Family(string? key) => Normalize(key) switch
    {
        TurtleTsm or VolatilityBreakout or VolSqueezeStructure or AtrNormalizedMomentum or LiqSweepContinuation or VwapBreakoutVolume => "BREAKOUT / TREND",
        VwapPullbackTrend or SupertrendEmaTrend or MarketStructureTrend or MarketStructurePullback or MtfTrendStructure => "TREND / STRUCTURE",
        VpVwapReversion or VwapDeviationReversion or ZscoreMeanReversion or CryptoPairsArb => "MEAN REVERSION",
        LiqSweepReversal or FailedBreakoutReversal or FundingOiReversal => "REVERSAL",
        OiPriceMomentum or FundingOiRegime or FundingBasisRv or TakerFlowMomentum or OiPriceVolumeRegime or XsRelativeStrength
            or FundingPriceMomentum or FundingExtremeMomentumExhaustion or BasisMeanReversion or FundingBasisVwap
            or OiBreakoutConfirmation => "FUTURES / FLOW",
        RegimeStrategyRouter => "ROUTER",
        BollingerReversion => "MEAN REVERSION",
        DonchianBreakout => "BREAKOUT / TREND",
        Bb202Break => "BREAKOUT / TREND",
        VolSpikeEmaTrend => "TREND",
        var scalp when IsScalping(scalp) => "SCALPING",
        var pa when IsPriceAction(pa) => "SCALPING_PRICE_ACTION",
        var near when IsNearMiss(near) => "NEAR_MISS",
        var csr when IsCrossSectionalReversal(csr) => "CROSS_SECTIONAL_REVERSAL",
        _ => "TREND"
    };

    public static string Normalize(string? key)
    {
        var raw = (key ?? "").Trim().ToLowerInvariant();
        return All.FirstOrDefault(k => k == raw) ?? EmaRsiTrend;
    }
}

public static class StrategySides
{
    public const string Long = "Long";
    public const string Short = "Short";
    public const string Both = "Both";

    public static string Normalize(string? value)
    {
        var raw = (value ?? Long).Trim();
        if (raw.Equals(Short, StringComparison.OrdinalIgnoreCase)) return Short;
        if (raw.Equals(Both, StringComparison.OrdinalIgnoreCase)) return Both;
        return Long;
    }

    public static bool AllowsLong(string? value)
    {
        var side = Normalize(value);
        return side is Long or Both;
    }

    public static bool AllowsShort(string? value)
    {
        var side = Normalize(value);
        return side is Short or Both;
    }
}

public sealed record StrategyQualityParams(
    bool RequireVolume = false,
    int VolumeLookback = 20,
    decimal MinAtrPercent = 0m,
    decimal MaxAtrPercent = 0m);

public sealed record StrategyTemplateParams(
    string TemplateKey = StrategyTemplateKeys.EmaRsiTrend,
    string AllowedSide = StrategySides.Long,
    string Timeframe = "5m",
    int EmaFast = 20,
    int EmaSlow = 50,
    int RsiPeriod = 14,
    decimal RsiMinimum = 50m,
    decimal RsiLongMax = 68m,
    decimal RsiOversold = 30m,
    decimal RsiOverbought = 70m,
    int MacdFast = 12,
    int MacdSlow = 26,
    int MacdSignal = 9,
    int BbPeriod = 20,
    decimal BbStdDev = 2m,
    int DonchianLength = 20,
    StrategyQualityParams? Quality = null,
    int EntryLookback = 20,
    int ExitLookback = 10,
    int AtrPeriod = 14,
    decimal AtrStopMultiplier = 2m,
    int TrendEmaPeriod = 50,
    bool VolumeFilterEnabled = true,
    int RelativeVolumePeriod = 20,
    decimal MinimumRelativeVolume = 1m,
    decimal MaxVwapDistanceAtr = 0.75m,
    decimal StopAtrMultiplier = 1.5m,
    int VolatilityLookback = 100,
    decimal CompressionPercentile = 0.20m,
    int AtrExpansionLookback = 20,
    decimal BreakoutRelativeVolume = 1.2m,
    int SupertrendPeriod = 10,
    decimal SupertrendMultiplier = 3m,
    int AdxPeriod = 14,
    decimal MinimumAdx = 20m,
    int OiLookback = 20,
    decimal OiChangeThreshold = 0.02m,
    decimal PriceChangeThreshold = 0.01m,
    string OiHypothesis = "continuation",
    int FundingLookback = 24,
    decimal FundingExtremePercentile = 0.90m,
    string FundingHypothesis = "continuation",
    decimal ZScoreEntry = 2m,
    decimal ValueAreaPercent = 0.70m,
    decimal SweepDepthAtr = 0.15m,
    int SwingLength = 3,
    bool UseFuturesFilter = true,
    decimal PriceDisplacementAtr = 1.5m,
    decimal OiExtremePercentile = 0.90m);

public static class StrategyTemplates
{
    public static StrategyTemplateParams DefaultsFor(string templateKey, bool qualityOn)
    {
        var key = StrategyTemplateKeys.Normalize(templateKey);
        var quality = qualityOn
            ? new StrategyQualityParams(true, 20, 0.15m, 4m)
            : new StrategyQualityParams();
        var core = new StrategyTemplateParams(TemplateKey: key, Quality: quality);
        return key switch
        {
            StrategyTemplateKeys.TurtleTsm => core with
            {
                EntryLookback = 20,
                ExitLookback = 10,
                AtrPeriod = 14,
                AtrStopMultiplier = 2m,
                TrendEmaPeriod = 50,
                VolumeFilterEnabled = true,
                RelativeVolumePeriod = 20,
                MinimumRelativeVolume = 1m,
                DonchianLength = 20
            },
            StrategyTemplateKeys.VwapPullbackTrend => core with
            {
                EmaFast = 20,
                EmaSlow = 50,
                AtrPeriod = 14,
                RelativeVolumePeriod = 20,
                MinimumRelativeVolume = 0.8m,
                VolumeFilterEnabled = true,
                RsiPeriod = 14,
                MaxVwapDistanceAtr = 0.75m,
                StopAtrMultiplier = 1.5m
            },
            StrategyTemplateKeys.VolatilityBreakout => core with
            {
                BbPeriod = 20,
                BbStdDev = 2m,
                AtrPeriod = 14,
                VolatilityLookback = 100,
                RelativeVolumePeriod = 20,
                BreakoutRelativeVolume = 1.2m,
                CompressionPercentile = 0.20m,
                AtrExpansionLookback = 20,
                EmaSlow = 50
            },
            StrategyTemplateKeys.SupertrendEmaTrend => core with
            {
                SupertrendPeriod = 10,
                SupertrendMultiplier = 3m,
                EmaFast = 20,
                EmaSlow = 50,
                AdxPeriod = 14,
                MinimumAdx = 20m,
                AtrPeriod = 10
            },
            StrategyTemplateKeys.OiPriceMomentum => core with
            {
                OiLookback = 20,
                OiChangeThreshold = 0.02m,
                PriceChangeThreshold = 0.01m,
                RelativeVolumePeriod = 20,
                MinimumRelativeVolume = 1m,
                OiHypothesis = "continuation",
                EmaFast = 20,
                EmaSlow = 50
            },
            StrategyTemplateKeys.FundingOiRegime => core with
            {
                FundingLookback = 24,
                FundingExtremePercentile = 0.90m,
                FundingHypothesis = "continuation",
                OiLookback = 20,
                OiChangeThreshold = 0.02m,
                PriceChangeThreshold = 0.01m,
                RelativeVolumePeriod = 20,
                EmaFast = 20,
                EmaSlow = 50,
                AtrPeriod = 14
            },
            var alpha when StrategyTemplateKeys.IsAlpha(alpha) => core with
            {
                EntryLookback = 20,
                MaxVwapDistanceAtr = 1.5m,
                StopAtrMultiplier = 1.5m,
                CompressionPercentile = 0.20m,
                BreakoutRelativeVolume = 1.2m,
                MinimumAdx = 25m,
                ZScoreEntry = 2m,
                ValueAreaPercent = 0.70m,
                SweepDepthAtr = 0.15m,
                SwingLength = 3,
                VolumeFilterEnabled = true,
                RelativeVolumePeriod = 20,
                MinimumRelativeVolume = 0.8m
            },
            StrategyTemplateKeys.VolSpikeEmaTrend => core with
            {
                Timeframe = "15m",
                AllowedSide = StrategySides.Both,
                EmaFast = 20,
                EmaSlow = 21,
                RelativeVolumePeriod = 20,
                MinimumRelativeVolume = 1.5m,
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.Bb202Break => core with
            {
                Timeframe = "15m",
                AllowedSide = StrategySides.Both,
                BbPeriod = 20,
                BbStdDev = 2m,
                VolumeFilterEnabled = false
            },
            var pa when StrategyTemplateKeys.IsPriceAction(pa) => core with
            {
                EmaFast = 8,
                EmaSlow = 21,
                RsiPeriod = 9,
                RelativeVolumePeriod = 20,
                VolumeFilterEnabled = false,
                SwingLength = 3,
                Timeframe = "5m",
                AllowedSide = StrategySides.Both
            },
            var scalp when StrategyTemplateKeys.IsScalping(scalp) => core with
            {
                EmaFast = 8,
                EmaSlow = 21,
                RsiPeriod = 9,
                RsiMinimum = 45m,
                RsiLongMax = 70m,
                MacdFast = 8,
                MacdSlow = 17,
                MacdSignal = 9,
                BbPeriod = 20,
                DonchianLength = 10,
                AdxPeriod = 14,
                MinimumAdx = 20m,
                RelativeVolumePeriod = 20,
                MinimumRelativeVolume = 1.2m,
                VolumeFilterEnabled = true,
                Timeframe = "5m"
            },
            _ => core
        };
    }

    public static StrategyTemplateParams Validate(StrategyTemplateParams parameters)
    {
        var key = StrategyTemplateKeys.Normalize(parameters.TemplateKey);
        if (parameters.EmaFast < 2 || parameters.EmaSlow <= parameters.EmaFast)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "EMA slow period must be greater than the fast period.");
        }

        if (parameters.RsiPeriod < 2)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "RSI period must be at least 2.");
        }

        if (parameters.RsiMinimum is < 0 or > 100 || parameters.RsiLongMax is < 0 or > 100)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "RSI bounds must be between 0 and 100.");
        }

        if (parameters.RsiLongMax <= parameters.RsiMinimum)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "RSI long max must be greater than RSI minimum.");
        }

        if (parameters.RsiOversold is < 0 or > 50 || parameters.RsiOverbought is < 50 or > 100)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "RSI oversold must be 0–50 and overbought 50–100.");
        }

        if (parameters.MacdFast < 2 || parameters.MacdSlow <= parameters.MacdFast || parameters.MacdSignal < 2)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "MACD slow must be greater than fast, and signal at least 2.");
        }

        if (parameters.BbPeriod < 5 || parameters.BbStdDev <= 0m)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Bollinger period must be at least 5 and stddev greater than 0.");
        }

        if (parameters.DonchianLength < 5 || parameters.EntryLookback < 5 || parameters.ExitLookback < 2)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Donchian length must be at least 5.");
        }

        if (parameters.AtrPeriod < 2
            || parameters.AtrStopMultiplier <= 0m
            || parameters.StopAtrMultiplier <= 0m
            || parameters.TrendEmaPeriod < 2
            || parameters.RelativeVolumePeriod < 2
            || parameters.MinimumRelativeVolume < 0m
            || parameters.MaxVwapDistanceAtr <= 0m
            || parameters.VolatilityLookback < 10
            || parameters.CompressionPercentile is < 0m or > 1m
            || parameters.AtrExpansionLookback < 2
            || parameters.BreakoutRelativeVolume < 0m
            || parameters.SupertrendPeriod < 2
            || parameters.SupertrendMultiplier <= 0m
            || parameters.AdxPeriod < 2
            || parameters.MinimumAdx < 0m
            || parameters.OiLookback < 2
            || parameters.FundingLookback < 2)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Advanced research parameters are out of range.");
        }

        var quality = parameters.Quality ?? new StrategyQualityParams();
        if (quality.VolumeLookback < 2)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Volume lookback must be at least 2.");
        }

        if (quality.MinAtrPercent < 0m || quality.MaxAtrPercent < 0m)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "ATR percent bounds cannot be negative.");
        }

        if (quality.MaxAtrPercent > 0m && quality.MinAtrPercent > quality.MaxAtrPercent)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "ATR min percent must be at most ATR max percent.");
        }

        return parameters with
        {
            TemplateKey = key,
            AllowedSide = StrategySides.Normalize(parameters.AllowedSide),
            Quality = quality
        };
    }

    public static string Build(string name, int version, StrategyTemplateParams parameters)
    {
        var p = Validate(parameters);
        var q = p.Quality ?? new StrategyQualityParams();
        var json = $$"""
            {
              "name": {{JsonSerializer.Serialize(name)}},
              "version": {{version}},
              "template": {{JsonSerializer.Serialize(p.TemplateKey)}},
              "timeframe": {{JsonSerializer.Serialize(p.Timeframe)}},
              "allowedSide": {{JsonSerializer.Serialize(p.AllowedSide)}},
              "params": {
                "emaFast": {{p.EmaFast}},
                "emaSlow": {{p.EmaSlow}},
                "rsiPeriod": {{p.RsiPeriod}},
                "rsiMinimum": {{Invariant(p.RsiMinimum)}},
                "rsiLongMax": {{Invariant(p.RsiLongMax)}},
                "rsiOversold": {{Invariant(p.RsiOversold)}},
                "rsiOverbought": {{Invariant(p.RsiOverbought)}},
                "macdFast": {{p.MacdFast}},
                "macdSlow": {{p.MacdSlow}},
                "macdSignal": {{p.MacdSignal}},
                "bbPeriod": {{p.BbPeriod}},
                "bbStdDev": {{Invariant(p.BbStdDev)}},
                "donchianLength": {{p.DonchianLength}},
                "entryLookback": {{p.EntryLookback}},
                "exitLookback": {{p.ExitLookback}},
                "atrPeriod": {{p.AtrPeriod}},
                "atrStopMultiplier": {{Invariant(p.AtrStopMultiplier)}},
                "trendEmaPeriod": {{p.TrendEmaPeriod}},
                "volumeFilterEnabled": {{(p.VolumeFilterEnabled ? "true" : "false")}},
                "relativeVolumePeriod": {{p.RelativeVolumePeriod}},
                "minimumRelativeVolume": {{Invariant(p.MinimumRelativeVolume)}},
                "maxVwapDistanceAtr": {{Invariant(p.MaxVwapDistanceAtr)}},
                "stopAtrMultiplier": {{Invariant(p.StopAtrMultiplier)}},
                "volatilityLookback": {{p.VolatilityLookback}},
                "compressionPercentile": {{Invariant(p.CompressionPercentile)}},
                "atrExpansionLookback": {{p.AtrExpansionLookback}},
                "breakoutRelativeVolume": {{Invariant(p.BreakoutRelativeVolume)}},
                "supertrendPeriod": {{p.SupertrendPeriod}},
                "supertrendMultiplier": {{Invariant(p.SupertrendMultiplier)}},
                "adxPeriod": {{p.AdxPeriod}},
                "minimumAdx": {{Invariant(p.MinimumAdx)}},
                "oiLookback": {{p.OiLookback}},
                "oiChangeThreshold": {{Invariant(p.OiChangeThreshold)}},
                "priceChangeThreshold": {{Invariant(p.PriceChangeThreshold)}},
                "oiHypothesis": {{JsonSerializer.Serialize(p.OiHypothesis)}},
                "fundingLookback": {{p.FundingLookback}},
                "fundingExtremePercentile": {{Invariant(p.FundingExtremePercentile)}},
                "fundingHypothesis": {{JsonSerializer.Serialize(p.FundingHypothesis)}},
                "zScoreEntry": {{Invariant(p.ZScoreEntry)}},
                "valueAreaPercent": {{Invariant(p.ValueAreaPercent)}},
                "sweepDepthAtr": {{Invariant(p.SweepDepthAtr)}},
                "swingLength": {{p.SwingLength}},
                "useFuturesFilter": {{(p.UseFuturesFilter ? "true" : "false")}},
                "priceDisplacementAtr": {{Invariant(p.PriceDisplacementAtr)}},
                "oiExtremePercentile": {{Invariant(p.OiExtremePercentile)}}
              },
              "quality": {
                "requireVolume": {{(q.RequireVolume ? "true" : "false")}},
                "volumeLookback": {{q.VolumeLookback}},
                "minAtrPercent": {{Invariant(q.MinAtrPercent)}},
                "maxAtrPercent": {{Invariant(q.MaxAtrPercent)}}
              }
            }
            """;
        new StrategyDefinitionValidator().Parse(json);
        return json;
    }

    public static StrategyTemplateParams Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return DefaultsFor(StrategyTemplateKeys.EmaRsiTrend, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var template = root.TryGetProperty("template", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(template) && LooksLegacyEmaRsi(root))
            {
                template = StrategyTemplateKeys.EmaRsiTrend;
            }

            var p = root.TryGetProperty("params", out var paramsNode) ? paramsNode : root;
            var q = root.TryGetProperty("quality", out var qualityNode) ? qualityNode : default;
            var legacy = EmaRsiTemplate.ReadLegacy(json);
            return new StrategyTemplateParams(
                StrategyTemplateKeys.Normalize(template),
                StrategySides.Normalize(root.TryGetProperty("allowedSide", out var side) ? side.GetString() : StrategySides.Long),
                root.TryGetProperty("timeframe", out var tf) ? tf.GetString() ?? "5m" : "5m",
                Int(p, "emaFast") ?? legacy.EmaFast,
                Int(p, "emaSlow") ?? legacy.EmaSlow,
                Int(p, "rsiPeriod") ?? legacy.RsiPeriod,
                Dec(p, "rsiMinimum") ?? legacy.RsiMinimum,
                Dec(p, "rsiLongMax") ?? 68m,
                Dec(p, "rsiOversold") ?? 30m,
                Dec(p, "rsiOverbought") ?? 70m,
                Int(p, "macdFast") ?? 12,
                Int(p, "macdSlow") ?? 26,
                Int(p, "macdSignal") ?? 9,
                Int(p, "bbPeriod") ?? 20,
                Dec(p, "bbStdDev") ?? 2m,
                Int(p, "donchianLength") ?? 20,
                new StrategyQualityParams(
                    Bool(q, "requireVolume"),
                    Int(q, "volumeLookback") ?? 20,
                    Dec(q, "minAtrPercent") ?? 0m,
                    Dec(q, "maxAtrPercent") ?? 0m),
                Int(p, "entryLookback") ?? 20,
                Int(p, "exitLookback") ?? 10,
                Int(p, "atrPeriod") ?? 14,
                Dec(p, "atrStopMultiplier") ?? 2m,
                Int(p, "trendEmaPeriod") ?? 50,
                !p.TryGetProperty("volumeFilterEnabled", out _) || Bool(p, "volumeFilterEnabled"),
                Int(p, "relativeVolumePeriod") ?? 20,
                Dec(p, "minimumRelativeVolume") ?? 1m,
                Dec(p, "maxVwapDistanceAtr") ?? 0.75m,
                Dec(p, "stopAtrMultiplier") ?? 1.5m,
                Int(p, "volatilityLookback") ?? 100,
                Dec(p, "compressionPercentile") ?? 0.20m,
                Int(p, "atrExpansionLookback") ?? 20,
                Dec(p, "breakoutRelativeVolume") ?? 1.2m,
                Int(p, "supertrendPeriod") ?? 10,
                Dec(p, "supertrendMultiplier") ?? 3m,
                Int(p, "adxPeriod") ?? 14,
                Dec(p, "minimumAdx") ?? 20m,
                Int(p, "oiLookback") ?? 20,
                Dec(p, "oiChangeThreshold") ?? 0.02m,
                Dec(p, "priceChangeThreshold") ?? 0.01m,
                Str(p, "oiHypothesis") ?? "continuation",
                Int(p, "fundingLookback") ?? 24,
                Dec(p, "fundingExtremePercentile") ?? 0.90m,
                Str(p, "fundingHypothesis") ?? "continuation",
                Dec(p, "zScoreEntry") ?? 2m,
                Dec(p, "valueAreaPercent") ?? 0.70m,
                Dec(p, "sweepDepthAtr") ?? 0.15m,
                Int(p, "swingLength") ?? 3,
                p.TryGetProperty("useFuturesFilter", out var uff) && uff.ValueKind == JsonValueKind.False ? false : true,
                Dec(p, "priceDisplacementAtr") ?? 1.5m,
                Dec(p, "oiExtremePercentile") ?? 0.90m);
        }
        catch (JsonException)
        {
            return DefaultsFor(StrategyTemplateKeys.EmaRsiTrend, false);
        }
    }

    public static string DisplayName(string templateKey) => StrategyTemplateKeys.Normalize(templateKey) switch
    {
        StrategyTemplateKeys.MacdTrend => "MACD Trend",
        StrategyTemplateKeys.RsiPullback => "RSI Pullback",
        StrategyTemplateKeys.BollingerReversion => "Bollinger Reversion",
        StrategyTemplateKeys.DonchianBreakout => "Donchian Breakout",
        StrategyTemplateKeys.TurtleTsm => "Turtle Time-Series Momentum",
        StrategyTemplateKeys.VwapPullbackTrend => "VWAP Pullback Trend",
        StrategyTemplateKeys.VolatilityBreakout => "Volatility Breakout",
        StrategyTemplateKeys.SupertrendEmaTrend => "Supertrend EMA Trend",
        StrategyTemplateKeys.OiPriceMomentum => "Open Interest Price Momentum",
        StrategyTemplateKeys.FundingOiRegime => "Funding Rate Price OI Regime",
        StrategyTemplateKeys.VpVwapReversion => "Volume Profile VWAP Mean Reversion",
        StrategyTemplateKeys.LiqSweepReversal => "Liquidity Sweep Reversal",
        StrategyTemplateKeys.LiqSweepContinuation => "Liquidity Sweep Breakout Continuation",
        StrategyTemplateKeys.FundingBasisRv => "Funding Basis Carry Relative Value",
        StrategyTemplateKeys.FundingOiReversal => "Funding Extreme OI Price Reversal",
        StrategyTemplateKeys.TakerFlowMomentum => "Taker Flow Volume Imbalance Momentum",
        StrategyTemplateKeys.OiPriceVolumeRegime => "OI Price Volume Regime",
        StrategyTemplateKeys.VwapDeviationReversion => "VWAP Deviation Reversion",
        StrategyTemplateKeys.VwapBreakoutVolume => "VWAP Breakout Volume",
        StrategyTemplateKeys.FailedBreakoutReversal => "Failed Breakout Reversal",
        StrategyTemplateKeys.VolSqueezeStructure => "Volatility Squeeze Structure Break",
        StrategyTemplateKeys.MarketStructureTrend => "Market Structure Trend Continuation",
        StrategyTemplateKeys.MarketStructurePullback => "Market Structure Pullback",
        StrategyTemplateKeys.AtrNormalizedMomentum => "ATR-Normalized Momentum",
        StrategyTemplateKeys.MtfTrendStructure => "Multi-Timeframe Trend LTF Structure",
        StrategyTemplateKeys.ZscoreMeanReversion => "Z-Score Statistical Mean Reversion",
        StrategyTemplateKeys.CryptoPairsArb => "Crypto Pairs Statistical Arbitrage",
        StrategyTemplateKeys.XsRelativeStrength => "Cross-Sectional Relative Strength Momentum",
        StrategyTemplateKeys.RegimeStrategyRouter => "Regime-Adaptive Strategy Router",
        StrategyTemplateKeys.FundingPriceMomentum => "Funding Price Momentum",
        StrategyTemplateKeys.FundingExtremeMomentumExhaustion => "Funding Extreme Momentum Exhaustion",
        StrategyTemplateKeys.BasisMeanReversion => "Basis Mean Reversion",
        StrategyTemplateKeys.FundingBasisVwap => "Funding Basis VWAP",
        StrategyTemplateKeys.OiBreakoutConfirmation => "OI Breakout Confirmation",
        StrategyTemplateKeys.VolSpikeEmaTrend => "BTC 15m Volume Spike EMA",
        StrategyTemplateKeys.Bb202Break => "BTC 15m Bollinger Break",
        StrategyTemplateKeys.ScalpEmaMomentum => "Scalp EMA Momentum",
        StrategyTemplateKeys.ScalpVwapReclaim => "Scalp VWAP Reclaim",
        StrategyTemplateKeys.ScalpVwapReversion => "Scalp VWAP Reversion",
        StrategyTemplateKeys.ScalpVwapBreakout => "Scalp VWAP Breakout",
        StrategyTemplateKeys.ScalpBreakoutRetest => "Scalp Breakout Retest",
        StrategyTemplateKeys.ScalpLiqSweep => "Scalp Liquidity Sweep",
        StrategyTemplateKeys.ScalpRsiPullback => "Scalp RSI Pullback",
        StrategyTemplateKeys.ScalpRsiReversion => "Scalp RSI Reversion",
        StrategyTemplateKeys.ScalpMacdMicro => "Scalp MACD Micro",
        StrategyTemplateKeys.ScalpBbReversion => "Scalp Bollinger Reversion",
        StrategyTemplateKeys.ScalpBbSqueeze => "Scalp Bollinger Squeeze",
        StrategyTemplateKeys.ScalpAtrBreakout => "Scalp ATR Breakout",
        StrategyTemplateKeys.ScalpAdxTrend => "Scalp ADX Trend",
        StrategyTemplateKeys.ScalpRvolMomentum => "Scalp Relative Volume Momentum",
        StrategyTemplateKeys.ScalpMarketStructure => "Scalp Market Structure",
        StrategyTemplateKeys.ScalpStochMomentum => "Scalp Stochastic Momentum",
        StrategyTemplateKeys.ScalpMtf => "Scalp Multi-Timeframe",
        StrategyTemplateKeys.ScalpSession => "Scalp Session Filter",
        StrategyTemplateKeys.ScalpTakerFlow => "Scalp Taker Flow",
        StrategyTemplateKeys.ScalpPriceOi => "Scalp Price Open Interest",
        StrategyTemplateKeys.ScalpFundingOi => "Scalp Funding Open Interest",
        StrategyTemplateKeys.ScalpBasis => "Scalp Basis",
        StrategyTemplateKeys.PaWDoubleBottom => "PA W Double Bottom",
        StrategyTemplateKeys.PaMDoubleTop => "PA M Double Top",
        StrategyTemplateKeys.PaBullFlag => "PA Bull Flag",
        StrategyTemplateKeys.PaBearFlag => "PA Bear Flag",
        StrategyTemplateKeys.PaPennant => "PA Pennant",
        StrategyTemplateKeys.PaAscendingTriangle => "PA Ascending Triangle",
        StrategyTemplateKeys.PaDescendingTriangle => "PA Descending Triangle",
        StrategyTemplateKeys.PaSymmetricalTriangle => "PA Symmetrical Triangle",
        StrategyTemplateKeys.PaRisingWedge => "PA Rising Wedge",
        StrategyTemplateKeys.PaFallingWedge => "PA Falling Wedge",
        StrategyTemplateKeys.PaRectangleBreakout => "PA Rectangle Breakout",
        StrategyTemplateKeys.PaBreakoutRetest => "PA Breakout Retest",
        StrategyTemplateKeys.PaLiquiditySweep => "PA Liquidity Sweep",
        StrategyTemplateKeys.PaHeadShoulders => "PA Head And Shoulders",
        StrategyTemplateKeys.PaInverseHeadShoulders => "PA Inverse Head And Shoulders",
        StrategyTemplateKeys.PaCandleSequence => "PA Candle Sequence",
        StrategyTemplateKeys.PaStructureBreak => "PA Structure Break",
        StrategyTemplateKeys.PaFailedBreakout => "PA Failed Breakout",
        var near when StrategyTemplateKeys.IsNearMiss(near) => NearMissTitle(near),
        StrategyTemplateKeys.CrossSectionalReversalReturn15m => "Return 15m Reversal",
        StrategyTemplateKeys.CrossSectionalReversalReturn1h => "Return 1h Reversal",
        _ => "EMA RSI Trend"
    };

    public static string Blurb(string templateKey) => StrategyTemplateKeys.Normalize(templateKey) switch
    {
        StrategyTemplateKeys.MacdTrend => "იმპულსის გადაკვეთას მიყვება (MACD × სიგნალი, ჰისტოგრამა, ნელი EMA). მიზანი — ტრენდის გაგრძელება, არა მოკლე ხმაური.",
        StrategyTemplateKeys.RsiPullback => "ტრენდში უკან დახევას იჭერს: RSI oversold/overbought-იდან ბრუნდება. მიზანი — ტრენდში იაფ შესვლა, არა წვერზე ნადირობა.",
        StrategyTemplateKeys.BollingerReversion => "ზოლიდან გადახრილ ფასს შუაში აბრუნებს. მიზანი — ექსტრემის კორექცია, არა გარღვევა.",
        StrategyTemplateKeys.DonchianBreakout => "ბოლო N სანთლის მაღალ/დაბალ ზოლს არღვევს და იმ მიმართულებით შედის. მიზანი — ახალი ექსტრემის გაგრძელება.",
        StrategyTemplateKeys.TurtleTsm => "Systematic trend-following strategy using prior-range breakouts, EMA trend confirmation and ATR-based volatility control.",
        StrategyTemplateKeys.VwapPullbackTrend => "Trend-following pullback strategy using VWAP, EMA structure, RSI confirmation and volatility-aware stops.",
        StrategyTemplateKeys.VolatilityBreakout => "Volatility-compression breakout strategy using Bollinger width, ATR expansion and relative volume.",
        StrategyTemplateKeys.SupertrendEmaTrend => "Trend-following strategy using Supertrend direction, EMA structure and ADX trend-strength confirmation.",
        StrategyTemplateKeys.OiPriceMomentum => "Futures-specific strategy researching conditional relationships between price movement, open interest, volume and trend.",
        StrategyTemplateKeys.FundingOiRegime => "Perpetual-futures strategy researching funding extremes together with price momentum and open-interest regimes.",
        StrategyTemplateKeys.VpVwapReversion => "Research whether VAL/VAH rejections revert toward POC/VWAP outside strong-trend regimes.",
        StrategyTemplateKeys.LiqSweepReversal => "Research failed breaks of causally confirmed swing highs/lows followed by a close back through the level.",
        StrategyTemplateKeys.LiqSweepContinuation => "Research sweeps that hold beyond the level with volume as breakout continuation, separate from reversal.",
        StrategyTemplateKeys.FundingBasisRv => "Research funding and basis extremes as directional or relative-value hypotheses. Requires aligned funding/index.",
        StrategyTemplateKeys.FundingOiReversal => "Research extreme funding plus OI and price displacement as a reversal hypothesis. Requires aligned series.",
        StrategyTemplateKeys.TakerFlowMomentum => "Research persistent taker buy/sell imbalance with price and volume confirmation.",
        StrategyTemplateKeys.OiPriceVolumeRegime => "Research conditional expectancy of price/OI/volume state combinations without pre-assigned bull/bear labels.",
        StrategyTemplateKeys.VwapDeviationReversion => "Research ATR-scaled VWAP deviations with rejection and a trend-regime filter.",
        StrategyTemplateKeys.VwapBreakoutVolume => "Research VWAP-aligned local breakouts with relative volume, on transition only.",
        StrategyTemplateKeys.FailedBreakoutReversal => "Research Donchian breakouts that fail to hold and close back inside the range.",
        StrategyTemplateKeys.VolSqueezeStructure => "Research Bollinger/Keltner compression then expansion with a structure break and volume.",
        StrategyTemplateKeys.MarketStructureTrend => "Research causal HH/HL or LH/LL continuation on a new confirmed swing.",
        StrategyTemplateKeys.MarketStructurePullback => "Research pullbacks to EMA/VWAP while causal market structure stays intact.",
        StrategyTemplateKeys.AtrNormalizedMomentum => "Research (Close[t]-Close[t-N])/ATR with trend and a persistence transition.",
        StrategyTemplateKeys.MtfTrendStructure => "Research last-completed HTF EMA trend with LTF structure/pullback entry.",
        StrategyTemplateKeys.ZscoreMeanReversion => "Research rolling close Z-score extremes with mean reversion disabled in strong ADX.",
        StrategyTemplateKeys.CryptoPairsArb => "Research rolling cointegrated crypto spreads. Pair selection must be causal; no survivorship.",
        StrategyTemplateKeys.XsRelativeStrength => "Research cross-sectional momentum ranks versus time-series momentum. Requires a universe snapshot.",
        StrategyTemplateKeys.RegimeStrategyRouter => "Deferred interpretable router. Must not be fit on OOS. Mapping regimes to families is a hypothesis.",
        StrategyTemplateKeys.FundingPriceMomentum => "Research funding together with price momentum, volume and trend as continuation vs contrarian hypotheses. Not a direction assumption.",
        StrategyTemplateKeys.FundingExtremeMomentumExhaustion => "Research funding extremes with weakening momentum and a reversal candle. Causal only.",
        StrategyTemplateKeys.BasisMeanReversion => "Research normalized basis z-score extremes as mean-reversion and as continuation, separately.",
        StrategyTemplateKeys.FundingBasisVwap => "Research funding extreme + basis extreme + VWAP deviation. Small parameter set only.",
        StrategyTemplateKeys.OiBreakoutConfirmation => "Research whether OI expansion adds incremental information to a volume-confirmed breakout. OI_SAMPLE_LIMITED.",
        StrategyTemplateKeys.VolSpikeEmaTrend => "ისტორიულად მორგებული BTCUSDT 15m კანდიდატი: volume spike + EMA21. არ არის validated alpha. SL 2.50% / TP 5.00% / 192 bar.",
        StrategyTemplateKeys.Bb202Break => "ისტორიულად მორგებული BTCUSDT 15m კანდიდატი: Bollinger (20,2) break. არ არის validated alpha. SL 4.00% / TP 5.00% / 192 bar.",
        StrategyTemplateKeys.ScalpEmaMomentum => "RESEARCH_ONLY scalping hypothesis: fast/slow EMA momentum on closed 1m–15m bars. Not a profit claim.",
        StrategyTemplateKeys.ScalpVwapReclaim => "RESEARCH_ONLY scalping hypothesis: session VWAP reclaim after a dip. Isolated book owns SL/TP.",
        StrategyTemplateKeys.ScalpVwapReversion => "RESEARCH_ONLY scalping hypothesis: ATR-scaled VWAP deviation fade. Isolated book owns SL/TP.",
        StrategyTemplateKeys.ScalpVwapBreakout => "RESEARCH_ONLY scalping hypothesis: VWAP-aligned breakout with relative volume.",
        StrategyTemplateKeys.ScalpBreakoutRetest => "RESEARCH_ONLY scalping hypothesis: Donchian break that fails and closes back inside.",
        StrategyTemplateKeys.ScalpLiqSweep => "RESEARCH_ONLY scalping hypothesis: failed swing sweep then close back through the level.",
        StrategyTemplateKeys.ScalpRsiPullback => "RESEARCH_ONLY scalping hypothesis: trend-aligned RSI pullback on short timeframes.",
        StrategyTemplateKeys.ScalpRsiReversion => "RESEARCH_ONLY scalping hypothesis: RSI extreme mean reversion outside strong ADX.",
        StrategyTemplateKeys.ScalpMacdMicro => "RESEARCH_ONLY scalping hypothesis: MACD histogram flip with slow EMA side.",
        StrategyTemplateKeys.ScalpBbReversion => "RESEARCH_ONLY scalping hypothesis: close returns inside Bollinger after a tag.",
        StrategyTemplateKeys.ScalpBbSqueeze => "RESEARCH_ONLY scalping hypothesis: Bollinger/Keltner squeeze then structure break.",
        StrategyTemplateKeys.ScalpAtrBreakout => "RESEARCH_ONLY scalping hypothesis: ATR-normalized momentum expansion.",
        StrategyTemplateKeys.ScalpAdxTrend => "RESEARCH_ONLY scalping hypothesis: Supertrend + EMA + ADX trend scalp.",
        StrategyTemplateKeys.ScalpRvolMomentum => "RESEARCH_ONLY scalping hypothesis: relative-volume spike with EMA side.",
        StrategyTemplateKeys.ScalpMarketStructure => "RESEARCH_ONLY scalping hypothesis: causal HH/HL or LH/LL continuation.",
        StrategyTemplateKeys.ScalpStochMomentum => "RESEARCH_ONLY scalping hypothesis: Stochastic %K/%D cross from an extreme.",
        StrategyTemplateKeys.ScalpMtf => "RESEARCH_ONLY scalping hypothesis: last-completed HTF trend with LTF trigger. No look-ahead.",
        StrategyTemplateKeys.ScalpSession => "RESEARCH_ONLY scalping hypothesis: UTC-session high/low context as a filter, not a hardcoded session pick.",
        StrategyTemplateKeys.ScalpTakerFlow => "RESEARCH_ONLY. Requires taker buy volume. Missing series = DATA_UNAVAILABLE.",
        StrategyTemplateKeys.ScalpPriceOi => "RESEARCH_ONLY. Requires open interest. Missing series = DATA_UNAVAILABLE.",
        StrategyTemplateKeys.ScalpFundingOi => "RESEARCH_ONLY. Requires funding + OI. Missing series = DATA_UNAVAILABLE.",
        StrategyTemplateKeys.ScalpBasis => "RESEARCH_ONLY. Requires mark/index basis. Missing series = DATA_UNAVAILABLE.",
        var pa when StrategyTemplateKeys.IsPriceAction(pa) =>
            "RESEARCH_ONLY price-action hypothesis. Causal confirmation only. Not a textbook LONG/SHORT. Isolated book owns SL/TP. LIVE off.",
        var near when StrategyTemplateKeys.IsNearMiss(near) =>
            "NEAR_MISS. Not validated and not a profit claim. Frozen Phase 8 definition. Paper and LIVE stay off until you arm them. Same Isolated risk and execution path.",
        var csr when StrategyTemplateKeys.IsCrossSectionalReversal(csr) =>
            "Repeatable cross-sectional reversal factor — not validated for trading.",
        _ => "ახალ ტრენდს იწყებს: სწრაფი EMA ნელს კვეთს, RSI ადასტურებს. მიზანი — მიმართულების ცვლილება, სუსტი გადაკვეთების გარეშე."
    };

    public static string DataDependencies(string templateKey) => StrategyTemplateKeys.Normalize(templateKey) switch
    {
        StrategyTemplateKeys.OiPriceMomentum => "OHLCV + OpenInterest. GET /futures/data/openInterestHist (~30d). Missing/short series = DATA_UNAVAILABLE. OI_HISTORICAL_DATA_LIMITATION.",
        StrategyTemplateKeys.FundingOiRegime or StrategyTemplateKeys.ScalpFundingOi => "OHLCV + Funding + OpenInterest. Funding = settled GET /fapi/v1/fundingRate at fundingTime. Missing = DATA_UNAVAILABLE.",
        StrategyTemplateKeys.FundingBasisRv => "OHLCV + Funding + MarkPrice + IndexPrice + Basis. Basis=(MarkClose-IndexClose)/IndexClose. Missing = DATA_UNAVAILABLE.",
        StrategyTemplateKeys.FundingOiReversal => "OHLCV + Funding + OpenInterest. Settled fundingTime only. Missing = DATA_UNAVAILABLE.",
        StrategyTemplateKeys.TakerFlowMomentum or StrategyTemplateKeys.ScalpTakerFlow => "OHLCV + TakerFlow. Binance kline index 9 taker buy base. Zero buy = DATA_UNAVAILABLE, not 0 imbalance.",
        StrategyTemplateKeys.MtfTrendStructure or StrategyTemplateKeys.ScalpMtf => "Entry timeframe OHLCV plus last completed HTF candles only.",
        StrategyTemplateKeys.OiPriceVolumeRegime or StrategyTemplateKeys.ScalpPriceOi => "OHLCV + OpenInterest. OI_HISTORICAL_DATA_LIMITATION if window longer than public hist.",
        StrategyTemplateKeys.CryptoPairsArb => "Multi-symbol OHLCV with causal pair selection windows. Single-book replay = DATA_UNAVAILABLE.",
        StrategyTemplateKeys.XsRelativeStrength => "Universe snapshot at each timestamp. Single-book replay = DATA_UNAVAILABLE.",
        var csr when StrategyTemplateKeys.IsCrossSectionalReversal(csr) =>
            "Closed 15m OHLCV on the BTCUSDT clock. Rank only symbols present at that timestamp. No forward return.",
        StrategyTemplateKeys.VpVwapReversion => "OHLCV and volume. Volume profile reconstructed from typical-price × volume bins.",
        StrategyTemplateKeys.FundingPriceMomentum => "OHLCV + Funding. Settled fundingTime only.",
        StrategyTemplateKeys.FundingExtremeMomentumExhaustion => "OHLCV + Funding. Settled fundingTime only.",
        StrategyTemplateKeys.BasisMeanReversion or StrategyTemplateKeys.ScalpBasis => "OHLCV + MarkPrice + IndexPrice + Basis. Matching closeTime only.",
        StrategyTemplateKeys.FundingBasisVwap => "OHLCV + Funding + Basis. Matching closeTime only.",
        StrategyTemplateKeys.OiBreakoutConfirmation => "OHLCV + OpenInterest. OI_HISTORICAL_DATA_LIMITATION (~29d). OI_SAMPLE_LIMITED.",
        var near when StrategyTemplateKeys.IsNearMiss(near) =>
            "Closed 5m entry plus last closed 1m, 3m, 15m, and 1h. Missing series is not fabricated.",
        _ => "Closed kline candles only."
    };

    public static string ResearchStatus(string templateKey) => StrategyTemplateKeys.Normalize(templateKey) switch
    {
        StrategyTemplateKeys.TakerFlowMomentum or StrategyTemplateKeys.ScalpTakerFlow => "DATA_UNAVAILABLE",
        StrategyTemplateKeys.OiPriceMomentum or StrategyTemplateKeys.ScalpPriceOi => "DATA_UNAVAILABLE",
        StrategyTemplateKeys.FundingOiRegime or StrategyTemplateKeys.ScalpFundingOi => "DATA_UNAVAILABLE",
        StrategyTemplateKeys.BasisMeanReversion or StrategyTemplateKeys.ScalpBasis => "RESEARCHING",
        StrategyTemplateKeys.FundingBasisRv => "RESEARCHING",
        StrategyTemplateKeys.FundingOiReversal => "RESEARCHING",
        StrategyTemplateKeys.OiPriceVolumeRegime => "RESEARCHING",
        StrategyTemplateKeys.FundingPriceMomentum => "RESEARCHING",
        StrategyTemplateKeys.FundingExtremeMomentumExhaustion => "RESEARCHING",
        StrategyTemplateKeys.FundingBasisVwap => "RESEARCHING",
        StrategyTemplateKeys.OiBreakoutConfirmation => "RESEARCHING",
        StrategyTemplateKeys.CryptoPairsArb => "DATA_UNAVAILABLE",
        StrategyTemplateKeys.XsRelativeStrength => "DATA_UNAVAILABLE",
        StrategyTemplateKeys.VolSpikeEmaTrend => StrategyValidationStatuses.HistoricallyFittedCandidate,
        StrategyTemplateKeys.Bb202Break => StrategyValidationStatuses.HistoricallyFittedCandidate,
        var near when StrategyTemplateKeys.IsNearMiss(near) => StrategyValidationStatuses.NearMiss,
        var csr when StrategyTemplateKeys.IsCrossSectionalReversal(csr) => StrategyValidationStatuses.Researching,
        var key when StrategyTemplateKeys.IsResearch(key) => "RESEARCHING",
        _ => "VALIDATION_PENDING"
    };

    private static string NearMissTitle(string templateKey)
    {
        var family = StrategyTemplateKeys.NearMissFamily(templateKey);
        var id = StrategyTemplateKeys.NearMissHypothesisId(templateKey);
        var variant = id.Split('|').ElementAtOrDefault(1) ?? "";
        return $"NEAR-MISS {family} {variant} 5m";
    }

    private static bool LooksLegacyEmaRsi(JsonElement root) =>
        root.TryGetProperty("entry", out _) && !root.TryGetProperty("template", out _);

    private static string Invariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? Str(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? Int(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static decimal? Dec(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var n) => n,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool Bool(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True ||
               (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
    }
}
