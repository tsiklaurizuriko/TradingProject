using FluentAssertions;
using TradingPlatform.Backtesting;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using Xunit;

namespace TradingPlatform.ResearchTests;

public sealed class FuturesAlphaPhase4Tests
{
    [Fact]
    public void Frozen_specs_are_the_two_phase3_continuation_hypotheses()
    {
        var frozen = FuturesAlphaPhase4.FrozenSpecs();
        frozen.Should().HaveCount(4);
        frozen.Should().OnlyContain(s => s.Params.FundingHypothesis == "continuation");
        frozen.Select(s => s.FamilyId).Distinct().Should().BeEquivalentTo(
            "funding_basis_rv",
            "funding_extreme_momentum_exhaustion");
        frozen.Should().NotContain(s => s.Neighborhood);
        frozen.Should().NotContain(s => s.Params.UseFuturesFilter && s.Role == "baseline");
    }

    [Fact]
    public void Neighborhood_is_is_only_and_does_not_replace_frozen_oos_ids()
    {
        var frozen = FuturesAlphaPhase4.FrozenSpecs().Select(s => s.CandidateId).ToHashSet();
        var neighbors = FuturesAlphaPhase4.NeighborhoodSpecs();
        neighbors.Should().OnlyContain(s => s.Neighborhood);
        neighbors.Should().OnlyContain(s => !frozen.Contains(s.CandidateId));
        neighbors.Should().OnlyContain(s => s.Role == "enhanced");
    }

    [Fact]
    public void Bootstrap_is_deterministic_for_the_same_seed()
    {
        var pnls = Enumerable.Range(0, 40).Select(i => i % 3 == 0 ? -12m : 8m).ToList();
        var trades = pnls.Select((p, i) => new Phase4TradeRow(
            "funding_basis_rv|enhanced|continuation", "BTCUSDT", "1h", "OOS", ResearchCostLabels.Base,
            DateTimeOffset.UnixEpoch.AddDays(i), "Long", p, 0.1m, 0.05m, 0m, p + 0.15m, "RANGE")).ToList();
        var a = FuturesAlphaPhase4.Bootstrap(trades);
        var b = FuturesAlphaPhase4.Bootstrap(trades);
        a.Should().BeEquivalentTo(b);
        a.Should().OnlyContain(x => x.Seed == FuturesAlphaPhase4.BootstrapSeed);
        a.Should().OnlyContain(x => x.Draws == FuturesAlphaPhase4.BootstrapDraws);
    }

    [Fact]
    public void Replay_accounting_identity_holds_when_funding_is_included()
    {
        var t0 = DateTimeOffset.UnixEpoch.AddDays(20);
        var candles = Enumerable.Range(0, 80).Select(i => new MarketCandle
        {
            Open = 100m + i,
            High = 101m + i,
            Low = 99m + i,
            Close = 100.5m + i,
            Volume = 10m,
            IsClosed = true,
            OpenTime = t0.AddHours(i),
            CloseTime = t0.AddHours(i + 1),
            ExchangeTimestamp = t0.AddHours(i + 1)
        }).ToList();
        var definition = new StrategyDefinitionValidator().Parse(
            StrategyTemplates.Build("t", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.DonchianBreakout, false) with
            {
                AllowedSide = StrategySides.Both,
                Timeframe = "1h",
                DonchianLength = 10
            }));
        var settlements = new ReplayFundingSettlement[]
        {
            new(candles[40].CloseTime.AddMinutes(-10), 0.0001m),
            new(candles[10].CloseTime, 0.0001m)
        };
        var settings = new ReplaySettings(t0, t0.AddDays(10), 10_000m, 1m, 5m, 0.04m, 0.02m, 2m, 4m);
        var result = new BacktestReplay(new StrategyEngine()).Run(
            definition, candles, settings, new CausalIndicatorCache(candles), 15, 80, null, null, settlements);
        result.CostNotes.Should().Be("INCLUDING_FUNDING");
        if (result.NumberOfTrades > 0)
        {
            BacktestReplay.AccountingIdentityHolds(result).Should().BeTrue(
                $"gross={result.GrossPnl} fees={result.FeesPaid} slip={result.SlippagePaid} fund={result.FundingPaid} net={result.NetProfit}");
        }
    }
}
