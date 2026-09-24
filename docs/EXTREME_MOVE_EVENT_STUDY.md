# Extreme move event study

This is an event study. A feature association is not a trading edge.

event_definition_hash `8617c34a122720a498458bec1084713326401f3258d4c63f0662c05d0850cb1f`
experiment_manifest_hash `9a584fc3dcdbdc09dd8139401eb7f68604bdd75776d7062cfffaff672d2e6939`

## Label

Clock: closed 1h bars. Horizon: 24 closed hours after the signal bar. UP excursion is future high / signal close - 1. DOWN excursion is future low / signal close - 1. The signal bar itself is not in the future window.

## Separation rule (frozen before this run)

An independent episode starts at the first bar where +10% or -10% is reached inside 24h, after warmup. Direction is whichever 10% threshold is touched first. On a tie, the larger absolute excursion wins. The episode then records every nested threshold hit by that same path, the time to each threshold, the maximum excursion, and the bar of that maximum. The next independent episode on that coin cannot start until 24 hours after the maximum-excursion bar. This 24h gap was not tuned on OOS.

NESTED_EVENT counts every threshold hit inside an episode. INDEPENDENT_EVENT counts the episode once, at its highest threshold.

| Threshold | Nested hits | Independent episodes at that highest threshold |
| --- | ---: | ---: |
| UP_10 | 35348 | 31373 |
| UP_20 | 3975 | 2258 |
| UP_30 | 1717 | 1123 |
| UP_50 | 594 | 407 |
| UP_80 | 187 | 72 |
| UP_100 | 115 | 63 |
| UP_150 | 52 | 52 |
| DOWN_10 | 28662 | 26911 |
| DOWN_20 | 1751 | 1240 |
| DOWN_30 | 511 | 381 |
| DOWN_50 | 130 | 107 |
| DOWN_80 | 23 | 23 |

Symbols in the 1h study: 523. Symbols skipped for short history (<1500 bars): 6.
Matched controls: 62160. A control is the same coin, same IS/VAL/OOS window, same trailing ATR-percentile bucket, and outside every episode exclusion zone. Unmatched episodes are omitted, not filled.

## Speed (frozen)

On the 1h clock, FAST means the primary threshold is reached in 1 hour or less. MEDIUM means 2 to 4 hours. SLOW means more than 4 hours. The 30-minute FAST cut cannot be observed on 1h bars. The 5m/15m section is the place that cut can be discussed, and only for the 10-coin set.

| Speed | Episodes |
| --- | ---: |
| FAST | 373 |
| MEDIUM | 2239 |
| SLOW | 61398 |

## Splits (frozen from the known BTC cache window, not from results)

- IS: bar open before 2025-09-23
- VAL: 2025-09-23 inclusive to 2026-03-23 exclusive
- OOS: 2026-03-23 inclusive onward
- Blocks: before 2025-03-18, before 2025-09-18, before 2026-03-18, and after

## Self-check

synthetic ramp episodes=1 up=1 nestedHits=4

## No-lookahead

PASS BTCUSDT 1h RSI(14) and ret_1 unchanged at T when 5 future bars are withheld. mismatches=0

## Short horizon

Short-horizon scan uses the 10 coins that already have Vision metrics. 5m and 15m files already on disk. No download.
15m symbols=10 horizon=96 bars. Nested key-threshold hits (clustered by the same 24h separation):
- UP_30: 19
- UP_50: 0
- UP_80: 0
- UP_100: 0
- DOWN_30: 15
- DOWN_50: 6
- DOWN_80: 0
T-15m and T-30m leads exist on this 15m clock. The 1h table does not pretend to observe them.
5m symbols=10 horizon=288 bars. Nested key-threshold hits (clustered by the same 24h separation):
- UP_30: 21
- UP_50: 0
- UP_80: 0
- UP_100: 0
- DOWN_30: 15
- DOWN_50: 4
- DOWN_80: 0
T-5m and T-1m leads exist only on this 5m clock. They are counted here and are not mixed into the 1h effect table.
Sub-hour feature lead/lag on the full 529-coin universe is DATA_UNAVAILABLE because that study clock is 1h.


Trading:LiveTradingEnabled=False. Trading:CrossSectionalReversal Enabled=True PaperEnabled=False LiveEnabled=True. Scalping Enabled=False AllowLive=False. This study did not change these flags, did not enable Paper, did not promote a strategy, and did not submit an order. VALIDATED_FOR_PAPER=NONE. LIVE_APPROVED=false. Cross-sectional LiveEnabled or global live is not false in appsettings. Those values were left untouched.
