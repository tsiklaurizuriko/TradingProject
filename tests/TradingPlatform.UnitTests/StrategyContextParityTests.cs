using System.Text.Json;
using FluentAssertions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Binance;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class StrategyContextParityTests
{
    private const int Warmup = 300;

    [Fact]
    public void Live_prefix_evaluation_matches_backtest_index_with_higher_timeframe_and_futures_inputs()
    {
        var all = ValidationBenchmark.CreateDeterministicSeries(Warmup + 160)
            .Select((c, i) => Copy(c, c.Volume * (0.35m + (i % 7) * 0.05m), true))
            .ToList();
        var candles = all.Skip(Warmup).ToList();
        var inputs = new StrategyMarketInputs(
            all,
            all.Select((c, i) => new TimedValue(c.OpenTime.AddMinutes(30), 1_000m + i * 3m + (i % 5) * 20m)).ToList(),
            all.Where((_, i) => i % 8 == 0).Select((c, i) => new TimedValue(c.OpenTime, (i % 3 - 1) * 0.0001m)).ToList());

        var engine = new StrategyEngine();
        var fullCache = new CausalIndicatorCache(candles);
        var fullHtf = StrategyMarketContext.HigherTimeframeCache(inputs);
        var fullFutures = StrategyMarketContext.Futures(candles, inputs);
        var checkedBars = 0;
        var inputsMattered = 0;
        foreach (var key in StrategyTemplateKeys.All.Where(StrategyMarketContext.NeedsAny))
        {
            var definition = StrategyValidation.Definition(key, "1h");
            for (var i = 60; i < candles.Count; i++)
            {
                var backtest = engine.EvaluateDetailAt(
                    definition,
                    new StrategyContext
                    {
                        ClosedCandles = candles,
                        CurrentPrice = candles[i].Close,
                        HigherTimeframeCache = fullHtf,
                        OpenInterest = fullFutures.OpenInterest,
                        FundingRate = fullFutures.FundingRate
                    },
                    fullCache,
                    i);

                var prefix = candles.Take(i + 1).ToList();
                var known = KnownAt(inputs, prefix[^1].CloseTime);
                var futures = StrategyMarketContext.Futures(prefix, known);
                var live = engine.EvaluateDetailAt(
                    definition,
                    new StrategyContext
                    {
                        ClosedCandles = prefix,
                        CurrentPrice = prefix[^1].Close,
                        HigherTimeframeCache = StrategyMarketContext.HigherTimeframeCache(known),
                        OpenInterest = futures.OpenInterest,
                        FundingRate = futures.FundingRate
                    },
                    new CausalIndicatorCache(prefix),
                    i);

                live.Signal.Should().Be(backtest.Signal, "{0} bar {1}: live '{2}' vs backtest '{3}'", key, i, live.Reason, backtest.Reason);
                live.Reason.Should().Be(backtest.Reason, "{0} bar {1}", key, i);
                checkedBars++;

                var blind = engine.EvaluateDetailAt(
                    definition,
                    new StrategyContext { ClosedCandles = candles, CurrentPrice = candles[i].Close },
                    fullCache,
                    i);
                if (blind.Reason != backtest.Reason)
                {
                    inputsMattered++;
                }
            }
        }

        checkedBars.Should().BeGreaterThan(0);
        inputsMattered.Should().BeGreaterThan(0, "the parity check must exercise the higher timeframe and futures inputs");
    }

    [Fact]
    public void Context_rules_name_the_inputs_each_template_reads()
    {
        StrategyMarketContext.NeedsHigherTimeframe(StrategyTemplateKeys.EmaRsiTrend).Should().BeTrue();
        StrategyMarketContext.NeedsHigherTimeframe(StrategyTemplateKeys.FlowZone).Should().BeTrue();
        StrategyMarketContext.NeedsOpenInterest(StrategyTemplateKeys.FlowZone).Should().BeTrue();
        StrategyMarketContext.NeedsOpenInterest(StrategyTemplateKeys.SqueezeWatch).Should().BeTrue();
        StrategyMarketContext.NeedsFunding(StrategyTemplateKeys.SqueezeWatch).Should().BeTrue();
        StrategyMarketContext.NeedsFunding(StrategyTemplateKeys.EmaRsiTrend).Should().BeFalse();
        StrategyMarketContext.NeedsAny(StrategyTemplateKeys.DonchianBreakout).Should().BeFalse();
    }

    [Fact]
    public void Aligned_open_interest_never_shows_a_value_stamped_after_the_bar_close()
    {
        var candles = ValidationBenchmark.CreateDeterministicSeries(10);
        var rows = new List<TimedValue>
        {
            new(candles[2].CloseTime, 5m),
            new(candles[2].CloseTime.AddMilliseconds(1), 6m),
            new(candles[5].OpenTime.AddMinutes(10), 7m)
        };
        var aligned = StrategyMarketContext.Futures(candles, new StrategyMarketInputs(null, rows, null)).OpenInterest!;

        aligned.Should().HaveCount(candles.Count);
        aligned[1].Should().BeNull();
        aligned[2].Should().Be(5m);
        aligned[3].Should().Be(6m);
        aligned[4].Should().Be(6m);
        aligned[5].Should().Be(7m);
        aligned[9].Should().Be(7m);
    }

    [Fact]
    public void Higher_timeframe_cache_ignores_the_open_bar()
    {
        var bars = ValidationBenchmark.CreateDeterministicSeries(5).ToList();
        bars[^1] = Copy(bars[^1], 0m, false);
        var cache = StrategyMarketContext.HigherTimeframeCache(new StrategyMarketInputs(bars, null, null));
        cache.Should().NotBeNull();
        cache!.Candles.Should().HaveCount(4);
        StrategyMarketContext.HigherTimeframeCache(StrategyMarketInputs.None).Should().BeNull();
    }

    [Fact]
    public void Parse_timed_values_reads_string_numbers_skips_bad_rows_and_sorts()
    {
        using var doc = JsonDocument.Parse("""
            [
              {"timestamp": 2000, "sumOpenInterest": "12.5"},
              {"timestamp": 1000, "sumOpenInterest": "10"},
              {"timestamp": 3000, "sumOpenInterest": null},
              {"sumOpenInterest": "9"},
              {"timestamp": 4000, "sumOpenInterest": "abc"}
            ]
            """);
        var rows = BinancePublicMarketDataClient.ParseTimedValues(doc.RootElement, "timestamp", "sumOpenInterest");

        rows.Select(r => r.Value).Should().Equal(10m, 12.5m);
        rows[0].Time.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1000));
    }

    [Fact]
    public void Market_data_age_uses_the_price_time_when_the_candle_is_on_schedule()
    {
        var close = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var now = close.AddMinutes(5);
        MarketDataAge.Milliseconds(now, now.AddSeconds(-2), close, TimeSpan.FromMinutes(15)).Should().Be(2_000);
    }

    [Fact]
    public void Market_data_age_without_a_live_price_is_the_candle_close_age()
    {
        var close = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        MarketDataAge.Milliseconds(close.AddSeconds(30), null, close, TimeSpan.FromMinutes(15)).Should().Be(30_000);
        MarketDataAge.Milliseconds(close.AddMinutes(5), null, close, TimeSpan.FromMinutes(15))
            .Should().BeGreaterThan(RiskEngine.MaxMarketDataAgeMs);
    }

    [Fact]
    public void Market_data_age_flags_a_missed_candle_even_with_a_fresh_price()
    {
        var close = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var now = close.AddMinutes(17);
        MarketDataAge.Milliseconds(now, now, close, TimeSpan.FromMinutes(15)).Should().Be(120_000);
    }

    [Fact]
    public void Market_data_age_is_never_negative()
    {
        var close = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        MarketDataAge.Milliseconds(close.AddSeconds(-5), close, close, TimeSpan.FromMinutes(15)).Should().Be(0);
    }

    [Fact]
    public void News_sizing_refuses_a_stale_or_empty_live_book()
    {
        var now = DateTimeOffset.UtcNow;
        var fresh = new LiveAccountSnapshot { HasKeys = true, FuturesBookFresh = true, UpdatedAt = now, UsdtFree = 400m, FuturesEquity = 520m };

        NewsTradeAdapter.LiveEquity(fresh, now, TimeSpan.FromMinutes(2)).Should().Be((520m, 400m, (string?)null));
        NewsTradeAdapter.LiveEquity(new LiveAccountSnapshot(), now, TimeSpan.FromMinutes(2)).Block.Should().NotBeNull();
        NewsTradeAdapter.LiveEquity(new LiveAccountSnapshot { HasKeys = true, FuturesBookFresh = true, UpdatedAt = now.AddMinutes(-5), UsdtFree = 400m, FuturesEquity = 520m }, now, TimeSpan.FromMinutes(2))
            .Block.Should().NotBeNull();
        NewsTradeAdapter.LiveEquity(new LiveAccountSnapshot { HasKeys = true, FuturesBookFresh = false, UpdatedAt = now, UsdtFree = 400m }, now, TimeSpan.FromMinutes(2))
            .Block.Should().NotBeNull();
        NewsTradeAdapter.LiveEquity(new LiveAccountSnapshot { HasKeys = true, FuturesBookFresh = true, UpdatedAt = now, UsdtFree = 0m }, now, TimeSpan.FromMinutes(2))
            .Block.Should().NotBeNull();
    }

    [Fact]
    public void News_client_order_id_is_stable_per_event_and_coin_and_fits_binance()
    {
        var id = NewsTradeAdapter.ClientOrderId("evt-1", "BTCUSDT");
        id.Should().Be(NewsTradeAdapter.ClientOrderId("evt-1", "btcusdt"));
        id.Should().NotBe(NewsTradeAdapter.ClientOrderId("evt-1", "ETHUSDT"));
        id.Should().NotBe(NewsTradeAdapter.ClientOrderId("evt-2", "BTCUSDT"));
        (id + "-sl").Length.Should().BeLessThanOrEqualTo(36);
    }

    private static MarketCandle Copy(MarketCandle c, decimal takerBuy, bool closed) => new()
    {
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Volume = c.Volume,
        TakerBuyVolume = takerBuy,
        IsClosed = closed,
        OpenTime = c.OpenTime,
        CloseTime = c.CloseTime,
        ExchangeTimestamp = c.ExchangeTimestamp
    };

    private static StrategyMarketInputs KnownAt(StrategyMarketInputs inputs, DateTimeOffset close) => new(
        inputs.HigherTimeframe?.Where(c => c.CloseTime <= close).ToList(),
        inputs.OpenInterest?.Where(r => r.Time <= close).ToList(),
        inputs.Funding?.Where(r => r.Time <= close).ToList());
}
