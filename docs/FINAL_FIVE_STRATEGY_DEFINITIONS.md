# Final five strategy definitions

Definition SHA-256 `65d057400c095d49bcb0cc54e3f2913b6c44549ae9bc55f882d89b2533b5dc58`.

BTCUSDT and ETHUSDT are mandatory. The third symbol is the next name in the pre-existing volume-ranked ScalpingCatalog / Phase7PriceActionUniverse list. That name is BNBUSDT. Strategy profit was not an input. SOLUSDT is the following name and was not substituted.

Frozen before any OOS read. Applies only to the five primary timeframe ids. Aggregate IS trades >= 30, IS profit factor >= 0.90 or no losses, VALIDATION trades >= 20, VALIDATION profit factor > 1, VALIDATION net > 0, and VALIDATION trades on at least 2 of the 3 symbols. Other timeframes are robustness and cannot replace a primary id. OOS is not an input.

These definitions are research signals. They are not in the operator catalog, paper is off, and live is off.

## FF-SWEEP-HOURLY|15m

- Name: Hourly-trend sweep reclaim
- Side rule: LONG
- Entry timeframe: 15m. The same rule is also scored on 1m, 3m, 5m, 15m, and 1h as robustness. A robustness timeframe cannot replace the primary id.
- Signal: A completed 15m sweep of a swing low already confirmed at or before that bar, only while the last closed 1h structure bias is up.
- Confirmation: The 15m sweep confirmation itself. No 1m or 3m BOS is required.
- Context: Last 1h candle with CloseTime <= the 15m CloseTime has bullish structure bias.
- Exit: Model B Isolated LOW: next-bar open, 2% stop, 4% target, 0.5% risk, 3x. No exit search.
- Why this is not a renamed prior strategy: Prior sweep-strict added 1m confirmation, collapsed the sample, and failed walk-forward. This keeps one causal hourly filter and drops the stack. Short sweeps are omitted because earlier published sweep books showed the short side weaker. That choice is frozen here and is not revised after this run's OOS.

## FF-EXPANSION-BOS|15m

- Name: Hourly-aligned expansion break
- Side rule: WITH_1H
- Entry timeframe: 15m. The same rule is also scored on 1m, 3m, 5m, 15m, and 1h as robustness. A robustness timeframe cannot replace the primary id.
- Signal: A completed 15m bar whose range is at least ATR(14) and whose body is at least 0.55 of the range, with same-bar BOS.
- Confirmation: Same-bar causal BOS. No lower-timeframe confirmation.
- Context: Trade only in the direction of the last closed 1h bias. Flat hourly bias skips the bar.
- Exit: Model B Isolated LOW book. No exit search.
- Why this is not a renamed prior strategy: 5m compression continuation reached OOS with a thin edge and a two-trade walk-forward. This asks a different question on 15m: does an expansion bar that breaks structure with the hourly trend survive costs, without a three-bar compression count.

## FF-RSI-QUIET|1h

- Name: Quiet-range RSI reclaim
- Side rule: BOTH
- Entry timeframe: 1h. The same rule is also scored on 1m, 3m, 5m, 15m, and 1h as robustness. A robustness timeframe cannot replace the primary id.
- Signal: 1h RSI(14) reclaims 30 from below, or loses 70 from above.
- Confirmation: The reclaim bar itself.
- Context: Same 1h bar must have ADX(14) below 20 and ATR(14) percentile below 40 over 50 bars. A trend bar is skipped.
- Exit: Model B Isolated LOW book. No exit search.
- Why this is not a renamed prior strategy: Frozen RSI lost money because it faded trends. Wave-2 mean reversion did the same. This keeps the standard 30/70 levels and refuses the trade when the hour is trending or expanding. Thresholds are the existing indicator defaults, not a search.

## FF-FAILED-DOWN|15m

- Name: Failed downside break, long only
- Side rule: LONG
- Entry timeframe: 15m. The same rule is also scored on 1m, 3m, 5m, 15m, and 1h as robustness. A robustness timeframe cannot replace the primary id.
- Signal: A completed 15m failed break of the prior 20-bar low: the break bar closed back above that low.
- Confirmation: The existing causal failed-breakout confirmation on that 15m close.
- Context: Last closed 1h bias is not bearish. One filter. No volume stack and no 1m BOS.
- Exit: Model B Isolated LOW book. No exit search.
- Why this is not a renamed prior strategy: Failed-breakout baselines lost because they faded real trends, and the short side was the damage. This takes only the failed downside break, and only when the hour is not already down.

## FF-RANGE-RELEASE|1h

- Name: Quiet-hour range release
- Side rule: BREAK
- Entry timeframe: 1h. The same rule is also scored on 1m, 3m, 5m, 15m, and 1h as robustness. A robustness timeframe cannot replace the primary id.
- Signal: Eight prior completed 1h bars each have range below their own ATR(14). The current completed hour closes beyond that eight-bar high or low and its own range is at least ATR(14).
- Confirmation: The release close. No triangle geometry and no second timeframe.
- Context: The eight-bar contraction is the context. There is no hourly overlay on top of the 1h entry.
- Exit: Model B Isolated LOW book. No exit search.
- Why this is not a renamed prior strategy: The Phase 7 symmetrical triangle on 1h was the only pattern family with an OOS profit factor above 1, on about 40 trades, and it stays insufficient and is not retuned. This is a fixed eight-bar contraction break, not that triangle.

## Rejected before the run

- `FF-FUNDING-EXHAUST` Funding plus structure exhaustion. Phase 4 funding books that looked strong on a short window failed on the long OOS (profit factor about 0.90 and 0.94).
- `FF-OI-BREAK` Open-interest confirmation of a break. Open-interest history is about 29 days. It is not treated as full-history evidence.
- `FF-TAKER-MOMENTUM` Taker-flow momentum. Taker coverage was missing or the sample was a handful of trades. The series is not fabricated.
- `FF-PURE-RSI` RSI extremes with no regime filter. The frozen RSI book was the least harmful of the five and still had profit factor 0.61. Repeating it adds nothing.
- `FF-EMA-TREND` EMA cross trend. The frozen EMA book lost about 40% after costs.
- `FF-DONCHIAN-BOTH` Donchian break both sides. The frozen Donchian book lost about 89%.
- `FF-VWAP-FADE` VWAP extension fade. Wave-2 VWAP extension was rejected. OOS profit factor was about 0.63.
- `FF-SWEEP-1M-BOS` Sweep plus 1m BOS plus 15m plus volume. Phase 8 strict sweep collapsed the sample and the walk-forward had 2 trades. The stack is the failure being avoided.
- `FF-COMPRESSION-5M-BOTH` 5m three-bar compression continuation, both sides. Already tested. Walk-forward had 2 trades and the short side was not robust. Not copied.
- `FF-SCALP-ADX` 5m ADX scalp. The promising book had 3 in-sample trades and then failed out of sample.
- `FF-RSI-IN-TREND` RSI fade while ADX is high. That is the mean-reversion failure mode, not a new hypothesis.
- `FF-FLAG-STACK` Flag plus displacement plus 3m plus 1h. Contextual flags lost the sample. ETH validation was negative. Another stack is rejected.
- `FF-MTF-BOS-ALL` 1h, 15m, 5m, and 1m BOS together. Phase 8 MTF strict failed the in-sample gate and was tiny on BTC, ETH, and BNB.
- `FF-TRIANGLE-RETUNE` Retune the Phase 7 symmetrical triangle. That 1h book stays INSUFFICIENT_DATA. Thresholds are not searched.
- `FF-SHORT-SWEEP` Short sweeps only. Published sweep-strict shorts had OOS profit factor 0.74. The side is excluded up front, not after this run.
- `FF-PAIRS` Cross-coin relative value. The pair universe is not loaded. DATA_UNAVAILABLE.
- `FF-LIQUIDATIONS` Liquidation cascade. Liquidations are not in the cache.
- `FF-ORDER-BOOK` Depth imbalance. Order-book history is not available.
- `FF-BTC-FITTED` The BTC 15m fitted Bollinger break. It was fit on BTC in-sample. It is not a new pre-registered hypothesis.
- `FF-ROUTER` Route between the five after seeing results. A router fit on these outcomes would be a second selection. It is not run.
