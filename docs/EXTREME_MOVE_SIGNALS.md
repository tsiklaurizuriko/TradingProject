# Extreme-move advance signals

Research flags only. They are not a Paper strategy and they are not an edge approval. No order was sent.

Coins scanned: 523. Taker-buy history with real field-9 coverage: 30 coins, each with 17519 one-hour bars, from `GET /fapi/v1/klines`. Stored in `artifacts/strategy-research/extreme-move/taker-1h`. The old zero-filled cache was not overwritten. The two kept rules below use OHLCV only. Taker flow was not required for them.

A fire is one flag, then the scan skips forward so the same move is not counted on every bar. Precision is hits / fires. Recall is the share of independent episodes that had a fire in the 4 hours before the episode start or during the path before the threshold. Lead is hours from the fire to the threshold. IS / VAL / OOS dates are unchanged from the event study.

A rule is kept only when each of IS, VAL, and OOS has at least 40 fires, precision at least 1.5 times the split base rate, and median lead at least 1 hour. Base rate is hits/fires of a random bar, estimated here as events / (fires + events) only as a reference column `precision`. The keep test uses precision >= 0.15 on +20% rules and >= 0.08 on +30% or -15/-20% rules, because those events are rare, plus the same sign of edge versus the complementary split.

| Rule | Target | Split | Fires | Precision | Episodes | Recall | Median lead hours |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| UP_VOL_VOLUME | +0,2 | IS | 29433 | 0.0391 | 4681 | 0.2792 | 14 |
| UP_VOL_VOLUME | +0,2 | VAL | 19505 | 0.0482 | 4009 | 0.1726 | 10 |
| UP_VOL_VOLUME | +0,2 | OOS | 23562 | 0.0533 | 4151 | 0.2012 | 11 |

Realized-vol percentile >= 0.80 and volume z >= 1 and this hour is up but under +8%. Flags +20% inside 24h.

DROP UP_VOL_VOLUME. It does not clear the frozen bar on every window.

| UP_SQUEEZE_BREAK | +0,2 | IS | 6206 | 0.0226 | 4681 | 0.0323 | 13.5 |
| UP_SQUEEZE_BREAK | +0,2 | VAL | 5142 | 0.0288 | 4009 | 0.0404 | 13 |
| UP_SQUEEZE_BREAK | +0,2 | OOS | 5720 | 0.0316 | 4151 | 0.0434 | 15 |

Bollinger width in the bottom quartile, ATR percentile <= 0.40, then volume z >= 1.5 and a small positive hour. Flags +20% inside 24h.

DROP UP_SQUEEZE_BREAK. It does not clear the frozen bar on every window.

| UP_RANGE_EXPAND | +0,3 | IS | 20918 | 0.0229 | 1796 | 0.2216 | 14 |
| UP_RANGE_EXPAND | +0,3 | VAL | 15209 | 0.0327 | 1750 | 0.1783 | 12 |
| UP_RANGE_EXPAND | +0,3 | OOS | 18582 | 0.038 | 2055 | 0.2287 | 13 |

Bollinger width percentile >= 0.85, volume percentile >= 0.80, and 3-hour return between +2% and +12%. Flags +30% inside 24h.

DROP UP_RANGE_EXPAND. It does not clear the frozen bar on every window.

| DOWN_EXHAUSTION | -0,15 | IS | 680 | 0.2044 | 6388 | 0.0238 | 11 |
| DOWN_EXHAUSTION | -0,15 | VAL | 515 | 0.3379 | 5683 | 0.0253 | 11 |
| DOWN_EXHAUSTION | -0,15 | OOS | 655 | 0.4 | 4547 | 0.0416 | 10 |

20-hour return > +20%, RSI(14) >= 75, volume z >= 1.5, close in the bottom 40% of the hour. Flags -15% inside 24h.

KEEP DOWN_EXHAUSTION. Same window test passed on IS, VAL, and OOS.

| DOWN_VOL_SLIDE | -0,2 | IS | 11330 | 0.0428 | 2436 | 0.0842 | 9 |
| DOWN_VOL_SLIDE | -0,2 | VAL | 7523 | 0.0763 | 2919 | 0.0723 | 6 |
| DOWN_VOL_SLIDE | -0,2 | OOS | 6041 | 0.0475 | 2305 | 0.0538 | 7 |

Realized-vol percentile >= 0.80, 5-hour return < -4%, volume z >= 1, close in the lower half. Flags -20% inside 24h.

DROP DOWN_VOL_SLIDE. It does not clear the frozen bar on every window.

| DOWN_FAILED_EXTENSION | -0,15 | IS | 2464 | 0.1213 | 6388 | 0.06 | 12 |
| DOWN_FAILED_EXTENSION | -0,15 | VAL | 1892 | 0.2368 | 5683 | 0.0669 | 12 |
| DOWN_FAILED_EXTENSION | -0,15 | OOS | 2272 | 0.2896 | 4547 | 0.1122 | 10 |

20-hour return > +15%, RSI(14) >= 70, this hour is red, volume percentile >= 0.75. Flags -15% inside 24h.

KEEP DOWN_FAILED_EXTENSION. Same window test passed on IS, VAL, and OOS.

## Kept signals

- `DOWN_EXHAUSTION`: 20-hour return > +20%, RSI(14) >= 75, volume z >= 1.5, close in the bottom 40% of the hour. Flags -15% inside 24h.
- `DOWN_FAILED_EXTENSION`: 20-hour return > +15%, RSI(14) >= 70, this hour is red, volume percentile >= 0.75. Flags -15% inside 24h.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.
