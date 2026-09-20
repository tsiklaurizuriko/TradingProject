using System.Net;
using System.Text;
using FluentAssertions;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class FuturesHistoryTests
{
    [Fact]
    public void Funding_pagination_advances_start_and_dedupes()
    {
        var t0 = DateTimeOffset.UnixEpoch.AddDays(100);
        var page1 = Enumerable.Range(0, 1000).Select(i => FundingJson("BTCUSDT", t0.AddHours(i), 0.0001m + i)).ToArray();
        var page2 = new[]
        {
            FundingJson("BTCUSDT", t0.AddHours(999), 0.0001m + 999),
            FundingJson("BTCUSDT", t0.AddHours(1000), 0.01m)
        };
        var http = new HttpClient(new ScriptHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("startTime=" + t0.ToUnixTimeMilliseconds()))
            {
                return Arr(page1);
            }

            return Arr(page2);
        })) { BaseAddress = new Uri("https://fapi.binance.com/") };

        var client = new BinanceFuturesHistoryClient(http, TimeSpan.Zero);
        var rows = client.GetFundingAsync("BTCUSDT", t0, t0.AddHours(2000)).GetAwaiter().GetResult();
        rows.Should().HaveCount(1001);
        rows[^1].FundingRate.Should().Be(0.01m);
        rows.Select(r => r.FundingTime).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Funding_is_settled_rate_at_fundingTime()
    {
        var json = """[{"symbol":"BTCUSDT","fundingRate":"0.0001","fundingTime":1700000000000,"markPrice":"42000.0"}]""";
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var rows = BinanceFuturesHistoryClient.ParseFunding(doc.RootElement, "BTCUSDT");
        rows.Should().HaveCount(1);
        rows[0].FundingRate.Should().Be(0.0001m);
        rows[0].FundingTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
        rows[0].MarkPriceAtFunding.Should().Be(42000m);
    }

    [Fact]
    public void Oi_clamps_start_inside_thirty_day_window()
    {
        string? seen = null;
        var http = new HttpClient(new ScriptHandler(req =>
        {
            seen ??= req.RequestUri!.Query;
            return "[]";
        })) { BaseAddress = new Uri("https://fapi.binance.com/") };
        var client = new BinanceFuturesHistoryClient(http, TimeSpan.Zero);
        var start = DateTimeOffset.UtcNow.AddDays(-90);
        var end = DateTimeOffset.UtcNow;
        client.GetOpenInterestHistAsync("BTCUSDT", "5m", start, end).GetAwaiter().GetResult();
        seen.Should().NotBeNull();
        var startMs = long.Parse(System.Text.RegularExpressions.Regex.Match(seen!, @"startTime=(\d+)").Groups[1].Value);
        var age = end - DateTimeOffset.FromUnixTimeMilliseconds(startMs);
        age.TotalDays.Should().BeLessThan(31);
        age.TotalDays.Should().BeGreaterThan(20);
    }

    [Fact]
    public void Oi_bad_request_is_empty_not_fabricated()
    {
        var http = new HttpClient(new StatusHandler(HttpStatusCode.BadRequest, "[]"))
        {
            BaseAddress = new Uri("https://fapi.binance.com/")
        };
        var client = new BinanceFuturesHistoryClient(http, TimeSpan.Zero);
        var rows = client.GetOpenInterestHistAsync("BTCUSDT", "5m", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow)
            .GetAwaiter().GetResult();
        rows.Should().BeEmpty();
    }

    [Fact]
    public void Future_observation_is_not_selected_for_earlier_signal()
    {
        var signal = DateTimeOffset.UnixEpoch.AddHours(1);
        var first = Bar(0, 100m);
        first.CloseTime = signal;
        var second = Bar(1, 101m);
        second.CloseTime = signal.AddMinutes(5);
        var candles = new List<MarketCandle> { first, second };
        var future = signal.AddMilliseconds(1);
        var aligned = AlignedMarketSeries.Align([(future, 99m)], candles);
        aligned[0].Should().BeNull();
        (aligned[0] is null || candles[0].CloseTime >= future).Should().BeTrue();
        aligned[1].Should().Be(99m);
    }

    [Fact]
    public void Taker_missing_is_data_unavailable_not_zero()
    {
        TakerFlow.Imbalance(Bar(0, 100m, volume: 0m, takerBuy: 0m)).Should().BeNull();
        var dir = Path.Combine(Path.GetTempPath(), "tp-taker-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(dir, """[{"OpenTime":"2024-01-01T00:00:00+00:00","CloseTime":"2024-01-01T00:05:00+00:00","Volume":10,"TakerBuyVolume":0}]""");
        var extracted = FuturesDataAudit.ExtractTaker(dir, "BTCUSDT");
        extracted.Should().HaveCount(1);
        extracted[0].Imbalance.Should().BeNull();
        extracted[0].TakerSellVolume.Should().BeNull();
        File.Delete(dir);
    }

    [Fact]
    public void Research_engine_skips_when_required_data_missing()
    {
        ResearchDataCatalog.SkipStatus(StrategyTemplateKeys.FundingOiRegime, new ResearchDataSnapshot())
            .Should().Be(ResearchStatuses.DataUnavailable);
        ResearchDataCatalog.SkipStatus(
                StrategyTemplateKeys.FundingOiRegime,
                new ResearchDataSnapshot(["Funding", "OpenInterest"], ["OpenInterest"]))
            .Should().Be(ResearchStatuses.InsufficientData);
        ResearchStatuses.ImplementationError.Should().Be("IMPLEMENTATION_ERROR");
        ResearchStatuses.ValidationFailed.Should().Be("VALIDATION_FAILED");
        ResearchStatuses.OosFailed.Should().Be("OOS_FAILED");
        ResearchStatuses.NoTrades.Should().NotBe(ResearchStatuses.InsufficientData);
    }

    [Fact]
    public void Oi_pagination_uses_period_windows_not_latest_page_only()
    {
        var queries = new List<string>();
        var t0 = DateTimeOffset.UtcNow.AddDays(-4);
        var http = new HttpClient(new ScriptHandler(req =>
        {
            queries.Add(req.RequestUri!.Query);
            var startMs = long.Parse(System.Text.RegularExpressions.Regex.Match(req.RequestUri.Query, @"startTime=(\d+)").Groups[1].Value);
            var start = DateTimeOffset.FromUnixTimeMilliseconds(startMs);
            return Arr([OiJson("BTCUSDT", start, 1m), OiJson("BTCUSDT", start.AddMinutes(5), 2m)]);
        })) { BaseAddress = new Uri("https://fapi.binance.com/") };
        var client = new BinanceFuturesHistoryClient(http, TimeSpan.Zero);
        var rows = client.GetOpenInterestHistAsync("BTCUSDT", "5m", t0, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
        queries.Count.Should().BeGreaterThan(1);
        rows.Select(r => r.Timestamp).Should().OnlyHaveUniqueItems();
        rows.Should().NotBeEmpty();
    }

    [Fact]
    public void Oi_pagination_and_empty_page_stop()
    {
        var t0 = DateTimeOffset.UtcNow.AddHours(-2);
        var http = new HttpClient(new ScriptHandler(_ => Arr([
            OiJson("ETHUSDT", t0, 1000m),
            OiJson("ETHUSDT", t0.AddMinutes(5), 1001m)
        ]))) { BaseAddress = new Uri("https://fapi.binance.com/") };
        var client = new BinanceFuturesHistoryClient(http, TimeSpan.Zero);
        var rows = client.GetOpenInterestHistAsync("ETHUSDT", "5m", t0, t0.AddHours(1)).GetAwaiter().GetResult();
        rows.Should().HaveCount(2);
        BinanceFuturesHistoryClient.OpenInterestLimitation.Should().Be("OI_HISTORICAL_DATA_LIMITATION");
    }

    [Fact]
    public void Mark_and_index_close_times_build_causal_basis()
    {
        var t = DateTimeOffset.UnixEpoch.AddHours(3);
        var mark = new PricePoint("BTCUSDT", t.AddMinutes(-5), t, 101m);
        var index = new PricePoint("BTCUSDT", t.AddMinutes(-5), t, 100m);
        var basis = BinanceFuturesHistoryClient.BuildBasis([mark], [index]);
        basis.Should().HaveCount(1);
        basis[0].AbsoluteBasis.Should().Be(1m);
        basis[0].NormalizedBasis.Should().Be(0.01m);
        BinanceFuturesHistoryClient.BuildBasis([mark], [index with { CloseTime = t.AddMinutes(5) }]).Should().BeEmpty();
    }

    [Fact]
    public void Taker_sell_is_volume_minus_buy_and_zero_den_is_unavailable()
    {
        var bar = Bar(10, 100m, volume: 100m, takerBuy: 80m);
        TakerFlow.TakerSellVolume(bar).Should().Be(20m);
        TakerFlow.Imbalance(bar).Should().Be(0.60m);
        TakerFlow.Imbalance(Bar(11, 100m, volume: 0m, takerBuy: 0m)).Should().BeNull();
        TakerFlow.Imbalance(Bar(12, 100m, volume: 10m, takerBuy: 0m)).Should().BeNull();
        TakerFlow.Imbalance(Bar(13, 100m, volume: 10m, takerBuy: 11m)).Should().BeNull();
    }

    [Fact]
    public void Alignment_rejects_future_funding_oi_mark_index_basis_and_taker()
    {
        var candles = new List<MarketCandle> { Bar(0, 100m), Bar(1, 101m) };
        var signal = candles[0].CloseTime;
        var funding = AlignedMarketSeries.Align(
        [
            (signal, 0.001m),
            (signal.AddMilliseconds(1), 0.9m)
        ], candles);
        funding[0].Should().Be(0.001m);
        funding[1].Should().Be(0.9m);

        var oi = AlignedMarketSeries.Align([(signal.AddSeconds(1), 50m)], candles);
        oi[0].Should().BeNull();
        oi[1].Should().NotBeNull();

        var mark = AlignedMarketSeries.Align([(candles[1].CloseTime, 200m)], candles);
        mark[0].Should().BeNull();
        mark[1].Should().Be(200m);

        var index = AlignedMarketSeries.Align([(candles[0].CloseTime, 99m)], candles);
        index[0].Should().Be(99m);

        var basis = AlignedMarketSeries.Align([(signal.AddMinutes(5), 0.05m)], candles);
        basis[0].Should().BeNull();

        AlphaIndicatorSeries.TakerImbalance([Bar(0, 100m, volume: 10m, takerBuy: 0m)])[0].Should().BeNull();
    }

    [Fact]
    public void Cache_resume_does_not_redownload_when_range_is_covered()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tp-fut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "funding"));
        var t0 = DateTimeOffset.UnixEpoch.AddDays(10);
        var cached = new[] { new FundingPoint("BTCUSDT", t0, 0.0001m, 1m), new FundingPoint("BTCUSDT", t0.AddHours(8), 0.0002m, 1m) };
        File.WriteAllText(Path.Combine(dir, "funding", "BTCUSDT.json"), System.Text.Json.JsonSerializer.Serialize(cached));
        var calls = 0;
        var http = new HttpClient(new ScriptHandler(_ =>
        {
            calls++;
            throw new InvalidOperationException("network should not run");
        })) { BaseAddress = new Uri("https://fapi.binance.com/") };
        var client = new BinanceFuturesHistoryClient(http, TimeSpan.Zero);
        var rows = FuturesHistoryCache.LoadOrFetchFundingAsync(client, dir, "BTCUSDT", t0, t0.AddHours(8), false)
            .GetAwaiter().GetResult();
        calls.Should().Be(0);
        rows.Should().HaveCount(2);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Duplicate_funding_times_keep_last()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("[" +
            FundingJson("BTCUSDT", DateTimeOffset.UnixEpoch, 0.1m) + "," +
            FundingJson("BTCUSDT", DateTimeOffset.UnixEpoch, 0.2m) + "]");
        var parsed = BinanceFuturesHistoryClient.ParseFunding(doc.RootElement, "BTCUSDT");
        parsed.Should().HaveCount(2);
        var http = new HttpClient(new ScriptHandler(_ => Arr([
            FundingJson("BTCUSDT", DateTimeOffset.UnixEpoch, 0.1m),
            FundingJson("BTCUSDT", DateTimeOffset.UnixEpoch, 0.2m)
        ]))) { BaseAddress = new Uri("https://fapi.binance.com/") };
        var rows = new BinanceFuturesHistoryClient(http, TimeSpan.Zero)
            .GetFundingAsync("BTCUSDT", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)).GetAwaiter().GetResult();
        rows.Should().HaveCount(1);
        rows[0].FundingRate.Should().Be(0.2m);
    }

    [Fact]
    public void Missing_required_data_is_data_unavailable_not_no_trades()
    {
        var empty = new ResearchDataSnapshot();
        ResearchDataCatalog.SkipStatus(StrategyTemplateKeys.FundingBasisRv, empty).Should().Be(ResearchStatuses.DataUnavailable);
        ResearchDataCatalog.SkipStatus(StrategyTemplateKeys.CryptoPairsArb, empty).Should().Be(ResearchStatuses.DataUnavailable);
        ResearchDataCatalog.SkipStatus(StrategyTemplateKeys.RegimeStrategyRouter, empty).Should().Be(ResearchStatuses.Researching);
        ResearchDataCatalog.SkipStatus(StrategyTemplateKeys.EmaRsiTrend, empty).Should().Be(ResearchStatuses.Researching);
        ResearchStatuses.NoTrades.Should().Be("NO_TRADES");
        ResearchStatuses.DataUnavailable.Should().NotBe(ResearchStatuses.InsufficientData);
        ResearchStatuses.DataUnavailable.Should().NotBe(ResearchStatuses.ImplementationError);
        ResearchStatuses.NoTrades.Should().NotBe(ResearchStatuses.OosFailed);
    }

    [Fact]
    public void Required_datasets_are_explicit()
    {
        StrategyTemplateKeys.RequiredDatasets(StrategyTemplateKeys.TakerFlowMomentum).Should().Contain("TakerFlow");
        StrategyTemplateKeys.RequiredDatasets(StrategyTemplateKeys.FundingBasisRv).Should().Contain(new[] { "Funding", "Basis" });
        StrategyTemplateKeys.RequiredDatasets(StrategyTemplateKeys.RsiPullback).Should().Equal("OHLCV");
    }

    [Fact]
    public void Replay_alignment_does_not_pass_future_funding_into_prefix()
    {
        var candles = Enumerable.Range(0, 3).Select(i => Bar(i, 100m + i)).ToList();
        var future = candles[2].CloseTime.AddMinutes(1);
        var aligned = FuturesHistoryCache.Align(
            candles,
            [new FundingPoint("BTCUSDT", future, 0.5m, null)],
            null, null, null, null);
        aligned.FundingRate![0].Should().BeNull();
        aligned.FundingRate[1].Should().BeNull();
        aligned.FundingRate[2].Should().BeNull();
    }

    private static MarketCandle Bar(int i, decimal close, decimal volume = 10m, decimal takerBuy = 0m) =>
        new()
        {
            Open = close,
            High = close,
            Low = close,
            Close = close,
            Volume = volume,
            TakerBuyVolume = takerBuy,
            IsClosed = true,
            OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
            CloseTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5),
            ExchangeTimestamp = DateTimeOffset.UnixEpoch.AddMinutes(i * 5 + 5)
        };

    private static string FundingJson(string symbol, DateTimeOffset time, decimal rate) =>
        $"{{\"symbol\":\"{symbol}\",\"fundingRate\":\"{rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}\",\"fundingTime\":{time.ToUnixTimeMilliseconds()},\"markPrice\":\"1\"}}";

    private static string OiJson(string symbol, DateTimeOffset time, decimal oi) =>
        $"{{\"symbol\":\"{symbol}\",\"sumOpenInterest\":\"{oi.ToString(System.Globalization.CultureInfo.InvariantCulture)}\",\"timestamp\":{time.ToUnixTimeMilliseconds()}}}";

    private static string Arr(IEnumerable<string> items) => "[" + string.Join(",", items) + "]";

    private sealed class ScriptHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _body;

        public ScriptHandler(Func<HttpRequestMessage, string> body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body(request), Encoding.UTF8, "application/json")
            });
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StatusHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }
}
