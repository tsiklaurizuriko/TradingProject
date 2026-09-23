using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using Xunit;

namespace TradingPlatform.BacktestingTests;

public sealed class ScalpingReplayHoldTests
{
    [Fact]
    public void Max_hold_bars_flattens_a_scalp_that_never_hits_sl_or_tp()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 60).Select(i => new MarketCandle
        {
            OpenTime = start.AddMinutes(i * 5),
            CloseTime = start.AddMinutes(i * 5 + 5),
            Open = 100m,
            High = 100.4m,
            Low = 99.6m,
            Close = 100m,
            Volume = 10,
            IsClosed = true
        }).ToList();
        var replay = new BacktestReplay(new HoldEngine());
        var settings = StrategyValidation.LowIsolatedRisk(start, start.AddHours(8)) with { MaxHoldBars = 8 };
        var result = replay.Run(new StrategyDefinition { Name = "hold", Timeframe = "5m" }, candles, settings, evaluateFromInclusive: 20);
        result.NumberOfTrades.Should().BeGreaterThan(0);
        result.Trades[0].Reason.Should().Be("TIME");
    }

    [Fact]
    public void Profit_factor_helper_is_wins_over_abs_losses()
    {
        var pf = ProfitFactorValue.From(20m, 10m, 4);
        pf.Kind.Should().Be(ProfitFactorKind.Finite);
        pf.Ratio.Should().Be(2m);
    }

    private sealed class HoldEngine : IStrategyEngine
    {
        public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason)
        {
            reason = "hold-test";
            if (!context.HasOpenPosition && context.ClosedCandles.Count == 25)
            {
                return SignalType.Buy;
            }

            return context.HasOpenPosition ? SignalType.Hold : SignalType.NoAction;
        }
    }
}
