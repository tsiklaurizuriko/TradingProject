using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Indicators;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class IndicatorTests
{
    [Fact]
    public void Sma_of_constant_series_equals_the_constant()
    {
        var candles = Enumerable.Range(0, 20).Select(i => Candle(10m, i)).ToList();
        var sma = new SmaIndicator(5).Compute(candles);
        sma.Last().Should().Be(10m);
        sma[3].Should().BeNull();
    }

    [Fact]
    public void Ema_reacts_to_a_price_increase_after_warmup()
    {
        var candles = Enumerable.Range(0, 10).Select(i => Candle(i < 5 ? 10m : 20m, i)).ToList();
        var ema = new EmaIndicator(3).Compute(candles);
        ema.Last().Should().NotBeNull();
        ema.Last()!.Value.Should().BeGreaterThan(10m);
    }

    [Fact]
    public void Rsi_is_high_on_a_monotonic_up_series()
    {
        var candles = Enumerable.Range(1, 30).Select(i => Candle(i, i)).ToList();
        var rsi = new RsiIndicator(14).Compute(candles);
        rsi.Last().Should().NotBeNull();
        rsi.Last()!.Value.Should().BeGreaterThan(70m);
    }

    private static MarketCandle Candle(decimal close, int i) => new()
    {
        Open = close,
        High = close,
        Low = close,
        Close = close,
        Volume = 1,
        OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
        CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5),
        IsClosed = true
    };
}
