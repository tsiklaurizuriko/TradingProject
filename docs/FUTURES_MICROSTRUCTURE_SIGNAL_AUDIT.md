# Futures microstructure signal audit

Feature manifest SHA-256 `b3b85ed16e31dd4fa41179eaafd02ade56214b2477a3fa338cdd977d5bcac59b`.

This audit joins causal futures data to the existing reconstructed book. It does not create a strategy, change a parameter, or call any relationship an edge.

## 1. Dataset

Trades: 11125. Winners: 3543. Losers: 7582.
Same arms as the signal-quality audit: Frozen Five at 15m, Final Five primary timeframes, Phase 8 baselines at 5m, BTCUSDT, ETHUSDT, BNBUSDT, Model B. Near-miss was not added. Entry and exit rules were not changed.
Gross expectancy -0.29. Net expectancy -0.56.

## 2. Feature definitions

Same rule as signal-quality-v1. Spearman rank correlation is between the pre-entry feature and net PnL. REPEATABLE requires the same sign and absolute rho of at least 0.05 in IS, validation, and OOS, each with at least 200 trades, the same sign in at least 3 of 4 chronological blocks with at least 100 trades, at least two independent family groups, and at least two of BTC, ETH, and BNB. FAMILY_SPECIFIC: the three windows agree, but only one family group meets the slice bar. SYMBOL_SPECIFIC: the three windows agree, but only one symbol meets the slice bar. OOS_ONLY: OOS meets the bar and IS does not share that sign. UNSTABLE: the windows disagree, or they agree but the block, family, or symbol bar fails. NO_EVIDENCE: everything else. Tertile cuts are the 1/3 and 2/3 quantiles of the feature on IS trades only. None of these labels is an edge. Features are not signed. Long and short are reported as slices.

Scored features (15): oi_change_1, oi_change_3, oi_change_12, oi_percentile, funding_rate, funding_percentile, funding_change, taker_imbalance, taker_imbalance_change, taker_buy_ratio, normalized_basis, basis_change, basis_percentile, depth_imbalance_1pct, depth_imbalance_change.
Every catalog feature had coverage on every coin and timeframe before aggregation.
Open-interest change is a fractional change. Funding change, basis change, taker-imbalance change, and depth-imbalance change are differences. Percentiles use only prior fresh observations.

## 3. Winner versus loser distributions

| Feature | n | Winner p25 | Winner p50 | Winner p75 | Loser p25 | Loser p50 | Loser p75 | Effect | Gross | Net | Win rate | PF |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| oi_change_1 | 11118 | -0.001 | 0.000 | 0.001 | -0.001 | 0.000 | 0.001 | 0.030 | -0.28 | -0.55 | 31.8% | 0.788 |
| oi_change_3 | 11118 | -0.002 | 0.000 | 0.002 | -0.002 | 0.000 | 0.002 | 0.031 | -0.28 | -0.55 | 31.8% | 0.788 |
| oi_change_12 | 11117 | -0.004 | 0.000 | 0.004 | -0.004 | 0.000 | 0.004 | 0.033 | -0.28 | -0.55 | 31.9% | 0.788 |
| oi_percentile | 11096 | 0.140 | 0.510 | 0.870 | 0.120 | 0.470 | 0.860 | 0.055 | -0.28 | -0.55 | 31.9% | 0.789 |
| funding_rate | 11125 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | -0.003 | -0.29 | -0.56 | 31.8% | 0.787 |
| funding_percentile | 11110 | 0.079 | 0.393 | 0.742 | 0.090 | 0.393 | 0.742 | 0.000 | -0.29 | -0.56 | 31.8% | 0.785 |
| funding_change | 11125 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | -0.29 | -0.56 | 31.8% | 0.787 |
| taker_imbalance | 11125 | -0.160 | -0.020 | 0.148 | -0.165 | -0.006 | 0.157 | -0.044 | -0.29 | -0.56 | 31.8% | 0.787 |
| taker_imbalance_change | 11125 | -0.165 | 0.001 | 0.180 | -0.162 | 0.009 | 0.178 | -0.023 | -0.29 | -0.56 | 31.8% | 0.787 |
| taker_buy_ratio | 11125 | 0.420 | 0.490 | 0.574 | 0.418 | 0.497 | 0.579 | -0.044 | -0.29 | -0.56 | 31.8% | 0.787 |
| normalized_basis | 11125 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.003 | -0.29 | -0.56 | 31.8% | 0.787 |
| basis_change | 11125 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | -0.028 | -0.29 | -0.56 | 31.8% | 0.787 |
| basis_percentile | 11103 | 0.200 | 0.500 | 0.810 | 0.220 | 0.540 | 0.810 | -0.068 | -0.28 | -0.55 | 31.9% | 0.789 |
| depth_imbalance_1pct | 11118 | -0.099 | 0.030 | 0.146 | -0.097 | 0.033 | 0.152 | -0.013 | -0.28 | -0.55 | 31.8% | 0.788 |
| depth_imbalance_change | 11118 | -0.066 | 0.000 | 0.061 | -0.063 | 0.000 | 0.064 | 0.000 | -0.28 | -0.55 | 31.8% | 0.788 |

Effect is the winner median minus the loser median, divided by the interquartile range. Profit factor is shown when the sample has at least 200 trades and both gains and losses. Funding rate, funding change, and normalized basis are smaller than 0.001, so those quartile columns print as 0.000. The rank correlations use the unrounded values.

## 4. Feature-by-feature results

| Feature | IS rho | VAL rho | OOS rho | IS n | VAL n | OOS n | IS high-low net | VAL high-low net | OOS high-low net | Blocks | Families | Symbols | Label |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| oi_change_1 | 0.013 | -0.013 | 0.020 | 7352 | 2099 | 1667 | 0.22 | -0.19 | 0.21 | 0 | 0 | 0 | NO_EVIDENCE |
| oi_change_3 | 0.000 | -0.008 | -0.018 | 7352 | 2099 | 1667 | 0.06 | 0.00 | -0.04 | 0 | 0 | 0 | NO_EVIDENCE |
| oi_change_12 | -0.009 | 0.023 | -0.011 | 7351 | 2099 | 1667 | -0.02 | 0.46 | -0.03 | 0 | 0 | 0 | NO_EVIDENCE |
| oi_percentile | -0.012 | 0.006 | 0.003 | 7330 | 2099 | 1667 | 0.12 | 0.24 | 0.31 | 0 | 0 | 0 | NO_EVIDENCE |
| funding_rate | -0.008 | 0.017 | 0.044 | 7352 | 2099 | 1674 | -0.06 | -0.09 | 0.70 | 0 | 0 | 0 | NO_EVIDENCE |
| funding_percentile | -0.006 | 0.013 | 0.047 | 7337 | 2099 | 1674 | -0.19 | 0.00 | 0.85 | 0 | 0 | 0 | NO_EVIDENCE |
| funding_change | -0.023 | -0.005 | 0.013 | 7352 | 2099 | 1674 | -0.26 | -0.09 | 0.31 | 0 | 0 | 0 | NO_EVIDENCE |
| taker_imbalance | -0.046 | -0.035 | 0.122 | 7352 | 2099 | 1674 | -0.63 | -0.30 | 1.39 | 1 | 0 | 0 | OOS_ONLY |
| taker_imbalance_change | -0.047 | -0.049 | 0.133 | 7352 | 2099 | 1674 | -0.58 | -0.44 | 1.33 | 1 | 0 | 0 | OOS_ONLY |
| taker_buy_ratio | -0.046 | -0.035 | 0.122 | 7352 | 2099 | 1674 | -0.63 | -0.30 | 1.39 | 1 | 0 | 0 | OOS_ONLY |
| normalized_basis | -0.015 | -0.010 | 0.041 | 7352 | 2099 | 1674 | -0.39 | -0.01 | 0.46 | 0 | 0 | 0 | NO_EVIDENCE |
| basis_change | -0.034 | -0.042 | 0.027 | 7352 | 2099 | 1674 | -0.52 | -0.39 | 0.57 | 0 | 0 | 0 | NO_EVIDENCE |
| basis_percentile | -0.046 | -0.028 | 0.008 | 7330 | 2099 | 1674 | -0.50 | -0.33 | 0.40 | 0 | 0 | 0 | NO_EVIDENCE |
| depth_imbalance_1pct | 0.022 | 0.017 | -0.057 | 7352 | 2099 | 1667 | 0.09 | 0.30 | -0.54 | 0 | 0 | 0 | OOS_ONLY |
| depth_imbalance_change | 0.029 | 0.006 | -0.079 | 7352 | 2099 | 1667 | 0.36 | 0.05 | -0.95 | 1 | 0 | 0 | OOS_ONLY |

High-low net uses tertile cuts taken from IS feature values only. A zero gap means a bucket had fewer than 30 trades.

## 5. IS, validation, OOS

| Feature | Phase | n | Winner p50 | Loser p50 | Rho | Net |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| oi_change_1 | IS | 7352 | 0.000 | 0.000 | 0.013 | -0.62 |
| oi_change_1 | VALIDATION | 2099 | 0.000 | 0.000 | -0.013 | -0.47 |
| oi_change_1 | OOS | 1667 | 0.000 | 0.000 | 0.020 | -0.36 |
| oi_change_3 | IS | 7352 | 0.000 | 0.000 | 0.000 | -0.62 |
| oi_change_3 | VALIDATION | 2099 | 0.000 | 0.000 | -0.008 | -0.47 |
| oi_change_3 | OOS | 1667 | 0.000 | 0.000 | -0.018 | -0.36 |
| oi_change_12 | IS | 7351 | 0.000 | 0.000 | -0.009 | -0.62 |
| oi_change_12 | VALIDATION | 2099 | 0.000 | 0.000 | 0.023 | -0.47 |
| oi_change_12 | OOS | 1667 | 0.000 | 0.000 | -0.011 | -0.36 |
| oi_percentile | IS | 7330 | 0.520 | 0.490 | -0.012 | -0.62 |
| oi_percentile | VALIDATION | 2099 | 0.450 | 0.410 | 0.006 | -0.47 |
| oi_percentile | OOS | 1667 | 0.490 | 0.460 | 0.003 | -0.36 |
| funding_rate | IS | 7352 | 0.000 | 0.000 | -0.008 | -0.62 |
| funding_rate | VALIDATION | 2099 | 0.000 | 0.000 | 0.017 | -0.47 |
| funding_rate | OOS | 1674 | 0.000 | 0.000 | 0.044 | -0.37 |
| funding_percentile | IS | 7337 | 0.337 | 0.337 | -0.006 | -0.63 |
| funding_percentile | VALIDATION | 2099 | 0.483 | 0.573 | 0.013 | -0.47 |
| funding_percentile | OOS | 1674 | 0.607 | 0.539 | 0.047 | -0.37 |
| funding_change | IS | 7352 | 0.000 | 0.000 | -0.023 | -0.62 |
| funding_change | VALIDATION | 2099 | 0.000 | 0.000 | -0.005 | -0.47 |
| funding_change | OOS | 1674 | 0.000 | 0.000 | 0.013 | -0.37 |
| taker_imbalance | IS | 7352 | -0.031 | -0.003 | -0.046 | -0.62 |
| taker_imbalance | VALIDATION | 2099 | -0.031 | -0.014 | -0.035 | -0.47 |
| taker_imbalance | OOS | 1674 | 0.075 | -0.007 | 0.122 | -0.37 |
| taker_imbalance_change | IS | 7352 | -0.004 | 0.014 | -0.047 | -0.62 |
| taker_imbalance_change | VALIDATION | 2099 | -0.024 | 0.000 | -0.049 | -0.47 |
| taker_imbalance_change | OOS | 1674 | 0.071 | -0.001 | 0.133 | -0.37 |
| taker_buy_ratio | IS | 7352 | 0.485 | 0.499 | -0.046 | -0.62 |
| taker_buy_ratio | VALIDATION | 2099 | 0.484 | 0.493 | -0.035 | -0.47 |
| taker_buy_ratio | OOS | 1674 | 0.538 | 0.496 | 0.122 | -0.37 |
| normalized_basis | IS | 7352 | 0.000 | 0.000 | -0.015 | -0.62 |
| normalized_basis | VALIDATION | 2099 | 0.000 | 0.000 | -0.010 | -0.47 |
| normalized_basis | OOS | 1674 | 0.000 | 0.000 | 0.041 | -0.37 |
| basis_change | IS | 7352 | 0.000 | 0.000 | -0.034 | -0.62 |
| basis_change | VALIDATION | 2099 | 0.000 | 0.000 | -0.042 | -0.47 |
| basis_change | OOS | 1674 | 0.000 | 0.000 | 0.027 | -0.37 |
| basis_percentile | IS | 7330 | 0.490 | 0.540 | -0.046 | -0.62 |
| basis_percentile | VALIDATION | 2099 | 0.470 | 0.510 | -0.028 | -0.47 |
| basis_percentile | OOS | 1674 | 0.570 | 0.560 | 0.008 | -0.37 |
| depth_imbalance_1pct | IS | 7352 | 0.026 | 0.027 | 0.022 | -0.62 |
| depth_imbalance_1pct | VALIDATION | 2099 | 0.057 | 0.044 | 0.017 | -0.47 |
| depth_imbalance_1pct | OOS | 1667 | 0.021 | 0.055 | -0.057 | -0.36 |
| depth_imbalance_change | IS | 7352 | 0.000 | 0.000 | 0.029 | -0.62 |
| depth_imbalance_change | VALIDATION | 2099 | -0.001 | -0.001 | 0.006 | -0.47 |
| depth_imbalance_change | OOS | 1667 | -0.008 | 0.000 | -0.079 | -0.36 |

## 6. Chronological stability

Blocks count how many of the four equal index quarters have at least 100 trades and absolute rho of at least 0.05 with the reference sign.

| Feature | Block 1 rho | Block 2 rho | Block 3 rho | Block 4 rho |
| --- | ---: | ---: | ---: | ---: |
| oi_change_1 | 0.030 | 0.007 | -0.008 | 0.000 |
| oi_change_3 | 0.012 | -0.001 | -0.012 | -0.020 |
| oi_change_12 | -0.016 | 0.002 | 0.019 | -0.009 |
| oi_percentile | -0.001 | -0.019 | -0.003 | 0.001 |
| funding_rate | -0.001 | 0.029 | -0.007 | 0.072 |
| funding_percentile | -0.006 | -0.006 | -0.007 | 0.053 |
| funding_change | -0.019 | -0.023 | -0.016 | -0.004 |
| taker_imbalance | -0.040 | -0.076 | -0.024 | 0.100 |
| taker_imbalance_change | -0.027 | -0.084 | -0.055 | 0.108 |
| taker_buy_ratio | -0.040 | -0.076 | -0.024 | 0.100 |
| normalized_basis | -0.021 | -0.002 | -0.022 | 0.050 |
| basis_change | -0.028 | -0.041 | -0.060 | 0.042 |
| basis_percentile | -0.052 | -0.047 | -0.041 | 0.019 |
| depth_imbalance_1pct | 0.028 | 0.004 | 0.006 | -0.044 |
| depth_imbalance_change | 0.035 | 0.034 | 0.025 | -0.088 |

## 7. Strategy-family stability

| Feature | Slice | n | Rho | Winner p50 | Loser p50 | Net |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| oi_change_1 | FROZEN_MEAN | 272 | -0.009 | 0.000 | 0.000 | -0.17 |
| oi_change_1 | FROZEN_TREND | 4633 | 0.063 | 0.000 | 0.000 | -0.32 |
| oi_change_1 | PA_CONTINUATION | 1125 | 0.022 | 0.000 | 0.000 | -0.54 |
| oi_change_1 | PA_FAILED | 1102 | -0.019 | 0.000 | 0.000 | -0.63 |
| oi_change_1 | PA_STRUCTURE | 2661 | -0.021 | 0.000 | 0.000 | -0.85 |
| oi_change_1 | PA_SWEEP | 1325 | 0.023 | 0.000 | 0.000 | -0.82 |
| oi_change_3 | FROZEN_MEAN | 272 | -0.189 | -0.001 | -0.001 | -0.17 |
| oi_change_3 | FROZEN_TREND | 4633 | 0.059 | 0.000 | 0.000 | -0.32 |
| oi_change_3 | PA_CONTINUATION | 1125 | 0.001 | 0.000 | 0.000 | -0.54 |
| oi_change_3 | PA_FAILED | 1102 | -0.058 | 0.000 | 0.000 | -0.63 |
| oi_change_3 | PA_STRUCTURE | 2661 | -0.040 | 0.000 | 0.000 | -0.85 |
| oi_change_3 | PA_SWEEP | 1325 | 0.052 | 0.000 | 0.000 | -0.82 |
| oi_change_12 | FROZEN_MEAN | 272 | -0.041 | 0.000 | -0.001 | -0.17 |
| oi_change_12 | FROZEN_TREND | 4633 | 0.049 | 0.000 | 0.000 | -0.32 |
| oi_change_12 | PA_CONTINUATION | 1125 | -0.031 | 0.001 | 0.000 | -0.54 |
| oi_change_12 | PA_FAILED | 1102 | -0.061 | 0.000 | 0.000 | -0.63 |
| oi_change_12 | PA_STRUCTURE | 2660 | -0.020 | 0.000 | 0.000 | -0.84 |
| oi_change_12 | PA_SWEEP | 1325 | 0.035 | 0.000 | 0.000 | -0.82 |
| oi_percentile | FROZEN_MEAN | 272 | -0.006 | 0.490 | 0.430 | -0.17 |
| oi_percentile | FROZEN_TREND | 4633 | 0.041 | 0.480 | 0.430 | -0.32 |
| oi_percentile | PA_CONTINUATION | 1122 | -0.024 | 0.510 | 0.470 | -0.54 |
| oi_percentile | PA_FAILED | 1098 | -0.025 | 0.490 | 0.540 | -0.63 |
| oi_percentile | PA_STRUCTURE | 2651 | -0.011 | 0.540 | 0.530 | -0.84 |
| oi_percentile | PA_SWEEP | 1320 | 0.019 | 0.580 | 0.520 | -0.80 |
| funding_rate | FROZEN_MEAN | 272 | -0.068 | 0.000 | 0.000 | -0.17 |
| funding_rate | FROZEN_TREND | 4639 | 0.056 | 0.000 | 0.000 | -0.32 |
| funding_rate | PA_CONTINUATION | 1125 | 0.047 | 0.000 | 0.000 | -0.54 |
| funding_rate | PA_FAILED | 1102 | 0.035 | 0.000 | 0.000 | -0.63 |
| funding_rate | PA_STRUCTURE | 2662 | -0.019 | 0.000 | 0.000 | -0.85 |
| funding_rate | PA_SWEEP | 1325 | 0.011 | 0.000 | 0.000 | -0.82 |
| funding_percentile | FROZEN_MEAN | 272 | -0.059 | 0.404 | 0.449 | -0.17 |
| funding_percentile | FROZEN_TREND | 4639 | 0.025 | 0.427 | 0.404 | -0.32 |
| funding_percentile | PA_CONTINUATION | 1111 | 0.044 | 0.371 | 0.337 | -0.60 |
| funding_percentile | PA_FAILED | 1102 | 0.066 | 0.416 | 0.416 | -0.63 |
| funding_percentile | PA_STRUCTURE | 2661 | -0.025 | 0.360 | 0.393 | -0.85 |
| funding_percentile | PA_SWEEP | 1325 | 0.049 | 0.382 | 0.404 | -0.82 |
| funding_change | FROZEN_MEAN | 272 | 0.022 | 0.000 | 0.000 | -0.17 |
| funding_change | FROZEN_TREND | 4639 | -0.002 | 0.000 | 0.000 | -0.32 |
| funding_change | PA_CONTINUATION | 1125 | -0.013 | 0.000 | 0.000 | -0.54 |
| funding_change | PA_FAILED | 1102 | 0.024 | 0.000 | 0.000 | -0.63 |
| funding_change | PA_STRUCTURE | 2662 | -0.065 | 0.000 | 0.000 | -0.85 |
| funding_change | PA_SWEEP | 1325 | -0.018 | 0.000 | 0.000 | -0.82 |
| taker_imbalance | FROZEN_MEAN | 272 | -0.001 | -0.022 | -0.011 | -0.17 |
| taker_imbalance | FROZEN_TREND | 4639 | 0.011 | -0.025 | -0.023 | -0.32 |
| taker_imbalance | PA_CONTINUATION | 1125 | -0.012 | -0.016 | -0.021 | -0.54 |
| taker_imbalance | PA_FAILED | 1102 | 0.010 | 0.054 | 0.041 | -0.63 |
| taker_imbalance | PA_STRUCTURE | 2662 | -0.054 | -0.025 | 0.005 | -0.85 |
| taker_imbalance | PA_SWEEP | 1325 | -0.011 | -0.047 | -0.015 | -0.82 |
| taker_imbalance_change | FROZEN_MEAN | 272 | 0.036 | 0.018 | 0.062 | -0.17 |
| taker_imbalance_change | FROZEN_TREND | 4639 | 0.010 | -0.007 | -0.003 | -0.32 |
| taker_imbalance_change | PA_CONTINUATION | 1125 | -0.014 | 0.005 | -0.014 | -0.54 |
| taker_imbalance_change | PA_FAILED | 1102 | -0.016 | 0.151 | 0.161 | -0.63 |
| taker_imbalance_change | PA_STRUCTURE | 2662 | -0.047 | -0.015 | 0.012 | -0.85 |
| taker_imbalance_change | PA_SWEEP | 1325 | -0.007 | 0.001 | 0.013 | -0.82 |
| taker_buy_ratio | FROZEN_MEAN | 272 | -0.001 | 0.489 | 0.495 | -0.17 |
| taker_buy_ratio | FROZEN_TREND | 4639 | 0.011 | 0.487 | 0.489 | -0.32 |
| taker_buy_ratio | PA_CONTINUATION | 1125 | -0.012 | 0.492 | 0.489 | -0.54 |
| taker_buy_ratio | PA_FAILED | 1102 | 0.010 | 0.527 | 0.521 | -0.63 |
| taker_buy_ratio | PA_STRUCTURE | 2662 | -0.054 | 0.488 | 0.502 | -0.85 |
| taker_buy_ratio | PA_SWEEP | 1325 | -0.011 | 0.477 | 0.492 | -0.82 |
| normalized_basis | FROZEN_MEAN | 272 | -0.031 | 0.000 | 0.000 | -0.17 |
| normalized_basis | FROZEN_TREND | 4639 | 0.032 | 0.000 | 0.000 | -0.32 |
| normalized_basis | PA_CONTINUATION | 1125 | -0.017 | 0.000 | 0.000 | -0.54 |
| normalized_basis | PA_FAILED | 1102 | 0.005 | 0.000 | 0.000 | -0.63 |
| normalized_basis | PA_STRUCTURE | 2662 | -0.020 | 0.000 | 0.000 | -0.85 |
| normalized_basis | PA_SWEEP | 1325 | -0.004 | 0.000 | 0.000 | -0.82 |
| basis_change | FROZEN_MEAN | 272 | -0.028 | 0.000 | 0.000 | -0.17 |
| basis_change | FROZEN_TREND | 4639 | -0.036 | 0.000 | 0.000 | -0.32 |
| basis_change | PA_CONTINUATION | 1125 | -0.040 | 0.000 | 0.000 | -0.54 |
| basis_change | PA_FAILED | 1102 | -0.010 | 0.000 | 0.000 | -0.63 |
| basis_change | PA_STRUCTURE | 2662 | -0.036 | 0.000 | 0.000 | -0.85 |
| basis_change | PA_SWEEP | 1325 | 0.015 | 0.000 | 0.000 | -0.82 |
| basis_percentile | FROZEN_MEAN | 272 | 0.087 | 0.590 | 0.540 | -0.17 |
| basis_percentile | FROZEN_TREND | 4639 | -0.037 | 0.480 | 0.530 | -0.32 |
| basis_percentile | PA_CONTINUATION | 1122 | -0.058 | 0.480 | 0.550 | -0.54 |
| basis_percentile | PA_FAILED | 1098 | -0.031 | 0.490 | 0.550 | -0.63 |
| basis_percentile | PA_STRUCTURE | 2652 | -0.046 | 0.480 | 0.550 | -0.84 |
| basis_percentile | PA_SWEEP | 1320 | -0.003 | 0.560 | 0.560 | -0.80 |
| depth_imbalance_1pct | FROZEN_MEAN | 272 | 0.053 | 0.024 | 0.022 | -0.17 |
| depth_imbalance_1pct | FROZEN_TREND | 4633 | -0.011 | 0.027 | 0.047 | -0.32 |
| depth_imbalance_1pct | PA_CONTINUATION | 1125 | 0.000 | 0.034 | 0.024 | -0.54 |
| depth_imbalance_1pct | PA_FAILED | 1102 | 0.046 | 0.038 | 0.037 | -0.63 |
| depth_imbalance_1pct | PA_STRUCTURE | 2661 | 0.044 | 0.026 | 0.021 | -0.85 |
| depth_imbalance_1pct | PA_SWEEP | 1325 | -0.016 | 0.039 | 0.033 | -0.82 |
| depth_imbalance_change | FROZEN_MEAN | 272 | 0.003 | -0.008 | 0.000 | -0.17 |
| depth_imbalance_change | FROZEN_TREND | 4633 | -0.012 | 0.000 | 0.000 | -0.32 |
| depth_imbalance_change | PA_CONTINUATION | 1125 | -0.024 | 0.000 | 0.000 | -0.54 |
| depth_imbalance_change | PA_FAILED | 1102 | 0.033 | -0.014 | -0.012 | -0.63 |
| depth_imbalance_change | PA_STRUCTURE | 2661 | 0.031 | 0.000 | 0.000 | -0.85 |
| depth_imbalance_change | PA_SWEEP | 1325 | -0.016 | 0.000 | 0.000 | -0.82 |

## 8. Symbol stability

| Feature | Slice | n | Rho | Winner p50 | Loser p50 | Net |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| oi_change_1 | BTCUSDT | 3177 | -0.010 | 0.000 | 0.000 | -0.52 |
| oi_change_1 | ETHUSDT | 4319 | 0.019 | 0.000 | 0.000 | -0.65 |
| oi_change_1 | BNBUSDT | 3622 | 0.015 | 0.000 | 0.000 | -0.48 |
| oi_change_3 | BTCUSDT | 3177 | -0.022 | 0.000 | 0.000 | -0.52 |
| oi_change_3 | ETHUSDT | 4319 | 0.010 | 0.000 | 0.000 | -0.65 |
| oi_change_3 | BNBUSDT | 3622 | -0.004 | 0.000 | 0.000 | -0.48 |
| oi_change_12 | BTCUSDT | 3177 | 0.007 | 0.000 | 0.000 | -0.52 |
| oi_change_12 | ETHUSDT | 4319 | 0.000 | 0.000 | 0.000 | -0.65 |
| oi_change_12 | BNBUSDT | 3621 | -0.026 | 0.000 | 0.000 | -0.48 |
| oi_percentile | BTCUSDT | 3169 | -0.019 | 0.520 | 0.480 | -0.50 |
| oi_percentile | ETHUSDT | 4313 | 0.011 | 0.510 | 0.460 | -0.65 |
| oi_percentile | BNBUSDT | 3614 | -0.024 | 0.490 | 0.480 | -0.47 |
| funding_rate | BTCUSDT | 3179 | 0.010 | 0.000 | 0.000 | -0.52 |
| funding_rate | ETHUSDT | 4322 | 0.014 | 0.000 | 0.000 | -0.65 |
| funding_rate | BNBUSDT | 3624 | 0.051 | 0.000 | 0.000 | -0.48 |
| funding_percentile | BTCUSDT | 3175 | 0.008 | 0.438 | 0.483 | -0.52 |
| funding_percentile | ETHUSDT | 4316 | 0.027 | 0.483 | 0.472 | -0.65 |
| funding_percentile | BNBUSDT | 3619 | 0.014 | 0.067 | 0.067 | -0.49 |
| funding_change | BTCUSDT | 3179 | -0.059 | 0.000 | 0.000 | -0.52 |
| funding_change | ETHUSDT | 4322 | -0.001 | 0.000 | 0.000 | -0.65 |
| funding_change | BNBUSDT | 3624 | -0.001 | 0.000 | 0.000 | -0.48 |
| taker_imbalance | BTCUSDT | 3179 | -0.010 | 0.005 | -0.013 | -0.52 |
| taker_imbalance | ETHUSDT | 4322 | -0.029 | -0.026 | 0.002 | -0.65 |
| taker_imbalance | BNBUSDT | 3624 | -0.018 | -0.023 | -0.013 | -0.48 |
| taker_imbalance_change | BTCUSDT | 3179 | -0.024 | 0.005 | 0.005 | -0.52 |
| taker_imbalance_change | ETHUSDT | 4322 | -0.034 | -0.003 | 0.012 | -0.65 |
| taker_imbalance_change | BNBUSDT | 3624 | -0.014 | 0.009 | 0.008 | -0.48 |
| taker_buy_ratio | BTCUSDT | 3179 | -0.010 | 0.503 | 0.494 | -0.52 |
| taker_buy_ratio | ETHUSDT | 4322 | -0.029 | 0.487 | 0.501 | -0.65 |
| taker_buy_ratio | BNBUSDT | 3624 | -0.018 | 0.489 | 0.494 | -0.48 |
| normalized_basis | BTCUSDT | 3179 | -0.081 | 0.000 | 0.000 | -0.52 |
| normalized_basis | ETHUSDT | 4322 | -0.012 | 0.000 | 0.000 | -0.65 |
| normalized_basis | BNBUSDT | 3624 | 0.059 | 0.000 | 0.000 | -0.48 |
| basis_change | BTCUSDT | 3179 | -0.058 | 0.000 | 0.000 | -0.52 |
| basis_change | ETHUSDT | 4322 | -0.003 | 0.000 | 0.000 | -0.65 |
| basis_change | BNBUSDT | 3624 | -0.032 | 0.000 | 0.000 | -0.48 |
| basis_percentile | BTCUSDT | 3171 | -0.076 | 0.500 | 0.570 | -0.50 |
| basis_percentile | ETHUSDT | 4316 | -0.009 | 0.540 | 0.560 | -0.65 |
| basis_percentile | BNBUSDT | 3616 | -0.027 | 0.470 | 0.480 | -0.47 |
| depth_imbalance_1pct | BTCUSDT | 3177 | -0.028 | 0.005 | 0.031 | -0.52 |
| depth_imbalance_1pct | ETHUSDT | 4319 | 0.064 | -0.007 | -0.012 | -0.65 |
| depth_imbalance_1pct | BNBUSDT | 3622 | -0.001 | 0.070 | 0.071 | -0.48 |
| depth_imbalance_change | BTCUSDT | 3177 | -0.018 | -0.001 | 0.004 | -0.52 |
| depth_imbalance_change | ETHUSDT | 4319 | 0.030 | 0.000 | 0.000 | -0.65 |
| depth_imbalance_change | BNBUSDT | 3622 | 0.013 | 0.000 | 0.000 | -0.48 |

## 9. Long and short stability

| Feature | Slice | n | Rho | Winner p50 | Loser p50 | Net |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| oi_change_1 | LONG | 5808 | 0.022 | 0.000 | 0.000 | -0.69 |
| oi_change_1 | SHORT | 5310 | 0.002 | 0.000 | 0.000 | -0.40 |
| oi_change_3 | LONG | 5808 | 0.025 | 0.000 | 0.000 | -0.69 |
| oi_change_3 | SHORT | 5310 | -0.030 | 0.000 | 0.000 | -0.40 |
| oi_change_12 | LONG | 5808 | 0.004 | 0.000 | 0.000 | -0.69 |
| oi_change_12 | SHORT | 5309 | -0.014 | 0.000 | 0.000 | -0.40 |
| oi_percentile | LONG | 5795 | 0.009 | 0.550 | 0.500 | -0.68 |
| oi_percentile | SHORT | 5301 | -0.026 | 0.460 | 0.440 | -0.41 |
| funding_rate | LONG | 5814 | 0.041 | 0.000 | 0.000 | -0.69 |
| funding_rate | SHORT | 5311 | 0.006 | 0.000 | 0.000 | -0.40 |
| funding_percentile | LONG | 5807 | 0.047 | 0.427 | 0.404 | -0.70 |
| funding_percentile | SHORT | 5303 | -0.015 | 0.371 | 0.382 | -0.41 |
| funding_change | LONG | 5814 | 0.013 | 0.000 | 0.000 | -0.69 |
| funding_change | SHORT | 5311 | -0.053 | 0.000 | 0.000 | -0.40 |
| taker_imbalance | LONG | 5814 | 0.054 | 0.114 | 0.126 | -0.69 |
| taker_imbalance | SHORT | 5311 | -0.050 | -0.135 | -0.142 | -0.40 |
| taker_imbalance_change | LONG | 5814 | -0.024 | 0.113 | 0.111 | -0.69 |
| taker_imbalance_change | SHORT | 5311 | 0.003 | -0.107 | -0.107 | -0.40 |
| taker_buy_ratio | LONG | 5814 | 0.054 | 0.557 | 0.563 | -0.69 |
| taker_buy_ratio | SHORT | 5311 | -0.050 | 0.433 | 0.429 | -0.40 |
| normalized_basis | LONG | 5814 | 0.030 | 0.000 | 0.000 | -0.69 |
| normalized_basis | SHORT | 5311 | -0.015 | 0.000 | 0.000 | -0.40 |
| basis_change | LONG | 5814 | -0.037 | 0.000 | 0.000 | -0.69 |
| basis_change | SHORT | 5311 | -0.002 | 0.000 | 0.000 | -0.40 |
| basis_percentile | LONG | 5801 | -0.019 | 0.610 | 0.610 | -0.68 |
| basis_percentile | SHORT | 5302 | -0.038 | 0.410 | 0.450 | -0.41 |
| depth_imbalance_1pct | LONG | 5808 | -0.059 | -0.024 | -0.025 | -0.69 |
| depth_imbalance_1pct | SHORT | 5310 | 0.083 | 0.095 | 0.105 | -0.40 |
| depth_imbalance_change | LONG | 5808 | -0.034 | -0.030 | -0.023 | -0.69 |
| depth_imbalance_change | SHORT | 5310 | 0.031 | 0.024 | 0.028 | -0.40 |

## 10. BTC context

No BTC-context interaction was built. The BTCUSDT rows in the symbol table are the BTC book itself, not a lead-lag feature.

## 11. Simultaneous-signal analysis

Simultaneous signals were not recomputed. This phase does not combine microstructure with the earlier signal-count features.

Timeframe slices:

| Feature | Slice | n | Rho | Winner p50 | Loser p50 | Net |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| oi_change_1 | 5m | 4421 | -0.011 | 0.000 | 0.000 | -0.75 |
| oi_change_1 | 15m | 6238 | 0.035 | 0.000 | 0.000 | -0.41 |
| oi_change_1 | 1h | 459 | 0.082 | 0.001 | 0.000 | -0.73 |
| oi_change_3 | 5m | 4421 | -0.031 | 0.000 | 0.000 | -0.75 |
| oi_change_3 | 15m | 6238 | 0.035 | 0.000 | 0.000 | -0.41 |
| oi_change_3 | 1h | 459 | 0.038 | 0.004 | 0.001 | -0.73 |
| oi_change_12 | 5m | 4420 | -0.020 | 0.000 | 0.000 | -0.74 |
| oi_change_12 | 15m | 6238 | 0.021 | 0.000 | 0.000 | -0.41 |
| oi_change_12 | 1h | 459 | 0.021 | 0.005 | -0.001 | -0.73 |
| oi_percentile | 5m | 4405 | -0.010 | 0.540 | 0.540 | -0.74 |
| oi_percentile | 15m | 6232 | 0.022 | 0.490 | 0.440 | -0.40 |
| oi_percentile | 1h | 459 | -0.015 | 0.540 | 0.510 | -0.73 |
| funding_rate | 5m | 4422 | -0.018 | 0.000 | 0.000 | -0.75 |
| funding_rate | 15m | 6244 | 0.050 | 0.000 | 0.000 | -0.41 |
| funding_rate | 1h | 459 | 0.068 | 0.000 | 0.000 | -0.73 |
| funding_percentile | 5m | 4422 | 0.000 | 0.360 | 0.393 | -0.75 |
| funding_percentile | 15m | 6244 | 0.030 | 0.427 | 0.404 | -0.41 |
| funding_percentile | 1h | 444 | 0.025 | 0.404 | 0.337 | -0.88 |
| funding_change | 5m | 4422 | -0.042 | 0.000 | 0.000 | -0.75 |
| funding_change | 15m | 6244 | -0.007 | 0.000 | 0.000 | -0.41 |
| funding_change | 1h | 459 | 0.006 | 0.000 | 0.000 | -0.73 |
| taker_imbalance | 5m | 4422 | -0.032 | -0.024 | 0.002 | -0.75 |
| taker_imbalance | 15m | 6244 | -0.005 | -0.017 | -0.011 | -0.41 |
| taker_imbalance | 1h | 459 | -0.006 | 0.010 | -0.018 | -0.73 |
| taker_imbalance_change | 5m | 4422 | -0.030 | -0.010 | 0.005 | -0.75 |
| taker_imbalance_change | 15m | 6244 | -0.024 | 0.008 | 0.013 | -0.41 |
| taker_imbalance_change | 1h | 459 | 0.027 | 0.022 | -0.010 | -0.73 |
| taker_buy_ratio | 5m | 4422 | -0.032 | 0.488 | 0.501 | -0.75 |
| taker_buy_ratio | 15m | 6244 | -0.005 | 0.491 | 0.494 | -0.41 |
| taker_buy_ratio | 1h | 459 | -0.006 | 0.505 | 0.491 | -0.73 |
| normalized_basis | 5m | 4422 | -0.040 | 0.000 | 0.000 | -0.75 |
| normalized_basis | 15m | 6244 | 0.032 | 0.000 | 0.000 | -0.41 |
| normalized_basis | 1h | 459 | 0.020 | 0.000 | 0.000 | -0.73 |
| basis_change | 5m | 4422 | -0.032 | 0.000 | 0.000 | -0.75 |
| basis_change | 15m | 6244 | -0.030 | 0.000 | 0.000 | -0.41 |
| basis_change | 1h | 459 | -0.014 | 0.000 | 0.000 | -0.73 |
| basis_percentile | 5m | 4406 | -0.058 | 0.480 | 0.550 | -0.74 |
| basis_percentile | 15m | 6238 | -0.024 | 0.530 | 0.530 | -0.40 |
| basis_percentile | 1h | 459 | -0.041 | 0.480 | 0.500 | -0.73 |
| depth_imbalance_1pct | 5m | 4421 | 0.034 | 0.027 | 0.020 | -0.75 |
| depth_imbalance_1pct | 15m | 6238 | 0.000 | 0.034 | 0.043 | -0.41 |
| depth_imbalance_1pct | 1h | 459 | -0.043 | 0.021 | 0.041 | -0.73 |
| depth_imbalance_change | 5m | 4421 | 0.031 | 0.000 | 0.000 | -0.75 |
| depth_imbalance_change | 15m | 6238 | -0.001 | 0.000 | 0.000 | -0.41 |
| depth_imbalance_change | 1h | 459 | -0.038 | -0.002 | 0.000 | -0.73 |

## 12. Strongest recurring relationships

- taker_imbalance: OOS_ONLY. IS rho -0.046, validation rho -0.035, OOS rho 0.122.
- taker_imbalance_change: OOS_ONLY. IS rho -0.047, validation rho -0.049, OOS rho 0.133.
- taker_buy_ratio: OOS_ONLY. IS rho -0.046, validation rho -0.035, OOS rho 0.122.
- depth_imbalance_1pct: OOS_ONLY. IS rho 0.022, validation rho 0.017, OOS rho -0.057.
- depth_imbalance_change: OOS_ONLY. IS rho 0.029, validation rho 0.006, OOS rho -0.079.

## 13. Unstable relationships

taker_imbalance (OOS_ONLY), taker_imbalance_change (OOS_ONLY), taker_buy_ratio (OOS_ONLY), depth_imbalance_1pct (OOS_ONLY), depth_imbalance_change (OOS_ONLY).

## 14. Data limitations

- Liquidations BTCUSDT ETHUSDT BNBUSDT n/a: DATA_UNAVAILABLE. liquidation 2023-06-01 status 404. liquidation 2024-03-15 status 404. liquidation 2024-03-31 status 404. liquidation 2025-01-01 status 404.
- Spread BTCUSDT ETHUSDT BNBUSDT n/a: DATA_UNAVAILABLE. The depth archive has percentage notional, not best bid/ask.
- Open interest REST BTCUSDT 5m: PARTIAL. On-disk span 29.0 days. This is not the Vision series and was not used for features.
- Open interest REST BTCUSDT 15m: PARTIAL. On-disk span 29.0 days. This is not the Vision series and was not used for features.
- Open interest REST BTCUSDT 1h: PARTIAL. On-disk span 29.0 days. This is not the Vision series and was not used for features.
- Open interest REST ETHUSDT 5m: PARTIAL. On-disk span 29.0 days. This is not the Vision series and was not used for features.
- Open interest REST ETHUSDT 15m: PARTIAL. On-disk span 29.0 days. This is not the Vision series and was not used for features.
- Open interest REST ETHUSDT 1h: PARTIAL. On-disk span 29.0 days. This is not the Vision series and was not used for features.
- Open interest REST BNBUSDT 5m: PARTIAL. On-disk span 29.0 days. This is not the Vision series and was not used for features.
- Open interest REST BNBUSDT 15m: PARTIAL. On-disk span 29.0 days. This is not the Vision series and was not used for features.
- Open interest REST BNBUSDT 1h: PARTIAL. On-disk span 29.0 days. This is not the Vision series and was not used for features.
- Price versus open-interest divergence and open-interest versus funding were not constructed.
- `sum_taker_long_short_vol_ratio` is present in the Vision metrics files and was not scored.
- Features were not combined with EMA slope or VWAP slope.

## 15. Final conclusion

- REPEATABLE: none.
- FAMILY_SPECIFIC: none.
- SYMBOL_SPECIFIC: none.
- OOS_ONLY: taker_imbalance, taker_imbalance_change, taker_buy_ratio, depth_imbalance_1pct, depth_imbalance_change.
- UNSTABLE: none.
- NO_EVIDENCE: oi_change_1, oi_change_3, oi_change_12, oi_percentile, funding_rate, funding_percentile, funding_change, normalized_basis, basis_change, basis_percentile.

## A. Data available

OHLCV, Funding, Open interest, Depth, Basis, Taker flow.

## B. Data partially available

Open interest REST.

## C. Data unavailable

Liquidations, Spread.

## D. Features with repeatable relationships

None. No scored feature met the pre-registered REPEATABLE bar.

## E. Features without evidence

oi_change_1, oi_change_3, oi_change_12, oi_percentile, funding_rate, funding_percentile, funding_change, normalized_basis, basis_change, basis_percentile.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.
