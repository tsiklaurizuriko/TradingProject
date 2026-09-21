# Model B Wave-5 strategy-router report

Existing signals only. No new entries, no FrozenRisk/LOW change, LIVE off, not VALIDATED_FOR_PAPER.
Question: can a causal regime/score router turn the existing weak pool into selected trades with better OOS expectancy than a same-count random subset?

**Verdict: REJECTED. NO ROBUST CONDITIONAL ALPHA FOUND.**

OOS TOP1-ALL PF **0.8946** vs random same-count **0.9088** vs bottom-ranked **0.9150**. The ranking is inverted. Unfiltered OOS PF is 0.8943. Isolated LOW $1000 / 0.5% / 3x was not changed. LIVE was not enabled.

## 1. Existing strategy universe

Candidates: **43**. Frozen five + ResearchRegistry.All + Wave-2 natives + OHLCV-only advanced/alpha templates. OI/funding/pairs/XS/router templates were not harvested (DATA_UNAVAILABLE or circular).

| Id | Family | Kind | Parent / native |
|---|---|---|---|
| FROZEN-ema_rsi_trend | TREND | parent_filter | ema_rsi_trend |
| FROZEN-macd_trend | TREND | parent_filter | macd_trend |
| FROZEN-rsi_pullback | TREND | parent_filter | rsi_pullback |
| FROZEN-bollinger_reversion | MEAN REVERSION | parent_filter | bollinger_reversion |
| FROZEN-donchian_breakout | BREAKOUT / TREND | parent_filter | donchian_breakout |
| DONCHIAN-ATR-001 | BREAKOUT / TREND | parent_filter | donchian_breakout |
| DONCHIAN-VOL-001 | BREAKOUT / TREND | parent_filter | donchian_breakout |
| DONCHIAN-TREND-001 | BREAKOUT / TREND | parent_filter | donchian_breakout |
| EMA-RSI-HTF-001 | TREND | parent_filter | ema_rsi_trend |
| EMA-RSI-ADX-001 | TREND | parent_filter | ema_rsi_trend |
| MACD-HTF-001 | TREND | parent_filter | macd_trend |
| RSI-PB-ADX-001 | TREND | parent_filter | rsi_pullback |
| RSI-PB-ATR-001 | TREND | parent_filter | rsi_pullback |
| VWAP-PB-001 | TREND | native | vwap_reclaim |
| VWAP-PB-EMA-001 | TREND | native | vwap_reclaim |
| ST-EMA-001 | TREND | native | supertrend_ema |
| VOL-BO-001 | BREAKOUT / TREND | parent_filter | donchian_breakout |
| TREND-PB-001 | TREND | native | trend_pullback |
| MTF-5M-15M-001 | TREND | parent_filter | rsi_pullback |
| MTF-15M-1H-001 | BREAKOUT / TREND | parent_filter | donchian_breakout |
| W2-SWEEP-RECLAIM-001 | WAVE2 | native | liquidity_sweep_reclaim |
| W2-FAILED-BO-VOL-001 | WAVE2 | native | failed_breakout_volume |
| W2-SQUEEZE-EXP-001 | WAVE2 | native | squeeze_expansion |
| W2-VOL-EXHAUST-001 | WAVE2 | native | volume_exhaustion |
| W2-VWAP-EXT-001 | WAVE2 | native | vwap_reclaim_extension |
| W2-BOS-PULLBACK-001 | WAVE2 | native | structure_bos_pullback |
| W2-DISPLACE-001 | WAVE2 | native | displacement_retrace |
| W2-REGIME-SWITCH-001 | WAVE2 | native | regime_switch_mr_bo |
| ALPHA-turtle_tsm | BREAKOUT / TREND | parent_filter | turtle_tsm |
| ALPHA-vwap_pullback_trend | TREND / STRUCTURE | parent_filter | vwap_pullback_trend |
| ALPHA-volatility_breakout | BREAKOUT / TREND | parent_filter | volatility_breakout |
| ALPHA-supertrend_ema_trend | TREND / STRUCTURE | parent_filter | supertrend_ema_trend |
| ALPHA-vp_vwap_reversion | MEAN REVERSION | parent_filter | vp_vwap_reversion |
| ALPHA-liq_sweep_reversal | REVERSAL | parent_filter | liq_sweep_reversal |
| ALPHA-liq_sweep_continuation | BREAKOUT / TREND | parent_filter | liq_sweep_continuation |
| ALPHA-vwap_deviation_reversion | MEAN REVERSION | parent_filter | vwap_deviation_reversion |
| ALPHA-vwap_breakout_volume | BREAKOUT / TREND | parent_filter | vwap_breakout_volume |
| ALPHA-failed_breakout_reversal | REVERSAL | parent_filter | failed_breakout_reversal |
| ALPHA-vol_squeeze_structure | BREAKOUT / TREND | parent_filter | vol_squeeze_structure |
| ALPHA-market_structure_trend | TREND / STRUCTURE | parent_filter | market_structure_trend |
| ALPHA-market_structure_pullback | TREND / STRUCTURE | parent_filter | market_structure_pullback |
| ALPHA-atr_normalized_momentum | BREAKOUT / TREND | parent_filter | atr_normalized_momentum |
| ALPHA-zscore_mean_reversion | MEAN REVERSION | parent_filter | zscore_mean_reversion |

## 2. Signal-event dataset

Events: **667973**. Symbols 10. Timeframes 15m, 1h, 5m.
IS 463147 / VAL 114004 / OOS 90822.
Fill = next open. SL 2% then TP 4%. Round-trip BASE cost 0.12%. Features at signal close only.

| Timeframe | Events | LONG | SHORT |
|---|---:|---:|---:|
| 15m | 234265 | 114742 | 119523 |
| 1h | 125597 | 61035 | 64562 |
| 5m | 308111 | 151524 | 156587 |

## 3. Conditional expectancy (IS only, descriptive)

Not used to trade OOS. Router scores are purged 90d windows, not these tables.

| Strategy | Bucket | n | PF | Exp | WR |
|---|---|---:|---:|---:|---:|
| ALPHA-atr_normalized_momentum | ALL | 13924 | 0.9342 | -0.09% | 34.12% |
| ALPHA-atr_normalized_momentum | VOL_LOW | 6294 | 0.9398 | -0.08% | 34.25% |
| ALPHA-atr_normalized_momentum | VOL_MID | 2571 | 0.9257 | -0.11% | 33.92% |
| ALPHA-atr_normalized_momentum | VOL_HIGH | 5059 | 0.9316 | -0.10% | 34.06% |
| ALPHA-atr_normalized_momentum | TREND_UP | 7091 | 0.9463 | -0.08% | 34.41% |
| ALPHA-atr_normalized_momentum | TREND_DOWN | 6833 | 0.9218 | -0.11% | 33.82% |
| ALPHA-atr_normalized_momentum | BTC_UP | 7208 | 0.9415 | -0.08% | 34.30% |
| ALPHA-atr_normalized_momentum | BTC_DOWN | 6716 | 0.9265 | -0.10% | 33.93% |
| ALPHA-atr_normalized_momentum | BREADTH_HIGH | 6363 | 0.9400 | -0.08% | 34.26% |
| ALPHA-atr_normalized_momentum | BREADTH_LOW | 7150 | 0.9298 | -0.10% | 34.01% |
| ALPHA-atr_normalized_momentum | RS_HIGH | 6417 | 0.9313 | -0.10% | 34.05% |
| ALPHA-atr_normalized_momentum | RS_LOW | 6890 | 0.9415 | -0.08% | 34.30% |
| ALPHA-atr_normalized_momentum | VOL_SHOCK | 10721 | 0.9353 | -0.09% | 34.15% |
| ALPHA-atr_normalized_momentum | LONG | 6878 | 0.9307 | -0.10% | 34.04% |
| ALPHA-atr_normalized_momentum | SHORT | 7046 | 0.9377 | -0.09% | 34.20% |
| ALPHA-failed_breakout_reversal | ALL | 12423 | 0.8664 | -0.19% | 32.45% |
| ALPHA-failed_breakout_reversal | VOL_LOW | 4426 | 0.8997 | -0.14% | 33.28% |
| ALPHA-failed_breakout_reversal | VOL_MID | 2397 | 0.8684 | -0.19% | 32.50% |
| ALPHA-failed_breakout_reversal | VOL_HIGH | 5600 | 0.8398 | -0.23% | 31.77% |
| ALPHA-failed_breakout_reversal | TREND_UP | 6362 | 0.8619 | -0.20% | 32.33% |
| ALPHA-failed_breakout_reversal | TREND_DOWN | 6061 | 0.8712 | -0.19% | 32.57% |
| ALPHA-failed_breakout_reversal | BTC_UP | 6233 | 0.8566 | -0.21% | 32.20% |
| ALPHA-failed_breakout_reversal | BTC_DOWN | 6190 | 0.8763 | -0.18% | 32.70% |
| ALPHA-failed_breakout_reversal | BREADTH_HIGH | 5560 | 0.8515 | -0.22% | 32.07% |
| ALPHA-failed_breakout_reversal | BREADTH_LOW | 6520 | 0.8831 | -0.17% | 32.87% |
| ALPHA-failed_breakout_reversal | RS_HIGH | 5896 | 0.8839 | -0.17% | 32.89% |
| ALPHA-failed_breakout_reversal | RS_LOW | 5973 | 0.8466 | -0.22% | 31.94% |
| ALPHA-failed_breakout_reversal | VOL_SHOCK | 9378 | 0.8488 | -0.22% | 32.00% |
| ALPHA-failed_breakout_reversal | LONG | 6200 | 0.8748 | -0.18% | 32.66% |
| ALPHA-failed_breakout_reversal | SHORT | 6223 | 0.8580 | -0.21% | 32.24% |
| ALPHA-liq_sweep_continuation | ALL | 23208 | 0.9362 | -0.09% | 34.17% |
| ALPHA-liq_sweep_continuation | VOL_LOW | 7555 | 0.9255 | -0.11% | 33.91% |
| ALPHA-liq_sweep_continuation | VOL_MID | 3996 | 0.8877 | -0.16% | 32.98% |
| ALPHA-liq_sweep_continuation | VOL_HIGH | 11657 | 0.9603 | -0.06% | 34.74% |
| ALPHA-liq_sweep_continuation | TREND_UP | 11527 | 0.9392 | -0.09% | 34.24% |
| ALPHA-liq_sweep_continuation | TREND_DOWN | 11681 | 0.9333 | -0.09% | 34.10% |
| ALPHA-liq_sweep_continuation | BTC_UP | 11289 | 0.9101 | -0.13% | 33.54% |
| ALPHA-liq_sweep_continuation | BTC_DOWN | 11919 | 0.9614 | -0.05% | 34.77% |
| ALPHA-liq_sweep_continuation | BREADTH_HIGH | 10611 | 0.9569 | -0.06% | 34.66% |
| ALPHA-liq_sweep_continuation | BREADTH_LOW | 12292 | 0.9231 | -0.11% | 33.85% |
| ALPHA-liq_sweep_continuation | RS_HIGH | 10816 | 0.9421 | -0.08% | 34.31% |
| ALPHA-liq_sweep_continuation | RS_LOW | 11554 | 0.9309 | -0.10% | 34.04% |
| ALPHA-liq_sweep_continuation | VOL_SHOCK | 23208 | 0.9362 | -0.09% | 34.17% |
| ALPHA-liq_sweep_continuation | LONG | 11266 | 0.9449 | -0.08% | 34.38% |
| ALPHA-liq_sweep_continuation | SHORT | 11942 | 0.9281 | -0.10% | 33.97% |
| ALPHA-liq_sweep_reversal | ALL | 14901 | 0.8632 | -0.20% | 32.37% |
| ALPHA-liq_sweep_reversal | VOL_LOW | 5995 | 0.8695 | -0.19% | 32.53% |
| ALPHA-liq_sweep_reversal | VOL_MID | 2903 | 0.9037 | -0.14% | 33.38% |
| ALPHA-liq_sweep_reversal | VOL_HIGH | 6003 | 0.8378 | -0.24% | 31.72% |
| ALPHA-liq_sweep_reversal | TREND_UP | 7430 | 0.8417 | -0.23% | 31.82% |
| ALPHA-liq_sweep_reversal | TREND_DOWN | 7471 | 0.8850 | -0.17% | 32.91% |
| ALPHA-liq_sweep_reversal | BTC_UP | 7408 | 0.8538 | -0.21% | 32.13% |
| ALPHA-liq_sweep_reversal | BTC_DOWN | 7493 | 0.8726 | -0.18% | 32.60% |
| ALPHA-liq_sweep_reversal | BREADTH_HIGH | 6377 | 0.8515 | -0.22% | 32.07% |
| ALPHA-liq_sweep_reversal | BREADTH_LOW | 7817 | 0.8784 | -0.18% | 32.75% |
| ALPHA-liq_sweep_reversal | RS_HIGH | 6898 | 0.8993 | -0.14% | 33.27% |
| ALPHA-liq_sweep_reversal | RS_LOW | 7371 | 0.8400 | -0.23% | 31.77% |
| ALPHA-liq_sweep_reversal | VOL_SHOCK | 10943 | 0.8507 | -0.22% | 32.05% |
| ALPHA-liq_sweep_reversal | LONG | 7937 | 0.8682 | -0.19% | 32.49% |
| ALPHA-liq_sweep_reversal | SHORT | 6964 | 0.8576 | -0.21% | 32.22% |
| ALPHA-market_structure_pullback | ALL | 10309 | 0.8865 | -0.16% | 32.95% |
| ALPHA-market_structure_pullback | VOL_LOW | 5263 | 0.9062 | -0.13% | 33.44% |
| ALPHA-market_structure_pullback | VOL_MID | 1802 | 0.8217 | -0.26% | 31.30% |
| ALPHA-market_structure_pullback | VOL_HIGH | 3244 | 0.8915 | -0.16% | 33.08% |
| ALPHA-market_structure_pullback | TREND_UP | 5382 | 0.8787 | -0.17% | 32.76% |
| ALPHA-market_structure_pullback | TREND_DOWN | 4927 | 0.8950 | -0.15% | 33.16% |
| ALPHA-market_structure_pullback | BTC_UP | 5511 | 0.8620 | -0.20% | 32.34% |
| ALPHA-market_structure_pullback | BTC_DOWN | 4798 | 0.9152 | -0.12% | 33.66% |
| ALPHA-market_structure_pullback | BREADTH_HIGH | 4806 | 0.9206 | -0.11% | 33.79% |
| ALPHA-market_structure_pullback | BREADTH_LOW | 4884 | 0.8666 | -0.19% | 32.45% |
| ALPHA-market_structure_pullback | RS_HIGH | 4790 | 0.8795 | -0.17% | 32.78% |
| ALPHA-market_structure_pullback | RS_LOW | 5030 | 0.8957 | -0.15% | 33.18% |
| ALPHA-market_structure_pullback | VOL_SHOCK | 5888 | 0.9007 | -0.14% | 33.31% |
| ALPHA-market_structure_pullback | LONG | 5272 | 0.8909 | -0.16% | 33.06% |
| ALPHA-market_structure_pullback | SHORT | 5037 | 0.8819 | -0.17% | 32.84% |
| ALPHA-market_structure_trend | ALL | 11156 | 0.9097 | -0.13% | 33.52% |
| ALPHA-market_structure_trend | VOL_LOW | 4798 | 0.8856 | -0.16% | 32.93% |
| ALPHA-market_structure_trend | VOL_MID | 2013 | 0.8582 | -0.21% | 32.24% |
| ALPHA-market_structure_trend | VOL_HIGH | 4345 | 0.9617 | -0.05% | 34.78% |
| ALPHA-market_structure_trend | TREND_UP | 5641 | 0.9117 | -0.13% | 33.58% |
| ALPHA-market_structure_trend | TREND_DOWN | 5515 | 0.9075 | -0.13% | 33.47% |
| ALPHA-market_structure_trend | BTC_UP | 5720 | 0.8894 | -0.16% | 33.02% |
| ALPHA-market_structure_trend | BTC_DOWN | 5436 | 0.9313 | -0.10% | 34.05% |
| ALPHA-market_structure_trend | BREADTH_HIGH | 5272 | 0.9070 | -0.13% | 33.46% |
| ALPHA-market_structure_trend | BREADTH_LOW | 5729 | 0.9121 | -0.12% | 33.58% |
| ALPHA-market_structure_trend | RS_HIGH | 5081 | 0.8965 | -0.15% | 33.20% |
| ALPHA-market_structure_trend | RS_LOW | 5538 | 0.9203 | -0.11% | 33.78% |
| ALPHA-market_structure_trend | VOL_SHOCK | 9402 | 0.9228 | -0.11% | 33.84% |
| ALPHA-market_structure_trend | LONG | 5516 | 0.8962 | -0.15% | 33.19% |
| ALPHA-market_structure_trend | SHORT | 5640 | 0.9229 | -0.11% | 33.85% |
| ALPHA-supertrend_ema_trend | ALL | 2572 | 0.9188 | -0.12% | 33.75% |
| ALPHA-supertrend_ema_trend | VOL_LOW | 847 | 1.1628 | 0.21% | 39.20% |
| ALPHA-supertrend_ema_trend | VOL_MID | 466 | 0.7589 | -0.36% | 29.61% |
| ALPHA-supertrend_ema_trend | VOL_HIGH | 1259 | 0.8338 | -0.24% | 31.61% |
| ALPHA-supertrend_ema_trend | TREND_UP | 1268 | 0.9719 | -0.04% | 35.02% |
| ALPHA-supertrend_ema_trend | TREND_DOWN | 1304 | 0.8691 | -0.19% | 32.52% |
| ALPHA-supertrend_ema_trend | BTC_UP | 1322 | 0.9529 | -0.07% | 34.57% |
| ALPHA-supertrend_ema_trend | BTC_DOWN | 1250 | 0.8836 | -0.17% | 32.88% |
| ALPHA-supertrend_ema_trend | BREADTH_HIGH | 1163 | 0.9747 | -0.04% | 35.08% |
| ALPHA-supertrend_ema_trend | BREADTH_LOW | 1363 | 0.8744 | -0.18% | 32.65% |
| ALPHA-supertrend_ema_trend | RS_HIGH | 1149 | 0.9781 | -0.03% | 35.16% |
| ALPHA-supertrend_ema_trend | RS_LOW | 1212 | 0.8952 | -0.15% | 33.17% |
| ALPHA-supertrend_ema_trend | VOL_SHOCK | 2198 | 0.9006 | -0.14% | 33.30% |
| ALPHA-supertrend_ema_trend | LONG | 1268 | 0.9719 | -0.04% | 35.02% |
| ALPHA-supertrend_ema_trend | SHORT | 1304 | 0.8691 | -0.19% | 32.52% |
| ALPHA-turtle_tsm | ALL | 18875 | 0.9256 | -0.11% | 33.91% |
| ALPHA-turtle_tsm | VOL_LOW | 6938 | 0.9280 | -0.10% | 33.97% |
| ALPHA-turtle_tsm | VOL_MID | 3467 | 0.8894 | -0.16% | 33.03% |
| ALPHA-turtle_tsm | VOL_HIGH | 8470 | 0.9386 | -0.09% | 34.23% |
| ALPHA-turtle_tsm | TREND_UP | 9423 | 0.9519 | -0.07% | 34.54% |
| ALPHA-turtle_tsm | TREND_DOWN | 9452 | 0.8999 | -0.14% | 33.28% |
| ALPHA-turtle_tsm | BTC_UP | 9304 | 0.9056 | -0.13% | 33.43% |
| ALPHA-turtle_tsm | BTC_DOWN | 9571 | 0.9452 | -0.08% | 34.39% |
| ALPHA-turtle_tsm | BREADTH_HIGH | 8808 | 0.9502 | -0.07% | 34.50% |
| ALPHA-turtle_tsm | BREADTH_LOW | 9837 | 0.9031 | -0.14% | 33.36% |
| ALPHA-turtle_tsm | RS_HIGH | 8745 | 0.9476 | -0.07% | 34.44% |
| ALPHA-turtle_tsm | RS_LOW | 9398 | 0.9038 | -0.14% | 33.38% |
| ALPHA-turtle_tsm | VOL_SHOCK | 16715 | 0.9205 | -0.11% | 33.79% |
| ALPHA-turtle_tsm | LONG | 9234 | 0.9406 | -0.08% | 34.28% |
| ALPHA-turtle_tsm | SHORT | 9641 | 0.9113 | -0.13% | 33.56% |
| ALPHA-vol_squeeze_structure | ALL | 4321 | 0.8356 | -0.24% | 31.66% |
| ALPHA-vol_squeeze_structure | VOL_LOW | 1673 | 0.8691 | -0.19% | 32.52% |
| ALPHA-vol_squeeze_structure | VOL_MID | 999 | 0.8777 | -0.18% | 32.73% |
| ALPHA-vol_squeeze_structure | VOL_HIGH | 1649 | 0.7782 | -0.33% | 30.14% |
| ALPHA-vol_squeeze_structure | TREND_UP | 2296 | 0.8510 | -0.22% | 32.06% |
| ALPHA-vol_squeeze_structure | TREND_DOWN | 2025 | 0.8184 | -0.27% | 31.21% |
| ALPHA-vol_squeeze_structure | BTC_UP | 2403 | 0.8408 | -0.23% | 31.79% |
| ALPHA-vol_squeeze_structure | BTC_DOWN | 1918 | 0.8291 | -0.25% | 31.49% |
| ALPHA-vol_squeeze_structure | BREADTH_HIGH | 1974 | 0.8435 | -0.23% | 31.86% |
| ALPHA-vol_squeeze_structure | BREADTH_LOW | 2280 | 0.8291 | -0.25% | 31.49% |
| ALPHA-vol_squeeze_structure | RS_HIGH | 1976 | 0.8462 | -0.22% | 31.93% |
| ALPHA-vol_squeeze_structure | RS_LOW | 2118 | 0.8292 | -0.25% | 31.49% |
| ALPHA-vol_squeeze_structure | VOL_SHOCK | 4321 | 0.8356 | -0.24% | 31.66% |
| ALPHA-vol_squeeze_structure | LONG | 2089 | 0.8423 | -0.23% | 31.83% |
| ALPHA-vol_squeeze_structure | SHORT | 2232 | 0.8293 | -0.25% | 31.50% |
| ALPHA-volatility_breakout | ALL | 5974 | 0.8928 | -0.15% | 33.11% |
| ALPHA-volatility_breakout | VOL_LOW | 2282 | 0.8656 | -0.19% | 32.43% |
| ALPHA-volatility_breakout | VOL_MID | 1409 | 0.9587 | -0.06% | 34.71% |
| ALPHA-volatility_breakout | VOL_HIGH | 2283 | 0.8807 | -0.17% | 32.81% |
| ALPHA-volatility_breakout | TREND_UP | 3207 | 0.8956 | -0.15% | 33.18% |
| ALPHA-volatility_breakout | TREND_DOWN | 2767 | 0.8897 | -0.16% | 33.03% |
| ALPHA-volatility_breakout | BTC_UP | 3341 | 0.8699 | -0.19% | 32.54% |
| ALPHA-volatility_breakout | BTC_DOWN | 2633 | 0.9226 | -0.11% | 33.84% |
| ALPHA-volatility_breakout | BREADTH_HIGH | 2688 | 0.9125 | -0.12% | 33.59% |
| ALPHA-volatility_breakout | BREADTH_LOW | 3179 | 0.8795 | -0.17% | 32.78% |
| ALPHA-volatility_breakout | RS_HIGH | 2748 | 0.9302 | -0.10% | 34.02% |
| ALPHA-volatility_breakout | RS_LOW | 2926 | 0.8511 | -0.22% | 32.06% |
| ALPHA-volatility_breakout | VOL_SHOCK | 5974 | 0.8928 | -0.15% | 33.11% |
| ALPHA-volatility_breakout | LONG | 2855 | 0.9066 | -0.13% | 33.45% |
| ALPHA-volatility_breakout | SHORT | 3119 | 0.8804 | -0.17% | 32.80% |
| ALPHA-vp_vwap_reversion | ALL | 10151 | 0.8564 | -0.21% | 32.19% |
| ALPHA-vp_vwap_reversion | VOL_LOW | 5315 | 0.8809 | -0.17% | 32.81% |
| ALPHA-vp_vwap_reversion | VOL_MID | 1878 | 0.8222 | -0.26% | 31.31% |
| ALPHA-vp_vwap_reversion | VOL_HIGH | 2958 | 0.8349 | -0.24% | 31.64% |
| ALPHA-vp_vwap_reversion | TREND_UP | 5697 | 0.8163 | -0.27% | 31.16% |
| ALPHA-vp_vwap_reversion | TREND_DOWN | 4454 | 0.9095 | -0.13% | 33.52% |
| ALPHA-vp_vwap_reversion | BTC_UP | 5722 | 0.8225 | -0.26% | 31.32% |
| ALPHA-vp_vwap_reversion | BTC_DOWN | 4429 | 0.9016 | -0.14% | 33.33% |
| ALPHA-vp_vwap_reversion | BREADTH_HIGH | 4976 | 0.8486 | -0.22% | 31.99% |
| ALPHA-vp_vwap_reversion | BREADTH_LOW | 4637 | 0.8523 | -0.21% | 32.09% |
| ALPHA-vp_vwap_reversion | RS_HIGH | 4987 | 0.8623 | -0.20% | 32.34% |
| ALPHA-vp_vwap_reversion | RS_LOW | 4719 | 0.8454 | -0.23% | 31.91% |
| ALPHA-vp_vwap_reversion | VOL_SHOCK | 7226 | 0.8288 | -0.25% | 31.48% |
| ALPHA-vp_vwap_reversion | LONG | 4185 | 0.8721 | -0.18% | 32.59% |
| ALPHA-vp_vwap_reversion | SHORT | 5966 | 0.8455 | -0.23% | 31.91% |
| ALPHA-vwap_breakout_volume | ALL | 17820 | 0.9193 | -0.11% | 33.76% |
| ALPHA-vwap_breakout_volume | VOL_LOW | 6639 | 0.9129 | -0.12% | 33.60% |
| ALPHA-vwap_breakout_volume | VOL_MID | 3176 | 0.8782 | -0.18% | 32.75% |
| ALPHA-vwap_breakout_volume | VOL_HIGH | 8005 | 0.9413 | -0.08% | 34.29% |
| ALPHA-vwap_breakout_volume | TREND_UP | 8871 | 0.9423 | -0.08% | 34.31% |
| ALPHA-vwap_breakout_volume | TREND_DOWN | 8949 | 0.8969 | -0.15% | 33.21% |
| ALPHA-vwap_breakout_volume | BTC_UP | 8869 | 0.9113 | -0.13% | 33.57% |
| ALPHA-vwap_breakout_volume | BTC_DOWN | 8951 | 0.9272 | -0.10% | 33.95% |
| ALPHA-vwap_breakout_volume | BREADTH_HIGH | 8158 | 0.9364 | -0.09% | 34.18% |
| ALPHA-vwap_breakout_volume | BREADTH_LOW | 9446 | 0.9046 | -0.14% | 33.40% |
| ALPHA-vwap_breakout_volume | RS_HIGH | 8163 | 0.9204 | -0.11% | 33.79% |
| ALPHA-vwap_breakout_volume | RS_LOW | 8952 | 0.9142 | -0.12% | 33.63% |
| ALPHA-vwap_breakout_volume | VOL_SHOCK | 17820 | 0.9193 | -0.11% | 33.76% |
| ALPHA-vwap_breakout_volume | LONG | 8610 | 0.9256 | -0.11% | 33.91% |
| ALPHA-vwap_breakout_volume | SHORT | 9210 | 0.9134 | -0.12% | 33.62% |
| ALPHA-vwap_deviation_reversion | ALL | 9147 | 0.8768 | -0.18% | 32.71% |
| ALPHA-vwap_deviation_reversion | VOL_LOW | 4727 | 0.8508 | -0.22% | 32.05% |
| ALPHA-vwap_deviation_reversion | VOL_MID | 1774 | 0.8451 | -0.23% | 31.91% |
| ALPHA-vwap_deviation_reversion | VOL_HIGH | 2646 | 0.9471 | -0.07% | 34.43% |
| ALPHA-vwap_deviation_reversion | TREND_UP | 4948 | 0.8748 | -0.18% | 32.66% |
| ALPHA-vwap_deviation_reversion | TREND_DOWN | 4199 | 0.8792 | -0.17% | 32.77% |
| ALPHA-vwap_deviation_reversion | BTC_UP | 4945 | 0.8564 | -0.21% | 32.19% |
| ALPHA-vwap_deviation_reversion | BTC_DOWN | 4202 | 0.9012 | -0.14% | 33.32% |
| ALPHA-vwap_deviation_reversion | BREADTH_HIGH | 4592 | 0.8907 | -0.16% | 33.06% |
| ALPHA-vwap_deviation_reversion | BREADTH_LOW | 4217 | 0.8614 | -0.20% | 32.32% |
| ALPHA-vwap_deviation_reversion | RS_HIGH | 4406 | 0.9133 | -0.12% | 33.61% |
| ALPHA-vwap_deviation_reversion | RS_LOW | 4328 | 0.8390 | -0.24% | 31.75% |
| ALPHA-vwap_deviation_reversion | VOL_SHOCK | 6225 | 0.8805 | -0.17% | 32.80% |
| ALPHA-vwap_deviation_reversion | LONG | 4194 | 0.8703 | -0.19% | 32.55% |
| ALPHA-vwap_deviation_reversion | SHORT | 4953 | 0.8824 | -0.17% | 32.85% |
| ALPHA-vwap_pullback_trend | ALL | 11875 | 0.9579 | -0.06% | 34.69% |
| ALPHA-vwap_pullback_trend | VOL_LOW | 5846 | 0.9603 | -0.06% | 34.74% |
| ALPHA-vwap_pullback_trend | VOL_MID | 2171 | 0.9617 | -0.05% | 34.78% |
| ALPHA-vwap_pullback_trend | VOL_HIGH | 3858 | 0.9522 | -0.07% | 34.55% |
| ALPHA-vwap_pullback_trend | TREND_UP | 6195 | 0.9533 | -0.07% | 34.58% |
| ALPHA-vwap_pullback_trend | TREND_DOWN | 5680 | 0.9630 | -0.05% | 34.81% |
| ALPHA-vwap_pullback_trend | BTC_UP | 6430 | 0.9676 | -0.05% | 34.91% |
| ALPHA-vwap_pullback_trend | BTC_DOWN | 5445 | 0.9466 | -0.07% | 34.42% |
| ALPHA-vwap_pullback_trend | BREADTH_HIGH | 5348 | 0.9706 | -0.04% | 34.99% |
| ALPHA-vwap_pullback_trend | BREADTH_LOW | 5761 | 0.9541 | -0.06% | 34.59% |
| ALPHA-vwap_pullback_trend | RS_HIGH | 5622 | 0.9296 | -0.10% | 34.01% |
| ALPHA-vwap_pullback_trend | RS_LOW | 5696 | 0.9897 | -0.01% | 35.43% |
| ALPHA-vwap_pullback_trend | VOL_SHOCK | 7199 | 0.9738 | -0.04% | 35.06% |
| ALPHA-vwap_pullback_trend | LONG | 6195 | 0.9533 | -0.07% | 34.58% |
| ALPHA-vwap_pullback_trend | SHORT | 5680 | 0.9630 | -0.05% | 34.81% |
| ALPHA-zscore_mean_reversion | ALL | 13992 | 0.8987 | -0.14% | 33.25% |
| ALPHA-zscore_mean_reversion | VOL_LOW | 6602 | 0.9188 | -0.12% | 33.75% |
| ALPHA-zscore_mean_reversion | VOL_MID | 2652 | 0.9391 | -0.09% | 34.24% |
| ALPHA-zscore_mean_reversion | VOL_HIGH | 4738 | 0.8495 | -0.22% | 32.02% |
| ALPHA-zscore_mean_reversion | TREND_UP | 7352 | 0.8982 | -0.15% | 33.24% |
| ALPHA-zscore_mean_reversion | TREND_DOWN | 6640 | 0.8992 | -0.14% | 33.27% |
| ALPHA-zscore_mean_reversion | BTC_UP | 7408 | 0.9088 | -0.13% | 33.50% |
| ALPHA-zscore_mean_reversion | BTC_DOWN | 6584 | 0.8874 | -0.16% | 32.97% |
| ALPHA-zscore_mean_reversion | BREADTH_HIGH | 6767 | 0.8907 | -0.16% | 33.06% |
| ALPHA-zscore_mean_reversion | BREADTH_LOW | 7025 | 0.9044 | -0.14% | 33.40% |
| ALPHA-zscore_mean_reversion | RS_HIGH | 6728 | 0.9209 | -0.11% | 33.80% |
| ALPHA-zscore_mean_reversion | RS_LOW | 6680 | 0.8762 | -0.18% | 32.69% |
| ALPHA-zscore_mean_reversion | VOL_SHOCK | 12370 | 0.8835 | -0.17% | 32.88% |
| ALPHA-zscore_mean_reversion | LONG | 6962 | 0.8997 | -0.14% | 33.28% |
| ALPHA-zscore_mean_reversion | SHORT | 7030 | 0.8977 | -0.15% | 33.23% |
| DONCHIAN-ATR-001 | ALL | 18503 | 0.9167 | -0.12% | 33.70% |
| DONCHIAN-ATR-001 | VOL_LOW | 6748 | 0.9174 | -0.12% | 33.71% |
| DONCHIAN-ATR-001 | VOL_MID | 3336 | 0.8813 | -0.17% | 32.82% |
| DONCHIAN-ATR-001 | VOL_HIGH | 8419 | 0.9305 | -0.10% | 34.03% |
| DONCHIAN-ATR-001 | TREND_UP | 9266 | 0.9461 | -0.08% | 34.41% |
| DONCHIAN-ATR-001 | TREND_DOWN | 9237 | 0.8879 | -0.16% | 32.99% |
| DONCHIAN-ATR-001 | BTC_UP | 9172 | 0.9079 | -0.13% | 33.48% |
| DONCHIAN-ATR-001 | BTC_DOWN | 9331 | 0.9254 | -0.11% | 33.91% |
| DONCHIAN-ATR-001 | BREADTH_HIGH | 8586 | 0.9344 | -0.09% | 34.13% |
| DONCHIAN-ATR-001 | BREADTH_LOW | 9683 | 0.8987 | -0.14% | 33.25% |
| DONCHIAN-ATR-001 | RS_HIGH | 8573 | 0.9254 | -0.11% | 33.91% |
| DONCHIAN-ATR-001 | RS_LOW | 9199 | 0.9079 | -0.13% | 33.48% |
| DONCHIAN-ATR-001 | VOL_SHOCK | 16806 | 0.9118 | -0.13% | 33.58% |
| DONCHIAN-ATR-001 | LONG | 9010 | 0.9272 | -0.10% | 33.95% |
| DONCHIAN-ATR-001 | SHORT | 9493 | 0.9069 | -0.13% | 33.46% |
| DONCHIAN-TREND-001 | ALL | 16550 | 0.9322 | -0.10% | 34.07% |
| DONCHIAN-TREND-001 | VOL_LOW | 6230 | 0.9291 | -0.10% | 34.00% |
| DONCHIAN-TREND-001 | VOL_MID | 2965 | 0.8837 | -0.17% | 32.88% |
| DONCHIAN-TREND-001 | VOL_HIGH | 7355 | 0.9549 | -0.06% | 34.62% |
| DONCHIAN-TREND-001 | TREND_UP | 8099 | 0.9500 | -0.07% | 34.50% |
| DONCHIAN-TREND-001 | TREND_DOWN | 8451 | 0.9154 | -0.12% | 33.66% |
| DONCHIAN-TREND-001 | BTC_UP | 8253 | 0.9257 | -0.11% | 33.91% |
| DONCHIAN-TREND-001 | BTC_DOWN | 8297 | 0.9387 | -0.09% | 34.23% |
| DONCHIAN-TREND-001 | BREADTH_HIGH | 7661 | 0.9561 | -0.06% | 34.64% |
| DONCHIAN-TREND-001 | BREADTH_LOW | 8704 | 0.9125 | -0.12% | 33.59% |
| DONCHIAN-TREND-001 | RS_HIGH | 7579 | 0.9541 | -0.06% | 34.60% |
| DONCHIAN-TREND-001 | RS_LOW | 8271 | 0.9162 | -0.12% | 33.68% |
| DONCHIAN-TREND-001 | VOL_SHOCK | 14721 | 0.9337 | -0.09% | 34.11% |
| DONCHIAN-TREND-001 | LONG | 8099 | 0.9500 | -0.07% | 34.50% |
| DONCHIAN-TREND-001 | SHORT | 8451 | 0.9154 | -0.12% | 33.66% |
| DONCHIAN-VOL-001 | ALL | 18152 | 0.9220 | -0.11% | 33.83% |
| DONCHIAN-VOL-001 | VOL_LOW | 6823 | 0.9190 | -0.11% | 33.75% |
| DONCHIAN-VOL-001 | VOL_MID | 3244 | 0.8791 | -0.17% | 32.77% |
| DONCHIAN-VOL-001 | VOL_HIGH | 8085 | 0.9421 | -0.08% | 34.31% |
| DONCHIAN-VOL-001 | TREND_UP | 9120 | 0.9411 | -0.08% | 34.29% |
| DONCHIAN-VOL-001 | TREND_DOWN | 9032 | 0.9029 | -0.14% | 33.36% |
| DONCHIAN-VOL-001 | BTC_UP | 9013 | 0.9067 | -0.13% | 33.45% |
| DONCHIAN-VOL-001 | BTC_DOWN | 9139 | 0.9373 | -0.09% | 34.19% |
| DONCHIAN-VOL-001 | BREADTH_HIGH | 8285 | 0.9398 | -0.08% | 34.25% |
| DONCHIAN-VOL-001 | BREADTH_LOW | 9633 | 0.9032 | -0.14% | 33.36% |
| DONCHIAN-VOL-001 | RS_HIGH | 8339 | 0.9241 | -0.11% | 33.88% |
| DONCHIAN-VOL-001 | RS_LOW | 9109 | 0.9120 | -0.13% | 33.58% |
| DONCHIAN-VOL-001 | VOL_SHOCK | 18152 | 0.9220 | -0.11% | 33.83% |
| DONCHIAN-VOL-001 | LONG | 8723 | 0.9307 | -0.10% | 34.04% |
| DONCHIAN-VOL-001 | SHORT | 9429 | 0.9140 | -0.12% | 33.63% |
| EMA-RSI-ADX-001 | ALL | 3253 | 0.9433 | -0.08% | 34.34% |
| EMA-RSI-ADX-001 | VOL_LOW | 1183 | 0.9390 | -0.09% | 34.23% |
| EMA-RSI-ADX-001 | VOL_MID | 544 | 0.8482 | -0.22% | 31.99% |
| EMA-RSI-ADX-001 | VOL_HIGH | 1526 | 0.9822 | -0.02% | 35.26% |
| EMA-RSI-ADX-001 | TREND_UP | 1562 | 0.9316 | -0.10% | 34.06% |
| EMA-RSI-ADX-001 | TREND_DOWN | 1691 | 0.9541 | -0.06% | 34.59% |
| EMA-RSI-ADX-001 | BTC_UP | 1719 | 0.9426 | -0.08% | 34.32% |
| EMA-RSI-ADX-001 | BTC_DOWN | 1534 | 0.9440 | -0.08% | 34.35% |
| EMA-RSI-ADX-001 | BREADTH_HIGH | 1492 | 0.9229 | -0.11% | 33.85% |
| EMA-RSI-ADX-001 | BREADTH_LOW | 1687 | 0.9676 | -0.05% | 34.91% |
| EMA-RSI-ADX-001 | RS_HIGH | 1399 | 0.9067 | -0.13% | 33.45% |
| EMA-RSI-ADX-001 | RS_LOW | 1639 | 0.9644 | -0.05% | 34.84% |
| EMA-RSI-ADX-001 | VOL_SHOCK | 2328 | 0.9534 | -0.07% | 34.58% |
| EMA-RSI-ADX-001 | LONG | 1562 | 0.9316 | -0.10% | 34.06% |
| EMA-RSI-ADX-001 | SHORT | 1691 | 0.9541 | -0.06% | 34.59% |
| EMA-RSI-HTF-001 | ALL | 4217 | 0.9999 | -0.00% | 35.67% |
| EMA-RSI-HTF-001 | VOL_LOW | 1933 | 1.0471 | 0.06% | 36.73% |
| EMA-RSI-HTF-001 | VOL_MID | 756 | 0.8700 | -0.19% | 32.54% |
| EMA-RSI-HTF-001 | VOL_HIGH | 1528 | 1.0086 | 0.01% | 35.86% |
| EMA-RSI-HTF-001 | TREND_UP | 2035 | 0.9855 | -0.02% | 35.33% |
| EMA-RSI-HTF-001 | TREND_DOWN | 2182 | 1.0136 | 0.02% | 35.98% |
| EMA-RSI-HTF-001 | BTC_UP | 2362 | 0.9863 | -0.02% | 35.35% |
| EMA-RSI-HTF-001 | BTC_DOWN | 1855 | 1.0174 | 0.02% | 36.06% |
| EMA-RSI-HTF-001 | BREADTH_HIGH | 1876 | 1.0161 | 0.02% | 36.03% |
| EMA-RSI-HTF-001 | BREADTH_LOW | 2222 | 1.0010 | 0.00% | 35.69% |
| EMA-RSI-HTF-001 | RS_HIGH | 1842 | 0.9421 | -0.08% | 34.31% |
| EMA-RSI-HTF-001 | RS_LOW | 2136 | 1.0271 | 0.04% | 36.28% |
| EMA-RSI-HTF-001 | VOL_SHOCK | 3153 | 1.0006 | 0.00% | 35.68% |
| EMA-RSI-HTF-001 | LONG | 2035 | 0.9855 | -0.02% | 35.33% |
| EMA-RSI-HTF-001 | SHORT | 2182 | 1.0136 | 0.02% | 35.98% |
| FROZEN-bollinger_reversion | ALL | 2806 | 0.9499 | -0.07% | 34.50% |
| FROZEN-bollinger_reversion | VOL_LOW | 1205 | 1.0008 | 0.00% | 35.68% |
| FROZEN-bollinger_reversion | VOL_MID | 571 | 0.8169 | -0.27% | 31.17% |
| FROZEN-bollinger_reversion | VOL_HIGH | 1030 | 0.9692 | -0.04% | 34.95% |
| FROZEN-bollinger_reversion | TREND_UP | 1547 | 0.9454 | -0.08% | 34.39% |
| FROZEN-bollinger_reversion | TREND_DOWN | 1259 | 0.9556 | -0.06% | 34.63% |
| FROZEN-bollinger_reversion | BTC_UP | 1519 | 0.9722 | -0.04% | 35.02% |
| FROZEN-bollinger_reversion | BTC_DOWN | 1287 | 0.9241 | -0.11% | 33.88% |
| FROZEN-bollinger_reversion | BREADTH_HIGH | 1177 | 0.9534 | -0.07% | 34.58% |
| FROZEN-bollinger_reversion | BREADTH_LOW | 1450 | 0.9493 | -0.07% | 34.48% |
| FROZEN-bollinger_reversion | RS_HIGH | 1304 | 0.9472 | -0.07% | 34.43% |
| FROZEN-bollinger_reversion | RS_LOW | 1308 | 0.9654 | -0.05% | 34.86% |
| FROZEN-bollinger_reversion | VOL_SHOCK | 2020 | 0.9544 | -0.06% | 34.60% |
| FROZEN-bollinger_reversion | LONG | 1546 | 0.9737 | -0.04% | 35.06% |
| FROZEN-bollinger_reversion | SHORT | 1260 | 0.9214 | -0.11% | 33.81% |
| FROZEN-donchian_breakout | ALL | 19118 | 0.9201 | -0.11% | 33.78% |
| FROZEN-donchian_breakout | VOL_LOW | 7041 | 0.9204 | -0.11% | 33.79% |
| FROZEN-donchian_breakout | VOL_MID | 3512 | 0.8896 | -0.16% | 33.03% |
| FROZEN-donchian_breakout | VOL_HIGH | 8565 | 0.9325 | -0.10% | 34.08% |
| FROZEN-donchian_breakout | TREND_UP | 9546 | 0.9516 | -0.07% | 34.54% |
| FROZEN-donchian_breakout | TREND_DOWN | 9572 | 0.8894 | -0.16% | 33.02% |
| FROZEN-donchian_breakout | BTC_UP | 9441 | 0.8997 | -0.14% | 33.28% |
| FROZEN-donchian_breakout | BTC_DOWN | 9677 | 0.9403 | -0.08% | 34.27% |
| FROZEN-donchian_breakout | BREADTH_HIGH | 8926 | 0.9437 | -0.08% | 34.35% |
| FROZEN-donchian_breakout | BREADTH_LOW | 9954 | 0.8974 | -0.15% | 33.22% |
| FROZEN-donchian_breakout | RS_HIGH | 8880 | 0.9403 | -0.08% | 34.27% |
| FROZEN-donchian_breakout | RS_LOW | 9493 | 0.9000 | -0.14% | 33.29% |
| FROZEN-donchian_breakout | VOL_SHOCK | 16864 | 0.9153 | -0.12% | 33.66% |
| FROZEN-donchian_breakout | LONG | 9340 | 0.9338 | -0.09% | 34.11% |
| FROZEN-donchian_breakout | SHORT | 9778 | 0.9072 | -0.13% | 33.46% |
| FROZEN-ema_rsi_trend | ALL | 6554 | 0.9366 | -0.09% | 34.18% |
| FROZEN-ema_rsi_trend | VOL_LOW | 3155 | 0.9521 | -0.07% | 34.55% |
| FROZEN-ema_rsi_trend | VOL_MID | 1125 | 0.8768 | -0.18% | 32.71% |
| FROZEN-ema_rsi_trend | VOL_HIGH | 2274 | 0.9454 | -0.08% | 34.39% |
| FROZEN-ema_rsi_trend | TREND_UP | 3135 | 0.9293 | -0.10% | 34.00% |
| FROZEN-ema_rsi_trend | TREND_DOWN | 3419 | 0.9433 | -0.08% | 34.34% |
| FROZEN-ema_rsi_trend | BTC_UP | 3599 | 0.9764 | -0.03% | 35.12% |
| FROZEN-ema_rsi_trend | BTC_DOWN | 2955 | 0.8896 | -0.16% | 33.03% |
| FROZEN-ema_rsi_trend | BREADTH_HIGH | 3018 | 0.9359 | -0.09% | 34.16% |
| FROZEN-ema_rsi_trend | BREADTH_LOW | 3371 | 0.9377 | -0.09% | 34.20% |
| FROZEN-ema_rsi_trend | RS_HIGH | 2870 | 0.8841 | -0.17% | 32.89% |
| FROZEN-ema_rsi_trend | RS_LOW | 3345 | 0.9831 | -0.02% | 35.28% |
| FROZEN-ema_rsi_trend | VOL_SHOCK | 4777 | 0.9421 | -0.08% | 34.31% |
| FROZEN-ema_rsi_trend | LONG | 3135 | 0.9293 | -0.10% | 34.00% |
| FROZEN-ema_rsi_trend | SHORT | 3419 | 0.9433 | -0.08% | 34.34% |
| FROZEN-macd_trend | ALL | 12578 | 0.9590 | -0.06% | 34.71% |
| FROZEN-macd_trend | VOL_LOW | 5638 | 0.9545 | -0.06% | 34.60% |
| FROZEN-macd_trend | VOL_MID | 2225 | 0.9602 | -0.06% | 34.74% |
| FROZEN-macd_trend | VOL_HIGH | 4715 | 0.9638 | -0.05% | 34.83% |
| FROZEN-macd_trend | TREND_UP | 6169 | 0.9822 | -0.02% | 35.26% |
| FROZEN-macd_trend | TREND_DOWN | 6409 | 0.9369 | -0.09% | 34.19% |
| FROZEN-macd_trend | BTC_UP | 6469 | 0.9521 | -0.07% | 34.55% |
| FROZEN-macd_trend | BTC_DOWN | 6109 | 0.9663 | -0.05% | 34.88% |
| FROZEN-macd_trend | BREADTH_HIGH | 5814 | 0.9853 | -0.02% | 35.33% |
| FROZEN-macd_trend | BREADTH_LOW | 6434 | 0.9385 | -0.09% | 34.22% |
| FROZEN-macd_trend | RS_HIGH | 5741 | 0.9510 | -0.07% | 34.52% |
| FROZEN-macd_trend | RS_LOW | 6278 | 0.9515 | -0.07% | 34.53% |
| FROZEN-macd_trend | VOL_SHOCK | 9997 | 0.9644 | -0.05% | 34.84% |
| FROZEN-macd_trend | LONG | 6219 | 0.9728 | -0.04% | 35.04% |
| FROZEN-macd_trend | SHORT | 6359 | 0.9456 | -0.08% | 34.39% |
| FROZEN-rsi_pullback | ALL | 62 | 0.7969 | -0.30% | 30.65% |
| FROZEN-rsi_pullback | VOL_HIGH | 50 | 0.7013 | -0.46% | 28.00% |
| FROZEN-rsi_pullback | TREND_UP | 38 | 0.4073 | -1.03% | 18.42% |
| FROZEN-rsi_pullback | TREND_DOWN | 24 | 1.8036 | 0.86% | 50.00% |
| FROZEN-rsi_pullback | BTC_UP | 20 | 0.6012 | -0.64% | 25.00% |
| FROZEN-rsi_pullback | BTC_DOWN | 42 | 0.9017 | -0.14% | 33.33% |
| FROZEN-rsi_pullback | BREADTH_HIGH | 24 | 1.8036 | 0.86% | 50.00% |
| FROZEN-rsi_pullback | BREADTH_LOW | 36 | 0.4354 | -0.97% | 19.44% |
| FROZEN-rsi_pullback | RS_HIGH | 30 | 0.4509 | -0.94% | 20.00% |
| FROZEN-rsi_pullback | RS_LOW | 26 | 1.1270 | 0.17% | 38.46% |
| FROZEN-rsi_pullback | VOL_SHOCK | 62 | 0.7969 | -0.30% | 30.65% |
| FROZEN-rsi_pullback | LONG | 24 | 1.8037 | 0.86% | 50.00% |
| FROZEN-rsi_pullback | SHORT | 38 | 0.4073 | -1.03% | 18.42% |
| MACD-HTF-001 | ALL | 10783 | 0.9816 | -0.03% | 35.24% |
| MACD-HTF-001 | VOL_LOW | 4807 | 0.9532 | -0.07% | 34.57% |
| MACD-HTF-001 | VOL_MID | 1939 | 0.9875 | -0.02% | 35.38% |
| MACD-HTF-001 | VOL_HIGH | 4037 | 1.0132 | 0.02% | 35.97% |
| MACD-HTF-001 | TREND_UP | 5287 | 0.9741 | -0.04% | 35.07% |
| MACD-HTF-001 | TREND_DOWN | 5496 | 0.9888 | -0.02% | 35.41% |
| MACD-HTF-001 | BTC_UP | 5542 | 0.9715 | -0.04% | 35.01% |
| MACD-HTF-001 | BTC_DOWN | 5241 | 0.9923 | -0.01% | 35.49% |
| MACD-HTF-001 | BREADTH_HIGH | 4830 | 1.0102 | 0.01% | 35.90% |
| MACD-HTF-001 | BREADTH_LOW | 5651 | 0.9646 | -0.05% | 34.84% |
| MACD-HTF-001 | RS_HIGH | 4858 | 0.9596 | -0.06% | 34.73% |
| MACD-HTF-001 | RS_LOW | 5392 | 0.9991 | -0.00% | 35.65% |
| MACD-HTF-001 | VOL_SHOCK | 8495 | 0.9837 | -0.02% | 35.29% |
| MACD-HTF-001 | LONG | 5262 | 0.9862 | -0.02% | 35.35% |
| MACD-HTF-001 | SHORT | 5521 | 0.9772 | -0.03% | 35.14% |
| MTF-15M-1H-001 | ALL | 4866 | 0.9563 | -0.06% | 34.65% |
| MTF-15M-1H-001 | VOL_LOW | 1936 | 0.9394 | -0.09% | 34.25% |
| MTF-15M-1H-001 | VOL_MID | 863 | 0.9465 | -0.08% | 34.41% |
| MTF-15M-1H-001 | VOL_HIGH | 2067 | 0.9765 | -0.03% | 35.12% |
| MTF-15M-1H-001 | TREND_UP | 2494 | 0.9426 | -0.08% | 34.32% |
| MTF-15M-1H-001 | TREND_DOWN | 2372 | 0.9709 | -0.04% | 34.99% |
| MTF-15M-1H-001 | BTC_UP | 2580 | 0.9337 | -0.09% | 34.11% |
| MTF-15M-1H-001 | BTC_DOWN | 2286 | 0.9823 | -0.02% | 35.26% |
| MTF-15M-1H-001 | BREADTH_HIGH | 2186 | 0.9516 | -0.07% | 34.54% |
| MTF-15M-1H-001 | BREADTH_LOW | 2617 | 0.9600 | -0.06% | 34.73% |
| MTF-15M-1H-001 | RS_HIGH | 2181 | 0.9511 | -0.07% | 34.53% |
| MTF-15M-1H-001 | RS_LOW | 2456 | 0.9616 | -0.05% | 34.77% |
| MTF-15M-1H-001 | VOL_SHOCK | 4385 | 0.9560 | -0.06% | 34.64% |
| MTF-15M-1H-001 | LONG | 2335 | 0.9383 | -0.09% | 34.22% |
| MTF-15M-1H-001 | SHORT | 2531 | 0.9732 | -0.04% | 35.05% |
| RSI-PB-ADX-001 | ALL | 57 | 0.5872 | -0.67% | 24.56% |
| RSI-PB-ADX-001 | VOL_HIGH | 46 | 0.5009 | -0.84% | 21.74% |
| RSI-PB-ADX-001 | TREND_UP | 36 | 0.2909 | -1.31% | 13.89% |
| RSI-PB-ADX-001 | TREND_DOWN | 21 | 1.3526 | 0.43% | 42.86% |
| RSI-PB-ADX-001 | BTC_DOWN | 39 | 0.7085 | -0.45% | 28.21% |
| RSI-PB-ADX-001 | BREADTH_HIGH | 21 | 1.3526 | 0.43% | 42.86% |
| RSI-PB-ADX-001 | BREADTH_LOW | 34 | 0.3110 | -1.26% | 14.71% |
| RSI-PB-ADX-001 | RS_HIGH | 30 | 0.4509 | -0.94% | 20.00% |
| RSI-PB-ADX-001 | RS_LOW | 22 | 0.6762 | -0.50% | 27.27% |
| RSI-PB-ADX-001 | VOL_SHOCK | 57 | 0.5872 | -0.67% | 24.56% |
| RSI-PB-ADX-001 | LONG | 22 | 1.5031 | 0.59% | 45.45% |
| RSI-PB-ADX-001 | SHORT | 35 | 0.2327 | -1.45% | 11.43% |
| ST-EMA-001 | ALL | 27836 | 0.9299 | -0.10% | 34.02% |
| ST-EMA-001 | VOL_LOW | 7929 | 0.9376 | -0.09% | 34.20% |
| ST-EMA-001 | VOL_MID | 4805 | 0.8346 | -0.24% | 31.63% |
| ST-EMA-001 | VOL_HIGH | 15102 | 0.9576 | -0.06% | 34.68% |
| ST-EMA-001 | TREND_UP | 13260 | 0.9486 | -0.07% | 34.46% |
| ST-EMA-001 | TREND_DOWN | 14576 | 0.9132 | -0.12% | 33.61% |
| ST-EMA-001 | BTC_UP | 13007 | 0.9480 | -0.07% | 34.45% |
| ST-EMA-001 | BTC_DOWN | 14829 | 0.9143 | -0.12% | 33.64% |
| ST-EMA-001 | BREADTH_HIGH | 12549 | 0.9496 | -0.07% | 34.49% |
| ST-EMA-001 | BREADTH_LOW | 14617 | 0.9166 | -0.12% | 33.69% |
| ST-EMA-001 | RS_HIGH | 12854 | 0.9472 | -0.07% | 34.43% |
| ST-EMA-001 | RS_LOW | 14113 | 0.9110 | -0.13% | 33.56% |
| ST-EMA-001 | VOL_SHOCK | 19110 | 0.9293 | -0.10% | 34.00% |
| ST-EMA-001 | LONG | 13260 | 0.9486 | -0.07% | 34.46% |
| ST-EMA-001 | SHORT | 14576 | 0.9132 | -0.12% | 33.61% |
| TREND-PB-001 | ALL | 737 | 0.9715 | -0.04% | 35.01% |
| TREND-PB-001 | VOL_LOW | 250 | 1.3497 | 0.43% | 42.80% |
| TREND-PB-001 | VOL_MID | 107 | 0.7696 | -0.35% | 29.91% |
| TREND-PB-001 | VOL_HIGH | 380 | 0.8224 | -0.26% | 31.32% |
| TREND-PB-001 | TREND_UP | 355 | 0.9563 | -0.06% | 34.65% |
| TREND-PB-001 | TREND_DOWN | 382 | 0.9859 | -0.02% | 35.34% |
| TREND-PB-001 | BTC_UP | 347 | 1.1074 | 0.14% | 38.04% |
| TREND-PB-001 | BTC_DOWN | 390 | 0.8609 | -0.20% | 32.31% |
| TREND-PB-001 | BREADTH_HIGH | 309 | 1.2586 | 0.33% | 41.10% |
| TREND-PB-001 | BREADTH_LOW | 380 | 0.8124 | -0.28% | 31.05% |
| TREND-PB-001 | RS_HIGH | 335 | 0.9429 | -0.08% | 34.33% |
| TREND-PB-001 | RS_LOW | 313 | 1.0050 | 0.01% | 35.78% |
| TREND-PB-001 | VOL_SHOCK | 562 | 0.9140 | -0.12% | 33.63% |
| TREND-PB-001 | LONG | 355 | 0.9563 | -0.06% | 34.65% |
| TREND-PB-001 | SHORT | 382 | 0.9859 | -0.02% | 35.34% |
| VOL-BO-001 | ALL | 13654 | 0.9227 | -0.11% | 33.84% |
| VOL-BO-001 | VOL_LOW | 2015 | 0.9106 | -0.13% | 33.55% |
| VOL-BO-001 | VOL_MID | 2299 | 0.8630 | -0.20% | 32.36% |
| VOL-BO-001 | VOL_HIGH | 9340 | 0.9405 | -0.08% | 34.27% |
| VOL-BO-001 | TREND_UP | 6772 | 0.9598 | -0.06% | 34.73% |
| VOL-BO-001 | TREND_DOWN | 6882 | 0.8872 | -0.16% | 32.97% |
| VOL-BO-001 | BTC_UP | 6469 | 0.9424 | -0.08% | 34.32% |
| VOL-BO-001 | BTC_DOWN | 7185 | 0.9053 | -0.13% | 33.42% |
| VOL-BO-001 | BREADTH_HIGH | 6026 | 0.9431 | -0.08% | 34.33% |
| VOL-BO-001 | BREADTH_LOW | 7485 | 0.9030 | -0.14% | 33.36% |
| VOL-BO-001 | RS_HIGH | 6140 | 0.9322 | -0.10% | 34.07% |
| VOL-BO-001 | RS_LOW | 6874 | 0.9124 | -0.12% | 33.59% |
| VOL-BO-001 | VOL_SHOCK | 13654 | 0.9227 | -0.11% | 33.84% |
| VOL-BO-001 | LONG | 6425 | 0.9334 | -0.09% | 34.10% |
| VOL-BO-001 | SHORT | 7229 | 0.9134 | -0.12% | 33.61% |
| VWAP-PB-001 | ALL | 15130 | 0.8962 | -0.15% | 33.19% |
| VWAP-PB-001 | VOL_LOW | 6057 | 0.9025 | -0.14% | 33.35% |
| VWAP-PB-001 | VOL_MID | 2696 | 0.8652 | -0.19% | 32.42% |
| VWAP-PB-001 | VOL_HIGH | 6377 | 0.9034 | -0.14% | 33.37% |
| VWAP-PB-001 | TREND_UP | 7524 | 0.9231 | -0.11% | 33.85% |
| VWAP-PB-001 | TREND_DOWN | 7606 | 0.8701 | -0.19% | 32.54% |
| VWAP-PB-001 | BTC_UP | 7497 | 0.9073 | -0.13% | 33.47% |
| VWAP-PB-001 | BTC_DOWN | 7633 | 0.8853 | -0.16% | 32.92% |
| VWAP-PB-001 | BREADTH_HIGH | 6873 | 0.9107 | -0.13% | 33.55% |
| VWAP-PB-001 | BREADTH_LOW | 7530 | 0.8922 | -0.15% | 33.09% |
| VWAP-PB-001 | RS_HIGH | 7130 | 0.9049 | -0.14% | 33.41% |
| VWAP-PB-001 | RS_LOW | 7360 | 0.8923 | -0.15% | 33.10% |
| VWAP-PB-001 | VOL_SHOCK | 6216 | 0.9078 | -0.13% | 33.48% |
| VWAP-PB-001 | LONG | 7466 | 0.9206 | -0.11% | 33.79% |
| VWAP-PB-001 | SHORT | 7664 | 0.8727 | -0.18% | 32.61% |
| VWAP-PB-EMA-001 | ALL | 11813 | 0.9703 | -0.04% | 34.98% |
| VWAP-PB-EMA-001 | VOL_LOW | 5446 | 0.9688 | -0.04% | 34.94% |
| VWAP-PB-EMA-001 | VOL_MID | 2170 | 0.9334 | -0.09% | 34.10% |
| VWAP-PB-EMA-001 | VOL_HIGH | 4197 | 0.9918 | -0.01% | 35.48% |
| VWAP-PB-EMA-001 | TREND_UP | 5854 | 1.0030 | 0.00% | 35.74% |
| VWAP-PB-EMA-001 | TREND_DOWN | 5959 | 0.9389 | -0.09% | 34.23% |
| VWAP-PB-EMA-001 | BTC_UP | 5978 | 0.9775 | -0.03% | 35.15% |
| VWAP-PB-EMA-001 | BTC_DOWN | 5835 | 0.9630 | -0.05% | 34.81% |
| VWAP-PB-EMA-001 | BREADTH_HIGH | 5335 | 0.9904 | -0.01% | 35.45% |
| VWAP-PB-EMA-001 | BREADTH_LOW | 5882 | 0.9577 | -0.06% | 34.68% |
| VWAP-PB-EMA-001 | RS_HIGH | 5502 | 0.9949 | -0.01% | 35.55% |
| VWAP-PB-EMA-001 | RS_LOW | 5742 | 0.9457 | -0.08% | 34.40% |
| VWAP-PB-EMA-001 | VOL_SHOCK | 3913 | 0.9533 | -0.07% | 34.58% |
| VWAP-PB-EMA-001 | LONG | 5854 | 1.0030 | 0.00% | 35.74% |
| VWAP-PB-EMA-001 | SHORT | 5959 | 0.9389 | -0.09% | 34.23% |
| W2-BOS-PULLBACK-001 | ALL | 17354 | 0.9356 | -0.09% | 34.15% |
| W2-BOS-PULLBACK-001 | VOL_LOW | 6479 | 0.9344 | -0.09% | 34.13% |
| W2-BOS-PULLBACK-001 | VOL_MID | 3480 | 0.8880 | -0.16% | 32.99% |
| W2-BOS-PULLBACK-001 | VOL_HIGH | 7395 | 0.9596 | -0.06% | 34.73% |
| W2-BOS-PULLBACK-001 | TREND_UP | 8565 | 0.9267 | -0.10% | 33.94% |
| W2-BOS-PULLBACK-001 | TREND_DOWN | 8789 | 0.9443 | -0.08% | 34.36% |
| W2-BOS-PULLBACK-001 | BTC_UP | 8552 | 0.9163 | -0.12% | 33.69% |
| W2-BOS-PULLBACK-001 | BTC_DOWN | 8802 | 0.9545 | -0.06% | 34.61% |
| W2-BOS-PULLBACK-001 | BREADTH_HIGH | 8351 | 0.9147 | -0.12% | 33.65% |
| W2-BOS-PULLBACK-001 | BREADTH_LOW | 8481 | 0.9596 | -0.06% | 34.72% |
| W2-BOS-PULLBACK-001 | RS_HIGH | 8062 | 0.9312 | -0.10% | 34.05% |
| W2-BOS-PULLBACK-001 | RS_LOW | 8617 | 0.9346 | -0.09% | 34.13% |
| W2-BOS-PULLBACK-001 | VOL_SHOCK | 6555 | 0.9338 | -0.09% | 34.11% |
| W2-BOS-PULLBACK-001 | LONG | 8791 | 0.9170 | -0.12% | 33.70% |
| W2-BOS-PULLBACK-001 | SHORT | 8563 | 0.9549 | -0.06% | 34.61% |
| W2-DISPLACE-001 | ALL | 17773 | 0.9513 | -0.07% | 34.53% |
| W2-DISPLACE-001 | VOL_LOW | 4854 | 0.9323 | -0.10% | 34.07% |
| W2-DISPLACE-001 | VOL_MID | 3108 | 0.8544 | -0.21% | 32.14% |
| W2-DISPLACE-001 | VOL_HIGH | 9811 | 0.9932 | -0.01% | 35.51% |
| W2-DISPLACE-001 | TREND_UP | 8998 | 0.9466 | -0.07% | 34.42% |
| W2-DISPLACE-001 | TREND_DOWN | 8775 | 0.9561 | -0.06% | 34.64% |
| W2-DISPLACE-001 | BTC_UP | 8683 | 0.9483 | -0.07% | 34.46% |
| W2-DISPLACE-001 | BTC_DOWN | 9090 | 0.9542 | -0.06% | 34.60% |
| W2-DISPLACE-001 | BREADTH_HIGH | 7767 | 0.9448 | -0.08% | 34.38% |
| W2-DISPLACE-001 | BREADTH_LOW | 9529 | 0.9672 | -0.05% | 34.90% |
| W2-DISPLACE-001 | RS_HIGH | 8225 | 0.9345 | -0.09% | 34.13% |
| W2-DISPLACE-001 | RS_LOW | 8841 | 0.9605 | -0.06% | 34.75% |
| W2-DISPLACE-001 | VOL_SHOCK | 7908 | 0.9849 | -0.02% | 35.32% |
| W2-DISPLACE-001 | LONG | 8567 | 0.9635 | -0.05% | 34.82% |
| W2-DISPLACE-001 | SHORT | 9206 | 0.9400 | -0.08% | 34.26% |
| W2-FAILED-BO-VOL-001 | ALL | 5780 | 0.8867 | -0.16% | 32.96% |
| W2-FAILED-BO-VOL-001 | VOL_LOW | 2814 | 0.8747 | -0.18% | 32.66% |
| W2-FAILED-BO-VOL-001 | VOL_MID | 1101 | 0.9430 | -0.08% | 34.33% |
| W2-FAILED-BO-VOL-001 | VOL_HIGH | 1865 | 0.8724 | -0.18% | 32.60% |
| W2-FAILED-BO-VOL-001 | TREND_UP | 2863 | 0.9156 | -0.12% | 33.67% |
| W2-FAILED-BO-VOL-001 | TREND_DOWN | 2917 | 0.8590 | -0.20% | 32.26% |
| W2-FAILED-BO-VOL-001 | BTC_UP | 2967 | 0.9032 | -0.14% | 33.37% |
| W2-FAILED-BO-VOL-001 | BTC_DOWN | 2813 | 0.8696 | -0.19% | 32.53% |
| W2-FAILED-BO-VOL-001 | BREADTH_HIGH | 2530 | 0.8870 | -0.16% | 32.96% |
| W2-FAILED-BO-VOL-001 | BREADTH_LOW | 3059 | 0.8838 | -0.17% | 32.89% |
| W2-FAILED-BO-VOL-001 | RS_HIGH | 2456 | 0.8746 | -0.18% | 32.65% |
| W2-FAILED-BO-VOL-001 | RS_LOW | 2974 | 0.8919 | -0.15% | 33.09% |
| W2-FAILED-BO-VOL-001 | LONG | 3184 | 0.8675 | -0.19% | 32.47% |
| W2-FAILED-BO-VOL-001 | SHORT | 2596 | 0.9108 | -0.13% | 33.55% |
| W2-REGIME-SWITCH-001 | ALL | 17153 | 0.8860 | -0.16% | 32.94% |
| W2-REGIME-SWITCH-001 | VOL_LOW | 6378 | 0.8537 | -0.21% | 32.13% |
| W2-REGIME-SWITCH-001 | VOL_MID | 2521 | 0.8431 | -0.23% | 31.85% |
| W2-REGIME-SWITCH-001 | VOL_HIGH | 8254 | 0.9250 | -0.11% | 33.90% |
| W2-REGIME-SWITCH-001 | TREND_UP | 8597 | 0.8997 | -0.14% | 33.28% |
| W2-REGIME-SWITCH-001 | TREND_DOWN | 8556 | 0.8723 | -0.18% | 32.60% |
| W2-REGIME-SWITCH-001 | BTC_UP | 8470 | 0.8869 | -0.16% | 32.96% |
| W2-REGIME-SWITCH-001 | BTC_DOWN | 8683 | 0.8850 | -0.17% | 32.91% |
| W2-REGIME-SWITCH-001 | BREADTH_HIGH | 8256 | 0.9009 | -0.14% | 33.31% |
| W2-REGIME-SWITCH-001 | BREADTH_LOW | 8568 | 0.8724 | -0.18% | 32.60% |
| W2-REGIME-SWITCH-001 | RS_HIGH | 7917 | 0.9168 | -0.12% | 33.70% |
| W2-REGIME-SWITCH-001 | RS_LOW | 8514 | 0.8601 | -0.20% | 32.29% |
| W2-REGIME-SWITCH-001 | VOL_SHOCK | 13538 | 0.8984 | -0.15% | 33.25% |
| W2-REGIME-SWITCH-001 | LONG | 7897 | 0.9027 | -0.14% | 33.35% |
| W2-REGIME-SWITCH-001 | SHORT | 9256 | 0.8718 | -0.18% | 32.58% |
| W2-SQUEEZE-EXP-001 | ALL | 10129 | 0.9474 | -0.07% | 34.44% |
| W2-SQUEEZE-EXP-001 | VOL_LOW | 7502 | 0.9511 | -0.07% | 34.52% |
| W2-SQUEEZE-EXP-001 | VOL_MID | 1625 | 0.9459 | -0.08% | 34.40% |
| W2-SQUEEZE-EXP-001 | VOL_HIGH | 1002 | 0.9223 | -0.11% | 33.83% |
| W2-SQUEEZE-EXP-001 | TREND_UP | 5251 | 0.9935 | -0.01% | 35.52% |
| W2-SQUEEZE-EXP-001 | TREND_DOWN | 4878 | 0.8994 | -0.14% | 33.27% |
| W2-SQUEEZE-EXP-001 | BTC_UP | 5616 | 0.9437 | -0.08% | 34.35% |
| W2-SQUEEZE-EXP-001 | BTC_DOWN | 4513 | 0.9519 | -0.07% | 34.54% |
| W2-SQUEEZE-EXP-001 | BREADTH_HIGH | 4806 | 0.9702 | -0.04% | 34.98% |
| W2-SQUEEZE-EXP-001 | BREADTH_LOW | 5184 | 0.9232 | -0.11% | 33.85% |
| W2-SQUEEZE-EXP-001 | RS_HIGH | 4574 | 0.9345 | -0.09% | 34.13% |
| W2-SQUEEZE-EXP-001 | RS_LOW | 5051 | 0.9554 | -0.06% | 34.63% |
| W2-SQUEEZE-EXP-001 | VOL_SHOCK | 10129 | 0.9474 | -0.07% | 34.44% |
| W2-SQUEEZE-EXP-001 | LONG | 4931 | 0.9714 | -0.04% | 35.00% |
| W2-SQUEEZE-EXP-001 | SHORT | 5198 | 0.9250 | -0.11% | 33.90% |
| W2-SWEEP-RECLAIM-001 | ALL | 11895 | 0.8923 | -0.15% | 33.10% |
| W2-SWEEP-RECLAIM-001 | VOL_LOW | 4868 | 0.8716 | -0.19% | 32.58% |
| W2-SWEEP-RECLAIM-001 | VOL_MID | 2283 | 0.9415 | -0.08% | 34.30% |
| W2-SWEEP-RECLAIM-001 | VOL_HIGH | 4744 | 0.8905 | -0.16% | 33.05% |
| W2-SWEEP-RECLAIM-001 | TREND_UP | 5998 | 0.8545 | -0.21% | 32.14% |
| W2-SWEEP-RECLAIM-001 | TREND_DOWN | 5897 | 0.9320 | -0.10% | 34.07% |
| W2-SWEEP-RECLAIM-001 | BTC_UP | 6047 | 0.8514 | -0.22% | 32.07% |
| W2-SWEEP-RECLAIM-001 | BTC_DOWN | 5848 | 0.9361 | -0.09% | 34.17% |
| W2-SWEEP-RECLAIM-001 | BREADTH_HIGH | 5079 | 0.8828 | -0.17% | 32.86% |
| W2-SWEEP-RECLAIM-001 | BREADTH_LOW | 6155 | 0.8949 | -0.15% | 33.16% |
| W2-SWEEP-RECLAIM-001 | RS_HIGH | 5544 | 0.9166 | -0.12% | 33.69% |
| W2-SWEEP-RECLAIM-001 | RS_LOW | 5772 | 0.8672 | -0.19% | 32.47% |
| W2-SWEEP-RECLAIM-001 | VOL_SHOCK | 11895 | 0.8923 | -0.15% | 33.10% |
| W2-SWEEP-RECLAIM-001 | LONG | 6113 | 0.8902 | -0.16% | 33.04% |
| W2-SWEEP-RECLAIM-001 | SHORT | 5782 | 0.8947 | -0.15% | 33.15% |
| W2-VOL-EXHAUST-001 | ALL | 8327 | 0.7948 | -0.30% | 30.59% |
| W2-VOL-EXHAUST-001 | VOL_LOW | 2694 | 0.8589 | -0.20% | 32.26% |
| W2-VOL-EXHAUST-001 | VOL_MID | 1545 | 0.8353 | -0.24% | 31.65% |
| W2-VOL-EXHAUST-001 | VOL_HIGH | 4088 | 0.7398 | -0.39% | 29.09% |
| W2-VOL-EXHAUST-001 | TREND_UP | 3912 | 0.8480 | -0.22% | 31.98% |
| W2-VOL-EXHAUST-001 | TREND_DOWN | 4415 | 0.7495 | -0.38% | 29.35% |
| W2-VOL-EXHAUST-001 | BTC_UP | 3988 | 0.8302 | -0.25% | 31.52% |
| W2-VOL-EXHAUST-001 | BTC_DOWN | 4339 | 0.7631 | -0.36% | 29.73% |
| W2-VOL-EXHAUST-001 | BREADTH_HIGH | 3372 | 0.8168 | -0.27% | 31.17% |
| W2-VOL-EXHAUST-001 | BREADTH_LOW | 4786 | 0.7778 | -0.33% | 30.13% |
| W2-VOL-EXHAUST-001 | RS_HIGH | 3357 | 0.8175 | -0.27% | 31.19% |
| W2-VOL-EXHAUST-001 | RS_LOW | 4484 | 0.7729 | -0.34% | 30.00% |
| W2-VOL-EXHAUST-001 | VOL_SHOCK | 8327 | 0.7948 | -0.30% | 30.59% |
| W2-VOL-EXHAUST-001 | LONG | 4716 | 0.7865 | -0.32% | 30.36% |
| W2-VOL-EXHAUST-001 | SHORT | 3611 | 0.8058 | -0.29% | 30.88% |
| W2-VWAP-EXT-001 | ALL | 7405 | 0.9474 | -0.07% | 34.44% |
| W2-VWAP-EXT-001 | VOL_LOW | 2738 | 0.9537 | -0.06% | 34.59% |
| W2-VWAP-EXT-001 | VOL_MID | 1310 | 0.9696 | -0.04% | 34.96% |
| W2-VWAP-EXT-001 | VOL_HIGH | 3357 | 0.9337 | -0.09% | 34.11% |
| W2-VWAP-EXT-001 | TREND_UP | 3858 | 0.9414 | -0.08% | 34.29% |
| W2-VWAP-EXT-001 | TREND_DOWN | 3547 | 0.9540 | -0.06% | 34.59% |
| W2-VWAP-EXT-001 | BTC_UP | 3900 | 0.9249 | -0.11% | 33.90% |
| W2-VWAP-EXT-001 | BTC_DOWN | 3505 | 0.9728 | -0.04% | 35.04% |
| W2-VWAP-EXT-001 | BREADTH_HIGH | 3180 | 0.9793 | -0.03% | 35.19% |
| W2-VWAP-EXT-001 | BREADTH_LOW | 3918 | 0.9354 | -0.09% | 34.15% |
| W2-VWAP-EXT-001 | RS_HIGH | 3490 | 0.9416 | -0.08% | 34.30% |
| W2-VWAP-EXT-001 | RS_LOW | 3466 | 0.9419 | -0.08% | 34.30% |
| W2-VWAP-EXT-001 | VOL_SHOCK | 6442 | 0.9471 | -0.07% | 34.43% |
| W2-VWAP-EXT-001 | LONG | 3533 | 0.9561 | -0.06% | 34.64% |
| W2-VWAP-EXT-001 | SHORT | 3872 | 0.9394 | -0.09% | 34.25% |

## 4. Regime analysis

BTC vol tercile = rank of BTC ATR% in the last 50 closed bars. Trend = EMA20 vs EMA50 on the coin. Breadth = share of harvested coins with close > EMA20 at the same OpenTime.

IS strategy-level ALL buckets stay in a tight band around PF 0.79–1.00. Large buckets (n≥100) do not flip to PF>1. A few small IS pockets (for example ALPHA-supertrend_ema_trend / VOL_LOW PF 1.16 n=847, TREND-PB-001 / VOL_LOW PF 1.35 n=250, EMA-RSI-HTF-001 / VOL_LOW PF 1.05 n=1933) look less bad in-sample. Those tables were **not** used to trade OOS. The purged 90-day score is the only routing input. OOS ranking did not preserve those pockets: top-ranked signals underperformed bottom-ranked and random.

## 5. Strategy / timeframe compatibility

| Slice | n | PF | Exp | WR |
|---|---:|---:|---:|---:|
| OOS-ALL-5m | 38895 | 0.9179 | -0.12% | 33.82% |
| OOS-ALL-15m | 32128 | 0.8851 | -0.16% | 33.01% |
| OOS-ALL-1h | 19799 | 0.8637 | -0.20% | 32.49% |
| OOS-ALL-ALL | 90822 | 0.8943 | -0.15% | 33.24% |

## 6. Signal scoring

score = mean of 5 causal components (90d, min 20 trades else 0): regime match (vol×trend bucket mean R), strategy recency, symbol fit, timeframe fit, volatility-bucket fit. R = net return / 2% stop. Purged: only trades with ExitTime < SignalTime.

## 7. Ranking results

| Slice | n | PF | Exp | WR | Mean R | DD | Symbols | Strategies | Top coin share |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| IS-ALL-5m | 218904 | 0.9101 | -0.13% | 33.54% | -0.0639 | -29007.02% | 10 | 42 | 17.64% |
| VALIDATION-ALL-5m | 50312 | 0.9181 | -0.12% | 33.73% | -0.0581 | -7130.67% | 10 | 42 | 22.01% |
| OOS-ALL-5m | 38895 | 0.9179 | -0.12% | 33.82% | -0.0580 | -6553.78% | 10 | 42 | 17.50% |
| OOS-TOP1-5m | 13221 | 0.8893 | -0.16% | 33.11% | -0.0790 | -2477.43% | 10 | 40 | 18.19% |
| OOS-BOTTOM1-5m | 13221 | 0.8992 | -0.14% | 33.36% | -0.0717 | -2295.69% | 10 | 39 | 15.27% |
| VAL-TOP1-5m | 14764 | 0.9040 | -0.14% | 33.39% | -0.0684 | -2190.54% | 10 | 39 | 25.06% |
| OOS-TOP2-5m | 18162 | 0.8973 | -0.15% | 33.30% | -0.0731 | -3254.66% | 10 | 40 | 17.88% |
| OOS-BOTTOM2-5m | 18162 | 0.9056 | -0.13% | 33.51% | -0.0670 | -2990.61% | 10 | 39 | 18.17% |
| VAL-TOP2-5m | 21319 | 0.9028 | -0.14% | 33.36% | -0.0693 | -3192.04% | 10 | 39 | 19.12% |
| OOS-TOP3-5m | 20492 | 0.8983 | -0.14% | 33.33% | -0.0724 | -3652.95% | 10 | 40 | 17.64% |
| OOS-BOTTOM3-5m | 20492 | 0.9073 | -0.13% | 33.56% | -0.0657 | -3297.31% | 10 | 39 | 17.50% |
| VAL-TOP3-5m | 24788 | 0.9034 | -0.14% | 33.37% | -0.0689 | -3699.84% | 10 | 39 | 18.88% |
| IS-ALL-15m | 161664 | 0.9170 | -0.12% | 33.71% | -0.0588 | -19792.02% | 10 | 42 | 19.12% |
| VALIDATION-ALL-15m | 40473 | 0.8627 | -0.20% | 32.35% | -0.0994 | -8855.32% | 10 | 42 | 17.33% |
| OOS-ALL-15m | 32128 | 0.8851 | -0.16% | 33.01% | -0.0823 | -7056.06% | 10 | 42 | 16.44% |
| OOS-TOP1-15m | 7345 | 0.9115 | -0.13% | 33.64% | -0.0628 | -1309.22% | 10 | 40 | 27.53% |
| OOS-BOTTOM1-15m | 7345 | 0.9334 | -0.09% | 34.20% | -0.0469 | -1084.54% | 10 | 40 | 23.09% |
| VAL-TOP1-15m | 7766 | 0.8844 | -0.17% | 32.90% | -0.0830 | -1386.79% | 10 | 39 | 21.41% |
| OOS-TOP2-15m | 11195 | 0.8948 | -0.15% | 33.25% | -0.0750 | -2223.04% | 10 | 40 | 17.08% |
| OOS-BOTTOM2-15m | 11195 | 0.9227 | -0.11% | 33.93% | -0.0546 | -1748.62% | 10 | 40 | 17.63% |
| VAL-TOP2-15m | 12231 | 0.8926 | -0.15% | 33.10% | -0.0769 | -2101.69% | 10 | 39 | 19.35% |
| OOS-TOP3-15m | 13366 | 0.8939 | -0.15% | 33.21% | -0.0757 | -2628.38% | 10 | 40 | 17.53% |
| OOS-BOTTOM3-15m | 13366 | 0.8999 | -0.14% | 33.38% | -0.0713 | -2452.30% | 10 | 40 | 15.55% |
| VAL-TOP3-15m | 15081 | 0.8917 | -0.16% | 33.08% | -0.0775 | -2611.53% | 10 | 39 | 22.42% |
| IS-ALL-1h | 82579 | 0.9387 | -0.09% | 34.23% | -0.0432 | -9327.90% | 10 | 38 | 24.78% |
| VALIDATION-ALL-1h | 23219 | 0.8983 | -0.15% | 33.24% | -0.0727 | -3641.67% | 10 | 40 | 24.13% |
| OOS-ALL-1h | 19799 | 0.8637 | -0.20% | 32.49% | -0.0984 | -4706.05% | 10 | 40 | 24.95% |
| OOS-TOP1-1h | 2599 | 0.8742 | -0.18% | 32.70% | -0.0905 | -549.02% | 10 | 38 | 31.56% |
| OOS-BOTTOM1-1h | 2599 | 0.9445 | -0.08% | 34.44% | -0.0389 | -367.05% | 10 | 37 | 21.89% |
| VAL-TOP1-1h | 2623 | 0.8632 | -0.20% | 32.37% | -0.0990 | -551.46% | 10 | 38 | 26.99% |
| OOS-TOP2-1h | 4385 | 0.9133 | -0.12% | 33.66% | -0.0615 | -782.50% | 10 | 38 | 22.74% |
| OOS-BOTTOM2-1h | 4385 | 0.9363 | -0.09% | 34.25% | -0.0448 | -687.56% | 10 | 38 | 22.85% |
| VAL-TOP2-1h | 4478 | 0.8959 | -0.15% | 33.18% | -0.0745 | -750.90% | 10 | 38 | 22.95% |
| OOS-TOP3-1h | 5601 | 0.9126 | -0.12% | 33.65% | -0.0620 | -1012.99% | 10 | 38 | 16.15% |
| OOS-BOTTOM3-1h | 5601 | 0.9237 | -0.11% | 33.96% | -0.0539 | -977.48% | 10 | 38 | 24.76% |
| VAL-TOP3-1h | 5824 | 0.8892 | -0.16% | 33.02% | -0.0794 | -1012.46% | 10 | 38 | 18.36% |
| IS-ALL-ALL | 463147 | 0.9176 | -0.12% | 33.72% | -0.0584 | -55913.98% | 10 | 43 | 17.69% |
| VALIDATION-ALL-ALL | 114004 | 0.8941 | -0.15% | 33.14% | -0.0757 | -19554.71% | 10 | 43 | 17.32% |
| OOS-ALL-ALL | 90822 | 0.8943 | -0.15% | 33.24% | -0.0754 | -18163.42% | 10 | 43 | 17.61% |
| OOS-TOP1-ALL | 23165 | 0.8946 | -0.15% | 33.24% | -0.0752 | -4214.40% | 10 | 41 | 17.00% |
| OOS-BOTTOM1-ALL | 23165 | 0.9150 | -0.12% | 33.75% | -0.0601 | -3628.86% | 10 | 40 | 17.79% |
| VAL-TOP1-ALL | 25153 | 0.8936 | -0.15% | 33.13% | -0.0761 | -4074.48% | 10 | 40 | 17.09% |
| OOS-TOP2-ALL | 33742 | 0.8986 | -0.14% | 33.33% | -0.0722 | -6175.17% | 10 | 41 | 17.09% |
| OOS-BOTTOM2-ALL | 33742 | 0.9153 | -0.12% | 33.75% | -0.0600 | -5353.44% | 10 | 40 | 17.38% |
| VAL-TOP2-ALL | 38028 | 0.8987 | -0.14% | 33.25% | -0.0724 | -6004.01% | 10 | 40 | 16.95% |
| OOS-TOP3-ALL | 39459 | 0.8988 | -0.14% | 33.33% | -0.0720 | -7207.54% | 10 | 41 | 16.51% |
| OOS-BOTTOM3-ALL | 39459 | 0.9071 | -0.13% | 33.55% | -0.0659 | -6595.37% | 10 | 40 | 15.82% |
| VAL-TOP3-ALL | 45693 | 0.8977 | -0.15% | 33.23% | -0.0731 | -7260.03% | 10 | 40 | 17.70% |
| OOS-TOP1-ALL-C1.25 | 23165 | 0.8753 | -0.18% | 33.23% | -0.0902 | -4707.24% | 10 | 41 | 16.31% |
| OOS-RAND1-ALL-C1.25 | 23165 | 0.8892 | -0.16% | 33.59% | -0.0797 | -4432.27% | 10 | 42 | 16.23% |
| OOS-TOP1-ALL-C1.5 | 23165 | 0.8565 | -0.21% | 33.23% | -0.1052 | -5305.54% | 10 | 41 | 15.42% |
| OOS-RAND1-ALL-C1.5 | 23165 | 0.8702 | -0.19% | 33.59% | -0.0947 | -4925.29% | 10 | 42 | 16.17% |
| OOS-TOP1-ALL-C2.0 | 23165 | 0.8205 | -0.27% | 33.22% | -0.1352 | -6643.48% | 10 | 41 | 14.23% |
| OOS-RAND1-ALL-C2.0 | 23165 | 0.8336 | -0.25% | 33.59% | -0.1247 | -6245.20% | 10 | 42 | 16.09% |

## 8. Consensus / conflict

| Slice | n | PF | Exp | WR | Mean R | DD | Symbols | Strategies | Top coin share |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| OOS-CONS2-5m | 14145 | 0.9608 | -0.05% | 34.89% | -0.0272 | -2282.12% | 10 | 42 | 29.04% |
| OOS-CONS3-5m | 3965 | 1.0240 | 0.03% | 36.29% | 0.0163 | -643.25% | 10 | 38 | 22.65% |
| OOS-SKIP-CONFLICT-5m | 35198 | 0.9148 | -0.12% | 33.75% | -0.0602 | -5859.71% | 10 | 42 | 16.96% |
| OOS-CONS2-15m | 14753 | 0.8821 | -0.17% | 32.94% | -0.0845 | -3677.83% | 10 | 42 | 20.62% |
| OOS-CONS3-15m | 5534 | 0.8725 | -0.18% | 32.62% | -0.0918 | -1829.38% | 10 | 40 | 24.58% |
| OOS-SKIP-CONFLICT-15m | 27745 | 0.8806 | -0.17% | 32.90% | -0.0856 | -6175.63% | 10 | 42 | 15.80% |
| OOS-CONS2-1h | 11641 | 0.8479 | -0.22% | 32.10% | -0.1103 | -3284.98% | 10 | 40 | 27.51% |
| OOS-CONS3-1h | 5491 | 0.9029 | -0.14% | 33.45% | -0.0691 | -1408.71% | 10 | 38 | 22.51% |
| OOS-SKIP-CONFLICT-1h | 15359 | 0.8773 | -0.18% | 32.85% | -0.0880 | -3636.85% | 10 | 40 | 27.82% |
| OOS-CONS2-ALL | 40539 | 0.8989 | -0.14% | 33.38% | -0.0719 | -8819.74% | 10 | 43 | 19.20% |
| OOS-CONS3-ALL | 14990 | 0.9222 | -0.11% | 33.90% | -0.0549 | -3158.75% | 10 | 40 | 22.80% |
| OOS-SKIP-CONFLICT-ALL | 78302 | 0.8952 | -0.15% | 33.27% | -0.0747 | -15510.27% | 10 | 43 | 16.45% |

## 9. Router-selected trades

| Slice | n | PF | Exp | WR | Mean R | DD | Symbols | Strategies | Top coin share |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| OOS-ALL-5m | 38895 | 0.9179 | -0.12% | 33.82% | -0.0580 | -6553.78% | 10 | 42 | 17.50% |
| OOS-TOP1-5m | 13221 | 0.8893 | -0.16% | 33.11% | -0.0790 | -2477.43% | 10 | 40 | 18.19% |
| OOS-ALL-15m | 32128 | 0.8851 | -0.16% | 33.01% | -0.0823 | -7056.06% | 10 | 42 | 16.44% |
| OOS-TOP1-15m | 7345 | 0.9115 | -0.13% | 33.64% | -0.0628 | -1309.22% | 10 | 40 | 27.53% |
| OOS-ALL-1h | 19799 | 0.8637 | -0.20% | 32.49% | -0.0984 | -4706.05% | 10 | 40 | 24.95% |
| OOS-TOP1-1h | 2599 | 0.8742 | -0.18% | 32.70% | -0.0905 | -549.02% | 10 | 38 | 31.56% |
| OOS-ALL-ALL | 90822 | 0.8943 | -0.15% | 33.24% | -0.0754 | -18163.42% | 10 | 43 | 17.61% |
| OOS-TOP1-ALL | 23165 | 0.8946 | -0.15% | 33.24% | -0.0752 | -4214.40% | 10 | 41 | 17.00% |
| OOS-TOP1-ALL-C1.25 | 23165 | 0.8753 | -0.18% | 33.23% | -0.0902 | -4707.24% | 10 | 41 | 16.31% |
| OOS-TOP1-ALL-C1.5 | 23165 | 0.8565 | -0.21% | 33.23% | -0.1052 | -5305.54% | 10 | 41 | 15.42% |
| OOS-TOP1-ALL-C2.0 | 23165 | 0.8205 | -0.27% | 33.22% | -0.1352 | -6643.48% | 10 | 41 | 14.23% |

## 10. Random-selection control

| Slice | n | PF | Exp | WR | Mean R | DD | Symbols | Strategies | Top coin share |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| OOS-RAND1-5m | 13221 | 0.9042 | -0.14% | 33.47% | -0.0680 | -2254.68% | 10 | 42 | 19.81% |
| VAL-RAND1-5m | 14764 | 0.9015 | -0.14% | 33.32% | -0.0703 | -2235.13% | 10 | 39 | 23.12% |
| OOS-RAND2-5m | 18162 | 0.9038 | -0.14% | 33.45% | -0.0683 | -3067.34% | 10 | 42 | 17.71% |
| VAL-RAND2-5m | 21319 | 0.9041 | -0.14% | 33.39% | -0.0683 | -3165.94% | 10 | 40 | 18.47% |
| OOS-RAND3-5m | 20492 | 0.9007 | -0.14% | 33.37% | -0.0706 | -3541.10% | 10 | 42 | 18.59% |
| VAL-RAND3-5m | 24788 | 0.8992 | -0.14% | 33.27% | -0.0720 | -3858.90% | 10 | 40 | 19.24% |
| OOS-RAND1-15m | 7345 | 0.9100 | -0.13% | 33.64% | -0.0639 | -1301.30% | 10 | 40 | 20.61% |
| VAL-RAND1-15m | 7766 | 0.8901 | -0.16% | 33.04% | -0.0787 | -1340.52% | 10 | 39 | 24.71% |
| OOS-RAND2-15m | 11195 | 0.9128 | -0.12% | 33.69% | -0.0618 | -1941.46% | 10 | 41 | 16.53% |
| VAL-RAND2-15m | 12231 | 0.8953 | -0.15% | 33.17% | -0.0749 | -2068.28% | 10 | 39 | 22.02% |
| OOS-RAND3-15m | 13366 | 0.9012 | -0.14% | 33.41% | -0.0703 | -2566.82% | 10 | 41 | 18.92% |
| VAL-RAND3-15m | 15081 | 0.8949 | -0.15% | 33.16% | -0.0752 | -2562.22% | 10 | 39 | 20.75% |
| OOS-RAND1-1h | 2599 | 0.9046 | -0.14% | 33.47% | -0.0678 | -483.79% | 10 | 37 | 19.72% |
| VAL-RAND1-1h | 2623 | 0.8983 | -0.15% | 33.24% | -0.0727 | -441.30% | 10 | 37 | 21.34% |
| OOS-RAND2-1h | 4385 | 0.9367 | -0.09% | 34.28% | -0.0445 | -709.20% | 10 | 38 | 23.47% |
| VAL-RAND2-1h | 4478 | 0.8941 | -0.15% | 33.14% | -0.0758 | -776.17% | 10 | 38 | 21.03% |
| OOS-RAND3-1h | 5601 | 0.9251 | -0.11% | 33.99% | -0.0528 | -955.02% | 10 | 38 | 29.83% |
| VAL-RAND3-1h | 5824 | 0.9058 | -0.13% | 33.43% | -0.0671 | -875.27% | 10 | 38 | 21.66% |
| OOS-RAND1-ALL | 23165 | 0.9088 | -0.13% | 33.59% | -0.0647 | -3939.25% | 10 | 42 | 16.31% |
| VAL-RAND1-ALL | 25153 | 0.9086 | -0.13% | 33.50% | -0.0650 | -3610.07% | 10 | 41 | 23.94% |
| OOS-RAND2-ALL | 33742 | 0.9113 | -0.13% | 33.65% | -0.0628 | -5718.24% | 10 | 42 | 16.74% |
| VAL-RAND2-ALL | 38028 | 0.9026 | -0.14% | 33.35% | -0.0694 | -5882.82% | 10 | 42 | 21.05% |
| OOS-RAND3-ALL | 39459 | 0.9099 | -0.13% | 33.61% | -0.0639 | -6749.38% | 10 | 42 | 15.34% |
| VAL-RAND3-ALL | 45693 | 0.8968 | -0.15% | 33.21% | -0.0737 | -7433.20% | 10 | 42 | 18.69% |
| OOS-RAND1-ALL-C1.25 | 23165 | 0.8892 | -0.16% | 33.59% | -0.0797 | -4432.27% | 10 | 42 | 16.23% |
| OOS-RAND1-ALL-C1.5 | 23165 | 0.8702 | -0.19% | 33.59% | -0.0947 | -4925.29% | 10 | 42 | 16.17% |
| OOS-RAND1-ALL-C2.0 | 23165 | 0.8336 | -0.25% | 33.59% | -0.1247 | -6245.20% | 10 | 42 | 16.09% |
Mandatory: if TOP-K does not beat RAND-K on OOS PF **and** expectancy, the router has no value.

## 11. Top-vs-bottom control

| Slice | n | PF | Exp | WR | Mean R | DD | Symbols | Strategies | Top coin share |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| OOS-TOP1-5m | 13221 | 0.8893 | -0.16% | 33.11% | -0.0790 | -2477.43% | 10 | 40 | 18.19% |
| OOS-BOTTOM1-5m | 13221 | 0.8992 | -0.14% | 33.36% | -0.0717 | -2295.69% | 10 | 39 | 15.27% |
| VAL-TOP1-5m | 14764 | 0.9040 | -0.14% | 33.39% | -0.0684 | -2190.54% | 10 | 39 | 25.06% |
| OOS-BOTTOM2-5m | 18162 | 0.9056 | -0.13% | 33.51% | -0.0670 | -2990.61% | 10 | 39 | 18.17% |
| OOS-BOTTOM3-5m | 20492 | 0.9073 | -0.13% | 33.56% | -0.0657 | -3297.31% | 10 | 39 | 17.50% |
| OOS-TOP1-15m | 7345 | 0.9115 | -0.13% | 33.64% | -0.0628 | -1309.22% | 10 | 40 | 27.53% |
| OOS-BOTTOM1-15m | 7345 | 0.9334 | -0.09% | 34.20% | -0.0469 | -1084.54% | 10 | 40 | 23.09% |
| VAL-TOP1-15m | 7766 | 0.8844 | -0.17% | 32.90% | -0.0830 | -1386.79% | 10 | 39 | 21.41% |
| OOS-BOTTOM2-15m | 11195 | 0.9227 | -0.11% | 33.93% | -0.0546 | -1748.62% | 10 | 40 | 17.63% |
| OOS-BOTTOM3-15m | 13366 | 0.8999 | -0.14% | 33.38% | -0.0713 | -2452.30% | 10 | 40 | 15.55% |
| OOS-TOP1-1h | 2599 | 0.8742 | -0.18% | 32.70% | -0.0905 | -549.02% | 10 | 38 | 31.56% |
| OOS-BOTTOM1-1h | 2599 | 0.9445 | -0.08% | 34.44% | -0.0389 | -367.05% | 10 | 37 | 21.89% |
| VAL-TOP1-1h | 2623 | 0.8632 | -0.20% | 32.37% | -0.0990 | -551.46% | 10 | 38 | 26.99% |
| OOS-BOTTOM2-1h | 4385 | 0.9363 | -0.09% | 34.25% | -0.0448 | -687.56% | 10 | 38 | 22.85% |
| OOS-BOTTOM3-1h | 5601 | 0.9237 | -0.11% | 33.96% | -0.0539 | -977.48% | 10 | 38 | 24.76% |
| OOS-TOP1-ALL | 23165 | 0.8946 | -0.15% | 33.24% | -0.0752 | -4214.40% | 10 | 41 | 17.00% |
| OOS-BOTTOM1-ALL | 23165 | 0.9150 | -0.12% | 33.75% | -0.0601 | -3628.86% | 10 | 40 | 17.79% |
| VAL-TOP1-ALL | 25153 | 0.8936 | -0.15% | 33.13% | -0.0761 | -4074.48% | 10 | 40 | 17.09% |
| OOS-BOTTOM2-ALL | 33742 | 0.9153 | -0.12% | 33.75% | -0.0600 | -5353.44% | 10 | 40 | 17.38% |
| OOS-BOTTOM3-ALL | 39459 | 0.9071 | -0.13% | 33.55% | -0.0659 | -6595.37% | 10 | 40 | 15.82% |
| OOS-TOP1-ALL-C1.25 | 23165 | 0.8753 | -0.18% | 33.23% | -0.0902 | -4707.24% | 10 | 41 | 16.31% |
| OOS-TOP1-ALL-C1.5 | 23165 | 0.8565 | -0.21% | 33.23% | -0.1052 | -5305.54% | 10 | 41 | 15.42% |
| OOS-TOP1-ALL-C2.0 | 23165 | 0.8205 | -0.27% | 33.22% | -0.1352 | -6643.48% | 10 | 41 | 14.23% |

## 12. OOS results

| Slice | n | PF | Exp | WR | Mean R | DD | Symbols | Strategies | Top coin share |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| OOS-ALL-5m | 38895 | 0.9179 | -0.12% | 33.82% | -0.0580 | -6553.78% | 10 | 42 | 17.50% |
| OOS-TOP1-5m | 13221 | 0.8893 | -0.16% | 33.11% | -0.0790 | -2477.43% | 10 | 40 | 18.19% |
| OOS-BOTTOM1-5m | 13221 | 0.8992 | -0.14% | 33.36% | -0.0717 | -2295.69% | 10 | 39 | 15.27% |
| OOS-RAND1-5m | 13221 | 0.9042 | -0.14% | 33.47% | -0.0680 | -2254.68% | 10 | 42 | 19.81% |
| OOS-TOP2-5m | 18162 | 0.8973 | -0.15% | 33.30% | -0.0731 | -3254.66% | 10 | 40 | 17.88% |
| OOS-BOTTOM2-5m | 18162 | 0.9056 | -0.13% | 33.51% | -0.0670 | -2990.61% | 10 | 39 | 18.17% |
| OOS-RAND2-5m | 18162 | 0.9038 | -0.14% | 33.45% | -0.0683 | -3067.34% | 10 | 42 | 17.71% |
| OOS-TOP3-5m | 20492 | 0.8983 | -0.14% | 33.33% | -0.0724 | -3652.95% | 10 | 40 | 17.64% |
| OOS-BOTTOM3-5m | 20492 | 0.9073 | -0.13% | 33.56% | -0.0657 | -3297.31% | 10 | 39 | 17.50% |
| OOS-RAND3-5m | 20492 | 0.9007 | -0.14% | 33.37% | -0.0706 | -3541.10% | 10 | 42 | 18.59% |
| OOS-LIQUID-5m | 11267 | 0.8825 | -0.17% | 32.93% | -0.0841 | -2212.10% | 10 | 39 | 19.74% |
| OOS-ALL-15m | 32128 | 0.8851 | -0.16% | 33.01% | -0.0823 | -7056.06% | 10 | 42 | 16.44% |
| OOS-TOP1-15m | 7345 | 0.9115 | -0.13% | 33.64% | -0.0628 | -1309.22% | 10 | 40 | 27.53% |
| OOS-BOTTOM1-15m | 7345 | 0.9334 | -0.09% | 34.20% | -0.0469 | -1084.54% | 10 | 40 | 23.09% |
| OOS-RAND1-15m | 7345 | 0.9100 | -0.13% | 33.64% | -0.0639 | -1301.30% | 10 | 40 | 20.61% |
| OOS-TOP2-15m | 11195 | 0.8948 | -0.15% | 33.25% | -0.0750 | -2223.04% | 10 | 40 | 17.08% |
| OOS-BOTTOM2-15m | 11195 | 0.9227 | -0.11% | 33.93% | -0.0546 | -1748.62% | 10 | 40 | 17.63% |
| OOS-RAND2-15m | 11195 | 0.9128 | -0.12% | 33.69% | -0.0618 | -1941.46% | 10 | 41 | 16.53% |
| OOS-TOP3-15m | 13366 | 0.8939 | -0.15% | 33.21% | -0.0757 | -2628.38% | 10 | 40 | 17.53% |
| OOS-BOTTOM3-15m | 13366 | 0.8999 | -0.14% | 33.38% | -0.0713 | -2452.30% | 10 | 40 | 15.55% |
| OOS-RAND3-15m | 13366 | 0.9012 | -0.14% | 33.41% | -0.0703 | -2566.82% | 10 | 41 | 18.92% |
| OOS-LIQUID-15m | 6101 | 0.9066 | -0.13% | 33.52% | -0.0663 | -1136.85% | 10 | 39 | 27.89% |
| OOS-ALL-1h | 19799 | 0.8637 | -0.20% | 32.49% | -0.0984 | -4706.05% | 10 | 40 | 24.95% |
| OOS-TOP1-1h | 2599 | 0.8742 | -0.18% | 32.70% | -0.0905 | -549.02% | 10 | 38 | 31.56% |
| OOS-BOTTOM1-1h | 2599 | 0.9445 | -0.08% | 34.44% | -0.0389 | -367.05% | 10 | 37 | 21.89% |
| OOS-RAND1-1h | 2599 | 0.9046 | -0.14% | 33.47% | -0.0678 | -483.79% | 10 | 37 | 19.72% |
| OOS-TOP2-1h | 4385 | 0.9133 | -0.12% | 33.66% | -0.0615 | -782.50% | 10 | 38 | 22.74% |
| OOS-BOTTOM2-1h | 4385 | 0.9363 | -0.09% | 34.25% | -0.0448 | -687.56% | 10 | 38 | 22.85% |
| OOS-RAND2-1h | 4385 | 0.9367 | -0.09% | 34.28% | -0.0445 | -709.20% | 10 | 38 | 23.47% |
| OOS-TOP3-1h | 5601 | 0.9126 | -0.12% | 33.65% | -0.0620 | -1012.99% | 10 | 38 | 16.15% |
| OOS-BOTTOM3-1h | 5601 | 0.9237 | -0.11% | 33.96% | -0.0539 | -977.48% | 10 | 38 | 24.76% |
| OOS-RAND3-1h | 5601 | 0.9251 | -0.11% | 33.99% | -0.0528 | -955.02% | 10 | 38 | 29.83% |
| OOS-LIQUID-1h | 1911 | 0.8735 | -0.18% | 32.71% | -0.0910 | -410.75% | 10 | 37 | 23.16% |
| OOS-ALL-ALL | 90822 | 0.8943 | -0.15% | 33.24% | -0.0754 | -18163.42% | 10 | 43 | 17.61% |
| OOS-TOP1-ALL | 23165 | 0.8946 | -0.15% | 33.24% | -0.0752 | -4214.40% | 10 | 41 | 17.00% |
| OOS-BOTTOM1-ALL | 23165 | 0.9150 | -0.12% | 33.75% | -0.0601 | -3628.86% | 10 | 40 | 17.79% |
| OOS-RAND1-ALL | 23165 | 0.9088 | -0.13% | 33.59% | -0.0647 | -3939.25% | 10 | 42 | 16.31% |
| OOS-TOP2-ALL | 33742 | 0.8986 | -0.14% | 33.33% | -0.0722 | -6175.17% | 10 | 41 | 17.09% |
| OOS-BOTTOM2-ALL | 33742 | 0.9153 | -0.12% | 33.75% | -0.0600 | -5353.44% | 10 | 40 | 17.38% |
| OOS-RAND2-ALL | 33742 | 0.9113 | -0.13% | 33.65% | -0.0628 | -5718.24% | 10 | 42 | 16.74% |
| OOS-TOP3-ALL | 39459 | 0.8988 | -0.14% | 33.33% | -0.0720 | -7207.54% | 10 | 41 | 16.51% |
| OOS-BOTTOM3-ALL | 39459 | 0.9071 | -0.13% | 33.55% | -0.0659 | -6595.37% | 10 | 40 | 15.82% |
| OOS-RAND3-ALL | 39459 | 0.9099 | -0.13% | 33.61% | -0.0639 | -6749.38% | 10 | 42 | 15.34% |
| OOS-LIQUID-ALL | 19279 | 0.8892 | -0.16% | 33.09% | -0.0792 | -3628.22% | 10 | 40 | 17.93% |
| OOS-PORT-MAX1 | 141 | 0.8801 | -0.17% | 32.62% | -0.0858 | -44.70% | 10 | 29 | 23.54% |
| OOS-PORT-MAX2 | 305 | 0.9886 | -0.02% | 35.41% | -0.0078 | -50.07% | 10 | 35 | 33.76% |

## 13. Cost sensitivity

| Slice | n | PF | Exp | WR | Mean R | DD | Symbols | Strategies | Top coin share |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| OOS-TOP1-ALL-C1.25 | 23165 | 0.8753 | -0.18% | 33.23% | -0.0902 | -4707.24% | 10 | 41 | 16.31% |
| OOS-RAND1-ALL-C1.25 | 23165 | 0.8892 | -0.16% | 33.59% | -0.0797 | -4432.27% | 10 | 42 | 16.23% |
| OOS-TOP1-ALL-C1.5 | 23165 | 0.8565 | -0.21% | 33.23% | -0.1052 | -5305.54% | 10 | 41 | 15.42% |
| OOS-RAND1-ALL-C1.5 | 23165 | 0.8702 | -0.19% | 33.59% | -0.0947 | -4925.29% | 10 | 42 | 16.17% |
| OOS-TOP1-ALL-C2.0 | 23165 | 0.8205 | -0.27% | 33.22% | -0.1352 | -6643.48% | 10 | 41 | 14.23% |
| OOS-RAND1-ALL-C2.0 | 23165 | 0.8336 | -0.25% | 33.59% | -0.1247 | -6245.20% | 10 | 42 | 16.09% |

TOP1 remains worse than random at BASE, +25%, +50%, and +100% costs.

## 14. Portfolio simulation

| Slice | n | PF | Exp | WR | Mean R | DD | Symbols | Strategies | Top coin share |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| OOS-LIQUID-5m | 11267 | 0.8825 | -0.17% | 32.93% | -0.0841 | -2212.10% | 10 | 39 | 19.74% |
| OOS-LIQUID-15m | 6101 | 0.9066 | -0.13% | 33.52% | -0.0663 | -1136.85% | 10 | 39 | 27.89% |
| OOS-LIQUID-1h | 1911 | 0.8735 | -0.18% | 32.71% | -0.0910 | -410.75% | 10 | 37 | 23.16% |
| OOS-LIQUID-ALL | 19279 | 0.8892 | -0.16% | 33.09% | -0.0792 | -3628.22% | 10 | 40 | 17.93% |
| OOS-PORT-MAX1 | 141 | 0.8801 | -0.17% | 32.62% | -0.0858 | -44.70% | 10 | 29 | 23.54% |
| OOS-PORT-MAX2 | 305 | 0.9886 | -0.02% | 35.41% | -0.0078 | -50.07% | 10 | 35 | 33.76% |
Max 1 / max 2 Isolated slots, one coin at a time, no shared margin. TOP-1 globally per timeframe timestamp.

## 15. Failure analysis

- Fewer trades is not success. Random same-count is the control.
- OOS TOP1-ALL PF 0.8946 vs RAND1 0.9088 vs BOTTOM1 0.9150. Ranking is inverted: bottom-ranked signals beat top-ranked.
- OOS CONS3-5m PF 1.024 is the only PF>1 slice. It has no random-selection control, fails on 15m (0.87) and 1h (0.90), and ALL CONS3 is 0.922. One timeframe is not an edge.
- Isolated PORT-MAX2 PF 0.99 still negative expectancy. Opportunity-cap does not create alpha.
- Cost +25/+50/+100: TOP1 remains worse than random at every multiplier.
- 10-coin 5m/15m/1h is the routing experiment. Full 528×5m was not loaded (5m JSON size).
- Funding/OI not attached to 5m/15m events. Liquidations still DATA_UNAVAILABLE.
- ML was not run: validation TOP1 did not beat random.

## 16. Final classification

**REJECTED**

**NO ROBUST CONDITIONAL ALPHA FOUND**

Do not create another strategy wave from this architecture. Isolated LOW and LIVE were not changed.

## Run notes
- Wave-5 router. Existing signals only. LIVE=OFF. Isolated LOW unchanged.
- Scores use only events with ExitTime < current SignalTime (purged of open/overlapping labels).
- Pre-registered: lookback 90d, min bucket 20, top-K 1/2/3, train end 2025-11-30, val end 2026-04-25.
- Equal-weight rank of 5 causal components. No ML unless validation beats random.
- OOS TOP1 vs RAND1: PF 0.8946 vs 0.9088; exp -0.0015 vs -0.0013.
- OOS TOP1 vs BOTTOM1: PF 0.8946 vs 0.9150.
- Validation beat random: False. ML skipped because deterministic routing did not clear the validation gate or OOS random control.
- NO ROBUST CONDITIONAL ALPHA FOUND. Router did not beat random same-count selection on OOS with positive expectancy.
- Wave-5 router. Existing signals only. LIVE disabled. Isolated LOW $1000 / 0.5% / 3x unchanged.
- Window 2024-09-18 → 2026-09-19. Timeframes 5m/15m/1h. Coins BTCUSDT,ETHUSDT,BNBUSDT,SOLUSDT,XRPUSDT,DOGEUSDT,ADAUSDT,AVAXUSDT,LINKUSDT,LTCUSDT.
- Harvest skips a second same-strategy same-coin signal while the Isolated book is still in the prior simulated trade (causal occupancy).
- Funding/OI not joined (Wave-4 Vision OI exists but was not attached to 5m/15m events). Liquidations DATA_UNAVAILABLE.
- Full 528-coin 5m harvest not loaded (cache JSON size). Router experiment is 10 liquid coins × 3 timeframes.
- Strategy universe 43.
- Harvest 5m: 308111 events.
- Harvest 15m: 234265 events.
- Harvest 1h: 125597 events.
