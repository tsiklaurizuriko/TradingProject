using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class MicrostructureFeatureTests
{
    [Fact]
    public void Alignment_keeps_a_print_inside_its_bar_and_rejects_the_next_bar()
    {
        var candles = Bars(3);
        var inside = candles[1].OpenTime.AddMinutes(1);
        var after = candles[1].CloseTime.AddMilliseconds(1);
        var aligned = MicrostructureFeatures.AlignToBar([(inside, 5m), (after, 9m)], candles);
        aligned[0].Should().BeNull();
        aligned[1].Should().Be(5m);
        aligned[2].Should().Be(9m);
    }

    [Fact]
    public void Future_funding_open_interest_basis_depth_and_taker_are_not_visible()
    {
        var candles = Bars(4);
        var oi = MicrostructureFeatures.AlignToBar([(candles[3].OpenTime.AddMinutes(1), 100m)], candles);
        var basis = MicrostructureFeatures.AlignToBar([(candles[3].CloseTime, 0.01m)], candles);
        var depth = MicrostructureFeatures.AlignToBar([(candles[3].OpenTime.AddMinutes(2), 0.4m)], candles);
        var funding = new List<FundingPoint> { new("BTCUSDT", candles[3].CloseTime, 0.002m, null) };
        var takerImbalance = new decimal?[] { 0.1m, null, null, 0.8m };
        var takerRatio = new decimal?[] { 0.55m, null, null, 0.9m };
        var atSignal = MicrostructureFeatures.Compute(1, oi, funding, candles[1].CloseTime, takerImbalance, takerRatio, basis, depth);
        atSignal["oi_change_1"].Should().BeNull();
        atSignal["funding_rate"].Should().BeNull();
        atSignal["normalized_basis"].Should().BeNull();
        atSignal["depth_imbalance_1pct"].Should().BeNull();
        atSignal["taker_imbalance"].Should().BeNull();
        atSignal["taker_buy_ratio"].Should().BeNull();
    }

    [Fact]
    public void Zero_taker_buy_is_missing_not_a_zero_imbalance()
    {
        var missing = MicrostructureFeatures.Taker(10m, 0m);
        missing.Ratio.Should().BeNull();
        missing.Imbalance.Should().BeNull();
        var present = MicrostructureFeatures.Taker(10m, 8m);
        present.Ratio.Should().Be(0.8m);
        present.Imbalance.Should().Be(0.6m);
    }

    [Fact]
    public void Feature_list_and_repeatable_rule_stay_fixed()
    {
        MicrostructureCatalog.Features.Should().HaveCount(15);
        MicrostructureCatalog.Version.Should().Be("microstructure-v1");
        SignalQualityFeatures.Classify(0.08m, 300, 0.07m, 250, 0.06m, 220, 3, 2, 2).Should().Be("REPEATABLE");
        SignalQualityFeatures.Classify(0.02m, 300, 0.02m, 250, 0.02m, 220, 0, 0, 0).Should().Be("NO_EVIDENCE");
    }

    private static List<MarketCandle> Bars(int count)
    {
        var list = new List<MarketCandle>();
        for (var i = 0; i < count; i++)
        {
            var open = DateTimeOffset.UnixEpoch.AddMinutes(15 * i);
            list.Add(new MarketCandle
            {
                OpenTime = open,
                CloseTime = open.AddMinutes(15).AddMilliseconds(-1),
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                Volume = 10m,
                IsClosed = true
            });
        }

        return list;
    }
}
