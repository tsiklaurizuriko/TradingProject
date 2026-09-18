using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Backtesting;

public sealed record ReplaySettings(
    DateTimeOffset Start,
    DateTimeOffset End,
    decimal InitialBalance,
    decimal RiskPercent,
    decimal Leverage,
    decimal FeePercent,
    decimal SlippagePercent,
    decimal StopLossPercent,
    decimal TakeProfitPercent,
    decimal MaxDailyLossPercent = 100m,
    decimal MaxPortfolioRiskPercent = 100m,
    int MaxSimultaneousPositions = 20,
    int MaxConsecutiveLosses = 100,
    int CooldownMinutes = 30,
    decimal MinimumLiquidationSafetyBufferPercent = 0.1m);

public sealed record ReplayTrade(
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    decimal Quantity,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal PnL,
    decimal Fees,
    string Reason);

public sealed record EquityPoint(long Time, decimal Equity);

public sealed record ReplayResult(
    decimal InitialBalance,
    decimal FinalBalance,
    decimal NetProfit,
    decimal ReturnPercent,
    int NumberOfTrades,
    decimal WinRate,
    decimal ProfitFactor,
    decimal AverageWin,
    decimal AverageLoss,
    decimal MaximumDrawdown,
    decimal? SharpeRatio,
    decimal FeesPaid,
    decimal LargestWinningTrade,
    decimal LargestLosingTrade,
    int BarsUsed,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string Assumptions,
    IReadOnlyList<ReplayTrade> Trades,
    IReadOnlyList<EquityPoint> Equity);

public sealed class BacktestReplay
{
    private readonly IStrategyEngine _engine;

    public BacktestReplay(IStrategyEngine engine) => _engine = engine;

    public ReplayResult Run(StrategyDefinition definition, IReadOnlyList<MarketCandle> candles, ReplaySettings settings)
    {
        var ordered = candles
            .Where(c => c.IsClosed)
            .OrderBy(c => c.OpenTime)
            .ToList();
        var start = settings.Start;
        var end = settings.End <= start ? start.AddDays(1) : settings.End;
        var equity = Math.Max(settings.InitialBalance, 1m);
        var peak = equity;
        var maxDrawdown = 0m;
        var feesPaid = 0m;
        var trades = new List<ReplayTrade>();
        var equityCurve = new List<EquityPoint>();
        OpenPosition? open = null;
        var pendingBuy = false;
        var pendingExit = false;
        var pendingExitReason = "Exit";
        var sampleEvery = Math.Max(1, ordered.Count / 300);
        var lastBar = start;
        var inWindow = 0;
        var consecutiveLosses = 0;
        DateTimeOffset? lastLossAt = null;
        var dayStart = DateTimeOffset.MinValue;
        var dayPnl = 0m;
        var riskEngine = new RiskEngine();
        var profile = ProfileFromSettings(settings);

        for (var i = 0; i < ordered.Count; i++)
        {
            var bar = ordered[i];
            if (bar.CloseTime < start)
            {
                continue;
            }

            if (bar.OpenTime > end)
            {
                break;
            }

            lastBar = bar.CloseTime;
            inWindow++;

            if (pendingBuy && open is null)
            {
                var utcDay = new DateTimeOffset(bar.OpenTime.UtcDateTime.Date, TimeSpan.Zero);
                if (utcDay != dayStart)
                {
                    dayStart = utcDay;
                    dayPnl = 0m;
                }

                var fill = ApplySlippage(bar.Open, settings.SlippagePercent, worseForBuy: true);
                var qty = Size(
                    riskEngine,
                    profile,
                    equity,
                    fill,
                    settings,
                    dayPnl,
                    consecutiveLosses,
                    lastLossAt,
                    bar.OpenTime);
                if (qty > 0m)
                {
                    var fee = Notional(qty, fill) * (settings.FeePercent / 100m);
                    equity -= fee;
                    feesPaid += fee;
                    open = new OpenPosition(bar.OpenTime, qty, fill, fee);
                }

                pendingBuy = false;
            }
            else if (pendingExit && open is not null)
            {
                Close(open, ApplySlippage(bar.Open, settings.SlippagePercent, worseForBuy: false), bar.OpenTime, pendingExitReason, settings, trades, ref equity, ref feesPaid, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
                open = null;
                pendingExit = false;
            }

            if (open is not null)
            {
                var stop = open.EntryPrice * (1m - settings.StopLossPercent / 100m);
                var target = open.EntryPrice * (1m + settings.TakeProfitPercent / 100m);
                if (bar.Low <= stop)
                {
                    var fill = Math.Min(stop, bar.Low);
                    fill = ApplySlippage(fill, settings.SlippagePercent, worseForBuy: false);
                    Close(open, fill, bar.CloseTime, "Stop loss", settings, trades, ref equity, ref feesPaid, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
                    open = null;
                    pendingExit = false;
                }
                else if (bar.High >= target)
                {
                    var fill = Math.Min(target, bar.High);
                    fill = ApplySlippage(fill, settings.SlippagePercent, worseForBuy: false);
                    Close(open, fill, bar.CloseTime, "Take profit", settings, trades, ref equity, ref feesPaid, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
                    open = null;
                    pendingExit = false;
                }
            }

            var mark = equity + (open is null ? 0m : (bar.Close - open.EntryPrice) * open.Quantity);
            if (mark > peak)
            {
                peak = mark;
            }

            if (peak > 0m)
            {
                var dd = (peak - mark) / peak * 100m;
                if (dd > maxDrawdown)
                {
                    maxDrawdown = dd;
                }
            }

            if (inWindow % sampleEvery == 0 || i == ordered.Count - 1)
            {
                equityCurve.Add(new EquityPoint(bar.CloseTime.ToUnixTimeSeconds(), Round(mark)));
            }

            var closed = ordered.Take(i + 1).ToList();
            var signal = _engine.Evaluate(
                definition,
                new StrategyContext
                {
                    ClosedCandles = closed,
                    AverageEntryPrice = open?.EntryPrice,
                    CurrentPrice = bar.Close,
                    HasOpenPosition = open is not null,
                },
                out var reason);

            if (open is null && !pendingBuy && signal == SignalType.Buy)
            {
                pendingBuy = true;
            }
            else if (open is not null && !pendingExit && signal is SignalType.Exit or SignalType.Sell)
            {
                pendingExit = true;
                pendingExitReason = string.IsNullOrWhiteSpace(reason) ? "Exit" : reason;
            }
        }

        if (open is not null)
        {
            var last = ordered.Last(c => c.OpenTime <= end);
            Close(open, ApplySlippage(last.Close, settings.SlippagePercent, worseForBuy: false), last.CloseTime, "End of window", settings, trades, ref equity, ref feesPaid, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
        }

        var wins = trades.Where(t => t.PnL > 0m).ToList();
        var losses = trades.Where(t => t.PnL < 0m).ToList();
        var grossWin = wins.Sum(t => t.PnL);
        var grossLoss = Math.Abs(losses.Sum(t => t.PnL));
        var net = equity - settings.InitialBalance;
        var returns = trades.Select(t => t.PnL / Math.Max(settings.InitialBalance, 1m)).ToList();
        decimal? sharpe = null;
        if (returns.Count >= 2)
        {
            var mean = returns.Average();
            var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
            var stdev = (decimal)Math.Sqrt((double)Math.Max(variance, 0m));
            if (stdev > 0m)
            {
                sharpe = Round(mean / stdev * (decimal)Math.Sqrt(returns.Count));
            }
        }

        var windowStart = ordered.FirstOrDefault(c => c.CloseTime >= start)?.OpenTime ?? start;
        var windowEnd = lastBar;
        var assumptions =
            "Long-only replay of the same strategy engine and Isolated RiskEngine as paper/LIVE. " +
            "Position size, SL, TP, leverage, daily halt, and consecutive-loss lock come from the active risk book. " +
            "Entries and signal exits fill at the next bar open with slippage. " +
            "Stop and take-profit use that bar's high/low; stop wins if both are hit. " +
            "Unclosed trades flatten at the last close. No look-ahead on unclosed candles. " +
            "Historical simulation, not a guarantee of future performance.";

        return new ReplayResult(
            settings.InitialBalance,
            Round(equity),
            Round(net),
            settings.InitialBalance == 0m ? 0m : Round(net / settings.InitialBalance * 100m),
            trades.Count,
            trades.Count == 0 ? 0m : Round((decimal)wins.Count / trades.Count * 100m),
            grossLoss == 0m ? (grossWin > 0m ? 99m : 0m) : Round(grossWin / grossLoss),
            wins.Count == 0 ? 0m : Round(wins.Average(t => t.PnL)),
            losses.Count == 0 ? 0m : Round(losses.Average(t => t.PnL)),
            Round(maxDrawdown),
            sharpe,
            Round(feesPaid),
            trades.Count == 0 ? 0m : Round(trades.Max(t => t.PnL)),
            trades.Count == 0 ? 0m : Round(trades.Min(t => t.PnL)),
            ordered.Count(c => c.CloseTime >= start && c.OpenTime <= end),
            windowStart,
            windowEnd,
            assumptions,
            trades,
            equityCurve);
    }

    private static void Close(
        OpenPosition open,
        decimal exitPrice,
        DateTimeOffset at,
        string reason,
        ReplaySettings settings,
        List<ReplayTrade> trades,
        ref decimal equity,
        ref decimal feesPaid,
        ref int consecutiveLosses,
        ref DateTimeOffset? lastLossAt,
        ref decimal dayPnl)
    {
        var exitFee = Notional(open.Quantity, exitPrice) * (settings.FeePercent / 100m);
        var pnl = (exitPrice - open.EntryPrice) * open.Quantity - open.EntryFee - exitFee;
        equity += (exitPrice - open.EntryPrice) * open.Quantity - exitFee;
        feesPaid += exitFee;
        dayPnl += pnl;
        if (pnl < 0m)
        {
            consecutiveLosses++;
            lastLossAt = at;
        }
        else
        {
            consecutiveLosses = 0;
        }

        trades.Add(new ReplayTrade(
            open.OpenedAt,
            at,
            Round(open.Quantity),
            Round(open.EntryPrice),
            Round(exitPrice),
            Round(pnl),
            Round(open.EntryFee + exitFee),
            reason));
    }

    private static decimal Size(
        RiskEngine engine,
        RiskProfile profile,
        decimal available,
        decimal price,
        ReplaySettings settings,
        decimal dayPnl,
        int consecutiveLosses,
        DateTimeOffset? lastLossAt,
        DateTimeOffset at)
    {
        var evaluation = engine.Evaluate(
            SignalType.Buy,
            profile,
            new RiskSnapshot
            {
                Equity = available,
                AvailableBalance = available,
                DailyRealizedPnL = dayPnl,
                AccountDailyPnL = dayPnl,
                Symbol = "BACKTEST",
                Price = price,
                ConsecutiveLosses = consecutiveLosses,
                LastLossAt = lastLossAt,
                Sizing = new RiskSizingHints
                {
                    TakerFeePercent = settings.FeePercent,
                    SlippagePercent = settings.SlippagePercent
                }
            },
            at);
        if (evaluation.Decision != RiskDecision.Approved || evaluation.ApprovedQuantity <= 0m)
        {
            return 0m;
        }

        return Math.Round(evaluation.ApprovedQuantity, 8, MidpointRounding.ToZero);
    }

    private static RiskProfile ProfileFromSettings(ReplaySettings settings) =>
        new()
        {
            Name = "BACKTEST",
            RiskPerTradePercent = settings.RiskPercent,
            StopLossPercent = settings.StopLossPercent,
            TakeProfitPercent = settings.TakeProfitPercent,
            MaxLeverage = settings.Leverage,
            MaxDailyLossPercent = settings.MaxDailyLossPercent,
            MaxPortfolioRiskPercent = settings.MaxPortfolioRiskPercent,
            MaxSimultaneousPositions = settings.MaxSimultaneousPositions,
            MaxConsecutiveLosses = settings.MaxConsecutiveLosses,
            CooldownMinutes = settings.CooldownMinutes,
            MinimumLiquidationSafetyBufferPercent = settings.MinimumLiquidationSafetyBufferPercent
        };

    private static decimal ApplySlippage(decimal price, decimal slippagePercent, bool worseForBuy)
    {
        var factor = slippagePercent / 100m;
        return worseForBuy ? price * (1m + factor) : price * (1m - factor);
    }

    private static decimal Notional(decimal qty, decimal price) => qty * price;

    private static decimal Round(decimal value) => Math.Round(value, 8, MidpointRounding.AwayFromZero);

    private sealed record OpenPosition(DateTimeOffset OpenedAt, decimal Quantity, decimal EntryPrice, decimal EntryFee);
}
