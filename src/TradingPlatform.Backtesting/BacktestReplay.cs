using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

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
    decimal MinimumLiquidationSafetyBufferPercent = 0.1m,
    bool HonorSuggestedStops = false,
    int MaxHoldBars = 0,
    bool BookStopsOff = false,
    decimal TickSize = 0m,
    bool PreserveNullTake = false,
    int ExecutionDelayBars = 0,
    decimal BreakEvenAtR = 0m);

public sealed record ReplayTrade(
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    decimal Quantity,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal PnL,
    decimal Fees,
    string Reason,
    string Side = "Long",
    decimal MidEntryPrice = 0m,
    decimal MidExitPrice = 0m,
    decimal SlippageCost = 0m,
    decimal FundingPnl = 0m,
    decimal GrossPnl = 0m);

public sealed record EquityPoint(long Time, decimal Equity);

public sealed record ReplaySideMetrics(
    int Trades,
    decimal NetPnL,
    decimal WinRate,
    decimal ProfitFactor,
    decimal Expectancy,
    decimal MaximumDrawdown,
    decimal PositivePnlSum = 0m,
    decimal AbsoluteNegativePnlSum = 0m,
    int WinningTrades = 0,
    int LosingTrades = 0,
    int ZeroPnlTrades = 0,
    decimal Fees = 0m);

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
    IReadOnlyList<EquityPoint> Equity,
    ReplaySideMetrics Long,
    ReplaySideMetrics Short,
    decimal FundingPaid = 0m,
    string CostNotes = "EXCLUDING_FUNDING",
    PnlTotals? Totals = null,
    IReadOnlyList<decimal>? BookReturns = null,
    IReadOnlyList<decimal>? BookDrawdowns = null,
    decimal SlippagePaid = 0m,
    decimal GrossPnl = 0m,
    decimal? SortinoRatio = null);

public sealed record ReplayFundingSettlement(DateTimeOffset FundingTime, decimal FundingRate);

public sealed class BacktestReplay
{
    private readonly IStrategyEngine _engine;

    public BacktestReplay(IStrategyEngine engine) => _engine = engine;

    public ReplayResult Run(
        StrategyDefinition definition,
        IReadOnlyList<MarketCandle> candles,
        ReplaySettings settings,
        CausalIndicatorCache? indicatorCache = null,
        int? evaluateFromInclusive = null,
        int? evaluateToExclusive = null,
        CausalIndicatorCache? higherTimeframeCache = null,
        StrategyFuturesSeries? futures = null,
        IReadOnlyList<ReplayFundingSettlement>? fundingSettlements = null)
    {
        IReadOnlyList<MarketCandle> ordered;
        if (indicatorCache is not null)
        {
            ordered = indicatorCache.Candles;
        }
        else
        {
            ordered = candles
                .Where(c => c.IsClosed)
                .OrderBy(c => c.OpenTime)
                .ToList();
        }

        var start = settings.Start;
        var end = settings.End <= start ? start.AddDays(1) : settings.End;
        var indexWindow = evaluateFromInclusive.HasValue || evaluateToExclusive.HasValue;
        var evalFrom = Math.Clamp(evaluateFromInclusive ?? 0, 0, ordered.Count);
        var evalTo = Math.Clamp(evaluateToExclusive ?? ordered.Count, evalFrom, ordered.Count);
        var equity = Math.Max(settings.InitialBalance, 1m);
        var peak = equity;
        var maxDrawdown = 0m;
        var feesPaid = 0m;
        var trades = new List<ReplayTrade>();
        var equityCurve = new List<EquityPoint>();
        OpenPosition? open = null;
        PositionSide? pendingEntry = null;
        decimal? pendingStop = null;
        decimal? pendingTake = null;
        var pendingExit = false;
        PositionSide? pendingReverse = null;
        var pendingExitReason = "Exit";
        var pendingDue = -1;
        var sampleEvery = Math.Max(1, ordered.Count / 300);
        var lastBar = start;
        var lastProcessed = -1;
        var inWindow = 0;
        var consecutiveLosses = 0;
        DateTimeOffset? lastLossAt = null;
        var dayStart = DateTimeOffset.MinValue;
        var dayPnl = 0m;
        var fundingPaid = 0m;
        var slippagePaid = 0m;
        var grossPnl = 0m;
        var includeFunding = fundingSettlements is { Count: > 0 };
        var fundingIndex = 0;
        var riskEngine = new RiskEngine();
        var closeByDay = new Dictionary<DateOnly, decimal>();
        var profile = ProfileFromSettings(settings);
        var cache = indicatorCache ?? new CausalIndicatorCache(ordered);
        var loopFrom = indexWindow ? evalFrom : 0;

        for (var i = loopFrom; i < ordered.Count; i++)
        {
            var bar = ordered[i];
            var inSignalWindow = !indexWindow || (i >= evalFrom && i < evalTo);
            var fillTail = indexWindow && i == evalTo;
            if (!indexWindow)
            {
                if (bar.CloseTime < start)
                {
                    continue;
                }

                if (bar.OpenTime > end)
                {
                    break;
                }
            }
            else if (i > evalTo)
            {
                break;
            }
            else if (fillTail && pendingEntry is null && !pendingExit)
            {
                break;
            }

            lastBar = bar.CloseTime;
            lastProcessed = i;
            if (inSignalWindow)
            {
                inWindow++;
            }

            if (pendingReverse is { } reverseSide && pendingExit && open is not null && i >= pendingDue)
            {
                Close(open, ApplySlippage(bar.Open, settings.SlippagePercent, worseForBuy: open.Side == PositionSide.Short), bar.Open, bar.OpenTime, pendingExitReason, settings, trades, ref equity, ref feesPaid, ref slippagePaid, ref grossPnl, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
                open = null;
                pendingExit = false;
                pendingEntry = reverseSide;
                pendingReverse = null;
            }

            if (pendingEntry is { } entrySide && open is null && i >= pendingDue)
            {
                var utcDay = new DateTimeOffset(bar.OpenTime.UtcDateTime.Date, TimeSpan.Zero);
                if (utcDay != dayStart)
                {
                    dayStart = utcDay;
                    dayPnl = 0m;
                }

                    var mid = bar.Open;
                    var fill = ApplySlippage(mid, settings.SlippagePercent, worseForBuy: entrySide == PositionSide.Long);
                    decimal? posStop = null;
                    decimal? posTake = null;
                    decimal? stopPctOverride = null;
                    if (settings.BookStopsOff)
                    {
                        posStop = pendingStop;
                        if (pendingStop is { } structural && fill > 0m)
                        {
                            stopPctOverride = Math.Abs(structural - fill) / fill * 100m;
                        }
                    }
                    else if (settings.HonorSuggestedStops && pendingStop is not null)
                    {
                        if (!TryStructuralStops(entrySide, fill, pendingStop, pendingTake, settings.TickSize, settings.PreserveNullTake, out posStop, out posTake, out stopPctOverride))
                        {
                            pendingEntry = null;
                            pendingStop = null;
                            pendingTake = null;
                        }
                    }

                    if (pendingEntry is not null)
                    {
                    var qty = Size(
                        riskEngine,
                        profile,
                        equity,
                        fill,
                        settings,
                        dayPnl,
                        consecutiveLosses,
                        lastLossAt,
                        bar.OpenTime,
                        entrySide,
                        stopPctOverride,
                        posTake is { } takePx && fill > 0m
                            ? Math.Abs(takePx - fill) / fill * 100m
                            : null);
                    if (qty > 0m)
                    {
                        var fee = Notional(qty, fill) * (settings.FeePercent / 100m);
                        equity -= fee;
                        feesPaid += fee;
                        open = new OpenPosition(bar.OpenTime, qty, fill, fee, entrySide, MidEntryPrice: mid, StopPrice: posStop, TakePrice: posTake, FillIndex: i);
                    }
                    }

                pendingEntry = null;
                pendingStop = null;
                pendingTake = null;
            }
            else if (pendingExit && open is not null && i >= pendingDue)
            {
                Close(open, ApplySlippage(bar.Open, settings.SlippagePercent, worseForBuy: open.Side == PositionSide.Short), bar.Open, bar.OpenTime, pendingExitReason, settings, trades, ref equity, ref feesPaid, ref slippagePaid, ref grossPnl, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
                open = null;
                pendingExit = false;
            }

            if (open is not null && includeFunding)
            {
                var prevClose = i == 0 ? DateTimeOffset.MinValue : ordered[i - 1].CloseTime;
                while (fundingIndex < fundingSettlements!.Count && fundingSettlements[fundingIndex].FundingTime <= prevClose)
                {
                    fundingIndex++;
                }

                while (fundingIndex < fundingSettlements.Count && fundingSettlements[fundingIndex].FundingTime <= bar.CloseTime)
                {
                    var f = fundingSettlements[fundingIndex];
                    fundingIndex++;
                    if (f.FundingTime < open.OpenedAt)
                    {
                        continue;
                    }

                    var notional = open.Quantity * bar.Close;
                    var cash = notional * f.FundingRate;
                    var signed = open.Side == PositionSide.Long ? -cash : cash;
                    equity += signed;
                    fundingPaid -= signed;
                    open = open with { FundingAccrued = open.FundingAccrued + signed };
                }
            }

            if (open is not null)
            {
                decimal? stop = open.StopPrice;
                decimal? target = open.TakePrice;
                if (!settings.BookStopsOff)
                {
                    stop ??= open.Side == PositionSide.Short
                        ? open.EntryPrice * (1m + settings.StopLossPercent / 100m)
                        : open.EntryPrice * (1m - settings.StopLossPercent / 100m);
                    if (!settings.PreserveNullTake)
                    {
                        target ??= open.Side == PositionSide.Short
                            ? open.EntryPrice * (1m - settings.TakeProfitPercent / 100m)
                            : open.EntryPrice * (1m + settings.TakeProfitPercent / 100m);
                    }
                }

                if (stop is { } stopPrice && open.Side == PositionSide.Long && bar.Low <= stopPrice)
                {
                    var mid = StopTouchPrice(stopPrice, bar.Open, isShort: false);
                    var fill = ApplySlippage(mid, settings.SlippagePercent, worseForBuy: false);
                    Close(open, fill, mid, bar.CloseTime, "Stop loss", settings, trades, ref equity, ref feesPaid, ref slippagePaid, ref grossPnl, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
                    open = null;
                    pendingExit = false;
                }
                else if (stop is { } shortStop && open.Side == PositionSide.Short && bar.High >= shortStop)
                {
                    var mid = StopTouchPrice(shortStop, bar.Open, isShort: true);
                    var fill = ApplySlippage(mid, settings.SlippagePercent, worseForBuy: true);
                    Close(open, fill, mid, bar.CloseTime, "Stop loss", settings, trades, ref equity, ref feesPaid, ref slippagePaid, ref grossPnl, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
                    open = null;
                    pendingExit = false;
                }
                else if (target is { } targetPrice && open.Side == PositionSide.Long && bar.High >= targetPrice)
                {
                    var mid = Math.Min(targetPrice, bar.High);
                    var fill = ApplySlippage(mid, settings.SlippagePercent, worseForBuy: false);
                    Close(open, fill, mid, bar.CloseTime, "Take profit", settings, trades, ref equity, ref feesPaid, ref slippagePaid, ref grossPnl, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
                    open = null;
                    pendingExit = false;
                }
                else if (target is { } shortTarget && open.Side == PositionSide.Short && bar.Low <= shortTarget)
                {
                    var mid = Math.Max(shortTarget, bar.Low);
                    var fill = ApplySlippage(mid, settings.SlippagePercent, worseForBuy: true);
                    Close(open, fill, mid, bar.CloseTime, "Take profit", settings, trades, ref equity, ref feesPaid, ref slippagePaid, ref grossPnl, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
                    open = null;
                    pendingExit = false;
                }
                else if (settings.MaxHoldBars > 0 && i - open.FillIndex + 1 >= settings.MaxHoldBars)
                {
                    var mid = bar.Close;
                    var fill = ApplySlippage(mid, settings.SlippagePercent, worseForBuy: open.Side == PositionSide.Short);
                    Close(open, fill, mid, bar.CloseTime, "TIME", settings, trades, ref equity, ref feesPaid, ref slippagePaid, ref grossPnl, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
                    open = null;
                    pendingExit = false;
                }
                else if (settings.BreakEvenAtR > 0m && !open.BreakEvenArmed && stop is { } initialStop)
                {
                    var risk = Math.Abs(open.EntryPrice - initialStop);
                    var reached = open.Side == PositionSide.Long
                        ? bar.High >= open.EntryPrice + risk * settings.BreakEvenAtR
                        : bar.Low <= open.EntryPrice - risk * settings.BreakEvenAtR;
                    if (risk > 0m && reached)
                    {
                        open = open with { StopPrice = open.EntryPrice, BreakEvenArmed = true };
                    }
                }
            }

            var mark = equity + (open is null ? 0m : Direction(open.Side) * (bar.Close - open.EntryPrice) * open.Quantity);
            closeByDay[DateOnly.FromDateTime(bar.CloseTime.UtcDateTime)] = mark;
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

            if (inSignalWindow)
            {
                SignalType signal;
                string reason;
                StrategySignalDetail? detail = null;
                var evalContext = new StrategyContext
                {
                    ClosedCandles = ordered,
                    AverageEntryPrice = open?.EntryPrice,
                    CurrentPrice = bar.Close,
                    HasOpenPosition = open is not null,
                    PositionSide = open?.Side ?? PositionSide.Long,
                    PositionOpenedAt = open?.OpenedAt,
                    ProtectiveStopPrice = open?.StopPrice,
                    HigherTimeframeCache = higherTimeframeCache,
                    OpenInterest = futures?.OpenInterest,
                    FundingRate = futures?.FundingRate,
                    MarkPrice = futures?.MarkPrice,
                    IndexPrice = futures?.IndexPrice,
                    NormalizedBasis = futures?.NormalizedBasis
                };
                detail = _engine.EvaluateDetailAt(definition, evalContext, cache, i);
                signal = detail.Signal;
                reason = detail.Reason;

                if (open is null && pendingEntry is null && signal is SignalType.Buy or SignalType.Sell)
                {
                    pendingEntry = signal == SignalType.Sell ? PositionSide.Short : PositionSide.Long;
                    pendingDue = i + 1 + Math.Max(0, settings.ExecutionDelayBars);
                    pendingStop = detail?.SuggestedStop;
                    pendingTake = detail?.SuggestedTakeProfit;
                }
                else if (open is not null && !pendingExit
                    && (signal is SignalType.Exit
                        || (open.Side == PositionSide.Long && signal == SignalType.Sell)
                        || (open.Side == PositionSide.Short && signal == SignalType.Buy)))
                {
                    pendingExit = true;
                    pendingDue = i + 1 + Math.Max(0, settings.ExecutionDelayBars);
                    pendingExitReason = string.IsNullOrWhiteSpace(reason) ? "Exit" : reason;
                    var opposite = (open.Side == PositionSide.Long && signal == SignalType.Sell)
                        || (open.Side == PositionSide.Short && signal == SignalType.Buy);
                    if (settings.BookStopsOff && opposite)
                    {
                        pendingReverse = signal == SignalType.Sell ? PositionSide.Short : PositionSide.Long;
                        pendingStop = detail?.SuggestedStop;
                        pendingTake = null;
                    }
                }
                else if (settings.BookStopsOff && open is not null && detail?.SuggestedStop is decimal trail)
                {
                    open = open with { StopPrice = trail };
                }
                else if (settings.HonorSuggestedStops && open is not null && detail?.SuggestedStop is decimal suggested)
                {
                    var tightened = StrategyExecutionRules.TighterStop(open.Side, open.StopPrice, suggested, bar.Close);
                    if (tightened is { } next && next != open.StopPrice)
                    {
                        open = open with { StopPrice = next };
                    }
                }
            }
        }

        if (open is not null)
        {
            var last = lastProcessed >= 0 && lastProcessed < ordered.Count
                ? ordered[lastProcessed]
                : ordered.Last(c => c.OpenTime <= end);
            Close(open, ApplySlippage(last.Close, settings.SlippagePercent, worseForBuy: open.Side == PositionSide.Short), last.Close, last.CloseTime, "End of window", settings, trades, ref equity, ref feesPaid, ref slippagePaid, ref grossPnl, ref consecutiveLosses, ref lastLossAt, ref dayPnl);
            closeByDay[DateOnly.FromDateTime(last.CloseTime.UtcDateTime)] = equity;
        }

        var net = equity - settings.InitialBalance;
        var ratios = DailyReturnMetrics.From(DailyReturnMetrics.Returns(closeByDay, Math.Max(settings.InitialBalance, 1m)));
        var sharpe = ratios.Sharpe;

        var windowStart = indexWindow && evalFrom < ordered.Count
            ? ordered[evalFrom].OpenTime
            : ordered.FirstOrDefault(c => c.CloseTime >= start)?.OpenTime ?? start;
        var windowEnd = lastBar;
        var barsUsed = indexWindow
            ? Math.Max(0, evalTo - evalFrom)
            : ordered.Count(c => c.CloseTime >= start && c.OpenTime <= end);
        var assumptions =
            "Replay of the same strategy engine and Isolated RiskEngine as paper/LIVE. " +
            "LONG and SHORT are both allowed, one Isolated position at a time. Opposite signals flatten only; they do not reverse on the same bar. " +
            "Position size, SL, TP, leverage, daily halt, and consecutive-loss lock come from the active risk book. " +
            "Entries and signal exits fill at the next bar open with slippage. " +
            "Stop and take-profit use that bar's high/low; stop wins if both are hit. " +
            "Unclosed trades flatten at the last processed close. No look-ahead on unclosed candles. " +
            "Index windows score only in-window signals; a last-bar signal fills at T+1 open if that next candle exists in the series, then the window flattens. " +
            "If T+1 does not exist, the pending fill is dropped. Execution state starts flat at the window. " +
            (includeFunding
                ? "Settled funding (fundingTime <= bar.CloseTime, not future) is applied to open Isolated notional (INCLUDING_FUNDING). "
                : "Funding is not applied in this replay (EXCLUDING_FUNDING). A missing funding series is not treated as a zero funding rate. ") +
            (settings.PreserveNullTake
                ? "A missing suggested target stays open. It is not replaced with 2R. "
                : "When a suggested target is missing, structural mode fills a 2R target. ") +
            (settings.TickSize > 0m
                ? "Stops and targets are rounded to the configured tick size. "
                : "TICK_SIZE_UNCONFIGURED: symbol tick size was not supplied. Stops are not tick-rounded. A zero tick is not an invented size. ") +
            "Historical simulation, not a guarantee of future performance.";

        var longMetrics = ReplayMetrics.ForSide(trades, "Long");
        var shortMetrics = ReplayMetrics.ForSide(trades, "Short");
        var totals = PnlTotals.FromTrades(trades);
        var pf = totals.ProfitFactor;
        var bookReturn = settings.InitialBalance == 0m ? 0m : net / settings.InitialBalance;
        return new ReplayResult(
            settings.InitialBalance,
            Round(equity),
            Round(net),
            settings.InitialBalance == 0m ? 0m : Round(bookReturn * 100m),
            trades.Count,
            totals.WinRate,
            pf.IsFinite ? pf.Ratio : 0m,
            totals.WinningTrades == 0 ? 0m : Round(totals.PositivePnlSum / totals.WinningTrades),
            totals.LosingTrades == 0 ? 0m : Round(-(totals.AbsoluteNegativePnlSum / totals.LosingTrades)),
            Round(maxDrawdown),
            sharpe,
            Round(feesPaid),
            trades.Count == 0 ? 0m : Round(trades.Max(t => t.PnL)),
            trades.Count == 0 ? 0m : Round(trades.Min(t => t.PnL)),
            barsUsed,
            windowStart,
            windowEnd,
            assumptions,
            trades,
            equityCurve,
            longMetrics,
            shortMetrics,
            FundingPaid: Round(fundingPaid),
            CostNotes: includeFunding ? "INCLUDING_FUNDING" : "EXCLUDING_FUNDING",
            Totals: totals,
            BookReturns: [bookReturn],
            BookDrawdowns: [maxDrawdown],
            SlippagePaid: Round(slippagePaid),
            GrossPnl: Round(grossPnl),
            SortinoRatio: ratios.Sortino);
    }

    /// <summary>
    /// Where a stop-market order triggers before slippage: the stop price, or the bar open when the bar gapped through it.
    /// Using the bar's extreme here and then adding slippage would charge the gap twice.
    /// </summary>
    public static decimal StopTouchPrice(decimal stopPrice, decimal barOpen, bool isShort) =>
        isShort ? Math.Max(stopPrice, barOpen) : Math.Min(stopPrice, barOpen);

    public static bool AccountingIdentityHolds(ReplayResult result, decimal tolerance = 0.05m)
    {
        var reconstructed = result.GrossPnl - result.FeesPaid - result.SlippagePaid - result.FundingPaid;
        return Math.Abs(reconstructed - result.NetProfit) <= tolerance;
    }

    private static void Close(
        OpenPosition open,
        decimal exitPrice,
        decimal midExit,
        DateTimeOffset at,
        string reason,
        ReplaySettings settings,
        List<ReplayTrade> trades,
        ref decimal equity,
        ref decimal feesPaid,
        ref decimal slippagePaid,
        ref decimal grossPnl,
        ref int consecutiveLosses,
        ref DateTimeOffset? lastLossAt,
        ref decimal dayPnl)
    {
        var exitFee = Notional(open.Quantity, exitPrice) * (settings.FeePercent / 100m);
        var pnl = Direction(open.Side) * (exitPrice - open.EntryPrice) * open.Quantity - open.EntryFee - exitFee + open.FundingAccrued;
        equity += Direction(open.Side) * (exitPrice - open.EntryPrice) * open.Quantity - exitFee;
        feesPaid += exitFee;
        dayPnl += pnl;
        var midEntry = open.MidEntryPrice == 0m ? open.EntryPrice : open.MidEntryPrice;
        var dir = Direction(open.Side);
        var gross = dir * (midExit - midEntry) * open.Quantity;
        var slip = dir * (open.EntryPrice - midEntry) * open.Quantity + dir * (midExit - exitPrice) * open.Quantity;
        grossPnl += gross;
        slippagePaid += slip;
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
            reason,
            open.Side == PositionSide.Short ? "Short" : "Long",
            Round(midEntry),
            Round(midExit),
            Round(slip),
            Round(open.FundingAccrued),
            Round(gross)));
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
        DateTimeOffset at,
        PositionSide side = PositionSide.Long,
        decimal? stopLossPercentOverride = null,
        decimal? takeProfitPercentOverride = null)
    {
        var sized = profile;
        if (stopLossPercentOverride is > 0m || takeProfitPercentOverride is > 0m)
        {
            sized = ProfileFromSettings(settings);
            if (stopLossPercentOverride is > 0m)
            {
                sized.StopLossPercent = stopLossPercentOverride.Value;
            }

            if (takeProfitPercentOverride is > 0m)
            {
                sized.TakeProfitPercent = takeProfitPercentOverride.Value;
            }
        }

        var evaluation = engine.Evaluate(
            side == PositionSide.Short ? SignalType.Sell : SignalType.Buy,
            sized,
            new RiskSnapshot
            {
                Equity = available,
                AvailableBalance = available,
                DailyRealizedPnL = dayPnl,
                AccountDailyPnL = dayPnl,
                Symbol = "BACKTEST",
                Price = price,
                Side = side,
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

    private static decimal Direction(PositionSide side) => side == PositionSide.Short ? -1m : 1m;

    private static decimal Notional(decimal qty, decimal price) => qty * price;

    private static decimal Round(decimal value) => Math.Round(value, 8, MidpointRounding.AwayFromZero);

    private static bool TryStructuralStops(
        PositionSide side,
        decimal fill,
        decimal? suggestedStop,
        decimal? suggestedTake,
        decimal tickSize,
        bool preserveNullTake,
        out decimal? stop,
        out decimal? take,
        out decimal? stopPercent)
    {
        stop = null;
        take = null;
        stopPercent = null;
        if (fill <= 0m || suggestedStop is not { } rawStop)
        {
            return false;
        }

        rawStop = StrategyExecutionRules.RoundStop(side, rawStop, tickSize);
        var minDistance = fill * 0.002m;
        if (side == PositionSide.Long)
        {
            if (rawStop >= fill || fill - rawStop < minDistance)
            {
                return false;
            }

            var dist = fill - rawStop;
            stop = rawStop;
            stopPercent = dist / fill * 100m;
            if (suggestedTake is { } tp)
            {
                var roundedTake = StrategyExecutionRules.RoundTarget(side, tp, tickSize);
                take = roundedTake >= fill + dist ? roundedTake : fill + dist * 2m;
            }
            else if (!preserveNullTake)
            {
                take = fill + dist * 2m;
            }

            return true;
        }

        if (rawStop <= fill || rawStop - fill < minDistance)
        {
            return false;
        }

        var shortDist = rawStop - fill;
        stop = rawStop;
        stopPercent = shortDist / fill * 100m;
        if (suggestedTake is { } shortTp)
        {
            var roundedTake = StrategyExecutionRules.RoundTarget(side, shortTp, tickSize);
            take = roundedTake <= fill - shortDist ? roundedTake : fill - shortDist * 2m;
        }
        else if (!preserveNullTake)
        {
            take = fill - shortDist * 2m;
        }

        return true;
    }

    private sealed record OpenPosition(
        DateTimeOffset OpenedAt,
        decimal Quantity,
        decimal EntryPrice,
        decimal EntryFee,
        PositionSide Side,
        decimal FundingAccrued = 0m,
        decimal MidEntryPrice = 0m,
        decimal? StopPrice = null,
        decimal? TakePrice = null,
        int FillIndex = 0,
        bool BreakEvenArmed = false);
}
