using FluentAssertions;
using TradingPlatform.News;
using Xunit;

namespace TradingPlatform.NewsTests;

public sealed class NewsActivityCopyTests
{
    [Fact]
    public void Old_news_says_no_order_was_sent()
    {
        NewsActivityCopy.Humanize("Stopped at age: 200m exceeds 60m.")
            .Should().Be("The news is 200 minutes old. Trades stop after 60 minutes, so no order was sent.");
    }

    [Fact]
    public void Priced_in_news_does_not_look_like_an_order()
    {
        NewsActivityCopy.Why("LONG", NewsRejection.AlreadyPricedIn, null, "NotSent")
            .Should().Be("The market has already moved on this news. No order was sent.");
    }

    [Fact]
    public void Fresh_news_is_sent_even_when_it_was_stored_before_this_cycle()
    {
        var cycle = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        NewsAiGate.ShouldAnalyze(cycle.AddMinutes(-10), cycle, 60, alreadyAnalyzed: false).Should().BeTrue();
        NewsAiGate.ShouldAnalyze(cycle.AddHours(-3), cycle, 60, alreadyAnalyzed: false).Should().BeFalse();
        NewsAiGate.ShouldAnalyze(cycle.AddMinutes(-5), cycle, 60, alreadyAnalyzed: false).Should().BeTrue();
        NewsAiGate.ShouldAnalyze(cycle.AddMinutes(-5), cycle, 60, alreadyAnalyzed: true).Should().BeFalse();
    }

    [Fact]
    public void A_fill_says_the_order_was_sent()
    {
        NewsActivityCopy.OrderState(true, "123", DateTimeOffset.UtcNow, null, "Approved", "Filled").Should().Be("Filled");
        NewsActivityCopy.Why("LONG", null, null, "Filled").Should().Be("A long order was filled on Binance.");
        NewsActivityCopy.OrderLine("Filled", 100.5m, 0.25m, 98m, 104m, "123")
            .Should().Be("Entry 100.5 · size 0.25 · stop 98 · target 104 · Binance 123");
    }
}
