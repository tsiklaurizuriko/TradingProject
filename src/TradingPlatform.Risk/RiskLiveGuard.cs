using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Risk;

public static class RiskLiveGuard
{
    public static void EnsureAllowed(TradingMode mode, RiskProfile risk)
    {
        if (mode == TradingMode.Live && !risk.AllowLive)
        {
            throw new DomainException(
                ErrorCodes.RiskLimitExceeded,
                $"{risk.Name} is not allowed on LIVE. Use LOW or MEDIUM.");
        }
    }
}
