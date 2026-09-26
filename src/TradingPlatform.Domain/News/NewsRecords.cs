using TradingPlatform.Domain.Common;

namespace TradingPlatform.Domain.News;

public sealed class NewsArticle : Entity
{
    public string Provider { get; set; } = string.Empty;
    public string ProviderArticleId { get; set; } = string.Empty;
    public string CanonicalUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed class StoredNewsEvent : Entity
{
    public string DedupKey { get; set; } = string.Empty;
    public string? PrimaryAsset { get; set; }
    public string MarketScope { get; set; } = "Asset";
    public string Direction { get; set; } = "Unknown";
    public double Impact { get; set; }
    public double Confidence { get; set; }
    public string EventType { get; set; } = "Other";
    public DateTimeOffset PublishedAtUtc { get; set; }
    public string ArticleIds { get; set; } = string.Empty;
    public ICollection<NewsEventAsset> Assets { get; set; } = new List<NewsEventAsset>();
}

public sealed class NewsEventAsset : Entity
{
    public Guid StoredNewsEventId { get; set; }
    public StoredNewsEvent? StoredNewsEvent { get; set; }
    public string Asset { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public double Relevance { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class NewsTradingSignal : Entity
{
    public Guid StoredNewsEventId { get; set; }
    public StoredNewsEvent? StoredNewsEvent { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset SignalTimeUtc { get; set; }
    public string Direction { get; set; } = "NO_TRADE";
    public double NewsScore { get; set; }
    public double MarketScore { get; set; }
    public double FinalScore { get; set; }
    public double NewsImpact { get; set; }
    public double NewsConfidence { get; set; }
    public double Relevance { get; set; }
    public string StrategyName { get; set; } = "news_market_confirmation";
    public string Reason { get; set; } = string.Empty;
    public string MarketDetail { get; set; } = string.Empty;
    public string RiskDecision { get; set; } = "NotEvaluated";
    public string RiskReason { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal Notional { get; set; }
    public decimal Leverage { get; set; }
    public decimal Margin { get; set; }
    public decimal StopLossPrice { get; set; }
    public decimal TakeProfitPrice { get; set; }
    public string? OrderClientId { get; set; }
    public Guid? OrderId { get; set; }
    public string OrderDecision { get; set; } = "NOT_RUNNING";
    public string? ExchangeOrderId { get; set; }
    public bool BecameTrade { get; set; }
    public ICollection<NewsSignalOutcome> Outcomes { get; set; } = new List<NewsSignalOutcome>();
}

public sealed class NewsSignalOutcome : Entity
{
    public Guid NewsTradingSignalId { get; set; }
    public NewsTradingSignal? NewsTradingSignal { get; set; }
    public string Horizon { get; set; } = string.Empty;
    public decimal ReferencePrice { get; set; }
    public decimal? FuturePrice { get; set; }
    public decimal? ReturnPercent { get; set; }
    public DateTimeOffset? ObservedAtUtc { get; set; }
}

public sealed class NewsTradingSession : Entity
{
    public bool Running { get; set; }
    public string Mode { get; set; } = "Live";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public int UniverseCount { get; set; }
    public string? LastStatus { get; set; }
}
