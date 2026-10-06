namespace TradingPlatform.News;

public sealed class NewsOptions
{
    public const string SectionName = "News";

    public bool Enabled { get; set; }

    public string Mode { get; set; } = "Historical";

    public int PollMinutes { get; set; } = 5;

    public string[] Providers { get; set; } = [];

    public bool CacheEnabled { get; set; } = true;

    public bool DeduplicationEnabled { get; set; } = true;

    public bool AiClassificationEnabled { get; set; }

    public double MinimumImpact { get; set; }

    public double MinimumConfidence { get; set; }

    /// <summary>Published default. Half-life of 60 minutes. Not fit on out-of-sample results.</summary>
    public double DecayLambdaPerMinute { get; set; } = 0.011552453;

    public int ClusterWindowMinutes { get; set; } = 360;

    public double TitleSimilarity { get; set; } = 0.8;

    public double HighImpact { get; set; } = 0.65;

    public double ExtremeImpact { get; set; } = 0.85;

    public string StorePath { get; set; } = "artifacts/data/news";

    public string? CoinGeckoApiKey { get; set; }

    public string? CoinDeskApiKey { get; set; }

    public string? CryptoPanicToken { get; set; }

    /// <summary>CryptoPanic path segment: growth or enterprise. The free developer plan ended 2026-04-01.</summary>
    public string CryptoPanicPlan { get; set; } = "growth";

    public string? FredApiKey { get; set; }

    public Dictionary<string, int> ProviderPollMinutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<NewsFeedOption> RssFeeds { get; set; } = [];

    public List<NewsFeedOption> OfficialFeeds { get; set; } = [];

    public NewsMarketStrategyOptions Strategy { get; set; } = new();

    public NewsAiOptions Ai { get; set; } = new();
}

public sealed class NewsAiOptions
{
    public bool Enabled { get; set; }

    public string Provider { get; set; } = "openai-compatible";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Set with News__Ai__ApiKey. An empty value never calls a model.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string FastModel { get; set; } = "gpt-4.1-mini";

    /// <summary>When false, only <see cref="FastModel"/> is called. A second model is not used for important or failed events.</summary>
    public bool UseStrongModel { get; set; }

    public string StrongModel { get; set; } = "gpt-4.1";

    public int TimeoutSeconds { get; set; } = 20;

    public string PromptVersion { get; set; } = "news-deep-v1";

    public int StrongImpactThreshold { get; set; } = 70;

    public int MaxAlreadyPricedIn { get; set; } = 55;

    public double MaxFavorableMovePercent { get; set; } = 3;

    public double MinExpectedEdgePercent { get; set; } = 0.25;

    public double MaxSpreadPercent { get; set; } = 0.08;

    public decimal MinQuoteVolumeUsdt { get; set; } = 5_000_000m;

    public int MinSourceReliability { get; set; } = 60;

    public int MinSecondaryConfidence { get; set; } = 85;

    public int MinSecondaryImpact { get; set; } = 80;

    /// <summary>Matches <c>RiskEngine.DefaultTakerFeePercent</c>. Percent points, so 0.04 is 0.04%.</summary>
    public double TakerFeePercent { get; set; } = 0.04;

    /// <summary>Matches <c>RiskEngine.DefaultSlippagePercent</c>. Percent points.</summary>
    public double SlippagePercent { get; set; } = 0.05;

    public bool UseJsonResponseFormat { get; set; } = true;

    public bool HasKey() => !string.IsNullOrWhiteSpace(ApiKey);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!Enabled)
        {
            return errors;
        }

        if (TimeoutSeconds is < 5 or > 120)
        {
            errors.Add("News AI timeout must be between 5 and 120 seconds.");
        }

        if (string.IsNullOrWhiteSpace(Provider) || string.IsNullOrWhiteSpace(FastModel) || string.IsNullOrWhiteSpace(PromptVersion) || (UseStrongModel && string.IsNullOrWhiteSpace(StrongModel)))
        {
            errors.Add("News AI provider, models, and prompt version are required when AI is enabled.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            errors.Add("News AI base URL must be an absolute http(s) URL.");
        }

        if (StrongImpactThreshold is < 0 or > 100 || MaxAlreadyPricedIn is < 0 or > 100 || MinSourceReliability is < 0 or > 100
            || MinSecondaryConfidence is < 0 or > 100 || MinSecondaryImpact is < 0 or > 100)
        {
            errors.Add("News AI scores must be between 0 and 100.");
        }

        if (MaxFavorableMovePercent <= 0 || MinExpectedEdgePercent < 0 || MaxSpreadPercent <= 0 || MinQuoteVolumeUsdt < 0
            || TakerFeePercent < 0 || SlippagePercent < 0)
        {
            errors.Add("News AI cost and move thresholds must be zero or positive, and spread and move caps must be positive.");
        }

        return errors;
    }
}

public sealed class NewsFeedOption
{
    public string Publisher { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}

public sealed class NewsMarketStrategyOptions
{
    public bool Enabled { get; set; } = true;

    public double MinNewsConfidence { get; set; } = 0.75;

    public double MinNewsImpact { get; set; } = 0.70;

    public double MinRelevance { get; set; } = 0.35;

    public double MinTotalScore { get; set; } = 75;

    public double MinNewsScore { get; set; } = 25;

    public double MinMarketScore { get; set; } = 20;

    public int MaxNewsAgeMinutes { get; set; } = 60;

    public double ConflictMarketScore { get; set; } = 25;

    public int SignalCooldownMinutes { get; set; } = 30;

    public NewsMarketWeights Weights { get; set; } = new();

    public NewsMarketTimeframes Timeframes { get; set; } = new();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (MaxNewsAgeMinutes <= 0)
        {
            errors.Add("MaxNewsAgeMinutes must be positive.");
        }

        if (MinNewsConfidence is < 0 or > 1 || MinNewsImpact is < 0 or > 1 || MinRelevance is < 0 or > 1)
        {
            errors.Add("Confidence, impact, and relevance thresholds must be between 0 and 1.");
        }

        if (MinTotalScore < 0 || MinNewsScore < 0 || MinMarketScore < 0 || ConflictMarketScore < 0)
        {
            errors.Add("Score thresholds must be zero or positive.");
        }

        foreach (var weight in new[] { Weights.News, Weights.Trend, Weights.Volume, Weights.TakerImbalance, Weights.Structure, Weights.Volatility })
        {
            if (weight < 0)
            {
                errors.Add("Strategy weights must be zero or positive.");
                break;
            }
        }

        foreach (var timeframe in new[] { Timeframes.Execution, Timeframes.Flow, Timeframes.Structure, Timeframes.Trend })
        {
            if (!TradingPlatform.Domain.Trading.TimeframeExtensions.TryParseInterval(timeframe, out _))
            {
                errors.Add("Unknown timeframe " + timeframe + ".");
            }
        }

        return errors;
    }
}

public sealed class NewsMarketWeights
{
    public double News { get; set; } = 40;

    public double Trend { get; set; } = 15;

    public double Volume { get; set; } = 15;

    public double TakerImbalance { get; set; } = 10;

    public double Structure { get; set; } = 10;

    public double Volatility { get; set; } = 10;
}

public sealed class NewsMarketTimeframes
{
    public string Execution { get; set; } = "15m";

    public string Flow { get; set; } = "5m";

    public string Structure { get; set; } = "15m";

    public string Trend { get; set; } = "1h";
}
