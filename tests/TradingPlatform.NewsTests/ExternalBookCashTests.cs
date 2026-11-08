using FluentAssertions;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.News;
using TradingPlatform.News;
using Xunit;

namespace TradingPlatform.NewsTests;

public sealed class ExternalBookCashTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Closed_pump_and_news_cash_lands_on_the_close_day_and_in_the_month()
    {
        var cash = new[]
        {
            new ExternalBookCash.DatedCash(new DateTimeOffset(2026, 10, 28, 12, 31, 0, TimeSpan.Zero), -1.5049m),
            new ExternalBookCash.DatedCash(new DateTimeOffset(2026, 11, 2, 3, 57, 0, TimeSpan.Zero), 34.49043598m),
        };
        var performance = Book();

        var folded = ExternalBookCash.Apply(performance, cash, Now);

        folded.Days.Select(day => (day.Date, day.PnL)).Should().Equal(
            ("2026-10-28", -2.50m),
            ("2026-11-02", 29.99m),
            ("2026-11-03", 1.00m));
        folded.Days[2].Cumulative.Should().Be(28.49m);
        folded.MonthlyPnL.Should().Be(32.69m);
        folded.TodaysPnL.Should().Be(1.36m);
        folded.RealizedPnL.Should().Be(22.11m);
    }

    [Fact]
    public void A_close_today_is_added_to_todays_pnl()
    {
        var cash = new[]
        {
            new ExternalBookCash.DatedCash(new DateTimeOffset(2026, 11, 3, 6, 0, 0, TimeSpan.Zero), 2.25m),
        };
        var overview = new PortfolioDto(
            0, 0, 0, 0, 0, false, "Healthy", null, [], [], [], [], [], [],
            LiveTodaysPnL: 1.36m);

        ExternalBookCash.Apply(Book(), cash, Now).TodaysPnL.Should().Be(3.61m);
        ExternalBookCash.Apply(overview, cash, Now).LiveTodaysPnL.Should().Be(3.61m);
    }

    [Fact]
    public void News_without_a_recorded_pnl_is_skipped_and_state_json_is_read()
    {
        var root = Path.Combine(Path.GetTempPath(), "pump-cash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "artifacts", "data", "pump-shorts"));
        File.WriteAllText(
            Path.Combine(root, "artifacts", "data", "pump-shorts", "state.json"),
            """
            {"Tickets":[{"Closed":true,"ClosedAt":"2026-11-02T03:57:10.728+00:00","RealizedPnl":34.49043598},{"Closed":false,"RealizedPnl":9}]}
            """);

        try
        {
            var rows = ExternalBookCash.Load(root, [new NewsTradeExecution { ClosedAtUtc = Now, RealizedPnL = null }]);
            rows.Should().ContainSingle();
            rows[0].Amount.Should().Be(34.49043598m);
            rows[0].At.UtcDateTime.Should().Be(new DateTime(2026, 11, 2, 3, 57, 10, 728, DateTimeKind.Utc));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PerformanceDto Book() => new(
        "Live",
        0, 0, 0, 0, 0, 0,
        -10.88m,
        3.09m,
        1.36m,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        null,
        0,
        0,
        [
            new PerformanceDayDto("2026-10-28", -1m, 0),
            new PerformanceDayDto("2026-11-02", -4.5m, 0),
            new PerformanceDayDto("2026-11-03", 1m, 0),
        ],
        [],
        [],
        [],
        MonthlyPnL: -1.80m);
}
