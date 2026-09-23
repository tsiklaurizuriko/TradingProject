using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using TradingPlatform.Infrastructure.Research;
using TradingPlatform.Strategies.Engine;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class PriceActionResearchQueryTests
{
    [Fact]
    public async Task Summary_never_promotes_paper_and_keeps_live_off()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pa-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "summary.json"), """
                {
                  "id": "run-pa",
                  "cupAndHandle": "NOT_IMPLEMENTED",
                  "books": [{
                    "CandidateId": "PA-PA_BULL_FLAG",
                    "Symbol": "BTCUSDT",
                    "Timeframe": "5m",
                    "Phase": "OOS",
                    "CostLabel": "BASE",
                    "Status": "RESEARCHING",
                    "TradeCount": 4,
                    "ProfitFactor": 0.9,
                    "NetPnl": -1.2
                  }],
                  "sequences": [{
                    "symbol": "BTCUSDT",
                    "timeframe": "5m",
                    "name": "3_bullish",
                    "occurrences": 12,
                    "meanFwd3": 0.001,
                    "medianMfe": 0.004,
                    "medianMae": -0.003,
                    "hitPos50": 0.4,
                    "hitNeg50": 0.3
                  }],
                  "sameCoinRejects": 1
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(dir, "phase7-data-expansion.json"), """
                {
                  "coverage": [{
                    "symbol": "BTCUSDT",
                    "timeframe": "1m",
                    "requestedFrom": "2026-03-26T00:00:00Z",
                    "requestedTo": "2026-09-22T23:59:59Z",
                    "actualFirstBar": "2026-03-26T00:00:00Z",
                    "actualLastBar": "2026-09-22T23:59:59Z",
                    "barCount": 259200,
                    "expectedBarCount": 259200,
                    "gapCount": 0,
                    "missingBarCount": 0,
                    "duplicateCount": 0,
                    "coveragePercent": 100,
                    "downloadedPages": 87,
                    "cacheHits": 0,
                    "cacheMisses": 1,
                    "continuous": true,
                    "qualityPassed": true,
                    "status": "FULL_COVERAGE"
                  }]
                }
                """);
            var configuration = Substitute.For<IConfiguration>();
            configuration["Trading:PriceAction:ArtifactDirectory"].Returns(dir);
            var query = new PriceActionResearchQuery(configuration);
            var summary = await query.GetSummaryAsync();
            summary.LiveOff.Should().BeTrue();
            summary.ScalpingLiveOff.Should().BeTrue();
            summary.PriceActionLiveOff.Should().BeTrue();
            summary.ValidatedForPaperAssigned.Should().BeFalse();
            summary.CupAndHandle.Should().Be("NOT_IMPLEMENTED");
            summary.Confirmation.Should().Contain("PRICE ACTION LIVE = OFF");
            summary.Strategies.Should().HaveCount(18);
            summary.Strategies.Should().OnlyContain(row => !row.OperatorCatalog && !row.Enabled);
            summary.Books.Should().ContainSingle(row => row.Coin == "BTCUSDT" && row.CandidateId == "PA-PA_BULL_FLAG");
            summary.Sequences.Should().ContainSingle(row => row.Name == "3_bullish" && row.Occurrences == 12);
            summary.DataExpansion.Should().ContainSingle(row =>
                row.Coin == "BTCUSDT"
                && row.Timeframe == "1m"
                && row.CoveragePercent == 100m
                && row.QualityPassed);
            summary.SameCoinRejects.Should().Be(1);
            StrategyTemplateKeys.IsOperatorCatalog(StrategyTemplateKeys.PaWDoubleBottom).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Summary_prefers_all_timeframe_alpha_artifacts_and_maps_coverage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pa-alpha-query-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "price-action");
        var alpha = Path.Combine(root, "price-action-alpha");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(alpha);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(alpha, "summary.json"), """
                {
                  "id": "all-tf-run",
                  "books": [{
                    "candidateId": "PA-PA_BULL_FLAG|STF|1h",
                    "symbol": "ETHUSDT",
                    "timeframe": "1h",
                    "phase": "OOS",
                    "costLabel": "BASE",
                    "status": "RESEARCH_COMPLETE",
                    "tradeCount": 12,
                    "profitFactor": 1.1,
                    "netPnl": 3.5
                  }],
                  "hypotheses": ["all-timeframe"]
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(alpha, "coverage-post.json"), """
                {
                  "coverage": [{
                    "symbol": "ETHUSDT",
                    "timeframe": "1h",
                    "requestedFrom": "2024-09-23T00:00:00Z",
                    "requestedTo": "2026-09-22T23:59:59Z",
                    "firstTimestamp": "2024-09-23T00:00:00Z",
                    "lastTimestamp": "2026-09-22T23:59:59Z",
                    "barCount": 17520,
                    "expectedBarCount": 17520,
                    "gapCount": 0,
                    "missingBarCount": 0,
                    "duplicateCount": 0,
                    "outOfOrderCount": 0,
                    "coveragePercent": 100,
                    "qualityPassed": true,
                    "status": "FULL_COVERAGE"
                  }]
                }
                """);
            var configuration = Substitute.For<IConfiguration>();
            configuration["Trading:PriceAction:ArtifactDirectory"].Returns(legacy);
            var summary = await new PriceActionResearchQuery(configuration).GetSummaryAsync();

            summary.LastRunId.Should().Be("all-tf-run");
            summary.Books.Should().ContainSingle(x => x.CandidateId.EndsWith("|STF|1h"));
            summary.Hypotheses.Should().ContainSingle("all-timeframe");
            summary.DataExpansion.Should().ContainSingle(x =>
                x.Coin == "ETHUSDT" && x.Timeframe == "1h" && x.Continuous && x.QualityPassed);
            summary.ValidatedForPaperAssigned.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Contextual_summary_stays_research_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpa-query-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "price-action");
        var contextual = Path.Combine(root, "contextual-price-action-alpha");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(contextual);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(contextual, "summary.json"), """
                {
                  "hypothesisCount": 25,
                  "families": 8,
                  "survivors": 0,
                  "validatedForPaperAssigned": true,
                  "hypotheses": ["CPA-SWEEP|BASELINE|5m"],
                  "futuresData": "DATA_UNAVAILABLE"
                }
                """);
            var configuration = Substitute.For<IConfiguration>();
            configuration["Trading:PriceAction:ArtifactDirectory"].Returns(legacy);
            var summary = await new PriceActionResearchQuery(configuration).GetContextualSummaryAsync();
            summary.LiveOff.Should().BeTrue();
            summary.ScalpingLiveOff.Should().BeTrue();
            summary.PriceActionLiveOff.Should().BeTrue();
            summary.PaperPromotionOff.Should().BeTrue();
            summary.ValidatedForPaperAssigned.Should().BeFalse();
            summary.HypothesisCount.Should().Be(25);
            summary.Survivors.Should().Be(0);
            summary.Hypotheses.Should().ContainSingle("CPA-SWEEP|BASELINE|5m");
            summary.Confirmation.Should().Contain("No VALIDATED_FOR_PAPER");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
