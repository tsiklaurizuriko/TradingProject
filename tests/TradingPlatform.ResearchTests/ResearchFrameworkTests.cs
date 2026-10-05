using System.Text.Json;
using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research.Framework;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class ResearchFrameworkTests
{
    [Fact]
    public void Normal_helpers_match_reference_values()
    {
        ResearchStatistics.NormalCdf(1.96).Should().BeApproximately(0.975, 1e-4);
        ResearchStatistics.NormalCdf(0).Should().BeApproximately(0.5, 1e-7);
        ResearchStatistics.NormalInverse(0.975).Should().BeApproximately(1.95996, 1e-4);
        ResearchStatistics.NormalInverse(0.01).Should().BeApproximately(-2.32635, 1e-4);
    }

    [Fact]
    public void Expected_max_sharpe_grows_with_trials_and_matches_the_closed_form()
    {
        ResearchStatistics.ExpectedMaxSharpe(1, 1d).Should().Be(0d);
        ResearchStatistics.ExpectedMaxSharpe(100, 1d).Should().BeApproximately(2.53, 0.02);
        ResearchStatistics.ExpectedMaxSharpe(1000, 1d).Should().BeGreaterThan(ResearchStatistics.ExpectedMaxSharpe(100, 1d));
    }

    [Fact]
    public void Deflated_sharpe_penalizes_many_trials()
    {
        var m = new SharpeMoments(0.1, 500, 0d, 3d);
        var psr = ResearchStatistics.ProbabilisticSharpe(m);
        var dsrOne = ResearchStatistics.DeflatedSharpe(m, 1, 0.002);
        var dsrMany = ResearchStatistics.DeflatedSharpe(m, 200, 0.002);

        psr.Should().BeGreaterThan(0.98);
        dsrOne.Should().BeApproximately(psr, 1e-12);
        dsrMany.Should().BeLessThan(psr);
        ResearchStatistics.ProbabilisticSharpe(new SharpeMoments(0d, 500, 0d, 3d)).Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void Moments_of_a_symmetric_series_have_zero_skew()
    {
        var returns = Enumerable.Range(0, 200).Select(i => i % 2 == 0 ? 0.01 : -0.01).ToList();
        var m = ResearchStatistics.Moments(returns);
        m.Skewness.Should().BeApproximately(0d, 1e-9);
        m.Sharpe.Should().BeApproximately(0d, 1e-9);
        m.Observations.Should().Be(200);
    }

    [Fact]
    public void Benjamini_hochberg_keeps_input_order_and_controls_discoveries()
    {
        double[] sorted = [0.001, 0.008, 0.039, 0.041, 0.042, 0.06, 0.074, 0.205, 0.212, 0.216];
        var shuffled = new[] { sorted[5], sorted[1], sorted[9], sorted[0], sorted[3], sorted[2], sorted[4], sorted[6], sorted[7], sorted[8] };

        var flags = ResearchStatistics.BenjaminiHochberg(shuffled, 0.05);

        flags.Count(f => f).Should().Be(2);
        flags[1].Should().BeTrue();
        flags[3].Should().BeTrue();
        ResearchStatistics.BenjaminiHochberg([], 0.05).Should().BeEmpty();
    }

    [Fact]
    public void Block_bootstrap_is_seeded_and_sees_no_loss_on_all_positive_days()
    {
        var returns = Enumerable.Range(0, 60).Select(_ => 0.001).ToList();
        var a = Robustness.BlockBootstrap(returns, 5, 200, 3);
        var b = Robustness.BlockBootstrap(returns, 5, 200, 3);
        a.Should().Be(b);
        a.ProbabilityOfLoss.Should().Be(0d);
        a.P95MaxDrawdownPercent.Should().Be(0d);

        var mixed = Enumerable.Range(0, 120).Select(i => i % 3 == 0 ? -0.02 : 0.009).ToList();
        var m = Robustness.BlockBootstrap(mixed, 5, 300, 3);
        m.P95MaxDrawdownPercent.Should().BeGreaterThan(0d);
        m.P5ReturnPercent.Should().BeLessThanOrEqualTo(m.P95ReturnPercent);
    }

    [Fact]
    public void Shuffle_drawdown_bounds_the_observed_path()
    {
        var s = Robustness.ShuffleDrawdown([-10m, -10m, 30m], 100m, 500, 1);
        s.ObservedMaxDrawdownPercent.Should().BeApproximately(20d, 1e-9);
        s.P95MaxDrawdownPercent.Should().BeLessThanOrEqualTo(20d + 1e-9);
        s.MedianMaxDrawdownPercent.Should().BeGreaterThan(0d);
    }

    [Fact]
    public void Neighbours_are_twenty_percent_away_and_integers_stay_distinct()
    {
        Robustness.Neighbours(20m, integer: true).Should().Equal(16m, 24m);
        Robustness.Neighbours(2m, integer: true).Should().Equal(1m, 3m);
        Robustness.Neighbours(2m, integer: false).Should().Equal(1.6m, 2.4m);
    }

    [Fact]
    public void Stability_requires_seventy_percent_of_neighbours_to_hold()
    {
        Robustness.Stability(1.5m, [1.4m, 1.2m, 0.9m]).Stable.Should().BeFalse();
        Robustness.Stability(1.5m, [1.4m, 1.3m, 1.2m]).Stable.Should().BeTrue();
        Robustness.Stability(1.5m, [1.4m, 1.0m, 1.3m]).Score.Should().BeApproximately(2d / 3d, 1e-9);
        Robustness.Stability(1.5m, []).Stable.Should().BeFalse();
    }

    [Fact]
    public void Breadth_counts_only_coins_with_trades()
    {
        Robustness.Breadth([(5, 10m), (3, -2m), (0, 0m), (4, 1m)]).Should().BeApproximately(2m / 3m, 0.0001m);
        Robustness.Breadth([]).Should().Be(0m);
    }

    [Fact]
    public void Cost_profiles_escalate_and_stress_adds_a_bar_of_delay()
    {
        CostProfiles.Bucket(1_000_000_000m).Should().Be(LiquidityBucket.Major);
        CostProfiles.Bucket(100_000_000m).Should().Be(LiquidityBucket.Mid);
        CostProfiles.Bucket(1_000_000m).Should().Be(LiquidityBucket.Small);

        var b = CostProfiles.For(CostProfileKind.Base, LiquidityBucket.Mid);
        var c = CostProfiles.For(CostProfileKind.Conservative, LiquidityBucket.Mid);
        var s = CostProfiles.For(CostProfileKind.Stress, LiquidityBucket.Mid);
        b.FeePercent.Should().Be(CostProfiles.TakerFeePercent);
        c.SlippagePercent.Should().BeGreaterThan(b.SlippagePercent);
        s.SlippagePercent.Should().BeGreaterThan(c.SlippagePercent);
        s.FeePercent.Should().BeGreaterThan(c.FeePercent);
        s.ExecutionDelayBars.Should().Be(1);
        b.ExecutionDelayBars.Should().Be(0);

        CostProfiles.For(CostProfileKind.Base, LiquidityBucket.Major, sampledHalfSpreadPercent: 0.03m).SlippagePercent.Should().Be(0.03m);
        CostProfiles.For(CostProfileKind.Base, LiquidityBucket.Small).SlippagePercent
            .Should().BeGreaterThan(CostProfiles.For(CostProfileKind.Base, LiquidityBucket.Major).SlippagePercent);
        CostProfiles.HalfSpreadPercent(99.99m, 100.01m).Should().BeApproximately(0.01m, 0.000001m);
        CostProfiles.ImpactPercent(1_000m, 10_000_000m, 4m).Should().BeApproximately(0.04m, 0.000001m);
        CostProfiles.ImpactPercent(2_500m, 5_000_000m, 5m).Should().BeApproximately(5m * (decimal)Math.Sqrt(0.0005), 0.000001m);
        CostProfiles.ImpactPercent(1_000m, 0m, 4m).Should().Be(CostProfiles.MaxImpactPercent);
        CostProfiles.ImpactPercent(1_000m, 10_000_000m, 0m).Should().Be(CostProfiles.MaxImpactPercent);
        CostProfiles.ImpactPercent(0m, 10_000_000m, 4m).Should().Be(0m);

        var settings = new ReplaySettings(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), 1000m, 1m, 3m, 0m, 0m, 2m, 4m);
        var applied = CostProfiles.Apply(settings, s);
        applied.FeePercent.Should().Be(s.FeePercent);
        applied.SlippagePercent.Should().Be(s.SlippagePercent);
        applied.ExecutionDelayBars.Should().Be(1);
    }

    [Fact]
    public void Registry_counts_distinct_configurations_and_survives_a_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"registry-{Guid.NewGuid():N}.jsonl");
        try
        {
            var registry = new ExperimentRegistry(path);
            registry.Append(Record("ema", "fast=9", 0.05));
            registry.Append(Record("ema", "fast=12", 0.01));
            registry.Append(Record("ema", "fast=9", 0.05));
            registry.Append(Record("donchian", "n=20", 0.02));

            registry.TrialCount("ema").Should().Be(2);
            registry.SharpeVariance("ema").Should().BeApproximately(0.0008, 1e-9);

            var reloaded = new ExperimentRegistry(path);
            reloaded.Records.Should().HaveCount(4);
            reloaded.TrialCount("ema").Should().Be(2);
            reloaded.TrialCount("donchian").Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Data_fingerprint_changes_when_any_close_changes()
    {
        var a = Bars(Enumerable.Range(0, 20).Select(i => 100m + i).ToArray());
        var b = Bars(Enumerable.Range(0, 20).Select(i => 100m + i).ToArray());
        b[7].Close += 0.01m;

        var ha = DataFingerprint.Of([("BTCUSDT", "1h", a)]);
        DataFingerprint.Of([("BTCUSDT", "1h", a)]).Should().Be(ha);
        DataFingerprint.Of([("BTCUSDT", "1h", b)]).Should().NotBe(ha);
        DataFingerprint.Of([("ETHUSDT", "1h", a)]).Should().NotBe(ha);
    }

    [Fact]
    public void Oos_vault_hands_out_the_last_twenty_percent_once_and_logs_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oos-{Guid.NewGuid():N}.jsonl");
        try
        {
            var split = OosVault.Split(100);
            split.InSample.Should().Be((0, 60));
            split.Validation.Should().Be((60, 80));

            var vault = new OosVault(path);
            vault.HasBeenOpened("ema", "h1").Should().BeFalse();
            var ticket = vault.Open("ema", "h1", 100, "final check after validation freeze");
            ticket.From.Should().Be(80);
            ticket.To.Should().Be(100);

            var again = () => vault.Open("ema", "h1", 100, "peek");
            again.Should().Throw<InvalidOperationException>();

            var reloaded = new OosVault(path);
            reloaded.HasBeenOpened("ema", "h1").Should().BeTrue();
            reloaded.Accesses.Should().ContainSingle();
            var reopen = () => reloaded.Open("ema", "h1", 100, "second session");
            reopen.Should().Throw<InvalidOperationException>();

            reloaded.Open("ema", "h2", 100, "new data").From.Should().Be(80);
            var noReason = () => reloaded.Open("bb", "h1", 100, " ");
            noReason.Should().Throw<ArgumentException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Point_in_time_universe_drops_late_listings_and_flags_survivorship()
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var json = JsonDocument.Parse($$"""
        {"symbols":[
          {"symbol":"BTCUSDT","contractType":"PERPETUAL","quoteAsset":"USDT","status":"TRADING","onboardDate":{{start.AddYears(-5).ToUnixTimeMilliseconds()}},"deliveryDate":4133404800000},
          {"symbol":"NEWUSDT","contractType":"PERPETUAL","quoteAsset":"USDT","status":"TRADING","onboardDate":{{start.AddDays(-10).ToUnixTimeMilliseconds()}},"deliveryDate":4133404800000},
          {"symbol":"OLDUSDT","contractType":"PERPETUAL","quoteAsset":"USDT","status":"SETTLING","onboardDate":{{start.AddYears(-2).ToUnixTimeMilliseconds()}},"deliveryDate":{{start.AddDays(40).ToUnixTimeMilliseconds()}}},
          {"symbol":"BTCUSDT_250328","contractType":"CURRENT_QUARTER","quoteAsset":"USDT","status":"TRADING","onboardDate":1}
        ]}
        """);

        var contracts = PointInTimeUniverse.ParseExchangeInfo(json.RootElement);
        contracts.Select(c => c.Symbol).Should().BeEquivalentTo("BTCUSDT", "NEWUSDT", "OLDUSDT");
        contracts.Single(c => c.Symbol == "BTCUSDT").DeliveryDate.Should().BeNull();

        PointInTimeUniverse.EligibleAt(contracts, start).Should().Equal("BTCUSDT", "OLDUSDT");

        var bias = PointInTimeUniverse.Assess(contracts, start, start.AddDays(90));
        bias.CurrentListingsOnly.Should().BeTrue();
        bias.ExcludedNotYetListed.Should().Be(1);
        bias.ExcludedDelistedInWindow.Should().Be(1);
        bias.Note.Should().Contain("SURVIVORSHIP");

        PointInTimeUniverse.Assess(contracts, start, start.AddDays(90), ["LUNAUSDT"]).CurrentListingsOnly.Should().BeFalse();
    }

    [Theory]
    [InlineData(12, 1.6, ResearchStatuses.Research)]
    [InlineData(15, 0.7, ResearchStatuses.Rejected)]
    [InlineData(80, 0.97, ResearchStatuses.Rejected)]
    [InlineData(80, 1.08, ResearchStatuses.Weak)]
    public void Verdict_rejects_or_holds_back_weak_validation(int trades, double pf, string expected)
    {
        ResearchVerdict.Assign(new VerdictInputs(trades, (decimal)pf, 0.99, 0.8m, true)).Status.Should().Be(expected);
    }

    [Fact]
    public void Verdict_needs_every_promising_gate_then_oos_and_stress_for_paper()
    {
        var promisingGates = new VerdictInputs(120, 1.3m, 0.95, 0.6m, true);
        ResearchVerdict.Assign(promisingGates with { DeflatedSharpeProbability = 0.8 }).Status.Should().Be(ResearchStatuses.Weak);
        ResearchVerdict.Assign(promisingGates with { Breadth = 0.5m }).Status.Should().Be(ResearchStatuses.Weak);
        ResearchVerdict.Assign(promisingGates with { Stable = false }).Status.Should().Be(ResearchStatuses.Weak);
        ResearchVerdict.Assign(promisingGates).Status.Should().Be(ResearchStatuses.Promising);

        var paper = promisingGates with { OosOpenedOnce = true, OosProfitFactorConservative = 1.1m, StressProfitFactor = 1.02m };
        ResearchVerdict.Assign(paper with { StressProfitFactor = 0.95m }).Status.Should().Be(ResearchStatuses.Promising);
        ResearchVerdict.Assign(paper with { OosProfitFactorConservative = 1.0m }).Status.Should().Be(ResearchStatuses.Promising);
        ResearchVerdict.Assign(paper).Status.Should().Be(ResearchStatuses.PaperCandidate);
        ResearchVerdict.Assign(paper with { ForwardWeeks = 3, ForwardMatchesBacktest = true }).Status.Should().Be(ResearchStatuses.PaperCandidate);
        ResearchVerdict.Assign(paper with { ForwardWeeks = 4, ForwardMatchesBacktest = true }).Status.Should().Be(ResearchStatuses.LiveCandidate);
        ResearchVerdict.Assign(paper).Reasons.Should().Contain(r => r.Contains("survivorship"));
    }

    [Fact]
    public void Calmar_is_annualized_return_over_drawdown()
    {
        DailyReturnMetrics.Calmar(10m, 365, 5m).Should().BeApproximately(2m, 0.0001m);
        DailyReturnMetrics.Calmar(10m, 365, 0m).Should().BeNull();
        DailyReturnMetrics.Calmar(21m, 730, 10m).Should().BeApproximately(1m, 0.001m);
    }

    [Fact]
    public void Execution_delay_moves_every_fill_one_bar_later()
    {
        var candles = Bars(Enumerable.Range(0, 60).Select(i => 100m + i * 0.1m).ToArray());
        var settings = new ReplaySettings(candles[0].OpenTime, candles[^1].CloseTime, 10_000m, 1m, 3m, 0.05m, 0.02m, 5m, 10m);
        var definition = new StrategyDefinition { Name = "scripted", Timeframe = "1h" };

        var now = new BacktestReplay(new ScriptedEngine()).Run(definition, candles, settings, evaluateFromInclusive: 20);
        var late = new BacktestReplay(new ScriptedEngine()).Run(definition, candles, settings with { ExecutionDelayBars = 1 }, evaluateFromInclusive: 20);

        now.Trades.Should().ContainSingle();
        late.Trades.Should().ContainSingle();
        now.Trades[0].OpenedAt.Should().Be(candles[25].OpenTime);
        late.Trades[0].OpenedAt.Should().Be(candles[26].OpenTime);
        late.Trades[0].ClosedAt.Should().Be(now.Trades[0].ClosedAt.AddHours(1));
        late.Trades[0].EntryPrice.Should().BeGreaterThan(now.Trades[0].EntryPrice);
    }

    private sealed class ScriptedEngine : IStrategyEngine
    {
        public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason)
        {
            reason = "scripted";
            var count = context.ClosedCandles.Count;
            if (!context.HasOpenPosition && count == 25)
            {
                return SignalType.Buy;
            }

            if (context.HasOpenPosition && count == 31)
            {
                return SignalType.Exit;
            }

            return context.HasOpenPosition ? SignalType.Hold : SignalType.NoAction;
        }
    }

    private static ExperimentRecord Record(string strategy, string param, double sharpe)
    {
        var parts = param.Split('=');
        return new ExperimentRecord(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UnixEpoch,
            strategy,
            strategy,
            "h",
            new Dictionary<string, string> { [parts[0]] = parts[1] },
            "hash",
            "CONSERVATIVE",
            "Validation",
            sharpe,
            100,
            1.1m,
            40,
            10m,
            ResearchStatuses.Weak);
    }

    private static List<MarketCandle> Bars(IReadOnlyList<decimal> closes)
    {
        var t0 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return closes.Select((close, i) =>
        {
            var open = i == 0 ? close : closes[i - 1];
            return new MarketCandle
            {
                Open = open,
                High = Math.Max(open, close) + 0.1m,
                Low = Math.Min(open, close) - 0.1m,
                Close = close,
                Volume = 10m,
                IsClosed = true,
                OpenTime = t0.AddHours(i),
                CloseTime = t0.AddHours(i + 1),
                ExchangeTimestamp = t0.AddHours(i + 1)
            };
        }).ToList();
    }
}
