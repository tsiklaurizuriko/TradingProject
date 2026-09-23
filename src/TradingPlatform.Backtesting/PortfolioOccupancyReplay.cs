using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Risk;

namespace TradingPlatform.Backtesting;

public sealed record OccupancyIntent(
    DateTimeOffset SignalCloseTime,
    DateTimeOffset FillTime,
    string StrategyKey,
    string Symbol,
    SignalType Signal,
    decimal FillPrice);

public sealed record OccupancyBar(
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    string Symbol,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close);

public sealed record OccupancyReject(
    DateTimeOffset Time,
    string StrategyKey,
    string Symbol,
    string Reason);

public sealed record OccupancyReplayResult(
    decimal InitialBalance,
    decimal FinalEquity,
    decimal NetProfit,
    decimal MaximumDrawdownPercent,
    IReadOnlyList<ReplayTrade> Trades,
    IReadOnlyList<EquityPoint> Equity,
    IReadOnlyList<OccupancyReject> Rejects,
    int SameCoinRejects,
    int SlotRejects,
    int HeatRejects);

/// <summary>
/// Chronological account replay across strategies/coins. Sizes and rejects via <see cref="RiskEngine"/>.
/// One Isolated coin globally. Per-strategy slot cap from the LOW book (max unique coins).
/// Sequential only — no Parallel.ForEach on orders.
/// </summary>
public static class PortfolioOccupancyReplay
{
    public static OccupancyReplayResult Run(
        IReadOnlyList<OccupancyIntent> intents,
        IReadOnlyList<OccupancyBar> bars,
        ReplaySettings settings)
    {
        var engine = new RiskEngine();
        var profile = Profile(settings);
        var equity = Math.Max(settings.InitialBalance, 1m);
        var peak = equity;
        var maxDd = 0m;
        var open = new Dictionary<string, OpenSlot>(StringComparer.OrdinalIgnoreCase);
        var trades = new List<ReplayTrade>();
        var rejects = new List<OccupancyReject>();
        var equityCurve = new List<EquityPoint>();
        var sameCoin = 0;
        var slots = 0;
        var heat = 0;
        var orderedBars = bars.OrderBy(b => b.OpenTime).ThenBy(b => b.Symbol, StringComparer.OrdinalIgnoreCase).ToList();
        var pending = intents
            .Where(x => x.Signal is SignalType.Buy or SignalType.Sell)
            .OrderBy(x => x.FillTime)
            .ThenBy(x => x.StrategyKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var p = 0;

        foreach (var bar in orderedBars)
        {
            while (p < pending.Count && pending[p].FillTime <= bar.OpenTime)
            {
                var intent = pending[p++];
                TryEnter(intent, engine, profile, settings, open, rejects, ref equity, ref sameCoin, ref slots, ref heat);
            }

            if (open.TryGetValue(bar.Symbol, out var slot))
            {
                slot.BarsHeld++;
                var closed = TryExit(slot, bar, settings, trades, ref equity);
                if (closed)
                {
                    open.Remove(bar.Symbol);
                }
            }

            var mark = equity;
            foreach (var live in open.Values)
            {
                var px = string.Equals(live.Symbol, bar.Symbol, StringComparison.OrdinalIgnoreCase) ? bar.Close : live.LastMark;
                if (string.Equals(live.Symbol, bar.Symbol, StringComparison.OrdinalIgnoreCase))
                {
                    live.LastMark = bar.Close;
                }

                mark += Direction(live.Side) * (px - live.EntryPrice) * live.Quantity;
            }

            if (mark > peak)
            {
                peak = mark;
            }

            if (peak > 0m)
            {
                var dd = (peak - mark) / peak * 100m;
                if (dd > maxDd)
                {
                    maxDd = dd;
                }
            }

            equityCurve.Add(new EquityPoint(bar.CloseTime.ToUnixTimeSeconds(), Round(mark)));
        }

        while (p < pending.Count)
        {
            TryEnter(pending[p++], engine, profile, settings, open, rejects, ref equity, ref sameCoin, ref slots, ref heat);
        }

        if (orderedBars.Count > 0)
        {
            var last = orderedBars[^1];
            foreach (var leftover in open.Values.ToArray())
            {
                Close(leftover, leftover.LastMark, last.CloseTime, "End of window", settings, trades, ref equity);
            }

            open.Clear();
        }

        return new OccupancyReplayResult(
            settings.InitialBalance,
            Round(equity),
            Round(equity - settings.InitialBalance),
            Round(maxDd),
            trades,
            equityCurve,
            rejects,
            sameCoin,
            slots,
            heat);
    }

    public static OccupancyReplayResult RunBooks(
        IReadOnlyList<(string StrategyKey, string Symbol, IReadOnlyList<MarketCandle> Candles, IReadOnlyList<SignalType> Signals)> books,
        ReplaySettings settings)
    {
        var intents = new List<OccupancyIntent>();
        var bars = new List<OccupancyBar>();
        foreach (var book in books)
        {
            var candles = book.Candles;
            for (var i = 0; i < candles.Count; i++)
            {
                var c = candles[i];
                bars.Add(new OccupancyBar(c.OpenTime, c.CloseTime, book.Symbol, c.Open, c.High, c.Low, c.Close));
                if (i + 1 >= candles.Count || i >= book.Signals.Count)
                {
                    continue;
                }

                var signal = book.Signals[i];
                if (signal is not (SignalType.Buy or SignalType.Sell))
                {
                    continue;
                }

                var next = candles[i + 1];
                intents.Add(new OccupancyIntent(c.CloseTime, next.OpenTime, book.StrategyKey, book.Symbol, signal, next.Open));
            }
        }

        return Run(intents, bars, settings);
    }

    private static void TryEnter(
        OccupancyIntent intent,
        RiskEngine engine,
        RiskProfile profile,
        ReplaySettings settings,
        Dictionary<string, OpenSlot> open,
        List<OccupancyReject> rejects,
        ref decimal equity,
        ref int sameCoin,
        ref int slots,
        ref int heat)
    {
        var side = intent.Signal == SignalType.Sell ? PositionSide.Short : PositionSide.Long;
        var coinsForStrategy = open.Values.Count(x => string.Equals(x.StrategyKey, intent.StrategyKey, StringComparison.OrdinalIgnoreCase));
        var already = open.ContainsKey(intent.Symbol);
        var openRisk = open.Values
            .Where(x => string.Equals(x.StrategyKey, intent.StrategyKey, StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.RiskPercent);
        var snapshot = new RiskSnapshot
        {
            Equity = equity,
            AvailableBalance = equity,
            Symbol = intent.Symbol,
            Price = intent.FillPrice,
            Side = side,
            SymbolAlreadyOpen = already,
            OpenPositionCount = coinsForStrategy,
            OpenRiskPercent = openRisk,
            MarketDataAgeMs = 0,
            Sizing = new RiskSizingHints
            {
                TakerFeePercent = settings.FeePercent,
                SlippagePercent = settings.SlippagePercent
            }
        };
        var eval = engine.Evaluate(intent.Signal, profile, snapshot, intent.FillTime);
        if (eval.Decision != RiskDecision.Approved || eval.ApprovedQuantity <= 0m)
        {
            var reason = eval.Reason;
            if (already || reason.Contains("already has an open Isolated", StringComparison.OrdinalIgnoreCase))
            {
                sameCoin++;
                reason = "SymbolAlreadyOpen";
            }
            else if (reason.Contains("Maximum simultaneous", StringComparison.OrdinalIgnoreCase))
            {
                slots++;
                reason = "MaxSimultaneousPositions";
            }
            else if (reason.Contains("portfolio planned risk", StringComparison.OrdinalIgnoreCase))
            {
                heat++;
                reason = "MaxPortfolioRiskPercent";
            }

            rejects.Add(new OccupancyReject(intent.FillTime, intent.StrategyKey, intent.Symbol, reason));
            return;
        }

        var fill = ApplySlippage(intent.FillPrice, settings.SlippagePercent, worseForBuy: side == PositionSide.Long);
        var qty = eval.ApprovedQuantity;
        var fee = qty * fill * (settings.FeePercent / 100m);
        equity -= fee;
        var stopPct = profile.StopLossPercent / 100m;
        var tpPct = profile.TakeProfitPercent / 100m;
        var stop = side == PositionSide.Short ? fill * (1m + stopPct) : fill * (1m - stopPct);
        var take = side == PositionSide.Short ? fill * (1m - tpPct) : fill * (1m + tpPct);
        open[intent.Symbol] = new OpenSlot
        {
            StrategyKey = intent.StrategyKey,
            Symbol = intent.Symbol,
            Side = side,
            Quantity = qty,
            EntryPrice = fill,
            MidEntry = intent.FillPrice,
            EntryFee = fee,
            OpenedAt = intent.FillTime,
            Stop = stop,
            Take = take,
            RiskPercent = eval.ActualRPercent,
            LastMark = fill
        };
    }

    private static bool TryExit(OpenSlot slot, OccupancyBar bar, ReplaySettings settings, List<ReplayTrade> trades, ref decimal equity)
    {
        if (slot.Side == PositionSide.Long)
        {
            if (bar.Low <= slot.Stop)
            {
                Close(slot, ApplySlippage(slot.Stop, settings.SlippagePercent, worseForBuy: false), bar.CloseTime, "Stop loss", settings, trades, ref equity);
                return true;
            }

            if (bar.High >= slot.Take)
            {
                Close(slot, ApplySlippage(slot.Take, settings.SlippagePercent, worseForBuy: false), bar.CloseTime, "Take profit", settings, trades, ref equity);
                return true;
            }
        }
        else
        {
            if (bar.High >= slot.Stop)
            {
                Close(slot, ApplySlippage(slot.Stop, settings.SlippagePercent, worseForBuy: true), bar.CloseTime, "Stop loss", settings, trades, ref equity);
                return true;
            }

            if (bar.Low <= slot.Take)
            {
                Close(slot, ApplySlippage(slot.Take, settings.SlippagePercent, worseForBuy: true), bar.CloseTime, "Take profit", settings, trades, ref equity);
                return true;
            }
        }

        if (settings.MaxHoldBars > 0 && slot.BarsHeld >= settings.MaxHoldBars)
        {
            Close(slot, ApplySlippage(bar.Close, settings.SlippagePercent, worseForBuy: slot.Side == PositionSide.Short), bar.CloseTime, "TIME", settings, trades, ref equity);
            return true;
        }

        return false;
    }

    private static void Close(
        OpenSlot open,
        decimal exitPrice,
        DateTimeOffset at,
        string reason,
        ReplaySettings settings,
        List<ReplayTrade> trades,
        ref decimal equity)
    {
        var exitFee = open.Quantity * exitPrice * (settings.FeePercent / 100m);
        var pnl = Direction(open.Side) * (exitPrice - open.EntryPrice) * open.Quantity - open.EntryFee - exitFee;
        equity += Direction(open.Side) * (exitPrice - open.EntryPrice) * open.Quantity - exitFee;
        trades.Add(new ReplayTrade(
            open.OpenedAt,
            at,
            Round(open.Quantity),
            Round(open.EntryPrice),
            Round(exitPrice),
            Round(pnl),
            Round(open.EntryFee + exitFee),
            reason,
            open.Side == PositionSide.Short ? "Short" : "Long",
            Round(open.MidEntry),
            Round(exitPrice)));
    }

    private static RiskProfile Profile(ReplaySettings settings) => new()
    {
        Name = "OCCUPANCY",
        RiskPerTradePercent = settings.RiskPercent,
        StopLossPercent = settings.StopLossPercent,
        TakeProfitPercent = settings.TakeProfitPercent,
        MaxLeverage = settings.Leverage,
        MaxDailyLossPercent = settings.MaxDailyLossPercent,
        MaxPortfolioRiskPercent = settings.MaxPortfolioRiskPercent,
        MaxSimultaneousPositions = settings.MaxSimultaneousPositions > 0 ? settings.MaxSimultaneousPositions : 5,
        MaxConsecutiveLosses = settings.MaxConsecutiveLosses,
        CooldownMinutes = settings.CooldownMinutes,
        MinimumLiquidationSafetyBufferPercent = settings.MinimumLiquidationSafetyBufferPercent
    };

    private static decimal ApplySlippage(decimal price, decimal slippagePercent, bool worseForBuy)
    {
        var factor = slippagePercent / 100m;
        return worseForBuy ? price * (1m + factor) : price * (1m - factor);
    }

    private static decimal Direction(PositionSide side) => side == PositionSide.Short ? -1m : 1m;

    private static decimal Round(decimal value) => Math.Round(value, 8, MidpointRounding.AwayFromZero);

    private sealed class OpenSlot
    {
        public required string StrategyKey { get; init; }
        public required string Symbol { get; init; }
        public required PositionSide Side { get; init; }
        public required decimal Quantity { get; init; }
        public required decimal EntryPrice { get; init; }
        public required decimal MidEntry { get; init; }
        public required decimal EntryFee { get; init; }
        public required DateTimeOffset OpenedAt { get; init; }
        public required decimal Stop { get; init; }
        public required decimal Take { get; init; }
        public required decimal RiskPercent { get; init; }
        public decimal LastMark { get; set; }
        public int BarsHeld { get; set; }
    }
}
