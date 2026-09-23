using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Strategies.PriceAction;

namespace TradingPlatform.Research;

/// <summary>
/// Research-only final-five design. Production catalogs, Risk, and Execution are not registered here.
/// Definitions are fixed. They are not a threshold search and they are not a rename of a prior candidate.
/// </summary>
public static class FinalFiveCatalog
{
    public const string Version = "final-five-v1";

    public const string ThirdSymbolRule =
        "BTCUSDT and ETHUSDT are mandatory. The third symbol is the next name in the pre-existing volume-ranked ScalpingCatalog / Phase7PriceActionUniverse list. That name is BNBUSDT. Strategy profit was not an input. SOLUSDT is the following name and was not substituted.";

    public const string SurvivorRule =
        "Frozen before any OOS read. Applies only to the five primary timeframe ids. Aggregate IS trades >= 30, IS profit factor >= 0.90 or no losses, VALIDATION trades >= 20, VALIDATION profit factor > 1, VALIDATION net > 0, and VALIDATION trades on at least 2 of the 3 symbols. Other timeframes are robustness and cannot replace a primary id. OOS is not an input.";

    public static readonly string[] Symbols = ["BTCUSDT", "ETHUSDT", "BNBUSDT"];

    public static readonly string[] Timeframes = ["1m", "3m", "5m", "15m", "1h"];

    public static readonly string[] CostLabels =
    [
        ResearchCostLabels.Base,
        ResearchCostLabels.Mild,
        ResearchCostLabels.High,
        ResearchCostLabels.Stress
    ];

    public static IReadOnlyList<FinalFiveHypothesis> Primary { get; } =
    [
        new(
            "FF-SWEEP-HOURLY|15m",
            "Hourly-trend sweep reclaim",
            "15m",
            "LONG",
            "A completed 15m sweep of a swing low already confirmed at or before that bar, only while the last closed 1h structure bias is up.",
            "The 15m sweep confirmation itself. No 1m or 3m BOS is required.",
            "Last 1h candle with CloseTime <= the 15m CloseTime has bullish structure bias.",
            "Model B Isolated LOW: next-bar open, 2% stop, 4% target, 0.5% risk, 3x. No exit search.",
            "Prior sweep-strict added 1m confirmation, collapsed the sample, and failed walk-forward. This keeps one causal hourly filter and drops the stack. Short sweeps are omitted because earlier published sweep books showed the short side weaker. That choice is frozen here and is not revised after this run's OOS."),
        new(
            "FF-EXPANSION-BOS|15m",
            "Hourly-aligned expansion break",
            "15m",
            "WITH_1H",
            "A completed 15m bar whose range is at least ATR(14) and whose body is at least 0.55 of the range, with same-bar BOS.",
            "Same-bar causal BOS. No lower-timeframe confirmation.",
            "Trade only in the direction of the last closed 1h bias. Flat hourly bias skips the bar.",
            "Model B Isolated LOW book. No exit search.",
            "5m compression continuation reached OOS with a thin edge and a two-trade walk-forward. This asks a different question on 15m: does an expansion bar that breaks structure with the hourly trend survive costs, without a three-bar compression count."),
        new(
            "FF-RSI-QUIET|1h",
            "Quiet-range RSI reclaim",
            "1h",
            "BOTH",
            "1h RSI(14) reclaims 30 from below, or loses 70 from above.",
            "The reclaim bar itself.",
            "Same 1h bar must have ADX(14) below 20 and ATR(14) percentile below 40 over 50 bars. A trend bar is skipped.",
            "Model B Isolated LOW book. No exit search.",
            "Frozen RSI lost money because it faded trends. Wave-2 mean reversion did the same. This keeps the standard 30/70 levels and refuses the trade when the hour is trending or expanding. Thresholds are the existing indicator defaults, not a search."),
        new(
            "FF-FAILED-DOWN|15m",
            "Failed downside break, long only",
            "15m",
            "LONG",
            "A completed 15m failed break of the prior 20-bar low: the break bar closed back above that low.",
            "The existing causal failed-breakout confirmation on that 15m close.",
            "Last closed 1h bias is not bearish. One filter. No volume stack and no 1m BOS.",
            "Model B Isolated LOW book. No exit search.",
            "Failed-breakout baselines lost because they faded real trends, and the short side was the damage. This takes only the failed downside break, and only when the hour is not already down."),
        new(
            "FF-RANGE-RELEASE|1h",
            "Quiet-hour range release",
            "1h",
            "BREAK",
            "Eight prior completed 1h bars each have range below their own ATR(14). The current completed hour closes beyond that eight-bar high or low and its own range is at least ATR(14).",
            "The release close. No triangle geometry and no second timeframe.",
            "The eight-bar contraction is the context. There is no hourly overlay on top of the 1h entry.",
            "Model B Isolated LOW book. No exit search.",
            "The Phase 7 symmetrical triangle on 1h was the only pattern family with an OOS profit factor above 1, on about 40 trades, and it stays insufficient and is not retuned. This is a fixed eight-bar contraction break, not that triangle.")
    ];

    public static IReadOnlyList<FinalFiveRejected> RejectedBeforeRun { get; } =
    [
        new("FF-FUNDING-EXHAUST", "Funding plus structure exhaustion.", "Phase 4 funding books that looked strong on a short window failed on the long OOS (profit factor about 0.90 and 0.94)."),
        new("FF-OI-BREAK", "Open-interest confirmation of a break.", "Open-interest history is about 29 days. It is not treated as full-history evidence."),
        new("FF-TAKER-MOMENTUM", "Taker-flow momentum.", "Taker coverage was missing or the sample was a handful of trades. The series is not fabricated."),
        new("FF-PURE-RSI", "RSI extremes with no regime filter.", "The frozen RSI book was the least harmful of the five and still had profit factor 0.61. Repeating it adds nothing."),
        new("FF-EMA-TREND", "EMA cross trend.", "The frozen EMA book lost about 40% after costs."),
        new("FF-DONCHIAN-BOTH", "Donchian break both sides.", "The frozen Donchian book lost about 89%."),
        new("FF-VWAP-FADE", "VWAP extension fade.", "Wave-2 VWAP extension was rejected. OOS profit factor was about 0.63."),
        new("FF-SWEEP-1M-BOS", "Sweep plus 1m BOS plus 15m plus volume.", "Phase 8 strict sweep collapsed the sample and the walk-forward had 2 trades. The stack is the failure being avoided."),
        new("FF-COMPRESSION-5M-BOTH", "5m three-bar compression continuation, both sides.", "Already tested. Walk-forward had 2 trades and the short side was not robust. Not copied."),
        new("FF-SCALP-ADX", "5m ADX scalp.", "The promising book had 3 in-sample trades and then failed out of sample."),
        new("FF-RSI-IN-TREND", "RSI fade while ADX is high.", "That is the mean-reversion failure mode, not a new hypothesis."),
        new("FF-FLAG-STACK", "Flag plus displacement plus 3m plus 1h.", "Contextual flags lost the sample. ETH validation was negative. Another stack is rejected."),
        new("FF-MTF-BOS-ALL", "1h, 15m, 5m, and 1m BOS together.", "Phase 8 MTF strict failed the in-sample gate and was tiny on BTC, ETH, and BNB."),
        new("FF-TRIANGLE-RETUNE", "Retune the Phase 7 symmetrical triangle.", "That 1h book stays INSUFFICIENT_DATA. Thresholds are not searched."),
        new("FF-SHORT-SWEEP", "Short sweeps only.", "Published sweep-strict shorts had OOS profit factor 0.74. The side is excluded up front, not after this run."),
        new("FF-PAIRS", "Cross-coin relative value.", "The pair universe is not loaded. DATA_UNAVAILABLE."),
        new("FF-LIQUIDATIONS", "Liquidation cascade.", "Liquidations are not in the cache."),
        new("FF-ORDER-BOOK", "Depth imbalance.", "Order-book history is not available."),
        new("FF-BTC-FITTED", "The BTC 15m fitted Bollinger break.", "It was fit on BTC in-sample. It is not a new pre-registered hypothesis."),
        new("FF-ROUTER", "Route between the five after seeing results.", "A router fit on these outcomes would be a second selection. It is not run.")
    ];

    public static IReadOnlyList<string> AllCandidateIds { get; } =
        Primary.SelectMany(row => Timeframes.Select(tf => Id(row.CandidateId, tf))).ToArray();

    public static bool IsPrimary(string candidateId) =>
        Primary.Any(row => string.Equals(row.CandidateId, candidateId, StringComparison.Ordinal));

    public static string Id(string mechanismId, string timeframe) =>
        mechanismId.Split('|')[0] + "|" + timeframe;

    public static string Mechanism(string candidateId) => candidateId.Split('|')[0];
}

public sealed record FinalFiveHypothesis(
    string CandidateId,
    string Name,
    string EntryTimeframe,
    string SideRule,
    string Entry,
    string Confirmation,
    string Context,
    string Exit,
    string Why);

public sealed record FinalFiveRejected(string Id, string Name, string Reason);

public static class FinalFiveSignals
{
    public const int AtrPeriod = 14;
    public const int RsiPeriod = 14;
    public const int AdxPeriod = 14;
    public const int AtrPercentileLookback = 50;
    public const decimal QuietAdxMax = 20m;
    public const decimal QuietAtrPercentileMax = 40m;
    public const decimal RsiOversold = 30m;
    public const decimal RsiOverbought = 70m;
    public const decimal DisplacementBodyRatio = 0.55m;
    public const int QuietBars = 8;

    public static SignalType[] Build(string candidateId, CausalIndicatorCache entry, CausalIndicatorCache? hourly)
    {
        var mechanism = FinalFiveCatalog.Mechanism(candidateId);
        var count = entry.Candles.Count;
        var signals = new SignalType[count];
        var book = entry.PriceAction();
        var atr = entry.Atr(AtrPeriod);
        var hourlyBook = hourly?.PriceAction();
        var rsi = mechanism == "FF-RSI-QUIET" ? entry.Rsi(RsiPeriod) : null;
        var adx = mechanism == "FF-RSI-QUIET" ? entry.Adx(AdxPeriod) : null;
        var percentile = mechanism == "FF-RSI-QUIET" ? entry.AtrPercentile(AtrPeriod, AtrPercentileLookback) : null;
        for (var i = 0; i < count; i++)
        {
            var bias = hourlyBook is null ? 0 : BiasAt(hourly!, hourlyBook, entry.Candles[i].CloseTime);
            signals[i] = mechanism switch
            {
                "FF-SWEEP-HOURLY" => Sweep(book, i, bias),
                "FF-EXPANSION-BOS" => Expansion(book, atr, i, bias),
                "FF-RSI-QUIET" => QuietRsi(rsi!, adx!, percentile!, i),
                "FF-FAILED-DOWN" => FailedDown(book, i, bias),
                "FF-RANGE-RELEASE" => RangeRelease(entry.Candles, book, atr, i),
                _ => SignalType.NoAction
            };
        }

        return signals;
    }

    public static int BiasAt(CausalIndicatorCache hourly, PriceActionBook hourlyBook, DateTimeOffset close)
    {
        var candles = hourly.Candles;
        var lo = 0;
        var hi = candles.Count - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (candles[mid].CloseTime <= close)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found < 0 ? 0 : hourlyBook.Structure[found].Bias;
    }

    private static SignalType Sweep(PriceActionBook book, int index, int hourlyBias) =>
        hourlyBias > 0 && book.ConfirmedAt(index, PatternKinds.LiquiditySweepLow).Any()
            ? SignalType.Buy
            : SignalType.NoAction;

    private static SignalType Expansion(PriceActionBook book, IReadOnlyList<decimal?> atr, int index, int hourlyBias)
    {
        if (hourlyBias == 0 || atr[index] is not { } volatility || volatility <= 0m)
        {
            return SignalType.NoAction;
        }

        var geom = book.Geoms[index];
        if (geom.Range < volatility || geom.BodyRange < DisplacementBodyRatio)
        {
            return SignalType.NoAction;
        }

        var structure = book.Structure[index];
        if (hourlyBias > 0 && structure.BosBull && geom.Bullish)
        {
            return SignalType.Buy;
        }

        if (hourlyBias < 0 && structure.BosBear && geom.Bearish)
        {
            return SignalType.Sell;
        }

        return SignalType.NoAction;
    }

    private static SignalType QuietRsi(
        IReadOnlyList<decimal?> rsi,
        IReadOnlyList<decimal?> adx,
        IReadOnlyList<decimal?> percentile,
        int index)
    {
        if (index < 1
            || rsi[index] is not { } now
            || rsi[index - 1] is not { } previous
            || adx[index] is not { } trend
            || percentile[index] is not { } vol
            || trend >= QuietAdxMax
            || vol >= QuietAtrPercentileMax)
        {
            return SignalType.NoAction;
        }

        if (previous < RsiOversold && now >= RsiOversold)
        {
            return SignalType.Buy;
        }

        if (previous > RsiOverbought && now <= RsiOverbought)
        {
            return SignalType.Sell;
        }

        return SignalType.NoAction;
    }

    private static SignalType FailedDown(PriceActionBook book, int index, int hourlyBias)
    {
        if (hourlyBias < 0)
        {
            return SignalType.NoAction;
        }

        var failed = book.ConfirmedAt(index, PatternKinds.FailedBreakout)
            .Any(row => string.Equals(row.Direction, "BULLISH", StringComparison.Ordinal));
        return failed ? SignalType.Buy : SignalType.NoAction;
    }

    private static SignalType RangeRelease(
        IReadOnlyList<MarketCandle> candles,
        PriceActionBook book,
        IReadOnlyList<decimal?> atr,
        int index)
    {
        if (index < QuietBars || atr[index] is not { } releaseAtr || releaseAtr <= 0m || book.Geoms[index].Range < releaseAtr)
        {
            return SignalType.NoAction;
        }

        var high = decimal.MinValue;
        var low = decimal.MaxValue;
        for (var j = index - QuietBars; j < index; j++)
        {
            if (atr[j] is not { } prior || prior <= 0m || book.Geoms[j].Range >= prior)
            {
                return SignalType.NoAction;
            }

            high = Math.Max(high, candles[j].High);
            low = Math.Min(low, candles[j].Low);
        }

        if (candles[index].Close > high)
        {
            return SignalType.Buy;
        }

        if (candles[index].Close < low)
        {
            return SignalType.Sell;
        }

        return SignalType.NoAction;
    }
}
