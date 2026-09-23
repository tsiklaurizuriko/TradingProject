using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class FinalFiveSignalTests
{
    [Fact]
    public void Later_bar_does_not_change_an_earlier_signal()
    {
        var prefix = Bars(120);
        var extended = prefix.Concat(Bars(8, prefix.Count)).ToList();
        var early = FinalFiveSignals.Build("FF-RANGE-RELEASE|1h", new CausalIndicatorCache(prefix), new CausalIndicatorCache(prefix));
        var later = FinalFiveSignals.Build("FF-RANGE-RELEASE|1h", new CausalIndicatorCache(extended), new CausalIndicatorCache(extended));
        later.Take(early.Length).Should().Equal(early);
    }

    [Fact]
    public void Sweep_is_long_only_and_requires_hourly_bias()
    {
        var entry = TrendBars(80, up: true);
        var flatHour = FlatBars(40);
        var upHour = TrendBars(40, up: true);
        var flat = FinalFiveSignals.Build("FF-SWEEP-HOURLY|15m", new CausalIndicatorCache(entry), new CausalIndicatorCache(flatHour));
        var aligned = FinalFiveSignals.Build("FF-SWEEP-HOURLY|15m", new CausalIndicatorCache(entry), new CausalIndicatorCache(upHour));
        flat.Should().NotContain(SignalType.Sell);
        aligned.Should().NotContain(SignalType.Sell);
        aligned.Count(x => x == SignalType.Buy).Should().BeGreaterThanOrEqualTo(flat.Count(x => x == SignalType.Buy));
    }

    [Fact]
    public void Primary_ids_are_fixed_and_the_third_symbol_is_the_volume_rank()
    {
        FinalFiveCatalog.Symbols.Should().Equal("BTCUSDT", "ETHUSDT", "BNBUSDT");
        FinalFiveCatalog.Primary.Select(x => x.CandidateId).Should().Equal(
            "FF-SWEEP-HOURLY|15m",
            "FF-EXPANSION-BOS|15m",
            "FF-RSI-QUIET|1h",
            "FF-FAILED-DOWN|15m",
            "FF-RANGE-RELEASE|1h");
        FinalFiveCatalog.RejectedBeforeRun.Should().HaveCount(20);
        FinalFiveCatalog.SurvivorRule.Should().Contain("before any OOS");
    }

    private static List<MarketCandle> Bars(int count, int offset = 0) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var n = i + offset;
            var close = 100m + (n % 7) - 3m;
            return new MarketCandle
            {
                OpenTime = DateTimeOffset.UnixEpoch.AddHours(n),
                CloseTime = DateTimeOffset.UnixEpoch.AddHours(n + 1),
                Open = close - 0.4m,
                High = close + 1m,
                Low = close - 1m,
                Close = close,
                Volume = 10m + n,
                IsClosed = true
            };
        }).ToList();

    private static List<MarketCandle> TrendBars(int count, bool up)
    {
        var bars = new List<MarketCandle>();
        var price = 100m;
        for (var i = 0; i < count; i++)
        {
            price += up ? 1.2m : -1.2m;
            if (i % 11 == 10)
            {
                price += up ? -6m : 6m;
            }

            bars.Add(new MarketCandle
            {
                OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 15),
                CloseTime = DateTimeOffset.UnixEpoch.AddMinutes((i + 1) * 15),
                Open = price - 0.5m,
                High = price + 0.8m,
                Low = price - 0.8m,
                Close = price,
                Volume = 20m,
                IsClosed = true
            });
        }

        return bars;
    }

    private static List<MarketCandle> FlatBars(int count) =>
        Enumerable.Range(0, count).Select(i => new MarketCandle
        {
            OpenTime = DateTimeOffset.UnixEpoch.AddHours(i),
            CloseTime = DateTimeOffset.UnixEpoch.AddHours(i + 1),
            Open = 100m,
            High = 100.2m,
            Low = 99.8m,
            Close = 100m,
            Volume = 5m,
            IsClosed = true
        }).ToList();
}
