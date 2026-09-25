# BTC daily MAX

Research replay. The live switch is a separate operator action and is off until a bot is started. The rule is frozen from Quantpedia SSRN 4955617: buy BTC when the daily price is the maximum of the last 10 days, and hold the next day. Thresholds were not searched on this cache.

Coin: BTCUSDT. Clock: one UTC day, the last closed 1h bar of that day. Days in cache: 736. From 2024-09-18 to 2026-09-23.

Signal: today's close is greater than or equal to every close in the previous 9 days, so the 10-day window ending today makes its high today. The book is long the next day and flat otherwise. Long only. No short. The MIN rule is not used.

Cost is charged on the fraction of the book that changes that day, using the same 12 bp and 15 bp figures as the time-series momentum replay. Funding is not in the 1h file and is not invented. Leverage is 1x.

## 10-day high

| Split | Days | Days in market | Mean gross | Mean net 12 bp | Mean net 15 bp | Growth 12 bp | Max drawdown 12 bp |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| IS | 369 | 0.2087 | 0.0003 | 0 | -0 | -0.0098 | 0.2021 |
| VAL | 181 | 0.1215 | 0.0005 | 0.0004 | 0.0004 | 0.0805 | 0.0509 |
| OOS | 185 | 0.2108 | 0.0002 | -0.0001 | -0.0002 | -0.0235 | 0.0888 |

Buy and hold on the same days, with no turnover cost inside the window:

| Split | Growth | Max drawdown |
| --- | ---: | ---: |
| IS | 0.824 | 0.281 |
| VAL | -0.3976 | 0.4956 |
| OOS | 0.2661 | 0.2868 |

## Published horizons, not a search

10, 20, 30, 40, and 50 are the horizons in the paper. The book uses 10. A higher number on this cache does not replace it.

| Days | IS growth 12 bp | VAL growth 12 bp | OOS growth 12 bp | IS mean net 12 bp | VAL mean net 12 bp | OOS mean net 12 bp |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | -0.0098 | 0.0805 | -0.0235 | 0 | 0.0004 | -0.0001 |
| 20 | 0.1073 | 0.0448 | -0.0425 | 0.0003 | 0.0003 | -0.0002 |
| 30 | 0.1664 | 0.0294 | 0.0252 | 0.0005 | 0.0002 | 0.0002 |
| 40 | 0.2556 | 0.0009 | 0.0008 | 0.0007 | 0 | 0 |
| 50 | 0.0922 | 0.0003 | 0.0099 | 0.0003 | 0 | 0.0001 |

The 10-day rule does not stay positive at 12 bp on every window. The operator can still start it. This is not a profit claim.

## Risk

One coin. Long only. Isolated. Leverage 1x. The platform stop is a rail, not the paper's exit. The position closes when the latest closed day is no longer a 10-day high.
