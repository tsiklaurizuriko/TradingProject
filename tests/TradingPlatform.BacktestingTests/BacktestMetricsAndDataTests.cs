using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using Xunit;

namespace TradingPlatform.BacktestingTests;

public sealed class BacktestMetricsAndDataTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(95, 100, false, 95)]
    [InlineData(95, 90, false, 90)]
    [InlineData(105, 100, true, 105)]
    [InlineData(105, 110, true, 110)]
    public void Stop_fills_at_the_stop_or_at_the_open_when_the_bar_gaps_through_it(double stop, double open, bool isShort, double expected) =>
        BacktestReplay.StopTouchPrice((decimal)stop, (decimal)open, isShort).Should().Be((decimal)expected);

    [Fact]
    public void Occupancy_stop_inside_the_bar_fills_at_the_stop_not_the_low()
    {
        var bars = new List<OccupancyBar>
        {
            Bar(0, 100m, 100.5m, 99.5m, 100m),
            Bar(1, 100m, 100.5m, 99.5m, 100m),
            Bar(2, 100m, 100.5m, 50m, 60m)
        };
        var trade = RunOneLong(bars);

        trade.Reason.Should().Be("Stop loss");
        trade.ExitPrice.Should().BeGreaterThan(90m, "a stop inside the bar triggers at the stop price, the bar low is not a fill");
        trade.ExitPrice.Should().BeLessThan(trade.EntryPrice);
    }

    [Fact]
    public void Occupancy_stop_after_a_gap_fills_at_the_open_with_slippage_once()
    {
        var bars = new List<OccupancyBar>
        {
            Bar(0, 100m, 100.5m, 99.5m, 100m),
            Bar(1, 100m, 100.5m, 99.5m, 100m),
            Bar(2, 80m, 81m, 79m, 80m)
        };
        var settings = Settings();
        var trade = RunOneLong(bars, settings);

        trade.Reason.Should().Be("Stop loss");
        trade.ExitPrice.Should().Be(Math.Round(80m * (1m - settings.SlippagePercent / 100m), 8),
            "the stop cannot fill above a gap open, and slippage applies once on top of the open");
    }

    [Fact]
    public void Daily_returns_start_from_initial_equity_in_day_order()
    {
        var closes = new Dictionary<DateOnly, decimal>
        {
            [new DateOnly(2026, 1, 2)] = 1100m,
            [new DateOnly(2026, 1, 1)] = 1000m * 1.05m
        };

        var returns = DailyReturnMetrics.Returns(closes, 1000m);

        returns.Should().HaveCount(2);
        returns[0].Should().Be(0.05m);
        returns[1].Should().BeApproximately(1100m / 1050m - 1m, 0.0000001m);
    }

    [Fact]
    public void Sharpe_and_sortino_are_annualized_from_daily_returns()
    {
        decimal[] returns = [0.01m, -0.005m, 0.02m, -0.01m];
        var mean = returns.Average();
        var stdev = (decimal)Math.Sqrt((double)(returns.Sum(r => (r - mean) * (r - mean)) / 3m));
        var downside = (decimal)Math.Sqrt((double)((0.005m * 0.005m + 0.01m * 0.01m) / 4m));

        var ratios = DailyReturnMetrics.From(returns);

        ratios.Days.Should().Be(4);
        ratios.Sharpe.Should().BeApproximately(mean / stdev * (decimal)Math.Sqrt(365d), 0.001m);
        ratios.Sortino.Should().BeApproximately(mean / downside * (decimal)Math.Sqrt(365d), 0.001m);
    }

    [Fact]
    public void Too_few_days_or_no_losing_day_gives_no_ratio_instead_of_a_huge_one()
    {
        DailyReturnMetrics.From([0.01m, 0.02m]).Sharpe.Should().BeNull();
        var noLoss = DailyReturnMetrics.From([0.01m, 0.02m, 0.03m]);
        noLoss.Sharpe.Should().NotBeNull();
        noLoss.Sortino.Should().BeNull();
        DailyReturnMetrics.From([0.01m, 0.01m, 0.01m]).Sharpe.Should().BeNull();
    }

    [Fact]
    public void Sharpe_does_not_grow_with_the_number_of_trades_on_the_same_daily_equity()
    {
        var closes = Enumerable.Range(0, 30).ToDictionary(
            day => DateOnly.FromDateTime(Start.AddDays(day).UtcDateTime),
            day => 1000m + day * 3m + (day % 3 == 0 ? -8m : 0m));

        var once = DailyReturnMetrics.From(DailyReturnMetrics.Returns(closes, 1000m));
        var again = DailyReturnMetrics.From(DailyReturnMetrics.Returns(new Dictionary<DateOnly, decimal>(closes), 1000m));

        once.Sharpe.Should().NotBeNull();
        again.Sharpe.Should().Be(once.Sharpe);
    }

    [Fact]
    public void Kline_pages_are_deduplicated_and_sorted()
    {
        var a = Candle(0, 100m);
        var b = Candle(1, 101m);
        var bAgain = Candle(1, 102m);
        var c = Candle(2, 103m);

        var series = KlineSeries.Normalize([c, a, b, bAgain], out var duplicates);

        duplicates.Should().Be(1);
        series.Select(x => x.Close).Should().Equal(100m, 102m, 103m);
    }

    [Fact]
    public void Missing_klines_are_reported_as_gaps()
    {
        var series = new[] { Candle(0, 1m), Candle(1, 1m), Candle(5, 1m), Candle(6, 1m), Candle(8, 1m) };

        var quality = KlineSeries.Inspect(series, Timeframe.FiveMinutes);

        quality.MissingBars.Should().Be(4);
        quality.Gaps.Should().HaveCount(2);
        quality.Gaps[0].ExpectedOpen.Should().Be(Start.AddMinutes(10));
        quality.Gaps[0].MissingBars.Should().Be(3);
        quality.MissingPercent.Should().Be(Math.Round(4m * 100m / 9m, 4));
    }

    [Fact]
    public void A_complete_series_has_no_gaps()
    {
        var series = Enumerable.Range(0, 20).Select(i => Candle(i, 1m)).ToList();
        KlineSeries.Inspect(series, Timeframe.FiveMinutes).Gaps.Should().BeEmpty();
    }

    private static ReplayTrade RunOneLong(List<OccupancyBar> bars, ReplaySettings? settings = null)
    {
        var intents = new[] { new OccupancyIntent(Start, Start.AddMinutes(5), "A", "BTCUSDT", SignalType.Buy, 100m) };
        var result = PortfolioOccupancyReplay.Run(intents, bars, settings ?? Settings());
        result.Trades.Should().ContainSingle();
        return result.Trades[0];
    }

    private static ReplaySettings Settings() =>
        StrategyValidation.LowIsolatedRisk(Start, Start.AddHours(2)) with { MaxSimultaneousPositions = 5 };

    private static OccupancyBar Bar(int i, decimal open, decimal high, decimal low, decimal close) =>
        new(Start.AddMinutes(i * 5), Start.AddMinutes(i * 5 + 5), "BTCUSDT", open, high, low, close);

    private static MarketCandle Candle(int i, decimal close) => new()
    {
        Timeframe = Timeframe.FiveMinutes,
        OpenTime = Start.AddMinutes(i * 5),
        CloseTime = Start.AddMinutes(i * 5 + 5).AddMilliseconds(-1),
        Open = close,
        High = close,
        Low = close,
        Close = close,
        IsClosed = true
    };
}
