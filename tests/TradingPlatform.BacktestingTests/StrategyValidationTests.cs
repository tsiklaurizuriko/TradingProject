using FluentAssertions;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using Xunit;

namespace TradingPlatform.BacktestingTests;

public sealed class StrategyValidationTests
{
    [Fact]
    public void Chronological_split_keeps_time_order_and_does_not_shuffle()
    {
        var candles = Enumerable.Range(0, 100).Select(i => Bar(i, 100m + i)).ToList();
        var (ins, val, oos) = StrategyValidation.ChronologicalSplit(candles);
        ins.Count.Should().Be(60);
        val.Count.Should().Be(20);
        oos.Count.Should().Be(20);
        ins[^1].OpenTime.Should().BeBefore(val[0].OpenTime);
        val[^1].OpenTime.Should().BeBefore(oos[0].OpenTime);
    }

    [Fact]
    public void Walk_forward_windows_are_anchored_forward()
    {
        var windows = StrategyValidation.WalkForwardWindows(count: 200, train: 80, test: 20, step: 20);
        windows.Should().NotBeEmpty();
        windows[0].Start.Should().Be(0);
        for (var i = 1; i < windows.Count; i++)
        {
            windows[i].Start.Should().BeGreaterThan(windows[i - 1].Start);
        }
    }

    [Fact]
    public void Regime_labels_a_monotonic_rise_as_bull_or_extended()
    {
        var candles = Enumerable.Range(0, 40).Select(i => Bar(i, 100m + i * 3m)).ToList();
        var regime = StrategyValidation.ClassifyRegime(candles);
        regime.Should().BeOneOf("strong_bull", "extended_trend", "high_vol_bull");
    }

    [Fact]
    public void Frozen_default_replay_records_long_and_short_buckets()
    {
        var definition = StrategyValidation.Definition(StrategyTemplateKeys.DonchianBreakout, "1h", StrategyTemplates.DefaultsFor(StrategyTemplateKeys.DonchianBreakout, false) with
        {
            DonchianLength = 5,
            EmaFast = 3,
            EmaSlow = 6
        });
        var up = Enumerable.Range(0, 40).Select(i => Bar(i, 100m + i)).ToList();
        var result = StrategyValidation.Run(definition, up);
        result.CostNotes.Should().Contain("EXCLUDING_FUNDING");
        (result.Long.Trades + result.Short.Trades).Should().Be(result.NumberOfTrades);
    }

    [Fact]
    public void Intraday_non_btc_slots_are_evaluated_not_skipped()
    {
        var candles = Enumerable.Range(0, 400).Select(i => Bar(i, 100m + i)).ToList();
        var series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>
        {
            [("ETHUSDT", "5m")] = candles,
            [("ETHUSDT", "15m")] = candles
        };
        var rows = StrategyValidationRunner.Evaluate(series, implementationOk: true);
        rows.Should().NotBeEmpty();
        foreach (var row in rows)
        {
            row.Symbols.Should().Contain("ETHUSDT");
            row.Timeframes.Should().Contain("5m");
            row.Timeframes.Should().Contain("15m");
            row.Notes.Should().NotContain(note => note.Contains("skipped", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static MarketCandle Bar(int i, decimal price) => new()
    {
        Open = price,
        High = price + 0.5m,
        Low = price - 0.5m,
        Close = price,
        Volume = 20,
        IsClosed = true,
        OpenTime = DateTimeOffset.UnixEpoch.AddHours(i),
        CloseTime = DateTimeOffset.UnixEpoch.AddHours(i + 1)
    };
}
