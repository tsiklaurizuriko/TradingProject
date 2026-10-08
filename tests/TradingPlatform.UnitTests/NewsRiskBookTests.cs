using FluentAssertions;
using TradingPlatform.Domain.Risk;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class NewsRiskBookTests
{
    [Fact]
    public void News_book_risks_half_a_percent_with_room_for_the_first_spike()
    {
        var book = NewsRiskBook.Create();

        book.Name.Should().Be("NEWS");
        book.IsActive.Should().BeFalse();
        book.AllowLive.Should().BeTrue();
        book.RiskPerTradePercent.Should().Be(0.5m);
        book.StopLossPercent.Should().Be(6m);
        book.TakeProfitPercent.Should().Be(12m);
        book.MaxLeverage.Should().Be(3m);
        book.MaxSimultaneousPositions.Should().Be(2);
        (book.StopLossPercent + book.MinimumLiquidationSafetyBufferPercent).Should().BeLessThan(100m / book.MaxLeverage);
    }
}
