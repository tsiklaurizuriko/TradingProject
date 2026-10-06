using FluentAssertions;
using Xunit;
using TradingPlatform.Binance;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Trading;

namespace TradingPlatform.UnitTests;

[Collection(BinanceWeightGateCollection.Name)]
public sealed class BotCycleSpeedTests
{
    [Fact]
    public void Flat_bot_waits_for_the_next_candle_after_a_hold()
    {
        var close = new DateTimeOffset(2026, 10, 2, 21, 15, 0, TimeSpan.Zero);
        BotCycleSchedule.FlatCandleDue(close.AddMinutes(14), Timeframe.FifteenMinutes, close).Should().BeFalse();
        BotCycleSchedule.FlatCandleDue(close.AddMinutes(15), Timeframe.FifteenMinutes, close).Should().BeTrue();
    }

    [Fact]
    public void Flat_bot_with_no_decision_is_due_immediately()
    {
        BotCycleSchedule.FlatCandleDue(DateTimeOffset.UtcNow, Timeframe.FiveMinutes, null).Should().BeTrue();
    }

    [Theory]
    [InlineData("fapi/v1/klines?symbol=BTCUSDT&interval=15m&limit=120", 2)]
    [InlineData("fapi/v1/klines?symbol=BTCUSDT&interval=15m&limit=99", 1)]
    [InlineData("fapi/v1/klines?symbol=BTCUSDT&interval=1h&limit=500", 5)]
    [InlineData("fapi/v1/klines?symbol=BTCUSDT&interval=1h", 5)]
    [InlineData("fapi/v1/ticker/price", 2)]
    [InlineData("fapi/v1/ticker/price?symbol=BTCUSDT", 1)]
    [InlineData("fapi/v1/ticker/24hr", 40)]
    [InlineData("fapi/v1/exchangeInfo", 1)]
    [InlineData("fapi/v1/openOrders", 40)]
    [InlineData("fapi/v1/openOrders?symbol=BTCUSDT", 1)]
    [InlineData("fapi/v1/order?symbol=BTCUSDT", 1)]
    [InlineData("fapi/v1/algoOrder?symbol=BTCUSDT", 1)]
    public void Public_request_weight_matches_the_usd_m_schedule(string url, int weight)
    {
        BinancePublicWeight.ForRequest(url).Should().Be(weight);
    }

    [Fact]
    public async Task Weight_gate_stops_before_the_minute_budget_is_exceeded()
    {
        BinancePublicWeightGate.Reset();
        try
        {
            var now = DateTimeOffset.UtcNow;
            BinancePublicWeightGate.UseClock(() => now);
            (await BinancePublicWeightGate.TryAcquireAsync(BinancePublicWeight.MinuteBudget, TimeSpan.Zero, CancellationToken.None))
                .Should().BeTrue();
            (await BinancePublicWeightGate.TryAcquireAsync(1, TimeSpan.Zero, CancellationToken.None))
                .Should().BeFalse();
            now = now.AddMinutes(1);
            (await BinancePublicWeightGate.TryAcquireAsync(1, TimeSpan.Zero, CancellationToken.None))
                .Should().BeTrue();
            BinancePublicWeightGate.CoolDown(TimeSpan.FromMinutes(1));
            (await BinancePublicWeightGate.TryAcquireAsync(1, TimeSpan.Zero, CancellationToken.None))
                .Should().BeFalse();
        }
        finally
        {
            BinancePublicWeightGate.Reset();
        }
    }
}
