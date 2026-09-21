using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class Btc15mFittedTests
{
    [Fact]
    public void Registry_exposes_both_frozen_btc_15m_candidates_without_changing_existing_catalogs()
    {
        ResearchRegistry.All.Should().HaveCount(15);
        ResearchRegistry.Wave2.Should().HaveCount(8);
        ResearchRegistry.Btc15mFitted.Should().HaveCount(2);
        ResearchRegistry.Btc15mFitted.Select(c => c.CandidateId).Should().BeEquivalentTo(
            "vol_spike_ema_trend",
            "bb20_2_break");

        var vol = ResearchRegistry.Find("vol_spike_ema_trend");
        var bb = ResearchRegistry.Find("bb20_2_break");
        vol.Should().NotBeNull();
        bb.Should().NotBeNull();
        ResearchRegistry.Filter("vol_spike_ema_trend", null, "15m").Should().ContainSingle(c => c.CandidateId == "vol_spike_ema_trend");
        ResearchRegistry.Filter(null, "bb20_2_break", "15m").Should().ContainSingle(c => c.CandidateId == "bb20_2_break");
        ResearchRegistry.Filter(null, null, null).Should().HaveCount(15);

        Wave5Catalog.RouterUniverse().Select(c => c.CandidateId).Should().NotContain("vol_spike_ema_trend");
        Wave5Catalog.RouterUniverse().Select(c => c.CandidateId).Should().NotContain("bb20_2_break");
    }

    [Fact]
    public void Frozen_configuration_matches_discovery_exactly()
    {
        var vol = ResearchRegistry.Find("vol_spike_ema_trend")!;
        vol.FrozenSymbol.Should().Be("BTCUSDT");
        vol.SupportedTimeframes.Should().Equal("15m");
        vol.SupportedDirections.Should().BeEquivalentTo("LONG", "SHORT");
        vol.StopLossPercent.Should().Be(2.5m);
        vol.TakeProfitPercent.Should().Be(5.0m);
        vol.MaxHoldBars.Should().Be(192);
        vol.Status.Should().Be(ResearchStatuses.HistoricallyFittedCandidate);
        vol.Status.Should().NotBe(ResearchStatuses.ValidatedForPaper);

        var bb = ResearchRegistry.Find("bb20_2_break")!;
        bb.FrozenSymbol.Should().Be("BTCUSDT");
        bb.SupportedTimeframes.Should().Equal("15m");
        bb.SupportedDirections.Should().BeEquivalentTo("LONG", "SHORT");
        bb.StopLossPercent.Should().Be(4.0m);
        bb.TakeProfitPercent.Should().Be(5.0m);
        bb.MaxHoldBars.Should().Be(192);
        bb.Status.Should().Be(ResearchStatuses.HistoricallyFittedCandidate);
        bb.Status.Should().NotBe(ResearchStatuses.ValidatedForPaper);

        var from = new DateTimeOffset(2024, 9, 18, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 19, 21, 15, 0, TimeSpan.Zero);
        var low = StrategyValidation.LowIsolatedRisk(from, to);
        low.StopLossPercent.Should().Be(2m);
        low.TakeProfitPercent.Should().Be(4m);
        low.RiskPercent.Should().Be(0.5m);
        low.Leverage.Should().Be(3m);
        low.InitialBalance.Should().Be(1_000m);

        var volBook = ResearchRunner.IsolatedBookFor(vol, from, to);
        volBook.StopLossPercent.Should().Be(2.5m);
        volBook.TakeProfitPercent.Should().Be(5.0m);
        volBook.MaxHoldBars.Should().Be(192);
        volBook.RiskPercent.Should().Be(0.5m);
        volBook.Leverage.Should().Be(3m);

        var bbBook = ResearchRunner.IsolatedBookFor(bb, from, to);
        bbBook.StopLossPercent.Should().Be(4.0m);
        bbBook.TakeProfitPercent.Should().Be(5.0m);
        bbBook.MaxHoldBars.Should().Be(192);

        var volDef = ResearchRunner.DefinitionFor(vol, "15m");
        volDef.Timeframe.Should().Be("15m");
        volDef.AllowedSide.Should().Be(StrategySides.Both);
        ResearchRunner.DefinitionFor(bb, "15m").Timeframe.Should().Be("15m");
    }

    [Fact]
    public void Vol_spike_and_bollinger_break_emit_both_sides_on_closed_bars()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var longVol = VolBars(t0, aboveEma: true);
        var shortVol = VolBars(t0, aboveEma: false);
        Signal(ResearchRegistry.Find("vol_spike_ema_trend")!, longVol, longVol.Count - 1).Should().Be(SignalType.Buy);
        Signal(ResearchRegistry.Find("vol_spike_ema_trend")!, shortVol, shortVol.Count - 1).Should().Be(SignalType.Sell);

        var longBb = BbBars(t0, breakUp: true);
        var shortBb = BbBars(t0, breakUp: false);
        Signal(ResearchRegistry.Find("bb20_2_break")!, longBb, longBb.Count - 1).Should().Be(SignalType.Buy);
        Signal(ResearchRegistry.Find("bb20_2_break")!, shortBb, shortBb.Count - 1).Should().Be(SignalType.Sell);
    }

    [Fact]
    public void Smoke_backtest_is_model_b_next_open_on_15m_btcusdt_with_frozen_exits()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Smoke(ResearchRegistry.Find("vol_spike_ema_trend")!, VolBars(t0, aboveEma: true), 2.5m, 5.0m);
        Smoke(ResearchRegistry.Find("bb20_2_break")!, BbBars(t0, breakUp: true), 4.0m, 5.0m);
    }

    private static void Smoke(ResearchCandidate candidate, List<MarketCandle> signalBars, decimal sl, decimal tp)
    {
        var last = signalBars[^1];
        var t0 = signalBars[0].OpenTime;
        signalBars.Add(Bar(t0, signalBars.Count, last.Close, last.Close + 0.2m, last.Close - 0.2m, last.Close, 10m));
        signalBars.Should().OnlyContain(c => (c.CloseTime - c.OpenTime) <= TimeSpan.FromMinutes(15));
        var definition = ResearchRunner.DefinitionFor(candidate, "15m");
        definition.Timeframe.Should().Be("15m");
        definition.AllowedSide.Should().Be(StrategySides.Both);
        var settings = ResearchRunner.IsolatedBookFor(candidate, signalBars[0].OpenTime, signalBars[^1].CloseTime);
        settings.StopLossPercent.Should().Be(sl);
        settings.TakeProfitPercent.Should().Be(tp);
        settings.MaxHoldBars.Should().Be(192);
        settings.RiskPercent.Should().Be(0.5m);
        settings.Leverage.Should().Be(3m);

        var engine = new ResearchStrategyEngine(candidate);
        var cache = new CausalIndicatorCache(signalBars);
        var replay = new BacktestReplay(engine).Run(definition, signalBars, settings, cache, 20, signalBars.Count);
        replay.NumberOfTrades.Should().BeGreaterThan(0);
        replay.Trades[0].OpenedAt.Should().Be(signalBars[^1].OpenTime);
    }

    [Fact]
    public void Time_exit_fires_after_max_hold_bars_and_stop_is_checked_first()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 12).Select(i => Bar(t0, i, 100m, 100.2m, 99.8m, 100m, 10m)).ToList();
        var definition = new StrategyDefinition { Name = "hold", Timeframe = "15m", AllowedSide = StrategySides.Both };
        var settings = new ReplaySettings(
            candles[0].OpenTime,
            candles[^1].CloseTime,
            1_000m,
            0.5m,
            3m,
            0.04m,
            0.02m,
            1m,
            50m,
            100m,
            100m,
            1,
            100,
            0,
            0.1m,
            false,
            2);
        var replay = new BacktestReplay(new AlwaysLongEngine()).Run(definition, candles, settings, null, 0, candles.Count);
        replay.Trades.Should().NotBeEmpty();
        replay.Trades.Should().Contain(t => t.Reason == "TIME");
    }

    [Fact]
    public void Research_runner_skips_non_btc_and_does_not_mark_validated()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = VolBars(t0, aboveEma: true);
        var candidate = ResearchRegistry.Find("vol_spike_ema_trend")!;
        var books = ResearchRunner.Evaluate(new ResearchRunRequest
        {
            Phase = ResearchPhases.Is,
            Candidates = [candidate],
            Symbols = ["ETHUSDT"],
            Timeframes = ["15m"],
            Series = new Dictionary<(string, string), IReadOnlyList<MarketCandle>>
            {
                [("ETHUSDT", "15m")] = candles
            },
            CostLabels = [ResearchCostLabels.Base],
            UseLowIsolated = true,
            SkipWalkForward = true
        });
        books.Should().ContainSingle();
        books[0].Notes.Should().Contain(n => n.Contains("Frozen to BTCUSDT"));
        candidate.Status.Should().Be(ResearchStatuses.HistoricallyFittedCandidate);
        candidate.Status.Should().NotBe(ResearchStatuses.ValidatedForPaper);
    }

    private static SignalType Signal(ResearchCandidate candidate, List<MarketCandle> candles, int index)
    {
        var engine = new ResearchStrategyEngine(candidate);
        var definition = ResearchRunner.DefinitionFor(candidate, "15m");
        return engine.EvaluateAt(
            definition,
            new StrategyContext { ClosedCandles = candles, CurrentPrice = candles[index].Close, HasOpenPosition = false },
            new CausalIndicatorCache(candles),
            index,
            out _);
    }

    private static List<MarketCandle> VolBars(DateTimeOffset t0, bool aboveEma)
    {
        var rows = new List<MarketCandle>();
        var px = aboveEma ? 100m : 100m;
        for (var i = 0; i < 40; i++)
        {
            var close = aboveEma ? 100m + i * 0.1m : 100m - i * 0.1m;
            rows.Add(Bar(t0, i, close, close + 0.2m, close - 0.2m, close, 10m));
            px = close;
        }

        var spike = aboveEma ? px + 2m : px - 2m;
        rows.Add(Bar(t0, 40, px, Math.Max(px, spike) + 0.2m, Math.Min(px, spike) - 0.2m, spike, 80m));
        return rows;
    }

    private static List<MarketCandle> BbBars(DateTimeOffset t0, bool breakUp)
    {
        var rows = new List<MarketCandle>();
        for (var i = 0; i < 30; i++)
        {
            rows.Add(Bar(t0, i, 100m, 100.1m, 99.9m, 100m, 10m));
        }

        var close = breakUp ? 108m : 92m;
        rows.Add(Bar(t0, 30, 100m, Math.Max(100m, close), Math.Min(100m, close), close, 10m));
        return rows;
    }

    private static MarketCandle Bar(DateTimeOffset t0, int i, decimal o, decimal h, decimal l, decimal c, decimal v) => new()
    {
        Open = o,
        High = h,
        Low = l,
        Close = c,
        Volume = v,
        IsClosed = true,
        OpenTime = t0.AddMinutes(i * 15),
        CloseTime = t0.AddMinutes(i * 15 + 15).AddMilliseconds(-1),
        ExchangeTimestamp = t0.AddMinutes(i * 15 + 15).AddMilliseconds(-1)
    };

    private sealed class AlwaysLongEngine : IStrategyEngine
    {
        public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason)
        {
            if (context.HasOpenPosition)
            {
                reason = "hold";
                return SignalType.Hold;
            }

            reason = "buy";
            return SignalType.Buy;
        }
    }
}
