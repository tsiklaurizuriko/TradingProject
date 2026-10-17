using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TradingPlatform.Application.Trading;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class ProductionNewStrategyTests
{
    [Fact]
    public void Registry_ids_end_with_new_and_stay_out_of_the_operator_catalog()
    {
        StrategyTemplateKeys.ProductionNew.Should().HaveCount(6);
        StrategyTemplateKeys.ProductionNew.Should().OnlyContain(id => id.EndsWith("-new", StringComparison.Ordinal));
        foreach (var id in StrategyTemplateKeys.ProductionNew)
        {
            StrategyTemplateKeys.IsKnown(id).Should().BeTrue();
            StrategyTemplateKeys.IsOperatorCatalog(id).Should().BeFalse();
            StrategyTemplates.DisplayName(id).Should().EndWith("-new");
            StrategyTemplates.ResearchStatus(id).Should().Be("INSUFFICIENT_EVIDENCE");
            var detail = ProductionNewEvaluator.Evaluate(
                StrategyTemplates.DefaultsFor(id, false),
                Bars(5, 100m),
                4,
                Flat(),
                new CausalIndicatorCache(Bars(5, 100m)));
            detail.Signal.Should().Be(SignalType.NoAction);
            detail.Reason.Should().Contain("NO_TRADE");
        }
    }

    [Fact]
    public void Ema_matches_sma_seed_and_alpha()
    {
        var candles = Closes(1m, 2m, 3m, 4m);
        var ema = new EmaIndicator(3).Compute(candles);
        ema[2].Should().Be(2m);
        ema[3].Should().Be(3m);
    }

    [Fact]
    public void Incremental_ema_matches_batch()
    {
        var full = Closes(Enumerable.Range(1, 30).Select(i => (decimal)i).ToArray());
        var batch = new EmaIndicator(5).Compute(full);
        var prefix = new EmaIndicator(5).Compute(full.Take(29).ToList());
        var alpha = 2m / 6m;
        var stepped = prefix[28]!.Value * (1m - alpha) + full[29].Close * alpha;
        stepped.Should().Be(batch[29]!.Value);
    }

    [Fact]
    public void Atr_is_zero_on_a_flat_market_and_rsi_is_100_on_a_rise()
    {
        var flat = Bars(20, 10m);
        new AtrIndicator(14).Compute(flat)[14].Should().Be(0m);
        var rising = Closes(Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray());
        new RsiIndicator(14).Compute(rising)[14].Should().Be(100m);
    }

    [Fact]
    public void Bollinger_uses_population_standard_deviation()
    {
        var values = Enumerable.Range(0, 20).Select(i => 10m + i).ToArray();
        var bands = BollingerSeries.Compute(Closes(values), 20, 2m);
        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / 20m;
        var sd = (decimal)Math.Sqrt((double)variance);
        bands.Mid[19].Should().Be(mean);
        bands.Upper[19].Should().Be(mean + 2m * sd);
        bands.Lower[19].Should().Be(mean - 2m * sd);
    }

    [Fact]
    public void Breakout_lookback_excludes_the_current_candle()
    {
        var candles = Bars(21, 10m);
        candles[^1] = Candle(20, 50m, 50m, 50m, 50m, 1m);
        ProductionNewMath.PriorHigh(candles, 20, 20).Should().Be(10m);
    }

    [Fact]
    public void Relative_volume_is_null_when_the_average_is_zero()
    {
        var candles = Bars(20, 10m, volume: 0m);
        ProductionNewMath.RelativeVolume(candles, 19, 20).Should().BeNull();
    }

    [Fact]
    public void Basis_z_score_rejects_a_zero_sigma()
    {
        var series = Enumerable.Repeat((decimal?)0.01m, 30).ToArray();
        ProductionNewMath.PopulationZScore(series, 29, 30).Should().BeNull();
    }

    [Fact]
    public void Trend_following_emits_long_and_short_and_respects_the_adx_boundary()
    {
        var up = Drift(260, 100m, 0.4m, up: true);
        var parameters = Loose(StrategyTemplateKeys.TrendFollowingNew) with { EmaFast = 50, EmaSlow = 200 };
        var buy = Eval(parameters, up);
        buy.Signal.Should().Be(SignalType.Buy);
        buy.SuggestedStop.Should().BeLessThan(up[^1].Close);
        buy.Reason.Should().Contain("trend-following-new");

        var down = Geometric(260, 200m, 0.997m);
        var sell = Eval(parameters, down);
        sell.Signal.Should().Be(SignalType.Sell);
        sell.SuggestedStop.Should().BeGreaterThan(down[^1].Close);

        var tooStrict = Eval(parameters with { MinimumAdx = 100m }, up);
        tooStrict.Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Unclosed_candle_and_short_history_do_not_signal()
    {
        var candles = Drift(260, 100m, 0.4m, up: true);
        candles[^1].IsClosed = false;
        Eval(Loose(StrategyTemplateKeys.TrendFollowingNew) with { EmaFast = 50, EmaSlow = 200 }, candles)
            .Reason.Should().Contain("unclosed");
        Eval(Loose(StrategyTemplateKeys.TrendFollowingNew), Bars(8, 100m)).Reason.Should().Contain("NO_TRADE");
    }

    [Fact]
    public void Open_trend_position_exits_on_the_opposite_ema_order_and_does_not_flip()
    {
        var up = Drift(260, 100m, 0.4m, up: true);
        var parameters = Loose(StrategyTemplateKeys.TrendFollowingNew) with { EmaFast = 50, EmaSlow = 200 };
        var context = new StrategyContext
        {
            ClosedCandles = up,
            CurrentPrice = up[^1].Close,
            HasOpenPosition = true,
            PositionSide = PositionSide.Short,
            AverageEntryPrice = up[^1].Close + 5m,
            ProtectiveStopPrice = up[^1].Close + 8m
        };
        var detail = ProductionNewEvaluator.Evaluate(parameters, up, up.Count - 1, context, new CausalIndicatorCache(up));
        detail.Signal.Should().Be(SignalType.Exit);
        detail.Reason.Should().Contain("not reversed");
    }

    [Fact]
    public void Breakout_rejects_a_chase_and_a_low_volume_print()
    {
        var candles = RangeThenBreak(close: 130m, volume: 10_000m);
        var chased = Eval(Loose(StrategyTemplateKeys.BreakoutVolumeNew) with { PriceDisplacementAtr = 0.1m }, candles);
        chased.Signal.Should().Be(SignalType.NoAction);

        var quiet = RangeThenBreak(close: 101m, volume: 1m);
        Eval(Loose(StrategyTemplateKeys.BreakoutVolumeNew), quiet).Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Market_regime_never_buys_or_sells()
    {
        var candles = Drift(220, 100m, 0.3m, up: true);
        var detail = Eval(Loose(StrategyTemplateKeys.MarketRegimeNew), candles);
        detail.Signal.Should().Be(SignalType.NoAction);
        detail.Reason.Should().Contain("market-regime-new");
        detail.Reason.Should().Contain("filter");
        detail.Signal.Should().NotBe(SignalType.Buy);
        detail.Signal.Should().NotBe(SignalType.Sell);
    }

    [Fact]
    public void Missing_benchmark_is_uncertain_when_the_filter_is_required()
    {
        var candles = Drift(220, 100m, 0.3m, up: true);
        var regime = MarketRegimeClassifier.Confirmed(
            candles,
            candles.Count - 1,
            new CausalIndicatorCache(candles),
            Loose(StrategyTemplateKeys.MarketRegimeNew) with { RequireBenchmark = true },
            Flat());
        regime.Should().Be(MarketRegimeStates.Uncertain);
    }

    [Fact]
    public void Momentum_ranks_only_the_supplied_universe_and_refuses_a_missing_one()
    {
        var alone = Eval(Loose(StrategyTemplateKeys.MomentumRelativeStrengthNew), Drift(80, 100m, 0.2m, up: true));
        alone.Reason.Should().Contain("peer universe");

        var decision = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(400);
        var peers = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAAUSDT"] = PathEnding(decision, 100m, 130m),
            ["BBBUSDT"] = PathEnding(decision, 100m, 80m),
            ["CCCUSDT"] = PathEnding(decision, 100m, 101m)
        };
        var candles = peers["AAAUSDT"].ToList();
        var context = new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            Symbol = "AAAUSDT",
            PeerCandles = peers
        };
        var detail = ProductionNewEvaluator.Evaluate(
            Loose(StrategyTemplateKeys.MomentumRelativeStrengthNew),
            candles,
            candles.Count - 1,
            context,
            new CausalIndicatorCache(candles));
        detail.Signal.Should().BeOneOf(SignalType.Buy, SignalType.NoAction);
        if (detail.Signal == SignalType.NoAction)
        {
            detail.Reason.Should().Contain("NO_TRADE");
        }
    }

    [Fact]
    public void Mean_reversion_does_not_buy_just_because_rsi_is_low_in_a_trend()
    {
        var down = Drift(220, 200m, 0.5m, up: false);
        var detail = Eval(Loose(StrategyTemplateKeys.MeanReversionRsiBollingerNew) with { MinimumAdx = 5m }, down);
        detail.Signal.Should().NotBe(SignalType.Buy);
    }

    [Fact]
    public void Funding_without_data_is_no_trade_and_hedged_mode_is_not_substituted()
    {
        var candles = Drift(80, 100m, 0.1m, up: true);
        Eval(Loose(StrategyTemplateKeys.FundingBasisNew), candles).Reason.Should().Contain("missing");
        var hedged = Eval(Loose(StrategyTemplateKeys.FundingBasisNew) with { HedgedMode = true }, candles);
        hedged.Reason.Should().Contain("hedged");
        hedged.Signal.Should().Be(SignalType.NoAction);
    }

    [Fact]
    public void Funding_can_propose_a_short_only_when_rate_basis_and_costs_agree()
    {
        var candles = Drift(80, 100m, 0.05m, up: true);
        var funding = Enumerable.Repeat((decimal?)0.0001m, candles.Count).ToArray();
        var basis = Enumerable.Repeat((decimal?)0.0001m, candles.Count).ToArray();
        funding[^1] = 0.02m;
        basis[^1] = 0.02m;
        var context = new StrategyContext
        {
            ClosedCandles = candles,
            CurrentPrice = candles[^1].Close,
            Symbol = "BTCUSDT",
            FundingRate = funding,
            NormalizedBasis = basis
        };
        var detail = ProductionNewEvaluator.Evaluate(
            Loose(StrategyTemplateKeys.FundingBasisNew) with { FundingLookback = 20, FundingZEntry = 1m, BasisZEntry = 1m },
            candles,
            candles.Count - 1,
            context,
            new CausalIndicatorCache(candles));
        detail.Signal.Should().BeOneOf(SignalType.Sell, SignalType.NoAction);
        detail.Reason.Should().NotContain("risk-free");
    }

    [Fact]
    public void Coordinator_rejects_duplicates_opposites_and_the_regime_module()
    {
        var now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var first = new ProductionNewIntent("trend-following-new", "BTCUSDT", now, "LONG", "a", "long");
        var duplicate = first with { StrategyId = "breakout-volume-new" };
        var regime = new ProductionNewIntent("market-regime-new", "ETHUSDT", now, "LONG", "b", "filter");
        var decisions = ProductionNewCoordinator.Select([duplicate, first, regime], new Dictionary<string, string>());
        decisions.Count(row => row.Accepted).Should().Be(1);
        decisions.Single(row => row.Intent.StrategyId == "market-regime-new").Accepted.Should().BeFalse();

        var opposite = ProductionNewCoordinator.Select(
            [first],
            new Dictionary<string, string> { ["BTCUSDT"] = "SHORT" });
        opposite[0].Accepted.Should().BeFalse();
        opposite[0].Reason.Should().Contain("not silently reversed");
    }

    [Fact]
    public void Correlation_fallback_rejects_a_short_sample()
    {
        var reason = ProductionNewCoordinator.CorrelationBlock([("ETHUSDT", [0.1m, 0.2m])], [0.1m, 0.2m]);
        reason.Should().Contain("NO_TRADE");
    }

    [Fact]
    public void Cost_aware_quantity_floors_and_never_rounds_up_through_the_minimum()
    {
        var quantity = CostAwareQuantity.Solve(10_000m, 0.0035m, 100m, 0.02m, 0.0004m, 0.0005m, 0m, 0.1m, 0.1m, 5m, 3);
        quantity.Should().NotBeNull();
        (quantity!.Value % 0.1m).Should().Be(0m);
        var tooSmall = CostAwareQuantity.Solve(10m, 0.0025m, 100m, 0.02m, 0.0004m, 0.0005m, 0m, 0.001m, 1m, 100m, 3);
        tooSmall.Should().BeNull();
    }

    [Fact]
    public void Risk_engine_rejects_cost_aware_size_that_cannot_be_estimated_and_halts_on_the_daily_loss()
    {
        var profile = new RiskProfile
        {
            RiskPerTradePercent = 0.35m,
            StopLossPercent = 2m,
            TakeProfitPercent = 4m,
            MaxLeverage = 2m,
            MaxDailyLossPercent = 2m,
            MaxPortfolioRiskPercent = 1.5m,
            MinimumLiquidationSafetyBufferPercent = 1m
        };
        var denied = RiskEngine.Plan(profile, 20m, 100m, PositionSide.Long, 0m, new RiskSizingHints
        {
            CostAware = true,
            StepSize = 0.001m,
            MinQuantity = 1m,
            MinNotional = 100m
        });
        denied.Allowed.Should().BeFalse();

        var halt = new RiskEngine().Evaluate(
            SignalType.Buy,
            profile,
            new RiskSnapshot
            {
                Equity = 1_000m,
                AvailableBalance = 1_000m,
                Price = 100m,
                AccountDailyPnL = -30m,
                Side = PositionSide.Long
            },
            DateTimeOffset.Parse("2024-06-01T12:00:00Z"));
        halt.Decision.Should().Be(RiskDecision.Rejected);
        halt.Reason.Should().Contain("Daily loss");
    }

    [Fact]
    public void Gate_blocks_live_and_paper_until_both_switches_are_on()
    {
        var options = new ProductionNewOptions();
        ProductionNewGate.EntryBlock(options, "trend-following-new", liveMode: true).Should().Contain("disabled");
        options.Strategies["trend-following-new"] = true;
        ProductionNewGate.EntryBlock(options, "trend-following-new", liveMode: true).Should().Contain("LiveEnabled");
        ProductionNewGate.EntryBlock(options, "trend-following-new", liveMode: false).Should().Contain("PaperEnabled");
        options.LiveEnabled = true;
        ProductionNewGate.EntryBlock(options, "trend-following-new", liveMode: true).Should().BeNull();
        ProductionNewGate.EntryBlock(options, "ema_rsi_trend", liveMode: true).Should().BeNull();
    }

    [Fact]
    public void Signal_identity_is_stable_and_a_repeated_evaluation_does_not_change()
    {
        var candles = Drift(260, 100m, 0.4m, up: true);
        var parameters = Loose(StrategyTemplateKeys.TrendFollowingNew) with { EmaFast = 50, EmaSlow = 200 };
        var left = Eval(parameters, candles);
        var right = Eval(parameters, candles);
        left.Signal.Should().Be(right.Signal);
        left.Reason.Should().Be(right.Reason);
        var id = ProductionNewMath.SignalId("trend-following-new", "BTCUSDT", candles[^1].CloseTime, "LONG");
        id.Should().Be(ProductionNewMath.SignalId("trend-following-new", "BTCUSDT", candles[^1].CloseTime, "LONG"));
        id.Should().StartWith("trend-following-new|");
    }

    [Fact]
    public void Synthetic_backtest_is_not_evidence_and_uses_the_next_bar()
    {
        var candles = Drift(180, 100m, 0.2m, up: true);
        var report = ProductionNewBacktest.Run("trend-following-new", candles, historicalUntouchedSample: false);
        report.Verdict.Should().Be(ProductionNewAcceptance.InsufficientEvidence);
        var replay = new BacktestReplay(new StrategyEngine());
        var result = replay.Run(
            StrategyValidation.Definition("trend-following-new", "1h"),
            candles,
            ProductionNewBacktest.Settings());
        result.Assumptions.Should().Contain("next bar open");
        result.FeesPaid.Should().BeGreaterThanOrEqualTo(0m);
        foreach (var trade in result.Trades)
        {
            trade.Fees.Should().BeGreaterThan(0m);
            trade.OpenedAt.Should().BeAfter(candles[0].OpenTime);
        }
    }

    [Fact]
    public void Prefix_evaluation_matches_the_full_series_at_the_same_index()
    {
        var candles = Drift(240, 100m, 0.15m, up: true);
        var engine = new StrategyEngine();
        var definition = StrategyValidation.Definition(StrategyTemplateKeys.TrendFollowingNew, "1h");
        var cache = new CausalIndicatorCache(candles);
        const int index = 220;
        var prefix = candles.Take(index + 1).ToList();
        var fromPrefix = engine.Evaluate(
            definition,
            new StrategyContext { ClosedCandles = prefix, CurrentPrice = prefix[^1].Close },
            out _);
        var fromFull = engine.EvaluateAt(
            definition,
            new StrategyContext { ClosedCandles = candles, CurrentPrice = candles[index].Close },
            cache,
            index,
            out _);
        fromFull.Should().Be(fromPrefix);
    }

    [Fact]
    public void Momentum_rank_ignores_a_peer_candle_after_the_decision()
    {
        var decision = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(400);
        var peers = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAAUSDT"] = PathEnding(decision, 100m, 130m),
            ["BBBUSDT"] = PathEnding(decision, 100m, 80m)
        };
        var parameters = Loose(StrategyTemplateKeys.MomentumRelativeStrengthNew);
        var frame = TimeSpan.FromHours(1);
        var before = ProductionNewEvaluator.RankUniverse(peers, decision, frame, parameters);
        var leaked = peers.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<MarketCandle>)pair.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        var extra = leaked["BBBUSDT"].ToList();
        extra.Add(Candle(500, 1_000m, 1_000m, 1_000m, 1_000m, 1m));
        extra[^1].OpenTime = decision.AddHours(4);
        extra[^1].CloseTime = decision.AddHours(5);
        leaked["BBBUSDT"] = extra;
        var after = ProductionNewEvaluator.RankUniverse(leaked, decision, frame, parameters);
        after.Select(row => (row.Symbol, row.Score)).Should().Equal(before.Select(row => (row.Symbol, row.Score)));
    }

    [Fact]
    public void Portfolio_overlay_rejects_a_second_coin_and_a_daily_loss()
    {
        var t0 = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var first = new ProductionNewBookTrade("trend-following-new", "BTCUSDT", "Long", t0, t0.AddHours(2), -50m, -40m, 10m, 10m, 0m, 10_000m);
        var sameCoin = new ProductionNewBookTrade("breakout-volume-new", "BTCUSDT", "Long", t0.AddHours(1), t0.AddHours(3), 40m, 50m, 5m, 5m, 0m, 10_000m);
        var blocked = ProductionNewPortfolio.Simulate([first, sameCoin], dailyLossPercent: 2m);
        blocked.Accepted.Should().Be(1);
        blocked.Rejected.Should().Be(1);
        blocked.RejectReasons.Should().ContainKey("symbol-open");

        var loser = new ProductionNewBookTrade("trend-following-new", "BTCUSDT", "Long", t0, t0.AddHours(1), -300m, -280m, 10m, 10m, 0m, 10_000m);
        var nextDay = new ProductionNewBookTrade("trend-following-new", "ETHUSDT", "Short", t0.AddHours(3), t0.AddHours(4), 25m, 30m, 2m, 2m, 1m, 10_000m);
        var halted = ProductionNewPortfolio.Simulate([loser, nextDay], dailyLossPercent: 2m);
        halted.RejectReasons.Should().ContainKey("daily-loss");
        halted.Label.Should().Be(ProductionNewPortfolio.DiagnosticLabel);
    }

    [Fact]
    public void Strategy_verdict_is_not_validated_from_one_symbol_or_a_losing_in_sample()
    {
        var thin = new ProductionNewEvidence(1, 200, 100, 100, 10m, 1.4m, true, 10m, 100, 10m, 8, 8, 8, true);
        ProductionNewVerdict.Judge(thin).Should().Be(ProductionNewVerdict.InsufficientEvidence);
        var unstable = thin with { SymbolsWithOutOfSampleTrades = 10, InSampleNet = -1m };
        ProductionNewVerdict.Judge(unstable).Should().Be(ProductionNewVerdict.Rejected);
        var blocked = thin with { RequiredDataPresent = false };
        ProductionNewVerdict.Judge(blocked).Should().Be(ProductionNewVerdict.DataBlocked);
        ProductionNewVerdict.FormatProfitFactor(3, 0, 0m).Should().Be("undefined_no_losing_trades");
    }

    [Fact]
    public void Host_configuration_keeps_the_six_strategies_off_even_when_the_process_live_flag_is_on()
    {
        var root = RepoRoot();
        foreach (var (folder, overlay) in new (string Folder, string? Overlay)[]
        {
            ("src/TradingPlatform.Api", null),
            ("src/TradingPlatform.Api", "appsettings.Live.json"),
            ("src/TradingPlatform.Api", "appsettings.Shadow.json"),
            ("src/TradingPlatform.Workers", null),
            ("src/TradingPlatform.Workers", "appsettings.Live.json")
        })
        {
            var builder = new ConfigurationBuilder().AddJsonFile(Path.Combine(root, folder, "appsettings.json"));
            if (overlay is not null)
            {
                builder.AddJsonFile(Path.Combine(root, folder, overlay));
            }

            var config = builder.Build();
            config.GetValue<bool>("Trading:ProductionNew:LiveEnabled").Should().BeFalse(folder + " " + overlay);
            config.GetValue<bool>("Trading:ProductionNew:PaperEnabled").Should().BeFalse(folder + " " + overlay);
            foreach (var id in StrategyTemplateKeys.ProductionNew)
            {
                var flag = config.GetValue<bool?>($"Trading:ProductionNew:Strategies:{id}");
                (flag is null or false).Should().BeTrue(id);
            }
        }

        var live = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src/TradingPlatform.Api/appsettings.json"))
            .AddJsonFile(Path.Combine(root, "src/TradingPlatform.Api/appsettings.Live.json"))
            .Build();
        live.GetValue<bool>("Trading:LiveTradingEnabled").Should().BeTrue();
        ProductionNewGate.EntryBlock(live.GetSection("Trading:ProductionNew").Get<ProductionNewOptions>(), "trend-following-new", liveMode: true)
            .Should().Contain("disabled");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TradingPlatform.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("TradingPlatform.slnx was not found above the test output.");
    }

    private static StrategySignalDetail Eval(StrategyTemplateParams parameters, List<MarketCandle> candles) =>
        ProductionNewEvaluator.Evaluate(parameters, candles, candles.Count - 1, Flat(candles), new CausalIndicatorCache(candles));

    private static StrategyTemplateParams Loose(string key) =>
        StrategyTemplates.DefaultsFor(key, false) with
        {
            Timeframe = "1h",
            MinimumAdx = 5m,
            VolatilityLookback = 30,
            FundingExtremePercentile = 0.99m,
            SwingLength = 1
        };

    private static StrategyContext Flat(List<MarketCandle>? candles = null) => new()
    {
        ClosedCandles = candles ?? [],
        CurrentPrice = candles is { Count: > 0 } ? candles[^1].Close : 0m,
        Symbol = "BTCUSDT"
    };

    private static List<MarketCandle> Bars(int count, decimal price, decimal volume = 100m)
    {
        var candles = new List<MarketCandle>(count);
        for (var i = 0; i < count; i++)
        {
            candles.Add(Candle(i, price, price, price, price, volume));
        }

        return candles;
    }

    private static List<MarketCandle> Closes(params decimal[] closes)
    {
        var candles = new List<MarketCandle>(closes.Length);
        for (var i = 0; i < closes.Length; i++)
        {
            var price = closes[i];
            candles.Add(Candle(i, price, price + 0.1m, price - 0.1m, price, 10m));
        }

        return candles;
    }

    private static List<MarketCandle> Geometric(int count, decimal start, decimal factor)
    {
        var candles = new List<MarketCandle>(count);
        var price = start;
        for (var i = 0; i < count; i++)
        {
            var open = price;
            var close = Math.Max(0.01m, decimal.Round(price * factor, 8, MidpointRounding.AwayFromZero));
            var high = Math.Max(open, close) * 1.001m;
            var low = Math.Min(open, close) * 0.999m;
            candles.Add(Candle(i, open, high, low, close, 1000m));
            price = close;
        }

        return candles;
    }

    private static List<MarketCandle> Drift(int count, decimal start, decimal step, bool up)
    {
        var candles = new List<MarketCandle>(count);
        var price = start;
        for (var i = 0; i < count; i++)
        {
            var open = price;
            var close = up ? price + step : Math.Max(1m, price - step);
            candles.Add(Candle(i, open, Math.Max(open, close) + 0.05m, Math.Min(open, close) - 0.05m, close, 100m + i));
            price = close;
        }

        return candles;
    }

    private static List<MarketCandle> RangeThenBreak(decimal close, decimal volume)
    {
        var candles = Bars(60, 100m);
        for (var i = 0; i < candles.Count; i++)
        {
            var wobble = (i % 2 == 0) ? 0.1m : -0.1m;
            var price = 100m + wobble;
            candles[i] = Candle(i, price, price + 0.2m, price - 0.2m, price, 100m);
        }

        var last = candles.Count - 1;
        candles[last] = Candle(last, 100m, close, 99.5m, close, volume);
        return candles;
    }

    private static List<MarketCandle> PathEnding(DateTimeOffset end, decimal start, decimal finish)
    {
        var candles = new List<MarketCandle>(400);
        var price = start;
        var step = (finish - start) / 399m;
        for (var i = 0; i < 400; i++)
        {
            var close = i == 399 ? finish : price + step;
            candles.Add(new MarketCandle
            {
                Open = price,
                High = Math.Max(price, close) + 0.2m,
                Low = Math.Max(0.1m, Math.Min(price, close) - 0.2m),
                Close = close,
                Volume = 1_000m,
                IsClosed = true,
                OpenTime = end.AddHours(i - 400),
                CloseTime = end.AddHours(i - 399)
            });
            price = close;
        }

        return candles;
    }

    private static MarketCandle Candle(int index, decimal open, decimal high, decimal low, decimal close, decimal volume)
    {
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new MarketCandle
        {
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
            IsClosed = true,
            OpenTime = t0.AddHours(index),
            CloseTime = t0.AddHours(index + 1)
        };
    }
}
