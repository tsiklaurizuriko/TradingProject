using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using Xunit;

namespace TradingPlatform.BacktestingTests;

public sealed class BacktestReplayTests
{
    [Fact]
    public void Replay_fills_next_bar_open_and_counts_a_round_trip()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 60).Select(i => Bar(start, i, 100m)).ToList();
        var replay = new BacktestReplay(new ScriptedEngine(40, 50));
        var result = replay.Run(
            new StrategyDefinition { Name = "script", Timeframe = "5m", Entry = new ConditionGroup(), Exit = new ConditionGroup() },
            candles,
            Settings(start, start.AddHours(6)));

        result.NumberOfTrades.Should().Be(1);
        result.Trades[0].EntryPrice.Should().Be(100m);
        result.Trades[0].ExitPrice.Should().Be(100m);
        result.Trades[0].Reason.Should().Be("script");
        result.FeesPaid.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Replay_uses_stop_when_the_bar_trades_through_it()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 50).Select(i =>
        {
            var bar = Bar(start, i, 100m);
            if (i == 41)
            {
                bar.Low = 90m;
                bar.Close = 91m;
            }

            return bar;
        }).ToList();
        var replay = new BacktestReplay(new ScriptedEngine(40, 80));
        var result = replay.Run(
            new StrategyDefinition { Name = "script", Timeframe = "5m", Entry = new ConditionGroup(), Exit = new ConditionGroup() },
            candles,
            Settings(start, start.AddHours(6), stopLossPercent: 1.5m));

        result.NumberOfTrades.Should().Be(1);
        result.Trades[0].Reason.Should().Be("Stop loss");
        result.Trades[0].PnL.Should().BeNegative();
    }

    [Fact]
    public void Replay_stop_wins_when_the_same_bar_also_prints_the_target()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 50).Select(i =>
        {
            var bar = Bar(start, i, 100m);
            if (i == 41)
            {
                bar.High = 120m;
                bar.Low = 90m;
                bar.Close = 100m;
            }

            return bar;
        }).ToList();
        var replay = new BacktestReplay(new ScriptedEngine(40, 80));
        var result = replay.Run(
            new StrategyDefinition { Name = "script", Timeframe = "5m", Entry = new ConditionGroup(), Exit = new ConditionGroup() },
            candles,
            Settings(start, start.AddHours(6), stopLossPercent: 1.5m));

        result.NumberOfTrades.Should().Be(1);
        result.Trades[0].Reason.Should().Be("Stop loss");
        result.Trades[0].Side.Should().Be("Long");
        result.Long.Trades.Should().Be(1);
        result.Short.Trades.Should().Be(0);
        result.CostNotes.Should().Contain("EXCLUDING_FUNDING");
    }

    [Fact]
    public void Replay_ignores_forming_candles()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 60).Select(i => Bar(start, i, 100m)).ToList();
        candles[45].IsClosed = false;
        var replay = new BacktestReplay(new ScriptedEngine(40, 50));
        var result = replay.Run(
            new StrategyDefinition { Name = "script", Timeframe = "5m", Entry = new ConditionGroup(), Exit = new ConditionGroup() },
            candles,
            Settings(start, start.AddHours(6)));

        result.NumberOfTrades.Should().Be(1);
        result.Trades[0].Side.Should().Be("Long");
    }

    [Fact]
    public void Replay_opens_and_closes_a_short()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 60).Select(i => Bar(start, i, 100m)).ToList();
        var replay = new BacktestReplay(new ScriptedEngine(40, 50, shortEntry: true));
        var result = replay.Run(
            new StrategyDefinition { Name = "script", Timeframe = "5m", Entry = new ConditionGroup(), Exit = new ConditionGroup() },
            candles,
            Settings(start, start.AddHours(6)));

        result.NumberOfTrades.Should().Be(1);
        result.Trades[0].EntryPrice.Should().Be(100m);
        result.Trades[0].ExitPrice.Should().Be(100m);
        result.Trades[0].Reason.Should().Be("script");
        result.Trades[0].Side.Should().Be("Short");
        result.FeesPaid.Should().BeGreaterThan(0m);
        result.Short.Trades.Should().Be(1);
    }

    [Fact]
    public void Index_window_fills_last_bar_signal_on_next_open_outside_the_window()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 60).Select(i => Bar(start, i, 100m + i)).ToList();
        var replay = new BacktestReplay(new ScriptedEngine(40, 80));
        var result = replay.Run(
            new StrategyDefinition { Name = "script", Timeframe = "5m", Entry = new ConditionGroup(), Exit = new ConditionGroup() },
            candles,
            Settings(start, start.AddHours(6)),
            evaluateFromInclusive: 10,
            evaluateToExclusive: 40);

        result.NumberOfTrades.Should().Be(1);
        result.Trades[0].OpenedAt.Should().Be(candles[40].OpenTime);
        result.Trades[0].EntryPrice.Should().Be(candles[40].Open);
        result.Trades[0].Reason.Should().Be("End of window");
        result.Assumptions.Should().Contain("T+1");
    }

    [Fact]
    public void Index_window_does_not_invent_a_fill_when_t_plus_one_is_missing()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 40).Select(i => Bar(start, i, 100m)).ToList();
        var replay = new BacktestReplay(new ScriptedEngine(40, 80));
        var result = replay.Run(
            new StrategyDefinition { Name = "script", Timeframe = "5m", Entry = new ConditionGroup(), Exit = new ConditionGroup() },
            candles,
            Settings(start, start.AddHours(6)),
            evaluateFromInclusive: 0,
            evaluateToExclusive: 40);

        result.NumberOfTrades.Should().Be(0);
    }

    [Fact]
    public void Index_window_starts_flat_and_does_not_score_pre_window_signals()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 60).Select(i => Bar(start, i, 100m)).ToList();
        var replay = new BacktestReplay(new ScriptedEngine(20, 30));
        var result = replay.Run(
            new StrategyDefinition { Name = "script", Timeframe = "5m", Entry = new ConditionGroup(), Exit = new ConditionGroup() },
            candles,
            Settings(start, start.AddHours(6)),
            evaluateFromInclusive: 35,
            evaluateToExclusive: 55);

        result.NumberOfTrades.Should().Be(0);
    }

    [Fact]
    public void Slippage_scenarios_do_not_improve_a_flat_round_trip()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 60).Select(i => Bar(start, i, 100m)).ToList();
        var nets = new[] { 0.05m, 0.10m, 0.20m }
            .Select(bps =>
            {
                var replay = new BacktestReplay(new ScriptedEngine(40, 50));
                return replay.Run(
                    new StrategyDefinition { Name = "script", Timeframe = "5m", Entry = new ConditionGroup(), Exit = new ConditionGroup() },
                    candles,
                    Settings(start, start.AddHours(6)) with { SlippagePercent = bps }).NetProfit;
            })
            .ToList();

        nets[0].Should().BeGreaterThan(nets[1]);
        nets[1].Should().BeGreaterThan(nets[2]);
        nets.Should().OnlyContain(value => value < 0m);
    }

    [Fact]
    public void Missing_target_is_not_replaced_when_preserve_null_take_is_set()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 60).Select(i => Bar(start, i, 100m)).ToList();
        candles[45].High = 120m;
        var definition = new StrategyDefinition { Name = "script", Timeframe = "5m", Entry = new ConditionGroup(), Exit = new ConditionGroup() };
        var open = Settings(start, start.AddHours(6), stopLossPercent: 3m) with
        {
            HonorSuggestedStops = true,
            PreserveNullTake = true
        };
        var replaced = open with { PreserveNullTake = false };
        var cache = new TradingPlatform.Strategies.Indicators.CausalIndicatorCache(candles);
        var kept = new BacktestReplay(new StopOnlyEngine()).Run(definition, candles, open, cache);
        var filled = new BacktestReplay(new StopOnlyEngine()).Run(definition, candles, replaced, cache);
        kept.Trades[0].Reason.Should().Be("End of window");
        filled.Trades[0].Reason.Should().Be("Take profit");
        kept.Assumptions.Should().Contain("not treated as a zero funding rate");
    }

    private static ReplaySettings Settings(DateTimeOffset start, DateTimeOffset end, decimal stopLossPercent = 2m) =>
        new(start, end, 10_000m, 1m, 1m, 0.1m, 0m, stopLossPercent, 4m);

    private static MarketCandle Bar(DateTimeOffset start, int index, decimal price) =>
        new()
        {
            OpenTime = start.AddMinutes(index * 5),
            CloseTime = start.AddMinutes(index * 5 + 5),
            Open = price,
            High = price + 0.5m,
            Low = price - 0.5m,
            Close = price,
            Volume = 10,
            IsClosed = true,
            ExchangeTimestamp = start.AddMinutes(index * 5 + 5),
        };

    private sealed class ScriptedEngine(int buyAtCount, int exitAtCount, bool shortEntry = false) : IStrategyEngine
    {
        public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason)
        {
            reason = "script";
            var n = context.ClosedCandles.Count;
            if (!context.HasOpenPosition && n == buyAtCount)
            {
                return shortEntry ? SignalType.Sell : SignalType.Buy;
            }

            if (context.HasOpenPosition && n == exitAtCount)
            {
                return SignalType.Exit;
            }

            return context.HasOpenPosition ? SignalType.Hold : SignalType.NoAction;
        }
    }

    private sealed class StopOnlyEngine : IStrategyEngine
    {
        public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason)
        {
            var n = context.ClosedCandles.Count;
            if (!context.HasOpenPosition && n == 40)
            {
                reason = "buy";
                return SignalType.Buy;
            }

            reason = "hold";
            return context.HasOpenPosition ? SignalType.Hold : SignalType.NoAction;
        }

        public StrategySignalDetail EvaluateDetailAt(
            StrategyDefinition definition,
            StrategyContext context,
            TradingPlatform.Strategies.Indicators.CausalIndicatorCache cache,
            int index)
        {
            var signal = ((IStrategyEngine)this).EvaluateAt(definition, context, cache, index, out var reason);
            decimal? stop = signal == SignalType.Buy ? 97m : null;
            return new StrategySignalDetail(signal, reason, SuggestedStop: stop);
        }
    }
}
