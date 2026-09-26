using TradingPlatform.Domain.Market;

namespace TradingPlatform.News;

public interface INewsFeatureProvider
{
    NewsFeatureBar FeaturesAt(DateTimeOffset candleOpenTime, TimeSpan barLength, string symbol);
}

public sealed class NewsFeatureProvider : INewsFeatureProvider
{
    private readonly IReadOnlyList<NewsEvent> _events;
    private readonly NewsOptions _options;

    public NewsFeatureProvider(IReadOnlyList<NewsEvent> events, NewsOptions options)
    {
        _events = events.OrderBy(item => item.PublishedAtUtc).ToList();
        _options = options;
    }

    public NewsFeatureBar FeaturesAt(DateTimeOffset candleOpenTime, TimeSpan barLength, string symbol) =>
        FeaturesAt(candleOpenTime, barLength, new NewsAssetContext(symbol, NewsAssetCatalog.BaseFromSymbol(symbol)));

    public NewsFeatureBar FeaturesAt(DateTimeOffset candleOpenTime, TimeSpan barLength, NewsAssetContext asset)
    {
        var visible = _events.Where(item => VisibleTo(item, candleOpenTime, barLength, asset.BaseAsset)).ToList();
        var primary = visible
            .Select(item => (Event: item, Weight: Decay(item, candleOpenTime) * NewsAssetCatalog.Relevance(item, asset.BaseAsset)))
            .OrderByDescending(item => item.Weight * item.Event.ImpactScore)
            .ToList();
        var lead = primary.FirstOrDefault();
        var impact24 = WindowImpact(visible, candleOpenTime, 1440, asset.BaseAsset);
        return new NewsFeatureBar
        {
            NewsSentiment = Weighted(primary, item => item.Sentiment),
            NewsImpact = impact24,
            NewsConfidence = Weighted(primary, item => item.ConfidenceScore),
            NewsNovelty = Weighted(primary, item => item.NoveltyScore),
            NewsSourceQuality = Weighted(primary, item => item.SourceQualityScore),
            NewsEventCount = visible.Count,
            BullishEventCount = visible.Count(item => item.Direction == EventDirection.Bullish),
            BearishEventCount = visible.Count(item => item.Direction == EventDirection.Bearish),
            MacroEventFlag = visible.Any(IsMacro),
            RegulatoryEventFlag = visible.Any(item => item.EventType is NewsEventType.Regulation or NewsEventType.Sec or NewsEventType.Legal),
            SecurityIncidentFlag = visible.Any(item => item.EventType is NewsEventType.Hack or NewsEventType.SecurityIncident),
            EtfEventFlag = visible.Any(item => item.EventType == NewsEventType.Etf),
            NewsIntensity = visible.Sum(item => item.ImpactScore * Decay(item, candleOpenTime) * NewsAssetCatalog.Relevance(item, asset.BaseAsset)),
            UniqueEventCount = visible.Count,
            SourceCount = visible.Sum(item => item.SourceCount),
            NewsImpact5m = WindowImpact(visible, candleOpenTime, 5, asset.BaseAsset),
            NewsImpact15m = WindowImpact(visible, candleOpenTime, 15, asset.BaseAsset),
            NewsImpact30m = WindowImpact(visible, candleOpenTime, 30, asset.BaseAsset),
            NewsImpact1h = WindowImpact(visible, candleOpenTime, 60, asset.BaseAsset),
            NewsImpact4h = WindowImpact(visible, candleOpenTime, 240, asset.BaseAsset),
            NewsImpact24h = impact24,
            Regime = RegimeOf(impact24, visible.Count),
            Direction = lead.Event?.Direction ?? EventDirection.Unknown,
            EventType = lead.Event?.EventType ?? NewsEventType.Other,
            EventId = lead.Event?.EventId,
            NewsTimestampUtc = lead.Event?.PublishedAtUtc,
            LiquidationDataAvailable = false,
            OpenInterestAvailable = false
        };
    }

    public static bool IsVisible(NewsEvent item, DateTimeOffset candleOpenTime, TimeSpan barLength, string symbol) =>
        VisibleTo(item, candleOpenTime, barLength, NewsAssetCatalog.BaseFromSymbol(symbol));

    private static bool VisibleTo(NewsEvent item, DateTimeOffset candleOpenTime, TimeSpan barLength, string baseAsset)
    {
        if (item.TimestampPrecision == TimestampPrecision.DateOnly)
        {
            if (barLength < TimeSpan.FromDays(1))
            {
                return false;
            }

            return item.PublishedAtUtc.UtcDateTime.Date.AddDays(1) <= candleOpenTime.UtcDateTime
                && NewsAssetCatalog.Relevance(item, baseAsset) > 0;
        }

        if (item.PublishedAtUtc > candleOpenTime)
        {
            return false;
        }

        return NewsAssetCatalog.Relevance(item, baseAsset) > 0;
    }

    public double Decay(NewsEvent item, DateTimeOffset candleOpenTime) =>
        DecayWeight(_options.DecayLambdaPerMinute, item.PublishedAtUtc, candleOpenTime);

    public static double DecayWeight(double lambdaPerMinute, DateTimeOffset publishedAtUtc, DateTimeOffset atUtc)
    {
        var age = Math.Max(0, (atUtc - publishedAtUtc).TotalMinutes);
        return Math.Exp(-lambdaPerMinute * age);
    }

    private double WindowImpact(IReadOnlyList<NewsEvent> visible, DateTimeOffset candleOpenTime, int minutes, string baseAsset)
    {
        return visible
            .Where(item => (candleOpenTime - item.PublishedAtUtc).TotalMinutes <= minutes)
            .Sum(item => item.ImpactScore * Decay(item, candleOpenTime) * NewsAssetCatalog.Relevance(item, baseAsset));
    }

    private NewsRegime RegimeOf(double impact, int count)
    {
        if (count == 0 || impact < Math.Max(_options.MinimumImpact, 0.05))
        {
            return NewsRegime.NoSignificantNews;
        }

        if (impact >= _options.ExtremeImpact)
        {
            return NewsRegime.Extreme;
        }

        return impact >= _options.HighImpact ? NewsRegime.HighImpact : NewsRegime.Normal;
    }

    private static bool IsMacro(NewsEvent item) =>
        item.EventType is NewsEventType.Macro
            or NewsEventType.InterestRate
            or NewsEventType.Inflation
            or NewsEventType.Employment
            or NewsEventType.Fed
            or NewsEventType.Sec
            or NewsEventType.Geopolitical;

    private static double Weighted(List<(NewsEvent Event, double Weight)> rows, Func<NewsEvent, double> selector)
    {
        var weight = rows.Sum(row => row.Weight);
        return weight <= 0 ? 0 : rows.Sum(row => selector(row.Event) * row.Weight) / weight;
    }
}

public static class NewsFeatureAligner
{
    public static NewsFeatureBar[] Align(
        INewsFeatureProvider provider,
        IReadOnlyList<MarketCandle> candles,
        NewsAssetContext asset)
    {
        var rows = new NewsFeatureBar[candles.Count];
        var concrete = provider as NewsFeatureProvider;
        for (var i = 0; i < candles.Count; i++)
        {
            var length = candles[i].CloseTime - candles[i].OpenTime;
            rows[i] = concrete is null
                ? provider.FeaturesAt(candles[i].OpenTime, length, asset.Symbol)
                : concrete.FeaturesAt(candles[i].OpenTime, length, asset);
        }

        return rows;
    }
}
