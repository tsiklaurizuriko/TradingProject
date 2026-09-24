# Cross-sectional reversal implementation

Research-only. This document does not replace `docs/CROSS_SECTIONAL_ALPHA_RESEARCH.md`.

## Source research

Manifest SHA-256 `fe81edf56d4af31cc0db0a215c09a6245437ad2a66eeb1f6caec11afb0ef247e`.

Ranking version `cross-section-v1`.

The source audit classified `return_15m` and `return_1h` as REPEATABLE. The stable sign is negative: the high recent-return decile underperforms the low recent-return decile. That classification is not a trading edge.

## Feature definitions

Both variants use the BTCUSDT 15-minute clock. A symbol is ranked only when its own closed bar exists at that timestamp and it has at least 96 of its own closed bars.

- `cross_sectional_reversal_return_15m`: `return_15m = close[t] / close[t-1] - 1`
- `cross_sectional_reversal_return_1h`: `return_1h = close[t] / close[t-4] - 1`

`return_4h`, `return_12h`, and `return_24h` are not strategy variants. The audit classified `return_4h` and `return_12h` as OOS_ONLY.

## Universe and ranking

The universe is the existing USD-M USDT perpetual cache, not a hardcoded BTC/ETH/BNB list. BTCDOMUSDT and the stablecoin bases excluded by the audit stay excluded. Membership is the names with a bar at that timestamp. A decile is formed only when at least 30 names are eligible. The tail is `max(1, count / 10)`. Ties break by symbol name, ordinal order.

## Direction

Top decile is a SHORT candidate. Bottom decile is a LONG candidate. The absolute return is not the signal. The percentile rank against the contemporaneous universe is the signal.

The source audit's market-neutral table is the opposite book: long the top decile and short the bottom decile. That measurement remains available for research reproduction. It is not the strategy signal. The published out-of-sample `return_15m` spreads (15m −5.70 bp through 24h −34.03 bp gross, and the matching nets after the Model B round trip) are baseline evidence. They were not used to pick a threshold or a holding period.

## Cost model

Model B, the existing low isolated research book: fee 0.04% and slippage 0.02% per fill. One replaced leg pays 12 bp round trip. No second fee model was added.

## Safety

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.

Both variants are registered as RESEARCHING and are off by default. They are not in the Frozen Five and not in the operator catalog. A single-symbol preview returns no order. Paper and live bot starts for these keys are rejected. Candidates that are reviewed still pass through the existing one-coin occupancy check and the existing risk engine, and none are approved for an order.

## Limitations

The cache is the current USD-M file set, so contracts delisted before it was built are absent. A name with no exit bar is left out of a research hold. Open interest, funding, taker imbalance, basis, and depth were not ranked: local history covers fewer than 30 names, and missing values were not filled in.

The strategy rank waits for 96 of a symbol's own bars, which is the written universe rule. The published spread table ranked a name once the feature lookback itself was finite. Reproducing those exact basis-point figures remains the job of the original audit, which was not rewritten.

## Status

Repeatable cross-sectional reversal factor — not validated for trading.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.
