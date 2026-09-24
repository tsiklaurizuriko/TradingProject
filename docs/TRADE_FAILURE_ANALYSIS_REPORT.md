# Trade failure analysis

Definition SHA-256 `b97e959ee7898d5ebacf319b3a85b4d3681b316f47fa6f70e4e3fee516295654`.

This is a diagnosis of the existing reconstructed book. It is not an edge. It is not a new strategy. No stop, target, or filter was changed.

## 1. Dataset and coverage

Path-diagnosed trades: 11125. Winners: 3543. Losers: 7582.
Arms: Frozen Five at 15m, Final Five primary timeframes, Phase 8 baselines at 5m. Near-miss was not added. Symbols: BTCUSDT, ETHUSDT, BNBUSDT.
The prior edge-discovery run stored aggregates only. These are the same arms and the same Model B book, replayed so each trade can carry a path. No new rule was introduced.
Phase 4 baseline trades used for costs only: 44876. They have no exit time, so they are not in the loser taxonomy.

## 2. Definitions

Fixed before aggregation. Losers are classified in this order and one label only. COST_DOMINATED: net is negative and gross is not. IMMEDIATELY_WRONG: within the first 3 bars, adverse excursion reaches 1% and favorable excursion stays under 0.5%. RIGHT_DIRECTION_STOP_TOO_TIGHT: the book exit is Stop loss, and within 20 bars after that exit the favorable excursion from the original entry reaches 2%. RIGHT_DIRECTION_EXIT_TOO_EARLY: the exit is not Stop loss and not Take profit, and within 20 bars after the exit the favorable excursion from the original entry reaches 4%. RIGHT_DIRECTION_BAD_ENTRY: favorable excursion during the trade reaches 1% and the trade still finishes negative. NO_CLEAR_CAUSE: every other loser. Same-bar order is unknown, so a favorable wick on the stop bar is not counted as movement before the stop, and an adverse wick on the target bar is not counted as movement before the target. A label is cross-family only when at least two independent groups each have 50 losers and that label is at least 30% of those losers. Nothing here is an edge, a new stop, or a new target.

Excursions are fractions of the fill price. Displayed percents are that fraction times 100. Horizon returns are the maximum favorable excursion inside 1, 3, 5, 10, and 20 bars from the fill, and they stop at the last available bar rather than borrowing a future bar that does not exist.

## 3. Global MAE and MFE

| Set | n | MAE p25 | MAE p50 | MAE p75 | MFE p25 | MFE p50 | MFE p75 | Hold p50 bars |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| All | 11125 | 0.53 | 1.37 | 2.13 | 0.37 | 1.15 | 3.32 | 39 |
| Winners | 3543 | 0.21 | 0.51 | 1.07 | 2.66 | 4.10 | 4.35 | 72 |
| Losers | 7582 | 0.92 | 2.05 | 2.24 | 0.21 | 0.61 | 1.44 | 31 |

Loser MAE buckets, as shares of the existing 2% stop:

- <0.5%: 883 (11.6%)
- 0.5–1%: 1170 (15.4%)
- 1–2%: 986 (13.0%)
- >=2%: 4543 (59.9%)

Loser MFE buckets:

- <0.5%: 3368 (44.4%)
- 0.5–1%: 1516 (20.0%)
- 1–2%: 1534 (20.2%)
- 2–4%: 1164 (15.4%)
- >=4%: 0 (0.0%)

## 4. Losing-trade taxonomy

| Label | n | Share of losers |
| --- | ---: | ---: |
| NO_CLEAR_CAUSE | 4144 | 54.7% |
| RIGHT_DIRECTION_BAD_ENTRY | 2470 | 32.6% |
| IMMEDIATELY_WRONG | 625 | 8.2% |
| COST_DOMINATED | 220 | 2.9% |
| RIGHT_DIRECTION_STOP_TOO_TIGHT | 94 | 1.2% |
| RIGHT_DIRECTION_EXIT_TOO_EARLY | 29 | 0.4% |

## 5. Family comparison

| Slice | Losers | Mode | Mode share | Gross exp | Net exp |
| --- | ---: | --- | ---: | ---: | ---: |
| FROZEN_MEAN | 120 | NO_CLEAR_CAUSE | 57.5% | -1.06 | -1.36 |
| FROZEN_TREND | 3306 | NO_CLEAR_CAUSE | 70.0% | -1.66 | -1.93 |
| PA_CONTINUATION | 723 | RIGHT_DIRECTION_BAD_ENTRY | 47.2% | -5.77 | -6.05 |
| PA_FAILED | 723 | RIGHT_DIRECTION_BAD_ENTRY | 48.3% | -5.32 | -5.59 |
| PA_STRUCTURE | 1815 | RIGHT_DIRECTION_BAD_ENTRY | 48.0% | -4.92 | -5.18 |
| PA_SWEEP | 895 | RIGHT_DIRECTION_BAD_ENTRY | 48.3% | -5.02 | -5.28 |


Research family:

| Slice | Losers | Mode | Mode share | Gross exp | Net exp |
| --- | ---: | --- | ---: | ---: | ---: |
| FINAL_FIVE | 1176 | RIGHT_DIRECTION_BAD_ENTRY | 47.8% | -5.67 | -5.95 |
| FROZEN | 3426 | NO_CLEAR_CAUSE | 69.6% | -1.64 | -1.91 |
| PHASE8 | 2980 | RIGHT_DIRECTION_BAD_ENTRY | 48.0% | -4.96 | -5.22 |


## 6. Long vs short

| Slice | Losers | Mode | Mode share | Gross exp | Net exp |
| --- | ---: | --- | ---: | ---: | ---: |
| LONG | 3976 | NO_CLEAR_CAUSE | 54.3% | -3.69 | -3.96 |
| SHORT | 3606 | NO_CLEAR_CAUSE | 55.0% | -3.43 | -3.70 |

- LONG all trades: n=5814 gross expectancy -0.43 net expectancy -0.69
- SHORT all trades: n=5311 gross expectancy -0.13 net expectancy -0.40

## 7. Symbols

| Slice | Losers | Mode | Mode share | Gross exp | Net exp |
| --- | ---: | --- | ---: | ---: | ---: |
| BNBUSDT | 2444 | NO_CLEAR_CAUSE | 57.9% | -3.54 | -3.82 |
| BTCUSDT | 2189 | NO_CLEAR_CAUSE | 59.0% | -3.35 | -3.62 |
| ETHUSDT | 2949 | NO_CLEAR_CAUSE | 48.8% | -3.75 | -4.01 |


## 8. Timeframes

| Slice | Losers | Mode | Mode share | Gross exp | Net exp |
| --- | ---: | --- | ---: | ---: | ---: |
| 15m | 4308 | NO_CLEAR_CAUSE | 63.1% | -2.42 | -2.70 |
| 1h | 294 | RIGHT_DIRECTION_BAD_ENTRY | 41.8% | -6.23 | -6.53 |
| 5m | 2980 | RIGHT_DIRECTION_BAD_ENTRY | 48.0% | -4.96 | -5.22 |


## 9. IS, validation, OOS

| Slice | Losers | Mode | Mode share | Gross exp | Net exp |
| --- | ---: | --- | ---: | ---: | ---: |
| IS | 5016 | NO_CLEAR_CAUSE | 51.6% | -3.90 | -4.18 |
| OOS | 1132 | NO_CLEAR_CAUSE | 62.1% | -2.70 | -2.95 |
| VALIDATION | 1434 | NO_CLEAR_CAUSE | 59.5% | -3.09 | -3.34 |


## 10. Chronological blocks

| Slice | Losers | Mode | Mode share | Gross exp | Net exp |
| --- | ---: | --- | ---: | ---: | ---: |
| BLOCK1 | 2284 | NO_CLEAR_CAUSE | 49.7% | -4.21 | -4.50 |
| BLOCK2 | 2061 | NO_CLEAR_CAUSE | 50.8% | -3.83 | -4.10 |
| BLOCK3 | 1709 | NO_CLEAR_CAUSE | 60.9% | -3.05 | -3.30 |
| BLOCK4 | 1528 | NO_CLEAR_CAUSE | 60.2% | -2.82 | -3.07 |


Volatility and trend, from the signal bar only:

| Slice | Losers | Mode | Mode share | Gross exp | Net exp |
| --- | ---: | --- | ---: | ---: | ---: |
| HIGH | 2999 | NO_CLEAR_CAUSE | 50.1% | -3.78 | -4.05 |
| LOW | 2782 | NO_CLEAR_CAUSE | 58.7% | -3.30 | -3.58 |
| NORMAL | 1793 | NO_CLEAR_CAUSE | 55.8% | -3.60 | -3.87 |
| UNKNOWN | 8 | NO_CLEAR_CAUSE | 75.0% | -5.41 | -5.71 |


| Slice | Losers | Mode | Mode share | Gross exp | Net exp |
| --- | ---: | --- | ---: | ---: | ---: |
| RANGE | 2451 | NO_CLEAR_CAUSE | 61.8% | -2.77 | -3.05 |
| TRANSITION | 1320 | NO_CLEAR_CAUSE | 57.4% | -3.36 | -3.63 |
| TREND | 3811 | NO_CLEAR_CAUSE | 49.1% | -4.15 | -4.41 |


## 11. Costs

Reconstructed book: gross expectancy -0.29, net expectancy -0.56.
Mean fees 0.18, mean slippage 0.09, mean funding 0.00.
Losers whose gross was non-negative: 220 (2.9%).
Phase 4 baselines, all symbols in that file: n=44876, gross expectancy -7.76, net expectancy -12.16, mean funding -0.13, cost-flipped losers 10.

## 12. Cross-family failure modes

- IMMEDIATELY_WRONG: groups at or above 30% with at least 50 losers: none.
- RIGHT_DIRECTION_BAD_ENTRY: groups at or above 30% with at least 50 losers: PA_SWEEP, PA_FAILED, PA_CONTINUATION, PA_STRUCTURE.
- RIGHT_DIRECTION_STOP_TOO_TIGHT: groups at or above 30% with at least 50 losers: none.
- RIGHT_DIRECTION_EXIT_TOO_EARLY: groups at or above 30% with at least 50 losers: none.
- COST_DOMINATED: groups at or above 30% with at least 50 losers: none.
- NO_CLEAR_CAUSE: groups at or above 30% with at least 50 losers: FROZEN_TREND, FROZEN_MEAN, PA_SWEEP, PA_FAILED, PA_CONTINUATION, PA_STRUCTURE.

## 13. Observed failure mechanisms

Ranked by loser count. A rank is not a proposal to change the book.

1. NO_CLEAR_CAUSE: 4144 losers, 54.7%.
2. RIGHT_DIRECTION_BAD_ENTRY: 2470 losers, 32.6%.
3. IMMEDIATELY_WRONG: 625 losers, 8.2%.
4. COST_DOMINATED: 220 losers, 2.9%.
5. RIGHT_DIRECTION_STOP_TOO_TIGHT: 94 losers, 1.2%.

Signal path: 423 losers (5.6%) reached a 2% favorable excursion inside 20 bars of entry, including bars after the exit.
Stop path: 4319 of 4445 stop exits (97.2%) had some favorable excursion on a bar strictly before the stop bar.
After the stop bar, 131 of 4445 (2.9%) later traded 2% in the original direction inside 20 bars. That count is not a new stop.
Timing: 506 losers (6.7%) were closed within 3 bars. 5657 (74.6%) were held more than 10 bars.
Trades that touched neither the 2% stop nor the 4% target within 3 bars: 10844. Their median 20-bar favorable excursion is 0.62% and their median in-trade MAE is 1.33%.

Winners are not evidence of an edge. They are the other half of the same book.

Take-profit winners whose adverse excursion before the target bar reached 1%: 902 of 2410.
Winner median hold 72 bars. Loser median hold 31 bars.

## 14. What the evidence does not support

Gross expectancy is -0.29 and net expectancy is -0.56. The loss is present before fees, slippage, and funding. A cost-only account does not fit this book.
The largest loser label is NO_CLEAR_CAUSE. The other labels are smaller. This phase does not convert the largest label into a new exit.
Shared labels under that bar: RIGHT_DIRECTION_BAD_ENTRY, NO_CLEAR_CAUSE. Sharing a failure mode is not an edge.
IS loser mode is NO_CLEAR_CAUSE. OOS loser mode is NO_CLEAR_CAUSE. The same mode appears in both windows.

## 15. Next research directions

The open question is which of the recorded labels dominates, and whether that dominance is the same in two families and in IS and OOS. Answering it does not require a new stop, a new target, or a new strategy.
Phase 4 still cannot support path labels until an exit time is stored. Funding on that file is a cost fact, not a path fact.
No parameter change is proposed. Paper is off. Live is off. Nothing is VALIDATED_FOR_PAPER.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.
