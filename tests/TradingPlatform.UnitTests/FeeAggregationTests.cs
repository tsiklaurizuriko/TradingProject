using FluentAssertions;
using TradingPlatform.Binance;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class FeeAggregationTests
{
    private static readonly DateTimeOffset Opened = DateTimeOffset.Parse("2026-09-21T00:00:00Z");

    [Fact]
    public void Known_amount_and_asset_are_summed_in_that_asset()
    {
        var trip = Close(
            Fill(OrderSide.Buy, 0.01m, "USDT"),
            Fill(OrderSide.Sell, 0.02m, "USDT", realized: 1m));

        trip.Fee.Status.Should().Be(FeeKnowledge.Known);
        trip.Fee.Amount.Should().Be(0.03m);
        trip.Fee.Asset.Should().Be("USDT");
        trip.Fee.DisplayAmount.Should().Be(0.03m);
    }

    [Fact]
    public void Known_amount_without_an_asset_is_not_stored_as_usdt_or_zero()
    {
        var trip = Close(
            Fill(OrderSide.Buy, 0.01m, null),
            Fill(OrderSide.Sell, 0.02m, null, realized: 1m));

        trip.Fee.Status.Should().Be(FeeKnowledge.AssetMissing);
        trip.Fee.Amount.Should().Be(0.03m);
        trip.Fee.Asset.Should().BeNull();
        trip.Fee.DisplayAmount.Should().BeNull();
    }

    [Fact]
    public void Missing_amount_stays_unknown_and_is_not_a_certified_zero()
    {
        var trip = Close(
            Fill(OrderSide.Buy, null, null),
            Fill(OrderSide.Sell, null, null, realized: 1m));

        trip.Fee.Status.Should().Be(FeeKnowledge.Unknown);
        trip.Fee.Amount.Should().BeNull();
        trip.Fee.DisplayAmount.Should().BeNull();
        trip.Fee.Should().NotBe(FeeBook.Known(0m, "USDT"));
    }

    [Fact]
    public void Known_zero_stays_distinct_from_unknown()
    {
        var trip = Close(
            Fill(OrderSide.Buy, 0m, "USDT"),
            Fill(OrderSide.Sell, 0m, "USDT", realized: 1m));

        trip.Fee.Status.Should().Be(FeeKnowledge.Known);
        trip.Fee.Amount.Should().Be(0m);
        trip.Fee.Asset.Should().Be("USDT");
        trip.Fee.DisplayAmount.Should().Be(0m);
    }

    [Fact]
    public void Different_assets_in_one_trade_are_not_added()
    {
        var trip = Close(
            Fill(OrderSide.Buy, 0.01m, "USDT"),
            Fill(OrderSide.Sell, 0.0001m, "BNB", realized: 1m));

        trip.Fee.Status.Should().Be(FeeKnowledge.Uncertain);
        trip.Fee.Amount.Should().BeNull();
        trip.Fee.DisplayAmount.Should().BeNull();
    }

    [Fact]
    public void Late_commission_replaces_unknown_without_leaving_a_fake_zero()
    {
        var trade = new Trade { Fees = 0m };
        var unknown = Close(Fill(OrderSide.Buy, null, null), Fill(OrderSide.Sell, null, null, realized: 1m));
        TradeFee.Apply(trade, unknown.Fee, replace: true);
        trade.FeeStatus.Should().Be(FeeKnowledge.Unknown);
        trade.Fees.Should().Be(0m);

        var known = Close(Fill(OrderSide.Buy, 0.01m, "USDT"), Fill(OrderSide.Sell, 0.02m, "USDT", realized: 1m));
        TradeFee.Apply(trade, known.Fee, replace: true);
        trade.FeeStatus.Should().Be(FeeKnowledge.Known);
        trade.Fees.Should().Be(0.03m);
        trade.FeeAsset.Should().Be("USDT");
    }

    [Fact]
    public void Replaying_the_same_cumulative_commission_does_not_book_it_again()
    {
        var first = FillAccounting.Apply(
            new BookedFill(0m, null, 0m),
            1m,
            new ExchangeFillReport(OrderStatus.Filled, 1m, 100m, 0.2m, "ex-1", null, FeeAsset: "USDT"));
        first.NewFill!.Fee.Should().Be(0.2m);
        first.NewFill.FeeKnown.Should().BeTrue();

        var replay = FillAccounting.Apply(
            new BookedFill(1m, 100m, 0.2m, "USDT"),
            1m,
            new ExchangeFillReport(OrderStatus.Filled, 1m, 100m, 0.2m, "ex-1", null, FeeAsset: "USDT"));
        replay.NewFill.Should().BeNull();
        replay.AdditionalFee.Should().Be(0m);
        replay.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void Recomputing_the_same_fills_is_idempotent()
    {
        var fills = new[]
        {
            Fill(OrderSide.Buy, 0.01m, "USDT"),
            Fill(OrderSide.Sell, 0.02m, "USDT", realized: 1m),
        };
        var once = BinanceClosedFill.RoundTrips("BTCUSDT", fills);
        var twice = BinanceClosedFill.RoundTrips("BTCUSDT", fills);
        twice.Should().BeEquivalentTo(once);

        var trade = new Trade();
        TradeFee.Apply(trade, once[0].Fee, replace: true);
        TradeFee.Apply(trade, twice[0].Fee, replace: true);
        trade.Fees.Should().Be(0.03m);
        trade.FeeStatus.Should().Be(FeeKnowledge.Known);
    }

    [Fact]
    public void A_stored_row_without_a_status_is_not_turned_into_a_known_zero()
    {
        ((int)FeeKnowledge.Unknown).Should().Be(0);
        var legacyZero = new Trade { Fees = 0m };
        var legacyAmount = new Trade { Fees = 1.25m };
        legacyZero.FeeStatus.Should().Be(FeeKnowledge.Unknown);
        legacyAmount.FeeStatus.Should().Be(FeeKnowledge.Unknown);

        var zero = FeeBook.FromStored(legacyZero.FeeStatus, legacyZero.Fees, legacyZero.FeeAsset);
        var amount = FeeBook.FromStored(legacyAmount.FeeStatus, legacyAmount.Fees, legacyAmount.FeeAsset);
        zero.Status.Should().Be(FeeKnowledge.Unknown);
        zero.DisplayAmount.Should().BeNull();
        amount.Status.Should().Be(FeeKnowledge.Unknown);
        amount.DisplayAmount.Should().BeNull();
        amount.Should().NotBe(FeeBook.Known(1.25m, "USDT"));
        zero.Should().NotBe(FeeBook.Known(0m, "USDT"));
    }

    private static BinanceClosedFill.ClosedIsolated Close(params BinanceClosedFill.Fill[] fills) =>
        BinanceClosedFill.RoundTrips("BTCUSDT", fills).Should().ContainSingle().Subject;

    private static BinanceClosedFill.Fill Fill(OrderSide side, decimal? fee, string? asset, decimal realized = 0m) =>
        new(side, 100m, 1m, fee, side == OrderSide.Buy ? Opened : Opened.AddMinutes(5), side == OrderSide.Buy ? "1" : "2", realized, asset);
}
