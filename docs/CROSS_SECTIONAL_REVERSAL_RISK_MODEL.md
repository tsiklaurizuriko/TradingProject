# Cross-sectional reversal risk model

The existing Risk Engine stays authoritative. `CrossSectionalRiskPolicy` only decides which candidates may be offered to it.

## Caps

Default configuration, not fitted on OOS:

- Max total positions 10
- Max long 5, max short 5
- Max cross-sectional planned risk 2%
- Max per position 0.25%
- Max leverage 3x
- Max cluster positions 3
- Max directional planned risk 1.5%
- Max heat 1

Equal risk is on. A basket of 4 positions is still 0.25% each, not 2% / 4.

## Priority

Longs: lowest recent return first. Shorts: highest recent return first. Ties break by symbol name. Out-of-sample profit is not an input.

## Rejections

Every skipped candidate keeps a reason: `SAME_SYMBOL_OCCUPIED`, `MAX_LONG_POSITIONS`, `MAX_SHORT_POSITIONS`, `MAX_TOTAL_POSITIONS`, `MAX_CROSS_SECTIONAL_RISK`, `CLUSTER_LIMIT`, `DIRECTIONAL_HEAT`, `CORRELATION_HEAT`, or `INSUFFICIENT_DATA`.

## Collision

One Isolated position per coin. If any open position already uses that coin, the cross-sectional candidate is rejected. Signals are not netted and a second Binance position is not created.

## Heat

Heat is the larger of position-count usage and planned-risk usage. Above `MaxHeat`, further names are rejected. Open positions are not closed because heat rose.

## What was not changed

`MaxDailyLossPercent` is stored and is not applied to Isolated entries. Consecutive-loss lock, max positions, and portfolio planned risk inside `RiskEngine.Evaluate` are unchanged for every other strategy.

A cross-sectional bot evaluates a detached copy: 0.25% per position, 2% stop, 4% take-profit, leverage capped at 3x, and at most 10 positions. LOW, MEDIUM, and HIGH rows are not updated.

Orders stay blocked while global live trading is off. Paper stays off. When global live and this strategy's live flag are both on, a selected coin uses the existing occupancy check and then `RiskEngine.Evaluate` on that copy. The shared stop-retry path for other bots is unchanged. The 15-minute universe is refreshed on a background pass, not inside the other bots' cycle. Fewer than 30 coins on the BTC 15-minute clock produces `INSUFFICIENT_DATA` and no order.
