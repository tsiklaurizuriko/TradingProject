using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.News;
using Xunit;

namespace TradingPlatform.NewsTests;

public sealed class NewsEventStudyTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Decision_waits_for_the_first_bar_that_closes_after_the_story_arrives()
    {
        var bars = Bars(Enumerable.Repeat(100m, 12).ToArray());
        NewsEventStudy.DecisionTime(bars, Start.AddMinutes(7)).Should().Be(Start.AddMinutes(10));
        NewsEventStudy.DecisionTime(bars, Start.AddMinutes(10)).Should().Be(Start.AddMinutes(10));
        NewsEventStudy.DecisionTime(bars, Start.AddHours(5)).Should().BeNull();
    }

    [Fact]
    public void Forward_return_is_signed_by_direction_and_measured_from_the_decision_close()
    {
        var closes = Enumerable.Range(0, 60).Select(i => 100m + i).ToArray();
        var bars = Bars(closes);
        var decision = bars[10].CloseTime;
        var expected = (double)((closes[13] / closes[10] - 1m) * 100m);
        NewsEventStudy.SignedForwardPercent(bars, decision, TimeSpan.FromMinutes(15), bullish: true).Should().BeApproximately(expected, 1e-9);
        NewsEventStudy.SignedForwardPercent(bars, decision, TimeSpan.FromMinutes(15), bullish: false).Should().BeApproximately(-expected, 1e-9);
    }

    [Fact]
    public void Forward_return_is_missing_when_the_horizon_runs_past_the_data_or_across_a_gap()
    {
        var bars = Bars(Enumerable.Range(0, 20).Select(i => 100m + i).ToArray());
        NewsEventStudy.SignedForwardPercent(bars, bars[15].CloseTime, TimeSpan.FromHours(1), bullish: true).Should().BeNull();
        var gapped = bars.Take(5).Concat(bars.Skip(15)).ToList();
        NewsEventStudy.SignedForwardPercent(gapped, gapped[4].CloseTime, TimeSpan.FromMinutes(10), bullish: true).Should().BeNull();
    }

    [Fact]
    public void First_sighting_ignores_copies_retrieved_later()
    {
        var early = Article("coindesk", "https://www.coindesk.com/etf", Start, Start.AddMinutes(3));
        var late = Article("theblock", "https://www.theblock.co/etf", Start.AddMinutes(20), Start.AddHours(2));
        var pipeline = new NewsPipeline(new NewsOptions());
        var clustered = pipeline.Build([early, late]).Single();
        clustered.SourceCount.Should().Be(2);
        var seen = NewsEventStudy.AtFirstSighting(clustered, [early, late], pipeline);
        seen.Should().NotBeNull();
        seen!.SourceCount.Should().Be(1);
        seen.DetectedAtUtc.Should().Be(Start.AddMinutes(3));
        seen.OriginalArticles.Should().ContainSingle(article => article.Id == early.Id);
    }

    [Fact]
    public void Summary_reports_hit_rate_t_stat_and_cost()
    {
        var summary = NewsEventStudy.Summarize("g", "1h", [1d, -0.5d, 2d, 0.5d]);
        summary.Count.Should().Be(4);
        summary.MeanPercent.Should().BeApproximately(0.75, 1e-12);
        summary.MedianPercent.Should().BeApproximately(0.75, 1e-12);
        summary.HitRate.Should().Be(0.75);
        summary.TStat.Should().BeApproximately(0.75 / Math.Sqrt(3.25 / 3 / 4), 1e-9);
        summary.MeanAfterCostPercent.Should().BeApproximately(0.75 - NewsEventStudy.RoundTripCostPercent, 1e-12);
        NewsEventStudy.Summarize("g", "1h", []).Count.Should().Be(0);
    }

    private static List<MarketCandle> Bars(decimal[] closes) =>
        closes.Select((close, i) => new MarketCandle
        {
            OpenTime = Start.AddMinutes(5 * i),
            CloseTime = Start.AddMinutes(5 * (i + 1)),
            Open = close,
            High = close,
            Low = close,
            Close = close,
            Volume = 10m,
            TakerBuyVolume = 5m,
            IsClosed = true
        }).ToList();

    private static RawNewsItem Article(string source, string url, DateTimeOffset published, DateTimeOffset retrieved) =>
        new()
        {
            Id = source + url,
            Provider = source,
            Source = source,
            SourceUrl = url,
            PublishedAtUtc = published,
            RetrievedAtUtc = retrieved,
            Title = "Bitcoin ETF approval inflow",
            Summary = "Bitcoin ETF approval inflow",
            RelatedAssets = ["BTC"],
            OriginalSourceId = url
        };
}
