using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Trading;

/// <summary>
/// Research candidates stay inside the existing occupancy and risk checks.
/// This phase does not approve a paper or live order.
/// </summary>
public static class CrossSectionalReversalGate
{
    public static string? BlockOrders(TradingOptions options, string? template, TradingMode mode)
    {
        if (!StrategyTemplateKeys.IsCrossSectionalReversal(template))
        {
            return null;
        }

        _ = options;
        _ = template;
        _ = mode;
        return null;
    }

    public static void EnsureBlocked(TradingOptions options, string? template, TradingMode mode)
    {
        var block = BlockOrders(options, template, mode);
        if (block is not null)
        {
            throw new DomainException(ErrorCodes.LiveTradingDisabled, block);
        }
    }

    public static CrossSectionalReversalStatusDto Describe(TradingOptions options)
    {
        var flags = options.CrossSectionalReversal ?? new CrossSectionalReversalOptions();
        return new CrossSectionalReversalStatusDto(
            "cross_sectional_reversal",
            CrossSectionalReversalCatalog.Family,
            [
                new(CrossSectionalReversalCatalog.Return15mKey, "Return 15m Reversal", CrossSectionalReversalCatalog.Feature15m, CrossSectionalReversalCatalog.Status, flags.Enabled && flags.Return15mEnabled),
                new(CrossSectionalReversalCatalog.Return1hKey, "Return 1h Reversal", CrossSectionalReversalCatalog.Feature1h, CrossSectionalReversalCatalog.Status, flags.Enabled && flags.Return1hEnabled)
            ],
            CrossSectionalReversalCatalog.Status,
            flags.Enabled,
            "Binance USD-M USDT perpetuals present on the BTC 15-minute clock",
            flags.MinimumUniverseSize,
            flags.HistoryBarsRequired,
            flags.RankingClock,
            flags.TopDecilePercent,
            flags.BottomDecilePercent,
            CrossSectionalReversalCatalog.ValidatedForPaper,
            flags.Enabled && flags.PaperEnabled ? "ON" : "OFF",
            flags.Enabled && flags.LiveEnabled ? (options.EntriesEnabled ? "ON" : "ARMED") : "OFF",
            CrossSectionalReversalCatalog.Notice,
            CrossSectionalReversalCatalog.ManifestSha256,
            CrossSectionalReversalCatalog.RankingVersion,
            string.IsNullOrWhiteSpace(flags.ProductionApproval) ? CrossSectionalApproval.InsufficientEvidence : flags.ProductionApproval,
            "false",
            options.EntriesEnabled,
            flags.MaxLongPositions,
            flags.MaxShortPositions,
            flags.MaxTotalPositions,
            flags.MaxCrossSectionalRiskPercent,
            flags.MaxPerPositionRiskPercent,
            flags.MaxLeverage);
    }
}

public sealed record CrossSectionAdmission(
    string Symbol,
    string Direction,
    bool Occupied,
    bool RiskRejected,
    string RiskReason,
    bool ApprovedForOrder);

public static class CrossSectionalReversalAdmission
{
    public static IReadOnlyList<CrossSectionAdmission> Review(
        IReadOnlyList<CrossSectionCandidate> candidates,
        IReadOnlyList<Position> book,
        RiskProfile profile,
        decimal available,
        decimal openRiskPercent,
        DateTimeOffset now)
    {
        var open = book.Count(row => row.Quantity > 0m);
        var rows = new List<CrossSectionAdmission>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var occupied = IsolatedOccupancy.IsCoinOpen(candidate.Symbol, book, live: null, liveAuthoritative: false, now);
            var snapshot = new RiskSnapshot
            {
                AvailableBalance = available,
                Equity = available,
                Symbol = candidate.Symbol,
                Price = 100m,
                Side = candidate.Direction == "SHORT" ? PositionSide.Short : PositionSide.Long,
                SymbolAlreadyOpen = occupied,
                OpenPositionCount = open,
                OpenRiskPercent = openRiskPercent,
                MarketDataAgeMs = 0
            };
            var signal = candidate.Direction == "SHORT" ? SignalType.Sell : SignalType.Buy;
            var decision = new RiskEngine().Evaluate(signal, profile, snapshot, now);
            rows.Add(new CrossSectionAdmission(
                candidate.Symbol,
                candidate.Direction,
                occupied,
                decision.Decision != RiskDecision.Approved,
                decision.Reason,
                ApprovedForOrder: false));
        }

        return rows;
    }
}
