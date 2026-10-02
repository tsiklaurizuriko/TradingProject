using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Risk;

public sealed record LiveRiskFacts(
    decimal Equity,
    decimal OpenRiskPercent,
    int OpenPositions,
    int PositionsOnSymbol,
    decimal DailyRealizedPnl,
    decimal RequestedRiskPercent,
    decimal Leverage,
    decimal StopDistancePercent,
    decimal Quantity,
    decimal Notional,
    decimal MinQuantity,
    decimal MinNotional,
    decimal? AvailableMargin,
    decimal RequiredMargin,
    bool KillSwitch,
    int ConsecutiveLosses,
    DateTimeOffset? LastLossAt,
    DateTimeOffset UtcNow,
    bool DrawdownKnown);

public static class RiskLiveGuard
{
    public static void EnsureAllowed(TradingMode mode, RiskProfile risk)
    {
        var reason = ProfileProblem(mode, risk);
        if (reason is not null)
        {
            throw new DomainException(ErrorCodes.RiskLimitExceeded, reason);
        }
    }

    public static string? ProfileProblem(TradingMode mode, RiskProfile? risk)
    {
        if (mode != TradingMode.Live)
        {
            return "Only live mode is supported.";
        }

        if (risk is null)
        {
            return "Live risk profile is missing.";
        }

        if (!risk.AllowLive)
        {
            return "This risk profile is not allowed to trade live.";
        }

        if (risk.RiskPerTradePercent <= 0m || risk.RiskPerTradePercent > 100m)
        {
            return "Risk per trade is missing or invalid.";
        }

        if (risk.StopLossPercent <= 0m)
        {
            return "Stop distance is missing.";
        }

        if (risk.MaxLeverage < 1m)
        {
            return "Maximum leverage is missing.";
        }

        if (risk.MaxDailyLossPercent <= 0m)
        {
            return "Daily loss limit is missing.";
        }

        if (risk.MaxPortfolioRiskPercent <= 0m)
        {
            return "Exposure limit is missing.";
        }

        if (risk.MaxSimultaneousPositions < 1)
        {
            return "Position limit is missing.";
        }

        return null;
    }

    public static string? Reject(TradingMode mode, RiskProfile profile, LiveRiskFacts facts)
    {
        var profileProblem = ProfileProblem(mode, profile);
        if (mode != TradingMode.Live)
        {
            return profileProblem;
        }

        if (profileProblem is not null)
        {
            return profileProblem;
        }

        if (facts.KillSwitch)
        {
            return "Kill switch is active.";
        }

        if (facts.Equity <= 0m)
        {
            return "Equity is missing. Live entry is blocked.";
        }

        if (facts.AvailableMargin is null)
        {
            return "Available margin is missing. Live entry is blocked.";
        }

        if (!facts.DrawdownKnown)
        {
            return "Daily drawdown cannot be calculated. Live entry is blocked.";
        }

        if (facts.RequestedRiskPercent > profile.RiskPerTradePercent)
        {
            return "Risk per trade exceeds the profile.";
        }

        if (facts.OpenRiskPercent + facts.RequestedRiskPercent > profile.MaxPortfolioRiskPercent)
        {
            return "Open exposure exceeds the profile.";
        }

        if (facts.OpenPositions >= profile.MaxSimultaneousPositions)
        {
            return "Position count is at the profile limit.";
        }

        if (facts.PositionsOnSymbol > 0)
        {
            return "This coin already has an open position.";
        }

        var dailyLimit = facts.Equity * profile.MaxDailyLossPercent / 100m;
        if (facts.DailyRealizedPnl <= -dailyLimit)
        {
            return "Daily realized loss is at the limit.";
        }

        if (facts.Leverage > profile.MaxLeverage)
        {
            return "Leverage exceeds the profile.";
        }

        if (facts.StopDistancePercent <= 0m)
        {
            return "Stop distance is not positive.";
        }

        if (facts.MinQuantity > 0m && facts.Quantity < facts.MinQuantity)
        {
            return "Quantity is below the exchange minimum.";
        }

        if (facts.MinNotional > 0m && facts.Notional < facts.MinNotional)
        {
            return "Notional is below the exchange minimum.";
        }

        if (facts.AvailableMargin.Value < facts.RequiredMargin)
        {
            return "Available margin is below the required margin.";
        }

        if (profile.MaxConsecutiveLosses > 0
            && facts.ConsecutiveLosses >= profile.MaxConsecutiveLosses
            && facts.LastLossAt is { } lossAt
            && profile.CooldownMinutes > 0
            && facts.UtcNow < lossAt.AddMinutes(profile.CooldownMinutes))
        {
            return "Loss cooldown is still active.";
        }

        return null;
    }
}
