using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Trading;

public readonly record struct CrossSectionDecision(SignalType Signal, string Reason);

/// <summary>
/// Ranks the closed 15-minute cache. Does not place orders and does not change the shared risk books.
/// </summary>
public static class CrossSectionalLiveBook
{
    public static CrossSectionDecision Decide(
        string? template,
        string symbol,
        TradingOptions options,
        IReadOnlyList<(string Symbol, IReadOnlyList<MarketCandle> Candles)> universe,
        IReadOnlySet<string> occupied,
        bool alreadyOpen)
    {
        if (alreadyOpen)
        {
            return new CrossSectionDecision(SignalType.NoAction, "Holding the open cross-sectional position.");
        }

        var flags = options.CrossSectionalReversal ?? new CrossSectionalReversalOptions();
        var feature = string.Equals(template, StrategyTemplateKeys.CrossSectionalReversalReturn1h, StringComparison.Ordinal)
            ? CrossSectionalReversalCatalog.Feature1h
            : CrossSectionalReversalCatalog.Feature15m;
        if (feature == CrossSectionalReversalCatalog.Feature15m && !flags.Return15mEnabled)
        {
            return new CrossSectionDecision(SignalType.NoAction, "Return 15m variant is off.");
        }

        if (feature == CrossSectionalReversalCatalog.Feature1h && !flags.Return1hEnabled)
        {
            return new CrossSectionDecision(SignalType.NoAction, "Return 1h variant is off.");
        }

        var lookback = CrossSectionalReversalCatalog.LookbackBars(feature);
        var history = Math.Max(1, flags.HistoryBarsRequired);
        DateTimeOffset? clock = null;
        IReadOnlyList<MarketCandle>? btc = null;
        foreach (var row in universe)
        {
            if (string.Equals(row.Symbol, "BTCUSDT", StringComparison.OrdinalIgnoreCase) && row.Candles.Count > lookback)
            {
                btc = row.Candles;
                clock = row.Candles[^1].OpenTime;
                break;
            }
        }

        if (clock is null || btc is null)
        {
            return new CrossSectionDecision(SignalType.NoAction, "INSUFFICIENT_DATA. BTCUSDT 15m clock is missing.");
        }

        var bars = new List<CrossSectionSymbolBar>();
        foreach (var row in universe)
        {
            var candles = row.Candles;
            if (candles.Count <= lookback || candles[^1].OpenTime != clock)
            {
                continue;
            }

            bars.Add(new CrossSectionSymbolBar
            {
                Symbol = row.Symbol,
                Close = candles[^1].Close,
                PriorClose = candles[^(lookback + 1)].Close,
                OwnClosedBars = candles.Count,
                OnClock = true
            });
        }

        var ranked = CrossSectionalReversalRanker.Rank(
            clock.Value,
            feature,
            bars,
            Math.Max(1, flags.MinimumEligibleSymbols),
            history,
            flags.AllowLong,
            flags.AllowShort);
        if (ranked.Count == 0)
        {
            return new CrossSectionDecision(SignalType.NoAction, "INSUFFICIENT_DATA. Fewer than the minimum eligible coins are on the BTC 15m clock.");
        }

        var plan = CrossSectionalRiskPolicy.Select(ranked, flags, occupied, clusters: null, ranked[0].UniverseSize);
        var mine = plan.Accepted.FirstOrDefault(row => string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (mine is null)
        {
            var rejected = plan.Rejected.FirstOrDefault(row => string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            return new CrossSectionDecision(
                SignalType.NoAction,
                rejected?.Reason ?? "Coin is outside the reversal deciles.");
        }

        var signal = mine.Direction == "SHORT" ? SignalType.Sell : SignalType.Buy;
        return new CrossSectionDecision(signal, $"{mine.Direction} {feature} equal-risk {flags.MaxPerPositionRiskPercent}%");
    }
}

/// <summary>
/// Detached copy used only for this strategy's order check. The stored LOW, MEDIUM, and HIGH books are not written.
/// </summary>
public static class CrossSectionalRiskBook
{
    public static RiskProfile Overlay(RiskProfile source, CrossSectionalReversalOptions? options)
    {
        var flags = options ?? new CrossSectionalReversalOptions();
        var leverage = source.MaxLeverage > 0m ? Math.Min(source.MaxLeverage, flags.MaxLeverage) : flags.MaxLeverage;
        return new RiskProfile
        {
            Name = source.Name,
            RiskPerTradePercent = flags.MaxPerPositionRiskPercent,
            StopLossPercent = 2m,
            TakeProfitPercent = 4m,
            MaxLeverage = leverage,
            MaxDailyLossPercent = source.MaxDailyLossPercent,
            MaxPortfolioRiskPercent = flags.MaxCrossSectionalRiskPercent,
            MaxSimultaneousPositions = flags.MaxTotalPositions,
            MaxConsecutiveLosses = source.MaxConsecutiveLosses,
            CooldownMinutes = source.CooldownMinutes,
            MinimumLiquidationSafetyBufferPercent = source.MinimumLiquidationSafetyBufferPercent,
            AllowLive = source.AllowLive
        };
    }
}
