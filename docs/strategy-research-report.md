# Strategy research report

Research layer above Model B. Frozen catalog templates were not modified. LIVE = OFF.
OOS is diagnostic only. Candidates are **not** frozen from this run.
Do not treat this as a LIVE recommendation. No candidate is marked BEST, GUARANTEED, or PROFITABLE.

## 1. Executive summary
**No robust edge identified in the tested candidate set** on this run.

Paper remains OFF. LIVE remains OFF. Phase 3 (full 528 × 3) is not started.

Candidates evaluated: 15. Books: 13570. CodeVersion: research-layer-1.

## 2. Baseline strategies

| Template | Model B Combined PF | Status |
|---|---:|---|
| Bollinger Reversion | 0.585 | VALIDATION_PENDING (immutable baseline) |
| Donchian Breakout | 0.705 | VALIDATION_PENDING (immutable baseline) |
| EMA RSI Trend | 0.754 | VALIDATION_PENDING (immutable baseline) |
| MACD Trend | 0.695 | VALIDATION_PENDING (immutable baseline) |
| RSI Pullback | 0.615 | VALIDATION_PENDING (immutable baseline) |

These frozen defaults did not demonstrate a cost-inclusive historical edge on 528 × 3. Research candidates are versioned separately.

## 3. Research hypotheses
- **DONCHIAN-ATR-001**: Breakouts may behave differently when ATR is expanding versus contracting.
- **DONCHIAN-VOL-001**: Breakout signals with unusually low relative volume may differ from normal/high-volume breakouts.
- **DONCHIAN-TREND-001**: Requiring EMA20/EMA50 alignment may reduce counter-trend Donchian breaks.
- **EMA-RSI-HTF-001**: Higher-timeframe EMA alignment may reduce counter-trend EMA-RSI entries.
- **EMA-RSI-ADX-001**: ADX trend-strength gating may skip EMA-RSI crosses in weak/range regimes.
- **MACD-HTF-001**: Higher-timeframe EMA alignment may reduce late or counter-trend MACD crosses.
- **RSI-PB-ADX-001**: RSI pullbacks may need a minimum ADX so they occur inside an actual trend.
- **RSI-PB-ATR-001**: Pullbacks may be more usable in moderate ATR percentile and worse in extremes.
- **VWAP-PB-001**: In a directional VWAP slope, reclaiming session VWAP may be a continuation entry.
- **VWAP-PB-EMA-001**: VWAP reclaim plus EMA20/EMA50 agreement may skip counter-trend reclaims.
- **ST-EMA-001**: Trend-following may improve when Supertrend direction agrees with EMA20/EMA50 structure.
- **VOL-BO-001**: Donchian breaks during elevated ATR percentile and relative volume may differ from quiet breaks.
- **TREND-PB-001**: Waiting for a pullback to EMA20 inside an EMA trend may improve continuation entries versus immediate crosses.
- **MTF-5M-15M-001**: 5m RSI pullback entries may be more robust when 15m EMA20/EMA50 agrees.
- **MTF-15M-1H-001**: 15m Donchian breaks may be more robust when 1h EMA20/EMA50 agrees.

## 4. Candidate definitions

### DONCHIAN-ATR-001
- Parent: `donchian_breakout` v1
- Kind: parent_filter
- Indicators: Donchian20, ATR14
- Entry: Donchian N-bar break on the closed candle, only if ATR(14) is higher than the previous closed ATR.
- Exit: Opposite Donchian/EMA signal plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### DONCHIAN-VOL-001
- Parent: `donchian_breakout` v1
- Kind: parent_filter
- Indicators: Donchian20, RelativeVolume20
- Entry: Donchian break only when relative volume (volume / SMA20) is at least 1.2.
- Exit: Opposite signal plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### DONCHIAN-TREND-001
- Parent: `donchian_breakout` v1
- Kind: parent_filter
- Indicators: Donchian20, EMA20, EMA50
- Entry: Donchian break only when EMA20 is on the same side of EMA50 as the break, with price versus slow EMA.
- Exit: Opposite signal plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### EMA-RSI-HTF-001
- Parent: `ema_rsi_trend` v1
- Kind: parent_filter
- Indicators: EMA20, EMA50, RSI14, HTF-EMA
- Entry: Frozen EMA RSI Trend signal, allowed only if the last closed higher-timeframe EMA20/EMA50 agrees.
- Exit: Opposite EMA cross plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### EMA-RSI-ADX-001
- Parent: `ema_rsi_trend` v1
- Kind: parent_filter
- Indicators: EMA20, EMA50, RSI14, ADX14
- Entry: Frozen EMA RSI Trend signal only when ADX(14) is at least 20.
- Exit: Opposite EMA cross plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### MACD-HTF-001
- Parent: `macd_trend` v1
- Kind: parent_filter
- Indicators: MACD, EMA50, HTF-EMA
- Entry: Frozen MACD Trend signal, allowed only if last closed HTF EMA20/EMA50 agrees.
- Exit: Opposite MACD cross plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### RSI-PB-ADX-001
- Parent: `rsi_pullback` v1
- Kind: parent_filter
- Indicators: RSI14, EMA50, ADX14
- Entry: Frozen RSI Pullback signal only when ADX(14) is at least 20.
- Exit: RSI recross of 50 / EMA reverse plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### RSI-PB-ATR-001
- Parent: `rsi_pullback` v1
- Kind: parent_filter
- Indicators: RSI14, EMA50, ATR-percentile
- Entry: Frozen RSI Pullback only when ATR percentile is between 0.30 and 0.80.
- Exit: RSI recross of 50 / EMA reverse plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### VWAP-PB-001
- Parent: `vwap_pullback` v1
- Kind: native
- Indicators: SessionVWAP
- Entry: LONG: VWAP slope > 0 and close reclaims VWAP. SHORT is the inverse. No extra EMA.
- Exit: Opposite reclaim or Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### VWAP-PB-EMA-001
- Parent: `vwap_pullback` v1
- Kind: native
- Indicators: SessionVWAP, EMA20, EMA50
- Entry: VWAP reclaim only when EMA20/EMA50 and price versus slow EMA agree with the side.
- Exit: Opposite reclaim or Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### ST-EMA-001
- Parent: `supertrend_ema` v1
- Kind: native
- Indicators: Supertrend10x3, EMA20, EMA50
- Entry: LONG: Supertrend bullish AND EMA20 > EMA50 AND close > EMA50. SHORT inverse.
- Exit: Opposite Supertrend/EMA condition plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### VOL-BO-001
- Parent: `donchian_breakout` v1
- Kind: parent_filter
- Indicators: Donchian20, ATR-percentile, RelativeVolume20
- Entry: Donchian break only when ATR percentile > 0.60 and relative volume > 1.2.
- Exit: Opposite signal plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### TREND-PB-001
- Parent: `trend_pullback` v1
- Kind: native
- Indicators: EMA20, EMA50, RSI14
- Entry: LONG: EMA20 > EMA50, EMA50 slope > 0, bar pulls into EMA20 then closes above, RSI crosses up through 40. SHORT inverse.
- Exit: Opposite trend/pullback condition plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m, 15m, 1h
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### MTF-5M-15M-001
- Parent: `rsi_pullback` v1
- Kind: parent_filter
- Indicators: RSI14, EMA50, 15m-EMA
- Entry: RSI Pullback on 5m only if last closed 15m EMA20/EMA50 agrees.
- Exit: RSI recross of 50 plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 5m
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

### MTF-15M-1H-001
- Parent: `donchian_breakout` v1
- Kind: parent_filter
- Indicators: Donchian20, 1h-EMA
- Entry: Donchian break on 15m only if last closed 1h EMA20/EMA50 agrees.
- Exit: Opposite Donchian/EMA plus Isolated book SL/TP.
- Directions: LONG, SHORT
- Timeframes: 15m
- Created: 2026-09-19 UTC
- CodeVersion: research-layer-1
- Dataset scope: pilot: 10 liquid USDT-M perpetuals × selected TF; Phase 2 representative; Phase 3 full discovered universe

## 5. IS results
- RSI-PB-ATR-001: n=6 PF 0,64530952 (+72,27/|111,99|) net -39,72 fees 23,93 wr 16,67% exp -6,62
- DONCHIAN-ATR-001: n=15755 PF 0,76284496 (+384115,88/|503530,74|) net -119414,85 fees 48023,33 wr 30,09% exp -7,58
- VWAP-PB-001: n=24859 PF 0,65522592 (+254457,11/|388350,20|) net -133893,08 fees 71595,46 wr 16,76% exp -5,39
- DONCHIAN-VOL-001: n=15675 PF 0,76645901 (+385280,46/|502675,88|) net -117395,42 fees 48290,63 wr 30,07% exp -7,49
- VWAP-PB-EMA-001: n=16799 PF 0,64828481 (+187024,37/|288491,06|) net -101466,69 fees 52447,93 wr 16,94% exp -6,04
- DONCHIAN-TREND-001: n=13910 PF 0,77616387 (+351270,70/|452572,85|) net -101302,16 fees 43837,16 wr 30,23% exp -7,28
- ST-EMA-001: n=37540 PF 0,71708980 (+449864,93/|627348,11|) net -177483,18 fees 84984,88 wr 21,20% exp -4,73
- EMA-RSI-HTF-001: n=2682 PF 0,78631034 (+81285,20/|103375,46|) net -22090,27 fees 10198,22 wr 25,95% exp -8,24
- EMA-RSI-ADX-001: n=1940 PF 0,74662452 (+57378,32/|76850,31|) net -19471,98 fees 7451,43 wr 26,91% exp -10,04
- MACD-HTF-001: n=11950 PF 0,70434931 (+170219,35/|241668,94|) net -71449,59 fees 39493,51 wr 26,85% exp -5,98
- RSI-PB-ADX-001: n=33 PF 0,95908017 (+748,29/|780,21|) net -31,93 fees 131,66 wr 33,33% exp -0,97
- VOL-BO-001: n=10275 PF 0,79061200 (+303918,60/|384409,29|) net -80490,69 fees 35192,55 wr 31,95% exp -7,83
- TREND-PB-001: n=538 PF 0,67118183 (+6690,96/|9968,93|) net -3277,97 fees 2125,66 wr 18,03% exp -6,09
- MTF-5M-15M-001: n=8 PF 5,11530910 (+298,20/|58,30|) net 239,90 fees 31,86 wr 37,50% exp 29,99
- MTF-15M-1H-001: n=2853 PF 0,78916704 (+101905,90/|129130,96|) net -27225,06 fees 10290,37 wr 31,09% exp -9,54

## 6. Validation results
- RSI-PB-ATR-001: n=4 PF 0,62060581 (+95,24/|153,46|) net -58,22 fees 15,98 wr 50,00% exp -14,56
- DONCHIAN-ATR-001: n=4848 PF 0,67540943 (+110287,73/|163290,18|) net -53002,45 fees 17517,34 wr 29,10% exp -10,93
- VWAP-PB-001: n=8091 PF 0,70597609 (+93997,69/|133145,71|) net -39148,02 fees 29925,31 wr 19,10% exp -4,84
- DONCHIAN-VOL-001: n=4821 PF 0,67153229 (+108782,86/|161992,01|) net -53209,14 fees 17336,91 wr 28,56% exp -11,04
- VWAP-PB-EMA-001: n=5366 PF 0,79579787 (+73450,17/|92297,52|) net -18847,35 fees 20404,62 wr 19,72% exp -3,51
- DONCHIAN-TREND-001: n=4294 PF 0,69844413 (+100637,35/|144087,91|) net -43450,55 fees 15661,19 wr 29,39% exp -10,12
- ST-EMA-001: n=12300 PF 0,66104499 (+150446,23/|227588,49|) net -77142,26 fees 39908,19 wr 20,56% exp -6,27
- EMA-RSI-HTF-001: n=864 PF 0,85779634 (+26240,34/|30590,41|) net -4350,07 fees 3441,62 wr 26,39% exp -5,03
- EMA-RSI-ADX-001: n=639 PF 0,53056177 (+11985,44/|22590,09|) net -10604,65 fees 2498,00 wr 23,00% exp -16,60
- MACD-HTF-001: n=3913 PF 0,70468247 (+54217,08/|76938,31|) net -22721,23 fees 14960,80 wr 27,78% exp -5,81
- RSI-PB-ADX-001: n=10 PF 0,63114010 (+243,68/|386,10|) net -142,42 fees 40,16 wr 60,00% exp -14,24
- VOL-BO-001: n=3342 PF 0,67067029 (+80121,65/|119465,04|) net -39343,39 fees 12331,33 wr 29,89% exp -11,77
- TREND-PB-001: n=158 PF 0,43111436 (+1270,69/|2947,45|) net -1676,76 fees 630,59 wr 17,72% exp -10,61
- MTF-5M-15M-001: n=2 PF 0,00000000 (+0,00/|51,60|) net -51,60 fees 7,98 wr 0,00% exp -25,80
- MTF-15M-1H-001: n=958 PF 0,77837555 (+32267,37/|41454,76|) net -9187,39 fees 3653,40 wr 29,75% exp -9,59

## 7. OOS results
- RSI-PB-ATR-001: n=2 PF 0,00000000 (+0,00/|52,52|) net -52,52 fees 8,02 wr 0,00% exp -26,26
- DONCHIAN-ATR-001: n=4295 PF 0,71828514 (+99111,27/|137983,19|) net -38871,91 fees 15349,42 wr 28,75% exp -9,05
- VWAP-PB-001: n=8122 PF 0,54764406 (+68809,65/|125646,67|) net -56837,02 fees 28834,81 wr 15,14% exp -7,00
- DONCHIAN-VOL-001: n=4274 PF 0,72367078 (+99347,91/|137283,29|) net -37935,38 fees 15341,43 wr 28,85% exp -8,88
- VWAP-PB-EMA-001: n=5603 PF 0,52747817 (+48968,83/|92835,74|) net -43866,91 fees 20558,38 wr 14,81% exp -7,83
- DONCHIAN-TREND-001: n=3838 PF 0,71150657 (+90551,09/|127266,69|) net -36715,60 fees 13824,97 wr 28,84% exp -9,57
- ST-EMA-001: n=12348 PF 0,61129121 (+126083,48/|206257,63|) net -80174,15 fees 38912,14 wr 18,68% exp -6,49
- EMA-RSI-HTF-001: n=750 PF 0,70650986 (+18369,94/|26000,97|) net -7631,03 fees 2946,25 wr 24,00% exp -10,17
- EMA-RSI-ADX-001: n=514 PF 0,72136360 (+12098,08/|16771,12|) net -4673,05 fees 2020,77 wr 26,46% exp -9,09
- MACD-HTF-001: n=3329 PF 0,61997117 (+40999,94/|66132,01|) net -25132,07 fees 12461,23 wr 25,05% exp -7,55
- RSI-PB-ADX-001: n=12 PF 2,66934639 (+613,48/|229,82|) net 383,65 fees 47,78 wr 41,67% exp 31,97
- VOL-BO-001: n=2940 PF 0,75579378 (+76410,49/|101099,65|) net -24689,16 fees 10920,95 wr 31,09% exp -8,40
- TREND-PB-001: n=168 PF 0,36043104 (+778,44/|2159,76|) net -1381,32 fees 669,84 wr 13,10% exp -8,22
- MTF-5M-15M-001: n=0 PF N/A (+0,00/|0,00|) net 0,00 fees 0,00 wr 0,00% exp 0,00
- MTF-15M-1H-001: n=896 PF 0,67112438 (+25639,72/|38204,12|) net -12564,40 fees 3287,14 wr 28,91% exp -14,02

## 8. Walk-forward results
- RSI-PB-ATR-001: windows 240, with trades 0, empty 240 (PF=N/A), finite median PF N/A, test net 0,00, positive-window 0,0%. NO_LOSSES excluded from median.
- DONCHIAN-ATR-001: windows 240, with trades 230, empty 10 (PF=N/A), finite median PF 0,00000000, test net -7582,84, positive-window 36,5%. NO_LOSSES excluded from median.
- VWAP-PB-001: windows 240, with trades 228, empty 12 (PF=N/A), finite median PF 0,02911234, test net -9809,31, positive-window 25,4%. NO_LOSSES excluded from median.
- DONCHIAN-VOL-001: windows 240, with trades 228, empty 12 (PF=N/A), finite median PF 0,00000000, test net -7163,95, positive-window 38,2%. NO_LOSSES excluded from median.
- VWAP-PB-EMA-001: windows 240, with trades 213, empty 27 (PF=N/A), finite median PF 0,00000000, test net -7653,81, positive-window 23,5%. NO_LOSSES excluded from median.
- DONCHIAN-TREND-001: windows 240, with trades 223, empty 17 (PF=N/A), finite median PF 0,00000000, test net -3624,55, positive-window 37,2%. NO_LOSSES excluded from median.
- ST-EMA-001: windows 240, with trades 240, empty 0 (PF=N/A), finite median PF 0,34944413, test net -13403,04, positive-window 30,4%. NO_LOSSES excluded from median.
- EMA-RSI-HTF-001: windows 240, with trades 71, empty 169 (PF=N/A), finite median PF 0,00000000, test net -769,23, positive-window 36,6%. NO_LOSSES excluded from median.
- EMA-RSI-ADX-001: windows 240, with trades 39, empty 201 (PF=N/A), finite median PF 0,00000000, test net 1010,57, positive-window 43,6%. NO_LOSSES excluded from median.
- MACD-HTF-001: windows 240, with trades 171, empty 69 (PF=N/A), finite median PF 0,00000000, test net -1059,01, positive-window 29,8%. NO_LOSSES excluded from median.
- RSI-PB-ADX-001: windows 240, with trades 1, empty 239 (PF=N/A), finite median PF 0,00000000, test net -10,39, positive-window 0,0%. NO_LOSSES excluded from median.
- VOL-BO-001: windows 240, with trades 177, empty 63 (PF=N/A), finite median PF 0,00000000, test net -7656,26, positive-window 27,7%. NO_LOSSES excluded from median.
- TREND-PB-001: windows 240, with trades 6, empty 234 (PF=N/A), finite median PF 0,00000000, test net 9,36, positive-window 16,7%. NO_LOSSES excluded from median.
- MTF-5M-15M-001: windows 80, with trades 1, empty 79 (PF=N/A), finite median PF 0,00000000, test net -10,39, positive-window 0,0%. NO_LOSSES excluded from median.
- MTF-15M-1H-001: windows 80, with trades 71, empty 9 (PF=N/A), finite median PF 0,00000000, test net -1621,73, positive-window 33,8%. NO_LOSSES excluded from median.

## 9. Symbol robustness
- DONCHIAN-ATR-001: symbols 10, PF>1 0, PF<1 10, median PF 0,721, mean PF 0,723, worst decile 0,589, best decile 0,870, top-2 |net| share 38,6%. 
- DONCHIAN-VOL-001: symbols 10, PF>1 0, PF<1 10, median PF 0,721, mean PF 0,729, worst decile 0,579, best decile 0,898, top-2 |net| share 39,6%. 
- DONCHIAN-TREND-001: symbols 10, PF>1 0, PF<1 10, median PF 0,691, mean PF 0,718, worst decile 0,599, best decile 0,824, top-2 |net| share 35,8%. 
- EMA-RSI-HTF-001: symbols 10, PF>1 0, PF<1 10, median PF 0,706, mean PF 0,683, worst decile 0,201, best decile 0,889, top-2 |net| share 38,5%. 
- EMA-RSI-ADX-001: symbols 10, PF>1 2, PF<1 8, median PF 0,846, mean PF 0,770, worst decile 0,299, best decile 1,003, top-2 |net| share 62,6%. 
- MACD-HTF-001: symbols 10, PF>1 0, PF<1 10, median PF 0,611, mean PF 0,621, worst decile 0,449, best decile 0,749, top-2 |net| share 31,6%. 
- RSI-PB-ADX-001: symbols 6, PF>1 2, PF<1 4, median PF 2,393, mean PF 2,393, worst decile 0,536, best decile 0,536, top-2 |net| share 77,6%. 
- RSI-PB-ATR-001: symbols 2, PF>1 0, PF<1 2, median PF N/A, mean PF N/A, worst decile N/A, best decile N/A, top-2 |net| share 100,0%. SYMBOL_CONCENTRATION: top two symbols hold >= 80% of |net|.
- VWAP-PB-001: symbols 10, PF>1 0, PF<1 10, median PF 0,523, mean PF 0,539, worst decile 0,446, best decile 0,585, top-2 |net| share 23,8%. 
- VWAP-PB-EMA-001: symbols 10, PF>1 0, PF<1 10, median PF 0,529, mean PF 0,525, worst decile 0,394, best decile 0,615, top-2 |net| share 26,1%. 
- ST-EMA-001: symbols 10, PF>1 0, PF<1 10, median PF 0,594, mean PF 0,603, worst decile 0,549, best decile 0,663, top-2 |net| share 24,1%. 
- VOL-BO-001: symbols 10, PF>1 0, PF<1 10, median PF 0,739, mean PF 0,762, worst decile 0,570, best decile 0,940, top-2 |net| share 45,5%. 
- TREND-PB-001: symbols 10, PF>1 0, PF<1 10, median PF 0,496, mean PF 0,449, worst decile 0,115, best decile 0,650, top-2 |net| share 40,9%. 
- MTF-5M-15M-001: symbols 0, PF>1 0, PF<1 0, median PF N/A, mean PF N/A, worst decile N/A, best decile N/A, top-2 |net| share 0,0%. 
- MTF-15M-1H-001: symbols 10, PF>1 0, PF<1 10, median PF 0,715, mean PF 0,693, worst decile 0,459, best decile 0,800, top-2 |net| share 35,8%. 

## 10. Timeframe robustness
- DONCHIAN-ATR-001: 5m PF 0,62997583; 15m PF 0,77834338; 1h PF 0,80485303
- DONCHIAN-VOL-001: 5m PF 0,64559998; 15m PF 0,77890950; 1h PF 0,79346790
- DONCHIAN-TREND-001: 5m PF 0,63645544; 15m PF 0,75054171; 1h PF 0,81177334
- EMA-RSI-HTF-001: 5m PF 0,88985802; 15m PF 0,47569869; 1h PF 0,72936078
- EMA-RSI-ADX-001: 5m PF 0,66021210; 15m PF 0,80726378; 1h PF 0,74711701
- MACD-HTF-001: 5m PF 0,53379514; 15m PF 0,65720511; 1h PF 0,72584751
- RSI-PB-ADX-001: 5m PF 192,15049877; 15m PF 0,85712104; 1h PF N/A
- RSI-PB-ATR-001: 5m PF N/A; 15m PF 0,00000000; 1h PF N/A
- VWAP-PB-001: 5m PF 0,49181630; 15m PF 0,54657716; 1h PF 0,62592876
- VWAP-PB-EMA-001: 5m PF 0,41761662; 15m PF 0,63990367; 1h PF 0,53144926
- ST-EMA-001: 5m PF 0,49988807; 15m PF 0,67822104; 1h PF 0,72600844
- VOL-BO-001: 5m PF 0,71265646; 15m PF 0,78343049; 1h PF 0,80659725
- TREND-PB-001: 5m PF 0,34527864; 15m PF 0,36661242; 1h PF 0,40178803
- MTF-5M-15M-001: 5m PF N/A; 15m PF N/A; 1h PF N/A
- MTF-15M-1H-001: 5m PF N/A; 15m PF 0,67112438; 1h PF N/A

## 11. LONG / SHORT
- DONCHIAN-ATR-001: Combined PF 0,71828514 (+99111,27/|137983,19|); LONG PF 0,89009688 (+68237,68/|76663,21|); SHORT PF 0,50348337 (+30873,59/|61319,98|). Same W/|L| operator. Not an average of book PFs.
- DONCHIAN-VOL-001: Combined PF 0,72367078 (+99347,91/|137283,29|); LONG PF 0,90030045 (+68032,39/|75566,31|); SHORT PF 0,50740529 (+31315,52/|61716,98|). Same W/|L| operator. Not an average of book PFs.
- DONCHIAN-TREND-001: Combined PF 0,71150657 (+90551,09/|127266,69|); LONG PF 0,88586209 (+65555,60/|74002,03|); SHORT PF 0,46926974 (+24995,49/|53264,66|). Same W/|L| operator. Not an average of book PFs.
- EMA-RSI-HTF-001: Combined PF 0,70650986 (+18369,94/|26000,97|); LONG PF 0,82596611 (+11244,64/|13613,93|); SHORT PF 0,57522203 (+7125,30/|12387,05|). Same W/|L| operator. Not an average of book PFs.
- EMA-RSI-ADX-001: Combined PF 0,72136360 (+12098,08/|16771,12|); LONG PF 0,94227254 (+7687,63/|8158,61|); SHORT PF 0,51209723 (+4410,44/|8612,51|). Same W/|L| operator. Not an average of book PFs.
- MACD-HTF-001: Combined PF 0,61997117 (+40999,94/|66132,01|); LONG PF 0,78817262 (+29567,69/|37514,24|); SHORT PF 0,39948058 (+11432,24/|28617,77|). Same W/|L| operator. Not an average of book PFs.
- RSI-PB-ADX-001: Combined PF 2,66934639 (+613,48/|229,82|); LONG PF 0,00000000 (+0,00/|52,42|); SHORT PF 3,45810872 (+613,48/|177,40|). Same W/|L| operator. Not an average of book PFs.
- RSI-PB-ATR-001: Combined PF 0,00000000 (+0,00/|52,52|); LONG PF N/A (+0,00/|0,00|); SHORT PF 0,00000000 (+0,00/|52,52|). Same W/|L| operator. Not an average of book PFs.
- VWAP-PB-001: Combined PF 0,54764406 (+68809,65/|125646,67|); LONG PF 0,66590314 (+41332,73/|62070,19|); SHORT PF 0,43218686 (+27476,92/|63576,48|). Same W/|L| operator. Not an average of book PFs.
- VWAP-PB-EMA-001: Combined PF 0,52747817 (+48968,83/|92835,74|); LONG PF 0,69455972 (+33853,29/|48740,65|); SHORT PF 0,34279405 (+15115,53/|44095,09|). Same W/|L| operator. Not an average of book PFs.
- ST-EMA-001: Combined PF 0,61129121 (+126083,48/|206257,63|); LONG PF 0,73485180 (+87844,65/|119540,63|); SHORT PF 0,44096115 (+38238,82/|86716,99|). Same W/|L| operator. Not an average of book PFs.
- VOL-BO-001: Combined PF 0,75579378 (+76410,49/|101099,65|); LONG PF 0,96841772 (+53326,88/|55065,99|); SHORT PF 0,50145064 (+23083,61/|46033,67|). Same W/|L| operator. Not an average of book PFs.
- TREND-PB-001: Combined PF 0,36043104 (+778,44/|2159,76|); LONG PF 0,41764476 (+491,32/|1176,40|); SHORT PF 0,29198583 (+287,13/|983,36|). Same W/|L| operator. Not an average of book PFs.
- MTF-5M-15M-001: Combined PF N/A (+0,00/|0,00|); LONG PF N/A (+0,00/|0,00|); SHORT PF N/A (+0,00/|0,00|). Same W/|L| operator. Not an average of book PFs.
- MTF-15M-1H-001: Combined PF 0,67112438 (+25639,72/|38204,12|); LONG PF 0,84106686 (+19838,01/|23586,73|); SHORT PF 0,39690399 (+5801,70/|14617,39|). Same W/|L| operator. Not an average of book PFs.

## 12. Regime analysis
- RSI-PB-ATR-001 LOW_VOLATILITY: n=2 PF INSUFFICIENT_SAMPLE net -52,52
- RSI-PB-ATR-001 TRANSITION: n=0 PF INSUFFICIENT_SAMPLE net 0,00
- RSI-PB-ATR-001 RANGE: n=0 PF INSUFFICIENT_SAMPLE net 0,00
- RSI-PB-ATR-001 BEAR: n=0 PF INSUFFICIENT_SAMPLE net 0,00
- DONCHIAN-ATR-001 LOW_VOLATILITY: n=3571 PF 0,69279790 net -29674,81
- DONCHIAN-ATR-001 RANGE: n=395 PF 0,73071048 net -5186,34
- VWAP-PB-001 LOW_VOLATILITY: n=6261 PF 0,48898621 net -40926,96
- DONCHIAN-ATR-001 TRANSITION: n=133 PF 0,70151750 net -2743,74
- VWAP-PB-001 RANGE: n=943 PF 0,67267544 net -6029,52
- VWAP-PB-001 TRANSITION: n=389 PF 0,57323564 net -4440,04
- DONCHIAN-ATR-001 BEAR: n=196 PF 0,90204339 net -1267,02
- VWAP-PB-001 BEAR: n=529 PF 0,67485166 net -5440,50
- DONCHIAN-VOL-001 LOW_VOLATILITY: n=3561 PF 0,70160250 net -28660,36
- DONCHIAN-VOL-001 RANGE: n=383 PF 0,76604079 net -4319,48
- DONCHIAN-VOL-001 TRANSITION: n=134 PF 0,68008166 net -2994,19
- DONCHIAN-VOL-001 BEAR: n=196 PF 0,85378229 net -1961,36
- VWAP-PB-EMA-001 LOW_VOLATILITY: n=4267 PF 0,50210083 net -28913,20
- VWAP-PB-EMA-001 RANGE: n=679 PF 0,62051186 net -5356,04
- VWAP-PB-EMA-001 TRANSITION: n=283 PF 0,49235744 net -4163,54
- VWAP-PB-EMA-001 BEAR: n=374 PF 0,56351565 net -5434,13
- DONCHIAN-TREND-001 LOW_VOLATILITY: n=3212 PF 0,69150130 net -27874,65
- DONCHIAN-TREND-001 RANGE: n=361 PF 0,72173366 net -5119,73
- DONCHIAN-TREND-001 TRANSITION: n=107 PF 0,69755865 net -2304,19
- DONCHIAN-TREND-001 BEAR: n=158 PF 0,86992050 net -1417,03
- ST-EMA-001 LOW_VOLATILITY: n=10616 PF 0,56602231 net -61047,14
- ST-EMA-001 RANGE: n=927 PF 0,70744221 net -7659,04
- ST-EMA-001 TRANSITION: n=323 PF 0,78138097 net -3122,97
- EMA-RSI-HTF-001 LOW_VOLATILITY: n=588 PF 0,74642942 net -4257,51
- EMA-RSI-HTF-001 RANGE: n=75 PF 0,32578925 net -2638,79
- EMA-RSI-HTF-001 TRANSITION: n=33 PF 0,60378101 net -799,45
- EMA-RSI-HTF-001 BEAR: n=54 PF 1,01974115 net 64,73
- EMA-RSI-ADX-001 LOW_VOLATILITY: n=434 PF 0,69786237 net -3782,76
- EMA-RSI-ADX-001 RANGE: n=45 PF 0,64491186 net -776,73
- EMA-RSI-ADX-001 TRANSITION: n=13 PF INSUFFICIENT_SAMPLE net -190,60
- EMA-RSI-ADX-001 BEAR: n=22 PF 1,05939494 net 77,05
- ST-EMA-001 BEAR: n=482 PF 0,66785021 net -8345,01
- MACD-HTF-001 LOW_VOLATILITY: n=2702 PF 0,58022945 net -19183,81
- MACD-HTF-001 RANGE: n=336 PF 0,78050126 net -1809,53
- MACD-HTF-001 TRANSITION: n=123 PF 0,52690444 net -2371,57
- MACD-HTF-001 BEAR: n=168 PF 0,75368919 net -1767,15
- RSI-PB-ADX-001 LOW_VOLATILITY: n=10 PF INSUFFICIENT_SAMPLE net 296,48
- RSI-PB-ADX-001 RANGE: n=2 PF INSUFFICIENT_SAMPLE net 87,17
- RSI-PB-ADX-001 TRANSITION: n=0 PF INSUFFICIENT_SAMPLE net 0,00
- RSI-PB-ADX-001 BEAR: n=0 PF INSUFFICIENT_SAMPLE net 0,00
- VOL-BO-001 LOW_VOLATILITY: n=2449 PF 0,74115371 net -18637,88
- VOL-BO-001 RANGE: n=270 PF 0,79166271 net -2861,22
- VOL-BO-001 BEAR: n=126 PF 0,94781840 net -442,48
- VOL-BO-001 TRANSITION: n=95 PF 0,60079887 net -2747,58
- TREND-PB-001 LOW_VOLATILITY: n=154 PF 0,36111776 net -1169,90
- TREND-PB-001 RANGE: n=10 PF INSUFFICIENT_SAMPLE net -99,80
- TREND-PB-001 TRANSITION: n=1 PF INSUFFICIENT_SAMPLE net -28,32
- TREND-PB-001 BEAR: n=3 PF INSUFFICIENT_SAMPLE net -83,29
- MTF-5M-15M-001 LOW_VOLATILITY: n=0 PF INSUFFICIENT_SAMPLE net 0,00
- MTF-5M-15M-001 TRANSITION: n=0 PF INSUFFICIENT_SAMPLE net 0,00
- MTF-15M-1H-001 TRANSITION: n=0 PF INSUFFICIENT_SAMPLE net 0,00
- MTF-15M-1H-001 LOW_VOLATILITY: n=702 PF 0,69817323 net -8461,73
- MTF-15M-1H-001 RANGE: n=194 PF 0,59655355 net -4102,68

## 13. Cost sensitivity
- DONCHIAN-ATR-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,71828514 (+99111,27/|137983,19|)
  - HIGH: PF 0,65472439 (+93055,17/|142128,76|)
  - STRESS: PF 0,60087205 (+87931,07/|146339,09|)
- DONCHIAN-VOL-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,72367078 (+99347,91/|137283,29|)
  - HIGH: PF 0,65971564 (+93276,64/|141389,16|)
  - STRESS: PF 0,60631380 (+88372,67/|145754,02|)
- DONCHIAN-TREND-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,71150657 (+90551,09/|127266,69|)
  - HIGH: PF 0,64939786 (+85219,83/|131229,00|)
  - STRESS: PF 0,59542018 (+80611,90/|135386,57|)
- EMA-RSI-HTF-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,70650986 (+18369,94/|26000,97|)
  - HIGH: PF 0,64506867 (+17784,02/|27569,19|)
  - STRESS: PF 0,59176405 (+17254,18/|29157,19|)
- EMA-RSI-ADX-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,72136360 (+12098,08/|16771,12|)
  - HIGH: PF 0,65519611 (+11682,14/|17829,99|)
  - STRESS: PF 0,59802612 (+11297,56/|18891,42|)
- MACD-HTF-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,61997117 (+40999,94/|66132,01|)
  - HIGH: PF 0,52711067 (+37811,92/|71734,31|)
  - STRESS: PF 0,45527848 (+35128,65/|77158,60|)
- RSI-PB-ADX-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 2,66934639 (+613,48/|229,82|)
  - HIGH: PF 2,39261534 (+600,16/|250,84|)
  - STRESS: PF 2,14839309 (+589,28/|274,29|)
- RSI-PB-ATR-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,00000000 (+0,00/|52,52|)
  - HIGH: PF 0,00000000 (+0,00/|58,54|)
  - STRESS: PF 0,00000000 (+0,00/|64,56|)
- VWAP-PB-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,54764406 (+68809,65/|125646,67|)
  - HIGH: PF 0,45658509 (+62713,95/|137354,34|)
  - STRESS: PF 0,38665694 (+57363,73/|148358,20|)
- VWAP-PB-EMA-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,52747817 (+48968,83/|92835,74|)
  - HIGH: PF 0,43826016 (+44872,32/|102387,40|)
  - STRESS: PF 0,37089603 (+41384,31/|111579,28|)
- ST-EMA-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,61129121 (+126083,48/|206257,63|)
  - HIGH: PF 0,52754755 (+112958,31/|214119,67|)
  - STRESS: PF 0,46108427 (+101802,21/|220788,74|)
- VOL-BO-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,75579378 (+76410,49/|101099,65|)
  - HIGH: PF 0,68921612 (+72231,54/|104802,45|)
  - STRESS: PF 0,63071891 (+68515,87/|108631,39|)
- TREND-PB-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,36043104 (+778,44/|2159,76|)
  - HIGH: PF 0,27599219 (+717,09/|2598,23|)
  - STRESS: PF 0,21791507 (+663,06/|3042,74|)
- MTF-5M-15M-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF N/A (+0,00/|0,00|)
  - HIGH: PF N/A (+0,00/|0,00|)
  - STRESS: PF N/A (+0,00/|0,00|)
- MTF-15M-1H-001: BASE vs HIGH compared on OOS W/|L|
  - BASE: PF 0,67112438 (+25639,72/|38204,12|)
  - HIGH: PF 0,61826789 (+24369,22/|39415,30|)
  - STRESS: PF 0,57604141 (+23392,04/|40608,26|)

## 14. Parameter stability
Each candidate uses its primary frozen parameters. Neighbor grids (Donchian 20/30/55) are stored on the candidate and must be run as new versions if changed. A single-point spike would be flagged PARAMETER_FRAGILE; this run does not search for a max-PF parameter.

## 15. Trade frequency
- DONCHIAN-ATR-001: OOS trades 4295, median hold 565.4 min
- DONCHIAN-VOL-001: OOS trades 4274, median hold 568.8 min
- DONCHIAN-TREND-001: OOS trades 3838, median hold 537.1 min
- EMA-RSI-HTF-001: OOS trades 750, median hold 558.0 min
- EMA-RSI-ADX-001: OOS trades 514, median hold 760.6 min
- MACD-HTF-001: OOS trades 3329, median hold 188.7 min
- RSI-PB-ADX-001: OOS trades 12, median hold 81.9 min
- RSI-PB-ATR-001: OOS trades 2, median hold 45.0 min
- VWAP-PB-001: OOS trades 8122, median hold 111.8 min
- VWAP-PB-EMA-001: OOS trades 5603, median hold 103.5 min
- ST-EMA-001: OOS trades 12348, median hold 210.2 min
- VOL-BO-001: OOS trades 2940, median hold 551.2 min
- TREND-PB-001: OOS trades 168, median hold 207.9 min
- MTF-5M-15M-001: OOS trades 0, median hold N/A
- MTF-15M-1H-001: OOS trades 896, median hold 378.7 min

## 16. MFE/MAE diagnostics
Post-signal only. Not used to create entries.
- DONCHIAN-ATR-001: MFE 0,0157 MAE 0,0116 r1 -0,0002 r3 -0,0004 r5 -0,0002 r10 -0,0001
- DONCHIAN-VOL-001: MFE 0,0158 MAE 0,0115 r1 -0,0002 r3 -0,0002 r5 0,0000 r10 0,0001
- DONCHIAN-TREND-001: MFE 0,0159 MAE 0,0118 r1 -0,0002 r3 -0,0004 r5 -0,0001 r10 0,0001
- EMA-RSI-HTF-001: MFE 0,0140 MAE 0,0109 r1 -0,0004 r3 -0,0005 r5 -0,0009 r10 -0,0010
- EMA-RSI-ADX-001: MFE 0,0150 MAE 0,0112 r1 -0,0001 r3 0,0000 r5 -0,0004 r10 -0,0002
- MACD-HTF-001: MFE 0,0097 MAE 0,0075 r1 -0,0003 r3 -0,0003 r5 -0,0002 r10 -0,0007
- RSI-PB-ADX-001: MFE 0,0366 MAE 0,0088 r1 0,0066 r3 0,0023 r5 0,0031 r10 0,0024
- RSI-PB-ATR-001: MFE 0,0060 MAE 0,0051 r1 -0,0009 r3 -0,0047 r5 -0,0029 r10 -0,0043
- VWAP-PB-001: MFE 0,0074 MAE 0,0059 r1 -0,0003 r3 -0,0004 r5 -0,0006 r10 -0,0003
- VWAP-PB-EMA-001: MFE 0,0074 MAE 0,0060 r1 -0,0004 r3 -0,0006 r5 -0,0009 r10 -0,0008
- ST-EMA-001: MFE 0,0104 MAE 0,0079 r1 -0,0005 r3 -0,0006 r5 -0,0005 r10 -0,0006
- VOL-BO-001: MFE 0,0165 MAE 0,0119 r1 -0,0004 r3 -0,0003 r5 -0,0003 r10 -0,0001
- TREND-PB-001: MFE 0,0056 MAE 0,0049 r1 -0,0015 r3 -0,0026 r5 -0,0020 r10 -0,0020
- MTF-5M-15M-001: MFE N/A MAE N/A r1 N/A r3 N/A r5 N/A r10 N/A
- MTF-15M-1H-001: MFE 0,0144 MAE 0,0118 r1 -0,0008 r3 -0,0008 r5 -0,0009 r10 -0,0017

## 17. Exit research
Exit family: strategy opposite-signal plus Isolated book fixed SL/TP (unchanged Risk Engine). ATR trailing / extra TP grids are remaining work. Do not put sizing inside strategies.

## 18. Failure analysis
- DONCHIAN-ATR-001: OOS expectancy/PF did not clear 1 after costs
- DONCHIAN-VOL-001: OOS expectancy/PF did not clear 1 after costs
- DONCHIAN-TREND-001: OOS expectancy/PF did not clear 1 after costs
- EMA-RSI-HTF-001: OOS expectancy/PF did not clear 1 after costs
- EMA-RSI-ADX-001: OOS expectancy/PF did not clear 1 after costs
- MACD-HTF-001: OOS expectancy/PF did not clear 1 after costs
- RSI-PB-ADX-001: this run does not promote VALIDATED_FOR_PAPER; remains a hypothesis only
- RSI-PB-ATR-001: insufficient OOS sample on this run
- VWAP-PB-001: OOS expectancy/PF did not clear 1 after costs
- VWAP-PB-EMA-001: OOS expectancy/PF did not clear 1 after costs
- ST-EMA-001: OOS expectancy/PF did not clear 1 after costs
- VOL-BO-001: OOS expectancy/PF did not clear 1 after costs
- TREND-PB-001: OOS expectancy/PF did not clear 1 after costs
- MTF-5M-15M-001: insufficient OOS sample on this run
- MTF-15M-1H-001: OOS expectancy/PF did not clear 1 after costs

## 19. Candidates eligible for Paper
None. Paper requires human approval after a later freeze + untouched OOS + walk-forward. LIVE remains OFF.

## 20. Candidates rejected / Phase 2
Rejected for Paper: all 15 (this run does not promote VALIDATED_FOR_PAPER).
Rejected as broken: none.
Still hypotheses (not Paper): DONCHIAN-ATR-001, DONCHIAN-VOL-001, DONCHIAN-TREND-001, EMA-RSI-HTF-001, EMA-RSI-ADX-001, MACD-HTF-001, RSI-PB-ADX-001, RSI-PB-ATR-001, VWAP-PB-001, VWAP-PB-EMA-001, ST-EMA-001, VOL-BO-001, TREND-PB-001, MTF-5M-15M-001, MTF-15M-1H-001.
- DONCHIAN-ATR-001: OOS expectancy/PF did not clear 1 after costs
- DONCHIAN-VOL-001: OOS expectancy/PF did not clear 1 after costs
- DONCHIAN-TREND-001: OOS expectancy/PF did not clear 1 after costs
- EMA-RSI-HTF-001: OOS expectancy/PF did not clear 1 after costs
- EMA-RSI-ADX-001: OOS expectancy/PF did not clear 1 after costs
- MACD-HTF-001: OOS expectancy/PF did not clear 1 after costs
- RSI-PB-ADX-001: this run does not promote VALIDATED_FOR_PAPER; remains a hypothesis only
- RSI-PB-ATR-001: insufficient OOS sample on this run
- VWAP-PB-001: OOS expectancy/PF did not clear 1 after costs
- VWAP-PB-EMA-001: OOS expectancy/PF did not clear 1 after costs
- ST-EMA-001: OOS expectancy/PF did not clear 1 after costs
- VOL-BO-001: OOS expectancy/PF did not clear 1 after costs
- TREND-PB-001: OOS expectancy/PF did not clear 1 after costs
- MTF-5M-15M-001: insufficient OOS sample on this run
- MTF-15M-1H-001: OOS expectancy/PF did not clear 1 after costs

## 21. Remaining research questions
- After this representative liquid-coin run, does any candidate still deserve a later freeze + untouched OOS (not a full 528 until asked)?
- ATR-based SL/TP versus book percents: is there a stable region, not a 1.73 ATR spike?
- Signal correlation / overlap for a future multi-coin book (diagnostics only).
- Neighbor parameter dispersion on Donchian 20/30/55 after Phase 2, without touching OOS.

## Run notes
- Strategy research. LIVE disabled. Frozen catalog templates unchanged. Phase=2. IndicatorModel=B windows. MetricsVersion=fixed PF=W/|L|. CodeVersion=research-layer-1.
- Checkpoint d:\Sources\TradingProject\artifacts\strategy-research\phase-2. Candle cache d:\Sources\TradingProject\artifacts\strategy-validation-cache. MaxParallel=2. Window 365 days. Timeframes 5m/15m/1h.
- OOS is labeled and not used to retune candidates. This run does not freeze or promote VALIDATED_FOR_PAPER.
