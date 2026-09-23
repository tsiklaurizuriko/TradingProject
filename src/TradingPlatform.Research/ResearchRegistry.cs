using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Research;

public static class ResearchRegistry
{
    public static readonly DateTimeOffset CreatedAt = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<ResearchCandidate> All { get; } = Build();
    public static IReadOnlyList<ResearchCandidate> Wave2 { get; } = BuildWave2();
    public static IReadOnlyList<ResearchCandidate> Btc15mFitted { get; } = BuildBtc15mFitted();
    public static IReadOnlyList<ResearchCandidate> Scalping { get; } = BuildScalping();
    public static IReadOnlyList<ResearchCandidate> PriceAction { get; } = BuildPriceAction();

    public static ResearchCandidate? Find(string candidateId) =>
        All.Concat(Wave2).Concat(Btc15mFitted).Concat(Scalping).Concat(PriceAction).FirstOrDefault(c =>
            string.Equals(c.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.NativeKey, candidateId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.ParentStrategyId, candidateId, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<ResearchCandidate> Filter(string? candidateId, string? strategy, string? timeframe)
    {
        IEnumerable<ResearchCandidate> rows = All;
        if (!string.IsNullOrWhiteSpace(candidateId) || !string.IsNullOrWhiteSpace(strategy))
        {
            rows = All.Concat(Wave2).Concat(Btc15mFitted).Concat(Scalping).Concat(PriceAction);
        }

        if (!string.IsNullOrWhiteSpace(candidateId))
        {
            rows = rows.Where(c =>
                string.Equals(c.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.NativeKey, candidateId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.ParentStrategyId, candidateId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(strategy))
        {
            rows = rows.Where(c =>
                string.Equals(c.ParentStrategyId, strategy, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.ParentTemplateKey, strategy, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.NativeKey, strategy, StringComparison.OrdinalIgnoreCase)
                || c.CandidateId.StartsWith(strategy, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(timeframe))
        {
            rows = rows.Where(c => c.SupportedTimeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase));
        }

        return rows.ToArray();
    }

    private static IReadOnlyList<ResearchCandidate> Build()
    {
        var tf = StrategyTemplateKeys.SupportedTimeframes;
        var sides = StrategyTemplateKeys.SupportedDirections;
        var donchianGrid = new[] { 20, 30, 55 };
        return
        [
            Parent(
                "DONCHIAN-ATR-001",
                StrategyTemplateKeys.DonchianBreakout,
                1,
                "Breakouts may behave differently when ATR is expanding versus contracting.",
                ["Donchian20", "ATR14"],
                "Donchian N-bar break on the closed candle, only if ATR(14) is higher than the previous closed ATR.",
                "Opposite Donchian/EMA signal plus Isolated book SL/TP.",
                new ResearchFilters(RequireAtrExpansion: true),
                tf),
            Parent(
                "DONCHIAN-VOL-001",
                StrategyTemplateKeys.DonchianBreakout,
                1,
                "Breakout signals with unusually low relative volume may differ from normal/high-volume breakouts.",
                ["Donchian20", "RelativeVolume20"],
                "Donchian break only when relative volume (volume / SMA20) is at least 1.2.",
                "Opposite signal plus Isolated book SL/TP.",
                new ResearchFilters(MinRelativeVolume: 1.2m),
                tf),
            Parent(
                "DONCHIAN-TREND-001",
                StrategyTemplateKeys.DonchianBreakout,
                1,
                "Requiring EMA20/EMA50 alignment may reduce counter-trend Donchian breaks.",
                ["Donchian20", "EMA20", "EMA50"],
                "Donchian break only when EMA20 is on the same side of EMA50 as the break, with price versus slow EMA.",
                "Opposite signal plus Isolated book SL/TP.",
                new ResearchFilters(RequireEmaAlignment: true, RequirePriceVsSlowEma: true),
                tf),
            Parent(
                "EMA-RSI-HTF-001",
                StrategyTemplateKeys.EmaRsiTrend,
                1,
                "Higher-timeframe EMA alignment may reduce counter-trend EMA-RSI entries.",
                ["EMA20", "EMA50", "RSI14", "HTF-EMA"],
                "Frozen EMA RSI Trend signal, allowed only if the last closed higher-timeframe EMA20/EMA50 agrees.",
                "Opposite EMA cross plus Isolated book SL/TP.",
                new ResearchFilters(HigherTimeframe: "next"),
                tf),
            Parent(
                "EMA-RSI-ADX-001",
                StrategyTemplateKeys.EmaRsiTrend,
                1,
                "ADX trend-strength gating may skip EMA-RSI crosses in weak/range regimes.",
                ["EMA20", "EMA50", "RSI14", "ADX14"],
                "Frozen EMA RSI Trend signal only when ADX(14) is at least 20.",
                "Opposite EMA cross plus Isolated book SL/TP.",
                new ResearchFilters(MinAdx: 20),
                tf),
            Parent(
                "MACD-HTF-001",
                StrategyTemplateKeys.MacdTrend,
                1,
                "Higher-timeframe EMA alignment may reduce late or counter-trend MACD crosses.",
                ["MACD", "EMA50", "HTF-EMA"],
                "Frozen MACD Trend signal, allowed only if last closed HTF EMA20/EMA50 agrees.",
                "Opposite MACD cross plus Isolated book SL/TP.",
                new ResearchFilters(HigherTimeframe: "next"),
                tf),
            Parent(
                "RSI-PB-ADX-001",
                StrategyTemplateKeys.RsiPullback,
                1,
                "RSI pullbacks may need a minimum ADX so they occur inside an actual trend.",
                ["RSI14", "EMA50", "ADX14"],
                "Frozen RSI Pullback signal only when ADX(14) is at least 20.",
                "RSI recross of 50 / EMA reverse plus Isolated book SL/TP.",
                new ResearchFilters(MinAdx: 20),
                tf),
            Parent(
                "RSI-PB-ATR-001",
                StrategyTemplateKeys.RsiPullback,
                1,
                "Pullbacks may be more usable in moderate ATR percentile and worse in extremes.",
                ["RSI14", "EMA50", "ATR-percentile"],
                "Frozen RSI Pullback only when ATR percentile is between 0.30 and 0.80.",
                "RSI recross of 50 / EMA reverse plus Isolated book SL/TP.",
                new ResearchFilters(MinAtrPercentile: 0.30m, MaxAtrPercentile: 0.80m),
                tf),
            Native(
                "VWAP-PB-001",
                "vwap_pullback",
                1,
                "In a directional VWAP slope, reclaiming session VWAP may be a continuation entry.",
                ["SessionVWAP"],
                "LONG: VWAP slope > 0 and close reclaims VWAP. SHORT is the inverse. No extra EMA.",
                "Opposite reclaim or Isolated book SL/TP.",
                new ResearchFilters(),
                "vwap_reclaim",
                tf),
            Native(
                "VWAP-PB-EMA-001",
                "vwap_pullback",
                1,
                "VWAP reclaim plus EMA20/EMA50 agreement may skip counter-trend reclaims.",
                ["SessionVWAP", "EMA20", "EMA50"],
                "VWAP reclaim only when EMA20/EMA50 and price versus slow EMA agree with the side.",
                "Opposite reclaim or Isolated book SL/TP.",
                new ResearchFilters(RequireEmaAlignment: true, RequirePriceVsSlowEma: true),
                "vwap_reclaim",
                tf),
            Native(
                "ST-EMA-001",
                "supertrend_ema",
                1,
                "Trend-following may improve when Supertrend direction agrees with EMA20/EMA50 structure.",
                ["Supertrend10x3", "EMA20", "EMA50"],
                "LONG: Supertrend bullish AND EMA20 > EMA50 AND close > EMA50. SHORT inverse.",
                "Opposite Supertrend/EMA condition plus Isolated book SL/TP.",
                new ResearchFilters(RequireEmaAlignment: true, RequirePriceVsSlowEma: true),
                "supertrend_ema",
                tf),
            Parent(
                "VOL-BO-001",
                StrategyTemplateKeys.DonchianBreakout,
                1,
                "Donchian breaks during elevated ATR percentile and relative volume may differ from quiet breaks.",
                ["Donchian20", "ATR-percentile", "RelativeVolume20"],
                "Donchian break only when ATR percentile > 0.60 and relative volume > 1.2.",
                "Opposite signal plus Isolated book SL/TP.",
                new ResearchFilters(MinAtrPercentile: 0.60m, MinRelativeVolume: 1.2m),
                tf),
            Native(
                "TREND-PB-001",
                "trend_pullback",
                1,
                "Waiting for a pullback to EMA20 inside an EMA trend may improve continuation entries versus immediate crosses.",
                ["EMA20", "EMA50", "RSI14"],
                "LONG: EMA20 > EMA50, EMA50 slope > 0, bar pulls into EMA20 then closes above, RSI crosses up through 40. SHORT inverse.",
                "Opposite trend/pullback condition plus Isolated book SL/TP.",
                new ResearchFilters(RequireEmaAlignment: true, RequireEmaSlope: true),
                "trend_pullback",
                tf),
            Parent(
                "MTF-5M-15M-001",
                StrategyTemplateKeys.RsiPullback,
                1,
                "5m RSI pullback entries may be more robust when 15m EMA20/EMA50 agrees.",
                ["RSI14", "EMA50", "15m-EMA"],
                "RSI Pullback on 5m only if last closed 15m EMA20/EMA50 agrees.",
                "RSI recross of 50 plus Isolated book SL/TP.",
                new ResearchFilters(HigherTimeframe: "15m"),
                ["5m"]),
            Parent(
                "MTF-15M-1H-001",
                StrategyTemplateKeys.DonchianBreakout,
                1,
                "15m Donchian breaks may be more robust when 1h EMA20/EMA50 agrees.",
                ["Donchian20", "1h-EMA"],
                "Donchian break on 15m only if last closed 1h EMA20/EMA50 agrees.",
                "Opposite Donchian/EMA plus Isolated book SL/TP.",
                new ResearchFilters(HigherTimeframe: "1h"),
                ["15m"])
        ];

        ResearchCandidate Parent(
            string id,
            string parent,
            int version,
            string hypothesis,
            string[] indicators,
            string entry,
            string exit,
            ResearchFilters filters,
            IReadOnlyList<string> timeframes) =>
            new(
                id,
                parent,
                version,
                hypothesis,
                ResearchKinds.ParentFilter,
                parent,
                "",
                indicators,
                entry,
                exit,
                filters,
                new ResearchNativeParams(),
                timeframes,
                sides,
                donchianGrid,
                CreatedAt,
                "pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe",
                ResearchStatuses.Researching);

        ResearchCandidate Native(
            string id,
            string parentId,
            int version,
            string hypothesis,
            string[] indicators,
            string entry,
            string exit,
            ResearchFilters filters,
            string nativeKey,
            IReadOnlyList<string> timeframes) =>
            new(
                id,
                parentId,
                version,
                hypothesis,
                ResearchKinds.Native,
                null,
                nativeKey,
                indicators,
                entry,
                exit,
                filters,
                new ResearchNativeParams(),
                timeframes,
                sides,
                donchianGrid,
                CreatedAt,
                "pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe",
                ResearchStatuses.Researching);
    }

    private static IReadOnlyList<ResearchCandidate> BuildWave2()
    {
        var tf = StrategyTemplateKeys.SupportedTimeframes;
        var sides = StrategyTemplateKeys.SupportedDirections;
        var donchianGrid = Array.Empty<int>();
        ResearchCandidate Native(
            string id,
            string hypothesis,
            string[] indicators,
            string entry,
            string exit,
            string nativeKey) =>
            new(
                id,
                nativeKey,
                1,
                hypothesis,
                ResearchKinds.Native,
                null,
                nativeKey,
                indicators,
                entry,
                exit,
                new ResearchFilters(),
                new ResearchNativeParams(),
                tf,
                sides,
                donchianGrid,
                CreatedAt,
                "wave2 screen: 10 liquid USDT-M perpetuals; LOW Isolated $1000 / 0.5% / 3x; structural stops; OOS not used to retune",
                ResearchStatuses.Researching,
                "research-layer-2");

        return
        [
            Native(
                "W2-SWEEP-RECLAIM-001",
                "Stop-runs that immediately reclaim a confirmed swing with volume are liquidity grabs, not breakouts.",
                ["ConfirmedSwing", "ATR14", "RelativeVolume20"],
                "Sweep a confirmed swing, close back inside, close in reclaim direction, relative volume ≥ 1.2.",
                "Structural stop beyond the sweep wick; 2R target. Isolated LOW $ risk unchanged.",
                Wave2NativeEvaluator.SweepReclaim),
            Native(
                "W2-FAILED-BO-VOL-001",
                "A Donchian break that fails back inside on dying volume is a fade, not a continuation.",
                ["Donchian20", "RelativeVolume20", "ATR14"],
                "Prior bar closes beyond Donchian with rel vol ≥ 1.2; this bar closes back inside with rel vol < 1.0.",
                "Structural stop beyond the failed extreme; 2R target.",
                Wave2NativeEvaluator.FailedBreakoutVolume),
            Native(
                "W2-SQUEEZE-EXP-001",
                "ATR-percentile compression stores energy; the first volume Donchian break after expansion has directional expectancy.",
                ["ATR-percentile", "ATR14", "Donchian20", "RelativeVolume20"],
                "Prior ATR percentile ≤ 0.25, ATR expands, close breaks Donchian with rel vol ≥ 1.2.",
                "Structural stop beyond the break bar; 2R target.",
                Wave2NativeEvaluator.SqueezeExpansion),
            Native(
                "W2-VOL-EXHAUST-001",
                "A high-volume small-body bar after a 3-bar run is exhaustion, not continuation.",
                ["RelativeVolume20", "ATR14"],
                "Rel vol ≥ 2, body ≤ 35% of range, 3-bar directional run, close in the opposite half of the bar.",
                "Structural stop beyond the exhaustion wick; 2R target.",
                Wave2NativeEvaluator.VolumeExhaustion),
            Native(
                "W2-VWAP-EXT-001",
                "Session VWAP is a fair-value magnet after an ATR-normalized extension; reclaim continues toward VWAP.",
                ["SessionVWAP", "ATR14", "RelativeVolume20"],
                "Prior close ≥ 0.75 ATR beyond session VWAP, this close reclaims VWAP, rel vol ≥ 1.0.",
                "Stop beyond the extension extreme; TP 0.5 ATR through VWAP.",
                Wave2NativeEvaluator.VwapExtension),
            Native(
                "W2-BOS-PULLBACK-001",
                "After a causal break of structure, a pullback that holds the broken swing continues the new structure.",
                ["ConfirmedSwing", "ATR14"],
                "BOS in the last 8 closed bars, this bar tags the broken swing within 0.15 ATR and closes back through it.",
                "Structural stop beyond the pullback extreme; 2R target.",
                Wave2NativeEvaluator.BosPullback),
            Native(
                "W2-DISPLACE-001",
                "A ≥1.5 ATR displacement bar is informed flow; a later retrace into its midpoint continues that direction.",
                ["ATR14"],
                "Displacement bar in the last 6 closed bars; this bar retraces to the midpoint and closes in the displacement direction.",
                "Stop beyond the displacement extreme; TP 0.5 ATR beyond the displacement high/low.",
                Wave2NativeEvaluator.DisplacementRetrace),
            Native(
                "W2-REGIME-SWITCH-001",
                "Low realized-vol regimes fade Donchian extremes; high realized-vol regimes follow Donchian breaks with volume.",
                ["ATR-percentile", "Donchian20", "RelativeVolume20", "ATR14"],
                "ATR percentile ≤ 0.35: rejection at Donchian. ATR percentile ≥ 0.65: Donchian break with rel vol ≥ 1.2. Mid-vol: no trade.",
                "Structural stop beyond the event wick; 2R target.",
                Wave2NativeEvaluator.RegimeSwitch)
        ];
    }

    private static IReadOnlyList<ResearchCandidate> BuildBtc15mFitted()
    {
        var created = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);
        const string scope =
            "BTCUSDT 15m historically fitted. Discovery 2024-09-18 00:00 UTC → 2026-09-19 21:14 UTC. Not validated alpha. Forward = bars after that cutoff only.";

        ResearchCandidate Fitted(
            string id,
            string hypothesis,
            string[] indicators,
            string entry,
            string exit,
            decimal sl,
            decimal tp) =>
            new(
                id,
                id,
                1,
                hypothesis,
                ResearchKinds.Native,
                null,
                id,
                indicators,
                entry,
                exit,
                new ResearchFilters(),
                new ResearchNativeParams(),
                ["15m"],
                ["LONG", "SHORT"],
                [],
                created,
                scope,
                ResearchStatuses.HistoricallyFittedCandidate,
                "btc-15m-fitted-v1",
                "BTCUSDT",
                sl,
                tp,
                192);

        return
        [
            Fitted(
                Btc15mFittedEvaluator.VolSpikeEmaTrend,
                "HISTORICALLY_FITTED_CANDIDATE from the BTCUSDT 15m 2y search. Not validated. Do not retune.",
                ["RelativeVolume20", "EMA21"],
                "Long when relative volume>1.5 and close>EMA21; short when relative volume>1.5 and close<EMA21 (first bar of the spike).",
                "Isolated LOW book SL 2.50% / TP 5.00% / time-exit 192 bars (48 hours). Opposite signal does not flatten.",
                2.5m,
                5.0m),
            Fitted(
                Btc15mFittedEvaluator.Bb202Break,
                "HISTORICALLY_FITTED_CANDIDATE from the BTCUSDT 15m 2y search. Not validated. Do not retune.",
                ["Bollinger20x2"],
                "Long when close crosses above the upper Bollinger (20,2); short when close crosses below the lower band.",
                "Isolated LOW book SL 4.00% / TP 5.00% / time-exit 192 bars (48 hours). Opposite signal does not flatten.",
                4.0m,
                5.0m)
        ];
    }

    public static string NextHigherTimeframe(string timeframe) => timeframe switch
    {
        "1m" => "15m",
        "3m" => "15m",
        "5m" => "15m",
        "15m" => "1h",
        _ => ""
    };

    private static IReadOnlyList<ResearchCandidate> BuildScalping()
    {
        var tf = StrategyTemplateKeys.ScalpingTimeframes;
        var sides = StrategyTemplateKeys.SupportedDirections;
        return StrategyTemplateKeys.Scalping.Select(key =>
            new ResearchCandidate(
                $"SCALP-{key.ToUpperInvariant()}",
                key,
                1,
                StrategyTemplates.Blurb(key),
                ResearchKinds.ParentFilter,
                key,
                "",
                ["closed-OHLCV"],
                StrategyTemplates.Blurb(key),
                "Isolated LOW book SL/TP. Replay may honor MaxHoldBars; LIVE Isolated path is unchanged.",
                key == StrategyTemplateKeys.ScalpMtf
                    ? new ResearchFilters(HigherTimeframe: "15m")
                    : new ResearchFilters(),
                new ResearchNativeParams(),
                tf,
                sides,
                [],
                CreatedAt,
                "v1: BTCUSDT + ETHUSDT + volume-ranked top-20 USD-M; 1m/3m 90d, 5m/15m 365d",
                ResearchStatuses.Researching)).ToArray();
    }

    private static IReadOnlyList<ResearchCandidate> BuildPriceAction()
    {
        var tf = StrategyTemplateKeys.ScalpingTimeframes;
        var sides = StrategyTemplateKeys.SupportedDirections;
        return StrategyTemplateKeys.PriceAction.Select(key =>
            new ResearchCandidate(
                $"PA-{key.ToUpperInvariant()}",
                key,
                1,
                StrategyTemplates.Blurb(key),
                ResearchKinds.ParentFilter,
                key,
                "",
                ["closed-OHLCV", "causal-pattern-events"],
                StrategyTemplates.Blurb(key),
                "Isolated LOW book SL/TP. Pattern confirmation is an event, not a textbook LONG/SHORT. LIVE off.",
                new ResearchFilters(),
                new ResearchNativeParams(),
                tf,
                sides,
                [],
                CreatedAt,
                "v1: OHLCV + volume + causal indicators. Futures series optional when present. Cup & Handle NOT_IMPLEMENTED.",
                ResearchStatuses.Researching)).ToArray();
    }
}
