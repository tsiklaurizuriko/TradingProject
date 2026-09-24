using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class EdgeFeatureTests
{
    [Fact]
    public void A_later_bar_does_not_change_the_signal_bar_labels()
    {
        var prefix = Bars(80);
        var extended = prefix.Concat(Bars(12, prefix.Count)).ToList();
        var hourly = Bars(40, 0, hours: 1);
        var early = EdgeFeatureBuilder.Build(
            "PA_SWEEP", "FF-SWEEP-HOURLY", "id", "ETHUSDT", "15m", "LONG", "IS", 1,
            prefix[^1].CloseTime, 1m, 0.1m, 0.05m,
            new CausalIndicatorCache(prefix), new CausalIndicatorCache(hourly), new CausalIndicatorCache(hourly), 60);
        var later = EdgeFeatureBuilder.Build(
            "PA_SWEEP", "FF-SWEEP-HOURLY", "id", "ETHUSDT", "15m", "LONG", "IS", 1,
            prefix[^1].CloseTime, 1m, 0.1m, 0.05m,
            new CausalIndicatorCache(extended), new CausalIndicatorCache(hourly), new CausalIndicatorCache(hourly), 60);
        later.VolHigh.Should().Be(early.VolHigh);
        later.AdxTrend.Should().Be(early.AdxTrend);
        later.Expansion.Should().Be(early.Expansion);
        later.CompressionRelease.Should().Be(early.CompressionRelease);
        later.HtfAligned.Should().Be(early.HtfAligned);
    }

    [Fact]
    public void Volatility_bins_use_the_zero_to_one_percentile()
    {
        EdgeFeatureBuilder.VolBucket(0.10m).Should().Be("LOW");
        EdgeFeatureBuilder.VolBucket(0.50m).Should().Be("NORMAL");
        EdgeFeatureBuilder.VolBucket(0.80m).Should().Be("HIGH");
        EdgeFeatureBuilder.VolBucket(40m).Should().Be("HIGH");
    }

    [Fact]
    public void Compression_release_does_not_read_the_next_bar()
    {
        var candles = Bars(40);
        var before = EdgeFeatureBuilder.Build(
            "PA_CONTINUATION", "FF-RANGE-RELEASE", "id", "BTCUSDT", "1h", "Long", "OOS", 4,
            candles[30].CloseTime, -2m, 0.2m, 0.1m,
            new CausalIndicatorCache(candles), new CausalIndicatorCache(candles), new CausalIndicatorCache(candles), 30);
        candles[31].High = candles[31].Close + 20m;
        candles[31].Low = candles[31].Close - 20m;
        var after = EdgeFeatureBuilder.Build(
            "PA_CONTINUATION", "FF-RANGE-RELEASE", "id", "BTCUSDT", "1h", "Long", "OOS", 4,
            candles[30].CloseTime, -2m, 0.2m, 0.1m,
            new CausalIndicatorCache(candles), new CausalIndicatorCache(candles), new CausalIndicatorCache(candles), 30);
        after.CompressionRelease.Should().Be(before.CompressionRelease);
        after.Side.Should().Be("LONG");
    }

    private static List<MarketCandle> Bars(int count, int offset = 0, int hours = 0)
    {
        var list = new List<MarketCandle>();
        for (var i = 0; i < count; i++)
        {
            var n = i + offset;
            var close = 100m + (n % 5) * 0.4m;
            var step = hours == 0 ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(hours);
            var open = DateTimeOffset.UnixEpoch + step * n;
            list.Add(new MarketCandle
            {
                OpenTime = open,
                CloseTime = open + step,
                Open = close - 0.3m,
                High = close + 0.8m,
                Low = close - 0.8m,
                Close = close,
                Volume = 10m + n,
                IsClosed = true
            });
        }

        return list;
    }
}
