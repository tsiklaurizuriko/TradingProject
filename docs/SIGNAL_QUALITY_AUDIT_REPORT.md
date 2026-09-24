# Signal quality audit

Feature manifest SHA-256 `07c075da4eb8a76b0df9aeff97c075f974f2317c472554c3875404677671e5c4`.

This audit does not create a strategy and does not call any relationship an edge.

## 1. Dataset

Trades: 11125. Winners: 3543. Losers: 7582.
Same reconstructed book as the trade-failure phase: Frozen Five at 15m, Final Five primary timeframes, Phase 8 baselines at 5m, BTCUSDT, ETHUSDT, BNBUSDT, Model B costs. Near-miss was not added.
Gross expectancy -0.29. Net expectancy -0.56.

## 2. Feature definitions

Fixed before aggregation. Spearman rank correlation is between the pre-entry feature and net PnL. REPEATABLE requires the same sign and absolute rho of at least 0.05 in IS, validation, and OOS, each with at least 200 trades, the same sign in at least 3 of 4 chronological blocks with at least 100 trades, at least two independent family groups, and at least two of BTC, ETH, and BNB. FAMILY_SPECIFIC: the three windows agree, but only one family group meets the slice bar. SYMBOL_SPECIFIC: the three windows agree, but only one symbol meets the slice bar. OOS_ONLY: OOS meets the bar and IS does not share that sign. UNSTABLE: the windows disagree, or they agree but the block, family, or symbol bar fails. NO_EVIDENCE: everything else. Tertile cuts are the 1/3 and 2/3 quantiles of the feature on IS trades only. Session and weekday are pre-registered slices, not ranked features, and they are not eligible for REPEATABLE. None of these labels is an edge.

37 features were hashed before aggregation. Signed features agree with the trade when positive. `minutes_since_signal` is DATA_UNAVAILABLE. Session and weekday are slices only.

## 3. Winner vs loser

| Feature | n | Winner p25 | Winner p50 | Winner p75 | Loser p25 | Loser p50 | Loser p75 | Effect |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| signed_ret_1 | 11125 | 0.001 | 0.003 | 0.005 | 0.001 | 0.003 | 0.005 | 0.011 |
| signed_ret_3 | 11125 | -0.001 | 0.003 | 0.007 | 0.000 | 0.003 | 0.007 | -0.018 |
| signed_ret_5 | 11125 | -0.001 | 0.003 | 0.007 | -0.001 | 0.003 | 0.007 | -0.032 |
| signed_ret_10 | 11124 | -0.002 | 0.003 | 0.008 | -0.002 | 0.003 | 0.008 | -0.050 |
| dist_high_20 | 11123 | 0.003 | 0.007 | 0.013 | 0.003 | 0.007 | 0.013 | -0.002 |
| dist_low_20 | 11123 | 0.003 | 0.006 | 0.013 | 0.003 | 0.006 | 0.012 | -0.004 |
| body_ratio | 11125 | 0.426 | 0.630 | 0.779 | 0.426 | 0.622 | 0.775 | 0.023 |
| upper_wick_ratio | 11125 | 0.052 | 0.143 | 0.297 | 0.047 | 0.149 | 0.301 | -0.023 |
| lower_wick_ratio | 11125 | 0.056 | 0.162 | 0.324 | 0.057 | 0.160 | 0.319 | 0.006 |
| consecutive_bars | 11125 | 1.000 | 1.000 | 2.000 | 1.000 | 1.000 | 3.000 | 0.000 |
| signed_accel_3 | 11125 | 0.000 | 0.003 | 0.007 | 0.000 | 0.003 | 0.007 | -0.015 |
| atr_percentile | 11124 | 0.200 | 0.560 | 0.900 | 0.180 | 0.520 | 0.900 | 0.056 |
| atr_over_price | 11124 | 0.003 | 0.004 | 0.005 | 0.002 | 0.003 | 0.005 | 0.031 |
| range_over_atr | 11124 | 0.979 | 1.336 | 1.863 | 0.979 | 1.334 | 1.859 | 0.003 |
| realized_vol_20 | 11123 | 0.002 | 0.002 | 0.003 | 0.001 | 0.002 | 0.003 | 0.020 |
| prior_compression | 11124 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 |
| adx | 11115 | 18.594 | 25.201 | 35.424 | 18.223 | 25.095 | 35.304 | 0.006 |
| signed_ema20_dist | 11123 | -0.001 | 0.003 | 0.007 | -0.001 | 0.003 | 0.007 | -0.022 |
| signed_ema50_dist | 11109 | -0.001 | 0.004 | 0.009 | 0.000 | 0.004 | 0.009 | -0.031 |
| signed_ema20_slope | 11118 | -0.001 | 0.000 | 0.002 | -0.001 | 0.000 | 0.002 | -0.025 |
| signed_ema50_slope | 11108 | -0.001 | 0.000 | 0.001 | 0.000 | 0.000 | 0.001 | -0.002 |
| signed_hourly_bias | 11125 | -1.000 | 1.000 | 1.000 | -1.000 | 1.000 | 1.000 | 0.000 |
| bos_aligned | 11125 | 0.000 | 0.000 | 1.000 | 0.000 | 0.000 | 1.000 | 0.000 |
| choch_aligned | 11125 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 |
| relative_volume | 11123 | 0.991 | 1.473 | 2.322 | 0.981 | 1.455 | 2.337 | 0.013 |
| volume_over_median_20 | 11123 | 1.155 | 1.830 | 3.202 | 1.144 | 1.815 | 3.155 | 0.007 |
| signed_vwap_dist | 11125 | -0.001 | 0.004 | 0.010 | -0.001 | 0.004 | 0.010 | -0.013 |
| signed_vwap_slope | 11125 | 0.000 | 0.000 | 0.001 | 0.000 | 0.000 | 0.001 | -0.037 |
| btc_signed_ret_20 | 11125 | -0.008 | 0.005 | 0.019 | -0.009 | 0.004 | 0.018 | 0.058 |
| btc_adx | 11125 | 20.697 | 27.461 | 36.332 | 20.747 | 26.617 | 35.667 | 0.056 |
| btc_atr_percentile | 11125 | 0.180 | 0.620 | 0.880 | 0.200 | 0.600 | 0.880 | 0.029 |
| btc_same_clock_signed | 11125 | -0.008 | 0.005 | 0.019 | -0.009 | 0.004 | 0.018 | 0.058 |
| coins_same_sign | 11102 | 0.000 | 1.000 | 1.000 | 0.000 | 1.000 | 1.000 | 0.000 |
| signals_already_closed | 11125 | 0.000 | 0.000 | 1.000 | 0.000 | 0.000 | 1.000 | 0.000 |
| symbols_already_closed | 11125 | 0.000 | 0.000 | 1.000 | 0.000 | 0.000 | 1.000 | 0.000 |
| minutes_since_signal | 0 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 |
| minutes_since_trade | 11122 | 20.000 | 75.000 | 195.000 | 20.000 | 80.000 | 215.000 | -0.026 |

Effect is the winner median minus the loser median, divided by the interquartile range. It is not a profit factor.

## 4. Feature results

| Feature | IS rho | VAL rho | OOS rho | IS high-low net | VAL high-low net | OOS high-low net | Blocks | Families | Symbols | Label |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| signed_ret_1 | 0.021 | 0.026 | 0.036 | -0.11 | -0.20 | 0.00 | 0 | 0 | 0 | NO_EVIDENCE |
| signed_ret_3 | 0.079 | 0.086 | 0.067 | 0.16 | -0.13 | -0.27 | 4 | 1 | 3 | FAMILY_SPECIFIC |
| signed_ret_5 | 0.094 | 0.083 | 0.078 | 0.34 | -0.14 | -0.27 | 4 | 0 | 3 | UNSTABLE |
| signed_ret_10 | 0.091 | 0.086 | 0.074 | 0.39 | 0.07 | -0.42 | 4 | 0 | 3 | UNSTABLE |
| dist_high_20 | -0.067 | -0.068 | -0.042 | -0.36 | -0.05 | -0.22 | 3 | 3 | 2 | NO_EVIDENCE |
| dist_low_20 | -0.074 | -0.046 | -0.002 | -0.56 | -0.53 | 0.46 | 2 | 2 | 2 | NO_EVIDENCE |
| body_ratio | 0.025 | 0.033 | 0.043 | 0.01 | 0.33 | 0.11 | 0 | 0 | 0 | NO_EVIDENCE |
| upper_wick_ratio | -0.003 | -0.010 | 0.001 | 0.07 | -0.05 | 0.29 | 0 | 0 | 0 | NO_EVIDENCE |
| lower_wick_ratio | -0.020 | -0.021 | -0.040 | -0.06 | -0.33 | -0.20 | 0 | 0 | 0 | NO_EVIDENCE |
| consecutive_bars | 0.086 | 0.055 | 0.042 | 0.27 | -0.19 | -0.20 | 2 | 1 | 3 | NO_EVIDENCE |
| signed_accel_3 | 0.024 | 0.046 | 0.044 | -0.12 | 0.01 | -0.03 | 0 | 0 | 0 | NO_EVIDENCE |
| atr_percentile | -0.024 | -0.036 | 0.025 | -0.14 | -0.23 | 0.11 | 0 | 0 | 0 | NO_EVIDENCE |
| atr_over_price | -0.089 | -0.053 | -0.011 | -0.48 | -0.29 | 0.46 | 2 | 5 | 2 | NO_EVIDENCE |
| range_over_atr | 0.063 | 0.077 | 0.051 | 0.18 | 0.22 | -0.05 | 3 | 0 | 3 | UNSTABLE |
| realized_vol_20 | -0.097 | -0.077 | -0.020 | -0.59 | -0.49 | 0.36 | 2 | 4 | 3 | NO_EVIDENCE |
| prior_compression | -0.054 | -0.087 | -0.106 | -0.04 | -0.07 | -0.07 | 3 | 1 | 3 | FAMILY_SPECIFIC |
| adx | -0.127 | -0.071 | -0.061 | -0.78 | 0.11 | 0.23 | 4 | 0 | 3 | UNSTABLE |
| signed_ema20_dist | 0.098 | 0.087 | 0.105 | 0.20 | -0.11 | 0.04 | 4 | 0 | 3 | UNSTABLE |
| signed_ema50_dist | 0.082 | 0.120 | 0.097 | 0.17 | 0.62 | 0.02 | 4 | 1 | 3 | FAMILY_SPECIFIC |
| signed_ema20_slope | 0.084 | 0.097 | 0.096 | 0.13 | 0.16 | 0.49 | 4 | 0 | 3 | UNSTABLE |
| signed_ema50_slope | 0.061 | 0.118 | 0.077 | 0.07 | 1.07 | 0.36 | 4 | 2 | 2 | REPEATABLE |
| signed_hourly_bias | -0.002 | 0.070 | 0.061 | 0.06 | 0.86 | 0.76 | 1 | 1 | 0 | OOS_ONLY |
| bos_aligned | 0.032 | 0.037 | -0.027 | 0.16 | 0.13 | -0.54 | 0 | 0 | 0 | NO_EVIDENCE |
| choch_aligned | 0.009 | 0.004 | -0.025 | 0.01 | -0.01 | -0.09 | 0 | 0 | 0 | NO_EVIDENCE |
| relative_volume | 0.115 | 0.130 | 0.073 | 0.53 | 0.41 | -0.01 | 4 | 0 | 3 | UNSTABLE |
| volume_over_median_20 | 0.107 | 0.109 | 0.062 | 0.50 | 0.26 | -0.13 | 4 | 0 | 3 | UNSTABLE |
| signed_vwap_dist | 0.050 | 0.088 | 0.048 | -0.01 | 0.63 | -0.23 | 3 | 3 | 2 | NO_EVIDENCE |
| signed_vwap_slope | 0.061 | 0.104 | 0.077 | 0.11 | 0.44 | 0.31 | 4 | 2 | 2 | REPEATABLE |
| btc_signed_ret_20 | 0.023 | 0.130 | 0.050 | -0.02 | 1.37 | 0.49 | 0 | 0 | 0 | NO_EVIDENCE |
| btc_adx | -0.059 | -0.029 | -0.006 | -0.41 | 0.46 | 0.46 | 2 | 1 | 1 | NO_EVIDENCE |
| btc_atr_percentile | -0.089 | -0.063 | -0.057 | -0.47 | 0.17 | -0.10 | 3 | 1 | 3 | FAMILY_SPECIFIC |
| btc_same_clock_signed | 0.023 | 0.130 | 0.050 | -0.02 | 1.37 | 0.49 | 0 | 0 | 0 | NO_EVIDENCE |
| coins_same_sign | -0.018 | -0.017 | 0.008 | -0.03 | -0.02 | 0.02 | 0 | 0 | 0 | NO_EVIDENCE |
| signals_already_closed | 0.018 | -0.008 | 0.007 | -0.11 | -0.27 | -0.29 | 0 | 0 | 0 | NO_EVIDENCE |
| symbols_already_closed | 0.019 | -0.005 | 0.000 | -0.11 | -0.27 | -0.29 | 0 | 0 | 0 | NO_EVIDENCE |
| minutes_since_signal | n/a | n/a | n/a | 0.00 | 0.00 | 0.00 | 0 | 0 | 0 | NO_EVIDENCE |
| minutes_since_trade | 0.086 | 0.095 | 0.086 | 0.24 | 0.23 | 0.44 | 4 | 1 | 3 | FAMILY_SPECIFIC |

High-low net uses tertile cuts taken from IS feature values only. A zero gap means a bucket had fewer than 30 trades.

## 5. IS, validation, OOS

The rho columns above are the stability test. A feature that is large in only one window does not pass.

## 6. Chronological stability

Blocks is how many of the four equal index quarters have at least 100 trades and absolute rho of at least 0.05 with the reference sign.

## 7. Family stability

Families counts independent groups: FROZEN_TREND, FROZEN_MEAN, PA_SWEEP, PA_FAILED, PA_CONTINUATION, PA_STRUCTURE. Two price-action variants in one group are not two families.

## 8. Symbol stability

Symbols counts how many of BTC, ETH, and BNB meet the same bar. One coin is not enough.

## 9. Long and short

- LONG: n=5814 win rate 31.6% gross -0.43 net -0.69
- SHORT: n=5311 win rate 32.1% gross -0.13 net -0.40

## 10. BTC context

- btc_signed_ret_20: NO_EVIDENCE. IS rho 0.023, validation rho 0.130, OOS rho 0.050.
- btc_adx: NO_EVIDENCE. IS rho -0.059, validation rho -0.029, OOS rho -0.006.
- btc_atr_percentile: FAMILY_SPECIFIC. IS rho -0.089, validation rho -0.063, OOS rho -0.057.
- btc_same_clock_signed: NO_EVIDENCE. IS rho 0.023, validation rho 0.130, OOS rho 0.050.
- coins_same_sign: NO_EVIDENCE. IS rho -0.018, validation rho -0.017, OOS rho 0.008.

## 11. Simultaneous signals

- signals_already_closed: NO_EVIDENCE. IS rho 0.018, validation rho -0.008, OOS rho 0.007.
- symbols_already_closed: NO_EVIDENCE. IS rho 0.019, validation rho -0.005, OOS rho 0.000.
- minutes_since_trade: FAMILY_SPECIFIC. IS rho 0.086, validation rho 0.095, OOS rho 0.086.

`minutes_since_signal` is DATA_UNAVAILABLE.

UTC session slices, not ranked features:

| Session | Phase | n | Win rate | Net expectancy |
| --- | --- | ---: | ---: | ---: |
| ASIA | IS | 2029 | 30.6% | -0.62 |
| EUROPE | IS | 2608 | 34.1% | -0.32 |
| US | IS | 2715 | 30.5% | -0.92 |
| ASIA | VALIDATION | 616 | 34.6% | -0.29 |
| EUROPE | VALIDATION | 816 | 31.9% | -0.57 |
| US | VALIDATION | 667 | 28.8% | -0.50 |
| ASIA | OOS | 488 | 30.7% | -0.52 |
| EUROPE | OOS | 672 | 35.1% | -0.13 |
| US | OOS | 514 | 30.4% | -0.53 |

## 12. Strongest recurring relationships

- signed_ret_3: FAMILY_SPECIFIC. IS rho 0.079, validation rho 0.086, OOS rho 0.067.
- signed_ret_5: UNSTABLE. IS rho 0.094, validation rho 0.083, OOS rho 0.078.
- signed_ret_10: UNSTABLE. IS rho 0.091, validation rho 0.086, OOS rho 0.074.
- range_over_atr: UNSTABLE. IS rho 0.063, validation rho 0.077, OOS rho 0.051.
- prior_compression: FAMILY_SPECIFIC. IS rho -0.054, validation rho -0.087, OOS rho -0.106.
- adx: UNSTABLE. IS rho -0.127, validation rho -0.071, OOS rho -0.061.
- signed_ema20_dist: UNSTABLE. IS rho 0.098, validation rho 0.087, OOS rho 0.105.
- signed_ema50_dist: FAMILY_SPECIFIC. IS rho 0.082, validation rho 0.120, OOS rho 0.097.
- signed_ema20_slope: UNSTABLE. IS rho 0.084, validation rho 0.097, OOS rho 0.096.
- signed_ema50_slope: REPEATABLE. IS rho 0.061, validation rho 0.118, OOS rho 0.077.
- signed_hourly_bias: OOS_ONLY. IS rho -0.002, validation rho 0.070, OOS rho 0.061.
- relative_volume: UNSTABLE. IS rho 0.115, validation rho 0.130, OOS rho 0.073.
- volume_over_median_20: UNSTABLE. IS rho 0.107, validation rho 0.109, OOS rho 0.062.
- signed_vwap_slope: REPEATABLE. IS rho 0.061, validation rho 0.104, OOS rho 0.077.
- btc_atr_percentile: FAMILY_SPECIFIC. IS rho -0.089, validation rho -0.063, OOS rho -0.057.
- minutes_since_trade: FAMILY_SPECIFIC. IS rho 0.086, validation rho 0.095, OOS rho 0.086.

## 13. Unstable relationships

signed_ret_3 (FAMILY_SPECIFIC), signed_ret_5 (UNSTABLE), signed_ret_10 (UNSTABLE), range_over_atr (UNSTABLE), prior_compression (FAMILY_SPECIFIC), adx (UNSTABLE), signed_ema20_dist (UNSTABLE), signed_ema50_dist (FAMILY_SPECIFIC), signed_ema20_slope (UNSTABLE), signed_hourly_bias (OOS_ONLY), relative_volume (UNSTABLE), volume_over_median_20 (UNSTABLE), btc_atr_percentile (FAMILY_SPECIFIC), minutes_since_trade (FAMILY_SPECIFIC).

## 14. Data limitations

The book is BTC, ETH, and BNB only. Near-miss is not a second sample. Funding is not in these replays. Unfilled signals were not stored. ATR percentile is a fraction from 0 to 1. Session VWAP is the existing causal session VWAP. BOS and CHoCH come from the existing causal structure book. No feature was added after the manifest hash.

## 15. Conclusion

- REPEATABLE: 2 features.
- FAMILY_SPECIFIC: 5 features.
- SYMBOL_SPECIFIC: none.
- OOS_ONLY: 1 features.
- UNSTABLE: 8 features.
- NO_EVIDENCE: 21 features.

Two features meet the pre-registered REPEATABLE bar: `signed_ema50_slope` and `signed_vwap_slope`. Their rank correlations are about 0.06 to 0.12. Winner and loser medians are the same at the reported precision, so the typical trade is not separated. The label is not an edge. Several momentum and volume features keep the same sign across IS, validation, and OOS in the pooled book and still fail the within-family bar, so they stay UNSTABLE. BTC context, simultaneous signals, candle shape, and BOS/CHoCH stay at NO_EVIDENCE.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.
