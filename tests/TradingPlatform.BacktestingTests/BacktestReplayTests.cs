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

    private static ReplaySettings Settings(DateTimeOffset start, DateTimeOffset end, decimal stopLossPercent = 50m) =>
        new(start, end, 10_000m, 1m, 1m, 0.1m, 0m, stopLossPercent, 20m);

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

    private sealed class ScriptedEngine(int buyAtCount, int exitAtCount) : IStrategyEngine
    {
        public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason)
        {
            reason = "script";
            var n = context.ClosedCandles.Count;
            if (!context.HasOpenPosition && n == buyAtCount)
            {
                return SignalType.Buy;
            }

            if (context.HasOpenPosition && n == exitAtCount)
            {
                return SignalType.Exit;
            }

            return context.HasOpenPosition ? SignalType.Hold : SignalType.NoAction;
        }
    }
}
