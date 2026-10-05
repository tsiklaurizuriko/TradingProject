using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingPlatform.Binance;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.Risk;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class RiskLossLimitTests
{
    [Theory]
    [InlineData(100, 100.1, 9.995)]
    [InlineData(50_000, 50_001, 0.19999)]
    public void Spread_is_measured_against_mid(decimal bid, decimal ask, decimal expectedBps)
    {
        EntryMarketGuard.SpreadBps(bid, ask).Should().BeApproximately(expectedBps, 0.001m);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0.0, 1)]
    [InlineData(101.0, 100)]
    public void Missing_or_crossed_books_have_no_spread(double? bid, decimal ask)
    {
        EntryMarketGuard.SpreadBps(bid is { } b ? (decimal)b : null, ask).Should().BeNull();
    }

    [Fact]
    public void Range_shock_compares_the_last_bar_to_the_median()
    {
        var bars = Enumerable.Range(0, 40).Select(_ => new EntryBar(101m, 99m, 100m)).ToList();
        EntryMarketGuard.RangeShock(bars).Should().Be(1m);

        bars.Add(new EntryBar(110m, 98m, 109m));
        EntryMarketGuard.RangeShock(bars).Should().Be(6m, "true range 12 over a median of 2");
    }

    [Fact]
    public void Range_shock_needs_history()
    {
        EntryMarketGuard.RangeShock([new EntryBar(1m, 1m, 1m), new EntryBar(2m, 1m, 2m)]).Should().BeNull();
    }

    [Theory]
    [InlineData(null, null, true, "unknown")]
    [InlineData(null, null, false, null)]
    [InlineData(20.0, 1.0, false, "Spread")]
    [InlineData(10.0, 6.0, true, "usual range")]
    [InlineData(10.0, 2.0, true, null)]
    public void Entry_guard_blocks_wide_books_and_shock_bars(double? spread, double? shock, bool live, string? expected)
    {
        var reason = EntryMarketGuard.Reject(
            spread is { } s ? (decimal)s : null,
            shock is { } k ? (decimal)k : null,
            maxSpreadBps: 15m,
            maxRangeShock: 5m,
            requireBook: live);

        if (expected is null)
        {
            reason.Should().BeNull();
        }
        else
        {
            reason.Should().Contain(expected);
        }
    }

    [Fact]
    public void Slippage_estimate_is_at_least_half_the_spread()
    {
        EntryMarketGuard.SlippagePercent(null, 0.05m).Should().Be(0.05m);
        EntryMarketGuard.SlippagePercent(2m, 0.05m).Should().Be(0.05m);
        EntryMarketGuard.SlippagePercent(30m, 0.05m).Should().Be(0.15m);
    }

    [Fact]
    public void Taker_fee_is_read_as_percent_and_bad_values_are_rejected()
    {
        BinanceLiveExchangeConnector.ReadTakerFeePercent(Json("""{"symbol":"BTCUSDT","makerCommissionRate":"0.000200","takerCommissionRate":"0.000500"}""")).Should().Be(0.05m);
        BinanceLiveExchangeConnector.ReadTakerFeePercent(Json("""{"takerCommissionRate":"5"}""")).Should().BeNull();
        BinanceLiveExchangeConnector.ReadTakerFeePercent(Json("""{"code":-2015}""")).Should().BeNull();
    }

    [Theory]
    [InlineData(10_000, 0.004, 0)]
    [InlineData(60_000, 0.005, 50)]
    [InlineData(5_000_000, 0.01, 800)]
    public void Maintenance_bracket_is_the_one_holding_the_notional(decimal notional, decimal rate, decimal cum)
    {
        var payload = Json("""
            [{"symbol":"BTCUSDT","brackets":[
              {"bracket":1,"initialLeverage":125,"notionalCap":50000,"notionalFloor":0,"maintMarginRatio":0.004,"cum":0.0},
              {"bracket":2,"initialLeverage":100,"notionalCap":600000,"notionalFloor":50000,"maintMarginRatio":0.005,"cum":50.0},
              {"bracket":3,"initialLeverage":75,"notionalCap":3000000,"notionalFloor":600000,"maintMarginRatio":0.0065,"cum":950.0},
              {"bracket":4,"initialLeverage":50,"notionalCap":4000000,"notionalFloor":3000000,"maintMarginRatio":0.01,"cum":800.0}
            ]}]
            """);

        BinanceLiveExchangeConnector.ReadMaintenanceBracket(payload, "BTCUSDT", notional)
            .Should().Be(new Application.Abstractions.Exchange.MaintenanceBracket(rate, cum));
    }

    [Fact]
    public void Maintenance_bracket_for_another_coin_is_not_used()
    {
        var payload = Json("""[{"symbol":"ETHUSDT","brackets":[{"notionalCap":50000,"notionalFloor":0,"maintMarginRatio":0.005,"cum":0}]}]""");

        BinanceLiveExchangeConnector.ReadMaintenanceBracket(payload, "BTCUSDT", 1_000m).Should().BeNull();
    }

    [Theory]
    [InlineData(1_000, 900, 0, 10.0)]
    [InlineData(1_000, 1_100, 0, 0.0)]
    [InlineData(1_000, 900, 20, null)]
    public void Drawdown_needs_a_recent_series(decimal peak, decimal equity, int lastPointMinutesAgo, double? expected)
    {
        var now = DateTimeOffset.UtcNow;

        BotEngine.DrawdownPercent(peak, now.AddMinutes(-lastPointMinutesAgo), equity, now)
            .Should().Be(expected is { } e ? (decimal)e : null);
        BotEngine.DrawdownPercent(null, null, equity, now).Should().BeNull();
    }

    [Theory]
    [InlineData("2026-10-05T10:00:00Z", "2026-10-05T00:00:00Z")]
    [InlineData("2026-10-11T23:59:00Z", "2026-10-05T00:00:00Z")]
    [InlineData("2026-10-12T00:00:00Z", "2026-10-12T00:00:00Z")]
    public void Week_starts_monday_utc(string now, string expected)
    {
        BotEngine.WeekStart(DateTimeOffset.Parse(now)).Should().Be(DateTimeOffset.Parse(expected));
    }

    [Fact]
    public async Task Equity_series_is_throttled_and_returns_the_peak()
    {
        await using var db = new TradingDbContext(new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase($"equity-{Guid.NewGuid():N}")
            .Options);
        var user = new User { Email = "a@b", NormalizedEmail = "A@B", DisplayName = "A", PasswordHash = "x" };
        var account = new ExchangeAccount { User = user, UserId = user.Id, Name = "Live", ApiKeyFingerprint = "f" };
        db.Users.Add(user);
        db.ExchangeAccounts.Add(account);
        await db.SaveChangesAsync();
        var store = new TradingStore(db);
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var gap = TimeSpan.FromMinutes(5);

        await store.RecordEquityPointAsync(account.Id, TradingMode.Live, 1_000m, start, gap);
        await store.RecordEquityPointAsync(account.Id, TradingMode.Live, 1_500m, start.AddMinutes(1), gap);
        await store.SaveChangesAsync();
        await store.RecordEquityPointAsync(account.Id, TradingMode.Live, 1_200m, start.AddMinutes(6), gap);
        await store.RecordEquityPointAsync(account.Id, TradingMode.Live, 0m, start.AddMinutes(20), gap);
        await store.SaveChangesAsync();

        (await db.BalanceSnapshots.CountAsync()).Should().Be(2, "the 1-minute point is inside the gap and a zero equity is not a reading");
        var (peak, lastAt) = await store.GetEquityPeakAsync(account.Id, TradingMode.Live, start.AddDays(-1));
        peak.Should().Be(1_200m);
        lastAt.Should().Be(start.AddMinutes(6));
        (await store.GetEquityPeakAsync(account.Id, TradingMode.Paper, start.AddDays(-1))).Peak.Should().BeNull();
    }

    [Fact]
    public void Live_guard_applies_weekly_and_drawdown_limits()
    {
        var profile = new RiskProfile
        {
            Name = "LOW",
            RiskPerTradePercent = 0.5m,
            StopLossPercent = 2m,
            TakeProfitPercent = 4m,
            MaxLeverage = 3m,
            MaxDailyLossPercent = 3m,
            MaxWeeklyLossPercent = 8m,
            MaxDrawdownPercent = 15m,
            MaxPortfolioRiskPercent = 4m,
            MaxSimultaneousPositions = 2,
            AllowLive = true
        };
        LiveRiskFacts Facts(decimal weekly, decimal? drawdown) => new(
            1_000m, 0m, 0, 0, 0m, 0.5m, 3m, 2m, 0.01m, 500m, 0.001m, 5m, 1_000m, 170m, false, 0, null, DateTimeOffset.UtcNow, true, weekly, drawdown);

        RiskLiveGuard.Reject(TradingMode.Live, profile, Facts(-79m, 14m)).Should().BeNull();
        RiskLiveGuard.Reject(TradingMode.Live, profile, Facts(-80m, null)).Should().Contain("Weekly");
        RiskLiveGuard.Reject(TradingMode.Live, profile, Facts(0m, 15m)).Should().Contain("drawdown");
    }

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
}
