# Time-series momentum

Research replay only. No Paper bot, no live order. The rule is frozen from Han, Kang, and Ryu, SSRN 4675565. Thresholds were not searched on this cache.

Coin: BTCUSDT. Clock: one UTC day, the last closed 1h bar of that day. Days in cache: 736. From 2024-09-18 to 2026-09-23.

Signal: the 28-day close-to-close return is in the top third of every earlier 28-day return on this series. Direction is long only. A signal funds one fifth of the book for each of the next five days, so five signals can fill the book. A day with no live sleeve is flat. The short book in the paper loses and is not used.

Cost is charged on the fraction of the book that changes that day. The paper uses 15 bp. This project's book uses 12 bp round trip. Funding is not in the 1h file and is not invented. Leverage is 1x.

| Split | Days | Days in market | Mean gross | Mean net 12 bp | Mean net 15 bp | Mean log net 12 bp | Growth 12 bp | Max drawdown 12 bp |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| IS | 369 | 0.2981 | 0.0011 | 0.0011 | 0.0011 | 0.0011 | 0.4741 | 0.0699 |
| VAL | 181 | 0.1934 | -0.0006 | -0.0007 | -0.0007 | -0.0007 | -0.1136 | 0.1272 |
| OOS | 185 | 0.5189 | 0.0005 | 0.0004 | 0.0004 | 0.0003 | 0.0664 | 0.0922 |

The path does not stay positive at 12 bp on every window. This is not a reason to place orders.

## What the folder does not put in the book

- Cross-sectional momentum. The same paper liquidates most of those accounts.
- Eight-to-ten-week reversal (SSRN 6703978). It is a different horizon from the 15-minute book already measured here, and this pass does not promote it.
- Pairs (SSRN 6188418). The reported window is seven months, funding is not in the result, and the first live run lost about a third of a 53 dollar book on a stop bug.
- Buying the 10-day low (SSRN 4955617). The out-of-sample section says that mean-reversion leg weakened. The 10-day high is the same family as this long-only trend book and is not a second signal.
- Candles, inside bars, supply-demand zones, and Smart Money from the two handbooks. Those are the same price-action family already marked unstable on this cache.

## Risk

One coin. Long only. Isolated. Leverage 1x. No short. A sleeve is one fifth of the book. Five sleeves are the whole book. There is no stop in the paper, so none is added here. The account is flat when the 28-day return leaves the top third and the open sleeves expire.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.
