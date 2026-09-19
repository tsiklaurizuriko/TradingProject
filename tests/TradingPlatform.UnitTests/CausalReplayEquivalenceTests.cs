using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class CausalReplayEquivalenceTests
{
    [Fact]
    public void Full_series_index_matches_prefix_evaluate_for_every_template()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(240).ToList();
        var engine = new StrategyEngine();
        var cache = new CausalIndicatorCache(candles);
        foreach (var key in StrategyTemplateKeys.All)
        {
            var definition = StrategyValidation.Definition(key, "1h");
            for (var i = 40; i < candles.Count; i++)
            {
                var prefix = candles.Take(i + 1).ToList();
                var flat = new StrategyContext
                {
                    ClosedCandles = prefix,
                    CurrentPrice = prefix[^1].Close,
                    HasOpenPosition = false
                };
                var prefixSignal = engine.Evaluate(definition, flat, out var prefixReason);
                var at = engine.EvaluateAt(
                    definition,
                    new StrategyContext
                    {
                        ClosedCandles = candles,
                        CurrentPrice = candles[i].Close,
                        HasOpenPosition = false
                    },
                    cache,
                    i,
                    out var atReason);
                at.Should().Be(prefixSignal, "{0} bar {1}: {2} vs {3}", key, i, prefixReason, atReason);
            }
        }
    }

    [Fact]
    public void Replay_fingerprint_is_stable_on_the_benchmark_dataset()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(400).ToList();
        var left = StrategyValidation.Run(StrategyValidation.Definition(StrategyTemplateKeys.DonchianBreakout, "1h"), candles);
        var right = StrategyValidation.Run(StrategyValidation.Definition(StrategyTemplateKeys.DonchianBreakout, "1h"), candles);
        right.NumberOfTrades.Should().Be(left.NumberOfTrades);
        right.NetProfit.Should().Be(left.NetProfit);
        right.ProfitFactor.Should().Be(left.ProfitFactor);
        right.CostNotes.Should().Contain("EXCLUDING_FUNDING");
        (right.Long.Trades + right.Short.Trades).Should().Be(right.NumberOfTrades);
    }

    [Fact]
    public void Open_position_evaluate_at_matches_prefix()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(180).ToList();
        var definition = StrategyValidation.Definition(StrategyTemplateKeys.EmaRsiTrend, "1h");
        var engine = new StrategyEngine();
        var cache = new CausalIndicatorCache(candles);
        const int i = 120;
        var prefix = candles.Take(i + 1).ToList();
        var ctx = new StrategyContext
        {
            ClosedCandles = prefix,
            CurrentPrice = prefix[^1].Close,
            HasOpenPosition = true,
            PositionSide = PositionSide.Long,
            AverageEntryPrice = prefix[^10].Close
        };
        var prefixSignal = engine.Evaluate(definition, ctx, out _);
        var at = engine.EvaluateAt(
            definition,
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[i].Close,
                HasOpenPosition = true,
                PositionSide = PositionSide.Long,
                AverageEntryPrice = prefix[^10].Close
            },
            cache,
            i,
            out _);
        at.Should().Be(prefixSignal);
    }
}
