using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class Wave4SignalTests
{
    [Fact]
    public void Vision_csv_parses_oi_and_ratios()
    {
        const string csv = """
            create_time,symbol,sum_open_interest,sum_open_interest_value,count_toptrader_long_short_ratio,sum_toptrader_long_short_ratio,count_long_short_ratio,sum_taker_long_short_vol_ratio
            2024-09-18 00:00:00,BTCUSDT,100,1000,1.1,0.9,1.08,0.99
            2024-09-18 00:05:00,BTCUSDT,110,1100,1.2,0.95,1.07,1.10
            """;
        var rows = BinanceVisionClient.ParseCsv(csv, "BTCUSDT");
        rows.Should().HaveCount(2);
        rows[0].CreateTime.Should().Be(new DateTimeOffset(2024, 9, 18, 0, 0, 0, TimeSpan.Zero));
        rows[1].SumOpenInterest.Should().Be(110m);
        rows[1].CountLongShortRatio.Should().Be(1.07m);
    }

    [Fact]
    public void Oi_alignment_uses_last_print_at_or_before_close_not_next_hour()
    {
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 3).Select(i => Bar("BTCUSDT", t0, i, 100m + i)).ToList();
        var panel = Wave3Panel.Align(
            new Dictionary<string, IReadOnlyList<MarketCandle>> { ["BTCUSDT"] = candles },
            ["BTCUSDT"]);
        var metrics = new Dictionary<string, IReadOnlyList<VisionMetricsPoint>>
        {
            ["BTCUSDT"] =
            [
                new(t0.AddMinutes(55), "BTCUSDT", 10m, 0m, null, null, null, null),
                new(t0.AddHours(1), "BTCUSDT", 99m, 0m, null, null, null, null)
            ]
        };
        var oi = BinanceVisionClient.AlignToPanel(panel, metrics, p => (double)p.SumOpenInterest);
        oi[0, 0].Should().Be(10d);
        oi[1, 0].Should().Be(99d);
    }

    [Fact]
    public void Oi_pct_change_does_not_use_future_level()
    {
        var level = new double[4, 1];
        level[0, 0] = 100;
        level[1, 0] = 110;
        level[2, 0] = 121;
        level[3, 0] = 80;
        var d1 = BinanceVisionClient.PctChange(level, 1);
        d1[1, 0].Should().BeApproximately(0.10, 1e-9);
        d1[2, 0].Should().BeApproximately(0.10, 1e-9);
        d1[3, 0].Should().BeApproximately(80 / 121.0 - 1.0, 1e-9);
    }

    [Fact]
    public void Zero_taker_buy_is_unavailable_not_zero_imbalance()
    {
        var bar = new Wave3Bar(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1, 1, 1, 1,
            Volume: 10m,
            TakerBuyVolume: 0m);
        double.IsNaN(Wave3Math.TakerImbalance(bar)).Should().BeTrue();
    }

    [Fact]
    public void Classify_rejects_flat_ic()
    {
        var hyp = Wave4SignalResearch.Hypotheses[0];
        var rows = new List<Wave3IcRow>
        {
            new(hyp.Id, "IS", 24, 500, 0.001, 0, 0.001, 0.001, 0, 0.001, -0.001, 0.5, 0.5, 0.0001),
            new(hyp.Id, "VALIDATION", 24, 200, -0.01, 0, 0, 0, 0, 0, 0, 0.5, 0.5, 0),
            new(hyp.Id, "OOS", 24, 200, 0.01, 0, 0, 0, 0, 0, 0, 0.5, 0.5, 0)
        };
        Wave4SignalResearch.Classify(hyp, rows).Should().Be("REJECTED");
    }

    [Fact]
    public void Classify_is_fragile_when_oos_exec_spread_misses_cost_hurdle()
    {
        var hyp = Wave4SignalResearch.Hypotheses.First(h => h.Id == "W4_M_LS_REV");
        var rows = new List<Wave3IcRow>
        {
            new(hyp.Id, "IS", 24, 500, 0.04, 0.01, 0.002, -0.0002, 0.002, 0.002, 0.0002, 0.55, 0.52, 0.002),
            new(hyp.Id, "VALIDATION", 24, 200, 0.01, 0.01, 0.001, 0, 0.001, 0.001, 0, 0.52, 0.5, 0.001),
            new(hyp.Id, "OOS", 24, 200, 0.02, 0.04, 0.0008, 0.0004, 0.0004, 0.0008, -0.0004, 0.52, 0.48, 0.0004)
        };
        Wave4SignalResearch.Classify(hyp, rows).Should().Be("FRAGILE");
    }

    private static MarketCandle Bar(string _, DateTimeOffset t0, int hour, decimal close) => new()
    {
        Open = close,
        High = close + 1m,
        Low = close - 1m,
        Close = close,
        Volume = 100,
        TakerBuyVolume = 40,
        IsClosed = true,
        OpenTime = t0.AddHours(hour),
        CloseTime = t0.AddHours(hour + 1).AddMilliseconds(-1)
    };
}
