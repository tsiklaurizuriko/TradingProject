namespace TradingPlatform.News;

public enum EventDirection
{
    Bullish,
    Bearish,
    Neutral,
    Mixed,
    Unknown
}

public enum NewsEventType
{
    Etf,
    Regulation,
    Exchange,
    SecurityIncident,
    Hack,
    Listing,
    Delisting,
    Partnership,
    ProtocolUpgrade,
    TokenUnlock,
    Funding,
    Acquisition,
    Bankruptcy,
    Legal,
    Adoption,
    InstitutionalFlow,
    Macro,
    InterestRate,
    Inflation,
    Employment,
    Fed,
    Sec,
    Geopolitical,
    Stablecoin,
    Mining,
    NetworkActivity,
    WhaleActivity,
    Other
}

public enum ExpectedHorizon
{
    Minutes,
    Hours,
    Days,
    Unknown
}

public enum TimestampPrecision
{
    Instant,
    DateOnly
}

public enum NewsRegime
{
    NoSignificantNews,
    Normal,
    HighImpact,
    Extreme
}

public enum MarketScope
{
    Asset,
    MultiAsset,
    Global
}

public sealed class RawNewsItem
{
    public string Id { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
    public DateTimeOffset RetrievedAtUtc { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? Author { get; set; }
    public string Language { get; set; } = "en";
    public string? OriginalSourceId { get; set; }
    public List<string> RelatedAssets { get; set; } = [];
    public List<string> RelatedProviderIds { get; set; } = [];
    public TimestampPrecision TimestampPrecision { get; set; } = TimestampPrecision.Instant;
    public decimal? Actual { get; set; }
    public decimal? Consensus { get; set; }
    public decimal? Previous { get; set; }
    public bool? IsScheduled { get; set; }
}

public sealed class AssetRelationship
{
    public string? Symbol { get; set; }
    public string BaseAsset { get; set; } = string.Empty;
    public string? ProviderAssetId { get; set; }
    public double Relevance { get; set; } = 1;
    public bool IsPrimary { get; set; }
}

public sealed record NewsAssetContext(string Symbol, string BaseAsset, string? ProviderAssetId = null);

public sealed class NewsArticleRef
{
    public string Id { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed class NewsEvent
{
    public string EventId { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
    public DateTimeOffset DetectedAtUtc { get; set; }
    public string? PrimaryAsset { get; set; }
    public List<string> Assets { get; set; } = [];
    public List<AssetRelationship> AffectedAssets { get; set; } = [];
    public MarketScope MarketScope { get; set; } = MarketScope.Asset;
    public List<string> ProviderAssetIds { get; set; } = [];
    public NewsEventType EventType { get; set; } = NewsEventType.Other;
    public EventDirection Direction { get; set; } = EventDirection.Unknown;
    public double Sentiment { get; set; }
    public double ImpactScore { get; set; }
    public double ConfidenceScore { get; set; }
    public double NoveltyScore { get; set; }
    public double SourceQualityScore { get; set; }
    public ExpectedHorizon ExpectedHorizon { get; set; } = ExpectedHorizon.Unknown;
    public int ExpectedHorizonMinutes { get; set; }
    public int SourceCount { get; set; }
    public List<NewsArticleRef> OriginalArticles { get; set; } = [];
    public TimestampPrecision TimestampPrecision { get; set; } = TimestampPrecision.Instant;
    public decimal? Actual { get; set; }
    public decimal? Consensus { get; set; }
    public decimal? Previous { get; set; }
    public decimal? Surprise { get; set; }
    public bool? IsScheduled { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed record NewsFeatureBar
{
    public double NewsSentiment { get; init; }
    public double NewsImpact { get; init; }
    public double NewsConfidence { get; init; }
    public double NewsNovelty { get; init; }
    public double NewsSourceQuality { get; init; }
    public int NewsEventCount { get; init; }
    public int BullishEventCount { get; init; }
    public int BearishEventCount { get; init; }
    public bool MacroEventFlag { get; init; }
    public bool RegulatoryEventFlag { get; init; }
    public bool SecurityIncidentFlag { get; init; }
    public bool EtfEventFlag { get; init; }
    public double NewsIntensity { get; init; }
    public int UniqueEventCount { get; init; }
    public int SourceCount { get; init; }
    public double NewsImpact5m { get; init; }
    public double NewsImpact15m { get; init; }
    public double NewsImpact30m { get; init; }
    public double NewsImpact1h { get; init; }
    public double NewsImpact4h { get; init; }
    public double NewsImpact24h { get; init; }
    public NewsRegime Regime { get; init; } = NewsRegime.NoSignificantNews;
    public EventDirection Direction { get; init; } = EventDirection.Unknown;
    public NewsEventType EventType { get; init; } = NewsEventType.Other;
    public string? EventId { get; init; }
    public DateTimeOffset? NewsTimestampUtc { get; init; }
    public bool LiquidationDataAvailable { get; init; }
    public bool OpenInterestAvailable { get; init; }
}
