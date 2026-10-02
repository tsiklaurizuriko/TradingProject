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
    public const string BtcEma20Ema50Long = "btc_ema20_ema50_long";
    public const string TsMomentum285 = "ts_momentum_28_5";
    public const string BtcDailyMax10 = "btc_daily_max_10";
    public const string FlowZone = "flow_zone";
    public const string SqueezeWatch = "squeeze_watch";
    public const string ImpulseCatch = "impulse_catch";
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
    public const string FlatRange = "flat_range";
    public const string MacContrarian710 = "mac_contrarian_7_10";
    public const string ZigZagFade = "zigzag_fade";
    public const string DonchianV2 = "donchian_v2_55";
    public const string BinHv45 = "binhv45";
    public const string ClucMay72018 = "cluc_may72018";
    public const string CombinedBinHCluc = "combined_binh_cluc";
    public const string Hlhb = "hlhb";
    public const string FAdxSma = "fadx_sma";
    public const string TripleSupertrend = "triple_supertrend";
    public const string ImpulseCatchV2 = "impulse_catch_v2";
    public const string ZigZagFadeV2 = "zigzag_fade_v2";
    public const string TripleSupertrendV2 = "triple_supertrend_v2";
    public const string TsMomentumV2 = "ts_momentum_v2";
    public const string EmaCrossV2 = "ema_cross_v2";
    public const string FAdxSmaV2 = "fadx_sma_v2";
    public const string BinHv45V2 = "binhv45_v2";
    public const string ClucMay72018V2 = "cluc_may72018_v2";
    public const string ClucMay72018V2Thirty = "cluc_may72018_v2_30m";
    public const string CombinedBinHClucV2 = "combined_binh_cluc_v2";
    public const string DonchianBreakoutV2FourHour = "donchian_breakout_v2_4h";
    public const string DonchianBreakoutV2Daily = "donchian_breakout_v2_1d";
    public const string SqueezeWatchV2 = "squeeze_watch_v2";
    public const string FlowZoneV2 = "flow_zone_v2";
    public const string FlatRangeV2 = "flat_range_v2";
    public const string EmaRsiTrendV2 = "ema_rsi_trend_v2";
    public const string EmaRsiTrendV2Thirty = "ema_rsi_trend_v2_30m";
    public const string BollingerReversionV2 = "bollinger_reversion_v2";

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
        Bb202Break,
        BtcEma20Ema50Long,
        TsMomentum285,
        BtcDailyMax10
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

    public static readonly string[] Imported =
    [
        MacContrarian710,
        ZigZagFade,
        DonchianV2,
        BinHv45,
        ClucMay72018,
        CombinedBinHCluc,
        Hlhb,
        FAdxSma,
        TripleSupertrend
    ];

    public static readonly string[] Range = [FlatRange];

    public static readonly string[] Flow = [FlowZone, ImpulseCatch];

    public static readonly string[] Positioning = [SqueezeWatch];

    /// <summary>One implementation each. These ids are already in the family arrays. The old parallel v2 ids are aliases only.</summary>
    public static readonly string[] Canonical =
    [
        ImpulseCatch,
        ZigZagFade,
        TripleSupertrend,
        TsMomentum285,
        BtcEma20Ema50Long,
        FAdxSma,
        BinHv45,
        ClucMay72018,
        CombinedBinHCluc,
        DonchianBreakout,
        DonchianV2,
        SqueezeWatch,
        FlowZone,
        FlatRange,
        EmaRsiTrend,
        BollingerReversion
    ];

    public static readonly IReadOnlyDictionary<string, string> ObsoleteAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [ImpulseCatchV2] = ImpulseCatch,
        [ZigZagFadeV2] = ZigZagFade,
        [TripleSupertrendV2] = TripleSupertrend,
        [TsMomentumV2] = TsMomentum285,
        [EmaCrossV2] = BtcEma20Ema50Long,
        [FAdxSmaV2] = FAdxSma,
        [BinHv45V2] = BinHv45,
        [ClucMay72018V2] = ClucMay72018,
        [ClucMay72018V2Thirty] = ClucMay72018,
        [CombinedBinHClucV2] = CombinedBinHCluc,
        [DonchianBreakoutV2FourHour] = DonchianBreakout,
        [DonchianBreakoutV2Daily] = DonchianV2,
        [SqueezeWatchV2] = SqueezeWatch,
        [FlowZoneV2] = FlowZone,
        [FlatRangeV2] = FlatRange,
        [EmaRsiTrendV2] = EmaRsiTrend,
        [EmaRsiTrendV2Thirty] = EmaRsiTrend,
        [BollingerReversionV2] = BollingerReversion
    };

    public static readonly string[] Refactored = [];

    public static readonly string[] All = [.. Frozen, .. Research, .. NearMiss, .. CrossSectionalReversal, .. Range, .. Flow, .. Positioning, .. Imported];

    /// <summary>
    /// Operator catalog: PAPER or weak guidance only. Avoid/blocked templates stay in the engine
    /// for Frozen tests and any bots already running them.
    /// </summary>
    public static readonly string[] OperatorCatalog =
    [
        EmaRsiTrend,
        RsiPullback,
        BollingerReversion,
        DonchianBreakout,
        SupertrendEmaTrend,
        LiqSweepContinuation,
        VolSqueezeStructure,
        VwapBreakoutVolume,
        MarketStructureTrend,
        VolSpikeEmaTrend,
        Bb202Break,
        BtcEma20Ema50Long,
        TsMomentum285,
        BtcDailyMax10,
        FlowZone,
        SqueezeWatch,
        ImpulseCatch,
        FlatRange,
        MacContrarian710,
        ZigZagFade,
        DonchianV2,
        BinHv45,
        ClucMay72018,
        CombinedBinHCluc,
        Hlhb,
        FAdxSma,
        TripleSupertrend
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

    public static bool IsImported(string? key) =>
        Imported.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> TimeframesFor(string? key)
    {
        var template = CanonicalId(key);
        if (IsCanonical(template))
        {
            return CanonicalTimeframe(template);
        }
        if (IsScalping(template))
        {
            return ScalpingTimeframes;
        }

        if (template == BtcEma20Ema50Long)
        {
            return ["30m"];
        }

        if (template is TsMomentum285 or BtcDailyMax10)
        {
            return ["1d"];
        }

        if (template == ImpulseCatch)
        {
            return ["15m"];
        }

        if (template is FlowZone or SqueezeWatch or FlatRange)
        {
            return ["1h"];
        }

        if (template is VolSpikeEmaTrend or Bb202Break)
        {
            return ["15m"];
        }

        if (template == MacContrarian710)
        {
            return ["5m"];
        }

        if (template == ZigZagFade)
        {
            return ["30m"];
        }

        if (template == DonchianV2)
        {
            return ["1d"];
        }

        if (template == BinHv45)
        {
            return ["1m"];
        }

        if (template is ClucMay72018 or CombinedBinHCluc)
        {
            return ["5m"];
        }

        if (template == Hlhb)
        {
            return ["4h"];
        }

        if (template is FAdxSma or TripleSupertrend)
        {
            return ["1h"];
        }

        if (IsCrossSectionalReversal(template))
        {
            return ["15m"];
        }

        return SupportedTimeframes;
    }

    private static string[] CanonicalTimeframe(string template) => template switch
    {
        ImpulseCatch or ZigZagFade or EmaRsiTrend or BollingerReversion or ClucMay72018 or CombinedBinHCluc => ["15m"],
        BtcEma20Ema50Long => ["30m"],
        BinHv45 or FlowZone => ["5m"],
        TripleSupertrend or FAdxSma or SqueezeWatch or FlatRange => ["1h"],
        TsMomentum285 or DonchianV2 => ["1d"],
        DonchianBreakout => ["4h"],
        _ => ["15m"]
    };

    public static IReadOnlyList<string> DirectionsFor(string? key)
    {
        var template = CanonicalId(key);
        if (template is ImpulseCatch or TsMomentum285 or ClucMay72018)
        {
            return ["LONG"];
        }

        return template is BtcDailyMax10 or Hlhb
            ? ["LONG"]
            : SupportedDirections;
    }

    public static bool IsOperatorCatalog(string? key)
    {
        var raw = (key ?? "").Trim();
        return OperatorCatalog.Contains(raw, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// v2 rows an operator can select for research. They are not in the live operator catalog.
    /// Selection does not enable the strategy and does not turn on live trading.
    /// </summary>
    public static bool IsResearchWorkflow(string? key) => false;

    public static bool IsCanonical(string? key) =>
        Canonical.Contains(KeyText(key), StringComparer.OrdinalIgnoreCase);

    public static string KeyText(string? key) => (key ?? "").Trim().ToLowerInvariant();

    public static bool IsObsoleteAlias(string? key) =>
        ObsoleteAliases.ContainsKey(KeyText(key));

    public static string CanonicalId(string? key)
    {
        var raw = KeyText(key);
        return ObsoleteAliases.TryGetValue(raw, out var canonical) ? canonical : Normalize(key);
    }

    public static bool ContainsTemplate(IEnumerable<string?> existing, string key)
    {
        var normalized = Normalize(key);
        foreach (var row in existing)
        {
            if (string.Equals(Normalize(row), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

    public static bool IsFlatRange(string? key) =>
        string.Equals(Normalize(key), FlatRange, StringComparison.Ordinal);

    public static bool IsRefactored(string? key) => IsCanonical(key);

    public static bool IsResearchOnlyFamily(string? key) => IsScalping(key) || IsPriceAction(key) || IsCrossSectionalReversal(key);

    public static bool IsResearch(string? key) =>
        Research.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static bool IsAlpha(string? key) =>
        Alpha.Contains(Normalize(key), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> RequiredDatasets(string templateKey) => Normalize(templateKey) switch
    {
        TakerFlowMomentum or ScalpTakerFlow => ["OHLCV", "TakerFlow"],
        OiPriceMomentum or OiPriceVolumeRegime or OiBreakoutConfirmation or ScalpPriceOi => ["OHLCV", "OpenInterest"],
        FundingOiRegime or FundingOiReversal or ScalpFundingOi or SqueezeWatch or SqueezeWatchV2 => ["OHLCV", "Funding", "OpenInterest"],
        FlowZoneV2 => ["OHLCV", "TakerFlow", "OpenInterest", "CompletedHtf"],
        BinHv45V2 or ClucMay72018V2 or ClucMay72018V2Thirty or EmaRsiTrendV2 or EmaRsiTrendV2Thirty => ["OHLCV", "CompletedHtf"],
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
            or OiBreakoutConfirmation or SqueezeWatch => "FUTURES / FLOW",
        RegimeStrategyRouter => "ROUTER",
        BollingerReversion or FlatRange or BinHv45 or ClucMay72018 or CombinedBinHCluc => "MEAN REVERSION",
        DonchianBreakout => "BREAKOUT / TREND",
        Bb202Break => "BREAKOUT / TREND",
        VolSpikeEmaTrend or BtcEma20Ema50Long or TsMomentum285 or BtcDailyMax10 or FlowZone or ImpulseCatch or FAdxSma or TripleSupertrend
            or ImpulseCatchV2 or TsMomentumV2 or EmaCrossV2 or FAdxSmaV2 or TripleSupertrendV2 or FlowZoneV2 or EmaRsiTrendV2 or EmaRsiTrendV2Thirty => "TREND",
        ZigZagFadeV2 => "REVERSAL",
        BinHv45V2 or ClucMay72018V2 or ClucMay72018V2Thirty or CombinedBinHClucV2 or FlatRangeV2 or BollingerReversionV2 => "MEAN REVERSION",
        DonchianBreakoutV2FourHour or DonchianBreakoutV2Daily => "BREAKOUT / TREND",
        SqueezeWatchV2 => "FUTURES / FLOW",
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
    decimal OiExtremePercentile = 0.90m,
    int MaxImpulseAgeBars = 32);

public static class StrategyTemplates
{
    public static StrategyTemplateParams DefaultsFor(string templateKey, bool qualityOn)
    {
        var key = StrategyTemplateKeys.CanonicalId(templateKey);
        var quality = qualityOn
            ? new StrategyQualityParams(true, 20, 0.15m, 4m)
            : new StrategyQualityParams();
        var core = new StrategyTemplateParams(TemplateKey: key, Quality: quality);
        if (StrategyTemplateKeys.IsCanonical(key))
        {
            return RefactoredDefaults(core);
        }

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
            StrategyTemplateKeys.BtcEma20Ema50Long => core with
            {
                Timeframe = "30m",
                AllowedSide = StrategySides.Long,
                EmaFast = 20,
                EmaSlow = 50,
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.TsMomentum285 => core with
            {
                Timeframe = "1d",
                AllowedSide = StrategySides.Long,
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.BtcDailyMax10 => core with
            {
                Timeframe = "1d",
                AllowedSide = StrategySides.Long,
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.ImpulseCatch => core with
            {
                Timeframe = "15m",
                AllowedSide = StrategySides.Long,
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.FlowZone or StrategyTemplateKeys.SqueezeWatch => core with
            {
                Timeframe = "1h",
                AllowedSide = StrategySides.Both,
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.FlatRange => core with
            {
                Timeframe = "1h",
                AllowedSide = StrategySides.Both,
                EntryLookback = FlatRangeStrategy.Lookback,
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
            StrategyTemplateKeys.MacContrarian710 => core with
            {
                EmaFast = 7,
                EmaSlow = 10,
                PriceChangeThreshold = 0.01m,
                AllowedSide = StrategySides.Both,
                Timeframe = "5m",
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.ZigZagFade => core with
            {
                SwingLength = 14,
                PriceChangeThreshold = 2m,
                AtrPeriod = 14,
                AtrStopMultiplier = 1.5m,
                AllowedSide = StrategySides.Both,
                Timeframe = "30m",
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.DonchianV2 => core with
            {
                EntryLookback = 55,
                ExitLookback = 5,
                AtrPeriod = 14,
                AtrStopMultiplier = 1.5m,
                AllowedSide = StrategySides.Both,
                Timeframe = "1d",
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.BinHv45 => core with
            {
                BbPeriod = 40,
                BbStdDev = 2m,
                AllowedSide = StrategySides.Long,
                Timeframe = "1m",
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.ClucMay72018 or StrategyTemplateKeys.CombinedBinHCluc => core with
            {
                EmaSlow = 50,
                BbPeriod = 20,
                BbStdDev = 2m,
                RelativeVolumePeriod = 30,
                AllowedSide = StrategySides.Long,
                Timeframe = "5m",
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.FAdxSma => core with
            {
                EmaFast = 12,
                EmaSlow = 48,
                AdxPeriod = 14,
                MinimumAdx = 30m,
                AllowedSide = StrategySides.Both,
                Timeframe = "1h",
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.TripleSupertrend => core with
            {
                AllowedSide = StrategySides.Both,
                Timeframe = "1h",
                VolumeFilterEnabled = false
            },
            StrategyTemplateKeys.Hlhb => core with
            {
                EmaFast = 5,
                EmaSlow = 10,
                RsiPeriod = 10,
                RsiMinimum = 50m,
                AdxPeriod = 14,
                MinimumAdx = 25m,
                AllowedSide = StrategySides.Long,
                Timeframe = "4h",
                VolumeFilterEnabled = false
            },
            var refactored when StrategyTemplateKeys.IsRefactored(refactored) => RefactoredDefaults(core),
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

    private static StrategyTemplateParams RefactoredDefaults(StrategyTemplateParams core)
    {
        var key = core.TemplateKey;
        var shared = core with
        {
            VolumeFilterEnabled = false,
            TrendEmaPeriod = 200,
            AtrPeriod = 14,
            EmaFast = 20,
            EmaSlow = 50,
            SwingLength = 5,
            AllowedSide = StrategySides.Both
        };
        return key switch
        {
            StrategyTemplateKeys.ImpulseCatch => shared with
            {
                Timeframe = "15m",
                AllowedSide = StrategySides.Long,
                EntryLookback = 16,
                ExitLookback = 32,
                PriceDisplacementAtr = 1.5m,
                MinimumRelativeVolume = 1.5m,
                RelativeVolumePeriod = 20,
                StopAtrMultiplier = 1.8m,
                AtrStopMultiplier = 2.5m,
                MaxImpulseAgeBars = 32
            },
            StrategyTemplateKeys.ZigZagFade => shared with
            {
                Timeframe = "15m",
                SwingLength = 5,
                SweepDepthAtr = 0.3m,
                AtrStopMultiplier = 2m,
                StopAtrMultiplier = 1.5m
            },
            StrategyTemplateKeys.TripleSupertrend => shared with
            {
                Timeframe = "1h",
                SupertrendPeriod = 10,
                SupertrendMultiplier = 3m,
                StopAtrMultiplier = 2m,
                AtrStopMultiplier = 3m
            },
            StrategyTemplateKeys.TsMomentum285 => shared with
            {
                Timeframe = "1d",
                AllowedSide = StrategySides.Long,
                TrendEmaPeriod = 100,
                EntryLookback = 28,
                AtrStopMultiplier = 3m,
                StopAtrMultiplier = 3m
            },
            StrategyTemplateKeys.BtcEma20Ema50Long => shared with
            {
                Timeframe = "30m",
                MinimumAdx = 18m,
                StopAtrMultiplier = 1.5m,
                AtrStopMultiplier = 2.5m
            },
            StrategyTemplateKeys.FAdxSma => shared with
            {
                Timeframe = "1h",
                MinimumAdx = 20m,
                StopAtrMultiplier = 2m,
                AtrStopMultiplier = 2m
            },
            StrategyTemplateKeys.BinHv45 => shared with
            {
                Timeframe = "5m",
                BbPeriod = 40,
                BbStdDev = 2m,
                MinimumRelativeVolume = 1.2m,
                StopAtrMultiplier = 1.5m,
                SweepDepthAtr = 0.3m
            },
            StrategyTemplateKeys.ClucMay72018 => shared with
            {
                Timeframe = "15m",
                AllowedSide = StrategySides.Long,
                BbPeriod = 20,
                BbStdDev = 2m,
                RsiOversold = 35m,
                ExitLookback = 24,
                SweepDepthAtr = 0.3m
            },
            StrategyTemplateKeys.CombinedBinHCluc => shared with
            {
                Timeframe = "15m",
                BbPeriod = 20,
                BbStdDev = 2m,
                MinimumAdx = 20m,
                RsiOversold = 35m,
                PriceChangeThreshold = 0.005m,
                ExitLookback = 24,
                SweepDepthAtr = 0.3m,
                StopAtrMultiplier = 1.5m
            },
            StrategyTemplateKeys.DonchianBreakout => shared with
            {
                Timeframe = "4h",
                EntryLookback = 20,
                ExitLookback = 10,
                DonchianLength = 20,
                TrendEmaPeriod = 100,
                StopAtrMultiplier = 2m,
                AtrStopMultiplier = 3m
            },
            StrategyTemplateKeys.DonchianV2 => shared with
            {
                Timeframe = "1d",
                EntryLookback = 55,
                ExitLookback = 20,
                DonchianLength = 55,
                TrendEmaPeriod = 100,
                StopAtrMultiplier = 2m,
                AtrStopMultiplier = 3m
            },
            StrategyTemplateKeys.SqueezeWatch => shared with
            {
                Timeframe = "1h",
                FundingLookback = 720,
                FundingExtremePercentile = 0.90m,
                StopAtrMultiplier = 1.5m,
                SweepDepthAtr = 0.3m
            },
            StrategyTemplateKeys.FlowZone => shared with
            {
                Timeframe = "5m",
                EntryLookback = 20,
                StopAtrMultiplier = 1.5m,
                AtrStopMultiplier = 2m
            },
            StrategyTemplateKeys.FlatRange => shared with
            {
                Timeframe = "1h",
                EntryLookback = 24,
                ExitLookback = 48,
                MinimumAdx = 18m
            },
            StrategyTemplateKeys.EmaRsiTrend => shared with
            {
                Timeframe = "15m",
                RsiOversold = 40m,
                RsiMinimum = 50m,
                MinimumRelativeVolume = 1.2m,
                StopAtrMultiplier = 1.5m,
                AtrStopMultiplier = 2m
            },
            StrategyTemplateKeys.BollingerReversion => shared with
            {
                Timeframe = "15m",
                BbPeriod = 20,
                BbStdDev = 2m,
                MinimumAdx = 20m,
                RsiOversold = 35m,
                RsiOverbought = 70m,
                PriceChangeThreshold = 0.005m,
                StopAtrMultiplier = 1.5m,
                ExitLookback = 12
            },
            _ => shared
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
                "oiExtremePercentile": {{Invariant(p.OiExtremePercentile)}},
                "maxImpulseAgeBars": {{p.MaxImpulseAgeBars}}
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
                Dec(p, "oiExtremePercentile") ?? 0.90m,
                Int(p, "maxImpulseAgeBars") ?? 32);
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
        StrategyTemplateKeys.BtcEma20Ema50Long => "30m EMA Cross",
        StrategyTemplateKeys.TsMomentum285 => "1d Time-Series Momentum",
        StrategyTemplateKeys.BtcDailyMax10 => "1d BTC 10-day High",
        StrategyTemplateKeys.FlowZone => "Flow Zone",
        StrategyTemplateKeys.SqueezeWatch => "Squeeze Watch",
        StrategyTemplateKeys.ImpulseCatch => "Impulse Catch",
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
        StrategyTemplateKeys.FlatRange => "Flat Range",
        StrategyTemplateKeys.MacContrarian710 => "Contrarian SMA 7/10",
        StrategyTemplateKeys.ZigZagFade => "ZigZag Fade",
        StrategyTemplateKeys.DonchianV2 => "Donchian 55/5",
        StrategyTemplateKeys.BinHv45 => "BinHV45",
        StrategyTemplateKeys.ClucMay72018 => "Cluc May 2018",
        StrategyTemplateKeys.CombinedBinHCluc => "Combined BinH Cluc",
        StrategyTemplateKeys.Hlhb => "HLHB",
        StrategyTemplateKeys.FAdxSma => "ADX SMA Cross",
        StrategyTemplateKeys.TripleSupertrend => "Triple Supertrend",
        StrategyTemplateKeys.ImpulseCatchV2 => "Impulse Catch v2",
        StrategyTemplateKeys.ZigZagFadeV2 => "ZigZag Fade v2",
        StrategyTemplateKeys.TripleSupertrendV2 => "Triple Supertrend v2",
        StrategyTemplateKeys.TsMomentumV2 => "1d Time-Series Momentum v2",
        StrategyTemplateKeys.EmaCrossV2 => "30m EMA Cross v2",
        StrategyTemplateKeys.FAdxSmaV2 => "ADX SMA Cross v2",
        StrategyTemplateKeys.BinHv45V2 => "BinHV45 v2",
        StrategyTemplateKeys.ClucMay72018V2 => "Cluc May 2018 v2",
        StrategyTemplateKeys.ClucMay72018V2Thirty => "Cluc May 2018 v2 30m",
        StrategyTemplateKeys.CombinedBinHClucV2 => "Combined BinH Cluc v2",
        StrategyTemplateKeys.DonchianBreakoutV2FourHour => "Donchian 20/10 v2 4h",
        StrategyTemplateKeys.DonchianBreakoutV2Daily => "Donchian 55/20 v2 1d",
        StrategyTemplateKeys.SqueezeWatchV2 => "Squeeze Watch v2",
        StrategyTemplateKeys.FlowZoneV2 => "Flow Zone v2",
        StrategyTemplateKeys.FlatRangeV2 => "Flat Range v2",
        StrategyTemplateKeys.EmaRsiTrendV2 => "EMA RSI Trend v2",
        StrategyTemplateKeys.EmaRsiTrendV2Thirty => "EMA RSI Trend v2 30m",
        StrategyTemplateKeys.BollingerReversionV2 => "Bollinger Reversion v2",
        _ => "EMA RSI Trend"
    };

    public static string Blurb(string templateKey) => StrategyTemplateKeys.Normalize(templateKey) switch
    {
        StrategyTemplateKeys.MacdTrend => "იმპულსის გადაკვეთას მიყვება (MACD × სიგნალი, ჰისტოგრამა, ნელი EMA). მიზანი — ტრენდის გაგრძელება, არა მოკლე ხმაური.",
        StrategyTemplateKeys.EmaRsiTrend => "სწრაფი EMA ნელს კვეთს და RSI ადასტურებს. რისკი 0.5%, სტოპი 3%, ტეიკი 9%, 2x. ხმაურმა 2% აღარ უნდა გაწყვიტოს ტრენდი.",
        StrategyTemplateKeys.RsiPullback => "ტრენდში უკან დახევას იჭერს. რისკი 0.5%, სტოპი 2.5%, ტეიკი 5%, 2x.",
        StrategyTemplateKeys.BollingerReversion => "ზოლიდან შუაში ბრუნდება. რისკი 0.5%, სტოპი 2.5%, ტეიკი 2%, 2x. მოკლე ტეიკი, რადგან საშუალოსკენ ბრუნდება და 4%-ს ხშირად ვერ ასწრებს.",
        StrategyTemplateKeys.DonchianBreakout => "ბოლო N სანთლის მაღალ/დაბალ ზოლს არღვევს და იმ მიმართულებით შედის. მიზანი — ახალი ექსტრემის გაგრძელება.",
        StrategyTemplateKeys.TurtleTsm => "Systematic trend-following strategy using prior-range breakouts, EMA trend confirmation and ATR-based volatility control.",
        StrategyTemplateKeys.VwapPullbackTrend => "Trend-following pullback strategy using VWAP, EMA structure, RSI confirmation and volatility-aware stops.",
        StrategyTemplateKeys.VolatilityBreakout => "Volatility-compression breakout strategy using Bollinger width, ATR expansion and relative volume.",
        StrategyTemplateKeys.SupertrendEmaTrend => "Supertrend, EMA და ADX. რისკი 0.5%, სტოპი 3%, ტეიკი 9%, 2x.",
        StrategyTemplateKeys.OiPriceMomentum => "Futures-specific strategy researching conditional relationships between price movement, open interest, volume and trend.",
        StrategyTemplateKeys.FundingOiRegime => "Perpetual-futures strategy researching funding extremes together with price momentum and open-interest regimes.",
        StrategyTemplateKeys.VpVwapReversion => "Research whether VAL/VAH rejections revert toward POC/VWAP outside strong-trend regimes.",
        StrategyTemplateKeys.LiqSweepReversal => "Research failed breaks of causally confirmed swing highs/lows followed by a close back through the level.",
        StrategyTemplateKeys.LiqSweepContinuation => "სვიპი დონეს მიღმა რჩება და გრძელდება. რისკი 0.5%, სტოპი 3.5%, ტეიკი 7%, 2x.",
        StrategyTemplateKeys.FundingBasisRv => "Research funding and basis extremes as directional or relative-value hypotheses. Requires aligned funding/index.",
        StrategyTemplateKeys.FundingOiReversal => "Research extreme funding plus OI and price displacement as a reversal hypothesis. Requires aligned series.",
        StrategyTemplateKeys.TakerFlowMomentum => "Research persistent taker buy/sell imbalance with price and volume confirmation.",
        StrategyTemplateKeys.OiPriceVolumeRegime => "Research conditional expectancy of price/OI/volume state combinations without pre-assigned bull/bear labels.",
        StrategyTemplateKeys.VwapDeviationReversion => "Research ATR-scaled VWAP deviations with rejection and a trend-regime filter.",
        StrategyTemplateKeys.VwapBreakoutVolume => "VWAP-ის გარღვევა მოცულობით. რისკი 0.5%, სტოპი 3.5%, ტეიკი 7%, 2x.",
        StrategyTemplateKeys.FailedBreakoutReversal => "Research Donchian breakouts that fail to hold and close back inside the range.",
        StrategyTemplateKeys.VolSqueezeStructure => "შეკუმშვის შემდეგ გარღვევა. რისკი 0.5%, სტოპი 3.5%, ტეიკი 7%, 2x.",
        StrategyTemplateKeys.MarketStructureTrend => "ახალი სვინგის გაგრძელება. რისკი 0.5%, სტოპი 3.5%, ტეიკი 7%, 2x.",
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
        StrategyTemplateKeys.BtcEma20Ema50Long => "ყველა მონეტა, 30 წუთი, მხოლოდ ყიდვა. EMA(20) დახურულ ბარზე კვეთს EMA(50)-ს ზემოთ. გასვლა უკუ გადაკვეთაზე. სტოპი 1%. ტეიკი 3%. არ არის validated.",
        StrategyTemplateKeys.TsMomentum285 => "BTCUSDT, დღიური, მხოლოდ ყიდვა. 28 დღის ამონაგები საკუთარი ისტორიის ზედა მესამედშია — ლონგი. ხუთი დღე რჩება, შორტი არ არის. VAL-ზე ზრდა −11% იყო. Live ჩართვა Bots-ზეა, როცა LIVE რეჟიმი და API გასაღები გაქვს. თავისით არ ეშვება. რისკის წიგნი 1x, სტოპი 8% მხოლოდ ღობეა.",
        StrategyTemplateKeys.BtcDailyMax10 => "BTCUSDT, დღიური, მხოლოდ ყიდვა. დღე 10 დღის მაქსიმუმზე იხურება — მეორე დღეს ლონგი. შორტი არ არის. ამ ქეშზე IS −1%, VAL +8%, OOS −2% 12 bp ხარჯის შემდეგ. Live ჩართვა Bots-ზეა. თავისით არ ეშვება. რისკის წიგნი 1x, სტოპი 8% მხოლოდ ღობეა.",
        StrategyTemplateKeys.ImpulseCatch => "ყველა მონეტა, მხოლოდ ყიდვა. 15 წუთიან დახურულ სანთელზე შედის, როცა ფასმა ბოლო რამდენიმე საათში ახლახან 8% გადალახა და ეს სანთელი მაინც მაღლა დაიხურა. მოცულობა ბოლო საშუალოზე მაღალი უნდა იყოს. გასვლაა, როცა სანთელი 3%-ს უკან იხევს. სტოპი 6%, ტეიკი 20%, 2x, ერთდროულად 8. წარსულზე არ არის გაზომილი.",
        StrategyTemplateKeys.FlowZone => "ყველა მონეტა, 1 საათი. ბოლო 24 საათის ზედა მეოთხედში და taker-ის ყიდვა ბარის 62%-ზე მეტია და ღია პოზიცია იზრდება — ყიდვა. ქვედა მეოთხედში, ძლიერი გაყიდვა და პოზიციის ზრდა — გაყიდვა. Taker ან ღია პოზიცია თუ არ მოდის, ორდერი არ იგზავნება. გასვლას სიგნალი აკეთებს, როცა ნაკადი ზონას ტოვებს. წაგებაში ეს გასვლა მაშინვე ხდება. 0.20%-ზე პატარა მოგებაზე არა — ეს საკომისიოს შიგნითაა. სტოპი 4% და ტეიკი 15% მხოლოდ ღობეა, თუ ბოტი გაითიშა. წარსულზე არ არის გაზომილი.",
        StrategyTemplateKeys.SqueezeWatch => "1 საათი, ორივე მხარე. 24 საათში ფასი 3%-ზე ნაკლებს იცვლება, open interest მინიმუმ 15%-ით იზრდება და funding −0.10%-ზე დაბალია — ყიდვა (გადატვირთული შორტი). იგივე სიმშვიდე და open interest, funding +0.10%-ზე მაღალია — გაყიდვა (გადატვირთული ლონგი). Funding ან open interest თუ არ მოდის, ორდერი არ იგზავნება. ღია პოზიცია იხურება, როცა funding ამ ზღვარს ტოვებს ან ფასი შესვლის წინააღმდეგ 2%-ს გადის. რისკი 0.5%, სტოპი 4%, ტეიკი 8%, 2x, ერთდროულად 3.",
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
        StrategyTemplateKeys.FlatRange =>
            "ფლეტზე ზედა და ქვედა ზღვარი იკეტება. ლონგი ქვედა მეხუთედში, შორტი ზედა მეხუთედში. სტოპი შესვლის ზღვარია, ტეიკ-პროფიტი მოპირდაპირე ზღვარი. ზომა ისე ითვლება, რომ სტოპმა დაგეგმილი რისკი წაიღოს. 24 საათში იხურება. არ არის validated.",
        StrategyTemplateKeys.MacContrarian710 =>
            "MAc(7,10,0.01). 5m SMA(7)/SMA(10). სწრაფი საშუალო ნელზე 1%-ით მაღლაა — შორტი, 1%-ით დაბლაა — ლონგი. რისკი 0.5%, სტოპი 5%, ტეიკი 5%, 3x.",
        StrategyTemplateKeys.ZigZagFade =>
            "ZigZag fade. 30m, სვინგი 14, deviation 2% (BTC). ETH 6%, SOL 5%. რისკი 0.5%, სტოპი 4%, ტეიკი 8%, 3x.",
        StrategyTemplateKeys.DonchianV2 =>
            "Donchian v2 daily. შესვლა 55, გასვლა 5, ATR 1.5. რისკი 0.5%, სტოპი 8%, ტეიკი 30%, 1x.",
        StrategyTemplateKeys.BinHv45 =>
            "BinHV45, 1 წუთი, მხოლოდ ლონგი. Bollinger(40, 2) ქვედა ზოლის ქვეშ დახურვა პატარა ქვედა ჩრდილით. გასვლაა, როცა დახურვა 2.5% მოგებაში ან 2.5% წაგებაშია. ჩრდილი არ ხურავს — სტოპი და ტეიკი ბირჟაზეა.",
        StrategyTemplateKeys.ClucMay72018 =>
            "Cluc, 5 წუთი, მხოლოდ ლონგი. დახურვა EMA(50)-ის და typical-price Bollinger ქვედა ზოლის 98.5%-ის ქვეშ, მოცულობა წინა 30 ბარის საშუალოს 20-ჯერ ნაკლებია. გასვლა შუა ზოლზე. ტეიკი 1%, სტოპი 5%.",
        StrategyTemplateKeys.CombinedBinHCluc =>
            "BinHV45 ან Cluc, 5 წუთი, მხოლოდ ლონგი. გასვლა შუა ზოლზე მხოლოდ მოგებაში. ტეიკი 5%, სტოპი 5%.",
        StrategyTemplateKeys.Hlhb =>
            "HLHB, 4 საათი, მხოლოდ ლონგი. RSI(10) 50-ს კვეთს და EMA(5) EMA(10)-ს იმავე ბარზე, ADX 25-ზე მეტია. უკუ გადაკვეთა ხურავს. ცოცხალი ღობეა სტოპი 8%, ტეიკი 62%, მხოლოდ 1x. Hyperopt-ის 32% სტოპი აღარ გამოიყენება.",
        StrategyTemplateKeys.FAdxSma =>
            "Freqtrade FAdxSma, 1 საათი, ორივე მხარე. SMA(12) კვეთს SMA(48)-ს და ADX(14) 30-ზე მეტია. გასვლა, როცა ADX 30-ს ქვემოთ ჩამოდის. ტეიკი 5%, სტოპი 5%.",
        StrategyTemplateKeys.TripleSupertrend =>
            "Freqtrade FSupertrend, 1 საათი, ორივე მხარე. ლონგი სამი Supertrend-ის up-ზე (8/4, 9/7, 8/1), შორტი სამი down-ზე (16/1, 18/3, 18/6). ტეიკი 10%, სტოპი 8%, მხოლოდ 1x.",
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
        var imported when StrategyTemplateKeys.IsImported(imported) => "RESEARCHING",
        var refactored when StrategyTemplateKeys.IsRefactored(refactored) => StrategyExecutionRules.VersionStatus,
        StrategyTemplateKeys.VolSpikeEmaTrend => StrategyValidationStatuses.HistoricallyFittedCandidate,
        StrategyTemplateKeys.Bb202Break => StrategyValidationStatuses.HistoricallyFittedCandidate,
        StrategyTemplateKeys.BtcEma20Ema50Long => StrategyValidationStatuses.HistoricallyFittedCandidate,
        StrategyTemplateKeys.TsMomentum285 => StrategyValidationStatuses.HistoricallyFittedCandidate,
        StrategyTemplateKeys.BtcDailyMax10 => StrategyValidationStatuses.HistoricallyFittedCandidate,
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
