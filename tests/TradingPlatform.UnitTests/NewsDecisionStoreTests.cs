using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingPlatform.Domain.News;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.News;
using TradingPlatform.Risk;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class NewsDecisionStoreTests
{
    [Fact]
    public void News_cost_defaults_match_the_risk_engine()
    {
        var ai = new NewsAiOptions();
        ai.TakerFeePercent.Should().Be((double)RiskEngine.DefaultTakerFeePercent);
        ai.SlippagePercent.Should().Be((double)RiskEngine.DefaultSlippagePercent);
    }

    [Fact]
    public async Task Analysis_decision_and_audit_keep_the_model_and_prompt_version()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new TradingDbContext(options);
        var store = new NewsDatabase(db);
        var stored = await store.AddEventAsync(new StoredNewsEvent { DedupKey = "evt-1", PublishedAtUtc = DateTimeOffset.UtcNow }, CancellationToken.None);
        var when = DateTimeOffset.UtcNow;
        await store.AddAnalysisAsync(new NewsAnalysis
        {
            StoredNewsEventId = stored.Id,
            EventDedupKey = "evt-1",
            Provider = "openai-compatible",
            Model = "gpt-4.1",
            PromptVersion = "news-deep-v1",
            Status = "Ok",
            EventType = "Etf",
            VerificationStatus = "confirmed",
            SourceReliability = 90,
            OverallConfidence = 95,
            Impact = 90,
            Novelty = 95,
            AlreadyPricedIn = 10,
            ExpectedHorizon = "1h",
            ClassifiedAtUtc = when,
            DetectionLatencyMs = 20_000,
            ClassificationLatencyMs = 4_000
        }, CancellationToken.None);
        await store.AddTradingDecisionAsync(new NewsTradingDecision
        {
            StoredNewsEventId = stored.Id,
            EventDedupKey = "evt-1",
            Symbol = "SOLUSDT",
            Decision = "NO_TRADE",
            RejectionReason = NewsRejection.AlreadyPricedIn,
            DecisionAtUtc = when
        }, CancellationToken.None);
        await store.AddDecisionAuditAsync(new NewsDecisionAudit
        {
            EventDedupKey = "evt-1",
            Symbol = "SOLUSDT",
            Decision = "NO_TRADE",
            RejectionReason = NewsRejection.AlreadyPricedIn,
            Provider = "openai-compatible",
            Model = "gpt-4.1",
            PromptVersion = "news-deep-v1",
            DecisionAtUtc = when,
            PayloadJson = "{\"latency\":20000}"
        }, CancellationToken.None);
        await store.AddTradeExecutionAsync(new NewsTradeExecution
        {
            EventDedupKey = "evt-1",
            Symbol = "SOLUSDT",
            Side = "LONG",
            ClientOrderId = "news-abc",
            Status = "READY"
        }, CancellationToken.None);
        await store.AddTradeExecutionAsync(new NewsTradeExecution
        {
            EventDedupKey = "evt-1",
            Symbol = "SOLUSDT",
            Side = "LONG",
            ClientOrderId = "news-abc",
            Status = "READY"
        }, CancellationToken.None);

        (await db.NewsAnalyses.CountAsync()).Should().Be(1);
        (await db.NewsAnalyses.SingleAsync()).Model.Should().Be("gpt-4.1");
        (await db.NewsAnalyses.SingleAsync()).PromptVersion.Should().Be("news-deep-v1");
        (await db.NewsTradingDecisions.SingleAsync()).RejectionReason.Should().Be(NewsRejection.AlreadyPricedIn);
        (await db.NewsDecisionAudits.SingleAsync()).PayloadJson.Should().Contain("latency");
        (await db.NewsTradeExecutions.CountAsync()).Should().Be(1);
    }
}
