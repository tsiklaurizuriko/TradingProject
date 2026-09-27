using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.News;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class NewsIngestionPersistenceTests
{
    [Fact]
    public async Task Duplicate_articles_keep_one_event_and_provider_health_is_readable()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new TradingDbContext(options);
        var store = new NewsDatabase(db);
        var published = new DateTimeOffset(2026, 9, 26, 11, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var events = new NewsPipeline(new NewsOptions()).Build(
        [
            Item("coingecko", "Cointelegraph", "https://cointelegraph.com/news/etf-approval?utm=1", published),
            Item("gdelt", "cointelegraph.com", "https://www.cointelegraph.com/news/etf-approval", published)
        ]);
        events.Should().ContainSingle();
        await store.SaveIngestionAsync(
            events,
            [
                new NewsProviderReport("coingecko", true, null, 1, 0, now),
                new NewsProviderReport("gdelt", true, null, 1, 0, now)
            ],
            CancellationToken.None);

        (await store.EventCountAsync(CancellationToken.None)).Should().Be(1);
        (await store.ArticleCountAsync(CancellationToken.None)).Should().Be(1);
        var article = await db.NewsArticles.Include(row => row.Sightings).SingleAsync();
        article.Provider.Should().Be("coingecko");
        article.Publisher.Should().Be("Cointelegraph");
        article.Source.Should().Be("Cointelegraph");
        article.Sightings.Select(row => row.Provider).Should().BeEquivalentTo(["coingecko", "gdelt"]);

        await store.SaveIngestionAsync(
            [],
            [new NewsProviderReport("cryptopanic", false, "CryptoPanic rate limit (HTTP 429).", 0, 0, now)],
            CancellationToken.None);
        await store.SaveIngestionAsync(
            new NewsPipeline(new NewsOptions()).Build(
            [
                Item("binance", "Cointelegraph", "https://cointelegraph.com/news/etf-approval", published)
            ]),
            [new NewsProviderReport("binance", true, null, 1, 0, now.AddMinutes(1))],
            CancellationToken.None);

        (await store.EventCountAsync(CancellationToken.None)).Should().Be(1);
        (await db.NewsArticleSightings.CountAsync()).Should().Be(3);
        var feed = await store.RecentFeedAsync(10, CancellationToken.None);
        var row = feed.Should().ContainSingle().Subject;
        row.Publisher.Should().Be("Cointelegraph");
        row.Providers.Should().Contain("coingecko").And.Contain("gdelt").And.Contain("binance");
        row.Evaluated.Should().BeFalse();
        row.Detail.Should().Contain("Stopped");
        row.Classification.Should().Contain("Etf");
        row.Impact.Should().NotBeNull();

        var health = await store.ProviderHealthAsync(CancellationToken.None);
        health.Should().Contain(item => item.Provider == "coingecko" && item.InsertedCount == 1 && item.LastSuccessUtc == now);
        health.Should().Contain(item => item.Provider == "gdelt" && item.DeduplicatedCount == 1 && item.FetchedCount == 1);
        health.Should().Contain(item =>
            item.Provider == "cryptopanic"
            && item.LastSuccessUtc == null
            && item.LastError!.Contains("429"));
        health.Should().Contain(item => item.Provider == "binance" && item.DeduplicatedCount == 1 && item.InsertedCount == 0);

        await store.SaveIngestionAsync(
            [],
            [new NewsProviderReport("coingecko", false, NewsProviderCatalog.CredentialsMissing, 0, 0, now, false)],
            CancellationToken.None);
        var disabled = (await store.ProviderHealthAsync(CancellationToken.None)).Single(item => item.Provider == "coingecko");
        disabled.Enabled.Should().BeFalse();
        disabled.LastError.Should().Be(NewsProviderCatalog.CredentialsMissing);
        NewsProviderHealthRow.Describe(disabled, now).Should().Be("Disabled");
        var working = (await store.ProviderHealthAsync(CancellationToken.None)).Single(item => item.Provider == "gdelt");
        working.Enabled.Should().BeTrue();
        NewsProviderHealthRow.Describe(working, now.AddMinutes(-1)).Should().Be("Working");
    }

    private static RawNewsItem Item(string provider, string publisher, string url, DateTimeOffset published) =>
        new()
        {
            Id = provider + ":" + url,
            Provider = provider,
            Source = publisher,
            SourceUrl = url,
            PublishedAtUtc = published,
            RetrievedAtUtc = published,
            Title = "Bitcoin ETF approval inflow",
            Summary = "Bitcoin ETF approval inflow",
            OriginalSourceId = url
        };
}
