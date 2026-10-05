using FluentAssertions;
using TradingPlatform.Binance;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

/// <summary>Hand-computed expectations; none of these numbers come from the code under test.</summary>
public sealed class TradePnlTests
{
    [Fact]
    public void Long_gross_and_net_match_hand_math()
    {
        // 2 @ 100 -> 110: gross 20. Taker 0.04%: entry 0.08, exit 0.088. Net 19.832.
        var gross = PositionFillBook.Exit(PositionSide.Long, 2m, 100m, 2m, 110m, 0m).RealizedPnl;
        var fees = FeeBook.Combine(FeeBook.Known(0.08m, "USDT"), FeeBook.Known(0.088m, "usdt"));

        gross.Should().Be(20m);
        TradePnl.Net(gross, fees, null).Should().Be(19.832m);
    }

    [Fact]
    public void Short_gross_and_net_match_hand_math_including_funding()
    {
        // Short 1 @ 100 -> 90: gross 10. Fees 0.04 + 0.036. Funding paid 0.01. Net 9.914.
        var gross = PositionFillBook.Exit(PositionSide.Short, 1m, 100m, 1m, 90m, 0m).RealizedPnl;
        var fees = FeeBook.Combine(FeeBook.Known(0.04m, "USDT"), FeeBook.Known(0.036m, "USDT"));

        gross.Should().Be(10m);
        TradePnl.Net(gross, fees, -0.01m).Should().Be(9.914m);
    }

    [Fact]
    public void Losing_short_is_negative_gross()
    {
        PositionFillBook.Exit(PositionSide.Short, 1m, 100m, 1m, 104m, 0m).RealizedPnl.Should().Be(-4m);
    }

    [Fact]
    public void Partial_exits_add_gross_and_fees_leg_by_leg()
    {
        var trade = new Trade { PnL = 0m, FeeStatus = FeeKnowledge.Known, Fees = 0.04m, FeeAsset = "USDT" };
        var first = PositionFillBook.Exit(PositionSide.Long, 1m, 100m, 0.4m, 105m, 0m);
        trade.PnL += first.RealizedPnl;
        TradeFee.Apply(trade, FeeBook.Known(0.0168m, "USDT"), replace: false);
        var second = PositionFillBook.Exit(PositionSide.Long, first.RemainingQuantity, 100m, 0.6m, 110m, 0m);
        trade.PnL += second.RealizedPnl;
        TradeFee.Apply(trade, FeeBook.Known(0.0264m, "USDT"), replace: false);

        first.Closed.Should().BeFalse();
        second.Closed.Should().BeTrue();
        trade.PnL.Should().Be(8m, "0.4 x 5 + 0.6 x 10");
        trade.Fees.Should().Be(0.0832m);
        trade.NetPnL.Should().Be(7.9168m);
    }

    [Fact]
    public void Multiple_executions_in_one_asset_sum()
    {
        var fee = FeeBook.Combine([FeeBook.Known(0.01m, "USDT"), FeeBook.Known(0.02m, "USDT"), FeeBook.Known(0.03m, "USDT")]);
        TradePnl.Net(1m, fee, null).Should().Be(0.94m);
    }

    [Fact]
    public void Mixed_fee_assets_are_uncertain_not_summed()
    {
        var fee = FeeBook.Combine(FeeBook.Known(0.01m, "USDT"), FeeBook.Known(0.00002m, "BNB"));
        fee.Status.Should().Be(FeeKnowledge.Uncertain);
        TradePnl.Net(1m, fee, null).Should().BeNull();
    }

    [Fact]
    public void Late_fee_merged_onto_an_unknown_leg_stays_pending_until_a_full_resync()
    {
        var trade = new Trade { PnL = 5m };
        TradeFee.Apply(trade, FeeBook.Unknown(), replace: false);
        trade.NetPnL.Should().BeNull();

        TradeFee.Apply(trade, FeeBook.Known(0.1m, "USDT"), replace: false);
        trade.NetPnL.Should().BeNull("one leg is still unknown, so the merged fee is uncertain");

        TradeFee.Apply(trade, FeeBook.Known(0.25m, "USDT"), replace: true);
        trade.NetPnL.Should().Be(4.75m, "the Binance trip resync replaces the fee for the whole trip");
    }

    [Fact]
    public void Duplicate_resync_of_the_same_trip_does_not_double_the_fee()
    {
        var trade = new Trade { PnL = 5m };
        TradeFee.Apply(trade, FeeBook.Known(0.25m, "USDT"), replace: true);
        TradeFee.Apply(trade, FeeBook.Known(0.25m, "USDT"), replace: true);

        trade.Fees.Should().Be(0.25m);
        trade.NetPnL.Should().Be(4.75m);
    }

    [Fact]
    public void Resync_that_loses_the_fee_marks_it_uncertain_instead_of_zero()
    {
        var trade = new Trade { PnL = 5m };
        TradeFee.Apply(trade, FeeBook.Known(0.25m, "USDT"), replace: true);
        TradeFee.Apply(trade, FeeBook.Unknown(), replace: true);

        trade.FeeStatus.Should().Be(FeeKnowledge.Uncertain);
        trade.NetPnL.Should().BeNull();
    }

    [Fact]
    public void A_known_zero_fee_in_any_asset_gives_net_equal_to_gross()
    {
        TradePnl.Net(3m, FeeBook.Known(0m, "BNB"), null).Should().Be(3m);
        TradePnl.Net(3m, FeeBook.Known(0m, "USDT"), 0.5m).Should().Be(3.5m);
    }

    [Theory]
    [InlineData(FeeKnowledge.Unknown, "not reported")]
    [InlineData(FeeKnowledge.AssetMissing, "asset is missing")]
    [InlineData(FeeKnowledge.Uncertain, "uncertain")]
    public void Every_pending_net_has_a_reason(FeeKnowledge status, string fragment)
    {
        var fee = status switch
        {
            FeeKnowledge.AssetMissing => FeeBook.AssetMissing(0.1m),
            FeeKnowledge.Uncertain => FeeBook.Uncertain(),
            _ => FeeBook.Unknown()
        };

        TradePnl.Net(1m, fee, null).Should().BeNull();
        TradePnl.PendingReason(fee).Should().Contain(fragment);
    }

    [Fact]
    public void Bnb_fee_reason_names_the_asset()
    {
        var fee = FeeBook.Known(0.0004m, "BNB");
        TradePnl.Net(1m, fee, null).Should().BeNull();
        TradePnl.PendingReason(fee).Should().Contain("BNB");
        TradePnl.PendingReason(FeeBook.Known(0.1m, "USDT")).Should().BeNull();
    }

    [Fact]
    public void Totals_report_known_net_and_pending_count_without_guessing()
    {
        var totals = TradePnl.Totals([(10m, 9.9m, 0m), (-4m, -4.05m, null), (2m, null, -0.01m)]);

        totals.Gross.Should().Be(8m);
        totals.KnownNet.Should().Be(5.85m);
        totals.Net.Should().BeNull("one trade is pending fees");
        totals.Pending.Should().Be(1);
        totals.Funding.Should().Be(-0.01m);
        totals.FundingMissing.Should().Be(1);

        TradePnl.Totals([(1m, 0.9m, null)]).Net.Should().Be(0.9m);
        TradePnl.Totals([]).Net.Should().BeNull();
    }

    [Fact]
    public void Funding_is_attributed_to_the_trip_window_by_coin()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var opened = now.AddHours(-20);
        var ledger = FundingLedger.From(
        [
            ("BTCUSDT", -0.5m, opened),
            ("BTCUSDT", -0.2m, opened.AddHours(4)),
            ("BTCUSDT", 0.1m, opened.AddHours(12)),
            ("ETHUSDT", -9m, opened.AddHours(4)),
            ("BTCUSDT", -7m, now.AddHours(-1))
        ], now, 1000);

        ledger.For("BTCUSDT", opened, opened.AddHours(12)).Should().Be(-0.1m, "a settlement at the open instant belongs to the earlier holder; one at the close belongs to this trip");
        ledger.For("ethusdt", opened, opened.AddHours(12)).Should().Be(-9m);
        ledger.For("SOLUSDT", opened, opened.AddHours(12)).Should().Be(0m, "the ledger covers this window and had no SOL funding");
    }

    [Fact]
    public void Funding_for_a_trip_older_than_the_income_window_is_not_recorded()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var ledger = FundingLedger.From([("BTCUSDT", -0.2m, now.AddDays(-2))], now, 1000);

        ledger.For("BTCUSDT", now.AddDays(-9), now.AddDays(-1)).Should().BeNull();
    }

    [Fact]
    public void A_truncated_funding_page_narrows_the_covered_window()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var rows = Enumerable.Range(0, 3).Select(i => ("BTCUSDT", -0.1m, now.AddHours(-i))).ToList();
        var ledger = FundingLedger.From(rows, now, rowLimit: 3);

        ledger.CoveredFrom.Should().Be(now.AddHours(-2));
        ledger.For("BTCUSDT", now.AddHours(-5), now).Should().BeNull();
    }
}
