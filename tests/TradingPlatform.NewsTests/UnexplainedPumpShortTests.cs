using FluentAssertions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.News;
using Xunit;

namespace TradingPlatform.NewsTests;

public sealed class UnexplainedPumpShortTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly UnexplainedPumpOptions Options = new();

    [Fact]
    public void One_day_rise_above_150_with_no_catalyst_and_a_stall_is_a_short()
    {
        var move = Move(high24h: 0.060m, last: 0.050m, base24: 0.020m);
        move!.Rise.Should().BeGreaterThan(1.50m);
        var decision = Decide(move, stalled: true, PumpNewsVerdict.Clear("none"));
        decision.Signal.Should().Be(UnexplainedPumpSignals.Short);
        decision.DaySpan.Should().Be(1);
    }

    [Fact]
    public void Model_saying_the_news_caused_the_rise_blocks_the_short()
    {
        PumpRiseReview.TryParse("""{"explainsRise":true,"reason":"Binance listed IRYS today."}""", out var news).Should().BeTrue();
        news.CatalystFound.Should().BeTrue();
        Decide(Move(0.060m, 0.050m, 0.020m), true, news).Signal.Should().Be(UnexplainedPumpSignals.NoTrade);
    }

    [Fact]
    public void Model_saying_a_promo_did_not_cause_the_rise_allows_the_short()
    {
        const string json = """{"explainsRise":false,"reason":"The openPR piece names IRYS only to advertise AlphaPepe."}""";
        PumpRiseReview.TryParse(json, out var news).Should().BeTrue();
        news.CatalystFound.Should().BeFalse();
        var decision = Decide(Move(0.060m, 0.050m, 0.020m), true, news);
        decision.Signal.Should().Be(UnexplainedPumpSignals.Short);
        decision.Reason.Should().Contain("AlphaPepe");
    }

    [Fact]
    public void A_fenced_model_reply_still_parses()
    {
        const string json = """
            ```json
            {"explainsRise":false,"reason":"Price recap only."}
            ```
            """;
        PumpRiseReview.TryParse(json, out var news).Should().BeTrue();
        news.CatalystFound.Should().BeFalse();
    }

    [Fact]
    public void A_broken_model_reply_does_not_short()
    {
        PumpRiseReview.TryParse("not json", out _).Should().BeFalse();
        var decision = Decide(Move(0.060m, 0.050m, 0.020m), true, PumpNewsVerdict.Incomplete("The model reply could not be read. No short."));
        decision.Signal.Should().Be(UnexplainedPumpSignals.NoTrade);
    }

    [Fact]
    public void The_same_headlines_are_not_sent_again()
    {
        var headlines = new[] { new PumpHeadline("Irys surges", "body", "openPR.com", Start) };
        var fingerprint = PumpRiseReview.Fingerprint(headlines);
        var prior = new PumpAskRecord
        {
            Symbol = "IRYSUSDT",
            Fingerprint = fingerprint,
            AskedAt = Start,
            Succeeded = true,
            CatalystFound = false,
            Reason = "promo"
        };
        PumpRiseReview.ShouldAsk(prior, fingerprint, Start.AddMinutes(5)).Should().BeFalse();
        PumpRiseReview.ShouldAsk(prior, fingerprint, Start.AddDays(2)).Should().BeFalse();
        PumpRiseReview.ShouldAsk(null, fingerprint, Start).Should().BeTrue();
    }

    [Fact]
    public void A_new_headline_is_sent_once()
    {
        var first = PumpRiseReview.Fingerprint([new PumpHeadline("Irys surges", "a", "openPR.com", Start)]);
        var second = PumpRiseReview.Fingerprint(
        [
            new PumpHeadline("Irys surges", "a", "openPR.com", Start),
            new PumpHeadline("Binance lists IRYS", "listing", "binance", Start)
        ]);
        var prior = new PumpAskRecord { Fingerprint = first, AskedAt = Start, Succeeded = true };
        PumpRiseReview.ShouldAsk(prior, second, Start.AddMinutes(1)).Should().BeTrue();
    }

    [Fact]
    public void A_failed_call_waits_before_trying_again()
    {
        var prior = new PumpAskRecord { Fingerprint = "abc", AskedAt = Start, Succeeded = false, Reason = "timeout" };
        PumpRiseReview.ShouldAsk(prior, "abc", Start.AddMinutes(10)).Should().BeFalse();
        PumpRiseReview.ShouldAsk(prior, "abc", Start.AddMinutes(30)).Should().BeTrue();
    }

    [Fact]
    public void Fingerprint_ignores_order_and_body()
    {
        var a = PumpRiseReview.Fingerprint(
        [
            new PumpHeadline("B", "one", "s", Start),
            new PumpHeadline("A", "two", "s", Start)
        ]);
        var b = PumpRiseReview.Fingerprint(
        [
            new PumpHeadline("A", "changed", "s", Start.AddHours(1)),
            new PumpHeadline("B", "other", "s", Start)
        ]);
        a.Should().Be(b);
    }

    [Fact]
    public void For_model_keeps_the_coin_and_drops_other_listings()
    {
        var rows = PumpRiseReview.ForModel(
            [
                new PumpHeadline("Binance will list FOO", "FOO", "binance", Start),
                new PumpHeadline("Best Crypto: Irys Surges as AlphaPepe Unlocks", "pre-listing", "google-news", Start),
                new PumpHeadline("Datachain rally", "storage", "google-news", Start)
            ],
            "IRYS",
            "IRYSUSDT");
        rows.Select(row => row.Title).Should().Equal(
            "Best Crypto: Irys Surges as AlphaPepe Unlocks",
            "Datachain rally");
    }

    [Fact]
    public void Prompt_sends_every_selected_headline_together()
    {
        var (system, user) = PumpRiseReview.Prompt(
            "IRYSUSDT",
            "IRYS",
            3.65m,
            [
                new PumpHeadline("Irys surges", "AlphaPepe", "openPR.com", Start),
                new PumpHeadline("IRYS volume", "no announcement", "google-news", Start)
            ]);
        system.Should().Contain("explainsRise");
        user.Should().Contain("IRYSUSDT");
        user.Should().Contain("Irys surges");
        user.Should().Contain("IRYS volume");
        user.Should().Contain("365%");
        user.Should().Contain("24 hours");
    }

    [Fact]
    public void A_wide_pump_stop_is_capped_so_ten_times_leverage_can_enter()
    {
        UnexplainedPumpShort.StopPercent(1.2818m, 1.43m, 0.50m).Should().Be(67.34m);
        UnexplainedPumpShort.OrderStopPercent(1.2818m, 1.43m, 0.50m).Should().Be(7.5m);
        UnexplainedPumpShort.OrderLeverage.Should().Be(10m);
        UnexplainedPumpShort.MaxEntryMarginUsdt.Should().Be(20m);
    }

    [Fact]
    public void The_first_squeeze_keeps_the_disaster_stop()
    {
        var follow = UnexplainedPumpShort.Follow(0.0189002m, 0.016863m, 0.020243m, 30m, 20m, 40m);
        follow.Following.Should().BeFalse();
        follow.Stop.Should().Be(0.020243m);
        UnexplainedPumpShort.BounceExit(follow, 0.019661m).Should().BeNull();
    }

    [Fact]
    public void A_dump_trails_thirty_percent_above_the_low()
    {
        var follow = UnexplainedPumpShort.Follow(0.0189002m, 0.012388m, 0.020243m, 30m, 20m, 40m);
        follow.Following.Should().BeTrue();
        follow.Stop.Should().Be(0.012388m * 1.30m);
        UnexplainedPumpShort.BounceExit(follow, 0.016027m).Should().BeNull();
        UnexplainedPumpShort.BounceExit(follow, follow.Stop).Should().Contain("bounce");
    }

    [Fact]
    public void A_forty_percent_drop_tightens_the_trail_to_twenty()
    {
        var entry = 0.0189002m;
        var follow = UnexplainedPumpShort.Follow(entry, entry * 0.60m, 0.020243m, 30m, 20m, 40m);
        follow.Following.Should().BeTrue();
        follow.Stop.Should().Be(entry * 0.60m * 1.20m);
    }

    [Fact]
    public void The_hold_clock_retires_once_the_drop_is_under_way()
    {
        UnexplainedPumpShort.DumpHasStarted(0.0189002m, 0.016863m, 15m).Should().BeFalse();
        UnexplainedPumpShort.DumpHasStarted(0.0189002m, 0.012388m, 15m).Should().BeTrue();
    }

    [Fact]
    public void The_follow_stop_waits_for_the_low_bar_to_close()
    {
        var lowAt = new DateTimeOffset(2026, 11, 3, 3, 47, 0, TimeSpan.Zero);
        UnexplainedPumpShort.BarHasClosed(lowAt, lowAt.AddMinutes(8)).Should().BeFalse();
        UnexplainedPumpShort.BarHasClosed(lowAt, new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public void A_rise_still_printing_the_high_is_not_a_short()
    {
        var move = Move(0.060m, 0.059m, 0.020m);
        var stalled = UnexplainedPumpShort.Stalled(0.059m, move!.PumpHigh, [Bar(0.058m)], Options.StallPullback);
        stalled.Should().BeFalse();
        Decide(move, stalled, PumpNewsVerdict.Clear("none")).Signal.Should().Be(UnexplainedPumpSignals.NoTrade);
    }

    [Fact]
    public void A_rise_under_150_percent_is_ignored()
    {
        UnexplainedPumpShort.Measure(0.045m, 0.040m, Change(0.040m, 0.020m), 1.50m).Should().BeNull();
    }

    [Fact]
    public void A_rise_spread_over_two_days_is_ignored_when_the_last_24_hours_are_under_150()
    {
        UnexplainedPumpShort.Measure(0.028m, 0.022m, Change(0.022m, 0.015m), 1.50m).Should().BeNull();
    }

    [Fact]
    public void A_failed_search_does_not_short()
    {
        var decision = Decide(Move(0.060m, 0.050m, 0.020m), true, PumpNewsVerdict.Incomplete("offline"));
        decision.Signal.Should().Be(UnexplainedPumpSignals.NoTrade);
        decision.Reason.Should().Contain("offline");
    }

    [Fact]
    public void Rss_without_items_is_a_completed_search()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss><channel></channel></rss>
            """;
        PumpNewsSearch.ReadRss(xml, "google-news").Should().NotBeNull();
        PumpNewsSearch.ReadRss(xml, "google-news")!.Should().BeEmpty();
    }

    [Fact]
    public void Realized_pnl_uses_only_the_fills_that_close_this_short()
    {
        var first = Start;
        var later = Start.AddHours(6);
        var fills = new[]
        {
            new ExchangeFillPnl(first, "SELL", 0.15005m, 103m, 0m),
            new ExchangeFillPnl(first.AddMinutes(20), "BUY", 0.159m, 40m, -0.36m),
            new ExchangeFillPnl(first.AddMinutes(21), "BUY", 0.160m, 63m, -0.63m),
            new ExchangeFillPnl(later, "SELL", 0.080m, 50m, 0m),
            new ExchangeFillPnl(later.AddHours(2), "BUY", 0.100m, 50m, -1.20m)
        };
        PumpRealizedPnl.TryClose(fills, first.AddMinutes(-1), 103m, out var close).Should().BeTrue();
        close.Realized.Should().Be(-0.99m);
        close.ClosedAt.Should().Be(first.AddMinutes(21));
        PumpRealizedPnl.EntryQuantity(fills, first.AddMinutes(-1)).Should().Be(103m);
    }

    [Fact]
    public void An_open_fill_alone_is_not_a_result()
    {
        var fills = new[] { new ExchangeFillPnl(Start, "SELL", 0.043m, 100m, 0m) };
        PumpRealizedPnl.TrySum(fills, Start.AddMinutes(-1), DateTimeOffset.MaxValue, out _).Should().BeFalse();
    }

    [Fact]
    public void Desk_keeps_a_fill_and_only_the_latest_skip_per_coin()
    {
        var older = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        var newer = older.AddMinutes(5);
        var merged = UnexplainedPumpDeskStore.Merge(
            [
                new UnexplainedPumpCheckRow { At = newer, Coin = "IRYSUSDT", OrderState = "NotSent", Reason = "still high" }
            ],
            [
                new UnexplainedPumpCheckRow { At = older, Coin = "IRYSUSDT", OrderState = "NotSent", Reason = "old" },
                new UnexplainedPumpCheckRow { At = older, Coin = "IRYSUSDT", OrderState = "Filled", Reason = "entered" }
            ]);
        merged.Should().HaveCount(2);
        merged.Should().Contain(row => row.OrderState == "Filled");
        merged.Should().Contain(row => row.Reason == "still high");
        merged.Should().NotContain(row => row.Reason == "old");
    }

    [Fact]
    public void Rss_item_keeps_the_title()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss><channel><item><title>IRYS lists on Upbit</title><pubDate>Tue, 20 Oct 2026 12:00:00 GMT</pubDate></item></channel></rss>
            """;
        var rows = PumpNewsSearch.ReadRss(xml, "google-news");
        rows.Should().ContainSingle();
        rows![0].Title.Should().Be("IRYS lists on Upbit");
    }

    [Fact]
    public void A_short_closed_from_outside_may_be_sent_once_more_the_same_day()
    {
        var day = new DateTimeOffset(2026, 10, 28, 12, 30, 0, TimeSpan.Zero);
        UnexplainedPumpShort.MaySendToday(0, 0, 1, out var retry).Should().BeTrue();
        retry.Should().Be(1);
        var again = UnexplainedPumpShort.ClientOrderId("EVAAUSDT", day, retry);
        again.Should().StartWith("pump-");
        again.Should().NotBe(UnexplainedPumpShort.ClientOrderId("EVAAUSDT", day));
        UnexplainedPumpShort.MaySendToday(1, 0, 0, out _).Should().BeFalse();
        UnexplainedPumpShort.MaySendToday(0, 1, 0, out _).Should().BeFalse();
        UnexplainedPumpShort.MaySendToday(0, 0, 2, out _).Should().BeFalse();
    }

    private static UnexplainedPumpDecision Decide(PumpMove? move, bool stalled, PumpNewsVerdict news) =>
        UnexplainedPumpShort.Decide("IRYSUSDT", move, stalled, news, move?.PumpHigh * 0.8m ?? 0m, 20_000_000m, Options);

    private static PumpMove? Move(decimal high24h, decimal last, decimal base24) =>
        UnexplainedPumpShort.Measure(high24h, last, Change(last, base24), Options.MinRise);

    private static decimal Change(decimal last, decimal basePrice) =>
        (last / basePrice - 1m) * 100m;

    private static MarketCandle Bar(decimal high) =>
        new()
        {
            OpenTime = Start,
            CloseTime = Start.AddMinutes(15),
            Open = high,
            High = high,
            Low = high,
            Close = high,
            IsClosed = true,
            Timeframe = Timeframe.FifteenMinutes
        };
}
