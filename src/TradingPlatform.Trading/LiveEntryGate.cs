using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Trading;

public sealed record LiveEntryFacts(
    TradingMode Mode,
    bool LiveTradingEnabled,
    bool KillSwitchActive,
    bool ReconciliationFresh,
    bool AccountOrSymbolBlocked,
    bool StrategyEnabled,
    bool RiskAccepted,
    bool SymbolFiltersValid,
    bool ReducingExposure,
    TradingVenueKind Venue = TradingVenueKind.Live);

public static class LiveEntryGate
{
    public const string BlockedMessage =
        "Live entries are blocked. Trading:LiveTradingEnabled is false. Exits of an open position still run. Startup does not turn this flag on.";

    public static string EntriesOffMessage(TradingVenueKind venue) => venue switch
    {
        TradingVenueKind.Shadow => "Shadow entries are blocked. Trading:ShadowTradingEnabled is false. Exits of an open position still run.",
        TradingVenueKind.Testnet => "Testnet entries are blocked. Trading:TestnetTradingEnabled is false. Exits of an open position still run.",
        _ => BlockedMessage
    };

    public static string? BlockNewEntry(TradingMode mode, bool liveTradingEnabled) =>
        Block(new LiveEntryFacts(mode, liveTradingEnabled, false, true, false, true, true, true, false));

    public static string? Block(LiveEntryFacts facts)
    {
        if (facts.Mode != TradingMode.Live)
        {
            return "Only live mode is supported. This request was rejected and was not treated as live.";
        }

        if (facts.ReducingExposure)
        {
            return null;
        }

        if (!facts.LiveTradingEnabled)
        {
            return EntriesOffMessage(facts.Venue);
        }

        if (facts.KillSwitchActive)
        {
            return "Kill switch is active. New live entries are blocked.";
        }

        if (!facts.ReconciliationFresh)
        {
            return "Exchange reconciliation is missing or stale. New live entries are blocked.";
        }

        if (facts.AccountOrSymbolBlocked)
        {
            return "This account or coin has an unresolved order or reconciliation exception. New live entries are blocked.";
        }

        if (!facts.StrategyEnabled)
        {
            return "This strategy is not enabled for live entries.";
        }

        if (!facts.RiskAccepted)
        {
            return "Risk validation rejected the live entry.";
        }

        if (!facts.SymbolFiltersValid)
        {
            return "Symbol filters are missing or invalid. New live entries are blocked.";
        }

        return null;
    }
}
