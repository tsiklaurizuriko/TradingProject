using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using Xunit;

namespace TradingPlatform.BacktestingTests;

public sealed class ValidationMetricsAggregationTests
{
    [Fact]
    public void Combined_profit_factor_is_sum_wins_over_abs_sum_losses_not_average_of_book_pfs()
    {
        var bookA = ValidationMetricsAggregator.FromTrades([T(100m), T(50m), T(-75m)]);
        var bookB = ValidationMetricsAggregator.FromTrades([T(10m), T(-100m)]);
        bookA.Totals!.ProfitFactor.FiniteRatio.Should().Be(2m);
        bookB.Totals!.ProfitFactor.FiniteRatio.Should().Be(0.1m);

        var combined = ValidationMetricsAggregator.Combine(bookA, bookB);
        var pf = ValidationMetricsAggregator.ProfitFactorOf(combined);

        pf.Kind.Should().Be(ProfitFactorKind.Finite);
        pf.Ratio.Should().Be(Math.Round(160m / 175m, 8, MidpointRounding.AwayFromZero));
        pf.Ratio.Should().NotBe(1.05m);
        combined.ProfitFactor.Should().Be(pf.Ratio);
    }

    [Fact]
    public void Long_short_and_combined_use_the_same_profit_factor_definition()
    {
        var trades = new[]
        {
            T(100m, "Long"),
            T(-40m, "Long"),
            T(20m, "Short"),
            T(-80m, "Short"),
            T(0m, "Long")
        };
        var result = ValidationMetricsAggregator.FromTrades(trades);
        ValidationMetricsAggregator.ProfitFactorOf(result.Long).Ratio.Should().Be(100m / 40m);
        ValidationMetricsAggregator.ProfitFactorOf(result.Short).Ratio.Should().Be(20m / 80m);
        ValidationMetricsAggregator.ProfitFactorOf(result).Ratio.Should().Be(120m / 120m);
        result.Long.ZeroPnlTrades.Should().Be(1);
        result.NumberOfTrades.Should().Be(5);
    }

    [Fact]
    public void No_loss_profit_factor_is_infinity_not_ninety_nine()
    {
        var result = ValidationMetricsAggregator.FromTrades([T(12m), T(8m)]);
        var pf = ValidationMetricsAggregator.ProfitFactorOf(result);
        pf.Kind.Should().Be(ProfitFactorKind.NoLosses);
        pf.Render().Should().Be("Infinity");
        result.ProfitFactor.Should().Be(0m);
        pf.Ratio.Should().NotBe(99m);
    }

    [Fact]
    public void No_trade_profit_factor_is_not_available()
    {
        var result = ValidationMetricsAggregator.FromTrades([]);
        var pf = ValidationMetricsAggregator.ProfitFactorOf(result);
        pf.Kind.Should().Be(ProfitFactorKind.NoTrades);
        pf.Render().Should().Be("N/A");
    }

    [Fact]
    public void Zero_pnl_trades_are_counted_and_do_not_enter_pf_sums()
    {
        var totals = PnlTotals.FromTrades([T(50m), T(-25m), T(0m), T(0m)]);
        totals.Trades.Should().Be(4);
        totals.ZeroPnlTrades.Should().Be(2);
        totals.PositivePnlSum.Should().Be(50m);
        totals.AbsoluteNegativePnlSum.Should().Be(25m);
        totals.ProfitFactor.Ratio.Should().Be(2m);
    }

    [Fact]
    public void Strip_keeps_exact_totals_so_merge_does_not_use_rounded_summaries()
    {
        var bookA = ValidationMetricsAggregator.Strip(ValidationMetricsAggregator.FromTrades([T(100m), T(50m), T(-75m)]));
        var bookB = ValidationMetricsAggregator.Strip(ValidationMetricsAggregator.FromTrades([T(10m), T(-100m)]));
        bookA.Trades.Should().BeEmpty();
        var combined = ValidationMetricsAggregator.Combine(bookA, bookB);
        ValidationMetricsAggregator.ProfitFactorOf(combined).Ratio.Should().Be(Math.Round(160m / 175m, 8, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Equal_book_return_uses_all_included_books_not_one_initial_balance()
    {
        var bookA = ValidationMetricsAggregator.FromTrades([T(75m)]);
        var bookB = ValidationMetricsAggregator.FromTrades([T(-90m)]);
        var combined = ValidationMetricsAggregator.Combine(bookA, bookB);
        var stats = EqualBookAggregation.Returns(combined.BookReturns!);
        stats.Books.Should().Be(2);
        stats.EqualBookReturn.Should().Be(-15m / 20_000m);
        combined.ReturnPercent.Should().Be(Math.Round(-15m / 20_000m * 100m, 8, MidpointRounding.AwayFromZero));
        combined.ReturnPercent.Should().NotBe(-0.15m);
        stats.PositiveBookPercent.Should().Be(50m);
    }

    [Fact]
    public void Normalized_equal_weight_drawdown_is_not_max_of_per_book_drawdown()
    {
        IReadOnlyList<decimal> bookA = [1.0m, 1.2m, 0.8m];
        IReadOnlyList<decimal> bookB = [1.0m, 0.9m, 0.9m];
        var perBookMax = Math.Max((1.2m - 0.8m) / 1.2m * 100m, (1.0m - 0.9m) / 1.0m * 100m);
        var portfolio = EqualBookAggregation.EqualWeightNormalizedDrawdown([bookA, bookB]);
        portfolio.Should().NotBeNull();
        portfolio.Should().Be((1.05m - 0.85m) / 1.05m * 100m);
        portfolio.Should().NotBe(perBookMax);
        EqualBookAggregation.EqualWeightNormalizedDrawdown([bookA, [1.0m, 0.9m]]).Should().BeNull();
    }

    [Fact]
    public void Walk_forward_empty_windows_are_na_and_do_not_dominate_median()
    {
        var empty = Slice("empty", ValidationMetricsAggregator.FromTrades([]));
        var strong = Slice("strong", ValidationMetricsAggregator.FromTrades([T(100m), T(-50m)]));
        var weak = Slice("weak", ValidationMetricsAggregator.FromTrades([T(10m), T(-100m)]));
        var stats = WalkForwardAggregation.From([empty, strong, weak, empty]);
        stats.TotalWindows.Should().Be(4);
        stats.WindowsWithTrades.Should().Be(2);
        stats.WindowsWithoutTrades.Should().Be(2);
        stats.MedianProfitFactor.Kind.Should().Be(ProfitFactorKind.Finite);
        stats.MedianProfitFactor.Ratio.Should().Be(1.05m);
        stats.WorstProfitFactor.Ratio.Should().Be(0.1m);
        stats.BestProfitFactor.Ratio.Should().Be(2m);
        stats.MedianProfitFactor.Ratio.Should().NotBe(0m);
    }

    [Fact]
    public void Fees_are_included_exactly_once_inside_net()
    {
        var trade = T(9.8m, fees: 0.2m);
        var totals = PnlTotals.FromTrades([trade]);
        totals.NetPnl.Should().Be(9.8m);
        totals.Fees.Should().Be(0.2m);
        totals.GrossAfterSlippageBeforeFees.Should().Be(10.0m);
        var merged = ValidationMetricsAggregator.Combine(
            ValidationMetricsAggregator.FromTrades([trade]),
            ValidationMetricsAggregator.FromTrades([]));
        merged.NetProfit.Should().Be(9.8m);
        merged.FeesPaid.Should().Be(0.2m);
    }

    [Fact]
    public void Slippage_is_in_fill_prices_and_is_not_subtracted_again()
    {
        const decimal qty = 1m;
        const decimal slippedEntry = 100.02m;
        const decimal slippedExit = 109.978m;
        const decimal entryFee = 0.04m;
        const decimal exitFee = 0.04m;
        var pnl = (slippedExit - slippedEntry) * qty - entryFee - exitFee;
        var doubleCountedSlippage = pnl - (slippedEntry * 0.0002m) - (slippedExit * 0.0002m);
        pnl.Should().NotBe(doubleCountedSlippage);
        var totals = PnlTotals.FromTrades([T(pnl, fees: entryFee + exitFee)]);
        totals.NetPnl.Should().Be(pnl);
        totals.Fees.Should().Be(entryFee + exitFee);
        totals.GrossAfterSlippageBeforeFees.Should().Be(pnl + entryFee + exitFee);
    }

    [Fact]
    public void Long_plus_short_across_books_stays_trade_weighted()
    {
        var left = ValidationMetricsAggregator.FromTrades([T(100m, "Long"), T(-75m, "Long")]);
        var right = ValidationMetricsAggregator.FromTrades([T(10m, "Short"), T(-100m, "Short")]);
        var combined = ValidationMetricsAggregator.Combine(left, right);
        ValidationMetricsAggregator.ProfitFactorOf(combined.Long).Ratio.Should().Be(Math.Round(100m / 75m, 8, MidpointRounding.AwayFromZero));
        ValidationMetricsAggregator.ProfitFactorOf(combined.Short).Ratio.Should().Be(0.1m);
        ValidationMetricsAggregator.ProfitFactorOf(combined).Ratio.Should().Be(Math.Round(110m / 175m, 8, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void No_ninety_nine_sentinel_contaminates_merge_or_walk_forward()
    {
        var noLoss = ValidationMetricsAggregator.FromTrades([T(40m)]);
        var finite = ValidationMetricsAggregator.FromTrades([T(100m), T(-50m)]);
        var merged = ValidationMetricsAggregator.Combine(noLoss, finite);
        var pf = ValidationMetricsAggregator.ProfitFactorOf(merged);
        pf.Kind.Should().Be(ProfitFactorKind.Finite);
        pf.Ratio.Should().Be(140m / 50m);
        pf.Ratio.Should().NotBe(99m);
        merged.ProfitFactor.Should().NotBe(99m);

        var walk = WalkForwardAggregation.From(
        [
            Slice("empty", ValidationMetricsAggregator.FromTrades([])),
            Slice("noloss", noLoss),
            Slice("finite", finite)
        ]);
        walk.NoLossesWindows.Should().Be(1);
        walk.WindowsWithoutTrades.Should().Be(1);
        walk.BestProfitFactor.Kind.Should().Be(ProfitFactorKind.NoLosses);
        walk.BestProfitFactor.Render().Should().Be("Infinity");
        walk.MedianProfitFactor.Ratio.Should().NotBe(99m);
        walk.WorstProfitFactor.Ratio.Should().NotBe(99m);
    }

    [Fact]
    public void In_sample_validation_and_oos_share_the_combine_operator()
    {
        var a = ValidationMetricsAggregator.FromTrades([T(100m), T(-50m)]);
        var b = ValidationMetricsAggregator.FromTrades([T(10m), T(-100m)]);
        var isMerge = ValidationMetricsAggregator.Combine(a, b);
        var valMerge = ValidationMetricsAggregator.Combine(a, b);
        var oosMerge = ValidationMetricsAggregator.Combine(a, b);
        isMerge.ProfitFactor.Should().Be(valMerge.ProfitFactor).And.Be(oosMerge.ProfitFactor);
        isMerge.SharpeRatio.Should().BeNull();
    }

    [Fact]
    public void Per_book_drawdown_distribution_is_reported_instead_of_portfolio_label()
    {
        var low = ValidationMetricsAggregator.FromTrades([T(10m), T(-5m)]);
        var high = ValidationMetricsAggregator.FromTrades([T(50m), T(-80m), T(-80m)]);
        var combined = ValidationMetricsAggregator.Combine(low, high);
        var stats = EqualBookAggregation.Drawdowns(combined.BookDrawdowns!);
        stats.Books.Should().Be(2);
        stats.Maximum.Should().Be(combined.BookDrawdowns!.Max());
        stats.Mean.Should().Be(combined.BookDrawdowns!.Average());
        combined.SharpeRatio.Should().BeNull();
    }

    private static ValidationSlice Slice(string label, ReplayResult result) =>
        new(label, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), 10, result);

    private static ReplayTrade T(decimal pnl, string side = "Long", decimal fees = 0m) =>
        new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5), 1m, 100m, 100m, pnl, fees, "fixture", side);
}
