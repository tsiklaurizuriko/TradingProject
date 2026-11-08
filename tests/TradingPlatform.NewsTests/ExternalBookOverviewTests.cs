using FluentAssertions;
using TradingPlatform.Domain.News;
using TradingPlatform.News;
using Xunit;

namespace TradingPlatform.NewsTests;

public sealed class ExternalBookOverviewTests
{
    [Fact]
    public void Pump_shorts_are_their_own_row_with_the_loss_stop_and_no_fixed_target()
    {
        var desk = new UnexplainedPumpDeskFile
        {
            Positions =
            [
                new() { Coin = "ESPORTSUSDT", Open = false, Pnl = 34.49043598m, PnlKnown = true },
                new() { Coin = "EVAAUSDT", Open = false, Pnl = -1.5049m, PnlKnown = true },
                new() { Coin = "OPENUSDT", Open = true, Pnl = 2.5m, PnlKnown = true },
            ]
        };

        var row = ExternalBookOverview.Pump(desk);

        row.Name.Should().Be(ExternalBookOverview.PumpName);
        row.Entries.Should().Be(3);
        row.Wins.Should().Be(1);
        row.Losses.Should().Be(1);
        row.OpenEntries.Should().Be(1);
        row.RealizedPnL.Should().Be(32.99m);
        row.UnrealizedPnL.Should().Be(2.50m);
        row.StopLossPercent.Should().Be(UnexplainedPumpShort.MaxStopPercent);
        row.TakeProfitPercent.Should().BeNull();
        row.MaxPositions.Should().BeNull();
        row.ExternalBook.Should().BeTrue();
    }

    [Fact]
    public void News_counts_filled_trades_and_keeps_the_news_book()
    {
        var rows = new[]
        {
            new NewsTradeExecution { OrderFilledAtUtc = DateTimeOffset.UtcNow, ClosedAtUtc = DateTimeOffset.UtcNow, RealizedPnL = 4m },
            new NewsTradeExecution { OrderFilledAtUtc = DateTimeOffset.UtcNow },
            new NewsTradeExecution { RealizedPnL = 99m },
        };

        var row = ExternalBookOverview.News(rows);

        row.Name.Should().Be(ExternalBookOverview.NewsName);
        row.Entries.Should().Be(2);
        row.Wins.Should().Be(1);
        row.Losses.Should().Be(0);
        row.OpenEntries.Should().Be(1);
        row.RealizedPnL.Should().Be(4m);
        row.StopLossPercent.Should().Be(6m);
        row.TakeProfitPercent.Should().Be(12m);
        row.MaxPositions.Should().Be(2);
        row.ExternalBook.Should().BeTrue();
    }
}
