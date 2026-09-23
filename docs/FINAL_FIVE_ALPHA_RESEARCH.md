FINAL FIVE ALPHA RESEARCH COMPLETE

Definition SHA-256 `65d057400c095d49bcb0cc54e3f2913b6c44549ae9bc55f882d89b2533b5dc58`. Survivor manifest SHA-256 `71277aeb75c335db8c8b7f82f6fbef6e84c78a821a566b1db6780ee19f9de813`.

## A. What previous research showed

No prior family was VALIDATED_FOR_PAPER. The frozen five lost money after costs. Wave-2 mechanisms were rejected. Funding and open interest looked better on short windows and failed, or lacked history. Price-action context raised some profit factors and cut the trade count. Sweep-strict and compression continuation reached OOS with thin, concentrated, walk-forward-poor books. Shorts were often the weaker side. Five-trade profit factors were treated as noise.

## B. Hypotheses considered

25 hypotheses were written down. 20 were rejected before any backtest. 5 were implemented. Each implemented rule was also scored on 1m, 3m, 5m, 15m, and 1h. Those extra rows are robustness. They were not allowed to replace the pre-registered primary id.

Multiple testing: twenty-five candidate ids were scored, and five mechanisms were the only ones eligible for the survivor list. A green OOS cell among that set is expected by chance. OOS was not used to change a threshold, a side, or which timeframe is primary.

## C. Eliminated before testing

- `FF-FUNDING-EXHAUST`: Phase 4 funding books that looked strong on a short window failed on the long OOS (profit factor about 0.90 and 0.94).
- `FF-OI-BREAK`: Open-interest history is about 29 days. It is not treated as full-history evidence.
- `FF-TAKER-MOMENTUM`: Taker coverage was missing or the sample was a handful of trades. The series is not fabricated.
- `FF-PURE-RSI`: The frozen RSI book was the least harmful of the five and still had profit factor 0.61. Repeating it adds nothing.
- `FF-EMA-TREND`: The frozen EMA book lost about 40% after costs.
- `FF-DONCHIAN-BOTH`: The frozen Donchian book lost about 89%.
- `FF-VWAP-FADE`: Wave-2 VWAP extension was rejected. OOS profit factor was about 0.63.
- `FF-SWEEP-1M-BOS`: Phase 8 strict sweep collapsed the sample and the walk-forward had 2 trades. The stack is the failure being avoided.
- `FF-COMPRESSION-5M-BOTH`: Already tested. Walk-forward had 2 trades and the short side was not robust. Not copied.
- `FF-SCALP-ADX`: The promising book had 3 in-sample trades and then failed out of sample.
- `FF-RSI-IN-TREND`: That is the mean-reversion failure mode, not a new hypothesis.
- `FF-FLAG-STACK`: Contextual flags lost the sample. ETH validation was negative. Another stack is rejected.
- `FF-MTF-BOS-ALL`: Phase 8 MTF strict failed the in-sample gate and was tiny on BTC, ETH, and BNB.
- `FF-TRIANGLE-RETUNE`: That 1h book stays INSUFFICIENT_DATA. Thresholds are not searched.
- `FF-SHORT-SWEEP`: Published sweep-strict shorts had OOS profit factor 0.74. The side is excluded up front, not after this run.
- `FF-PAIRS`: The pair universe is not loaded. DATA_UNAVAILABLE.
- `FF-LIQUIDATIONS`: Liquidations are not in the cache.
- `FF-ORDER-BOOK`: Order-book history is not available.
- `FF-BTC-FITTED`: It was fit on BTC in-sample. It is not a new pre-registered hypothesis.
- `FF-ROUTER`: A router fit on these outcomes would be a second selection. It is not run.

## D. Pre-OOS gate

Frozen before any OOS read. Applies only to the five primary timeframe ids. Aggregate IS trades >= 30, IS profit factor >= 0.90 or no losses, VALIDATION trades >= 20, VALIDATION profit factor > 1, VALIDATION net > 0, and VALIDATION trades on at least 2 of the 3 symbols. Other timeframes are robustness and cannot replace a primary id. OOS is not an input.

| Id | IS n | IS PF | IS net | VAL n | VAL PF | VAL net | VAL symbols | Passed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| FF-SWEEP-HOURLY|15m | 280 | 0.667 | -381.94 | 72 | 0.765 | -66.69 | 3 | False |
| FF-EXPANSION-BOS|15m | 439 | 0.782 | -386.04 | 115 | 1.217 | 85.89 | 3 | False |
| FF-RSI-QUIET|1h | 34 | 0.483 | -92.50 | 15 | 0.689 | -22.04 | 3 | False |
| FF-FAILED-DOWN|15m | 176 | 0.607 | -292.61 | 50 | 1.059 | 10.03 | 3 | False |
| FF-RANGE-RELEASE|1h | 247 | 0.914 | -85.28 | 90 | 0.797 | -79.32 | 3 | False |

## E. OOS

BASE cost. Drawdown is the worst per-symbol book drawdown in percent of that book's equity. It is not a combined three-coin equity curve. The combined curve is in section I.

| Id | n | PF | Net | Expectancy | Win rate | Long n/PF | Short n/PF | Worst book DD % |
| --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: |
| FF-SWEEP-HOURLY|15m | 69 | 1.508 | 106.32 | 1.54 | 0.478 | 69/1.508 | 0/n/a | 3.38 |
| FF-EXPANSION-BOS|15m | 94 | 0.658 | -132.70 | -1.41 | 0.298 | 62/0.904 | 32/0.299 | 11.62 |
| FF-RSI-QUIET|1h | 5 | 0.855 | -3.32 | -0.66 | 0.400 | 2/1.404 | 3/0.613 | 1.55 |
| FF-FAILED-DOWN|15m | 49 | 1.318 | 49.79 | 1.02 | 0.449 | 49/1.318 | 0/n/a | 3.02 |
| FF-RANGE-RELEASE|1h | 71 | 0.812 | -54.04 | -0.76 | 0.338 | 44/1.121 | 27/0.445 | 8.84 |

Chronological quarters (BLOCK1 through BLOCK4), BASE cost, primary ids. A block is the same rule on an earlier slice of the same series. It is not a new strategy.

| Id | Block | n | PF | Net |
| --- | --- | ---: | ---: | ---: |
| FF-SWEEP-HOURLY|15m | BLOCK1 | 125 | 0.740 | -135.61 |
| FF-SWEEP-HOURLY|15m | BLOCK2 | 112 | 0.535 | -231.05 |
| FF-SWEEP-HOURLY|15m | BLOCK3 | 90 | 0.779 | -78.96 |
| FF-SWEEP-HOURLY|15m | BLOCK4 | 93 | 1.392 | 117.16 |
| FF-EXPANSION-BOS|15m | BLOCK1 | 203 | 0.929 | -56.08 |
| FF-EXPANSION-BOS|15m | BLOCK2 | 181 | 0.699 | -232.25 |
| FF-EXPANSION-BOS|15m | BLOCK3 | 142 | 0.899 | -52.49 |
| FF-EXPANSION-BOS|15m | BLOCK4 | 122 | 0.629 | -189.80 |
| FF-RSI-QUIET|1h | BLOCK1 | 15 | 0.636 | -27.53 |
| FF-RSI-QUIET|1h | BLOCK2 | 15 | 0.484 | -41.42 |
| FF-RSI-QUIET|1h | BLOCK3 | 14 | 0.754 | -15.82 |
| FF-RSI-QUIET|1h | BLOCK4 | 10 | 0.369 | -33.55 |
| FF-FAILED-DOWN|15m | BLOCK1 | 79 | 0.533 | -168.36 |
| FF-FAILED-DOWN|15m | BLOCK2 | 71 | 0.504 | -161.83 |
| FF-FAILED-DOWN|15m | BLOCK3 | 56 | 1.059 | 11.98 |
| FF-FAILED-DOWN|15m | BLOCK4 | 69 | 1.337 | 74.58 |
| FF-RANGE-RELEASE|1h | BLOCK1 | 120 | 1.059 | 28.38 |
| FF-RANGE-RELEASE|1h | BLOCK2 | 83 | 0.800 | -67.12 |
| FF-RANGE-RELEASE|1h | BLOCK3 | 117 | 0.794 | -103.36 |
| FF-RANGE-RELEASE|1h | BLOCK4 | 85 | 0.777 | -77.84 |

## F. Walk-forward

| Id | Windows | n | PF | Net | Expectancy |
| --- | ---: | ---: | ---: | ---: | ---: |
| FF-SWEEP-HOURLY|15m | 24 | 16 | 0.869 | -1.34 | -0.08 |
| FF-EXPANSION-BOS|15m | 24 | 23 | 0.175 | -36.33 | -1.58 |
| FF-RSI-QUIET|1h | 24 | 2 | no-losses | 19.51 | 9.76 |
| FF-FAILED-DOWN|15m | 24 | 10 | 4.498 | 8.76 | 0.88 |
| FF-RANGE-RELEASE|1h | 24 | 16 | 0.261 | -37.89 | -2.37 |

## G. Cost stress

### BASE

| Id | n | PF | Net | Expectancy | Win rate | Long n/PF | Short n/PF | Worst book DD % |
| --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: |
| FF-SWEEP-HOURLY|15m | 69 | 1.508 | 106.32 | 1.54 | 0.478 | 69/1.508 | 0/n/a | 3.38 |
| FF-EXPANSION-BOS|15m | 94 | 0.658 | -132.70 | -1.41 | 0.298 | 62/0.904 | 32/0.299 | 11.62 |
| FF-RSI-QUIET|1h | 5 | 0.855 | -3.32 | -0.66 | 0.400 | 2/1.404 | 3/0.613 | 1.55 |
| FF-FAILED-DOWN|15m | 49 | 1.318 | 49.79 | 1.02 | 0.449 | 49/1.318 | 0/n/a | 3.02 |
| FF-RANGE-RELEASE|1h | 71 | 0.812 | -54.04 | -0.76 | 0.338 | 44/1.121 | 27/0.445 | 8.84 |

### MILD

| Id | n | PF | Net | Expectancy | Win rate | Long n/PF | Short n/PF | Worst book DD % |
| --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: |
| FF-SWEEP-HOURLY|15m | 69 | 1.478 | 101.34 | 1.47 | 0.478 | 69/1.478 | 0/n/a | 3.43 |
| FF-EXPANSION-BOS|15m | 94 | 0.646 | -138.63 | -1.47 | 0.298 | 62/0.888 | 32/0.294 | 11.80 |
| FF-RSI-QUIET|1h | 5 | 0.841 | -3.68 | -0.74 | 0.400 | 2/1.380 | 3/0.603 | 1.55 |
| FF-FAILED-DOWN|15m | 49 | 1.310 | 48.48 | 0.99 | 0.449 | 49/1.310 | 0/n/a | 3.07 |
| FF-RANGE-RELEASE|1h | 71 | 0.798 | -58.76 | -0.83 | 0.338 | 44/1.102 | 27/0.437 | 8.96 |

### HIGH

| Id | n | PF | Net | Expectancy | Win rate | Long n/PF | Short n/PF | Worst book DD % |
| --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: |
| FF-SWEEP-HOURLY|15m | 69 | 1.458 | 97.56 | 1.41 | 0.478 | 69/1.458 | 0/n/a | 3.36 |
| FF-EXPANSION-BOS|15m | 94 | 0.634 | -145.02 | -1.54 | 0.298 | 62/0.871 | 32/0.288 | 11.98 |
| FF-RSI-QUIET|1h | 5 | 0.828 | -4.03 | -0.81 | 0.400 | 2/1.357 | 3/0.594 | 1.56 |
| FF-FAILED-DOWN|15m | 49 | 1.292 | 45.95 | 0.94 | 0.449 | 49/1.292 | 0/n/a | 3.12 |
| FF-RANGE-RELEASE|1h | 71 | 0.784 | -63.42 | -0.89 | 0.338 | 44/1.083 | 27/0.430 | 9.07 |

### STRESS

| Id | n | PF | Net | Expectancy | Win rate | Long n/PF | Short n/PF | Worst book DD % |
| --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: |
| FF-SWEEP-HOURLY|15m | 69 | 1.419 | 90.42 | 1.31 | 0.478 | 69/1.419 | 0/n/a | 3.37 |
| FF-EXPANSION-BOS|15m | 95 | 0.607 | -159.49 | -1.68 | 0.284 | 63/0.825 | 32/0.282 | 12.77 |
| FF-RSI-QUIET|1h | 5 | 0.801 | -4.74 | -0.95 | 0.400 | 2/1.312 | 3/0.576 | 1.57 |
| FF-FAILED-DOWN|15m | 49 | 1.254 | 40.55 | 0.83 | 0.449 | 49/1.254 | 0/n/a | 3.21 |
| FF-RANGE-RELEASE|1h | 71 | 0.756 | -73.27 | -1.03 | 0.338 | 44/1.044 | 27/0.415 | 9.29 |

## H. Symbol robustness

OOS BASE by coin. BNBUSDT was chosen by the pre-existing volume rank, before these numbers existed.

| Id | Coin | OOS n | OOS PF | OOS net |
| --- | --- | ---: | ---: | ---: |
| FF-SWEEP-HOURLY|15m | BTCUSDT | 17 | 0.956 | -2.69 |
| FF-SWEEP-HOURLY|15m | ETHUSDT | 32 | 1.703 | 65.82 |
| FF-SWEEP-HOURLY|15m | BNBUSDT | 20 | 1.782 | 43.19 |
| FF-EXPANSION-BOS|15m | BTCUSDT | 27 | 0.741 | -26.83 |
| FF-EXPANSION-BOS|15m | ETHUSDT | 38 | 0.482 | -88.39 |
| FF-EXPANSION-BOS|15m | BNBUSDT | 29 | 0.846 | -17.49 |
| FF-RSI-QUIET|1h | BTCUSDT | 3 | 3.177 | 13.43 |
| FF-RSI-QUIET|1h | ETHUSDT | 1 | 0.000 | -9.74 |
| FF-RSI-QUIET|1h | BNBUSDT | 1 | 0.000 | -7.01 |
| FF-FAILED-DOWN|15m | BTCUSDT | 12 | 1.415 | 14.60 |
| FF-FAILED-DOWN|15m | ETHUSDT | 21 | 1.018 | 1.41 |
| FF-FAILED-DOWN|15m | BNBUSDT | 16 | 1.757 | 33.78 |
| FF-RANGE-RELEASE|1h | BTCUSDT | 28 | 0.653 | -40.97 |
| FF-RANGE-RELEASE|1h | ETHUSDT | 23 | 1.156 | 13.24 |
| FF-RANGE-RELEASE|1h | BNBUSDT | 20 | 0.688 | -26.32 |

The regime tag on each book is the classification of the first evaluated bar of that window, not a per-trade regime. On these three coins the OOS window mostly opens as LOW_VOLATILITY, so this run does not show trend, range, and high-volatility behavior separately.

## I. Portfolio

```
{
  "TradeCount": 143,
  "Net": 27.68727285,
  "FinalEquity": 1027.68727285,
  "MaximumDrawdownPercent": 11.16128643,
  "SameCoinRejects": 1240,
  "SlotRejects": 45,
  "HeatRejects": 0,
  "Note": "OOS-window signals of the five primary ids on one Isolated book. This replay does not change the survivor list."
}
```

## J. The five strategies

### FF-SWEEP-HOURLY|15m — Hourly-trend sweep reclaim

Label: `INSUFFICIENT_EVIDENCE`. Not VALIDATED_FOR_PAPER.

| Item | Value |
| --- | --- |
| Side | LONG |
| Entry | A completed 15m sweep of a swing low already confirmed at or before that bar, only while the last closed 1h structure bias is up. |
| Confirmation | The 15m sweep confirmation itself. No 1m or 3m BOS is required. |
| Context | Last 1h candle with CloseTime <= the 15m CloseTime has bullish structure bias. |
| Exit | Model B Isolated LOW: next-bar open, 2% stop, 4% target, 0.5% risk, 3x. No exit search. |
| IS | n=280 PF=0.667 net=-381.94 |
| Validation | n=72 PF=0.765 net=-66.69 symbols=3 |
| OOS BASE | n=69 PF=1.508 net=106.32 |
| Difference | Prior sweep-strict added 1m confirmation, collapsed the sample, and failed walk-forward. This keeps one causal hourly filter and drops the stack. Short sweeps are omitted because earlier published sweep books showed the short side weaker. That choice is frozen here and is not revised after this run's OOS. |

### FF-EXPANSION-BOS|15m — Hourly-aligned expansion break

Label: `INSUFFICIENT_EVIDENCE`. Not VALIDATED_FOR_PAPER.

| Item | Value |
| --- | --- |
| Side | WITH_1H |
| Entry | A completed 15m bar whose range is at least ATR(14) and whose body is at least 0.55 of the range, with same-bar BOS. |
| Confirmation | Same-bar causal BOS. No lower-timeframe confirmation. |
| Context | Trade only in the direction of the last closed 1h bias. Flat hourly bias skips the bar. |
| Exit | Model B Isolated LOW book. No exit search. |
| IS | n=439 PF=0.782 net=-386.04 |
| Validation | n=115 PF=1.217 net=85.89 symbols=3 |
| OOS BASE | n=94 PF=0.658 net=-132.70 |
| Difference | 5m compression continuation reached OOS with a thin edge and a two-trade walk-forward. This asks a different question on 15m: does an expansion bar that breaks structure with the hourly trend survive costs, without a three-bar compression count. |

### FF-RSI-QUIET|1h — Quiet-range RSI reclaim

Label: `INSUFFICIENT_EVIDENCE`. Not VALIDATED_FOR_PAPER.

| Item | Value |
| --- | --- |
| Side | BOTH |
| Entry | 1h RSI(14) reclaims 30 from below, or loses 70 from above. |
| Confirmation | The reclaim bar itself. |
| Context | Same 1h bar must have ADX(14) below 20 and ATR(14) percentile below 40 over 50 bars. A trend bar is skipped. |
| Exit | Model B Isolated LOW book. No exit search. |
| IS | n=34 PF=0.483 net=-92.50 |
| Validation | n=15 PF=0.689 net=-22.04 symbols=3 |
| OOS BASE | n=5 PF=0.855 net=-3.32 |
| Difference | Frozen RSI lost money because it faded trends. Wave-2 mean reversion did the same. This keeps the standard 30/70 levels and refuses the trade when the hour is trending or expanding. Thresholds are the existing indicator defaults, not a search. |

### FF-FAILED-DOWN|15m — Failed downside break, long only

Label: `INSUFFICIENT_EVIDENCE`. Not VALIDATED_FOR_PAPER.

| Item | Value |
| --- | --- |
| Side | LONG |
| Entry | A completed 15m failed break of the prior 20-bar low: the break bar closed back above that low. |
| Confirmation | The existing causal failed-breakout confirmation on that 15m close. |
| Context | Last closed 1h bias is not bearish. One filter. No volume stack and no 1m BOS. |
| Exit | Model B Isolated LOW book. No exit search. |
| IS | n=176 PF=0.607 net=-292.61 |
| Validation | n=50 PF=1.059 net=10.03 symbols=3 |
| OOS BASE | n=49 PF=1.318 net=49.79 |
| Difference | Failed-breakout baselines lost because they faded real trends, and the short side was the damage. This takes only the failed downside break, and only when the hour is not already down. |

### FF-RANGE-RELEASE|1h — Quiet-hour range release

Label: `INSUFFICIENT_EVIDENCE`. Not VALIDATED_FOR_PAPER.

| Item | Value |
| --- | --- |
| Side | BREAK |
| Entry | Eight prior completed 1h bars each have range below their own ATR(14). The current completed hour closes beyond that eight-bar high or low and its own range is at least ATR(14). |
| Confirmation | The release close. No triangle geometry and no second timeframe. |
| Context | The eight-bar contraction is the context. There is no hourly overlay on top of the 1h entry. |
| Exit | Model B Isolated LOW book. No exit search. |
| IS | n=247 PF=0.914 net=-85.28 |
| Validation | n=90 PF=0.797 net=-79.32 symbols=3 |
| OOS BASE | n=71 PF=0.812 net=-54.04 |
| Difference | The Phase 7 symmetrical triangle on 1h was the only pattern family with an OOS profit factor above 1, on about 40 trades, and it stays insufficient and is not retuned. This is a fixed eight-bar contraction break, not that triangle. |

## Answers

1. Each strategy exploits one behavior: hourly-aligned sweep reclaim, hourly-aligned expansion, quiet-range RSI reclaim, a failed downside break, or a 1h contraction release.
2. They are not renames. Each drops a stack or a regime that already failed, or moves the question off the 5m book that walk-forward could not support.
3. The failures they address are listed on each definition: cost-eaten trends, RSI in trends, short sweeps, confirmation stacks, and the untuned 1h triangle.
4. Evidence is the IS, validation, OOS, walk-forward, and cost tables above. Empty or tiny samples are not evidence.
5. Anything that misses the pre-OOS gate is weak even if one OOS cell is green.
6. 1m and 3m history in the cache is about 180 days. 5m and 15m are about one year. 1h is about two years. Funding cashflow is not in the Model B replay. Open interest, liquidations, and the order book were not used.
7. Ready for PAPER: no.
8. VALIDATED_FOR_PAPER: no.
9. LIVE allowed: no.

LIVE = OFF. PAPER = OFF. Price Action LIVE = OFF. Scalping LIVE = OFF. No production risk, execution, Isolated margin, or portfolio-risk change. No Binance order was sent by this run.
