using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class FlatRangeStrategyTests
{
    [Fact]
    public void Flat_close_near_the_low_locks_the_band_as_stop_and_take_profit()
    {
        var candles = Band(80, 100m, 102m, 101m);
        candles[^1].Close = 100.30m;
        var detail = FlatRangeStrategy.Evaluate(candles, candles.Count - 1, false, "Both", null);

        detail.Signal.Should().Be(SignalType.Buy);
        detail.SuggestedStop.Should().Be(100m);
        detail.SuggestedTakeProfit.Should().Be(102m);
    }

    [Fact]
    public void Flat_close_near_the_high_shorts_the_upper_bound()
    {
        var candles = Band(80, 100m, 102m, 101m);
        candles[^1].Close = 101.70m;
        var detail = FlatRangeStrategy.Evaluate(candles, candles.Count - 1, false, "Both", null);

        detail.Signal.Should().Be(SignalType.Sell);
        detail.SuggestedStop.Should().Be(102m);
        detail.SuggestedTakeProfit.Should().Be(100m);
    }

    [Fact]
    public void Open_flat_holds_until_the_24_hour_stop()
    {
        var candles = Band(80, 100m, 102m, 101m);
        var open = candles[^1].CloseTime.AddHours(-23);
        FlatRangeStrategy.Evaluate(candles, candles.Count - 1, true, "Both", open).Signal.Should().Be(SignalType.Hold);
        var expired = candles[^1].CloseTime.AddHours(-24);
        FlatRangeStrategy.Evaluate(candles, candles.Count - 1, true, "Both", expired).Signal.Should().Be(SignalType.Exit);
    }

    private static List<MarketCandle> Band(int count, decimal low, decimal high, decimal close)
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var rows = new List<MarketCandle>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new MarketCandle
            {
                OpenTime = start.AddHours(i),
                CloseTime = start.AddHours(i + 1).AddTicks(-1),
                Open = close,
                High = high,
                Low = low,
                Close = close,
                Volume = 1m,
                IsClosed = true
            });
        }

        return rows;
    }
}
