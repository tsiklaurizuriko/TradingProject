using FluentAssertions;
using Xunit;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Trading;

namespace TradingPlatform.ResearchTests;

public class CrossSectionalReversalTests
{
    [Fact]
    public void Return15m_is_close_over_prior_close_minus_one()
    {
        CrossSectionalReversalRanker.FeatureValue(101m, 100m).Should().Be(0.01m);
        CrossSectionalReversalCatalog.LookbackBars(CrossSectionalReversalCatalog.Feature15m).Should().Be(1);
    }

    [Fact]
    public void Return1h_uses_four_bars_on_the_same_clock()
    {
        CrossSectionalReversalCatalog.LookbackBars(CrossSectionalReversalCatalog.Feature1h).Should().Be(4);
        var close = Panel(31, 6, (t, s) => 100m + s + t);
        var ranked = CrossSectionalReversalReplay.RankClock(Clock(6), Symbols(31), close, 5, CrossSectionalReversalCatalog.Feature1h, historyBars: 1);
        ranked.Should().NotBeEmpty();
        ranked.Select(row => row.Feature).Should().OnlyContain(value => value == CrossSectionalReversalCatalog.Feature1h);
    }

    [Fact]
    public void Symbol_with_fewer_than_96_own_bars_is_excluded()
    {
        var bars = Universe(30);
        bars[0] = new CrossSectionSymbolBar { Symbol = bars[0].Symbol, Close = bars[0].Close, PriorClose = bars[0].PriorClose, OwnClosedBars = 95 };
        CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature15m, bars).Should().BeEmpty();
    }

    [Fact]
    public void Missing_own_bar_excludes_the_symbol()
    {
        var bars = Universe(31);
        bars[0] = new CrossSectionSymbolBar { Symbol = bars[0].Symbol, Close = null, PriorClose = 1m, OwnClosedBars = 96 };
        var ranked = CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature15m, bars);
        ranked.Select(row => row.Symbol).Should().NotContain(bars[0].Symbol);
        ranked[0].UniverseSize.Should().Be(30);
    }

    [Fact]
    public void Fewer_than_30_names_forms_no_decile()
    {
        CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature15m, Universe(29)).Should().BeEmpty();
    }

    [Fact]
    public void Top_decile_is_short_and_bottom_decile_is_long()
    {
        var ranked = CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature15m, Universe(30));
        ranked.Where(row => row.Decile == "top").Should().OnlyContain(row => row.Direction == "SHORT");
        ranked.Where(row => row.Decile == "bottom").Should().OnlyContain(row => row.Direction == "LONG");
        ranked.Count(row => row.Decile == "top").Should().Be(3);
        ranked.Count(row => row.Decile == "bottom").Should().Be(3);
        ranked.Where(row => row.Direction == "SHORT").Select(row => row.FeatureValue).Min()
            .Should().BeGreaterThan(ranked.Where(row => row.Direction == "LONG").Select(row => row.FeatureValue).Max());
    }

    [Fact]
    public void Ties_break_by_symbol_name()
    {
        var bars = Universe(30);
        for (var i = 0; i < bars.Count; i++)
        {
            bars[i] = new CrossSectionSymbolBar
            {
                Symbol = $"N{i:00}USDT",
                Close = 100m,
                PriorClose = 100m,
                OwnClosedBars = 96
            };
        }

        var ranked = CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature15m, bars);
        ranked.Where(row => row.Decile == "bottom").Select(row => row.Symbol).Should().Equal("N00USDT", "N01USDT", "N02USDT");
        ranked.Where(row => row.Decile == "top").Select(row => row.Symbol).Should().Equal("N27USDT", "N28USDT", "N29USDT");
    }

    [Fact]
    public void Later_close_does_not_change_the_rank_at_t()
    {
        var symbols = Symbols(30);
        var close = Panel(30, 98, (t, s) => 100m + s + (t * 0.01m));
        var before = CrossSectionalReversalReplay.RankClock(Clock(98), symbols, close, 96, CrossSectionalReversalCatalog.Feature15m);
        close[97, 0] = 10_000m;
        var after = CrossSectionalReversalReplay.RankClock(Clock(98), symbols, close, 96, CrossSectionalReversalCatalog.Feature15m);
        after.Select(row => (row.Symbol, row.Direction, row.FeatureValue)).Should().Equal(before.Select(row => (row.Symbol, row.Direction, row.FeatureValue)));
    }

    [Fact]
    public void Ranking_uses_the_supplied_btc_clock_only()
    {
        var bars = Universe(30);
        bars.Add(new CrossSectionSymbolBar
        {
            Symbol = "OFFUSDT",
            Close = 1m,
            PriorClose = 100m,
            OwnClosedBars = 200,
            OnClock = false
        });
        var ranked = CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature15m, bars);
        ranked.Select(row => row.Symbol).Should().NotContain("OFFUSDT");
        ranked[0].RankingTimestamp.Should().Be(Stamp);
    }

    [Fact]
    public void Universe_membership_is_the_symbols_present_at_that_timestamp()
    {
        var symbols = Symbols(31);
        var close = Panel(31, 97, (t, s) => s == 0 && t == 96 ? null : 100m + s);
        var ranked = CrossSectionalReversalReplay.RankClock(Clock(97), symbols, close, 96, CrossSectionalReversalCatalog.Feature15m);
        ranked.Select(row => row.Symbol).Should().NotContain(symbols[0]);
        ranked[0].UniverseSize.Should().Be(30);
    }

    [Fact]
    public void Repeated_ranking_keeps_the_same_order()
    {
        var bars = Universe(40);
        var first = CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature1h, bars);
        var second = CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature1h, bars);
        second.Select(row => row.Symbol + row.Direction).Should().Equal(first.Select(row => row.Symbol + row.Direction));
    }

    [Fact]
    public void Occupied_symbol_is_not_approved_for_a_second_position()
    {
        var ranked = CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature15m, Universe(30));
        var occupied = ranked[0];
        var book = new List<Position>
        {
            new() { Symbol = occupied.Symbol, Quantity = 1m, Side = PositionSide.Long }
        };
        var review = CrossSectionalReversalAdmission.Review(ranked, book, Profile(), 1000m, 0m, Stamp);
        review.Single(row => row.Symbol == occupied.Symbol).Occupied.Should().BeTrue();
        review.Should().OnlyContain(row => row.ApprovedForOrder == false);
    }

    [Fact]
    public void Aggregate_risk_still_rejects_when_the_book_is_full()
    {
        var ranked = CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature15m, Universe(30));
        var profile = Profile();
        profile.MaxSimultaneousPositions = 2;
        var review = CrossSectionalReversalAdmission.Review(
            ranked,
            [new Position { Symbol = "AAAAUSDT", Quantity = 1m }, new Position { Symbol = "BBBBUSDT", Quantity = 1m }],
            profile,
            1000m,
            0m,
            Stamp);
        review.Should().OnlyContain(row => row.RiskRejected && row.ApprovedForOrder == false);
    }

    [Fact]
    public void Isolated_margin_formula_is_unchanged()
    {
        PortfolioRisk.IsolatedMargin(900m, 3m).Should().Be(300m);
        var plan = RiskEngine.Plan(Profile(), 1000m, 100m, PositionSide.Long);
        plan.IsolatedMargin.Should().Be(plan.PositionNotional / plan.Leverage);
        plan.Allowed.Should().BeTrue();
        var tight = Profile();
        tight.StopLossPercent = 40m;
        tight.TakeProfitPercent = 50m;
        tight.MaxLeverage = 3m;
        RiskEngine.Plan(tight, 1000m, 100m, PositionSide.Long).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Live_and_paper_stay_blocked_and_validation_stays_none()
    {
        var options = new TradingPlatform.Application.Trading.TradingOptions { LiveTradingEnabled = false };
        var paper = CrossSectionalReversalGate.BlockOrders(options, StrategyTemplateKeys.CrossSectionalReversalReturn15m, TradingMode.Paper);
        var live = CrossSectionalReversalGate.BlockOrders(options, StrategyTemplateKeys.CrossSectionalReversalReturn15m, TradingMode.Live);
        paper.Should().BeNull();
        live.Should().BeNull();
        CrossSectionalReversalGate.Describe(options).ValidationStatus.Should().Be("NONE");
        CrossSectionalReversalGate.Describe(options).PaperStatus.Should().Be("OFF");
        CrossSectionalReversalGate.Describe(options).LiveStatus.Should().Be("OFF");
        CrossSectionalReversalGate.Describe(options).Enabled.Should().BeFalse();
        var act = () => RiskLiveGuard.EnsureAllowed(TradingMode.Live, new RiskProfile { Name = "HIGH", AllowLive = true });
        act.Should().NotThrow();
    }

    [Fact]
    public void Frozen_five_keys_are_unchanged_and_single_symbol_preview_does_not_order()
    {
        StrategyTemplateKeys.Frozen.Should().Equal(
            "ema_rsi_trend",
            "macd_trend",
            "rsi_pullback",
            "bollinger_reversion",
            "donchian_breakout");
        StrategyTemplateKeys.IsFrozen(StrategyTemplateKeys.CrossSectionalReversalReturn15m).Should().BeFalse();
        StrategyTemplateKeys.IsOperatorCatalog(StrategyTemplateKeys.CrossSectionalReversalReturn1h).Should().BeFalse();
        var candles = new List<MarketCandle>
        {
            Bar(0, 100m),
            Bar(1, 101m)
        };
        var signal = new StrategyEngine().Evaluate(
            new StrategyDefinition { Template = StrategyTemplateKeys.CrossSectionalReversalReturn15m, Timeframe = "15m" },
            new StrategyContext { ClosedCandles = candles, CurrentPrice = 101m },
            out var reason);
        signal.Should().Be(SignalType.NoAction);
        reason.Should().Contain("contemporaneous universe");
        var frozen = new StrategyEngine().Evaluate(
            new StrategyDefinition { Template = StrategyTemplateKeys.EmaRsiTrend, Timeframe = "15m" },
            new StrategyContext { ClosedCandles = candles, CurrentPrice = 101m },
            out var frozenReason);
        frozenReason.Should().NotContain("contemporaneous universe");
        frozen.Should().BeOneOf(SignalType.NoAction, SignalType.Hold, SignalType.Buy, SignalType.Sell);
    }

    [Fact]
    public void Equal_risk_does_not_grow_when_fewer_slots_fill_and_live_stays_blocked()
    {
        var options = new TradingPlatform.Application.Trading.CrossSectionalReversalOptions
        {
            MaxLongPositions = 2,
            MaxShortPositions = 5,
            MaxTotalPositions = 10,
            MaxPerPositionRiskPercent = 0.25m,
            MaxCrossSectionalRiskPercent = 2m,
            MaxDirectionalRiskPercent = 1.5m,
            MaxHeat = 1m,
            MaxClusterPositions = 1,
            MinimumEligibleSymbols = 30
        };
        var ranked = CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature15m, Universe(30));
        var plan = CrossSectionalRiskPolicy.Select(
            ranked,
            options,
            occupiedSymbols: new HashSet<string>(StringComparer.Ordinal) { ranked.First(row => row.Direction == "LONG").Symbol },
            clusters: ranked.Where(row => row.Direction == "SHORT").Select(row => new CrossSectionCluster(row.Symbol, "ALT")).ToList());
        plan.Accepted.Should().OnlyContain(row => row.PlannedRiskPercent == 0.25m);
        plan.Rejected.Should().Contain(row => row.Reason == CrossSectionalRiskPolicy.SameSymbolOccupied);
        plan.Rejected.Should().Contain(row => row.Reason == CrossSectionalRiskPolicy.ClusterLimit);
        var trading = new TradingPlatform.Application.Trading.TradingOptions { LiveTradingEnabled = false };
        CrossSectionalRiskPolicy.LiveActivationBlock(trading, true, true, true, false, false, false, true).Should().Contain("LIVE = OFF");
        trading.LiveTradingEnabled = true;
        trading.CrossSectionalReversal.Enabled = true;
        trading.CrossSectionalReversal.LiveEnabled = true;
        trading.CrossSectionalReversal.Return15mEnabled = true;
        trading.CrossSectionalReversal.ProductionApproval = "INSUFFICIENT_EVIDENCE";
        CrossSectionalRiskPolicy.LiveActivationBlock(trading, true, true, true, false, false, false, true).Should().BeNull();
        CrossSectionalReversalGate.BlockOrders(trading, StrategyTemplateKeys.CrossSectionalReversalReturn15m, TradingMode.Live).Should().BeNull();
        CrossSectionalReversalGate.BlockOrders(trading, StrategyTemplateKeys.EmaRsiTrend, TradingMode.Live).Should().BeNull();
        trading.LiveTradingEnabled = false;
        CrossSectionalReversalGate.BlockOrders(trading, StrategyTemplateKeys.CrossSectionalReversalReturn15m, TradingMode.Live).Should().BeNull();
    }

    [Fact]
    public void Own_risk_copy_does_not_change_the_shared_book_and_rank_uses_the_btc_clock()
    {
        var source = Profile();
        var copy = CrossSectionalRiskBook.Overlay(source, new TradingPlatform.Application.Trading.CrossSectionalReversalOptions
        {
            MaxPerPositionRiskPercent = 0.25m,
            MaxCrossSectionalRiskPercent = 2m,
            MaxLeverage = 3,
            MaxTotalPositions = 10
        });
        source.RiskPerTradePercent.Should().Be(0.5m);
        source.MaxSimultaneousPositions.Should().Be(2);
        copy.RiskPerTradePercent.Should().Be(0.25m);
        copy.StopLossPercent.Should().Be(2m);
        copy.TakeProfitPercent.Should().Be(4m);
        copy.MaxLeverage.Should().Be(3m);
        copy.MaxPortfolioRiskPercent.Should().Be(2m);
        copy.MaxSimultaneousPositions.Should().Be(10);

        var options = new TradingPlatform.Application.Trading.TradingOptions();
        options.CrossSectionalReversal.Enabled = true;
        options.CrossSectionalReversal.Return15mEnabled = true;
        options.CrossSectionalReversal.HistoryBarsRequired = 2;
        options.CrossSectionalReversal.MinimumEligibleSymbols = 30;
        var universe = ClockUniverse(30);
        var bottom = CrossSectionalLiveBook.Decide(
            StrategyTemplateKeys.CrossSectionalReversalReturn15m,
            "S00USDT",
            options,
            universe,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            alreadyOpen: false);
        bottom.Signal.Should().Be(SignalType.Buy);
        var occupied = CrossSectionalLiveBook.Decide(
            StrategyTemplateKeys.CrossSectionalReversalReturn15m,
            "S00USDT",
            options,
            universe,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "S00USDT" },
            alreadyOpen: false);
        occupied.Reason.Should().Be(CrossSectionalRiskPolicy.SameSymbolOccupied);
        occupied.Signal.Should().Be(SignalType.NoAction);
        var missing = CrossSectionalLiveBook.Decide(
            StrategyTemplateKeys.CrossSectionalReversalReturn15m,
            "S00USDT",
            options,
            universe.Where(row => !string.Equals(row.Symbol, "BTCUSDT", StringComparison.Ordinal)).ToList(),
            new HashSet<string>(),
            alreadyOpen: false);
        missing.Reason.Should().Contain("INSUFFICIENT_DATA");
    }

    [Fact]
    public void Portfolio_profit_factor_uses_total_pnl_and_costs_subtract()
    {
        CrossSectionalRobustness.ProfitFactor(10m, 0m).Should().Be("NO_LOSSES");
        CrossSectionalRobustness.ProfitFactor(0m, 0m).Should().Be("NO_TRADES");
        CrossSectionalRobustness.ProfitFactor(8m, -4m).Should().Be("2");
        var cost = CrossSectionalRobustness.ApplyCosts(0.01m, 0.0008m, 0.0004m, 0.0001m, 1m);
        cost.Net.Should().Be(cost.Gross - cost.Fees - cost.Slippage - cost.Funding);
        CrossSectionalRobustness.LabelOf(10, 2, 3).Should().Be("INSUFFICIENT_DATA");
        CrossSectionalRobustness.LabelOf(40, 3, 3).Should().Be("CONSISTENT");
        CrossSectionalRobustness.BreakEvenRoundTrip(-0.01m, 1m).Should().Be(0m);
    }

    [Fact]
    public void Model_b_round_trip_matches_the_research_cost()
    {
        CrossSectionalReversalCatalog.RoundTripPerLeg.Should().Be(0.0012m);
        var symbols = Symbols(30);
        var close = Panel(30, 8, (t, s) => 100m + (s * 0.1m) + t);
        var measured = CrossSectionalReversalReplay.MeasureResearchBook(symbols, close, CrossSectionalReversalCatalog.Feature15m, 1);
        measured.Holds.Should().BeGreaterThan(0);
        measured.Net.Should().Be(measured.Gross - (measured.Turnover * 2m * CrossSectionalReversalCatalog.RoundTripPerLeg));
    }

    [Fact]
    public void Stablecoin_and_index_contracts_are_excluded()
    {
        var bars = Universe(30);
        bars.Add(new CrossSectionSymbolBar { Symbol = "BTCDOMUSDT", Close = 1m, PriorClose = 2m, OwnClosedBars = 200 });
        bars.Add(new CrossSectionSymbolBar { Symbol = "USDCUSDT", Close = 1m, PriorClose = 2m, OwnClosedBars = 200 });
        var ranked = CrossSectionalReversalRanker.Rank(Stamp, CrossSectionalReversalCatalog.Feature15m, bars);
        ranked.Select(row => row.Symbol).Should().NotContain(["BTCDOMUSDT", "USDCUSDT"]);
    }

    private static readonly DateTimeOffset Stamp = new(2024, 9, 18, 0, 0, 0, TimeSpan.Zero);

    private static List<CrossSectionSymbolBar> Universe(int count)
    {
        var rows = new List<CrossSectionSymbolBar>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new CrossSectionSymbolBar
            {
                Symbol = $"S{i:00}USDT",
                Close = 100m + i,
                PriorClose = 100m,
                OwnClosedBars = 96
            });
        }

        return rows;
    }

    private static string[] Symbols(int count) => Enumerable.Range(0, count).Select(i => $"S{i:00}USDT").ToArray();

    private static DateTimeOffset[] Clock(int count) =>
        Enumerable.Range(0, count).Select(i => Stamp.AddMinutes(i * 15)).ToArray();

    private static decimal?[,] Panel(int symbols, int times, Func<int, int, decimal?> price)
    {
        var close = new decimal?[times, symbols];
        for (var t = 0; t < times; t++)
        {
            for (var s = 0; s < symbols; s++)
            {
                close[t, s] = price(t, s);
            }
        }

        return close;
    }

    private static List<(string Symbol, IReadOnlyList<MarketCandle> Candles)> ClockUniverse(int count)
    {
        var rows = new List<(string Symbol, IReadOnlyList<MarketCandle> Candles)>();
        var btc = new List<MarketCandle>();
        for (var i = 0; i < 3; i++)
        {
            btc.Add(Bar(i, 100m));
        }

        rows.Add(("BTCUSDT", btc));
        for (var s = 0; s < count; s++)
        {
            var series = new List<MarketCandle>
            {
                Bar(0, 100m),
                Bar(1, 100m),
                Bar(2, 100m + s)
            };
            rows.Add(($"S{s:00}USDT", series));
        }

        return rows;
    }

    private static MarketCandle Bar(int index, decimal close) => new()
    {
        OpenTime = Stamp.AddMinutes(index * 15),
        CloseTime = Stamp.AddMinutes((index + 1) * 15),
        Open = close,
        High = close,
        Low = close,
        Close = close,
        Volume = 1m,
        IsClosed = true
    };

    private static RiskProfile Profile() => new()
    {
        Name = "LOW",
        RiskPerTradePercent = 0.5m,
        StopLossPercent = 2m,
        TakeProfitPercent = 4m,
        MaxLeverage = 3m,
        MaxPortfolioRiskPercent = 4m,
        MaxSimultaneousPositions = 2,
        MaxConsecutiveLosses = 5,
        CooldownMinutes = 30,
        MinimumLiquidationSafetyBufferPercent = 1m,
        AllowLive = false
    };
}
