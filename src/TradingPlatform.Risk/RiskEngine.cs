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
}

public sealed record RiskSnapshot
{
    public decimal Equity { get; init; }
    public decimal AvailableBalance { get; init; }
    public decimal DailyRealizedPnL { get; init; }
    public int OpenPositions { get; init; }
    public int DailyTrades { get; init; }
    public int ConsecutiveLosses { get; init; }
    public DateTimeOffset? LastLossAt { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal StopLossPercent { get; init; }
    public decimal AccountDailyPnL { get; init; }
    public int AccountOpenPositions { get; init; }
    public int AccountDailyTrades { get; init; }
    public decimal AccountOpenNotional { get; init; }
    public bool SymbolAlreadyOpen { get; init; }
    public IReadOnlyList<decimal> OpenRiskFractions { get; init; } = [];
}

public interface IRiskEngine
{
    RiskEvaluation Evaluate(SignalType signal, RiskProfile profile, RiskSnapshot snapshot, DateTimeOffset utcNow);
}

public sealed class RiskEngine : IRiskEngine
{
    public const int MaxCrossOpenPositions = 5;

    public RiskEvaluation Evaluate(SignalType signal, RiskProfile profile, RiskSnapshot snapshot, DateTimeOffset utcNow)
    {
        if (signal is SignalType.Hold or SignalType.NoAction)
        {
            return Reject("No trade signal.");
        }

        if (profile.AllowedSymbolsCsv is { Length: > 0 } allowed)
        {
            var set = allowed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!set.Contains(snapshot.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                return Reject("Symbol is not allowed by the risk profile.");
            }
        }

        if (snapshot.AvailableBalance < profile.MinAvailableBalance)
        {
            return Reject("Available balance is below the configured minimum.");
        }

        var equity = snapshot.Equity > 0m ? snapshot.Equity : snapshot.AvailableBalance;
        var dailyLoss = snapshot.AccountDailyPnL < 0m ? -snapshot.AccountDailyPnL : 0m;
        if (dailyLoss == 0m && snapshot.DailyRealizedPnL < 0m)
        {
            dailyLoss = -snapshot.DailyRealizedPnL;
        }

        var maxDailyLoss = equity * (profile.MaxDailyLossPercent / 100m);
        if (maxDailyLoss > 0m && dailyLoss >= maxDailyLoss)
        {
            return new RiskEvaluation
            {
                Decision = RiskDecision.Rejected,
                Reason = "Account daily loss limit reached. New entries are halted.",
                HaltAccount = profile.StopAccountOnDailyLoss
            };
        }

        if (signal is not SignalType.Buy)
        {
            return new RiskEvaluation { Decision = RiskDecision.Approved, Reason = "Exit allowed." };
        }

        if (snapshot.SymbolAlreadyOpen)
        {
            return Reject("This symbol already has an open position in this book. Binance USD-M is one position per coin.");
        }

        var maxOpen = profile.MarginMode == MarginMode.Cross
            ? Math.Min(profile.MaxOpenPositions, MaxCrossOpenPositions)
            : profile.MaxOpenPositions;
        if (snapshot.AccountOpenPositions >= maxOpen)
        {
            return Reject(
                profile.MarginMode == MarginMode.Cross
                    ? "Cross book is full. Isolated is required to run a large coin universe."
                    : "Maximum open positions for this book have been reached.");
        }

        if (snapshot.AccountDailyTrades >= profile.MaxDailyTrades)
        {
            return Reject("Maximum daily trades for this book have been reached.");
        }

        if (snapshot.ConsecutiveLosses >= profile.MaxConsecutiveLosses)
        {
            return Reject("Maximum consecutive losses reached.");
        }

        if (snapshot.LastLossAt is { } lastLoss &&
            utcNow - lastLoss < TimeSpan.FromMinutes(profile.CooldownAfterLossMinutes))
        {
            return Reject("Cooldown after loss is still active.");
        }

        if (snapshot.Price <= 0m || snapshot.StopLossPercent <= 0m)
        {
            return Reject("Unable to size the order without a stop.");
        }

        var qtyFromR = PortfolioRisk.QuantityFromR(
            equity,
            profile.RiskPerTradePercent,
            snapshot.Price,
            snapshot.StopLossPercent);
        var maxNotional = equity * (profile.MaxPositionPercent / 100m);
        var remainingExposure = Math.Max(0m, equity * (profile.MaxTotalExposurePercent / 100m) - snapshot.AccountOpenNotional);
        var reserve = Math.Clamp(profile.MinFreeMarginPercent, 0m, 90m) / 100m;
        var freeForNew = snapshot.AvailableBalance * (1m - reserve);
        var leverage = Math.Max(1m, profile.MaxLeverage);
        var buyingPower = freeForNew * leverage;
        var notionalCap = MinPositive(qtyFromR * snapshot.Price, maxNotional, remainingExposure, buyingPower);
        if (notionalCap <= 0m)
        {
            return Reject("No remaining exposure or margin for a new position.");
        }

        var quantity = FitToHeat(
            notionalCap / snapshot.Price,
            profile,
            snapshot,
            equity);
        var maxHeat = profile.MaxPortfolioHeatPercent / 100m;
        if (quantity <= 0m || Heat(quantity, profile, snapshot, equity) > maxHeat)
        {
            return Reject("Portfolio heat is full. Correlated alts share one risk budget.");
        }

        return new RiskEvaluation
        {
            Decision = RiskDecision.Approved,
            Reason = "Risk checks passed.",
            ApprovedQuantity = quantity
        };
    }

    private static decimal FitToHeat(decimal maxQuantity, RiskProfile profile, RiskSnapshot snapshot, decimal equity)
    {
        var maxHeat = profile.MaxPortfolioHeatPercent / 100m;
        if (maxHeat <= 0m)
        {
            return maxQuantity;
        }

        if (Heat(maxQuantity, profile, snapshot, equity) <= maxHeat)
        {
            return maxQuantity;
        }

        var lo = 0m;
        var hi = maxQuantity;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2m;
            if (Heat(mid, profile, snapshot, equity) <= maxHeat)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static decimal Heat(decimal quantity, RiskProfile profile, RiskSnapshot snapshot, decimal equity)
    {
        var next = snapshot.OpenRiskFractions.ToList();
        next.Add(PortfolioRisk.RiskFraction(quantity, snapshot.Price, snapshot.StopLossPercent, equity));
        return PortfolioRisk.CorrelatedHeat(next, profile.CorrelationFactor);
    }

    private static decimal MinPositive(params decimal[] values)
    {
        var min = decimal.MaxValue;
        foreach (var value in values)
        {
            if (value > 0m && value < min)
            {
                min = value;
            }
        }

        return min == decimal.MaxValue ? 0m : min;
    }

    private static RiskEvaluation Reject(string reason) =>
        new() { Decision = RiskDecision.Rejected, Reason = reason };
}
