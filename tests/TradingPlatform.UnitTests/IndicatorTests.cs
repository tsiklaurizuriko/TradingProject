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

    [Fact]
    public void Donchian_at_T_ignores_the_current_bar_high_and_low()
    {
        var candles = new[]
        {
            Candle(10m, 0, high: 10m, low: 9m),
            Candle(11m, 1, high: 11m, low: 10m),
            Candle(12m, 2, high: 12m, low: 11m),
            Candle(13m, 3, high: 100m, low: 1m)
        };
        var (high, low) = DonchianSeries.Compute(candles, 3);
        high[3].Should().Be(12m);
        low[3].Should().Be(9m);
        high[3].Should().NotBe(100m);
        low[3].Should().NotBe(1m);
    }

    [Fact]
    public void Macd_histogram_is_macd_minus_signal()
    {
        var candles = Enumerable.Range(1, 40).Select(i => Candle(100m + i, i)).ToList();
        var (macd, signal, hist) = MacdSeries.Compute(candles, 3, 6, 2);
        var i = candles.Count - 1;
        macd[i].Should().NotBeNull();
        signal[i].Should().NotBeNull();
        hist[i].Should().Be(macd[i] - signal[i]);
    }

    [Fact]
    public void Bollinger_mid_is_the_sma_of_closes()
    {
        var candles = Enumerable.Range(0, 8).Select(i => Candle(10m + i, i)).ToList();
        var (mid, upper, lower) = BollingerSeries.Compute(candles, 5, 2m);
        mid[^1].Should().NotBeNull();
        upper[^1]!.Value.Should().BeGreaterThan(mid[^1]!.Value);
        lower[^1]!.Value.Should().BeLessThan(mid[^1]!.Value);
    }

    private static MarketCandle Candle(decimal close, int i, decimal? high = null, decimal? low = null) => new()
    {
        Open = close,
        High = high ?? close,
        Low = low ?? close,
        Close = close,
        Volume = 1,
        OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
        CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5),
        IsClosed = true
    };
}
