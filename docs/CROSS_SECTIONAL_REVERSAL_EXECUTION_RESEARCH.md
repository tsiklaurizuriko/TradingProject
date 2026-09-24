# Cross-sectional reversal execution research

Model B is unchanged: fee 0.04% per fill, slippage 0.02% per fill. One fully replaced leg is 12 bp round trip. Sensitivity multipliers (1.25x, 1.5x, 2x, 3x) scale that cost. They were not used to pick a holding period.

## What the audit already measured

For `return_15m`, the published non-overlapping book is long the high-return decile and short the low-return decile. Out-of-sample gross was negative at every pre-registered horizon, and net was more negative after the 12 bp round trip. Turnover was about 0.8 of each leg on the short horizons. That book is the measurement portfolio.

The strategy book is the reverse: short the top decile, long the bottom decile. Flipping the sign of a negative measurement does not by itself clear fees, slippage, or turnover. This pass did not promote a horizon or a partial-rebalance rule.

## Variants not promoted

Full rebalance, retention band, hysteresis, and minimum rank change were not frozen and were not scored on OOS.

## Funding

Settled funding on the 527-name universe is DATA_UNAVAILABLE. Missing funding was not forward-filled. Long and short funding were not assumed to offset.

## Break-even

Break-even round-trip cost is gross per hold divided by replaced legs, and only when that gross is positive. A non-positive gross does not have a positive cost budget. The measurement book's out-of-sample gross was negative, so it does not tolerate additional cost.
