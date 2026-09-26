using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Strategies.PriceAction;

namespace TradingPlatform.News;

public static class NewsHypotheses
{
    public const string Momentum = "news_momentum";
    public const string Overreaction = "news_overreaction";
    public const string OiContinuation = "news_oi_continuation";
    public const string OiShortCover = "news_oi_short_cover";
    public const string OiLongLiquidation = "news_oi_long_liquidation";
    public const string Liquidation = "news_liquidation";
    public const string Macro = "news_macro";
    public const string Structure = "news_structure";
    public const string Taker = "news_taker";

    public static readonly string[] All =
    [
        Momentum, Overreaction, OiContinuation, OiShortCover, OiLongLiquidation, Liquidation, Macro, Structure, Taker
    ];
}

public sealed record NewsAblation(
    bool UseNews = true,
    bool UseVolume = true,
    bool UseRelativeVolume = true,
    bool UseVwap = true,
    bool UseTaker = true,
    bool UseOpenInterest = true,
    bool UseStructure = true)
{
    public static NewsAblation Full { get; } = new();

    public string Label { get; init; } = "full";
}

public sealed class NewsStrategyEngine : IStrategyEngine
{
    private readonly string _hypothesis;
    private readonly NewsAssetContext _asset;
    private readonly NewsOptions _options;
    private readonly NewsAblation _ablation;
    private readonly NewsFeatureProvider _features;
    private NewsFeatureBar[]? _aligned;
    private IReadOnlyList<MarketCandle>? _alignedCandles;

    public NewsStrategyEngine(
        string hypothesis,
        string symbol,
        IReadOnlyList<NewsEvent> events,
        NewsOptions options,
        NewsAblation? ablation = null,
        NewsAssetContext? asset = null)
    {
        _hypothesis = hypothesis;
        _asset = asset ?? new NewsAssetContext(symbol, NewsAssetCatalog.BaseFromSymbol(symbol));
        _options = options;
        _ablation = ablation ?? NewsAblation.Full;
        _features = new NewsFeatureProvider(events, options);
    }

    public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason)
    {
        var cache = new CausalIndicatorCache(context.ClosedCandles);
        return EvaluateAt(definition, context, cache, Math.Max(0, context.ClosedCandles.Count - 1), out reason);
    }

    public SignalType EvaluateAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index,
        out string reason)
    {
        var detail = EvaluateDetailAt(definition, context, cache, index);
        reason = detail.Reason;
        return detail.Signal;
    }

    public StrategySignalDetail EvaluateDetailAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index)
    {
        if (!_options.Enabled)
        {
            return new StrategySignalDetail(SignalType.NoAction, "News intelligence is disabled.", Status: "RESEARCHING");
        }

        if (_hypothesis == NewsHypotheses.Liquidation)
        {
            return new StrategySignalDetail(
                SignalType.NoAction,
                "Liquidation history is DATA_UNAVAILABLE. No liquidation series was fabricated.",
                Status: "DATA_UNAVAILABLE");
        }

        var candles = cache.Candles;
        if (!ReferenceEquals(_alignedCandles, candles))
        {
            _aligned = NewsFeatureAligner.Align(_features, candles, _asset);
            _alignedCandles = candles;
        }

        var feature = _aligned![Math.Clamp(index, 0, _aligned.Length - 1)];
        return NewsRuleEvaluator.Evaluate(_hypothesis, _ablation, _options, feature, candles, index, context, cache);
    }
}

public static class NewsRuleEvaluator
{
    public const double MomentumImpact = 0.7;
    public const double MomentumConfidence = 0.7;
    public const double MomentumNovelty = 0.6;

    public static StrategySignalDetail Evaluate(
        string hypothesis,
        NewsAblation ablation,
        NewsOptions options,
        NewsFeatureBar feature,
        IReadOnlyList<MarketCandle> candles,
        int index,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (index <= 0 || index >= candles.Count)
        {
            return Detail(SignalType.NoAction, "Not enough bars.", feature, hypothesis);
        }

        var wanted = hypothesis switch
        {
            NewsHypotheses.Momentum => Momentum(ablation, feature, candles, index, cache),
            NewsHypotheses.Overreaction => Overreaction(ablation, options, feature, candles, index, cache),
            NewsHypotheses.OiContinuation => OpenInterest(ablation, feature, candles, index, context, cache, continuation: true),
            NewsHypotheses.OiShortCover => OpenInterest(ablation, feature, candles, index, context, cache, shortCover: true),
            NewsHypotheses.OiLongLiquidation => OpenInterest(ablation, feature, candles, index, context, cache, longLiquidation: true),
            NewsHypotheses.Macro => Macro(ablation, feature),
            NewsHypotheses.Structure => Structure(ablation, feature, candles, index, cache),
            NewsHypotheses.Taker => Taker(ablation, feature, candles, index, cache),
            _ => SignalType.NoAction
        };

        if (context.HasOpenPosition)
        {
            var opposite = (wanted == SignalType.Buy && context.PositionSide == PositionSide.Short)
                || (wanted == SignalType.Sell && context.PositionSide == PositionSide.Long);
            return Detail(opposite ? SignalType.Exit : SignalType.Hold, "Position open.", feature, hypothesis);
        }

        return Detail(wanted, Reason(hypothesis, feature), feature, hypothesis);
    }

    private static SignalType Momentum(
        NewsAblation ablation,
        NewsFeatureBar feature,
        IReadOnlyList<MarketCandle> candles,
        int index,
        CausalIndicatorCache cache)
    {
        if (NewsBullish(ablation, feature) && ConfirmLong(ablation, candles, index, cache))
        {
            return SignalType.Buy;
        }

        if (NewsBearish(ablation, feature) && ConfirmShort(ablation, candles, index, cache))
        {
            return SignalType.Sell;
        }

        return SignalType.NoAction;
    }

    private static SignalType Overreaction(
        NewsAblation ablation,
        NewsOptions options,
        NewsFeatureBar feature,
        IReadOnlyList<MarketCandle> candles,
        int index,
        CausalIndicatorCache cache)
    {
        var z = cache.CloseZScore(20);
        var rvol = cache.RelativeVolume(20);
        var extremeDown = z[index] is <= -2m && rvol[index] is >= 2m && candles[index].Close < candles[index - 1].Close;
        var extremeUp = z[index] is >= 2m && rvol[index] is >= 2m && candles[index].Close > candles[index - 1].Close;
        var bearishNews = !ablation.UseNews || (feature.Direction == EventDirection.Bearish && feature.NewsImpact >= options.ExtremeImpact);
        var bullishNews = !ablation.UseNews || (feature.Direction == EventDirection.Bullish && feature.NewsImpact >= options.ExtremeImpact);
        if (bearishNews && extremeDown)
        {
            return SignalType.Buy;
        }

        if (bullishNews && extremeUp)
        {
            return SignalType.Sell;
        }

        return SignalType.NoAction;
    }

    private static SignalType OpenInterest(
        NewsAblation ablation,
        NewsFeatureBar feature,
        IReadOnlyList<MarketCandle> candles,
        int index,
        StrategyContext context,
        CausalIndicatorCache cache,
        bool continuation = false,
        bool shortCover = false,
        bool longLiquidation = false)
    {
        if (!ablation.UseOpenInterest || context.OpenInterest is not { } oi || index >= oi.Count || oi[index] is null || oi[index - 1] is null)
        {
            return SignalType.NoAction;
        }

        var oiUp = oi[index] > oi[index - 1];
        var oiDown = oi[index] < oi[index - 1];
        var priceUp = candles[index].Close > candles[index - 1].Close;
        var priceDown = candles[index].Close < candles[index - 1].Close;
        var volumeUp = !ablation.UseVolume || candles[index].Volume > candles[index - 1].Volume;
        if (continuation && NewsBullish(ablation, feature) && priceUp && oiUp && volumeUp)
        {
            return SignalType.Buy;
        }

        if (continuation && NewsBearish(ablation, feature) && priceDown && oiUp && volumeUp)
        {
            return SignalType.Sell;
        }

        if (shortCover && NewsBullish(ablation, feature) && priceUp && oiDown)
        {
            return SignalType.Buy;
        }

        if (longLiquidation && NewsBearish(ablation, feature) && priceDown && oiDown)
        {
            return SignalType.Sell;
        }

        return SignalType.NoAction;
    }

    private static SignalType Macro(NewsAblation ablation, NewsFeatureBar feature)
    {
        if (!feature.MacroEventFlag || feature.NewsConfidence < 0.6)
        {
            return SignalType.NoAction;
        }

        if (NewsBullish(ablation, feature))
        {
            return SignalType.Buy;
        }

        if (NewsBearish(ablation, feature))
        {
            return SignalType.Sell;
        }

        return SignalType.NoAction;
    }

    private static SignalType Structure(
        NewsAblation ablation,
        NewsFeatureBar feature,
        IReadOnlyList<MarketCandle> candles,
        int index,
        CausalIndicatorCache cache)
    {
        var book = cache.PriceAction();
        var sweepLow = book.ConfirmedAt(index, PatternKinds.LiquiditySweepLow).Any();
        var sweepHigh = book.ConfirmedAt(index, PatternKinds.LiquiditySweepHigh).Any();
        var bosBull = book.Structure[index].BosBull;
        var bosBear = book.Structure[index].BosBear;
        var volume = !ablation.UseRelativeVolume || cache.RelativeVolume(20)[index] is >= 1.5m;
        var structureLong = !ablation.UseStructure || (sweepLow && bosBull);
        var structureShort = !ablation.UseStructure || (sweepHigh && bosBear);
        if (NewsBullish(ablation, feature) && structureLong && volume)
        {
            return SignalType.Buy;
        }

        if (NewsBearish(ablation, feature) && structureShort && volume)
        {
            return SignalType.Sell;
        }

        return SignalType.NoAction;
    }

    private static SignalType Taker(
        NewsAblation ablation,
        NewsFeatureBar feature,
        IReadOnlyList<MarketCandle> candles,
        int index,
        CausalIndicatorCache cache)
    {
        var taker = cache.TakerImbalance()[index];
        if (ablation.UseTaker && taker is null)
        {
            return SignalType.NoAction;
        }

        var priceUp = candles[index].Close > candles[index - 1].Close;
        var priceDown = candles[index].Close < candles[index - 1].Close;
        if (NewsBullish(ablation, feature) && priceUp && taker is > 0m)
        {
            return SignalType.Buy;
        }

        if (NewsBearish(ablation, feature) && priceDown && taker is < 0m)
        {
            return SignalType.Sell;
        }

        return SignalType.NoAction;
    }

    private static bool NewsBullish(NewsAblation ablation, NewsFeatureBar feature) =>
        !ablation.UseNews
        || (feature.Direction == EventDirection.Bullish
            && feature.NewsConfidence >= MomentumConfidence
            && feature.NewsNovelty >= MomentumNovelty
            && feature.NewsImpact >= MomentumImpact
            && feature.NewsSentiment > 0.3);

    private static bool NewsBearish(NewsAblation ablation, NewsFeatureBar feature) =>
        !ablation.UseNews
        || (feature.Direction == EventDirection.Bearish
            && feature.NewsConfidence >= MomentumConfidence
            && feature.NewsNovelty >= MomentumNovelty
            && feature.NewsImpact >= MomentumImpact
            && feature.NewsSentiment < -0.3);

    private static bool ConfirmLong(NewsAblation ablation, IReadOnlyList<MarketCandle> candles, int index, CausalIndicatorCache cache)
    {
        if (candles[index].Close <= candles[index - 1].Close)
        {
            return false;
        }

        if (ablation.UseVolume && candles[index].Volume <= 0)
        {
            return false;
        }

        if (ablation.UseRelativeVolume && cache.RelativeVolume(20)[index] is not >= 1.5m)
        {
            return false;
        }

        if (ablation.UseVwap)
        {
            var vwap = cache.SessionVwap()[index];
            if (vwap is null || candles[index].Close <= vwap)
            {
                return false;
            }
        }

        if (ablation.UseTaker && cache.TakerImbalance()[index] is not > 0m)
        {
            return false;
        }

        return true;
    }

    private static bool ConfirmShort(NewsAblation ablation, IReadOnlyList<MarketCandle> candles, int index, CausalIndicatorCache cache)
    {
        if (candles[index].Close >= candles[index - 1].Close)
        {
            return false;
        }

        if (ablation.UseVolume && candles[index].Volume <= 0)
        {
            return false;
        }

        if (ablation.UseRelativeVolume && cache.RelativeVolume(20)[index] is not >= 1.5m)
        {
            return false;
        }

        if (ablation.UseVwap)
        {
            var vwap = cache.SessionVwap()[index];
            if (vwap is null || candles[index].Close >= vwap)
            {
                return false;
            }
        }

        if (ablation.UseTaker && cache.TakerImbalance()[index] is not < 0m)
        {
            return false;
        }

        return true;
    }

    private static string Reason(string hypothesis, NewsFeatureBar feature) =>
        "source=" + hypothesis
        + " eventId=" + (feature.EventId ?? "none")
        + " eventType=" + feature.EventType
        + " score=" + feature.NewsImpact.ToString("0.00")
        + " newsTimestamp=" + (feature.NewsTimestampUtc?.ToString("O") ?? "none");

    private static StrategySignalDetail Detail(SignalType signal, string reason, NewsFeatureBar feature, string hypothesis) =>
        new(
            signal,
            reason,
            feature.NewsTimestampUtc,
            Snapshot: new Dictionary<string, decimal?>
            {
                ["newsImpact"] = (decimal)feature.NewsImpact,
                ["newsSentiment"] = (decimal)feature.NewsSentiment,
                ["newsNovelty"] = (decimal)feature.NewsNovelty,
                ["newsConfidence"] = (decimal)feature.NewsConfidence
            },
            Status: hypothesis == NewsHypotheses.Liquidation ? "DATA_UNAVAILABLE" : "RESEARCHING");
}
