using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using TradingPlatform.Infrastructure.Research;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class ScalpingResearchQueryTests
{
    [Fact]
    public async Task Summary_maps_pascal_books_and_camel_coverage_without_promoting_paper()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scalp-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "coverage.json"), """
                [{"symbol":"BTCUSDT","timeframe":"5m","bars":100,"gaps":0,"takerCoverage":0.8,"status":"RESEARCHING","notes":"ok"}]
                """);
            await File.WriteAllTextAsync(Path.Combine(dir, "summary.json"), """
                {
                  "id": "run-1",
                  "books": [{
                    "CandidateId": "SCALP-SCALP_EMA_MOMENTUM",
                    "Symbol": "BTCUSDT",
                    "Timeframe": "5m",
                    "Phase": "OOS",
                    "CostLabel": "BASE",
                    "Status": "RESEARCHING",
                    "TradeCount": 8,
                    "ProfitFactor": 0.28,
                    "MedianHoldingMinutes": 40,
                    "P25HoldingMinutes": 20,
                    "P75HoldingMinutes": 40,
                    "NetPnl": -3.1
                  }],
                  "occupancyRejects": [{
                    "Time": "2026-09-21T06:45:00+00:00",
                    "StrategyKey": "scalp_rsi_pullback",
                    "Symbol": "BTCUSDT",
                    "Reason": "SymbolAlreadyOpen"
                  }],
                  "sameCoinRejects": 2,
                  "slotRejects": 0,
                  "heatRejects": 1
                }
                """);

            var configuration = Substitute.For<IConfiguration>();
            configuration["Trading:Scalping:ArtifactDirectory"].Returns(dir);
            var query = new ScalpingResearchQuery(configuration);
            var summary = await query.GetSummaryAsync();
            summary.LiveOff.Should().BeTrue();
            summary.ScalpingLiveOff.Should().BeTrue();
            summary.ValidatedForPaperAssigned.Should().BeFalse();
            summary.Confirmation.Should().Contain("No VALIDATED_FOR_PAPER");
            summary.LastRunId.Should().Be("run-1");
            summary.Coverage.Should().ContainSingle(row => row.Coin == "BTCUSDT" && row.Timeframe == "5m" && row.Bars == 100);
            summary.Books.Should().ContainSingle(row => row.CandidateId == "SCALP-SCALP_EMA_MOMENTUM" && row.Coin == "BTCUSDT" && row.ProfitFactor == 0.28m);
            summary.SameCoinRejects.Should().Be(2);
            summary.HeatRejects.Should().Be(1);
            summary.OccupancyRejects.Should().ContainSingle(row => row.Coin == "BTCUSDT" && row.Reason == "SymbolAlreadyOpen");
            summary.Strategies.Should().OnlyContain(row => !row.OperatorCatalog && !row.Enabled);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
