using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Trading;

public static class LiveEntryGate
{
    public const string BlockedMessage =
        "Live entries are blocked. Trading:LiveTradingEnabled is false. Exits of an open position still run. Startup does not turn this flag on.";

    public static string? BlockNewEntry(TradingMode mode, bool liveTradingEnabled) =>
        mode == TradingMode.Live && !liveTradingEnabled ? BlockedMessage : null;
}
