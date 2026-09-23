# CONTEXTUAL PRICE ACTION ALPHA

LIVE = OFF. Scalping LIVE = OFF. Price Action LIVE = OFF. PAPER promotion = OFF. VALIDATED_FOR_PAPER = none.
No definition was changed after the survivor manifest. OOS was not used to pick thresholds.

## 1. Objective
Test whether a causal price-action event improves after frozen higher-timeframe context and confirmation, versus the same event alone.

## 2. Phase 7 baseline
Phase 7 tested 162 hypotheses. Descending triangle 5m failed (OOS PF about 0.61). Symmetrical triangle 1h had OOS PF about 1.26 on about 40 trades and remains INSUFFICIENT_DATA. No MTF variant survived. Phase 8 does not retune that triangle and does not repeat the 162-hypothesis grid.
Phase 7 symmetrical triangle 1h stays INSUFFICIENT_DATA. It is not retuned and is not in this hypothesis list.

## 3. Data universe
Coins: BTCUSDT, ETHUSDT, BNBUSDT, SOLUSDT, XRPUSDT, DOGEUSDT, ADAUSDT, AVAXUSDT, LINKUSDT, LTCUSDT, DOTUSDT, NEARUSDT, UNIUSDT, ATOMUSDT, APTUSDT.
Timeframes loaded from the existing ResearchKlineCache: 1m, 3m, 5m, 15m, 1h. Entry timeframe: 5m. No second copy of OHLCV was downloaded.
Phase 7 coverage SHA-256 `7a2b053135af93e5182c2d35eab0d5d3b777f93bba3c081a478570f610083ba4`.
- 1m: coins=15.
- 3m: coins=15.
- 5m: coins=15.
- 15m: coins=15.
- 1h: coins=15.

## 4. Setup definitions
- `CPA-SWEEP|BASELINE|5m`: 5m liquidity sweep of a swing already confirmed at or before the signal bar. Low sweep is long; high sweep is short. Confirmation: No auxiliary timeframe. Context: Same-timeframe sweep only.
- `CPA-SWEEP|CONTEXTUAL|5m`: 5m liquidity sweep of a swing already confirmed at or before the signal bar. Low sweep is long; high sweep is short. Confirmation: Last 3m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias.
- `CPA-SWEEP|STRICT|5m`: 5m liquidity sweep of a swing already confirmed at or before the signal bar. Low sweep is long; high sweep is short. Confirmation: Last 1m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias. Last closed 15m structure bias must agree. Strict only: causal relative volume, lookback 20, must be at least 1. Missing volume stays missing.
- `CPA-FAILED_BREAKOUT|BASELINE|5m`: 5m failed breakout confirmed on the close back inside a level known before the breakout bar. Confirmation: No auxiliary timeframe. Context: Same-timeframe failed breakout only.
- `CPA-FAILED_BREAKOUT|CONTEXTUAL|5m`: 5m failed breakout confirmed on the close back inside a level known before the breakout bar. Confirmation: Last closed 3m candle must print same-side BOS or CHoCH. Context: Last closed 15m bar must still be inside its causal range.
- `CPA-FAILED_BREAKOUT|STRICT|5m`: 5m failed breakout confirmed on the close back inside a level known before the breakout bar. Confirmation: Last closed 1m candle must print same-side BOS or CHoCH. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias. Last closed 15m structure bias must agree. Strict only: causal relative volume, lookback 20, must be at least 1. Missing volume stays missing.
- `CPA-BREAKOUT_RETEST|BASELINE|5m`: 5m breakout-retest confirmed on the retest close. The broken level and the breakout bar are already closed. Confirmation: No auxiliary timeframe. Context: Same-timeframe breakout-retest only.
- `CPA-BREAKOUT_RETEST|CONTEXTUAL|5m`: 5m breakout-retest confirmed on the retest close. The broken level and the breakout bar are already closed. Breakout bar body/range >= 0.55 and range >= ATR(14). Confirmation: Last 3m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias.
- `CPA-BREAKOUT_RETEST|STRICT|5m`: 5m breakout-retest confirmed on the retest close. The broken level and the breakout bar are already closed. Breakout bar body/range >= 0.55 and range >= ATR(14). Confirmation: Last 1m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias. Last closed 15m structure must already be HH+HL or LH+LL in the same direction. Strict only: causal relative volume, lookback 20, must be at least 1. Missing volume stays missing.
- `CPA-PULLBACK|BASELINE|5m`: 5m liquidity sweep that reclaims a swing while the same-timeframe structure bias already agrees. Confirmation: No auxiliary timeframe. Context: Same-timeframe trend bias only.
- `CPA-PULLBACK|CONTEXTUAL|5m`: 5m liquidity sweep that reclaims a swing while the same-timeframe structure bias already agrees. Confirmation: Last 3m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias.
- `CPA-PULLBACK|STRICT|5m`: 5m liquidity sweep that reclaims a swing while the same-timeframe structure bias already agrees. Confirmation: Last 1m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias. Last closed 15m structure must already be HH+HL or LH+LL in the same direction. Strict only: causal relative volume, lookback 20, must be at least 1. Missing volume stays missing.
- `CPA-WM|BASELINE|5m`: W/M known at the second swing confirmation. Baseline does not wait for the neckline. Confirmation: No auxiliary timeframe. Context: Pattern detection only. Neckline break is not required.
- `CPA-WM|CONTEXTUAL|5m`: W/M neckline break on a later completed 5m close. Detection time is not rewritten. Confirmation: Last 3m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias.
- `CPA-WM|STRICT|5m`: W/M neckline break on a later completed 5m close. Detection time is not rewritten. Confirmation: Last 1m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias. Last closed 15m structure bias must agree. Strict only: causal relative volume, lookback 20, must be at least 1. Missing volume stays missing.
- `CPA-FLAG|BASELINE|5m`: Existing causal flag or pennant breakout: impulse, consolidation, and the breakout close. Confirmation: No auxiliary timeframe. Context: Engine breakout only. No extra displacement gate.
- `CPA-FLAG|CONTEXTUAL|5m`: Existing causal flag or pennant breakout: impulse, consolidation, and the breakout close. Breakout bar body/range >= 0.55 and range >= ATR(14). Confirmation: Last 3m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias.
- `CPA-FLAG|STRICT|5m`: Existing causal flag or pennant breakout: impulse, consolidation, and the breakout close. Breakout bar body/range >= 0.55 and range >= ATR(14). Confirmation: Last 1m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias. Last closed 15m structure must already be HH+HL or LH+LL in the same direction. Strict only: causal relative volume, lookback 20, must be at least 1. Missing volume stays missing.
- `CPA-COMPRESSION|CONTINUATION|5m`: At least three prior compressed bars, then an expansion bar. Continuation requires same-side BOS. Reversal requires same-side CHoCH. Confirmation: Same-bar 5m BOS agrees with the expansion candle. Context: No higher timeframe.
- `CPA-COMPRESSION|REVERSAL|5m`: At least three prior compressed bars, then an expansion bar. Continuation requires same-side BOS. Reversal requires same-side CHoCH. Confirmation: Same-bar 5m CHoCH agrees with the expansion candle. Context: No higher timeframe.
- `CPA-COMPRESSION|CONTINUATION_CONTEXT|5m`: At least three prior compressed bars, then an expansion bar. Continuation requires same-side BOS. Reversal requires same-side CHoCH. Confirmation: Same-bar 5m BOS agrees with the expansion candle. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias.
- `CPA-COMPRESSION|REVERSAL_CONTEXT|5m`: At least three prior compressed bars, then an expansion bar. Continuation requires same-side BOS. Reversal requires same-side CHoCH. Confirmation: Same-bar 5m CHoCH agrees with the expansion candle. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias.
- `CPA-MTF|BASELINE|5m`: 5m BOS on the completed candle. Bullish and bearish BOS on the same bar is not a signal. Confirmation: No auxiliary timeframe. Context: 5m BOS only.
- `CPA-MTF|CONTEXTUAL|5m`: 5m BOS on the completed candle. Bullish and bearish BOS on the same bar is not a signal. Confirmation: Last 3m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias. Last closed 15m structure must already be HH+HL or LH+LL in the same direction.
- `CPA-MTF|STRICT|5m`: 5m BOS on the completed candle. Bullish and bearish BOS on the same bar is not a signal. Confirmation: Last 1m candle with CloseTime <= 5m CloseTime must print same-side BOS. Context: Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias. Last closed 15m structure must already be HH+HL or LH+LL in the same direction. Strict only: causal relative volume, lookback 20, must be at least 1. Missing volume stays missing.

## 5. Causal features
Entry bar i uses 5m bars <= i. Confirmation and context use the last candle whose CloseTime <= the 5m CloseTime. Swings are only those confirmed at or before i. Outcomes never enter the signal.
Displacement body/range >= 0.55, range >= ATR(14). Strict relative volume lookback 20, minimum 1. Compression run >= 3 using the existing range median rule. Funding, OI, taker flow, liquidations, order book, and basis are DATA_UNAVAILABLE.

## 6. Hypothesis count
Families: 8. Frozen variants: 25. Pre-OOS manifest SHA-256 `945675e8d7da7e8ebc56989c25d8a9e33f2bb54dcd5e41dc8e77a7c210f19398`. Survivor manifest SHA-256 `c213d95f0d849cc57ea08fad88281fef28e88ef8d6f3b014a526f48d42fade89`. Entered OOS: 2.
Gate: Frozen before OOS: aggregate IS trades >= 30, IS PF >= 0.90 or NoLosses, VALIDATION trades >= 20, VALIDATION PF > 1 and net > 0, and VALIDATION trades on at least 5 symbols. OOS is not an input.

## 7. Baseline vs contextual
| Family | Variant | IS n | IS PF | IS net | VAL n | VAL PF | VAL net | OOS n | OOS PF | OOS net | OOS expectancy | Status |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| SWEEP | BASELINE | 5092 | 0.78768084 | -3755,04 | 1445 | 0.92292531 | -416,75 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| SWEEP | CONTEXTUAL | 35 | 0.35953122 | -104,30 | 88 | 1.14807955 | 44,30 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| SWEEP | STRICT | 34 | 0.92663850 | -9,23 | 81 | 1.25564805 | 67,57 | 78 | 1.06878124 | 18,81 | 0,2412 | RESEARCHING |
| FAILED_BREAKOUT | BASELINE | 4535 | 0.78435497 | -3466,15 | 1272 | 0.85758422 | -686,72 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| FAILED_BREAKOUT | CONTEXTUAL | 16 | 0.38875179 | -45,91 | 35 | 0.97110375 | -3,73 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| FAILED_BREAKOUT | STRICT | 8 | 0.55512106 | -15,63 | 13 | 2.82034230 | 50,29 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| BREAKOUT_RETEST | BASELINE | 4915 | 0.78717703 | -3657,88 | 1493 | 0.62454445 | -2172,98 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| BREAKOUT_RETEST | CONTEXTUAL | 26 | 0.70122070 | -31,69 | 61 | 0.86724586 | -31,35 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| BREAKOUT_RETEST | STRICT | 5 | 0.43633011 | -12,59 | 16 | 1.14342316 | 7,36 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| PULLBACK | BASELINE | 4128 | 0.80737582 | -2819,71 | 1231 | 0.83800120 | -760,46 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| PULLBACK | CONTEXTUAL | 24 | 0.45303820 | -58,84 | 52 | 1.11306041 | 20,84 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| PULLBACK | STRICT | 7 | 2.23189057 | 21,52 | 14 | 2.89153117 | 57,42 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| WM | BASELINE | 4671 | 0.84661545 | -2621,56 | 1379 | 0.83949129 | -842,79 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| WM | CONTEXTUAL | 242 | 0.60811060 | -393,83 | 514 | 1.04402140 | 80,10 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| WM | STRICT | 57 | 0.71179031 | -67,13 | 115 | 1.01576351 | 6,35 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| FLAG | BASELINE | 5528 | 0.78114731 | -4133,05 | 1632 | 0.60605780 | -2493,75 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| FLAG | CONTEXTUAL | 215 | 0.66676062 | -288,28 | 521 | 0.94589418 | -102,69 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| FLAG | STRICT | 26 | 0.62303803 | -41,12 | 43 | 0.87051817 | -20,55 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| COMPRESSION | CONTINUATION | 135 | 1.06835534 | 32,99 | 35 | 1.46873638 | 49,15 | 50 | 1.08205821 | 14,08 | 0,2816 | RESEARCHING |
| COMPRESSION | REVERSAL | 71 | 1.14611563 | 36,10 | 19 | 2.12552040 | 50,51 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| COMPRESSION | CONTINUATION_CONTEXT | 40 | 0.79053825 | -33,61 | 11 | 1.87533894 | 27,39 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| COMPRESSION | REVERSAL_CONTEXT | 27 | 1.11647432 | 11,20 | 4 | 5.54164849 | 24,03 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| MTF | BASELINE | 5435 | 0.77436673 | -4191,30 | 1587 | 0.72802892 | -1615,94 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| MTF | CONTEXTUAL | 145 | 0.54793215 | -278,33 | 406 | 0.94137681 | -88,23 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |
| MTF | STRICT | 39 | 0.62422591 | -59,16 | 76 | 1.03306124 | 8,76 | 0 | N/A | 0,00 | 0,0000 | VALIDATION_FAILED |

A contextual row is ahead of its family baseline only when its OOS expectancy is higher and its OOS trades are at least half of the baseline. Validation numbers are shown when OOS was not opened.
- CPA-SWEEP|CONTEXTUAL|5m vs BASELINE: expectancy 0,5034 vs -0,2884, trades 88 vs 1445. Sample retained: False.
- CPA-SWEEP|STRICT|5m vs BASELINE: expectancy 0,2412 vs -0,2884, trades 78 vs 1445. Sample retained: False.
- CPA-FAILED_BREAKOUT|CONTEXTUAL|5m vs BASELINE: expectancy -0,1065 vs -0,5399, trades 35 vs 1272. Sample retained: False.
- CPA-FAILED_BREAKOUT|STRICT|5m vs BASELINE: expectancy 3,8688 vs -0,5399, trades 13 vs 1272. Sample retained: False.
- CPA-BREAKOUT_RETEST|CONTEXTUAL|5m vs BASELINE: expectancy -0,5140 vs -1,4554, trades 61 vs 1493. Sample retained: False.
- CPA-BREAKOUT_RETEST|STRICT|5m vs BASELINE: expectancy 0,4602 vs -1,4554, trades 16 vs 1493. Sample retained: False.
- CPA-PULLBACK|CONTEXTUAL|5m vs BASELINE: expectancy 0,4007 vs -0,6178, trades 52 vs 1231. Sample retained: False.
- CPA-PULLBACK|STRICT|5m vs BASELINE: expectancy 4,1013 vs -0,6178, trades 14 vs 1231. Sample retained: False.
- CPA-WM|CONTEXTUAL|5m vs BASELINE: expectancy 0,1558 vs -0,6112, trades 514 vs 1379. Sample retained: False.
- CPA-WM|STRICT|5m vs BASELINE: expectancy 0,0553 vs -0,6112, trades 115 vs 1379. Sample retained: False.
- CPA-FLAG|CONTEXTUAL|5m vs BASELINE: expectancy -0,1971 vs -1,5280, trades 521 vs 1632. Sample retained: False.
- CPA-FLAG|STRICT|5m vs BASELINE: expectancy -0,4779 vs -1,5280, trades 43 vs 1632. Sample retained: False.
- CPA-COMPRESSION|REVERSAL|5m vs CONTINUATION: expectancy 2,6583 vs 0,2816, trades 19 vs 50. Sample retained: False.
- CPA-COMPRESSION|CONTINUATION_CONTEXT|5m vs CONTINUATION: expectancy 2,4899 vs 0,2816, trades 11 vs 50. Sample retained: False.
- CPA-COMPRESSION|REVERSAL_CONTEXT|5m vs CONTINUATION: expectancy 6,0087 vs 0,2816, trades 4 vs 50. Sample retained: False.
- CPA-MTF|CONTEXTUAL|5m vs BASELINE: expectancy -0,2173 vs -1,0182, trades 406 vs 1587. Sample retained: False.
- CPA-MTF|STRICT|5m vs BASELINE: expectancy 0,1152 vs -1,0182, trades 76 vs 1587. Sample retained: False.


## 8-12. IS, validation, OOS, walk-forward, cost stress
| Hypothesis | WF windows | WF n | WF PF | WF net | MILD PF/net | HIGH 1.5x PF/net | STRESS PF/net | Top block share |
| --- | ---: | ---: | ---: | ---: | --- | --- | --- | ---: |
| CPA-SWEEP\|STRICT\|5m | 120 | 2 | 0.32313956 | -1,09 | 1.04799498/13,30 | 1.03084305/8,63 | 0.94494431/-16,02 | 98,7% |
| CPA-COMPRESSION\|CONTINUATION\|5m | 120 | 2 | 1.75657720 | 0,49 | 1.06898962/11,90 | 1.04793609/8,38 | 1.00857467/1,54 | 55,1% |

## 13-15. Symbol, side, regime
| Hypothesis | Symbols + / tested | Top-two share | LONG n/PF/net | SHORT n/PF/net | Top regime share |
| --- | ---: | ---: | --- | --- | ---: |
| CPA-SWEEP\|STRICT\|5m | 8 / 15 | 27,3% | 45/1.37390929/53,08 | 33/0.73953469/-34,26 | 0,0% |
| CPA-COMPRESSION\|CONTINUATION\|5m | 7 / 15 | 33,8% | 24/1.31125492/23,28 | 26/0.90501962/-9,20 | 0,0% |

## 16-17. Portfolio occupancy and drawdown
IS contextual signals, before outcomes: max coins with a signal on the same 5m close = 11. Same-coin hypothesis collisions = 362.
OOS survivor occupancy: trades=93, net=45,77, final equity=1045,77, max drawdown=7,06%, same-coin rejects=31, slot rejects=33, heat rejects=0.
Live Portfolio Risk was not modified. This replay is research reporting.

## 18. Multiple-testing caveat
Phase 7 already screened 162 hypotheses. Phase 8 adds 25 more frozen variants across 8 families, 15 coins, and one entry timeframe. A PF above 1 on one slice is an expected false positive at this count. Failed rows stay in the table. OOS was limited to the pre-registered gate. That gate itself is one more selection step and does not make a survivor validated.

## 19. Failed hypotheses
- `CPA-SWEEP|BASELINE|5m`: VALIDATION_FAILED
- `CPA-SWEEP|CONTEXTUAL|5m`: VALIDATION_FAILED
- `CPA-FAILED_BREAKOUT|BASELINE|5m`: VALIDATION_FAILED
- `CPA-FAILED_BREAKOUT|CONTEXTUAL|5m`: VALIDATION_FAILED
- `CPA-FAILED_BREAKOUT|STRICT|5m`: VALIDATION_FAILED
- `CPA-BREAKOUT_RETEST|BASELINE|5m`: VALIDATION_FAILED
- `CPA-BREAKOUT_RETEST|CONTEXTUAL|5m`: VALIDATION_FAILED
- `CPA-BREAKOUT_RETEST|STRICT|5m`: VALIDATION_FAILED
- `CPA-PULLBACK|BASELINE|5m`: VALIDATION_FAILED
- `CPA-PULLBACK|CONTEXTUAL|5m`: VALIDATION_FAILED
- `CPA-PULLBACK|STRICT|5m`: VALIDATION_FAILED
- `CPA-WM|BASELINE|5m`: VALIDATION_FAILED
- `CPA-WM|CONTEXTUAL|5m`: VALIDATION_FAILED
- `CPA-WM|STRICT|5m`: VALIDATION_FAILED
- `CPA-FLAG|BASELINE|5m`: VALIDATION_FAILED
- `CPA-FLAG|CONTEXTUAL|5m`: VALIDATION_FAILED
- `CPA-FLAG|STRICT|5m`: VALIDATION_FAILED
- `CPA-COMPRESSION|REVERSAL|5m`: VALIDATION_FAILED
- `CPA-COMPRESSION|CONTINUATION_CONTEXT|5m`: VALIDATION_FAILED
- `CPA-COMPRESSION|REVERSAL_CONTEXT|5m`: VALIDATION_FAILED
- `CPA-MTF|BASELINE|5m`: VALIDATION_FAILED
- `CPA-MTF|CONTEXTUAL|5m`: VALIDATION_FAILED
- `CPA-MTF|STRICT|5m`: VALIDATION_FAILED

## 20. Interesting hypotheses
None. No hypothesis met the pre-registered interesting bar, and none is VALIDATED_FOR_PAPER.

`CPA-COMPRESSION|CONTINUATION|5m` and `CPA-SWEEP|STRICT|5m` stayed RESEARCHING after the pre-OOS gate, but neither is interesting. Compression walk-forward has 2 trades. Sweep strict puts 98.7% of absolute block net in one chronological block, and its 2.0x cost PF is below 1. Adding 1h/15m/3m context did not keep half of the baseline sample on any family.

## 21. Final status
VALIDATED_FOR_PAPER = none.
RESEARCHING: 2.
Other statuses are listed above. LIVE, Scalping LIVE, Price Action LIVE, and PAPER promotion stayed OFF. Risk Engine, Execution, Isolated margin, Frozen Five, and the Phase 7 hypotheses were not changed.

## Coin-level books
| Coin | Hypothesis | Phase | Cost | n | PF | Net | LONG n/PF | SHORT n/PF | Status |
| --- | --- | --- | --- | ---: | ---: | ---: | --- | --- | --- |
| ADAUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 339 | 0.77134280 | -275,76 | 158/0.55926051 | 181/0.99455689 | RESEARCHING |
| ADAUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 107 | 0.62030084 | -159,95 | 50/0.39439840 | 57/0.86408736 | VALIDATION_FAILED |
| APTUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 467 | 0.78836228 | -343,20 | 235/0.66569233 | 232/0.92536513 | RESEARCHING |
| APTUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 155 | 0.58849948 | -246,59 | 82/0.42935475 | 73/0.79857959 | VALIDATION_FAILED |
| ATOMUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 322 | 0.91127840 | -101,16 | 166/0.85919267 | 156/0.96929197 | RESEARCHING |
| ATOMUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 107 | 0.54342850 | -193,15 | 56/0.47278731 | 51/0.62528559 | VALIDATION_FAILED |
| AVAXUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 344 | 0.79411755 | -249,60 | 161/0.61229447 | 183/0.97390501 | RESEARCHING |
| AVAXUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 107 | 0.51165355 | -212,73 | 51/0.35326496 | 56/0.67454866 | VALIDATION_FAILED |
| BNBUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 203 | 0.70624188 | -220,14 | 100/0.56667859 | 103/0.86027768 | RESEARCHING |
| BNBUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 50 | 0.79942987 | -37,79 | 24/0.73446709 | 26/0.85896303 | VALIDATION_FAILED |
| BTCUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 154 | 0.80466216 | -111,53 | 75/0.65100963 | 79/0.96650803 | RESEARCHING |
| BTCUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 43 | 0.68111465 | -53,83 | 18/0.47728620 | 25/0.86164260 | VALIDATION_FAILED |
| DOGEUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 335 | 0.73585932 | -295,50 | 171/0.66404407 | 164/0.81518540 | RESEARCHING |
| DOGEUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 71 | 0.70394148 | -82,80 | 34/0.36150869 | 37/1.13541539 | VALIDATION_FAILED |
| DOTUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 385 | 0.96598207 | -46,55 | 183/0.87173018 | 202/1.05609054 | RESEARCHING |
| DOTUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 106 | 0.61946714 | -159,30 | 51/0.50424307 | 55/0.74285070 | VALIDATION_FAILED |
| ETHUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 279 | 0.75847351 | -246,45 | 137/0.62838720 | 142/0.89745879 | RESEARCHING |
| ETHUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 64 | 0.68067867 | -83,49 | 26/0.52896141 | 38/0.78879126 | VALIDATION_FAILED |
| LINKUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 336 | 0.69253754 | -349,23 | 164/0.57177021 | 172/0.81592716 | RESEARCHING |
| LINKUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 83 | 0.76502762 | -75,69 | 42/0.68266964 | 41/0.85581935 | VALIDATION_FAILED |
| LTCUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 276 | 0.61297494 | -372,41 | 140/0.54326364 | 136/0.69443990 | RESEARCHING |
| LTCUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 64 | 0.42943375 | -156,95 | 38/0.29988066 | 26/0.67158273 | VALIDATION_FAILED |
| NEARUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 456 | 0.72278114 | -404,78 | 232/0.55987984 | 224/0.91609828 | RESEARCHING |
| NEARUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 235 | 0.53997134 | -391,84 | 126/0.65563651 | 109/0.41516699 | VALIDATION_FAILED |
| SOLUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 320 | 0.93294305 | -78,24 | 150/0.94982757 | 170/0.91868876 | RESEARCHING |
| SOLUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 95 | 0.62189930 | -143,47 | 45/0.58361688 | 50/0.65951746 | VALIDATION_FAILED |
| UNIUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 413 | 0.73728978 | -366,77 | 202/0.61333801 | 211/0.86423436 | RESEARCHING |
| UNIUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 147 | 0.68165273 | -176,72 | 84/0.68413372 | 63/0.67829656 | VALIDATION_FAILED |
| XRPUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | IS | BASE | 286 | 0.81426529 | -196,56 | 134/0.67451520 | 152/0.94943159 | RESEARCHING |
| XRPUSDT | CPA-BREAKOUT_RETEST\|BASELINE\|5m | VALIDATION | BASE | 59 | 1.00628171 | 1,31 | 31/0.67362568 | 28/1.52999936 | RESEARCHING |
| ADAUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 2 | 1.47788877 | 3,15 | 1/0.00000000 | 1/Infinity / NoLosses | IS_PROMISING |
| ADAUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 5.38814607 | 24,03 | 1/0.00000000 | 3/Infinity / NoLosses | RESEARCHING |
| APTUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 3 | 0.83652452 | -1,89 | 2/1.55094780 | 1/0.00000000 | RESEARCHING |
| APTUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.54330865 | -8,19 | 2/1.70224370 | 2/0.00000000 | VALIDATION_FAILED |
| ATOMUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 3 | 0.00000000 | -16,08 | 1/0.00000000 | 2/0.00000000 | RESEARCHING |
| ATOMUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 2 | 1.50856995 | 3,27 | 0/N/A | 2/1.50856995 | RESEARCHING |
| AVAXUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 5.26655060 | 23,85 | 0/N/A | 4/5.26655060 | RESEARCHING |
| BNBUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 0.69608978 | -4,20 | 1/0.00000000 | 2/1.31070503 | VALIDATION_FAILED |
| BTCUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 2 | 0.00000000 | -11,83 | 0/N/A | 2/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 1.54606596 | 6,90 | 1/Infinity / NoLosses | 3/0.77477973 | RESEARCHING |
| DOGEUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 2 | 1.08401589 | 0,48 | 1/Infinity / NoLosses | 1/0.00000000 | IS_PROMISING |
| DOGEUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.00000000 | -22,12 | 3/0.00000000 | 1/0.00000000 | VALIDATION_FAILED |
| DOTUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 2 | 1.70902234 | 4,02 | 1/0.00000000 | 1/Infinity / NoLosses | IS_PROMISING |
| DOTUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.00000000 | -22,31 | 2/0.00000000 | 2/0.00000000 | VALIDATION_FAILED |
| ETHUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -6,47 | 1/0.00000000 | 0/N/A | RESEARCHING |
| ETHUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 0.90066313 | -1,07 | 2/0.00000000 | 1/Infinity / NoLosses | VALIDATION_FAILED |
| LINKUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 2 | 1.66003398 | 3,87 | 1/Infinity / NoLosses | 1/0.00000000 | IS_PROMISING |
| LINKUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.58150913 | -6,90 | 4/0.58150913 | 0/N/A | VALIDATION_FAILED |
| LTCUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -5,31 | 0/N/A | 1/0.00000000 | RESEARCHING |
| LTCUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 10 | 0.70373042 | -12,26 | 9/0.83243934 | 1/0.00000000 | VALIDATION_FAILED |
| NEARUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 3 | 0.85220278 | -1,69 | 2/1.59266554 | 1/0.00000000 | RESEARCHING |
| NEARUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 2 | Infinity / NoLosses | 19,60 | 1/Infinity / NoLosses | 1/Infinity / NoLosses | RESEARCHING |
| SOLUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 5 | 0.35965438 | -17,26 | 4/0.44771276 | 1/0.00000000 | VALIDATION_FAILED |
| UNIUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -5,39 | 0/N/A | 1/0.00000000 | RESEARCHING |
| UNIUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.00000000 | -22,91 | 2/0.00000000 | 2/0.00000000 | VALIDATION_FAILED |
| XRPUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | IS | BASE | 4 | 1.38528771 | 5,43 | 2/1.43393861 | 2/1.33977501 | IS_PROMISING |
| XRPUSDT | CPA-BREAKOUT_RETEST\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 1.72264603 | 8,22 | 3/0.85661872 | 1/Infinity / NoLosses | RESEARCHING |
| ADAUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 2 | Infinity / NoLosses | 19,61 | 0/N/A | 2/Infinity / NoLosses | RESEARCHING |
| APTUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 3 | 3.58057920 | 14,09 | 1/0.00000000 | 2/Infinity / NoLosses | RESEARCHING |
| BNBUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -5,37 | 0/N/A | 1/0.00000000 | VALIDATION_FAILED |
| BTCUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,69 | 0/N/A | 1/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 2 | 1.81158206 | 4,37 | 0/N/A | 2/1.81158206 | RESEARCHING |
| DOGEUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,83 | 0/N/A | 1/0.00000000 | RESEARCHING |
| DOGEUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,33 | 1/0.00000000 | 0/N/A | RESEARCHING |
| DOTUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -5,69 | 1/0.00000000 | 0/N/A | VALIDATION_FAILED |
| ETHUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,48 | 1/0.00000000 | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 0,14 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| LTCUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -6,60 | 1/0.00000000 | 0/N/A | VALIDATION_FAILED |
| SOLUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 4 | 0.59306984 | -6,62 | 0/N/A | 4/0.59306984 | VALIDATION_FAILED |
| UNIUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| UNIUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-BREAKOUT_RETEST\|STRICT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -6,56 | 0/N/A | 1/0.00000000 | VALIDATION_FAILED |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 2 | 1.40065956 | 2,79 | 0/N/A | 2/1.40065956 | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 2 | 0.00000000 | -11,88 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -7,91 | 1/0.00000000 | 0/N/A | VALIDATION_FAILED |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 2 | Infinity / NoLosses | 19,60 | 1/Infinity / NoLosses | 1/Infinity / NoLosses | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 2 | Infinity / NoLosses | 19,60 | 1/Infinity / NoLosses | 1/Infinity / NoLosses | RESEARCHING |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 6 | 0.35607025 | -17,17 | 1/0.00000000 | 5/0.44223051 | RESEARCHING |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 6 | 0.00000000 | -38,30 | 2/0.00000000 | 4/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -5,29 | 0/N/A | 1/0.00000000 | VALIDATION_FAILED |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 4 | 0.00000000 | -22,44 | 3/0.00000000 | 1/0.00000000 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -5,64 | 0/N/A | 1/0.00000000 | VALIDATION_FAILED |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 1 | 0.00000000 | -5,35 | 0/N/A | 1/0.00000000 | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 5 | 1.90519402 | 13,96 | 3/0.63161525 | 2/Infinity / NoLosses | IS_PROMISING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 2 | Infinity / NoLosses | 19,58 | 2/Infinity / NoLosses | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 2 | 1.74246147 | 4,15 | 2/1.74246147 | 0/N/A | IS_PROMISING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 4 | 0.59147746 | -6,70 | 2/0.00000000 | 2/1.79862250 | RESEARCHING |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 4 | 4.69593135 | 23,18 | 3/Infinity / NoLosses | 1/0.00000000 | IS_PROMISING |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 2 | 1.53586715 | 3,40 | 1/0.00000000 | 1/Infinity / NoLosses | IS_PROMISING |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -5,48 | 0/N/A | 1/0.00000000 | VALIDATION_FAILED |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | IS | BASE | 2 | 1.68107447 | 3,93 | 0/N/A | 2/1.68107447 | IS_PROMISING |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 3 | 0.00000000 | -18,31 | 2/0.00000000 | 1/0.00000000 | RESEARCHING |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 2 | 1.81430756 | 4,36 | 0/N/A | 2/1.81430756 | RESEARCHING |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 2 | 0.00000000 | -12,38 | 0/N/A | 2/0.00000000 | RESEARCHING |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 4 | 0.52768372 | -8,73 | 2/0.00000000 | 2/1.31908722 | RESEARCHING |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 1 | 0.00000000 | -5,52 | 0/N/A | 1/0.00000000 | OOS_FAILED |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 1 | 0.00000000 | -5,67 | 0/N/A | 1/0.00000000 | OOS_FAILED |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 1 | 0.00000000 | -5,60 | 0/N/A | 1/0.00000000 | OOS_FAILED |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 1 | 0.00000000 | -5,82 | 0/N/A | 1/0.00000000 | OOS_FAILED |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 3 | 0.79043646 | -2,57 | 0/N/A | 3/0.79043646 | VALIDATION_FAILED |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 2 | 1.18400718 | 1,50 | 2/1.18400718 | 0/N/A | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 3 | 3.06980987 | 13,14 | 0/N/A | 3/3.06980987 | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 1 | 0.00000000 | -5,56 | 1/0.00000000 | 0/N/A | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 7 | 0.63184126 | -11,11 | 3/0.72400313 | 4/0.56036716 | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 6 | 1.44687945 | 9,01 | 3/0.69986833 | 3/3.06980987 | IS_PROMISING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 5 | 1.12185188 | 2,10 | 1/Infinity / NoLosses | 4/0.56036716 | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 5 | 1.08410767 | 1,48 | 1/Infinity / NoLosses | 4/0.54176008 | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 5 | 1.10559893 | 1,83 | 1/Infinity / NoLosses | 4/0.55237384 | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 5 | 1.04275383 | 0,77 | 1/Infinity / NoLosses | 4/0.52133503 | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 2 | 0.00000000 | -13,18 | 2/0.00000000 | 0/N/A | VALIDATION_FAILED |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 2 | Infinity / NoLosses | 19,61 | 0/N/A | 2/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 5 | 2.39084306 | 17,05 | 3/0.80079343 | 2/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 3 | Infinity / NoLosses | 29,55 | 0/N/A | 3/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 4 | 1.59482323 | 7,22 | 3/0.80079343 | 1/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 4 | 1.53666559 | 6,67 | 3/0.77130126 | 1/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 4 | 1.56539367 | 6,95 | 3/0.78586922 | 1/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 4 | 1.48121610 | 6,12 | 3/0.74318443 | 1/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 2 | Infinity / NoLosses | 13,43 | 0/N/A | 2/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 1 | Infinity / NoLosses | 1,15 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 4 | 0.60618291 | -6,34 | 1/0.00000000 | 3/0.90191220 | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 7 | 2.21826979 | 21,48 | 2/Infinity / NoLosses | 5/1.10656502 | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 5 | 2.60155123 | 18,11 | 3/3.47723286 | 2/1.73280704 | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 12 | 1.74064657 | 24,90 | 4/5.56501949 | 8/1.02844350 | IS_PROMISING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 1 | Infinity / NoLosses | 9,63 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 1 | Infinity / NoLosses | 9,69 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 1 | Infinity / NoLosses | 9,51 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 4 | 1.73989817 | 8,29 | 2/1.74704592 | 2/1.73280704 | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 11 | 1.26593363 | 8,69 | 2/1.81193866 | 9/1.15975170 | RESEARCHING |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 3 | 0.00000000 | -17,01 | 1/0.00000000 | 2/0.00000000 | RESEARCHING |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 3 | 0.00000000 | -16,16 | 0/N/A | 3/0.00000000 | RESEARCHING |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 14 | 1.33557180 | 14,81 | 4/1.80953232 | 10/1.18264622 | IS_PROMISING |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 3 | 0.00000000 | -16,16 | 0/N/A | 3/0.00000000 | OOS_FAILED |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 3 | 0.00000000 | -16,61 | 0/N/A | 3/0.00000000 | OOS_FAILED |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 3 | 0.00000000 | -16,38 | 0/N/A | 3/0.00000000 | OOS_FAILED |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 3 | 0.00000000 | -16,86 | 0/N/A | 3/0.00000000 | OOS_FAILED |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 1 | 0.00000000 | -6,23 | 0/N/A | 1/0.00000000 | VALIDATION_FAILED |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 5 | 0.00000000 | -29,44 | 3/0.00000000 | 2/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 13 | 0.96021584 | -2,00 | 4/1.46292809 | 9/0.78184831 | RESEARCHING |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 7 | 2.14628064 | 21,07 | 4/1.52727835 | 3/3.62158329 | RESEARCHING |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 8 | 0.30434279 | -22,94 | 6/0.44999136 | 2/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 23 | 0.83116239 | -15,31 | 11/0.87059131 | 12/0.79491628 | RESEARCHING |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 7 | 0.36304722 | -17,70 | 5/0.59138009 | 2/0.00000000 | OOS_FAILED |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 7 | 0.34394875 | -18,71 | 5/0.56065483 | 2/0.00000000 | OOS_FAILED |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 7 | 0.35337054 | -18,20 | 5/0.57581740 | 2/0.00000000 | OOS_FAILED |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 7 | 0.32582585 | -19,72 | 5/0.53145931 | 2/0.00000000 | OOS_FAILED |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 3 | 0.91188331 | -0,94 | 1/0.00000000 | 2/1.82567841 | VALIDATION_FAILED |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 5 | 0.45201200 | -11,77 | 2/0.00000000 | 3/0.92358599 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 3 | 0.00000000 | -16,98 | 3/0.00000000 | 0/N/A | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 2 | 1.71956405 | 4,06 | 0/N/A | 2/1.71956405 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 2 | 1.42279258 | 2,88 | 0/N/A | 2/1.42279258 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 8 | 0.25371234 | -28,55 | 5/0.00000000 | 3/0.92358599 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 2 | 1.42279258 | 2,88 | 0/N/A | 2/1.42279258 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 2 | 1.37397756 | 2,60 | 0/N/A | 2/1.37397756 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 2 | 1.39811479 | 2,74 | 0/N/A | 2/1.39811479 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 2 | 1.32725549 | 2,33 | 0/N/A | 2/1.32725549 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 2 | 1.71956405 | 4,06 | 0/N/A | 2/1.71956405 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 2 | 0.00000000 | -10,78 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 2 | 0.00000000 | -11,36 | 2/0.00000000 | 0/N/A | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 1 | 0.00000000 | -6,44 | 1/0.00000000 | 0/N/A | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 3 | 3.66044703 | 14,25 | 1/0.00000000 | 2/Infinity / NoLosses | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 5 | 0.00000000 | -28,31 | 4/0.00000000 | 1/0.00000000 | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 3 | 3.66044703 | 14,25 | 1/0.00000000 | 2/Infinity / NoLosses | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 3 | 3.51629533 | 13,85 | 1/0.00000000 | 2/Infinity / NoLosses | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 3 | 3.58737039 | 14,05 | 1/0.00000000 | 2/Infinity / NoLosses | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 3 | 3.37982869 | 13,46 | 1/0.00000000 | 2/Infinity / NoLosses | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 4 | 1.49590141 | 6,49 | 3/3.32837749 | 1/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 11 | 1.70059374 | 24,26 | 5/0.34989049 | 6/7.24460012 | RESEARCHING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 6 | 1.67708225 | 11,82 | 5/2.57645350 | 1/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 6 | 7.53287762 | 42,94 | 4/4.51569216 | 2/Infinity / NoLosses | RESEARCHING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 17 | 1.63934518 | 34,67 | 9/1.16283622 | 8/2.43458442 | IS_PROMISING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 5 | 6.05032783 | 32,87 | 3/3.03314222 | 2/Infinity / NoLosses | RESEARCHING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 5 | 7.15689588 | 33,47 | 3/3.58748381 | 2/Infinity / NoLosses | RESEARCHING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 5 | 7.30437176 | 33,80 | 3/3.66216824 | 2/Infinity / NoLosses | RESEARCHING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 5 | 6.87382920 | 32,80 | 3/3.44414314 | 2/Infinity / NoLosses | RESEARCHING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 5 | 2.58658906 | 17,97 | 5/2.58658906 | 0/N/A | RESEARCHING |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 4 | 1.50761913 | 6,58 | 2/1.43883930 | 2/1.58344261 | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 2 | 1.74246147 | 4,15 | 2/1.74246147 | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 1 | 0.00000000 | -5,43 | 0/N/A | 1/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 6 | 3.52545342 | 28,03 | 4/5.45369295 | 2/1.69477471 | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 7 | 1.21863955 | 5,26 | 4/1.57643199 | 3/0.83724636 | IS_PROMISING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 6 | 3.52545342 | 28,03 | 4/5.45369295 | 2/1.69477471 | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 6 | 3.38691122 | 27,20 | 4/5.23584040 | 2/1.62996448 | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 6 | 3.45525568 | 27,61 | 4/5.34328237 | 2/1.66194478 | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 6 | 3.25549758 | 26,38 | 4/5.02939854 | 2/1.56842449 | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 1 | 0.00000000 | -0,65 | 0/N/A | 1/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 2 | 0.00000000 | -11,85 | 2/0.00000000 | 0/N/A | RESEARCHING |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 3 | 0.86849341 | -1,47 | 2/0.00000000 | 1/Infinity / NoLosses | RESEARCHING |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 5 | 2.73543103 | 18,53 | 3/3.66757993 | 2/1.81428077 | RESEARCHING |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 4 | 0.59211630 | -6,67 | 3/0.89748360 | 1/0.00000000 | RESEARCHING |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 7 | 0.28680241 | -23,84 | 5/0.00000000 | 2/1.80851487 | RESEARCHING |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 3 | 0.89748360 | -1,11 | 3/0.89748360 | 0/N/A | OOS_FAILED |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 3 | 0.86222005 | -1,54 | 3/0.86222005 | 0/N/A | OOS_FAILED |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 3 | 0.87961311 | -1,32 | 3/0.87961311 | 0/N/A | OOS_FAILED |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 3 | 0.82879144 | -1,96 | 3/0.82879144 | 0/N/A | OOS_FAILED |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 4 | 5.15136647 | 23,80 | 2/Infinity / NoLosses | 2/1.71846598 | RESEARCHING |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 1 | 0.00000000 | -5,50 | 1/0.00000000 | 0/N/A | RESEARCHING |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 2 | 1.62517359 | 3,75 | 0/N/A | 2/1.62517359 | RESEARCHING |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 1 | 0.00000000 | -5,58 | 1/0.00000000 | 0/N/A | RESEARCHING |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 4 | 0.00000000 | -22,69 | 1/0.00000000 | 3/0.00000000 | RESEARCHING |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 3 | 0.84583172 | -1,77 | 1/0.00000000 | 2/1.62517359 | RESEARCHING |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 3 | 0.00000000 | -17,22 | 1/0.00000000 | 2/0.00000000 | OOS_FAILED |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 3 | 0.00000000 | -17,67 | 1/0.00000000 | 2/0.00000000 | OOS_FAILED |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 3 | 0.00000000 | -17,44 | 1/0.00000000 | 2/0.00000000 | OOS_FAILED |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 3 | 0.00000000 | -18,11 | 1/0.00000000 | 2/0.00000000 | OOS_FAILED |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 2 | 0.00000000 | -11,12 | 1/0.00000000 | 1/0.00000000 | VALIDATION_FAILED |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 2 | Infinity / NoLosses | 19,58 | 2/Infinity / NoLosses | 0/N/A | RESEARCHING |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 4 | 0.53388955 | -8,51 | 1/Infinity / NoLosses | 3/0.00000000 | RESEARCHING |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 5 | 6.99127580 | 33,77 | 3/3.46446285 | 2/Infinity / NoLosses | RESEARCHING |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 1 | 0.00000000 | -5,84 | 0/N/A | 1/0.00000000 | RESEARCHING |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 8 | 1.61964943 | 15,06 | 5/6.90890893 | 3/0.00000000 | IS_PROMISING |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 1 | 0.00000000 | -5,84 | 0/N/A | 1/0.00000000 | OOS_FAILED |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 1 | 0.00000000 | -6,00 | 0/N/A | 1/0.00000000 | OOS_FAILED |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 1 | 0.00000000 | -5,92 | 0/N/A | 1/0.00000000 | OOS_FAILED |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 1 | 0.00000000 | -6,15 | 0/N/A | 1/0.00000000 | OOS_FAILED |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 3 | Infinity / NoLosses | 29,54 | 1/Infinity / NoLosses | 2/Infinity / NoLosses | RESEARCHING |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 2 | 0.00000000 | -10,83 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 3 | 0.90657781 | -0,99 | 2/0.00000000 | 1/Infinity / NoLosses | RESEARCHING |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 3 | 3.07696221 | 13,19 | 1/0.00000000 | 2/Infinity / NoLosses | RESEARCHING |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 5 | 0.41317401 | -13,52 | 1/Infinity / NoLosses | 4/0.00000000 | RESEARCHING |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 8 | 1.04417467 | 1,22 | 4/0.00000000 | 4/5.27429899 | IS_PROMISING |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 3 | 0.81382014 | -2,20 | 1/Infinity / NoLosses | 2/0.00000000 | OOS_FAILED |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 3 | 0.78284877 | -2,63 | 1/Infinity / NoLosses | 2/0.00000000 | OOS_FAILED |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 3 | 0.79813799 | -2,42 | 1/Infinity / NoLosses | 2/0.00000000 | OOS_FAILED |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 3 | 0.75339177 | -3,07 | 1/Infinity / NoLosses | 2/0.00000000 | OOS_FAILED |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 2 | 0.00000000 | -11,34 | 0/N/A | 2/0.00000000 | VALIDATION_FAILED |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK1 | BASE | 5 | 0.54094471 | -8,28 | 1/0.00000000 | 4/0.78544825 | RESEARCHING |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK2 | BASE | 5 | 1.85058045 | 9,56 | 3/3.45391234 | 2/0.23576989 | RESEARCHING |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK3 | BASE | 3 | 0.83722647 | -1,90 | 2/0.00000000 | 1/Infinity / NoLosses | RESEARCHING |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | BLOCK4 | BASE | 4 | 0.00000000 | -22,48 | 3/0.00000000 | 1/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | IS | BASE | 10 | 1.14789702 | 5,01 | 4/1.73754613 | 6/0.85918391 | IS_PROMISING |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | BASE | 3 | 0.00000000 | -17,26 | 2/0.00000000 | 1/0.00000000 | OOS_FAILED |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | HIGH | 3 | 0.00000000 | -17,70 | 2/0.00000000 | 1/0.00000000 | OOS_FAILED |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | MILD | 3 | 0.00000000 | -17,48 | 2/0.00000000 | 1/0.00000000 | OOS_FAILED |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | OOS | STRESS | 3 | 0.00000000 | -18,15 | 2/0.00000000 | 1/0.00000000 | OOS_FAILED |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | VALIDATION | BASE | 2 | 0.58379344 | -2,55 | 2/0.58379344 | 0/N/A | VALIDATION_FAILED |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-COMPRESSION\|CONTINUATION\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 1 | 0.00000000 | -5,56 | 1/0.00000000 | 0/N/A | RESEARCHING |
| APTUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 2 | Infinity / NoLosses | 19,60 | 1/Infinity / NoLosses | 1/Infinity / NoLosses | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 3 | 0.91547090 | -0,89 | 1/0.00000000 | 2/1.83431170 | RESEARCHING |
| BNBUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 4 | 0.49387151 | -10,00 | 0/N/A | 4/0.49387151 | RESEARCHING |
| BTCUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -5,29 | 0/N/A | 1/0.00000000 | VALIDATION_FAILED |
| DOGEUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 4 | 0.00000000 | -22,44 | 3/0.00000000 | 1/0.00000000 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 1 | 0.00000000 | -5,35 | 0/N/A | 1/0.00000000 | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 3 | 0.63502626 | -5,52 | 2/0.00000000 | 1/Infinity / NoLosses | RESEARCHING |
| ETHUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 2 | Infinity / NoLosses | 19,58 | 2/Infinity / NoLosses | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 2 | 0.00000000 | -11,05 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| LTCUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 3 | Infinity / NoLosses | 29,52 | 3/Infinity / NoLosses | 0/N/A | RESEARCHING |
| SOLUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| UNIUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 2 | 1.53586715 | 3,40 | 1/0.00000000 | 1/Infinity / NoLosses | IS_PROMISING |
| UNIUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| XRPUSDT | CPA-COMPRESSION\|REVERSAL_CONTEXT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 3 | 0.75625676 | -3,14 | 1/0.00000000 | 2/1.31908722 | RESEARCHING |
| ADAUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 2 | 1.73407773 | 4,12 | 2/1.73407773 | 0/N/A | IS_PROMISING |
| APTUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 1 | 0.00000000 | -5,32 | 1/0.00000000 | 0/N/A | VALIDATION_FAILED |
| ATOMUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 2 | Infinity / NoLosses | 13,43 | 0/N/A | 2/Infinity / NoLosses | RESEARCHING |
| AVAXUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 5 | 1.20919307 | 3,37 | 1/Infinity / NoLosses | 4/0.60528827 | IS_PROMISING |
| AVAXUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 5 | 1.21979065 | 3,51 | 3/0.91361683 | 2/1.83431171 | IS_PROMISING |
| BNBUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 1 | 0.00000000 | -6,23 | 0/N/A | 1/0.00000000 | VALIDATION_FAILED |
| BTCUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 15 | 1.04439544 | 2,47 | 8/1.63592620 | 7/0.60731660 | IS_PROMISING |
| BTCUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 3 | 0.91188331 | -0,94 | 1/0.00000000 | 2/1.82567841 | VALIDATION_FAILED |
| DOGEUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 7 | 0.29323849 | -23,39 | 4/0.00000000 | 3/0.92115110 | RESEARCHING |
| DOGEUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 4 | 0.00000000 | -22,32 | 3/0.00000000 | 1/0.00000000 | RESEARCHING |
| DOTUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 9 | 1.76589855 | 21,23 | 7/1.06224731 | 2/Infinity / NoLosses | IS_PROMISING |
| ETHUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 4 | 1.71926667 | 8,15 | 4/1.71926667 | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 3 | 3.53749884 | 14,06 | 1/Infinity / NoLosses | 2/1.76124817 | IS_PROMISING |
| LINKUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 3 | 0.00000000 | -17,56 | 2/0.00000000 | 1/0.00000000 | RESEARCHING |
| LTCUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 4 | 5.15136647 | 23,80 | 2/Infinity / NoLosses | 2/1.71846598 | RESEARCHING |
| NEARUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 1 | 0.00000000 | -5,95 | 0/N/A | 1/0.00000000 | RESEARCHING |
| NEARUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 1 | 0.00000000 | -5,57 | 0/N/A | 1/0.00000000 | VALIDATION_FAILED |
| SOLUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 4 | 5.42989549 | 24,04 | 3/Infinity / NoLosses | 1/0.00000000 | IS_PROMISING |
| SOLUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 2 | Infinity / NoLosses | 19,60 | 1/Infinity / NoLosses | 1/Infinity / NoLosses | RESEARCHING |
| UNIUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 5 | 1.12958195 | 2,22 | 2/0.00000000 | 3/3.53306027 | IS_PROMISING |
| UNIUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-COMPRESSION\|REVERSAL\|5m | IS | BASE | 5 | 6.60521521 | 33,43 | 2/Infinity / NoLosses | 3/3.30960652 | IS_PROMISING |
| XRPUSDT | CPA-COMPRESSION\|REVERSAL\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 3,60 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| ADAUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 321 | 0.68475360 | -357,94 | 164/0.54314621 | 157/0.85561326 | RESEARCHING |
| ADAUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 95 | 0.79481133 | -81,23 | 50/0.66070205 | 45/0.97110786 | VALIDATION_FAILED |
| APTUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 436 | 0.78692687 | -324,35 | 190/0.63242239 | 246/0.92271155 | RESEARCHING |
| APTUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 134 | 0.77085403 | -118,37 | 74/0.51526700 | 60/1.19182923 | VALIDATION_FAILED |
| ATOMUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 303 | 0.77804600 | -243,12 | 164/0.71051612 | 139/0.86592348 | RESEARCHING |
| ATOMUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 86 | 0.93394672 | -20,37 | 43/0.80809914 | 43/1.07439136 | VALIDATION_FAILED |
| AVAXUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 322 | 0.68114527 | -350,94 | 163/0.44826111 | 159/0.98712565 | RESEARCHING |
| AVAXUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 84 | 0.58295171 | -145,57 | 47/0.45668060 | 37/0.77328891 | VALIDATION_FAILED |
| BNBUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 192 | 0.96671166 | -23,99 | 107/0.87993315 | 85/1.08860185 | RESEARCHING |
| BNBUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 54 | 0.76617123 | -48,50 | 21/0.66050093 | 33/0.84052760 | VALIDATION_FAILED |
| BTCUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 138 | 0.75388618 | -131,97 | 70/0.57397158 | 68/0.98396098 | RESEARCHING |
| BTCUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 38 | 0.75756162 | -35,32 | 28/0.63904142 | 10/1.16567218 | VALIDATION_FAILED |
| DOGEUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 318 | 0.72058207 | -305,55 | 163/0.63762441 | 155/0.81361369 | RESEARCHING |
| DOGEUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 72 | 1.14616349 | 38,29 | 39/0.67268005 | 33/2.14026740 | RESEARCHING |
| DOTUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 328 | 0.87760991 | -141,87 | 172/0.72545185 | 156/1.07263967 | RESEARCHING |
| DOTUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 78 | 1.53047740 | 138,15 | 32/1.09124092 | 46/1.93195705 | RESEARCHING |
| ETHUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 256 | 0.77886461 | -211,56 | 128/0.65128003 | 128/0.92358462 | RESEARCHING |
| ETHUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 54 | 1.29035630 | 53,79 | 32/0.81190723 | 22/2.59457309 | RESEARCHING |
| LINKUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 325 | 0.76929401 | -263,93 | 168/0.68970570 | 157/0.86095276 | RESEARCHING |
| LINKUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 74 | 1.29393821 | 74,42 | 32/1.10265844 | 42/1.46576819 | RESEARCHING |
| LTCUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 253 | 0.90944290 | -83,02 | 126/0.75091048 | 127/1.10074491 | RESEARCHING |
| LTCUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 50 | 1.07111410 | 12,46 | 28/0.71994616 | 22/1.70799622 | RESEARCHING |
| NEARUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 406 | 0.61800078 | -498,02 | 198/0.55028695 | 208/0.68711886 | RESEARCHING |
| NEARUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 196 | 0.64266715 | -264,84 | 95/0.76382531 | 101/0.54177171 | VALIDATION_FAILED |
| SOLUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 294 | 0.87223099 | -137,89 | 144/0.80732114 | 150/0.93730224 | RESEARCHING |
| SOLUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 78 | 0.77559542 | -68,13 | 39/0.62586780 | 39/0.94967413 | VALIDATION_FAILED |
| UNIUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 380 | 0.90204015 | -137,31 | 176/0.77526558 | 204/1.01814423 | RESEARCHING |
| UNIUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 116 | 0.70223012 | -138,08 | 49/0.67130585 | 67/0.72605163 | VALIDATION_FAILED |
| XRPUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | IS | BASE | 263 | 0.71947977 | -254,70 | 140/0.55165306 | 123/0.94810969 | RESEARCHING |
| XRPUSDT | CPA-FAILED_BREAKOUT\|BASELINE\|5m | VALIDATION | BASE | 63 | 0.67206077 | -83,43 | 34/0.41341837 | 29/1.07733374 | VALIDATION_FAILED |
| ADAUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 2 | 0.00000000 | -12,34 | 2/0.00000000 | 0/N/A | RESEARCHING |
| ADAUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 3.24244297 | 13,55 | 1/Infinity / NoLosses | 2/1.61434590 | RESEARCHING |
| APTUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -6,27 | 1/0.00000000 | 0/N/A | RESEARCHING |
| APTUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 0.77897837 | -2,76 | 2/1.48802841 | 1/0.00000000 | VALIDATION_FAILED |
| ATOMUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -5,29 | 0/N/A | 1/0.00000000 | RESEARCHING |
| ATOMUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 2 | 0.71847425 | -1,54 | 0/N/A | 2/0.71847425 | VALIDATION_FAILED |
| AVAXUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -5,62 | 1/0.00000000 | 0/N/A | RESEARCHING |
| AVAXUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| BNBUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 0.79250201 | -2,53 | 1/Infinity / NoLosses | 2/0.00000000 | VALIDATION_FAILED |
| BTCUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 3 | 0.00000000 | -16,21 | 1/0.00000000 | 2/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | Infinity / NoLosses | 39,56 | 3/Infinity / NoLosses | 1/Infinity / NoLosses | RESEARCHING |
| DOGEUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 0.00000000 | -17,61 | 2/0.00000000 | 1/0.00000000 | VALIDATION_FAILED |
| DOTUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| DOTUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 2 | 1.60123960 | 3,64 | 2/1.60123960 | 0/N/A | IS_PROMISING |
| ETHUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 2 | 1.52151660 | 3,34 | 1/0.00000000 | 1/Infinity / NoLosses | RESEARCHING |
| LINKUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -6,86 | 1/0.00000000 | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 0.76793448 | -2,95 | 1/0.00000000 | 2/1.78505188 | VALIDATION_FAILED |
| LTCUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -5,33 | 1/0.00000000 | 0/N/A | RESEARCHING |
| LTCUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 2 | 0.00000000 | -12,05 | 1/0.00000000 | 1/0.00000000 | VALIDATION_FAILED |
| NEARUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 2 | 0.00000000 | -11,86 | 2/0.00000000 | 0/N/A | VALIDATION_FAILED |
| SOLUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -5,46 | 0/N/A | 1/0.00000000 | RESEARCHING |
| SOLUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 1.77188969 | 5,92 | 2/1.27030931 | 1/Infinity / NoLosses | RESEARCHING |
| UNIUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -5,67 | 1/0.00000000 | 0/N/A | RESEARCHING |
| XRPUSDT | CPA-FAILED_BREAKOUT\|CONTEXTUAL\|5m | VALIDATION | BASE | 5 | 0.39553168 | -14,81 | 4/0.50342909 | 1/0.00000000 | VALIDATION_FAILED |
| ADAUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| APTUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -6,42 | 1/0.00000000 | 0/N/A | RESEARCHING |
| APTUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 3 | 0.90331180 | -1,04 | 1/0.00000000 | 2/1.81313731 | VALIDATION_FAILED |
| AVAXUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| BNBUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| BNBUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| BTCUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,45 | 1/0.00000000 | 0/N/A | RESEARCHING |
| BTCUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| DOGEUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 2 | 0.00000000 | -10,85 | 2/0.00000000 | 0/N/A | VALIDATION_FAILED |
| LINKUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -6,86 | 1/0.00000000 | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,45 | 0/N/A | 1/0.00000000 | RESEARCHING |
| LTCUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 2 | 1.61513972 | 3,71 | 2/1.61513972 | 0/N/A | RESEARCHING |
| NEARUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| NEARUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| SOLUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,31 | 0/N/A | 1/0.00000000 | RESEARCHING |
| UNIUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,63 | 0/N/A | 1/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-FAILED_BREAKOUT\|STRICT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| ADAUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 373 | 0.94394081 | -76,01 | 176/0.80746366 | 197/1.07987015 | RESEARCHING |
| ADAUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 118 | 0.62383289 | -171,18 | 56/0.47890169 | 62/0.76813174 | VALIDATION_FAILED |
| APTUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 536 | 0.68468423 | -491,49 | 274/0.55362463 | 262/0.83911286 | RESEARCHING |
| APTUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 165 | 0.73355844 | -174,22 | 91/0.58374885 | 74/0.94855619 | VALIDATION_FAILED |
| ATOMUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 371 | 0.72107738 | -338,93 | 180/0.61976235 | 191/0.82641781 | RESEARCHING |
| ATOMUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 115 | 0.64860999 | -151,72 | 66/0.54537932 | 49/0.80211363 | VALIDATION_FAILED |
| AVAXUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 387 | 0.62770384 | -460,20 | 190/0.45581800 | 197/0.82247295 | RESEARCHING |
| AVAXUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 115 | 0.45041596 | -257,48 | 56/0.31247981 | 59/0.59781195 | VALIDATION_FAILED |
| BNBUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 232 | 0.65580532 | -286,94 | 124/0.55094953 | 108/0.79275740 | RESEARCHING |
| BNBUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 52 | 0.40762623 | -132,51 | 24/0.36188556 | 28/0.44699670 | VALIDATION_FAILED |
| BTCUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 163 | 0.93981646 | -35,98 | 79/0.79590137 | 84/1.08773522 | RESEARCHING |
| BTCUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 49 | 0.76489207 | -43,60 | 26/0.51267900 | 23/1.13717187 | VALIDATION_FAILED |
| DOGEUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 367 | 0.91250237 | -117,05 | 173/0.74806837 | 194/1.08133093 | RESEARCHING |
| DOGEUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 89 | 0.53645262 | -167,58 | 42/0.26711919 | 47/0.87515203 | VALIDATION_FAILED |
| DOTUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 426 | 0.82308611 | -261,63 | 202/0.73941963 | 224/0.90286843 | RESEARCHING |
| DOTUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 114 | 0.71796889 | -127,71 | 55/0.49702357 | 59/0.98435326 | VALIDATION_FAILED |
| ETHUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 307 | 0.77625664 | -245,79 | 157/0.64562450 | 150/0.92917908 | RESEARCHING |
| ETHUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 77 | 0.60708613 | -124,42 | 38/0.44868973 | 39/0.77852896 | VALIDATION_FAILED |
| LINKUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 381 | 0.71491911 | -359,72 | 189/0.63126323 | 192/0.80574191 | RESEARCHING |
| LINKUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 104 | 0.59611923 | -164,42 | 51/0.50350989 | 53/0.69726144 | VALIDATION_FAILED |
| LTCUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 297 | 0.77115839 | -242,99 | 152/0.67854847 | 145/0.87472252 | RESEARCHING |
| LTCUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 53 | 0.86425531 | -26,85 | 23/0.60057614 | 30/1.10691148 | VALIDATION_FAILED |
| NEARUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 507 | 0.83090689 | -288,43 | 242/0.74977892 | 265/0.90836242 | RESEARCHING |
| NEARUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 273 | 0.56382036 | -414,90 | 132/0.67663913 | 141/0.46674283 | VALIDATION_FAILED |
| SOLUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 396 | 0.75332034 | -335,73 | 193/0.61854575 | 203/0.89780220 | RESEARCHING |
| SOLUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 88 | 0.58025793 | -145,59 | 45/0.46626036 | 43/0.71931990 | VALIDATION_FAILED |
| UNIUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 486 | 0.79329228 | -357,80 | 227/0.68106149 | 259/0.90038546 | RESEARCHING |
| UNIUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 155 | 0.57657961 | -258,16 | 83/0.61972690 | 72/0.52692823 | VALIDATION_FAILED |
| XRPUSDT | CPA-FLAG\|BASELINE\|5m | IS | BASE | 299 | 0.77709257 | -234,38 | 140/0.64451829 | 159/0.90801817 | RESEARCHING |
| XRPUSDT | CPA-FLAG\|BASELINE\|5m | VALIDATION | BASE | 65 | 0.50260242 | -133,41 | 37/0.38659951 | 28/0.68039847 | VALIDATION_FAILED |
| ADAUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 11 | 0.64505003 | -16,07 | 4/1.67604388 | 7/0.28942912 | RESEARCHING |
| ADAUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 35 | 1.02073483 | 2,57 | 14/0.72010139 | 21/1.25352149 | RESEARCHING |
| APTUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 19 | 0.78329032 | -15,84 | 9/0.84866603 | 10/0.72753853 | RESEARCHING |
| APTUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 56 | 0.80505095 | -41,41 | 27/0.83118478 | 29/0.78016430 | VALIDATION_FAILED |
| ATOMUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 12 | 0.40485704 | -30,05 | 7/0.66227185 | 5/0.06233109 | RESEARCHING |
| ATOMUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 26 | 1.65285984 | 49,99 | 9/0.49510183 | 17/2.84962419 | RESEARCHING |
| AVAXUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 16 | 0.61555357 | -24,13 | 10/0.73413330 | 6/0.41493584 | RESEARCHING |
| AVAXUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 44 | 1.10714905 | 16,90 | 21/0.66160992 | 23/1.67514551 | RESEARCHING |
| BNBUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 7 | 1.95653860 | 17,00 | 3/2.91847539 | 4/1.37539198 | IS_PROMISING |
| BNBUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 32 | 1.06704144 | 7,40 | 15/1.24205621 | 17/0.93586090 | RESEARCHING |
| BTCUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 12 | 0.95906016 | -1,67 | 6/2.52612171 | 6/0.33407379 | RESEARCHING |
| BTCUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 23 | 1.03281383 | 2,66 | 8/0.81887104 | 15/1.16569219 | RESEARCHING |
| DOGEUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 18 | 1.01174323 | 0,77 | 14/1.58371169 | 4/0.00000000 | IS_PROMISING |
| DOGEUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 30 | 0.63776095 | -43,13 | 11/0.00000000 | 19/1.18897529 | VALIDATION_FAILED |
| DOTUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 21 | 0.53218799 | -41,25 | 12/0.33839194 | 9/0.86431692 | RESEARCHING |
| DOTUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 31 | 0.51207600 | -63,69 | 10/0.42578447 | 21/0.55875004 | VALIDATION_FAILED |
| ETHUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 16 | 0.93004077 | -4,38 | 7/1.26911049 | 9/0.73600379 | RESEARCHING |
| ETHUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 29 | 0.78979825 | -22,94 | 11/0.72528727 | 18/0.82667166 | VALIDATION_FAILED |
| LINKUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 16 | 0.23903280 | -62,21 | 6/0.31376422 | 10/0.19293506 | RESEARCHING |
| LINKUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 39 | 0.66761782 | -51,61 | 19/0.78002120 | 20/0.56766263 | VALIDATION_FAILED |
| LTCUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 8 | 1.00677113 | 0,20 | 4/0.56895781 | 4/1.63506620 | IS_PROMISING |
| LTCUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 26 | 2.58125253 | 93,41 | 13/1.69070050 | 13/3.97952428 | RESEARCHING |
| NEARUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 11 | 1.07194884 | 2,63 | 7/2.17746874 | 4/0.00000000 | IS_PROMISING |
| NEARUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 47 | 0.81021355 | -34,09 | 25/0.63982294 | 22/1.05504217 | VALIDATION_FAILED |
| SOLUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 17 | 0.21636135 | -68,63 | 10/0.18450016 | 7/0.25841451 | RESEARCHING |
| SOLUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 32 | 1.13293960 | 15,26 | 17/1.48046657 | 15/0.82636739 | RESEARCHING |
| UNIUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 15 | 1.14863813 | 7,65 | 5/1.11530230 | 10/1.16623302 | IS_PROMISING |
| UNIUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 39 | 0.91908860 | -12,04 | 14/1.52051759 | 25/0.65814514 | VALIDATION_FAILED |
| XRPUSDT | CPA-FLAG\|CONTEXTUAL\|5m | IS | BASE | 16 | 0.27070854 | -52,30 | 6/0.34878140 | 10/0.22531804 | RESEARCHING |
| XRPUSDT | CPA-FLAG\|CONTEXTUAL\|5m | VALIDATION | BASE | 32 | 0.81597677 | -21,97 | 20/0.45859145 | 12/1.68914620 | VALIDATION_FAILED |
| ADAUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 3 | 0.00000000 | -16,55 | 1/0.00000000 | 2/0.00000000 | RESEARCHING |
| ADAUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 3 | 3.58230499 | 14,13 | 0/N/A | 3/3.58230499 | RESEARCHING |
| APTUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,34 | 0/N/A | 1/0.00000000 | RESEARCHING |
| APTUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 6 | 0.31265960 | -20,91 | 3/0.79540694 | 3/0.00000000 | VALIDATION_FAILED |
| ATOMUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| ATOMUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 2 | 0.00000000 | -11,15 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| AVAXUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 4 | 0.52485400 | -8,78 | 1/0.00000000 | 3/0.74541816 | VALIDATION_FAILED |
| BNBUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 2 | 1.75283239 | 4,16 | 1/Infinity / NoLosses | 1/0.00000000 | IS_PROMISING |
| BNBUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 4 | 3.84461423 | 15,64 | 2/Infinity / NoLosses | 2/0.30213454 | RESEARCHING |
| BTCUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 3 | 0.00000000 | -16,66 | 1/0.00000000 | 2/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 2 | 0.00000000 | -10,89 | 1/0.00000000 | 1/0.00000000 | VALIDATION_FAILED |
| DOGEUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,62 | 0/N/A | 1/0.00000000 | RESEARCHING |
| DOGEUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 4 | 0.00000000 | -23,19 | 2/0.00000000 | 2/0.00000000 | VALIDATION_FAILED |
| DOTUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 2 | 1.82304546 | 4,38 | 1/0.00000000 | 1/Infinity / NoLosses | IS_PROMISING |
| DOTUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,96 | 1/0.00000000 | 0/N/A | RESEARCHING |
| ETHUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -5,41 | 1/0.00000000 | 0/N/A | VALIDATION_FAILED |
| LINKUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 4 | 4.51806826 | 22,90 | 2/1.49667874 | 2/Infinity / NoLosses | RESEARCHING |
| LTCUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 2 | 1.72427287 | 4,10 | 1/0.00000000 | 1/Infinity / NoLosses | IS_PROMISING |
| LTCUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 4 | 0.77709204 | -2,80 | 3/0.00000000 | 1/Infinity / NoLosses | VALIDATION_FAILED |
| NEARUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 2 | 1.37722490 | 2,67 | 1/Infinity / NoLosses | 1/0.00000000 | IS_PROMISING |
| NEARUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 3 | 3.17740762 | 13,43 | 2/1.59544149 | 1/Infinity / NoLosses | RESEARCHING |
| SOLUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 2 | 1.77123347 | 4,25 | 0/N/A | 2/1.77123347 | RESEARCHING |
| UNIUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 4 | 0.53821807 | -8,26 | 2/1.59287623 | 2/0.00000000 | RESEARCHING |
| UNIUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 2 | 0.00000000 | -11,03 | 0/N/A | 2/0.00000000 | VALIDATION_FAILED |
| XRPUSDT | CPA-FLAG\|STRICT\|5m | IS | BASE | 2 | 1.53300586 | 3,37 | 0/N/A | 2/1.53300586 | IS_PROMISING |
| XRPUSDT | CPA-FLAG\|STRICT\|5m | VALIDATION | BASE | 4 | 0.55084066 | -7,90 | 2/1.70510236 | 2/0.00000000 | VALIDATION_FAILED |
| ADAUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 383 | 0.81711161 | -238,58 | 192/0.66013171 | 191/0.99398927 | RESEARCHING |
| ADAUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 123 | 0.65006222 | -168,18 | 57/0.47659761 | 66/0.82671176 | VALIDATION_FAILED |
| APTUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 535 | 0.70590969 | -498,04 | 246/0.57204115 | 289/0.83129420 | RESEARCHING |
| APTUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 171 | 0.70286687 | -189,72 | 82/0.58948354 | 89/0.82148603 | VALIDATION_FAILED |
| ATOMUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 355 | 0.70514463 | -351,38 | 176/0.62043863 | 179/0.79613985 | RESEARCHING |
| ATOMUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 113 | 0.57666297 | -184,64 | 61/0.44973096 | 52/0.75003937 | VALIDATION_FAILED |
| AVAXUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 374 | 0.74417149 | -328,61 | 196/0.56136343 | 178/0.99121655 | RESEARCHING |
| AVAXUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 98 | 0.89589268 | -35,47 | 48/0.75110728 | 50/1.05155759 | VALIDATION_FAILED |
| BNBUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 209 | 0.71855995 | -222,04 | 102/0.56047061 | 107/0.90075684 | RESEARCHING |
| BNBUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 54 | 0.58999958 | -90,93 | 26/0.51237250 | 28/0.66546634 | VALIDATION_FAILED |
| BTCUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 160 | 0.94659257 | -31,33 | 77/0.85680965 | 83/1.03728448 | RESEARCHING |
| BTCUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 42 | 0.63136455 | -61,73 | 19/0.34794939 | 23/0.90679235 | VALIDATION_FAILED |
| DOGEUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 351 | 0.84881841 | -185,20 | 176/0.69524556 | 175/1.02292039 | RESEARCHING |
| DOGEUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 73 | 0.67577496 | -93,88 | 36/0.40952147 | 37/1.00243195 | VALIDATION_FAILED |
| DOTUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 437 | 0.90958362 | -141,73 | 213/0.75005317 | 224/1.08308902 | RESEARCHING |
| DOTUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 110 | 0.73509992 | -113,27 | 57/0.53276066 | 53/0.99868864 | VALIDATION_FAILED |
| ETHUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 312 | 0.70636188 | -315,23 | 174/0.55807715 | 138/0.92291287 | RESEARCHING |
| ETHUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 65 | 0.79493819 | -51,17 | 32/0.63529352 | 33/0.97700967 | VALIDATION_FAILED |
| LINKUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 352 | 0.72864132 | -336,38 | 173/0.62587749 | 179/0.83703404 | RESEARCHING |
| LINKUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 76 | 0.93029103 | -19,84 | 37/0.83346409 | 39/1.02754226 | VALIDATION_FAILED |
| LTCUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 299 | 0.82860367 | -188,84 | 148/0.70630611 | 151/0.96420123 | RESEARCHING |
| LTCUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 55 | 0.89533363 | -21,03 | 28/0.68847414 | 27/1.14802705 | VALIDATION_FAILED |
| NEARUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 550 | 0.68904727 | -523,12 | 283/0.59846665 | 267/0.79181992 | RESEARCHING |
| NEARUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 293 | 0.63676367 | -380,77 | 149/0.65696443 | 144/0.61562132 | VALIDATION_FAILED |
| SOLUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 378 | 0.71392232 | -351,81 | 188/0.59649236 | 190/0.84209581 | RESEARCHING |
| SOLUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 90 | 0.96973562 | -9,90 | 53/0.86117877 | 37/1.14442458 | VALIDATION_FAILED |
| UNIUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 441 | 0.75262433 | -373,87 | 211/0.62870288 | 230/0.88006788 | RESEARCHING |
| UNIUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 167 | 0.68775422 | -194,79 | 82/0.68975269 | 85/0.68584324 | VALIDATION_FAILED |
| XRPUSDT | CPA-MTF\|BASELINE\|5m | IS | BASE | 299 | 0.90400164 | -105,11 | 136/0.84490981 | 163/0.95551841 | RESEARCHING |
| XRPUSDT | CPA-MTF\|BASELINE\|5m | VALIDATION | BASE | 57 | 0.99693966 | -0,63 | 36/0.72828832 | 21/1.68663759 | VALIDATION_FAILED |
| ADAUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 9 | 0.00000000 | -49,26 | 6/0.00000000 | 3/0.00000000 | RESEARCHING |
| ADAUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 31 | 0.81302419 | -21,75 | 13/0.51518471 | 18/1.08087565 | VALIDATION_FAILED |
| APTUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 12 | 0.86901055 | -5,86 | 7/1.29791087 | 5/0.43750566 | RESEARCHING |
| APTUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 43 | 1.01361457 | 2,03 | 18/1.37978574 | 25/0.79895630 | RESEARCHING |
| ATOMUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 10 | 0.75079413 | -9,57 | 4/0.58049001 | 6/0.87916816 | RESEARCHING |
| ATOMUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 17 | 0.46348931 | -42,92 | 6/0.66445693 | 11/0.35585881 | VALIDATION_FAILED |
| AVAXUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 10 | 0.00000000 | -51,00 | 5/0.00000000 | 5/0.00000000 | RESEARCHING |
| AVAXUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 39 | 0.73643952 | -40,15 | 18/0.21049775 | 21/1.48271592 | VALIDATION_FAILED |
| BNBUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 8 | 1.53118224 | 12,10 | 4/1.67664786 | 4/1.37686080 | IS_PROMISING |
| BNBUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 26 | 0.78081517 | -22,06 | 11/0.58639292 | 15/0.96409827 | VALIDATION_FAILED |
| BTCUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 8 | 0.54356180 | -16,34 | 4/1.51906010 | 4/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 18 | 1.37895312 | 21,71 | 8/0.58229334 | 10/2.51908561 | RESEARCHING |
| DOGEUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 13 | 0.69972804 | -16,38 | 6/1.41192902 | 7/0.27890809 | RESEARCHING |
| DOGEUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 26 | 1.75184766 | 55,43 | 11/0.75515447 | 15/2.93987569 | RESEARCHING |
| DOTUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 9 | 0.47609894 | -20,94 | 5/0.42480114 | 4/0.54152559 | RESEARCHING |
| DOTUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 12 | 0.55266850 | -23,30 | 8/0.23184994 | 4/1.81677812 | VALIDATION_FAILED |
| ETHUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 9 | 0.20751538 | -36,89 | 2/1.72965515 | 7/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 21 | 0.46361035 | -54,95 | 10/0.34560863 | 11/0.59982056 | VALIDATION_FAILED |
| LINKUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 11 | 0.38402000 | -30,88 | 4/1.61566932 | 7/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 24 | 1.31547019 | 23,76 | 9/2.88249095 | 15/0.85114824 | RESEARCHING |
| LTCUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 8 | 0.99553031 | -0,13 | 3/0.89511939 | 5/1.05460611 | RESEARCHING |
| LTCUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 26 | 1.31569490 | 25,99 | 14/1.00721098 | 12/1.76835767 | RESEARCHING |
| NEARUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 6 | 1.68802341 | 11,92 | 3/3.26476797 | 3/0.86035616 | IS_PROMISING |
| NEARUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 38 | 0.87852975 | -18,88 | 23/1.09040716 | 15/0.59012444 | VALIDATION_FAILED |
| SOLUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 9 | 0.20732618 | -36,16 | 3/0.88817792 | 6/0.00000000 | RESEARCHING |
| SOLUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 25 | 0.52743408 | -52,55 | 13/0.50536454 | 12/0.55158173 | VALIDATION_FAILED |
| UNIUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 11 | 1.46425797 | 15,63 | 5/1.17183410 | 6/1.76337538 | IS_PROMISING |
| UNIUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 29 | 1.30093028 | 29,65 | 15/1.33342499 | 14/1.26482579 | RESEARCHING |
| XRPUSDT | CPA-MTF\|CONTEXTUAL\|5m | IS | BASE | 12 | 0.20967424 | -44,57 | 4/0.58389298 | 8/0.06021734 | RESEARCHING |
| XRPUSDT | CPA-MTF\|CONTEXTUAL\|5m | VALIDATION | BASE | 31 | 1.30336902 | 29,77 | 18/0.70527795 | 13/2.76612084 | RESEARCHING |
| ADAUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 2 | 0.00000000 | -11,82 | 2/0.00000000 | 0/N/A | RESEARCHING |
| ADAUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 7 | 2.32465366 | 22,39 | 5/1.16331654 | 2/Infinity / NoLosses | RESEARCHING |
| APTUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 3 | 0.88872990 | -1,21 | 2/1.75214748 | 1/0.00000000 | RESEARCHING |
| APTUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 8 | 0.24237338 | -29,60 | 4/0.53761540 | 4/0.00000000 | VALIDATION_FAILED |
| ATOMUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 2 | 1.84058170 | 4,43 | 1/0.00000000 | 1/Infinity / NoLosses | IS_PROMISING |
| ATOMUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 4 | 0.58405597 | -6,87 | 2/0.00000000 | 2/1.77495096 | VALIDATION_FAILED |
| AVAXUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 2 | 0.00000000 | -10,72 | 2/0.00000000 | 0/N/A | RESEARCHING |
| AVAXUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 4 | 1.79683105 | 8,67 | 2/0.00000000 | 2/Infinity / NoLosses | RESEARCHING |
| BNBUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 3 | 0.00000000 | -16,23 | 1/0.00000000 | 2/0.00000000 | RESEARCHING |
| BNBUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 5 | 0.42212271 | -13,12 | 3/0.00000000 | 2/1.79641706 | VALIDATION_FAILED |
| BTCUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 4 | 0.05074316 | -15,85 | 3/0.00000000 | 1/Infinity / NoLosses | RESEARCHING |
| BTCUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 4 | 0.58341104 | -6,93 | 2/0.00000000 | 2/1.67276139 | VALIDATION_FAILED |
| DOGEUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 5 | 2.52992769 | 17,85 | 3/Infinity / NoLosses | 2/0.00000000 | IS_PROMISING |
| DOGEUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 5 | 0.43135238 | -12,71 | 2/0.00000000 | 3/0.90834215 | VALIDATION_FAILED |
| DOTUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 3 | 0.86280468 | -1,54 | 1/0.00000000 | 2/1.72790901 | RESEARCHING |
| DOTUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 4 | 0.55367211 | -7,78 | 1/0.00000000 | 3/0.80912495 | VALIDATION_FAILED |
| ETHUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 2 | 0.00000000 | -11,10 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 2 | 0.00000000 | -10,82 | 2/0.00000000 | 0/N/A | VALIDATION_FAILED |
| LINKUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 5 | Infinity / NoLosses | 49,72 | 2/Infinity / NoLosses | 3/Infinity / NoLosses | RESEARCHING |
| LTCUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 4 | 0.55047491 | -7,91 | 1/0.00000000 | 3/0.80033619 | RESEARCHING |
| LTCUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 6 | 0.42058542 | -13,42 | 5/0.55305041 | 1/0.00000000 | VALIDATION_FAILED |
| NEARUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 2 | 1.70344275 | 4,00 | 1/Infinity / NoLosses | 1/0.00000000 | IS_PROMISING |
| NEARUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 4 | 0.55064047 | -7,86 | 3/0.79915320 | 1/0.00000000 | VALIDATION_FAILED |
| SOLUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 3 | 0.00000000 | -17,10 | 0/N/A | 3/0.00000000 | RESEARCHING |
| SOLUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 4 | 1.68036704 | 7,93 | 2/1.76030950 | 2/1.60812909 | RESEARCHING |
| UNIUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 2 | 1.58998445 | 3,62 | 1/Infinity / NoLosses | 1/0.00000000 | IS_PROMISING |
| UNIUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 8 | 1.03688626 | 1,04 | 1/0.00000000 | 7/1.28854125 | RESEARCHING |
| XRPUSDT | CPA-MTF\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,33 | 0/N/A | 1/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-MTF\|STRICT\|5m | VALIDATION | BASE | 6 | 3.53990791 | 28,12 | 3/3.45115514 | 3/3.63349279 | RESEARCHING |
| ADAUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 297 | 0.85968927 | -152,22 | 133/0.62162316 | 164/1.08782570 | RESEARCHING |
| ADAUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 87 | 0.82262018 | -59,74 | 40/0.53937491 | 47/1.12859724 | VALIDATION_FAILED |
| APTUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 365 | 0.77509155 | -280,95 | 177/0.69738964 | 188/0.85651486 | RESEARCHING |
| APTUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 115 | 0.70626137 | -132,45 | 58/0.55677959 | 57/0.87835463 | VALIDATION_FAILED |
| ATOMUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 279 | 0.75916964 | -234,33 | 126/0.63933947 | 153/0.86829150 | RESEARCHING |
| ATOMUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 82 | 1.03988518 | 11,29 | 41/0.85161980 | 41/1.26405666 | RESEARCHING |
| AVAXUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 298 | 0.64739700 | -361,03 | 145/0.45020339 | 153/0.88327639 | RESEARCHING |
| AVAXUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 79 | 0.72172177 | -89,31 | 42/0.52706158 | 37/0.98717690 | VALIDATION_FAILED |
| BNBUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 176 | 0.88616631 | -72,37 | 83/0.74180813 | 93/1.03670490 | RESEARCHING |
| BNBUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 52 | 0.45527532 | -121,12 | 24/0.33817555 | 28/0.56679378 | VALIDATION_FAILED |
| BTCUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 151 | 0.98178094 | -10,17 | 68/0.80653162 | 83/1.14377210 | RESEARCHING |
| BTCUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 30 | 1.30866064 | 30,54 | 13/1.02018756 | 17/1.58521320 | RESEARCHING |
| DOGEUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 300 | 0.80957406 | -201,95 | 144/0.70863928 | 156/0.91011739 | RESEARCHING |
| DOGEUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 71 | 0.98431491 | -3,91 | 28/0.65646292 | 43/1.26764703 | VALIDATION_FAILED |
| DOTUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 314 | 0.89494386 | -117,28 | 144/0.64557825 | 170/1.14730699 | RESEARCHING |
| DOTUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 83 | 0.85185022 | -46,20 | 40/0.62903514 | 43/1.10916796 | VALIDATION_FAILED |
| ETHUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 239 | 0.71206223 | -249,81 | 125/0.67342663 | 114/0.75479129 | RESEARCHING |
| ETHUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 59 | 0.92396460 | -16,54 | 32/0.67361409 | 27/1.27045605 | VALIDATION_FAILED |
| LINKUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 268 | 0.91867723 | -82,58 | 142/0.79380371 | 126/1.07589135 | RESEARCHING |
| LINKUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 76 | 0.68030291 | -96,93 | 42/0.57142190 | 34/0.83731342 | VALIDATION_FAILED |
| LTCUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 229 | 0.73703460 | -215,61 | 118/0.67902338 | 111/0.80355136 | RESEARCHING |
| LTCUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 44 | 1.13888439 | 21,39 | 25/0.72661793 | 19/1.93160307 | RESEARCHING |
| NEARUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 362 | 0.84405944 | -197,89 | 184/0.77293605 | 178/0.92162715 | RESEARCHING |
| NEARUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 192 | 0.75337961 | -181,60 | 115/0.79792048 | 77/0.68947903 | VALIDATION_FAILED |
| SOLUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 264 | 0.83307487 | -158,75 | 124/0.68591645 | 140/0.97431492 | RESEARCHING |
| SOLUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 77 | 0.77240097 | -68,34 | 40/0.62667955 | 37/0.95730782 | VALIDATION_FAILED |
| UNIUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 358 | 0.63667614 | -427,02 | 179/0.50652735 | 179/0.78166216 | RESEARCHING |
| UNIUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 123 | 1.02479363 | 12,01 | 62/1.01419083 | 61/1.03588400 | RESEARCHING |
| XRPUSDT | CPA-PULLBACK\|BASELINE\|5m | IS | BASE | 228 | 0.93110275 | -57,76 | 95/0.84602217 | 133/0.99442335 | RESEARCHING |
| XRPUSDT | CPA-PULLBACK\|BASELINE\|5m | VALIDATION | BASE | 61 | 0.91276766 | -19,56 | 22/0.63266829 | 39/1.10725293 | VALIDATION_FAILED |
| ADAUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | Infinity / NoLosses | 29,53 | 2/Infinity / NoLosses | 1/Infinity / NoLosses | RESEARCHING |
| APTUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 2 | 1.31024408 | 2,29 | 2/1.31024408 | 0/N/A | IS_PROMISING |
| APTUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.47474956 | -10,78 | 1/Infinity / NoLosses | 3/0.00000000 | VALIDATION_FAILED |
| ATOMUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 2 | 22.39985070 | 9,32 | 0/N/A | 2/22.39985070 | IS_PROMISING |
| ATOMUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 1 | 0.00000000 | -5,52 | 1/0.00000000 | 0/N/A | VALIDATION_FAILED |
| AVAXUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 2 | 0.00000000 | -10,63 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| AVAXUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 1.41207874 | 5,70 | 3/0.70486323 | 1/Infinity / NoLosses | RESEARCHING |
| BNBUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 2 | 1.68997276 | 3,96 | 0/N/A | 2/1.68997276 | IS_PROMISING |
| BNBUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 1.81462695 | 8,75 | 1/Infinity / NoLosses | 3/0.90741760 | RESEARCHING |
| BTCUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 2 | 0.00000000 | -10,74 | 0/N/A | 2/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 2 | Infinity / NoLosses | 19,60 | 1/Infinity / NoLosses | 1/Infinity / NoLosses | RESEARCHING |
| DOGEUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.54491877 | -8,09 | 1/0.00000000 | 3/0.80438664 | VALIDATION_FAILED |
| DOTUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.79008081 | -2,57 | 3/0.83031398 | 1/0.00000000 | VALIDATION_FAILED |
| ETHUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 2 | 0.00000000 | -11,29 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 6 | 1.44749437 | 9,11 | 2/1.12440391 | 4/1.69239936 | RESEARCHING |
| LINKUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 4 | 0.00000000 | -23,49 | 1/0.00000000 | 3/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.00000000 | -22,80 | 1/0.00000000 | 3/0.00000000 | VALIDATION_FAILED |
| LTCUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 2 | 0.00000000 | -10,67 | 0/N/A | 2/0.00000000 | RESEARCHING |
| LTCUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 0.80396648 | -2,35 | 0/N/A | 3/0.80396648 | VALIDATION_FAILED |
| NEARUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 2 | 0.00000000 | -16,88 | 1/0.00000000 | 1/0.00000000 | VALIDATION_FAILED |
| SOLUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 3 | 0.00000000 | -21,14 | 0/N/A | 3/0.00000000 | RESEARCHING |
| SOLUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 2 | 1.27030931 | 2,07 | 2/1.27030931 | 0/N/A | RESEARCHING |
| UNIUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 2 | Infinity / NoLosses | 19,61 | 0/N/A | 2/Infinity / NoLosses | RESEARCHING |
| UNIUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 2 | 1.74286576 | 4,13 | 1/Infinity / NoLosses | 1/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -6,07 | 1/0.00000000 | 0/N/A | RESEARCHING |
| XRPUSDT | CPA-PULLBACK\|CONTEXTUAL\|5m | VALIDATION | BASE | 7 | 1.59640486 | 10,95 | 3/1.41667265 | 4/1.70545567 | RESEARCHING |
| ADAUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,51 | 1/0.00000000 | 0/N/A | RESEARCHING |
| ADAUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -6,16 | 0/N/A | 1/0.00000000 | VALIDATION_FAILED |
| APTUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 2 | Infinity / NoLosses | 19,58 | 2/Infinity / NoLosses | 0/N/A | RESEARCHING |
| ATOMUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| ATOMUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 2 | 1.53975759 | 3,42 | 1/Infinity / NoLosses | 1/0.00000000 | IS_PROMISING |
| BTCUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 3 | 3.54978539 | 14,07 | 2/1.76519855 | 1/Infinity / NoLosses | RESEARCHING |
| DOGEUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| ETHUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| ETHUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 1 | 0.00000000 | -6,29 | 1/0.00000000 | 0/N/A | VALIDATION_FAILED |
| LTCUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| LTCUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 4 | 1.56193175 | 6,96 | 3/2.74219004 | 1/0.00000000 | RESEARCHING |
| NEARUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| UNIUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-PULLBACK\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,63 | 0/N/A | 1/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-PULLBACK\|STRICT\|5m | VALIDATION | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| ADAUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 360 | 0.85484020 | -183,67 | 195/0.67999277 | 165/1.10528216 | RESEARCHING |
| ADAUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 99 | 0.93775071 | -22,52 | 57/0.67896247 | 42/1.41526364 | VALIDATION_FAILED |
| APTUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 450 | 0.81417465 | -288,62 | 229/0.71822835 | 221/0.92652529 | RESEARCHING |
| APTUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 148 | 0.84887417 | -88,19 | 72/0.63087515 | 76/1.10734599 | VALIDATION_FAILED |
| ATOMUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 322 | 0.84938780 | -173,39 | 171/0.74992417 | 151/0.97928966 | RESEARCHING |
| ATOMUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 109 | 0.70901129 | -118,77 | 52/0.51781137 | 57/0.92425322 | VALIDATION_FAILED |
| AVAXUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 351 | 0.81000989 | -224,90 | 184/0.62749259 | 167/1.05629593 | RESEARCHING |
| AVAXUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 93 | 1.10018125 | 32,09 | 41/0.89602204 | 52/1.29625203 | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 202 | 0.90144185 | -73,53 | 97/0.79644069 | 105/1.00864038 | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 50 | 1.09450597 | 16,36 | 26/0.85066893 | 24/1.40707355 | RESEARCHING |
| BTCUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 165 | 0.70910837 | -179,12 | 82/0.55620317 | 83/0.88288037 | RESEARCHING |
| BTCUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 43 | 0.76397500 | -38,59 | 27/0.58264106 | 16/1.18177983 | VALIDATION_FAILED |
| DOGEUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 367 | 0.69414325 | -397,07 | 194/0.60751069 | 173/0.80136436 | RESEARCHING |
| DOGEUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 75 | 0.66946834 | -100,49 | 43/0.55043046 | 32/0.85646474 | VALIDATION_FAILED |
| DOTUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 387 | 0.91431504 | -119,02 | 206/0.72205408 | 181/1.18059954 | RESEARCHING |
| DOTUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 106 | 1.10344014 | 38,34 | 58/0.94771514 | 48/1.32182574 | RESEARCHING |
| ETHUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 286 | 0.67207555 | -332,76 | 145/0.58466079 | 141/0.76848168 | RESEARCHING |
| ETHUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 60 | 0.92783809 | -15,92 | 30/0.74534989 | 30/1.13879515 | VALIDATION_FAILED |
| LINKUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 327 | 0.86684255 | -165,19 | 180/0.73643929 | 147/1.05068543 | RESEARCHING |
| LINKUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 79 | 0.87909918 | -35,85 | 42/0.70652943 | 37/1.10448939 | VALIDATION_FAILED |
| LTCUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 281 | 0.66401363 | -333,97 | 151/0.58072745 | 130/0.77771690 | RESEARCHING |
| LTCUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 55 | 0.93516031 | -12,83 | 26/0.73518077 | 29/1.15762712 | VALIDATION_FAILED |
| NEARUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 507 | 0.71207615 | -440,28 | 244/0.70126099 | 263/0.72255536 | RESEARCHING |
| NEARUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 250 | 1.07563548 | 69,53 | 132/1.15866424 | 118/0.98922126 | RESEARCHING |
| SOLUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 359 | 0.76819429 | -297,30 | 165/0.60787342 | 194/0.92718919 | RESEARCHING |
| SOLUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 76 | 0.85775499 | -41,87 | 36/0.73289826 | 40/0.98061035 | VALIDATION_FAILED |
| UNIUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 437 | 0.75567970 | -337,73 | 234/0.69411391 | 203/0.83134946 | RESEARCHING |
| UNIUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 144 | 0.87979830 | -68,34 | 71/0.82271390 | 73/0.93652839 | VALIDATION_FAILED |
| XRPUSDT | CPA-SWEEP\|BASELINE\|5m | IS | BASE | 291 | 0.79952429 | -208,49 | 162/0.69088578 | 129/0.95021127 | RESEARCHING |
| XRPUSDT | CPA-SWEEP\|BASELINE\|5m | VALIDATION | BASE | 58 | 0.86796436 | -29,70 | 30/0.62978661 | 28/1.17324773 | VALIDATION_FAILED |
| ADAUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -5,55 | 0/N/A | 1/0.00000000 | RESEARCHING |
| ADAUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | Infinity / NoLosses | 29,53 | 2/Infinity / NoLosses | 1/Infinity / NoLosses | RESEARCHING |
| APTUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 3 | 0.70235174 | -4,10 | 3/0.70235174 | 0/N/A | RESEARCHING |
| APTUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 7 | 0.66734153 | -9,62 | 2/Infinity / NoLosses | 5/0.00000000 | VALIDATION_FAILED |
| ATOMUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 2 | 22.39985070 | 9,32 | 0/N/A | 2/22.39985070 | IS_PROMISING |
| ATOMUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.00000000 | -23,69 | 3/0.00000000 | 1/0.00000000 | VALIDATION_FAILED |
| AVAXUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 2 | 0.00000000 | -10,65 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| AVAXUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 7 | 1.17422632 | 4,33 | 4/0.50496078 | 3/3.50093492 | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 1 | 0.00000000 | -5,74 | 0/N/A | 1/0.00000000 | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 7 | 3.72198034 | 29,81 | 4/Infinity / NoLosses | 3/0.90741761 | RESEARCHING |
| BTCUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 4 | 0.00000000 | -22,05 | 1/0.00000000 | 3/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 5 | 6.36600918 | 33,31 | 2/Infinity / NoLosses | 3/3.17435638 | RESEARCHING |
| DOGEUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 2 | 0.00000000 | -10,53 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| DOGEUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 4 | 0.54491877 | -8,09 | 1/0.00000000 | 3/0.80438664 | VALIDATION_FAILED |
| DOTUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 2 | 1.83296614 | 4,43 | 1/0.00000000 | 1/Infinity / NoLosses | IS_PROMISING |
| DOTUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 7 | 0.32061921 | -20,41 | 3/0.83031398 | 4/0.00000000 | VALIDATION_FAILED |
| ETHUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 2 | 0.00000000 | -11,18 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 8 | 1.50482577 | 13,26 | 3/0.68304672 | 5/2.53289870 | RESEARCHING |
| LINKUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 5 | 0.00000000 | -28,40 | 2/0.00000000 | 3/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 9 | 0.48898482 | -19,91 | 4/1.62291387 | 5/0.00000000 | VALIDATION_FAILED |
| LTCUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 3 | 0.00000000 | -16,00 | 1/0.00000000 | 2/0.00000000 | RESEARCHING |
| LTCUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 5 | 1.21422148 | 3,42 | 2/1.81448634 | 3/0.91485797 | RESEARCHING |
| NEARUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 5 | 0.34666812 | -18,16 | 2/1.72203414 | 3/0.00000000 | VALIDATION_FAILED |
| SOLUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 3 | 0.00000000 | -21,14 | 0/N/A | 3/0.00000000 | RESEARCHING |
| SOLUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 2.52836015 | 11,84 | 3/2.52836015 | 0/N/A | RESEARCHING |
| UNIUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 2 | Infinity / NoLosses | 19,61 | 0/N/A | 2/Infinity / NoLosses | RESEARCHING |
| UNIUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 3 | 0.90443369 | -1,02 | 1/Infinity / NoLosses | 2/0.00000000 | VALIDATION_FAILED |
| XRPUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | IS | BASE | 3 | 0.80823470 | -2,31 | 1/0.00000000 | 2/1.63237972 | RESEARCHING |
| XRPUSDT | CPA-SWEEP\|CONTEXTUAL\|5m | VALIDATION | BASE | 11 | 1.67200196 | 19,71 | 5/1.57687911 | 6/1.74226195 | RESEARCHING |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 5 | 0.40190465 | -14,33 | 4/0.56386610 | 1/0.00000000 | RESEARCHING |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 5 | 0.00000000 | -30,23 | 3/0.00000000 | 2/0.00000000 | RESEARCHING |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 2 | 0.00000000 | -11,72 | 2/0.00000000 | 0/N/A | RESEARCHING |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 4 | 0.00000000 | -24,22 | 3/0.00000000 | 1/0.00000000 | OOS_FAILED |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 4 | 0.00000000 | -24,80 | 3/0.00000000 | 1/0.00000000 | OOS_FAILED |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 4 | 0.00000000 | -24,51 | 3/0.00000000 | 1/0.00000000 | OOS_FAILED |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 4 | 0.00000000 | -25,39 | 3/0.00000000 | 1/0.00000000 | OOS_FAILED |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 4 | 0.52588661 | -8,78 | 2/1.79699095 | 2/0.00000000 | VALIDATION_FAILED |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 7 | 2.15258851 | 20,92 | 4/4.56433737 | 3/0.83261510 | RESEARCHING |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 8 | 1.63191841 | 15,06 | 3/0.75597831 | 5/2.66754679 | RESEARCHING |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 2 | 1.50770036 | 3,26 | 2/1.50770036 | 0/N/A | IS_PROMISING |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 8 | 1.63191841 | 15,06 | 3/0.75597831 | 5/2.66754679 | RESEARCHING |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 8 | 1.57125945 | 13,95 | 3/0.72914448 | 5/2.56198171 | RESEARCHING |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 8 | 1.60121084 | 14,51 | 3/0.74240774 | 5/2.61404145 | RESEARCHING |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 8 | 1.60667014 | 14,32 | 3/0.78766323 | 5/2.46382675 | RESEARCHING |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 5 | 2.50554202 | 17,61 | 2/Infinity / NoLosses | 3/0.83261511 | RESEARCHING |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| APTUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 7 | 2.37428359 | 22,66 | 1/Infinity / NoLosses | 6/1.78661662 | RESEARCHING |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 6 | 1.71734655 | 12,18 | 1/Infinity / NoLosses | 5/1.14742992 | RESEARCHING |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 3 | 3.61273173 | 14,09 | 1/Infinity / NoLosses | 2/1.81623170 | IS_PROMISING |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 5 | 2.69768747 | 18,46 | 1/Infinity / NoLosses | 4/1.80243604 | RESEARCHING |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 5 | 2.59005231 | 17,77 | 1/Infinity / NoLosses | 4/1.73097015 | RESEARCHING |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 5 | 2.64312297 | 18,11 | 1/Infinity / NoLosses | 4/1.76620762 | RESEARCHING |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 5 | 1.11676133 | 1,98 | 1/0.00000000 | 4/1.65091459 | RESEARCHING |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 5 | 1.12993255 | 2,23 | 0/N/A | 5/1.12993255 | RESEARCHING |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ATOMUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 3 | 0.88300521 | -1,29 | 2/0.00000000 | 1/Infinity / NoLosses | RESEARCHING |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 5 | 1.07043647 | 1,28 | 2/0.00000000 | 3/3.07185699 | RESEARCHING |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 5 | 1.07043647 | 1,28 | 2/0.00000000 | 3/3.07185699 | RESEARCHING |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 5 | 1.03148937 | 0,59 | 2/0.00000000 | 3/2.96172686 | RESEARCHING |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 5 | 1.05072462 | 0,93 | 2/0.00000000 | 3/3.01613573 | RESEARCHING |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 5 | 0.99438108 | -0,11 | 2/0.00000000 | 3/2.85666534 | OOS_FAILED |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 3 | 0.88300521 | -1,29 | 2/0.00000000 | 1/Infinity / NoLosses | VALIDATION_FAILED |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| AVAXUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 4 | 5.27057758 | 23,93 | 3/3.49521120 | 1/Infinity / NoLosses | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 8 | 2.37658088 | 22,91 | 8/2.37658088 | 0/N/A | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 8 | 2.37658088 | 22,91 | 8/2.37658088 | 0/N/A | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 8 | 2.26319536 | 21,78 | 8/2.26319536 | 0/N/A | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 8 | 2.31886367 | 22,34 | 8/2.31886367 | 0/N/A | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 8 | 2.15758232 | 20,65 | 8/2.15758232 | 0/N/A | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 3 | 3.53144504 | 14,05 | 2/1.75607867 | 1/Infinity / NoLosses | RESEARCHING |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BNBUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 11 | 2.07063499 | 30,41 | 9/2.21704276 | 2/1.56198625 | RESEARCHING |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 4 | 0.58705515 | -6,74 | 4/0.58705515 | 0/N/A | RESEARCHING |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 7 | 1.29667769 | 6,67 | 6/1.80702885 | 1/0.00000000 | IS_PROMISING |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 3 | 0.87829121 | -1,34 | 3/0.87829121 | 0/N/A | OOS_FAILED |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 3 | 0.84388387 | -1,76 | 3/0.84388387 | 0/N/A | OOS_FAILED |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 3 | 0.86085924 | -1,55 | 3/0.86085924 | 0/N/A | OOS_FAILED |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 3 | 0.81124136 | -2,18 | 3/0.81124136 | 0/N/A | OOS_FAILED |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 5 | 2.57884764 | 18,04 | 4/1.71688449 | 1/Infinity / NoLosses | RESEARCHING |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 1 | Infinity / NoLosses | 0,52 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 1 | 0.00000000 | -1,62 | 1/0.00000000 | 0/N/A | RESEARCHING |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| BTCUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 8 | 0.92553191 | -2,33 | 1/Infinity / NoLosses | 7/0.61594977 | RESEARCHING |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 4 | 0.00000000 | -23,80 | 2/0.00000000 | 2/0.00000000 | RESEARCHING |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 1 | 0.00000000 | -5,90 | 0/N/A | 1/0.00000000 | RESEARCHING |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 3 | 0.00000000 | -17,65 | 1/0.00000000 | 2/0.00000000 | OOS_FAILED |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 3 | 0.00000000 | -18,10 | 1/0.00000000 | 2/0.00000000 | OOS_FAILED |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 3 | 0.00000000 | -17,88 | 1/0.00000000 | 2/0.00000000 | OOS_FAILED |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 3 | 0.00000000 | -18,55 | 1/0.00000000 | 2/0.00000000 | OOS_FAILED |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 8 | 1.14621093 | 3,73 | 2/Infinity / NoLosses | 6/0.75900725 | RESEARCHING |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOGEUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 8 | 0.57543513 | -14,10 | 4/0.57562289 | 4/0.57524632 | RESEARCHING |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 11 | 1.45714852 | 15,33 | 7/1.32496174 | 4/1.71535602 | RESEARCHING |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 2 | 0.00000000 | -10,55 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 11 | 1.45714852 | 15,33 | 7/1.32496174 | 4/1.71535602 | RESEARCHING |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 11 | 1.40055294 | 13,79 | 7/1.27275251 | 4/1.65030530 | RESEARCHING |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 11 | 1.42847344 | 14,56 | 7/1.29850773 | 4/1.68239971 | RESEARCHING |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 11 | 1.34685959 | 12,24 | 7/1.22323106 | 4/1.58856934 | RESEARCHING |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 6 | 0.84341665 | -3,59 | 3/0.84466782 | 3/0.84216138 | VALIDATION_FAILED |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| DOTUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 4 | 1.40529217 | 5,65 | 4/1.40529217 | 0/N/A | RESEARCHING |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 7 | 1.23111742 | 5,46 | 6/1.64063674 | 1/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 6 | 1.60142701 | 10,97 | 5/2.37139725 | 1/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 6 | 1.61486181 | 10,98 | 5/2.44776722 | 1/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 6 | 1.57146050 | 10,55 | 5/2.32765756 | 1/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 6 | 1.55403082 | 10,14 | 5/2.35549647 | 1/0.00000000 | RESEARCHING |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 4 | 0.50652499 | -9,49 | 4/0.50652499 | 0/N/A | VALIDATION_FAILED |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ETHUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 5 | 0.00000000 | -29,20 | 5/0.00000000 | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 6 | 0.88097148 | -2,62 | 3/3.53708749 | 3/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 2 | 0.00000000 | -12,48 | 2/0.00000000 | 0/N/A | RESEARCHING |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 5 | 1.19978378 | 3,24 | 3/3.53708749 | 2/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 5 | 1.15183255 | 2,53 | 3/3.39863419 | 2/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 5 | 1.17547696 | 2,89 | 3/3.46693854 | 2/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 5 | 1.10642546 | 1,82 | 3/3.26727624 | 2/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 4 | 0.00000000 | -22,67 | 3/0.00000000 | 1/0.00000000 | VALIDATION_FAILED |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LINKUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 10 | 0.40124150 | -28,62 | 4/0.52557706 | 6/0.32407074 | RESEARCHING |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 6 | 1.82561975 | 13,30 | 4/5.49231285 | 2/0.00000000 | RESEARCHING |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 5 | 0.40825055 | -13,94 | 3/0.84636355 | 2/0.00000000 | RESEARCHING |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 3 | 0.91394679 | -0,91 | 1/Infinity / NoLosses | 2/0.00000000 | OOS_FAILED |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 3 | 0.87673329 | -1,34 | 1/Infinity / NoLosses | 2/0.00000000 | OOS_FAILED |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 3 | 0.89507692 | -1,13 | 1/Infinity / NoLosses | 2/0.00000000 | OOS_FAILED |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 3 | 0.84153922 | -1,78 | 1/Infinity / NoLosses | 2/0.00000000 | OOS_FAILED |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 8 | 0.97105538 | -0,86 | 4/1.56704461 | 4/0.55265863 | VALIDATION_FAILED |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| LTCUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 4 | 1.78363720 | 8,57 | 2/0.00000000 | 2/Infinity / NoLosses | RESEARCHING |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 6 | 0.88700722 | -2,48 | 2/1.85100512 | 4/0.58211261 | RESEARCHING |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,76 | 0/N/A | 1/Infinity / NoLosses | RESEARCHING |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 4 | 0.59023740 | -6,70 | 1/0.00000000 | 3/0.86888533 | OOS_FAILED |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 4 | 0.56712986 | -7,27 | 1/0.00000000 | 3/0.83499571 | OOS_FAILED |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 4 | 0.57852631 | -6,98 | 1/0.00000000 | 3/0.85171212 | OOS_FAILED |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 4 | 0.54523153 | -7,84 | 1/0.00000000 | 3/0.80286252 | OOS_FAILED |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 5 | 1.18810184 | 3,07 | 3/0.89888559 | 2/1.75882327 | RESEARCHING |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| NEARUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 4 | 25.31118121 | 28,36 | 2/Infinity / NoLosses | 2/8.44366327 | RESEARCHING |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 6 | 0.87716168 | -2,70 | 3/3.53393772 | 3/0.00000000 | RESEARCHING |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 1 | Infinity / NoLosses | 9,74 | 1/Infinity / NoLosses | 0/N/A | RESEARCHING |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 4 | 0.58136285 | -6,90 | 1/Infinity / NoLosses | 3/0.00000000 | OOS_FAILED |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 4 | 0.55817938 | -7,48 | 1/Infinity / NoLosses | 3/0.00000000 | OOS_FAILED |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 4 | 0.56961341 | -7,19 | 1/Infinity / NoLosses | 3/0.00000000 | OOS_FAILED |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 4 | 0.53620839 | -8,06 | 1/Infinity / NoLosses | 3/0.00000000 | OOS_FAILED |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 5 | 2.68807945 | 18,48 | 3/3.54811933 | 2/1.80571883 | RESEARCHING |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| SOLUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 7 | 1.34835564 | 7,56 | 2/1.75660060 | 5/1.20847507 | RESEARCHING |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 7 | 0.55779222 | -15,37 | 5/0.94140138 | 2/0.00000000 | RESEARCHING |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 3 | 0.90752070 | -0,99 | 0/N/A | 3/0.90752070 | RESEARCHING |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 5 | 0.33184777 | -19,34 | 3/0.64729434 | 2/0.00000000 | OOS_FAILED |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 5 | 0.32087790 | -20,06 | 3/0.62660596 | 2/0.00000000 | OOS_FAILED |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 5 | 0.32630690 | -19,70 | 3/0.63684952 | 2/0.00000000 | OOS_FAILED |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 5 | 0.31034222 | -20,77 | 3/0.60669972 | 2/0.00000000 | OOS_FAILED |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 6 | 1.75371870 | 12,65 | 4/1.73335963 | 2/1.79561975 | RESEARCHING |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| UNIUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK3 | BASE | 11 | 1.48060002 | 15,66 | 4/5.32192456 | 7/0.71534070 | RESEARCHING |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | BLOCK4 | BASE | 6 | 0.88236394 | -2,55 | 5/1.16458768 | 1/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | IS | BASE | 3 | 0.00000000 | -16,67 | 0/N/A | 3/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | OOS | BASE | 4 | 1.80151500 | 8,62 | 3/3.56579772 | 1/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | OOS | HIGH | 4 | 1.72923568 | 8,06 | 3/3.42548674 | 1/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | OOS | MILD | 4 | 1.76487766 | 8,34 | 3/3.49470005 | 1/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | OOS | STRESS | 4 | 1.66078042 | 7,50 | 3/3.29242179 | 1/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | VALIDATION | BASE | 10 | 1.98999871 | 24,41 | 6/2.09672857 | 4/1.85039661 | RESEARCHING |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | WF0 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | WF1 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | WF2 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | WF3 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | WF4 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | WF5 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | WF6 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| XRPUSDT | CPA-SWEEP\|STRICT\|5m | WF7 | BASE | 0 | N/A | 0,00 | 0/N/A | 0/N/A | NO_TRADES |
| ADAUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 320 | 0.92369087 | -91,65 | 146/0.70998739 | 174/1.13121052 | RESEARCHING |
| ADAUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 99 | 0.71678011 | -111,93 | 54/0.52102087 | 45/0.99755904 | VALIDATION_FAILED |
| APTUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 448 | 0.77307136 | -342,24 | 235/0.67175341 | 213/0.89719755 | RESEARCHING |
| APTUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 149 | 0.86846465 | -74,93 | 75/0.66671153 | 74/1.11548658 | VALIDATION_FAILED |
| ATOMUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 313 | 0.88459785 | -132,62 | 161/0.79818542 | 152/0.98619724 | RESEARCHING |
| ATOMUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 92 | 0.92391847 | -25,19 | 47/0.66798773 | 45/1.27864429 | VALIDATION_FAILED |
| AVAXUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 330 | 0.92198543 | -105,38 | 165/0.72013361 | 165/1.16247955 | RESEARCHING |
| AVAXUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 95 | 0.77008867 | -83,85 | 41/0.52439998 | 54/0.99247270 | VALIDATION_FAILED |
| BNBUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 198 | 0.71493907 | -211,84 | 91/0.66200462 | 107/0.76189703 | RESEARCHING |
| BNBUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 47 | 1.38480916 | 58,64 | 19/1.24788666 | 28/1.48328936 | RESEARCHING |
| BTCUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 147 | 0.69339227 | -175,53 | 73/0.55690083 | 74/0.85679037 | RESEARCHING |
| BTCUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 39 | 0.75816281 | -36,95 | 24/0.54328666 | 15/1.23929207 | VALIDATION_FAILED |
| DOGEUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 315 | 0.84437653 | -179,30 | 137/0.69255413 | 178/0.98129528 | RESEARCHING |
| DOGEUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 75 | 0.87990185 | -33,93 | 35/0.48087528 | 40/1.40180812 | VALIDATION_FAILED |
| DOTUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 366 | 0.80488482 | -264,35 | 180/0.58596370 | 186/1.07038062 | RESEARCHING |
| DOTUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 101 | 0.79963286 | -76,56 | 54/0.68003316 | 47/0.95684899 | VALIDATION_FAILED |
| ETHUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 257 | 0.82072975 | -166,29 | 116/0.67788130 | 141/0.94688106 | RESEARCHING |
| ETHUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 64 | 0.81413927 | -47,69 | 33/0.64735651 | 31/1.01802005 | VALIDATION_FAILED |
| LINKUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 318 | 0.90395885 | -110,67 | 153/0.79146587 | 165/1.02037450 | RESEARCHING |
| LINKUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 75 | 0.98282544 | -4,91 | 35/0.81427109 | 40/1.15907855 | VALIDATION_FAILED |
| LTCUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 255 | 0.79703745 | -191,12 | 122/0.72874671 | 133/0.86352447 | RESEARCHING |
| LTCUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 55 | 1.15398615 | 29,66 | 27/0.72435749 | 28/1.74050013 | RESEARCHING |
| NEARUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 415 | 0.80035999 | -280,30 | 199/0.75219669 | 216/0.84651997 | RESEARCHING |
| NEARUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 201 | 0.78012251 | -166,08 | 112/0.84176470 | 89/0.70734690 | VALIDATION_FAILED |
| SOLUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 306 | 0.96226189 | -44,00 | 144/0.79954467 | 162/1.13457593 | RESEARCHING |
| SOLUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 85 | 0.79016259 | -69,98 | 36/0.74255882 | 49/0.82645128 | VALIDATION_FAILED |
| UNIUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 415 | 0.77895334 | -321,48 | 194/0.62973403 | 221/0.92941781 | RESEARCHING |
| UNIUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 133 | 0.74880098 | -130,57 | 70/0.70913045 | 63/0.79152066 | VALIDATION_FAILED |
| XRPUSDT | CPA-WM\|BASELINE\|5m | IS | BASE | 268 | 0.99527196 | -4,79 | 128/0.83641325 | 140/1.15836628 | RESEARCHING |
| XRPUSDT | CPA-WM\|BASELINE\|5m | VALIDATION | BASE | 69 | 0.75224276 | -68,52 | 32/0.46052141 | 37/1.08121510 | VALIDATION_FAILED |
| ADAUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 19 | 0.20149332 | -74,86 | 10/0.17995961 | 9/0.22723937 | RESEARCHING |
| ADAUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 28 | 1.68117578 | 56,08 | 13/1.03413805 | 15/2.58376376 | RESEARCHING |
| APTUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 24 | 1.00314083 | 0,27 | 14/1.27099656 | 10/0.70557402 | IS_PROMISING |
| APTUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 54 | 0.57744386 | -93,66 | 24/0.67870546 | 30/0.50028776 | VALIDATION_FAILED |
| ATOMUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 13 | 0.78558791 | -10,41 | 7/0.70863629 | 6/0.88147127 | RESEARCHING |
| ATOMUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 31 | 1.04186242 | 4,65 | 14/0.87357455 | 17/1.20804703 | RESEARCHING |
| AVAXUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 16 | 0.36377615 | -49,52 | 10/0.67625739 | 6/0.00000000 | RESEARCHING |
| AVAXUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 41 | 0.85052721 | -23,68 | 19/0.45800618 | 22/1.29834530 | VALIDATION_FAILED |
| BNBUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 10 | 0.64977889 | -13,29 | 3/0.87855612 | 7/0.55761804 | RESEARCHING |
| BNBUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 28 | 1.12229404 | 11,70 | 10/1.27126864 | 18/1.05037386 | RESEARCHING |
| BTCUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 15 | 0.67270005 | -18,62 | 9/1.65587778 | 6/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 18 | 1.02588282 | 1,64 | 9/0.75073872 | 9/1.34479776 | RESEARCHING |
| DOGEUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 17 | 0.91280606 | -5,54 | 8/2.83436651 | 9/0.20756757 | RESEARCHING |
| DOGEUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 39 | 0.97949911 | -2,86 | 14/0.45389066 | 25/1.43074002 | VALIDATION_FAILED |
| DOTUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 19 | 0.55417439 | -37,82 | 11/0.55081630 | 8/0.55926581 | RESEARCHING |
| DOTUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 39 | 0.76564730 | -35,14 | 17/0.35107076 | 22/1.26851909 | VALIDATION_FAILED |
| ETHUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 17 | 0.38181938 | -48,58 | 9/0.55256802 | 8/0.22842684 | RESEARCHING |
| ETHUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 29 | 1.42019979 | 39,13 | 12/0.98532172 | 17/1.79344057 | RESEARCHING |
| LINKUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 14 | 1.13705668 | 7,02 | 6/1.52250152 | 8/0.90709107 | IS_PROMISING |
| LINKUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 31 | 1.00109374 | 0,12 | 16/0.88511569 | 15/1.12816905 | RESEARCHING |
| LTCUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 8 | 0.59372937 | -13,37 | 4/0.61193196 | 4/0.57653635 | RESEARCHING |
| LTCUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 28 | 1.50156793 | 43,45 | 15/0.62215137 | 13/4.02463913 | RESEARCHING |
| NEARUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 19 | 0.48400987 | -40,47 | 8/1.06381181 | 11/0.18138564 | RESEARCHING |
| NEARUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 36 | 0.97181202 | -3,64 | 20/1.11429394 | 16/0.80946260 | VALIDATION_FAILED |
| SOLUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 16 | 0.39950415 | -43,48 | 10/0.42421224 | 6/0.35816827 | RESEARCHING |
| SOLUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 33 | 0.92217037 | -9,86 | 16/0.93652052 | 17/0.90817903 | VALIDATION_FAILED |
| UNIUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 23 | 0.90359382 | -8,29 | 8/0.56087607 | 15/1.13111352 | RESEARCHING |
| UNIUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 44 | 1.04867522 | 7,64 | 20/1.69541276 | 24/0.67723412 | RESEARCHING |
| XRPUSDT | CPA-WM\|CONTEXTUAL\|5m | IS | BASE | 12 | 0.33596361 | -36,88 | 6/0.34927463 | 6/0.32356565 | RESEARCHING |
| XRPUSDT | CPA-WM\|CONTEXTUAL\|5m | VALIDATION | BASE | 35 | 1.87147017 | 84,54 | 19/1.66574928 | 16/2.13719331 | RESEARCHING |
| ADAUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 6 | 0.33956251 | -18,63 | 5/0.42717356 | 1/0.00000000 | RESEARCHING |
| ADAUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 6 | 0.84025307 | -3,67 | 3/0.00000000 | 3/3.16718877 | VALIDATION_FAILED |
| APTUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 3 | 0.69733846 | -4,20 | 2/1.38171384 | 1/0.00000000 | RESEARCHING |
| APTUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 9 | 1.38575679 | 10,77 | 4/1.63320923 | 5/1.20363070 | RESEARCHING |
| ATOMUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 2 | 1.70695565 | 4,01 | 1/Infinity / NoLosses | 1/0.00000000 | IS_PROMISING |
| ATOMUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 4 | 1.72956292 | 8,24 | 2/1.76503601 | 2/1.69566041 | RESEARCHING |
| AVAXUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 4 | 0.00000000 | -21,37 | 1/0.00000000 | 3/0.00000000 | RESEARCHING |
| AVAXUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 13 | 0.74449458 | -13,20 | 7/0.29753806 | 6/1.51828293 | VALIDATION_FAILED |
| BNBUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 4 | 0.00000000 | -23,13 | 2/0.00000000 | 2/0.00000000 | RESEARCHING |
| BNBUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 7 | 0.69844866 | -8,41 | 4/0.56790830 | 3/0.90712115 | VALIDATION_FAILED |
| BTCUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 2 | 0.00000000 | -11,24 | 1/0.00000000 | 1/0.00000000 | RESEARCHING |
| BTCUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 9 | 0.86506120 | -4,56 | 3/0.00000000 | 6/1.70301093 | VALIDATION_FAILED |
| DOGEUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 7 | 0.67243882 | -9,32 | 3/3.09564645 | 4/0.00000000 | RESEARCHING |
| DOGEUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 8 | 0.53906780 | -16,47 | 3/0.00000000 | 5/1.07939503 | VALIDATION_FAILED |
| DOTUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 4 | 1.78367935 | 8,57 | 2/0.00000000 | 2/Infinity / NoLosses | IS_PROMISING |
| DOTUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 10 | 0.82920475 | -5,97 | 3/3.53072309 | 7/0.32511252 | VALIDATION_FAILED |
| ETHUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 2 | 1.82235124 | 4,38 | 1/0.00000000 | 1/Infinity / NoLosses | IS_PROMISING |
| ETHUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 8 | 0.52659364 | -17,35 | 4/0.00000000 | 4/1.54795248 | VALIDATION_FAILED |
| LINKUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 3 | 0.00000000 | -18,21 | 1/0.00000000 | 2/0.00000000 | RESEARCHING |
| LINKUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 8 | 1.37378492 | 8,38 | 5/1.81381809 | 3/0.90100723 | RESEARCHING |
| LTCUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 5 | 0.39669660 | -14,74 | 0/N/A | 5/0.39669660 | RESEARCHING |
| LTCUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 10 | 0.84125870 | -5,48 | 5/0.54203813 | 5/1.16234398 | VALIDATION_FAILED |
| NEARUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 4 | 1.51442873 | 6,61 | 1/Infinity / NoLosses | 3/0.75649870 | IS_PROMISING |
| NEARUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 4 | 1.31450936 | 4,69 | 2/Infinity / NoLosses | 2/0.00000000 | RESEARCHING |
| SOLUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 3 | 0.80860387 | -2,28 | 1/Infinity / NoLosses | 2/0.00000000 | RESEARCHING |
| SOLUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 5 | 2.61297268 | 18,13 | 2/1.72650034 | 3/3.50652297 | RESEARCHING |
| UNIUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 5 | Infinity / NoLosses | 49,72 | 2/Infinity / NoLosses | 3/Infinity / NoLosses | RESEARCHING |
| UNIUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 7 | 2.24678202 | 21,77 | 4/1.73155701 | 3/3.20759629 | RESEARCHING |
| XRPUSDT | CPA-WM\|STRICT\|5m | IS | BASE | 3 | 0.00000000 | -17,31 | 0/N/A | 3/0.00000000 | RESEARCHING |
| XRPUSDT | CPA-WM\|STRICT\|5m | VALIDATION | BASE | 7 | 1.48130204 | 9,49 | 4/0.68011085 | 3/3.52512900 | RESEARCHING |
