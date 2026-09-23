using FluentAssertions;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Execution;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class NearMissIntegrationTests
{
    [Fact]
    public void Default_configuration_is_off()
    {
        var options = new TradingOptions();
        options.LiveTradingEnabled.Should().BeFalse();
        options.PriceAction.Enabled.Should().BeFalse();
        options.PriceAction.PaperEnabled.Should().BeFalse();
        options.PriceAction.LiveEnabled.Should().BeFalse();
        options.PriceAction.AllowLive.Should().BeFalse();
        options.Scalping.Enabled.Should().BeFalse();
        options.Scalping.AllowLive.Should().BeFalse();
        NearMissAudit.SelectedRows.Should().OnlyContain(row =>
            NearMissGate.BlockReason(options, NearMissAudit.TemplateKey(row), TradingMode.Paper) != null
            && NearMissGate.BlockReason(options, NearMissAudit.TemplateKey(row), TradingMode.Live) != null);
    }

    [Fact]
    public void Audit_selects_seven_and_leaves_the_rest()
    {
        NearMissAudit.Phase8.Should().HaveCount(25);
        NearMissAudit.SelectedRows.Select(row => row.HypothesisId).Should().Equal(
            "CPA-SWEEP|CONTEXTUAL|5m",
            "CPA-SWEEP|STRICT|5m",
            "CPA-PULLBACK|CONTEXTUAL|5m",
            "CPA-WM|CONTEXTUAL|5m",
            "CPA-WM|STRICT|5m",
            "CPA-COMPRESSION|CONTINUATION|5m",
            "CPA-MTF|STRICT|5m");
        NearMissAudit.SelectedRows.Should().OnlyContain(row => row.Phase8Status != "VALIDATED_FOR_PAPER");
        StrategyTemplateKeys.NearMiss.Should().HaveCount(7);
        StrategyTemplateKeys.NearMiss.Should().OnlyContain(key => key.Length <= 64);
    }

    [Fact]
    public void Frozen_five_scalping_and_phase7_keys_stay_separate()
    {
        StrategyTemplateKeys.Frozen.Should().Equal(
            StrategyTemplateKeys.EmaRsiTrend,
            StrategyTemplateKeys.MacdTrend,
            StrategyTemplateKeys.RsiPullback,
            StrategyTemplateKeys.BollingerReversion,
            StrategyTemplateKeys.DonchianBreakout);
        StrategyTemplateKeys.NearMiss.Should().NotIntersectWith(StrategyTemplateKeys.Frozen);
        StrategyTemplateKeys.NearMiss.Should().NotIntersectWith(StrategyTemplateKeys.Scalping);
        StrategyTemplateKeys.NearMiss.Should().NotIntersectWith(StrategyTemplateKeys.PriceAction);
        StrategyTemplateKeys.IsNearMiss(StrategyTemplateKeys.EmaRsiTrend).Should().BeFalse();
        StrategyTemplateKeys.IsPriceAction(StrategyTemplateKeys.NearMiss[0]).Should().BeFalse();
        StrategyTemplateKeys.IsScalping(StrategyTemplateKeys.NearMiss[0]).Should().BeFalse();
    }

    [Fact]
    public void Live_cannot_bypass_the_global_switch()
    {
        var options = Armed();
        options.LiveTradingEnabled = false;
        NearMissGate.BlockReason(options, StrategyTemplateKeys.NearMiss[0], TradingMode.Live)
            .Should().Contain("Global LIVE");
    }

    [Fact]
    public void Paper_stays_off_until_paper_is_armed()
    {
        var options = Armed();
        options.PriceAction.PaperEnabled = false;
        NearMissGate.BlockReason(options, StrategyTemplateKeys.NearMiss[0], TradingMode.Paper)
            .Should().Contain("paper");
    }

    [Fact]
    public void Selected_strategy_reaches_the_existing_risk_engine()
    {
        var engine = new RiskEngine();
        var profile = new RiskProfile
        {
            RiskPerTradePercent = 0.5m,
            StopLossPercent = 1.5m,
            TakeProfitPercent = 3m,
            MaxLeverage = 3m,
            MaxPortfolioRiskPercent = 4m,
            MaxSimultaneousPositions = 2
        };
        var open = engine.Evaluate(SignalType.Buy, profile, Snapshot(symbolOpen: true), DateTimeOffset.UtcNow);
        open.Decision.Should().Be(RiskDecision.Rejected);
        NearMissGate.RejectLabel(open.Reason).Should().Be("RejectedSameSymbol");

        var slot = engine.Evaluate(SignalType.Buy, profile, Snapshot() with { OpenPositionCount = 2 }, DateTimeOffset.UtcNow);
        NearMissGate.RejectLabel(slot.Reason).Should().Be("RejectedSlot");

        var heat = engine.Evaluate(SignalType.Buy, profile, Snapshot() with { OpenRiskPercent = 4m }, DateTimeOffset.UtcNow);
        NearMissGate.RejectLabel(heat.Reason).Should().Be("RejectedHeat");
    }

    [Fact]
    public void Paper_connector_is_not_the_live_connector()
    {
        var paper = new PaperExchangeConnector(new EmptyMarketDataCache(), new FixedClock(), Microsoft.Extensions.Options.Options.Create(new TradingOptions()));
        var factory = new ExchangeConnectorFactory(paper, []);
        factory.Create(TradingMode.Paper, null).Mode.Should().Be(TradingMode.Paper);
        factory.Create(TradingMode.Paper, null).Name.Should().Be("PaperSimulator");
        var live = () => factory.Create(TradingMode.Live, Guid.NewGuid());
        live.Should().Throw<TradingPlatform.Domain.Errors.DomainException>();
    }

    [Fact]
    public void Near_miss_template_does_not_fall_through_to_ema()
    {
        var definition = StrategyTemplates.Build(
            "near",
            1,
            StrategyTemplates.DefaultsFor(StrategyTemplateKeys.NearMiss[0], qualityOn: false));
        var parsed = new StrategyDefinitionValidator().Parse(definition);
        var candles = Enumerable.Range(0, 80).Select(i => new TradingPlatform.Domain.Market.MarketCandle
        {
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100m + (i % 2),
            Volume = 10m,
            IsClosed = true,
            OpenTime = DateTimeOffset.UnixEpoch.AddMinutes(i * 5),
            CloseTime = DateTimeOffset.UnixEpoch.AddMinutes((i + 1) * 5)
        }).ToList();
        var signal = new StrategyEngine().Evaluate(
            parsed,
            new StrategyContext { ClosedCandles = candles, CurrentPrice = 100m },
            out var reason);
        signal.Should().Be(SignalType.NoAction);
        reason.Should().Contain("frozen contextual book");
    }

    [Fact]
    public void Stored_arm_opens_only_the_chosen_candidate()
    {
        var options = new TradingOptions { LiveTradingEnabled = false };
        var chosen = StrategyTemplateKeys.NearMiss[0];
        var other = StrategyTemplateKeys.NearMiss[1];
        PriceActionArm.Apply(options.PriceAction, new Dictionary<string, string>
        {
            [PriceActionArm.EnabledKey] = "true",
            [PriceActionArm.PaperKey] = "true",
            [PriceActionArm.LiveKey] = "true",
            [PriceActionArm.CandidateKey(chosen)] = "true"
        });

        NearMissGate.BlockReason(options, chosen, TradingMode.Paper).Should().BeNull();
        NearMissGate.BlockReason(options, other, TradingMode.Paper).Should().NotBeNull();
        NearMissGate.BlockReason(options, chosen, TradingMode.Live).Should().Contain("Global LIVE");
        PriceActionArm.RejectLive(false, true).Should().Contain("Global LIVE");
        PriceActionArm.RejectLive(true, true).Should().BeNull();
    }

    [Fact]
    public void Missing_arm_settings_leave_defaults_off()
    {
        var options = new TradingOptions();
        PriceActionArm.Apply(options.PriceAction, new Dictionary<string, string>());
        options.PriceAction.Enabled.Should().BeFalse();
        options.PriceAction.PaperEnabled.Should().BeFalse();
        options.PriceAction.LiveEnabled.Should().BeFalse();
    }

    private static TradingOptions Armed()
    {
        var options = new TradingOptions
        {
            LiveTradingEnabled = true,
            PriceAction = new PriceActionOptions
            {
                Enabled = true,
                PaperEnabled = true,
                LiveEnabled = true
            }
        };
        foreach (var key in StrategyTemplateKeys.NearMiss)
        {
            options.PriceAction.Candidates[key] = new NearMissCandidateOptions { Enabled = true };
        }

        return options;
    }

    private static RiskSnapshot Snapshot(bool symbolOpen = false) => new()
    {
        Equity = 1000m,
        AvailableBalance = 1000m,
        Symbol = "BTCUSDT",
        Price = 100m,
        Side = PositionSide.Long,
        SymbolAlreadyOpen = symbolOpen,
        Sizing = new RiskSizingHints { ExchangeMaxLeverage = 20m, StepSize = 0.001m, MinQuantity = 0.001m, MinNotional = 5m }
    };

    private sealed class FixedClock : TradingPlatform.Application.Abstractions.IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class EmptyMarketDataCache : TradingPlatform.Application.Abstractions.MarketData.IMarketDataCache
    {
        public void SetTicker(string symbol, decimal price, DateTimeOffset timestamp) { }
        public bool TryGetTicker(string symbol, out decimal price) { price = 0m; return false; }
        public void SetKlines(string symbol, Timeframe timeframe, IReadOnlyList<TradingPlatform.Domain.Market.MarketCandle> candles) { }
        public IReadOnlyList<TradingPlatform.Domain.Market.MarketCandle> GetKlines(string symbol, Timeframe timeframe) => [];
        public IReadOnlyList<TradingPlatform.Application.Abstractions.MarketData.CachedTicker> GetTickers() => [];
    }
}
