using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class SignalQualityTests
{
    [Fact]
    public void A_later_bar_does_not_change_pre_entry_features()
    {
        var candles = Bars(80);
        var hourly = Bars(40, hours: 1);
        var before = SignalQualityFeatures.Compute(new CausalIndicatorCache(candles), new CausalIndicatorCache(hourly), new CausalIndicatorCache(hourly), new CausalIndicatorCache(hourly), new CausalIndicatorCache(hourly), 50, true);
        candles.Add(Bars(1, 80)[0]);
        var after = SignalQualityFeatures.Compute(new CausalIndicatorCache(candles), new CausalIndicatorCache(hourly), new CausalIndicatorCache(hourly), new CausalIndicatorCache(hourly), new CausalIndicatorCache(hourly), 50, true);
        after["signed_ret_5"].Should().Be(before["signed_ret_5"]);
        after["adx"].Should().Be(before["adx"]);
        after["signed_vwap_dist"].Should().Be(before["signed_vwap_dist"]);
        after["bos_aligned"].Should().Be(before["bos_aligned"]);
    }

    [Fact]
    public void Repeatable_requires_every_pre_registered_bar()
    {
        SignalQualityFeatures.Classify(0.08m, 300, 0.07m, 250, 0.06m, 220, 3, 2, 2).Should().Be("REPEATABLE");
        SignalQualityFeatures.Classify(0.08m, 300, 0.07m, 250, 0.06m, 220, 3, 1, 2).Should().Be("FAMILY_SPECIFIC");
        SignalQualityFeatures.Classify(0.08m, 300, 0.07m, 250, -0.06m, 220, 3, 2, 2).Should().Be("OOS_ONLY");
        SignalQualityFeatures.Classify(0.01m, 300, 0.01m, 250, 0.01m, 220, 0, 0, 0).Should().Be("NO_EVIDENCE");
    }

    [Fact]
    public void Feature_list_is_fixed()
    {
        SignalQualityCatalog.Features.Should().HaveCount(37);
        SignalQualityCatalog.MinimumAbsRho.Should().Be(0.05m);
    }

    private static List<MarketCandle> Bars(int count, int offset = 0, int hours = 0)
    {
        var list = new List<MarketCandle>();
        var step = hours == 0 ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(hours);
        for (var i = 0; i < count; i++)
        {
            var n = i + offset;
            var close = 100m + n * 0.1m;
            var open = DateTimeOffset.UnixEpoch + step * n;
            list.Add(new MarketCandle
            {
                OpenTime = open,
                CloseTime = open + step,
                Open = close - 0.2m,
                High = close + 0.5m,
                Low = close - 0.4m,
                Close = close,
                Volume = 10m + (n % 7),
                IsClosed = true
            });
        }

        return list;
    }
}
