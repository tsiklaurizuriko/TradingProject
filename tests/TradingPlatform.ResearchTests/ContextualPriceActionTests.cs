using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Strategies.PriceAction;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class ContextualPriceActionTests
{
    [Fact]
    public void Hypothesis_set_is_small_and_frozen()
    {
        var rows = ContextualPriceActionCatalog.Hypotheses;
        rows.Should().HaveCount(25);
        rows.Select(x => x.Family).Distinct().Should().BeEquivalentTo(
            "SWEEP", "FAILED_BREAKOUT", "BREAKOUT_RETEST", "PULLBACK", "WM", "FLAG", "COMPRESSION", "MTF");
        rows.Select(x => x.CandidateId).Distinct().Should().HaveCount(25);
        rows.Should().OnlyContain(x => x.EntryTimeframe == "5m");
        rows.Should().NotContain(x => x.CandidateId.Contains("SYMMETRICAL", StringComparison.OrdinalIgnoreCase));
        rows.Where(x => x.Variant == "STRICT").Should().OnlyContain(x => x.RequireRelativeVolume && x.ConfirmationTimeframe == "1m");
        rows.Where(x => x.Variant == "CONTEXTUAL" && x.Family != "FAILED_BREAKOUT" && x.Family != "COMPRESSION")
            .Should().OnlyContain(x => x.ConfirmationTimeframe == "3m" && x.ContextTimeframe == "1h");
        ContextualPriceActionCatalog.SurvivorRule.Should().Contain("OOS is not an input");
        ContextualPriceActionCatalog.CostLabels.Should().Equal("BASE", "MILD", "HIGH", "STRESS");
    }

    [Fact]
    public void Sweep_baseline_fires_and_opposite_context_blocks_it()
    {
        var entry = SweepSeries();
        var index = entry.Count - 1;
        var close = entry[index].CloseTime;
        var caches = new Dictionary<string, CausalIndicatorCache>(StringComparer.OrdinalIgnoreCase)
        {
            ["5m"] = new(entry),
            ["3m"] = new(Flat("3m", close, 3)),
            ["1m"] = new(Flat("1m", close, 1)),
            ["15m"] = new(Downtrend("15m", close, 15)),
            ["1h"] = new(Downtrend("1h", close, 60))
        };

        var rows = ContextualPriceActionSignals.BuildAll(caches);
        rows.Should().OnlyContain(x => !x.SeriesMissing);
        rows.Single(x => x.CandidateId == "CPA-SWEEP|BASELINE|5m").Signals[index].Should().Be(SignalType.Buy);
        rows.Single(x => x.CandidateId == "CPA-SWEEP|CONTEXTUAL|5m").Signals[index].Should().Be(SignalType.NoAction);
        rows.Single(x => x.CandidateId == "CPA-SWEEP|STRICT|5m").Signals[index].Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Future_bar_and_unclosed_higher_timeframe_do_not_change_the_signal()
    {
        var entry = SweepSeries();
        var index = entry.Count - 1;
        var close = entry[index].CloseTime;
        var higher = Downtrend("1h", close, 60);
        var withFutureEntry = entry.ToList();
        withFutureEntry.Add(Bar(entry.Count, 5, 1m, 50m));
        var futureHigher = higher.ToList();
        futureHigher.Add(new MarketCandle
        {
            Open = 1m,
            High = 1m,
            Low = 1m,
            Close = 1m,
            Volume = 20m,
            IsClosed = true,
            OpenTime = close.AddMinutes(1),
            CloseTime = close.AddHours(1)
        });

        var before = SignalAt(entry, higher);
        var after = SignalAt(withFutureEntry, futureHigher);
        after[index].Should().Be(before[index]);
        before[index].Should().Be(SignalType.Buy);
    }

    [Fact]
    public void Missing_required_series_is_not_fabricated()
    {
        var caches = new Dictionary<string, CausalIndicatorCache>(StringComparer.OrdinalIgnoreCase)
        {
            ["5m"] = new(SweepSeries())
        };
        var rows = ContextualPriceActionSignals.BuildAll(caches);
        rows.Single(x => x.CandidateId == "CPA-SWEEP|BASELINE|5m").SeriesMissing.Should().BeFalse();
        rows.Single(x => x.CandidateId == "CPA-SWEEP|CONTEXTUAL|5m").SeriesMissing.Should().BeTrue();
        rows.Single(x => x.CandidateId == "CPA-SWEEP|CONTEXTUAL|5m").Signals.Should().BeEmpty();
    }

    [Fact]
    public void Replay_is_deterministic_and_displacement_uses_only_that_bar()
    {
        var entry = SweepSeries();
        var first = ContextualPriceActionSignals.BuildAll(new Dictionary<string, CausalIndicatorCache>(StringComparer.OrdinalIgnoreCase)
        {
            ["5m"] = new(entry)
        });
        var second = ContextualPriceActionSignals.BuildAll(new Dictionary<string, CausalIndicatorCache>(StringComparer.OrdinalIgnoreCase)
        {
            ["5m"] = new(entry.ToList())
        });
        first.Single(x => x.CandidateId == "CPA-MTF|BASELINE|5m").Signals
            .Should().Equal(second.Single(x => x.CandidateId == "CPA-MTF|BASELINE|5m").Signals);

        var geom = CandleGeom.From(new MarketCandle { Open = 100m, High = 101m, Low = 99.8m, Close = 100.9m, Volume = 10m, IsClosed = true });
        ContextualPriceActionSignals.Displacement(geom, 0.5m, true).Should().BeTrue();
        ContextualPriceActionSignals.Displacement(geom, null, true).Should().BeFalse();
        ContextualPriceActionSignals.TrendAligned(new StructureBar(0, true, true, false, false, 1, false, false, false, false, false, false, false), true)
            .Should().BeTrue();
        ContextualPriceActionSignals.TrendAligned(new StructureBar(0, false, false, true, true, -1, false, false, false, false, false, false, false), true)
            .Should().BeFalse();
    }

    private static SignalType[] SignalAt(IReadOnlyList<MarketCandle> entry, IReadOnlyList<MarketCandle> higher)
    {
        var close = entry[^1].CloseTime;
        var caches = new Dictionary<string, CausalIndicatorCache>(StringComparer.OrdinalIgnoreCase)
        {
            ["5m"] = new(entry),
            ["3m"] = new(Flat("3m", close, 3)),
            ["1m"] = new(Flat("1m", close, 1)),
            ["15m"] = new(higher),
            ["1h"] = new(higher)
        };
        return ContextualPriceActionSignals.BuildAll(caches)
            .Single(x => x.CandidateId == "CPA-SWEEP|BASELINE|5m").Signals;
    }

    private static List<MarketCandle> SweepSeries()
    {
        var candles = new List<MarketCandle>();
        decimal px = 100m;
        for (var i = 0; i < 20; i++)
        {
            px -= 0.4m;
            candles.Add(Bar(i, 5, px + 0.05m, px, px + 0.2m, px - 0.2m));
        }

        for (var i = 0; i < 16; i++)
        {
            px += 0.5m;
            candles.Add(Bar(candles.Count, 5, px - 0.05m, px, px + 0.2m, px - 0.2m));
        }

        var swingLo = CausalSwingSeries.Detect(candles, 3).Lows.Last();
        candles.Add(Bar(candles.Count, 5, swingLo.Price + 0.3m, swingLo.Price + 0.25m, swingLo.Price + 0.4m, swingLo.Price - 1.2m));
        return candles;
    }

    private static List<MarketCandle> Flat(string timeframe, DateTimeOffset end, int minutes)
    {
        var bars = new List<MarketCandle>();
        for (var i = 8; i >= 1; i--)
        {
            var close = end.AddMinutes(-i * minutes);
            bars.Add(new MarketCandle
            {
                Open = 100m,
                High = 100.2m,
                Low = 99.8m,
                Close = 100m,
                Volume = 20m,
                IsClosed = true,
                OpenTime = close.AddMinutes(-minutes),
                CloseTime = close
            });
        }

        return bars;
    }

    private static List<MarketCandle> Downtrend(string timeframe, DateTimeOffset end, int minutes)
    {
        var bars = new List<MarketCandle>();
        decimal px = 120m;
        for (var i = 30; i >= 1; i--)
        {
            px -= 0.4m;
            var close = end.AddMinutes(-i * minutes);
            bars.Add(new MarketCandle
            {
                Open = px + 0.3m,
                High = px + 0.4m,
                Low = px - 0.2m,
                Close = px,
                Volume = 20m,
                IsClosed = true,
                OpenTime = close.AddMinutes(-minutes),
                CloseTime = close
            });
        }

        return bars;
    }

    private static MarketCandle Bar(int i, int minutes, decimal open, decimal close, decimal high, decimal low) =>
        new()
        {
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = 20m,
            IsClosed = true,
            OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * minutes),
            CloseTime = DateTimeOffset.UnixEpoch.AddMinutes((i + 1) * minutes)
        };
}
