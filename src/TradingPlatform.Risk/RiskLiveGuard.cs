using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Risk;

public static class RiskLiveGuard
{
    public static void EnsureAllowed(TradingMode mode, RiskProfile risk)
    {
        _ = mode;
        _ = risk;
    }
}
