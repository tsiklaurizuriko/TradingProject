using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Research;

public static class ResearchRegistry
{
    public static readonly DateTimeOffset CreatedAt = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<ResearchCandidate> All { get; } = Build();

    public static ResearchCandidate? Find(string candidateId) =>
        All.FirstOrDefault(c => string.Equals(c.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<ResearchCandidate> Filter(string? candidateId, string? strategy, string? timeframe)
    {
        IEnumerable<ResearchCandidate> rows = All;
        if (!string.IsNullOrWhiteSpace(candidateId))
        {
            rows = rows.Where(c => string.Equals(c.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(strategy))
        {
            rows = rows.Where(c =>
                string.Equals(c.ParentStrategyId, strategy, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.ParentTemplateKey, strategy, StringComparison.OrdinalIgnoreCase)
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

    public static string NextHigherTimeframe(string timeframe) => timeframe switch
    {
        "5m" => "15m",
        "15m" => "1h",
        _ => ""
    };
}
