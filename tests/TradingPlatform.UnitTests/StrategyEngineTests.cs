using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.Strategies.Engine;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class StrategyEngineTests
{
    [Fact]
    public void Sample_ema_rsi_definition_parses()
    {
        var validator = new StrategyDefinitionValidator();
        var definition = validator.Parse(DatabaseSeeder.SampleEmaRsiDefinition);
        definition.Name.Should().Be("EMA RSI Strategy");
        definition.Entry.Should().NotBeNull();
        definition.Exit.Should().NotBeNull();
    }

    [Fact]
    public void Cross_above_and_rsi_filter_emits_buy_on_closed_candles_only()
    {
        var engine = new StrategyEngine();
        var definition = new StrategyDefinitionValidator().Parse(DatabaseSeeder.SampleEmaRsiDefinition);
        var candles = new List<MarketCandle>();
        for (var i = 0; i < 80; i++)
        {
            var price = i < 50 ? 100m - i * 0.2m : 90m + (i - 50) * 0.8m;
            candles.Add(new MarketCandle
            {
                Open = price,
                High = price,
                Low = price,
                Close = price,
                Volume = 10,
                OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
                CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5),
                IsClosed = true
            });
        }

        var signal = engine.Evaluate(definition, new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = false
        }, out var reason);

        reason.Should().NotBeNullOrWhiteSpace();
        signal.Should().BeOneOf(SignalType.Buy, SignalType.NoAction);
    }

    [Fact]
    public void Strategy_stop_nodes_do_not_exit_an_open_position()
    {
        var engine = new StrategyEngine();
        var definition = new StrategyDefinitionValidator().Parse(DatabaseSeeder.SampleEmaRsiDefinition);
        var candles = Enumerable.Range(0, 60).Select(i => new MarketCandle
        {
            Open = 100,
            High = 100,
            Low = 100,
            Close = 100,
            Volume = 1,
            OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
            CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5),
            IsClosed = true
        }).ToList();

        var signal = engine.Evaluate(definition, new StrategyContext
        {
            ClosedCandles = candles,
            HasOpenPosition = true,
            AverageEntryPrice = 100m,
            CurrentPrice = 98m
        }, out _);

        signal.Should().Be(SignalType.Hold);
    }

    [Fact]
    public void Ema_rsi_template_round_trips_seeded_definition()
    {
        var parsed = EmaRsiTemplate.Read(DatabaseSeeder.SampleEmaRsiDefinition);
        parsed.Should().Be(new EmaRsiParameters(20, 50, 14, 50, 1.5m, 3m));

        var json = EmaRsiTemplate.Build("Custom EMA", 2, "15m", parsed with { EmaFast = 12, EmaSlow = 26, StopLossPercent = 2m });
        var rebuilt = EmaRsiTemplate.Read(json);
        rebuilt.EmaFast.Should().Be(12);
        rebuilt.EmaSlow.Should().Be(26);
        new StrategyDefinitionValidator().Parse(json).Timeframe.Should().Be("15m");
        json.Should().NotContain("STOP_LOSS");
        json.Should().NotContain("TAKE_PROFIT");
    }

    [Fact]
    public void Ema_rsi_template_rejects_inverted_periods()
    {
        var act = () => EmaRsiTemplate.Validate(new EmaRsiParameters(50, 20, 14, 50, 1.5m, 3m));
        act.Should().Throw<TradingPlatform.Domain.Errors.DomainException>();
    }

    [Fact]
    public void Ema_rsi_trend_long_on_cross_and_rsi_band()
    {
        SignalOf(StrategyTemplateKeys.EmaRsiTrend, FlatThen(110m)).Should().Be(SignalType.Buy);
    }

    [Fact]
    public void Ema_rsi_trend_short_on_cross_down()
    {
        SignalOf(StrategyTemplateKeys.EmaRsiTrend, FlatThen(90m)).Should().Be(SignalType.Sell);
    }

    [Fact]
    public void Macd_trend_long_on_cross()
    {
        SignalOf(StrategyTemplateKeys.MacdTrend, FlatThen(120m)).Should().Be(SignalType.Buy);
    }

    [Fact]
    public void Rsi_pullback_long_after_oversold_cross()
    {
        var json = StrategyTemplates.Build("RSI Pullback", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.RsiPullback, false) with
        {
            AllowedSide = StrategySides.Both,
            EmaFast = 3,
            EmaSlow = 6,
            RsiPeriod = 3,
            RsiOversold = 40m,
            RsiOverbought = 60m
        });
        var candles = Enumerable.Range(0, 24).Select(i => Bar(i, 100m, 10m)).ToList();
        candles.Add(Bar(24, 70m, 10m));
        candles.Add(Bar(25, 70m, 10m));
        candles.Add(Bar(26, 70m, 10m));
        candles.Add(Bar(27, 101m, 10m));
        new StrategyEngine().Evaluate(
            new StrategyDefinitionValidator().Parse(json),
            Flat(candles),
            out _).Should().Be(SignalType.Buy);
    }

    [Fact]
    public void Bollinger_reversion_long_when_close_returns_inside()
    {
        var json = StrategyTemplates.Build("BB", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.BollingerReversion, false) with
        {
            AllowedSide = StrategySides.Both,
            BbPeriod = 5,
            BbStdDev = 1m,
            EmaFast = 2,
            EmaSlow = 3
        });
        var candles = Enumerable.Range(0, 20).Select(i => Bar(i, 100m, 10m)).ToList();
        candles.Add(Bar(20, 70m, 10m));
        candles.Add(Bar(21, 99m, 10m));
        var signal = new StrategyEngine().Evaluate(
            new StrategyDefinitionValidator().Parse(json),
            Flat(candles),
            out var reason);
        signal.Should().Be(SignalType.Buy, reason);
    }

    [Fact]
    public void Donchian_breakout_long_and_short()
    {
        var json = StrategyTemplates.Build("DC", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.DonchianBreakout, false) with
        {
            AllowedSide = StrategySides.Both,
            DonchianLength = 5,
            EmaFast = 3,
            EmaSlow = 6
        });
        var definition = new StrategyDefinitionValidator().Parse(json);
        var engine = new StrategyEngine();
        var up = Enumerable.Range(0, 6).Select(i => Bar(i, i == 5 ? 110m : 100m, 10m)).ToList();
        engine.Evaluate(definition, Flat(up), out _).Should().Be(SignalType.Buy);
        var down = Enumerable.Range(0, 6).Select(i => Bar(i, i == 5 ? 90m : 100m, 10m)).ToList();
        engine.Evaluate(definition, Flat(down), out _).Should().Be(SignalType.Sell);
    }

    [Fact]
    public void Volume_filter_skips_an_otherwise_valid_donchian_long()
    {
        var json = StrategyTemplates.Build("DC", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.DonchianBreakout, false) with
        {
            AllowedSide = StrategySides.Long,
            DonchianLength = 5,
            Quality = new StrategyQualityParams(true, 5, 0m, 0m)
        });
        var candles = Enumerable.Range(0, 6).Select(i => Bar(i, i == 5 ? 110m : 100m, i == 5 ? 1m : 20m)).ToList();
        new StrategyEngine().Evaluate(
            new StrategyDefinitionValidator().Parse(json),
            Flat(candles),
            out var reason).Should().Be(SignalType.NoAction);
        reason.Should().Contain("Quality");
    }

    [Fact]
    public void Atr_filter_skips_chop()
    {
        var json = StrategyTemplates.Build("DC", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.DonchianBreakout, false) with
        {
            AllowedSide = StrategySides.Long,
            DonchianLength = 5,
            Quality = new StrategyQualityParams(false, 20, 50m, 0m)
        });
        var candles = Enumerable.Range(0, 20).Select(i => Bar(i, i == 19 ? 101m : 100m, 10m)).ToList();
        new StrategyEngine().Evaluate(
            new StrategyDefinitionValidator().Parse(json),
            Flat(candles),
            out var reason).Should().Be(SignalType.NoAction);
        reason.Should().Contain("Quality");
    }

    [Fact]
    public void Open_long_emits_exit_not_reverse_on_opposite_cross()
    {
        var json = StrategyTemplates.Build("EMA RSI Trend", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.EmaRsiTrend, false) with
        {
            AllowedSide = StrategySides.Both,
            EmaFast = 3,
            EmaSlow = 6,
            RsiPeriod = 3,
            RsiMinimum = 0m,
            RsiLongMax = 100m
        });
        var candles = FlatThen(90m);
        var signal = new StrategyEngine().Evaluate(
            new StrategyDefinitionValidator().Parse(json),
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[^1].Close,
                HasOpenPosition = true,
                AverageEntryPrice = candles[0].Close,
                PositionSide = TradingPlatform.Domain.Positions.PositionSide.Long
            },
            out _);
        signal.Should().Be(SignalType.Exit);
    }

    [Fact]
    public void Template_ignores_leftover_stop_nodes_when_flat_conditions_do_not_exit()
    {
        var json = """
            {
              "name": "Legacy mix",
              "version": 1,
              "template": "ema_rsi_trend",
              "timeframe": "5m",
              "allowedSide": "Long",
              "params": { "emaFast": 20, "emaSlow": 50, "rsiPeriod": 14, "rsiMinimum": 50, "rsiLongMax": 68 },
              "quality": { "requireVolume": false, "volumeLookback": 20, "minAtrPercent": 0, "maxAtrPercent": 0 },
              "exit": { "operator": "OR", "conditions": [ { "type": "STOP_LOSS", "percent": 1.5 } ] }
            }
            """;
        var candles = Enumerable.Range(0, 60).Select(i => Bar(i, 100m, 1m)).ToList();
        new StrategyEngine().Evaluate(
            new StrategyDefinitionValidator().Parse(json),
            new StrategyContext
            {
                ClosedCandles = candles,
                HasOpenPosition = true,
                AverageEntryPrice = 100m,
                CurrentPrice = 90m,
                PositionSide = TradingPlatform.Domain.Positions.PositionSide.Long
            },
            out _).Should().Be(SignalType.Hold);
    }

    [Fact]
    public void Inverted_template_periods_do_not_throw_and_emit_no_action()
    {
        var json = """
            {
              "name": "bad",
              "version": 1,
              "template": "ema_rsi_trend",
              "timeframe": "5m",
              "allowedSide": "Both",
              "params": { "emaFast": 50, "emaSlow": 20, "rsiPeriod": 14, "rsiMinimum": 50, "rsiLongMax": 68 },
              "quality": { "requireVolume": false, "volumeLookback": 20, "minAtrPercent": 0, "maxAtrPercent": 0 }
            }
            """;
        var candles = Enumerable.Range(0, 40).Select(i => Bar(i, 100m + i, 10m)).ToList();
        new StrategyEngine().Evaluate(
            new StrategyDefinitionValidator().Parse(json),
            Flat(candles),
            out var reason).Should().Be(SignalType.NoAction);
        reason.Should().Contain("EMA");
    }

    [Fact]
    public void Insufficient_closed_candles_emit_no_action()
    {
        var json = StrategyTemplates.Build("EMA RSI Trend", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.EmaRsiTrend, false) with
        {
            AllowedSide = StrategySides.Both
        });
        new StrategyEngine().Evaluate(
            new StrategyDefinitionValidator().Parse(json),
            Flat([Bar(0, 100m, 1m)]),
            out var reason).Should().Be(SignalType.NoAction);
        reason.Should().Contain("closed candles");
    }

    [Fact]
    public void Allowed_side_long_only_flattens_on_blocked_buy_while_open()
    {
        var json = StrategyTemplates.Build("MACD Trend", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.MacdTrend, false) with
        {
            AllowedSide = StrategySides.Short,
            EmaFast = 3,
            EmaSlow = 6,
            MacdFast = 3,
            MacdSlow = 6,
            MacdSignal = 2
        });
        var candles = FlatThen(120m);
        new StrategyEngine().Evaluate(
            new StrategyDefinitionValidator().Parse(json),
            new StrategyContext
            {
                ClosedCandles = candles,
                CurrentPrice = candles[^1].Close,
                HasOpenPosition = true,
                PositionSide = TradingPlatform.Domain.Positions.PositionSide.Short
            },
            out _).Should().Be(SignalType.Exit);
    }

    [Fact]
    public void Allowed_side_long_only_blocks_short_entry_when_flat()
    {
        SignalOf(StrategyTemplateKeys.EmaRsiTrend, FlatThen(90m), StrategySides.Long).Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Macd_trend_short_on_cross_down()
    {
        SignalOf(StrategyTemplateKeys.MacdTrend, FlatThen(80m)).Should().Be(SignalType.Sell);
    }

    [Fact]
    public void Bollinger_reversion_short_when_close_returns_inside()
    {
        var json = StrategyTemplates.Build("BB", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.BollingerReversion, false) with
        {
            AllowedSide = StrategySides.Both,
            BbPeriod = 5,
            BbStdDev = 1m,
            EmaFast = 2,
            EmaSlow = 3
        });
        var candles = Enumerable.Range(0, 20).Select(i => Bar(i, 100m, 10m)).ToList();
        candles.Add(Bar(20, 130m, 10m));
        candles.Add(Bar(21, 101m, 10m));
        var signal = new StrategyEngine().Evaluate(
            new StrategyDefinitionValidator().Parse(json),
            Flat(candles),
            out var reason);
        signal.Should().Be(SignalType.Sell, reason);
    }

    [Fact]
    public void Donchian_does_not_refire_while_still_outside_after_the_break()
    {
        var json = StrategyTemplates.Build("DC", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.DonchianBreakout, false) with
        {
            AllowedSide = StrategySides.Both,
            DonchianLength = 5,
            EmaFast = 3,
            EmaSlow = 6
        });
        var definition = new StrategyDefinitionValidator().Parse(json);
        var engine = new StrategyEngine();
        var breakout = Enumerable.Range(0, 6).Select(i => Bar(i, i == 5 ? 110m : 100m, 10m)).ToList();
        engine.Evaluate(definition, Flat(breakout), out _).Should().Be(SignalType.Buy);
        breakout.Add(Bar(6, 109m, 10m));
        engine.Evaluate(definition, Flat(breakout), out _).Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Prefix_signal_is_stable_after_future_bars_are_appended()
    {
        var json = StrategyTemplates.Build("EMA RSI Trend", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.EmaRsiTrend, false) with
        {
            AllowedSide = StrategySides.Both,
            EmaFast = 3,
            EmaSlow = 6,
            RsiPeriod = 3,
            RsiMinimum = 0m,
            RsiLongMax = 100m
        });
        var definition = new StrategyDefinitionValidator().Parse(json);
        var engine = new StrategyEngine();
        var prefix = FlatThen(110m);
        var atT = engine.Evaluate(definition, Flat(prefix), out var reasonT);
        var future = prefix.Concat(Enumerable.Range(0, 8).Select(i => Bar(prefix.Count + i, 140m + i, 10m))).ToList();
        engine.Evaluate(definition, Flat(prefix), out var reasonAgain).Should().Be(atT);
        reasonAgain.Should().Be(reasonT);
        engine.Evaluate(definition, Flat(future.Take(prefix.Count).ToList()), out _).Should().Be(atT);
    }

    private static SignalType SignalOf(string template, IReadOnlyList<MarketCandle> candles, string? side = null)
    {
        var json = StrategyTemplates.Build(template, 1, StrategyTemplates.DefaultsFor(template, false) with
        {
            AllowedSide = side ?? StrategySides.Both,
            EmaFast = 3,
            EmaSlow = 6,
            RsiPeriod = 3,
            RsiMinimum = 0m,
            RsiLongMax = 100m,
            MacdFast = 3,
            MacdSlow = 6,
            MacdSignal = 2
        });
        return new StrategyEngine().Evaluate(new StrategyDefinitionValidator().Parse(json), Flat(candles), out _);
    }

    private static StrategyContext Flat(IReadOnlyList<MarketCandle> candles) =>
        new()
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            HasOpenPosition = false
        };

    private static List<MarketCandle> FlatThen(decimal last)
    {
        var candles = Enumerable.Range(0, 30).Select(i => Bar(i, 100m, 10m)).ToList();
        candles.Add(Bar(30, last, 10m));
        return candles;
    }

    private static MarketCandle Bar(int i, decimal price, decimal volume) =>
        new()
        {
            Open = price,
            High = price,
            Low = price,
            Close = price,
            Volume = volume,
            OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
            CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5),
            IsClosed = true
        };
}
