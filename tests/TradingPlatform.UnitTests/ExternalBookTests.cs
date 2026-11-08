using FluentAssertions;
using TradingPlatform.Application.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class ExternalBookTests
{
    private static readonly DateTimeOffset Opened = new(2026, 10, 28, 12, 30, 40, TimeSpan.Zero);

    [Fact]
    public void A_pump_short_on_an_open_coin_is_not_given_to_a_bot()
    {
        var held = ExternalBook.HeldSymbols(
            ["EVAAUSDT"],
            [
                new ExternalBook.OutsideMark("EVAAUSDT", Opened, false),
                new ExternalBook.OutsideMark("EVAAUSDT", Opened.AddSeconds(3), false)
            ]);

        held.Should().Contain("EVAAUSDT");
    }

    [Fact]
    public void A_newer_bot_entry_keeps_the_coin()
    {
        var held = ExternalBook.HeldSymbols(
            ["EVAAUSDT"],
            [
                new ExternalBook.OutsideMark("EVAAUSDT", Opened, false),
                new ExternalBook.OutsideMark("EVAAUSDT", Opened.AddHours(1), true)
            ]);

        held.Should().BeEmpty();
    }

    [Fact]
    public void A_news_order_after_an_older_bot_trade_is_not_taken()
    {
        var held = ExternalBook.HeldSymbols(
            ["EVAAUSDT"],
            [
                new ExternalBook.OutsideMark("EVAAUSDT", Opened.AddDays(-2), true),
                new ExternalBook.OutsideMark("evaausdt", Opened, false)
            ]);

        held.Should().Contain("EVAAUSDT");
    }

    [Fact]
    public void A_flat_coin_is_not_held()
    {
        var held = ExternalBook.HeldSymbols(
            [],
            [new ExternalBook.OutsideMark("EVAAUSDT", Opened, false)]);

        held.Should().BeEmpty();
    }

    [Theory]
    [InlineData("pump-6c0ed7994e9152937b4d")]
    [InlineData("pump-6c0ed7994e9152937b4d-sl")]
    [InlineData("news-abc123-tp")]
    [InlineData("news-abc123-hz")]
    public void News_and_pump_client_ids_are_outside_the_bot_book(string clientOrderId)
    {
        ExternalBook.IsNewsOrPump(clientOrderId).Should().BeTrue();
    }

    [Fact]
    public void A_bot_stop_is_not_an_outside_order()
    {
        ExternalBook.IsNewsOrPump("sl" + Guid.NewGuid().ToString("N")[..16]).Should().BeFalse();
        ExternalBook.IsNewsOrPump("L0123456789").Should().BeFalse();
    }

    [Fact]
    public void A_round_trip_that_opened_with_the_pump_is_not_the_bots_trade()
    {
        var marks = new List<ExternalBook.OutsideMark>
        {
            new("EVAAUSDT", Opened, false),
            new("EVAAUSDT", Opened.AddSeconds(3), false)
        };

        ExternalBook.TripBelongsOutside(Opened.AddSeconds(1), marks).Should().BeTrue();
        ExternalBook.TripBelongsOutside(Opened.AddHours(5), marks).Should().BeFalse();
    }

    [Fact]
    public void A_pump_close_hours_later_still_keeps_the_round_trip_out_of_the_bot()
    {
        var closed = Opened.AddHours(5);
        var marks = new List<ExternalBook.OutsideMark>
        {
            new("ESPORTSUSDT", closed, false)
        };

        ExternalBook.TripBelongsOutside(Opened, closed, marks).Should().BeTrue();
        ExternalBook.TripBelongsOutside(Opened, closed, []).Should().BeFalse();
    }

    [Fact]
    public void A_bot_entry_in_the_same_window_keeps_the_round_trip()
    {
        var marks = new List<ExternalBook.OutsideMark>
        {
            new("EVAAUSDT", Opened, false),
            new("EVAAUSDT", Opened.AddSeconds(2), true)
        };

        ExternalBook.TripBelongsOutside(Opened, marks).Should().BeFalse();
    }
}
