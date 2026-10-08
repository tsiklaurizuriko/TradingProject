using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TradingPlatform.News;

public sealed class DeepNewsAnalyzer
{
    private readonly NewsOptions _options;
    private readonly INewsLanguageModel _model;
    private readonly ILogger _logger;
    private readonly Func<DateTimeOffset> _clock;

    public DeepNewsAnalyzer(NewsOptions options, INewsLanguageModel model, ILogger? logger = null, Func<DateTimeOffset>? clock = null)
    {
        _options = options;
        _model = model;
        _logger = logger ?? NullLogger.Instance;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<NewsDeepAnalysis> AnalyzeAsync(NewsEvent item, NewsAssetCatalog catalog, CancellationToken cancellationToken)
    {
        var detected = item.DetectedAtUtc;
        var published = item.PublishedAtUtc;
        var allowed = catalog.Identities.Select(identity => identity.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var (system, user) = NewsAnalysisPrompt.Build(item, _options, allowed);
        Log("NewsAiStarted", item, _options.Ai.FastModel, null);
        var fast = await CompleteAsync(_options.Ai.FastModel, system, user, allowed, cancellationToken);
        if (_options.Ai.UseStrongModel && fast.Status != "Ok")
        {
            var strongName = _options.Ai.StrongModel;
            if (!string.Equals(strongName, _options.Ai.FastModel, StringComparison.OrdinalIgnoreCase))
            {
                var recovered = await CompleteAsync(strongName, system, user, allowed, cancellationToken);
                if (recovered.Status == "Ok")
                {
                    fast = recovered with { UsedStrongModel = true };
                }
            }
        }
        else if (_options.Ai.UseStrongModel && NeedsStrongModel(fast) && !string.Equals(_options.Ai.StrongModel, _options.Ai.FastModel, StringComparison.OrdinalIgnoreCase))
        {
            var strong = await CompleteAsync(_options.Ai.StrongModel, system, user, allowed, cancellationToken);
            if (strong.Status == "Ok")
            {
                fast = strong with { UsedStrongModel = true };
            }
        }

        var classified = _clock();
        if (classified < detected)
        {
            classified = detected;
        }

        if (fast.Status != "Ok")
        {
            MarkFailed(item, fast.Status, fast.Error, classified, fast.Provider, fast.Model, _options.Ai.PromptVersion, fast.RawJson);
            Log(fast.Status == "Invalid" ? "NewsAiFailed" : "NewsAiFailed", item, fast.Model, fast.Error);
            return fast;
        }

        Apply(item, fast, catalog, classified);
        if (item.DetectedAtUtc != detected || item.PublishedAtUtc != published)
        {
            item.DetectedAtUtc = detected;
            item.PublishedAtUtc = published;
        }

        Log("NewsAiCompleted", item, item.AiModel, null);
        return fast;
    }

    public static void MarkFailed(NewsEvent item, string status, string error, DateTimeOffset classifiedAt, string? provider = null, string? model = null, string? promptVersion = null, string? raw = null)
    {
        item.AnalysisStatus = status;
        item.AnalysisError = error;
        item.ClassifiedAtUtc = classifiedAt;
        item.Direction = EventDirection.Unknown;
        item.EventType = NewsEventType.Other;
        item.Sentiment = 0;
        item.ImpactScore = 0;
        item.ConfidenceScore = 0;
        item.ShouldConsiderTrading = false;
        item.AffectedAssets.Clear();
        item.Assets.Clear();
        item.PrimaryAsset = null;
        item.AiProvider = provider;
        item.AiModel = model;
        item.PromptVersion = promptVersion;
        item.Reason = string.IsNullOrWhiteSpace(raw) ? error : raw.Length <= 8000 ? raw : raw[..8000];
    }

    public static bool TryRestore(NewsEvent item, string rawJson, NewsAssetCatalog catalog, DateTimeOffset classifiedAt, NewsOptions? options = null)
    {
        var allowed = catalog.Identities.Select(identity => identity.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!NewsAnalysisParser.TryParse(rawJson, allowed, out var analysis, out _))
        {
            return false;
        }

        Apply(item, analysis, catalog, classifiedAt);
        if (options is not null)
        {
            item.AiProvider = string.IsNullOrWhiteSpace(item.AiProvider) ? options.Ai.Provider : item.AiProvider;
            item.AiModel = string.IsNullOrWhiteSpace(item.AiModel) ? options.Ai.FastModel : item.AiModel;
            item.PromptVersion = string.IsNullOrWhiteSpace(item.PromptVersion) ? options.Ai.PromptVersion : item.PromptVersion;
        }

        return item.AnalysisStatus == "Ok";
    }

    private async Task<NewsDeepAnalysis> CompleteAsync(string model, string system, string user, IReadOnlyList<string> allowed, CancellationToken cancellationToken)
    {
        NewsModelCompletion completion;
        try
        {
            completion = await _model.CompleteAsync(
                new NewsModelRequest(model, system, user, TimeSpan.FromSeconds(Math.Max(5, _options.Ai.TimeoutSeconds))),
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return NewsDeepAnalysis.Failed("Timeout", "The model timed out.", _options.Ai.Provider, model, _options.Ai.PromptVersion);
        }

        if (completion.TimedOut)
        {
            return NewsDeepAnalysis.Failed("Timeout", completion.Error ?? "The model timed out.", completion.Provider, completion.Model, _options.Ai.PromptVersion);
        }

        if (!completion.Succeeded || string.IsNullOrWhiteSpace(completion.Content))
        {
            return NewsDeepAnalysis.Failed("Failed", completion.Error ?? "The model call failed.", completion.Provider, model, _options.Ai.PromptVersion);
        }

        if (!NewsAnalysisParser.TryParse(completion.Content, allowed, out var analysis, out var error))
        {
            return NewsDeepAnalysis.Failed("Invalid", error, completion.Provider, completion.Model, _options.Ai.PromptVersion, completion.Content);
        }

        return new NewsDeepAnalysis
        {
            Status = analysis.Status,
            Error = analysis.Error,
            EventType = analysis.EventType,
            ParsedType = analysis.ParsedType,
            VerificationStatus = analysis.VerificationStatus,
            SourceReliability = analysis.SourceReliability,
            OverallConfidence = analysis.OverallConfidence,
            Impact = analysis.Impact,
            Novelty = analysis.Novelty,
            AlreadyPricedIn = analysis.AlreadyPricedIn,
            ExpectedHorizon = analysis.ExpectedHorizon,
            ExpectedHorizonMinutes = analysis.ExpectedHorizonMinutes,
            MarketMechanism = analysis.MarketMechanism,
            AffectedAssets = analysis.AffectedAssets,
            RiskFlags = analysis.RiskFlags,
            ShouldConsiderTrading = analysis.ShouldConsiderTrading,
            Provider = completion.Provider,
            Model = completion.Model,
            PromptVersion = _options.Ai.PromptVersion,
            RawJson = analysis.RawJson
        };
    }

    private bool NeedsStrongModel(NewsDeepAnalysis analysis)
    {
        if (analysis.Impact >= _options.Ai.StrongImpactThreshold || analysis.AffectedAssets.Count == 0)
        {
            return true;
        }

        if (analysis.VerificationStatus is not ("confirmed" or "official" or "verified"))
        {
            return true;
        }

        var primary = analysis.AffectedAssets.FirstOrDefault(asset => asset.Role == "PRIMARY") ?? analysis.AffectedAssets.FirstOrDefault();
        return primary is null || primary.Direction is "UNCERTAIN" or "NEUTRAL";
    }

    private static void Apply(NewsEvent item, NewsDeepAnalysis analysis, NewsAssetCatalog catalog, DateTimeOffset classifiedAt)
    {
        var links = new List<AssetRelationship>();
        foreach (var asset in analysis.AffectedAssets)
        {
            var identity = catalog.FindSymbol(asset.Symbol);
            if (identity is null)
            {
                continue;
            }

            links.Add(new AssetRelationship
            {
                Symbol = identity.Symbol,
                BaseAsset = identity.BaseAsset,
                ProviderAssetId = identity.ProviderAssetId,
                IsPrimary = asset.Role == "PRIMARY",
                Role = asset.Role,
                Relevance = asset.Role == "PRIMARY" ? 1 : asset.Role == "SECONDARY" ? NewsAssetCatalog.SecondaryRelevance : NewsAssetCatalog.GlobalRelevance,
                AssetDirection = asset.Direction,
                Impact = asset.Impact,
                Confidence = asset.Confidence,
                Reason = asset.Reason
            });
        }

        if (links.Count > 0 && links.All(link => !link.IsPrimary))
        {
            var best = links.OrderByDescending(link => link.Role == "SECONDARY").ThenByDescending(link => link.Confidence).First();
            if (best.Role == "SECONDARY")
            {
                best.IsPrimary = true;
                best.Role = "PRIMARY";
                best.Relevance = 1;
            }
        }

        var primary = links.FirstOrDefault(link => link.IsPrimary);
        item.AffectedAssets = links;
        item.Assets = links.Select(link => link.BaseAsset).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        item.PrimaryAsset = primary?.BaseAsset;
        item.MarketScope = links.Count > 1 ? MarketScope.MultiAsset : MarketScope.Asset;
        item.EventType = analysis.ParsedType;
        item.Direction = DirectionOf(primary?.AssetDirection);
        item.Sentiment = item.Direction switch
        {
            EventDirection.Bullish => 1,
            EventDirection.Bearish => -1,
            _ => 0
        };
        item.ImpactScore = analysis.Impact / 100d;
        item.ConfidenceScore = analysis.OverallConfidence / 100d;
        item.NoveltyScore = analysis.Novelty / 100d;
        item.SourceQualityScore = analysis.SourceReliability / 100d;
        item.SourceReliability = analysis.SourceReliability;
        item.AlreadyPricedIn = analysis.AlreadyPricedIn;
        item.VerificationStatus = analysis.VerificationStatus;
        item.MarketMechanism = analysis.MarketMechanism;
        item.RiskFlags = analysis.RiskFlags.ToList();
        item.ShouldConsiderTrading = analysis.ShouldConsiderTrading;
        item.ExpectedHorizonMinutes = analysis.ExpectedHorizonMinutes;
        item.ExpectedHorizon = analysis.ExpectedHorizonMinutes >= 1440
            ? ExpectedHorizon.Days
            : analysis.ExpectedHorizonMinutes >= 60
                ? ExpectedHorizon.Hours
                : ExpectedHorizon.Minutes;
        item.AnalysisStatus = "Ok";
        item.AnalysisError = string.Empty;
        item.ClassifiedAtUtc = classifiedAt;
        item.AiProvider = analysis.Provider;
        item.AiModel = analysis.Model;
        item.PromptVersion = analysis.PromptVersion;
        item.UsedStrongModel = analysis.UsedStrongModel;
        item.Reason = string.IsNullOrWhiteSpace(analysis.RawJson) ? analysis.MarketMechanism : analysis.RawJson;
    }

    private static EventDirection DirectionOf(string? direction) => direction switch
    {
        "LONG" => EventDirection.Bullish,
        "SHORT" => EventDirection.Bearish,
        "NEUTRAL" => EventDirection.Neutral,
        _ => EventDirection.Unknown
    };

    private void Log(string stage, NewsEvent item, string? model, string? reason) =>
        _logger.LogInformation(
            "{Stage} {EventId} {Symbol} {Decision} {Confidence} {Impact} {Latency} {Model} {Reason}",
            stage,
            item.EventId,
            item.PrimaryAsset ?? string.Empty,
            item.Direction.ToString(),
            item.ConfidenceScore,
            item.ImpactScore,
            item.ClassifiedAtUtc is { } classified ? NewsLatency.Milliseconds(item.DetectedAtUtc, classified) : 0,
            model ?? string.Empty,
            reason ?? string.Empty);
}
