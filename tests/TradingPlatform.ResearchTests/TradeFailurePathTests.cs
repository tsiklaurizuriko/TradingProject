using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class TradeFailurePathTests
{
    [Fact]
    public void Five_bar_excursion_ignores_a_later_bar()
    {
        var candles = Flat(12, 100m);
        var before = TradeFailurePath.Measure(candles, 0, 2, true, 100m, "Stop loss", -1m, 0.1m, 0.05m, 0m, -1m);
        candles[5].High = 130m;
        var after = TradeFailurePath.Measure(candles, 0, 2, true, 100m, "Stop loss", -1m, 0.1m, 0.05m, 0m, -1m);
        after.Mfe5.Should().Be(before.Mfe5);
    }

    [Fact]
    public void Cost_dominated_outranks_a_later_recovery()
    {
        var facts = Sample() with { Gross = 0.2m, Net = -0.1m, ImmediateWrong = true, RecoveredAfterStop = true };
        TradeFailurePath.Classify(facts).Should().Be("COST_DOMINATED");
    }

    [Fact]
    public void Immediate_wrong_outranks_a_later_recovery()
    {
        var facts = Sample() with { ImmediateWrong = true, RecoveredAfterStop = true, Reason = "Stop loss" };
        TradeFailurePath.Classify(facts).Should().Be("IMMEDIATELY_WRONG");
    }

    [Fact]
    public void Stop_recovery_is_not_a_new_stop_distance()
    {
        var facts = Sample() with { Reason = "Stop loss", RecoveredAfterStop = true };
        TradeFailurePath.Classify(facts).Should().Be("RIGHT_DIRECTION_STOP_TOO_TIGHT");
        TradeFailureCatalog.StopDistance.Should().Be(0.02m);
        TradeFailureCatalog.TargetDistance.Should().Be(0.04m);
    }

    private static FailureFacts Sample() =>
        new(0.02m, 0.004m, 1, 0, 1, null, 0m, 0.02m, false, false, 0m, 0m, 0m, 0m, 0m, 2, "Stop loss", -1m, 0.1m, 0.05m, 0m, -1m, false, false);

    private static List<MarketCandle> Flat(int count, decimal price)
    {
        var list = new List<MarketCandle>();
        for (var i = 0; i < count; i++)
        {
            var open = DateTimeOffset.UnixEpoch.AddMinutes(i * 15);
            list.Add(new MarketCandle
            {
                OpenTime = open,
                CloseTime = open.AddMinutes(15),
                Open = price,
                High = price + 0.2m,
                Low = price - 0.2m,
                Close = price,
                Volume = 1m,
                IsClosed = true
            });
        }

        return list;
    }
}
