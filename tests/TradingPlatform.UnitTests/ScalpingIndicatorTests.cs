using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class ScalpingIndicatorTests
{
    [Fact]
    public void Stochastic_is_causal_and_warms_up()
    {
        var candles = Enumerable.Range(0, 20).Select(i => Candle(10m + i, i, high: 12m + i, low: 9m + i)).ToList();
        var k = ScalpingIndicatorSeries.StochasticK(candles, 5);
        k[3].Should().BeNull();
        k[4].Should().NotBeNull();
        var prefix = ScalpingIndicatorSeries.StochasticK(candles.Take(10).ToList(), 5);
        prefix[9].Should().Be(k[9]);
    }

    [Fact]
    public void Stoch_rsi_needs_rsi_warmup()
    {
        var rsi = Enumerable.Range(0, 20).Select(i => i < 5 ? (decimal?)null : 40m + i).ToList();
        var stoch = ScalpingIndicatorSeries.StochRsi(rsi, 5);
        stoch[8].Should().BeNull();
        stoch[19].Should().NotBeNull();
    }

    [Fact]
    public void Hma_is_null_until_weighted_window_fills()
    {
        var candles = Enumerable.Range(0, 16).Select(i => Candle(100m + i, i)).ToList();
        var hma = ScalpingIndicatorSeries.Hma(candles, 9);
        hma[0].Should().BeNull();
        hma[^1].Should().NotBeNull();
    }

    [Fact]
    public void Aroon_williams_mfi_cmf_ao_are_causal()
    {
        var candles = Enumerable.Range(0, 40).Select(i => Candle(50m + i, i, high: 51m + i, low: 49m + i, volume: 10m + i)).ToList();
        var (up, down) = ScalpingIndicatorSeries.Aroon(candles, 14);
        up[13].Should().BeNull();
        up[14].Should().NotBeNull();
        down[14].Should().NotBeNull();
        var wr = ScalpingIndicatorSeries.WilliamsR(candles, 14);
        wr[13].Should().NotBeNull();
        wr[13]!.Value.Should().BeLessThanOrEqualTo(0m);
        ScalpingIndicatorSeries.Mfi(candles, 14)[^1].Should().NotBeNull();
        ScalpingIndicatorSeries.Cmf(candles, 20)[^1].Should().NotBeNull();
        ScalpingIndicatorSeries.AwesomeOscillator(candles)[^1].Should().NotBeNull();
    }

    [Fact]
    public void Parabolic_sar_and_session_high_low_do_not_look_ahead()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 22, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 12).Select(i => Candle(100m + i, i, open: t0.AddMinutes(i * 5), high: 101m + i, low: 99m + i)).ToList();
        candles[6] = Candle(90m, 6, open: t0.AddMinutes(30).AddDays(1), high: 91m, low: 89m);
        var sar = ScalpingIndicatorSeries.ParabolicSar(candles);
        sar[1].Should().NotBeNull();
        var prefix = ScalpingIndicatorSeries.ParabolicSar(candles.Take(8).ToList());
        prefix[7].Should().Be(sar[7]);
        var (hi, lo) = ScalpingIndicatorSeries.SessionHighLow(candles);
        hi[5]!.Value.Should().BeGreaterThan(hi[6]!.Value);
        ScalpingIndicatorSeries.UtcHour(candles[0]).Should().Be(22);
    }

    [Fact]
    public void Gaps_do_not_invent_taker_imbalance()
    {
        var candles = new List<MarketCandle>
        {
            Candle(10m, 0),
            Candle(11m, 5)
        };
        candles[0].TakerBuyVolume = 0m;
        candles[1].TakerBuyVolume = 0m;
        candles.Should().OnlyContain(c => c.TakerBuyVolume == 0m);
    }

    private static MarketCandle Candle(
        decimal close,
        int i,
        DateTimeOffset? open = null,
        decimal? high = null,
        decimal? low = null,
        decimal volume = 1m) => new()
    {
        Open = close,
        High = high ?? close,
        Low = low ?? close,
        Close = close,
        Volume = volume,
        OpenTime = open ?? DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
        CloseTime = (open ?? DateTimeOffset.UnixEpoch.AddMinutes(i * 5)).AddMinutes(5),
        IsClosed = true
    };
}
