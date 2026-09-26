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

    public string? CryptoPanicToken { get; set; }

    public string? FredApiKey { get; set; }

    public NewsMarketStrategyOptions Strategy { get; set; } = new();
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
