using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class PriceActionDataQualityTests
{
    [Fact]
    public void Continuous_aligned_ohlcv_passes()
    {
        var candles = Enumerable.Range(0, 10).Select(Bar).ToList();

        var result = PriceActionDataQuality.Audit(candles, 60_000);

        result.QualityPassed.Should().BeTrue();
        result.Continuous.Should().BeTrue();
        result.DuplicateTimestamps.Should().Be(0);
        result.GapSegments.Should().Be(0);
        result.MissingBars.Should().Be(0);
    }

    [Fact]
    public void Gaps_and_duplicates_are_preserved_and_reported()
    {
        var candles = new List<MarketCandle>
        {
            Bar(0),
            Bar(1),
            Bar(1),
            Bar(4)
        };

        var result = PriceActionDataQuality.Audit(candles, 60_000);

        result.QualityPassed.Should().BeFalse();
        result.DuplicateTimestamps.Should().Be(1);
        result.GapSegments.Should().Be(1);
        result.MissingBars.Should().Be(2);
    }

    [Fact]
    public void Impossible_ohlc_negative_volume_and_alignment_fail()
    {
        var invalid = Bar(0);
        invalid.OpenTime = invalid.OpenTime.AddSeconds(1);
        invalid.CloseTime = invalid.CloseTime.AddSeconds(1);
        invalid.High = 99m;
        invalid.Low = 101m;
        invalid.Volume = -1m;

        var result = PriceActionDataQuality.Audit([invalid], 60_000);

        result.QualityPassed.Should().BeFalse();
        result.MisalignedTimestamps.Should().Be(1);
        result.ImpossibleOhlc.Should().Be(1);
        result.NegativeVolume.Should().Be(1);
    }

    [Fact]
    public void Phase7_universe_is_bounded_and_reuses_scalping_universe()
    {
        Phase7PriceActionUniverse.Symbols.Length.Should().BeInRange(10, 20);
        Phase7PriceActionUniverse.Symbols.Should().Contain(["BTCUSDT", "ETHUSDT"]);
        Phase7PriceActionUniverse.Symbols.Should().OnlyContain(symbol =>
            ScalpingCatalog.Universe.Contains(symbol, StringComparer.OrdinalIgnoreCase));
        Phase7PriceActionUniverse.SelectionBasis.Should().Contain("existing");
    }

    private static MarketCandle Bar(int minute) =>
        new()
        {
            OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(minute),
            CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(minute + 1).AddMilliseconds(-1),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100.5m,
            Volume = 10m,
            IsClosed = true
        };
}
