using System.Text.Json;
using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.BacktestingTests;

/// <summary>
/// The indexed rule path must match the per-bar prefix evaluation it replaced, which stays here as the oracle.
/// </summary>
public sealed class RuleEngineParityTests
{
    private static readonly DateTimeOffset Start = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, int> Indicators => new()
    {
        { "SMA", 1 }, { "SMA", 20 }, { "EMA", 9 }, { "EMA", 50 }, { "RSI", 2 }, { "RSI", 14 }, { "WMA", 10 },
        { "ATR", 14 }, { "VOLUME", 1 }, { "AVERAGEVOLUME", 20 }, { "ATRPERCENT", 14 }
    };

    [Theory]
    [MemberData(nameof(Indicators))]
    public void Every_registered_indicator_is_causal(string name, int period)
    {
        var candles = Walk(400, seed: 3);
        var full = new IndicatorRegistry().Create(name, period).Compute(candles);

        full.Should().HaveCount(candles.Count);
        for (var i = 0; i < candles.Count; i++)
        {
            var prefix = new IndicatorRegistry().Create(name, period).Compute(candles.Take(i + 1).ToList());
            full[i].Should().Be(prefix[^1], $"{name}({period}) at bar {i} must not depend on later bars");
        }
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void Indexed_rule_evaluation_matches_prefix_evaluation_at_every_bar(string label, StrategyDefinition definition)
    {
        var candles = Walk(500, seed: label.Length);
        var cache = new CausalIndicatorCache(candles);
        var indexed = new StrategyEngine();

        for (var i = 0; i < candles.Count; i++)
        {
            foreach (var open in new[] { false, true })
            {
                var oracle = new StrategyEngine().Evaluate(definition, new StrategyContext { ClosedCandles = candles.Take(i + 1).ToList(), HasOpenPosition = open }, out var oracleReason);
                var detail = indexed.EvaluateDetailAt(definition, new StrategyContext { ClosedCandles = candles, HasOpenPosition = open }, cache, i);

                detail.Signal.Should().Be(oracle, $"{label} at bar {i} with open={open}");
                detail.Reason.Should().Be(oracleReason);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void Replay_trades_match_the_prefix_oracle(string label, StrategyDefinition definition)
    {
        var candles = Walk(1_500, seed: 17 + label.Length);
        var settings = new ReplaySettings(candles[0].OpenTime, candles[^1].CloseTime, 10_000m, 1m, 1m, 0.05m, 0.02m, 2m, 4m);

        var fast = new BacktestReplay(new StrategyEngine()).Run(definition, candles, settings);
        var oracle = new BacktestReplay(new PrefixOracleEngine()).Run(definition, candles, settings);

        oracle.NumberOfTrades.Should().BePositive(label);
        fast.NumberOfTrades.Should().Be(oracle.NumberOfTrades, label);
        fast.Trades.Select(t => (t.EntryPrice, t.ExitPrice, t.PnL, t.Reason))
            .Should().Equal(oracle.Trades.Select(t => (t.EntryPrice, t.ExitPrice, t.PnL, t.Reason)));
        fast.FeesPaid.Should().Be(oracle.FeesPaid);
    }

    [Fact]
    public void Indexed_path_ignores_bars_after_the_index_even_when_the_context_holds_them()
    {
        var definition = Rule("rsi", Node("RSI", 14, ComparisonKind.LessThan, "30"), Node("RSI", 14, ComparisonKind.GreaterThan, "70"));
        var candles = Walk(300, seed: 5);
        var engine = new StrategyEngine();
        var cache = new CausalIndicatorCache(candles);
        var rsi = new RsiIndicator(14).Compute(candles);

        for (var i = 0; i < candles.Count; i++)
        {
            var signal = engine.EvaluateAt(definition, new StrategyContext { ClosedCandles = candles }, cache, i, out _);
            signal.Should().Be(rsi[i] < 30m ? SignalType.Buy : SignalType.NoAction, $"bar {i}");
        }
    }

    public static TheoryData<string, StrategyDefinition> Definitions => new()
    {
        { "rsi-threshold", Rule("rsi-threshold", Node("RSI", 14, ComparisonKind.LessThan, "40"), Node("RSI", 14, ComparisonKind.GreaterThan, "60")) },
        { "ema-cross", Rule("ema-cross", Node("EMA", 9, ComparisonKind.CrossesAbove, """{"indicator":"EMA","period":21}"""), Node("EMA", 9, ComparisonKind.CrossesBelow, """{"indicator":"EMA","period":21}""")) },
        { "sma-wma-or", new StrategyDefinition
            {
                Name = "sma-wma-or",
                Timeframe = "15m",
                Entry = new ConditionGroup
                {
                    Operator = BooleanOperator.Or,
                    Conditions = [Node("SMA", 10, ComparisonKind.GreaterOrEqual, """{"indicator":"WMA","period":30}"""), Node("ATRPERCENT", 14, ComparisonKind.GreaterThan, "1.2")]
                },
                Exit = new ConditionGroup
                {
                    Conditions =
                    [
                        Node("SMA", 10, ComparisonKind.LessOrEqual, """{"indicator":"WMA","period":30}"""),
                        new ConditionNode { Group = new ConditionGroup { Operator = BooleanOperator.Not, Conditions = [Node("VOLUME", 1, ComparisonKind.GreaterThan, """{"indicator":"AVERAGEVOLUME","period":20}""")] } }
                    ]
                }
            }
        },
        { "atr-volume-and", Rule("atr-volume-and",
            new ConditionNode { Group = new ConditionGroup { Conditions = [Node("ATR", 14, ComparisonKind.LessThan, """{"indicator":"ATR","period":50}"""), Node("VOLUME", 1, ComparisonKind.GreaterThan, """{"indicator":"AVGVOLUME","period":10}""")] } },
            Node("RSI", 2, ComparisonKind.GreaterThan, "80")) }
    };

    private static StrategyDefinition Rule(string name, ConditionNode entry, ConditionNode exit) => new()
    {
        Name = name,
        Timeframe = "15m",
        Entry = new ConditionGroup { Conditions = [entry] },
        Exit = new ConditionGroup { Conditions = [exit] }
    };

    private static ConditionNode Node(string indicator, int period, ComparisonKind comparison, string value) => new()
    {
        Indicator = indicator,
        Period = period,
        Comparison = comparison,
        Value = JsonDocument.Parse(value).RootElement
    };

    private static List<MarketCandle> Walk(int count, int seed)
    {
        var random = new Random(seed);
        var price = 100m;
        var rows = new List<MarketCandle>(count);
        for (var i = 0; i < count; i++)
        {
            var open = price;
            price = Math.Max(1m, price * (1m + (decimal)(random.NextDouble() - 0.5) * 0.02m));
            rows.Add(new MarketCandle
            {
                OpenTime = Start.AddMinutes(i * 15),
                CloseTime = Start.AddMinutes((i + 1) * 15),
                Open = open,
                High = Math.Max(open, price) * (1m + (decimal)random.NextDouble() * 0.004m),
                Low = Math.Min(open, price) * (1m - (decimal)random.NextDouble() * 0.004m),
                Close = price,
                Volume = 100m + random.Next(0, 200),
                IsClosed = true,
                ExchangeTimestamp = Start.AddMinutes((i + 1) * 15)
            });
        }

        return rows;
    }

    /// <summary>The pre-fix behavior: every bar evaluates a fresh engine on a copied prefix.</summary>
    private sealed class PrefixOracleEngine : IStrategyEngine
    {
        public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason) =>
            new StrategyEngine().Evaluate(definition, context, out reason);
    }
}
