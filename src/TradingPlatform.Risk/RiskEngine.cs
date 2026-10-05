using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Risk;

public enum RiskDecision
{
    Approved = 0,
    Rejected = 1
}

public sealed class RiskEvaluation
{
    public required RiskDecision Decision { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal ApprovedQuantity { get; init; }
    public bool HaltAccount { get; init; }
    public decimal RiskUsdt { get; init; }
    public decimal NotionalUsdt { get; init; }
    public decimal IsolatedMargin { get; init; }
    public decimal ActualRPercent { get; init; }
    public RiskPlan? Plan { get; init; }
}

public sealed record RiskSizingHints
{
    public decimal StepSize { get; init; }
    public decimal MinQuantity { get; init; }
    public decimal MinNotional { get; init; }
    public int? QuantityPrecision { get; init; }
    public decimal ExchangeMaxLeverage { get; init; }
    /// <summary>
    /// When the protective stop is tighter than this, size from this percent so a close stop does not inflate notional.
    /// The order stop stays at the profile stop.
    /// </summary>
    public decimal SizingStopLossPercent { get; init; }
    public decimal MaxMarginUsdt { get; init; }
    public decimal TakerFeePercent { get; init; }
    public decimal SlippagePercent { get; init; }

    /// <summary>Bracket maintenance margin rate as a fraction (0.004 = 0.4%). Zero uses <see cref="RiskEngine.DefaultMaintenanceMarginRate"/>.</summary>
    public decimal MaintenanceMarginRate { get; init; }

    /// <summary>Bracket maintenance amount ("cum") in USDT.</summary>
    public decimal MaintenanceAmount { get; init; }
}

public sealed record RiskPlan
{
    public decimal AvailableBalance { get; init; }
    public decimal Price { get; init; }
    public decimal RiskPerTradePercent { get; init; }
    public decimal RiskAmount { get; init; }
    public decimal ActualRiskAmount { get; init; }
    public decimal StopLossPercent { get; init; }
    public decimal StopLossPrice { get; init; }
    public decimal TakeProfitPercent { get; init; }
    public decimal TakeProfitPrice { get; init; }
    public decimal PositionNotional { get; init; }
    public decimal Quantity { get; init; }
    public decimal Leverage { get; init; }
    public decimal IsolatedMargin { get; init; }
    public decimal EstimatedEntryFee { get; init; }
    public decimal EstimatedExitFee { get; init; }
    public decimal EstimatedFee { get; init; }
    public decimal EstimatedSlippage { get; init; }
    public decimal EstimatedTotalRisk { get; init; }
    public decimal LiquidationPrice { get; init; }
    public decimal PortfolioRiskBefore { get; init; }
    public decimal PortfolioRiskAfter { get; init; }
    public bool Allowed { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record RiskSnapshot
{
    public decimal Equity { get; init; }
    public decimal AvailableBalance { get; init; }
    public decimal DailyRealizedPnL { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public PositionSide Side { get; init; }
    /// <summary>Realized PnL since the UTC day start plus open losses (open gains do not offset realized losses).</summary>
    public decimal AccountDailyPnL { get; init; }

    /// <summary>Same as <see cref="AccountDailyPnL"/> since the UTC week start.</summary>
    public decimal AccountWeeklyPnL { get; init; }

    /// <summary>Percent below the recorded equity peak. Null when there is no equity series (backtests, paper).</summary>
    public decimal? DrawdownPercent { get; init; }
    public bool SymbolAlreadyOpen { get; init; }
    /// <summary>Open Isolated coins already held by the evaluating strategy. Other running strategies do not consume this quota.</summary>
    public int OpenPositionCount { get; init; }
    public decimal OpenRiskPercent { get; init; }
    public int ConsecutiveLosses { get; init; }
    public DateTimeOffset? LastLossAt { get; init; }
    public int MarketDataAgeMs { get; init; }
    public RiskSizingHints? Sizing { get; init; }
}

public interface IRiskEngine
{
    RiskEvaluation Evaluate(SignalType signal, RiskProfile profile, RiskSnapshot snapshot, DateTimeOffset utcNow);
}

public sealed class RiskEngine : IRiskEngine
{
    public const decimal DefaultTakerFeePercent = 0.04m;
    public const decimal DefaultSlippagePercent = 0.05m;
    public const int MaxMarketDataAgeMs = 60_000;
    public const decimal EstimatedTotalRiskTolerance = 1.5m;

    /// <summary>Used when the bracket is unknown. Binance's lowest USD-M tiers run 0.4%–1.5%; 1% is a middle, not a best case.</summary>
    public const decimal DefaultMaintenanceMarginRate = 0.01m;

    public static RiskPlan Plan(
        RiskProfile profile,
        decimal available,
        decimal price,
        PositionSide side = PositionSide.Long,
        RiskSizingHints? sizing = null) =>
        Plan(profile, available, price, side, openRiskPercent: 0m, sizing);

    public static RiskPlan Plan(
        RiskProfile profile,
        decimal available,
        decimal price,
        PositionSide side,
        decimal openRiskPercent,
        RiskSizingHints? sizing)
    {
        if (available <= 0m)
        {
            return Denied("Available futures balance is zero.");
        }

        if (price <= 0m)
        {
            return Denied("Price is required to size the order.");
        }

        if (profile.RiskPerTradePercent <= 0m || profile.StopLossPercent <= 0m || profile.TakeProfitPercent <= 0m)
        {
            return Denied("Risk, stop loss, and take profit percents must be greater than zero.");
        }

        var exchangeCap = sizing is { ExchangeMaxLeverage: > 0m } ? sizing.ExchangeMaxLeverage : profile.MaxLeverage;
        var leverage = Math.Max(1m, Math.Min(profile.MaxLeverage, exchangeCap));
        var bankruptcyPercent = 100m / leverage;
        var buffer = profile.MinimumLiquidationSafetyBufferPercent > 0m
            ? profile.MinimumLiquidationSafetyBufferPercent
            : 1m;
        if (profile.StopLossPercent + buffer >= bankruptcyPercent)
        {
            return Denied("Stop loss is too close to Isolated liquidation.");
        }

        var feePercent = sizing is { TakerFeePercent: > 0m } ? sizing.TakerFeePercent : DefaultTakerFeePercent;
        var slipPercent = sizing is { SlippagePercent: > 0m } ? sizing.SlippagePercent : DefaultSlippagePercent;
        var riskAmount = available * (profile.RiskPerTradePercent / 100m);
        var sizingStop = profile.StopLossPercent;
        if (sizing is { SizingStopLossPercent: > 0m } && sizing.SizingStopLossPercent > sizingStop)
        {
            sizingStop = sizing.SizingStopLossPercent;
        }

        var quantity = PortfolioRisk.QuantityFromRiskUsdt(riskAmount, price, sizingStop);
        var raisedToMinimum = false;
        var portfolioCap = profile.MaxPortfolioRiskPercent > 0m ? profile.MaxPortfolioRiskPercent : 4m;
        if (sizing is { StepSize: > 0m } || sizing is { MinQuantity: > 0m } || sizing is { MinNotional: > 0m } || sizing is { QuantityPrecision: >= 0 })
        {
            var precision = sizing?.QuantityPrecision
                ?? PortfolioRisk.EffectiveQuantityPrecision(0, sizing?.StepSize ?? 0m);
            quantity = PortfolioRisk.FloorToStep(quantity, sizing?.StepSize ?? 0m, precision);
            var minQty = sizing?.MinQuantity ?? 0m;
            var minNotional = sizing?.MinNotional ?? 0m;
            if ((minQty > 0m && quantity < minQty) || (minNotional > 0m && quantity * price < minNotional))
            {
                var floor = minQty;
                if (minNotional > 0m)
                {
                    floor = Math.Max(floor, minNotional / price);
                }

                var raised = PortfolioRisk.CeilToStep(floor, sizing?.StepSize ?? 0m, precision);
                var raisedRisk = raised * price * (profile.StopLossPercent / 100m);
                var raisedPercent = available > 0m ? raisedRisk / available * 100m : 0m;
                var raisedMargin = PortfolioRisk.IsolatedMargin(raised * price, leverage);
                var raisedFee = raised * price * (feePercent / 100m);
                if (raised <= 0m
                    || (minQty > 0m && raised < minQty)
                    || (minNotional > 0m && raised * price < minNotional)
                    || raisedMargin + raisedFee > available
                    || openRiskPercent + raisedPercent > portfolioCap)
                {
                    return Denied("Calculated position size is below exchange minimum and cannot be traded within the configured risk.");
                }

                quantity = raised;
                raisedToMinimum = true;
            }
        }

        if (sizing is { MaxMarginUsdt: > 0m }
            && PortfolioRisk.IsolatedMargin(quantity * price, leverage) > sizing.MaxMarginUsdt)
        {
            var precision = sizing.QuantityPrecision
                ?? PortfolioRisk.EffectiveQuantityPrecision(0, sizing.StepSize);
            var capped = PortfolioRisk.FloorToStep(
                sizing.MaxMarginUsdt * leverage / price,
                sizing.StepSize,
                precision);
            var minQty = sizing.MinQuantity;
            var minNotional = sizing.MinNotional;
            if (capped <= 0m
                || (minQty > 0m && capped < minQty)
                || (minNotional > 0m && capped * price < minNotional))
            {
                return Denied("Calculated position size is below exchange minimum and cannot be traded within the configured risk.");
            }

            quantity = capped;
        }

        var notional = quantity * price;
        var actualRisk = notional * (profile.StopLossPercent / 100m);
        var margin = PortfolioRisk.IsolatedMargin(notional, leverage);
        var entryFee = notional * (feePercent / 100m);
        var exitFee = notional * (feePercent / 100m);
        var slippage = notional * (slipPercent / 100m);
        var estimatedTotal = actualRisk + entryFee + exitFee + slippage;
        if (margin + entryFee > available)
        {
            return Denied("Isolated margin plus fee exceeds available balance.");
        }

        if (!raisedToMinimum && estimatedTotal > riskAmount * EstimatedTotalRiskTolerance)
        {
            return Denied("Estimated total risk including fees and slippage exceeds the configured risk tolerance.");
        }

        var newRiskPercent = available > 0m ? actualRisk / available * 100m : 0m;
        var projected = openRiskPercent + newRiskPercent;
        if (projected > portfolioCap)
        {
            return Denied("Projected portfolio planned risk for this strategy exceeds the configured maximum.");
        }

        var stopPrice = side == PositionSide.Short
            ? price * (1m + profile.StopLossPercent / 100m)
            : price * (1m - profile.StopLossPercent / 100m);
        var takePrice = side == PositionSide.Short
            ? price * (1m - profile.TakeProfitPercent / 100m)
            : price * (1m + profile.TakeProfitPercent / 100m);
        var mmr = sizing is { MaintenanceMarginRate: > 0m } ? sizing.MaintenanceMarginRate : DefaultMaintenanceMarginRate;
        var liquidation = PortfolioRisk.IsolatedLiquidationPrice(
            price,
            quantity,
            leverage,
            side == PositionSide.Short,
            mmr,
            sizing?.MaintenanceAmount ?? 0m);
        if (profile.StopLossPercent + buffer >= PortfolioRisk.LiquidationDistancePercent(price, liquidation))
        {
            return Denied($"Stop loss is too close to Isolated liquidation after {mmr * 100m:0.##}% maintenance margin.");
        }

        return new RiskPlan
        {
            AvailableBalance = available,
            Price = price,
            RiskPerTradePercent = profile.RiskPerTradePercent,
            RiskAmount = riskAmount,
            ActualRiskAmount = actualRisk,
            StopLossPercent = profile.StopLossPercent,
            StopLossPrice = stopPrice,
            TakeProfitPercent = profile.TakeProfitPercent,
            TakeProfitPrice = takePrice,
            PositionNotional = notional,
            Quantity = quantity,
            Leverage = leverage,
            IsolatedMargin = margin,
            EstimatedEntryFee = entryFee,
            EstimatedExitFee = exitFee,
            EstimatedFee = entryFee + exitFee,
            EstimatedSlippage = slippage,
            EstimatedTotalRisk = estimatedTotal,
            LiquidationPrice = liquidation,
            PortfolioRiskBefore = openRiskPercent,
            PortfolioRiskAfter = projected,
            Allowed = true,
            Reason = "Risk checks passed."
        };
    }

    public RiskEvaluation Evaluate(SignalType signal, RiskProfile profile, RiskSnapshot snapshot, DateTimeOffset utcNow)
    {
        if (signal is SignalType.Hold or SignalType.NoAction)
        {
            return Reject("No trade signal.");
        }

        if (signal is SignalType.Exit)
        {
            return new RiskEvaluation { Decision = RiskDecision.Approved, Reason = "Exit allowed." };
        }

        if (snapshot.MarketDataAgeMs > MaxMarketDataAgeMs)
        {
            return Lock("Market data is stale. New Isolated entries are locked.");
        }

        var lossHalt = LossHalt(profile, snapshot);
        if (lossHalt is not null)
        {
            return Lock(lossHalt);
        }

        var available = snapshot.AvailableBalance;
        var maxLosses = profile.MaxConsecutiveLosses > 0 ? profile.MaxConsecutiveLosses : 5;
        var cooldown = TimeSpan.FromMinutes(profile.CooldownMinutes > 0 ? profile.CooldownMinutes : 30);
        if (snapshot.ConsecutiveLosses >= maxLosses
            && snapshot.LastLossAt is { } last
            && utcNow < last + cooldown)
        {
            return Lock("Consecutive loss limit reached. New entries are locked until the cooldown ends.");
        }

        if (snapshot.SymbolAlreadyOpen)
        {
            return Reject("This coin already has an open Isolated position. Binance USD-M is one position per coin.");
        }

        var maxOpen = profile.MaxSimultaneousPositions > 0 ? profile.MaxSimultaneousPositions : 2;
        if (snapshot.OpenPositionCount >= maxOpen)
        {
            return Reject("Maximum simultaneous Isolated positions reached for this strategy.");
        }

        var side = signal == SignalType.Sell || snapshot.Side == PositionSide.Short
            ? PositionSide.Short
            : PositionSide.Long;
        var plan = Plan(
            profile,
            available,
            snapshot.Price,
            side,
            snapshot.OpenRiskPercent,
            snapshot.Sizing);
        if (!plan.Allowed)
        {
            return Reject(plan.Reason);
        }

        return new RiskEvaluation
        {
            Decision = RiskDecision.Approved,
            Reason = plan.Reason,
            ApprovedQuantity = plan.Quantity,
            RiskUsdt = plan.RiskAmount,
            NotionalUsdt = plan.PositionNotional,
            IsolatedMargin = plan.IsolatedMargin,
            ActualRPercent = plan.RiskPerTradePercent,
            Plan = plan
        };
    }

    /// <summary>Daily, weekly and drawdown halts. Limits are percent of current equity; zero turns a limit off.</summary>
    public static string? LossHalt(RiskProfile profile, RiskSnapshot snapshot)
    {
        var equity = snapshot.Equity > 0m ? snapshot.Equity : snapshot.AvailableBalance;
        if (equity <= 0m)
        {
            return null;
        }

        if (profile.MaxDailyLossPercent > 0m && snapshot.AccountDailyPnL <= -(equity * profile.MaxDailyLossPercent / 100m))
        {
            return $"Daily loss {snapshot.AccountDailyPnL:0.##} USDT reached the {profile.MaxDailyLossPercent:0.##}% limit. New entries resume at the next UTC day.";
        }

        if (profile.MaxWeeklyLossPercent > 0m && snapshot.AccountWeeklyPnL <= -(equity * profile.MaxWeeklyLossPercent / 100m))
        {
            return $"Weekly loss {snapshot.AccountWeeklyPnL:0.##} USDT reached the {profile.MaxWeeklyLossPercent:0.##}% limit. New entries resume next UTC week.";
        }

        if (profile.MaxDrawdownPercent > 0m && snapshot.DrawdownPercent is { } drawdown && drawdown >= profile.MaxDrawdownPercent)
        {
            return $"Equity is {drawdown:0.##}% below its recorded peak (limit {profile.MaxDrawdownPercent:0.##}%). New entries stay halted while it is. A withdrawal also lowers equity and counts here.";
        }

        return null;
    }

    private static RiskPlan Denied(string reason) =>
        new() { Allowed = false, Reason = reason };

    private static RiskEvaluation Reject(string reason) =>
        new() { Decision = RiskDecision.Rejected, Reason = reason };

    private static RiskEvaluation Lock(string reason) =>
        new() { Decision = RiskDecision.Rejected, Reason = reason, HaltAccount = true };
}
