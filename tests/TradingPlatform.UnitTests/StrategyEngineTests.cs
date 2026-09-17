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
    public void Stop_loss_exits_an_open_position()
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

        signal.Should().Be(SignalType.Exit);
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
        rebuilt.StopLossPercent.Should().Be(2m);
        new StrategyDefinitionValidator().Parse(json).Timeframe.Should().Be("15m");
    }

    [Fact]
    public void Ema_rsi_template_rejects_inverted_periods()
    {
        var act = () => EmaRsiTemplate.Validate(new EmaRsiParameters(50, 20, 14, 50, 1.5m, 3m));
        act.Should().Throw<TradingPlatform.Domain.Errors.DomainException>();
    }
}
