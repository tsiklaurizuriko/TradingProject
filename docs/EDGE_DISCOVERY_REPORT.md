# Edge discovery

Definition SHA-256 `3cd11f98be4063e92601fff0f9113b189d7e9484eb89231e5b35330fa962a2ea`.

Every relationship below is EXPLORATORY. None is VALIDATED_FOR_PAPER. No strategy was created or changed.

## 1. Trades analyzed

Reconstructed cost-inclusive trades: 11125. Feature-ready: 11115.
Phase 4 baseline trades read from the existing file, not rerun: 44876.

## 2. Families

Frozen Five templates at 15m, unmodified. Final Five primary ids at their pre-registered timeframe. Phase 8 baselines for sweep, pullback, W/M, compression continuation, MTF, and failed breakout at 5m. Near-miss is the same Phase 8 book and was not counted twice. Funding baselines are summarized from the existing Phase 4 trade file.

Independent groups used by the gate: FROZEN_TREND, FROZEN_MEAN, PA_SWEEP, PA_FAILED, PA_CONTINUATION, PA_STRUCTURE. Sweep and failed-breakout variants inside one group are related, not independent.

## 3. Features

Taken on the completed signal bar, before the next-bar fill: ATR percentile, ADX, bar range versus ATR, eight-bar compression then release, 1h structure bias, BTC 1h trend and ATR percentile, same-clock BTC relative return, UTC session, UTC weekday. No future bar enters a feature.

## 4. Regimes

Orthogonal bins, not a crossed grid: volatility low/normal/high, ADX range/trend, bar expansion/contraction, compression-release, hourly alignment, BTC trend alignment, BTC volatility. 28 conditions were scored, including 7 weekdays. They were not crossed with each other.

Frozen before aggregation. A condition is INTERESTING only when the in-set has n>=80, profit factor>1, net>0, the out-set has n>=80 and profit factor<1, at least two independent family groups each have in-set n>=25 and net>0, at least two of BTC ETH BNB each have in-set n>=20 and net>0, at least three chronological blocks each have in-set n>=15 and net>0, and both IS (n>=30) and VALIDATION (n>=20) have in-set net>0. OOS is not an input to this label. INTERESTING is still EXPLORATORY, never VALIDATED_FOR_PAPER. A cell that misses the rule is NOISE or INSUFFICIENT_EVIDENCE.

| Condition | Label | In n | In PF | In net | In exp | Out n | Out PF | Out net | Groups | Symbols | Blocks | IS net | VAL net | OOS net |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: | ---: | ---: | ---: |
| VOL_LOW | NOISE | 3993 | 0.783 | -2161.13 | -0.54 | 7122 | 0.791 | -3993.71 | 0 |  | 0 | -1535.89 | -296.64 | -328.60 |
| VOL_NORMAL | NOISE | 2669 | 0.812 | -1301.99 | -0.49 | 8446 | 0.780 | -4852.85 | 1 |  | 0 | -989.83 | -228.69 | -83.47 |
| VOL_HIGH | NOISE | 4453 | 0.779 | -2691.72 | -0.60 | 6662 | 0.795 | -3463.12 | 0 |  | 0 | -2037.62 | -452.12 | -201.98 |
| ADX_RANGE | NOISE | 3516 | 0.808 | -1433.83 | -0.41 | 7599 | 0.781 | -4721.01 | 0 |  | 0 | -845.30 | -387.11 | -201.42 |
| ADX_TREND | NOISE | 5615 | 0.756 | -4099.92 | -0.73 | 5500 | 0.832 | -2054.92 | 0 |  | 0 | -3372.03 | -488.92 | -238.97 |
| BAR_EXPANSION | NOISE | 8183 | 0.785 | -4385.78 | -0.54 | 2932 | 0.796 | -1769.06 | 0 |  | 0 | -3280.13 | -696.78 | -408.87 |
| BAR_CONTRACTION | NOISE | 219 | 0.764 | -173.48 | -0.79 | 10896 | 0.789 | -5981.36 | 0 |  | 0 | -82.02 | -29.61 | -61.86 |
| COMPRESSION_RELEASE | NOISE | 955 | 0.725 | -880.38 | -0.92 | 10160 | 0.796 | -5274.46 | 0 |  | 0 | -555.86 | -189.21 | -135.30 |
| HTF_ALIGNED | NOISE | 6871 | 0.833 | -2992.48 | -0.44 | 4244 | 0.716 | -3162.36 | 0 |  | 0 | -2718.19 | -173.11 | -101.18 |
| HTF_OPPOSED | NOISE | 4244 | 0.716 | -3162.36 | -0.75 | 6871 | 0.833 | -2992.48 | 0 |  | 0 | -1845.15 | -804.34 | -512.87 |
| BTC_TREND_ALIGNED | NOISE | 3838 | 0.882 | -1165.45 | -0.30 | 2488 | 0.702 | -2233.36 | 1 |  | 0 | -1316.29 | 274.84 | -123.99 |
| BTC_TREND_OPPOSED | NOISE | 2488 | 0.702 | -2233.36 | -0.90 | 3838 | 0.882 | -1165.45 | 1 |  | 0 | -1488.44 | -730.47 | -14.45 |
| BTC_VOL_HIGH | NOISE | 4985 | 0.782 | -3177.70 | -0.64 | 6130 | 0.794 | -2977.14 | 0 |  | 0 | -2639.33 | -255.60 | -282.77 |
| BTC_VOL_LOW | NOISE | 3705 | 0.845 | -1292.59 | -0.35 | 7410 | 0.765 | -4862.25 | 1 |  | 0 | -800.82 | -375.58 | -116.20 |
| LONG | NOISE | 5814 | 0.743 | -4039.90 | -0.69 | 5311 | 0.839 | -2141.11 | 0 |  | 1 | -3501.27 | -933.50 | 394.88 |
| SHORT | NOISE | 5311 | 0.839 | -2141.11 | -0.40 | 5814 | 0.743 | -4039.90 | 0 |  | 0 | -1088.24 | -43.94 | -1008.93 |
| ALT_STRONGER | NOISE | 1068 | 0.703 | -1009.04 | -0.94 | 6871 | 0.801 | -3523.28 | 0 |  | 0 | -838.27 | -101.17 | -69.60 |
| ALT_WEAKER | NOISE | 1090 | 0.753 | -853.31 | -0.78 | 6849 | 0.792 | -3679.02 | 0 |  | 0 | -785.69 | -68.02 | 0.40 |
| SESSION_ASIA | NOISE | 3133 | 0.780 | -1691.89 | -0.54 | 7982 | 0.791 | -4462.95 | 0 |  | 0 | -1258.48 | -180.13 | -253.27 |
| SESSION_EUROPE | NOISE | 4087 | 0.865 | -1354.13 | -0.33 | 7028 | 0.748 | -4800.71 | 0 |  | 0 | -801.85 | -466.34 | -85.94 |
| SESSION_US | NOISE | 3895 | 0.726 | -3108.82 | -0.80 | 7220 | 0.828 | -3046.02 | 0 |  | 0 | -2503.00 | -330.98 | -274.84 |
| DOW_SUNDAY | NOISE | 1295 | 0.742 | -830.29 | -0.64 | 9820 | 0.794 | -5324.55 | 2 |  | 0 | -681.16 | -114.40 | -34.73 |
| DOW_MONDAY | NOISE | 1710 | 0.713 | -1336.37 | -0.78 | 9405 | 0.802 | -4818.47 | 0 |  | 0 | -996.13 | -235.44 | -104.80 |
| DOW_TUESDAY | NOISE | 1643 | 0.829 | -712.81 | -0.43 | 9472 | 0.781 | -5442.03 | 1 |  | 0 | -422.38 | -60.00 | -230.43 |
| DOW_WEDNESDAY | NOISE | 1759 | 0.692 | -1479.14 | -0.84 | 9356 | 0.807 | -4675.70 | 1 |  | 0 | -978.87 | -451.76 | -48.52 |
| DOW_THURSDAY | NOISE | 1781 | 0.878 | -560.73 | -0.31 | 9334 | 0.771 | -5594.11 | 1 |  | 1 | -506.69 | 29.61 | -83.66 |
| DOW_FRIDAY | NOISE | 1785 | 0.843 | -747.65 | -0.42 | 9330 | 0.777 | -5407.19 | 0 |  | 0 | -736.45 | -39.75 | 28.55 |
| DOW_SATURDAY | NOISE | 1142 | 0.828 | -487.85 | -0.43 | 9973 | 0.784 | -5666.99 | 0 |  | 1 | -241.67 | -105.71 | -140.47 |

## 5. Time

Blocks are equal index quarters of each symbol series. IS, validation, and OOS use the existing chronological split. A late OOS profit that is absent from IS and validation does not pass the gate.

## 6. Symbols

BTC, ETH, and BNB only. BNB was not chosen for this phase. The previous larger universe was not reconstructed. A condition needs two of these three coins.
- BTCUSDT: n=3179 PF=0.793 net=-1639.38
- ETHUSDT: n=4322 PF=0.763 net=-2798.02
- BNBUSDT: n=3624 PF=0.813 net=-1743.61

## 7. Long and short

- LONG: n=5814 PF=0.743 net=-4039.90 expectancy=-0.69
- SHORT: n=5311 PF=0.839 net=-2141.11 expectancy=-0.40

## 8. FF-SWEEP-HOURLY|15m

All blocks: n=416 PF=0.786 net=-334.64. OOS: n=66 PF=1.638 net=104.51.

| Slice | OOS n | OOS PF | OOS net | Winner share | Loser share |
| --- | ---: | ---: | ---: | ---: | ---: |
| LONG | 66 | 1.638 | 104.51 | 33/33 | 33/33 |
| SHORT | 0 | n/a | 0.00 | 0/33 | 0/33 |
| VOL_HIGH | 21 | 1.454 | 25.86 | 10/33 | 11/33 |
| VOL_LOW | 25 | 1.845 | 49.75 | 13/33 | 12/33 |
| ADX_TREND | 41 | 1.817 | 79.02 | 22/33 | 19/33 |
| ADX_RANGE | 18 | 1.814 | 34.03 | 9/33 | 9/33 |
| EXPANSION | 32 | 1.624 | 48.28 | 16/33 | 16/33 |
| HTF_ALIGNED | 66 | 1.638 | 104.51 | 33/33 | 33/33 |
| BTC_ALIGNED | 27 | 1.318 | 22.41 | 12/33 | 15/33 |
| BTCUSDT | 17 | 0.956 | -2.35 |  |  |
| ETHUSDT | 30 | 1.945 | 63.78 |  |  |
| BNBUSDT | 19 | 1.992 | 43.08 |  |  |
- Block 1: n=125 PF=0.734 net=-140.34
- Block 2: n=111 PF=0.518 net=-233.20
- Block 3: n=87 PF=0.816 net=-55.65
- Block 4: n=93 PF=1.372 net=94.55

## 9. FF-FAILED-DOWN|15m

All blocks: n=275 PF=0.769 net=-243.76. OOS: n=48 PF=1.362 net=49.15.

| Slice | OOS n | OOS PF | OOS net | Winner share | Loser share |
| --- | ---: | ---: | ---: | ---: | ---: |
| LONG | 48 | 1.362 | 49.15 | 22/22 | 26/26 |
| SHORT | 0 | n/a | 0.00 | 0/22 | 0/26 |
| VOL_HIGH | 20 | 2.209 | 56.81 | 12/22 | 8/26 |
| VOL_LOW | 14 | 0.820 | -8.10 | 5/22 | 9/26 |
| ADX_TREND | 19 | 1.687 | 32.04 | 10/22 | 9/26 |
| ADX_RANGE | 19 | 0.998 | -0.13 | 7/22 | 12/26 |
| EXPANSION | 30 | 2.468 | 88.63 | 18/22 | 12/26 |
| HTF_ALIGNED | 48 | 1.362 | 49.15 | 22/22 | 26/26 |
| BTC_ALIGNED | 17 | 0.856 | -7.51 | 6/22 | 11/26 |
| BTCUSDT | 12 | 1.415 | 14.09 |  |  |
| ETHUSDT | 21 | 1.018 | 1.25 |  |  |
| BNBUSDT | 15 | 2.014 | 33.81 |  |  |
- Block 1: n=80 PF=0.521 net=-176.73
- Block 2: n=71 PF=0.514 net=-151.36
- Block 3: n=55 PF=1.096 net=16.92
- Block 4: n=69 PF=1.342 net=67.41

## 10. Phase 8

- CPA-SWEEP: all n=909 PF=0.761 net=-756.28; OOS n=100 PF=0.691 net=-95.84
- CPA-PULLBACK: all n=810 PF=0.811 net=-543.61; OOS n=103 PF=0.809 net=-60.46
- CPA-WM: all n=849 PF=0.738 net=-778.51; OOS n=96 PF=0.602 net=-118.47
- CPA-COMPRESSION: all n=78 PF=1.173 net=46.43; OOS n=15 PF=1.018 net=0.92
- CPA-MTF: all n=949 PF=0.751 net=-817.89; OOS n=111 PF=0.716 net=-92.21
- CPA-FAILED_BREAKOUT: all n=827 PF=0.849 net=-451.62; OOS n=95 PF=0.964 net=-10.54

Phase 4 existing labels, baseline continuation only, BASE cost. This taxonomy is the old mutually exclusive regime tag. It is not crossed into the bins above.

| Cell | n | PF | Net |
| --- | ---: | ---: | ---: |
| OOS|BEAR|Short | 296 | 0.757 | -6133.27 |
| OOS|BULL|Long | 413 | 0.831 | -5544.65 |
| OOS|HIGH_VOL|Long | 11 | 0.234 | -2301.53 |
| OOS|HIGH_VOL|Short | 1 | 0.000 | -175.45 |
| OOS|LOW_VOL|Long | 2101 | 0.799 | -30046.21 |
| OOS|LOW_VOL|Short | 2111 | 0.843 | -23417.51 |
| OOS|RANGE|Long | 284 | 0.621 | -8599.52 |
| OOS|RANGE|Short | 361 | 0.598 | -11772.53 |
| OOS|STRONG_BEAR|Short | 8 | 0.833 | -127.87 |
| OOS|STRONG_BULL|Long | 40 | 0.654 | -1710.38 |
| OOS|TRANSITION|Long | 277 | 0.600 | -9226.42 |
| OOS|TRANSITION|Short | 307 | 0.629 | -9552.44 |

## 11. Cross-strategy

A shared condition counts only when two independent groups both have positive in-set net with at least 25 trades. Related price-action variants inside one group do not qualify as two families.

## 12. Edge clusters

No condition separated winners from losers under the pre-registered rule. There is no edge cluster to name.

## 13. Evidence for each cluster

Listed under each cluster. Where no cluster exists, there is no supporting cell.

## 14. Evidence against each cluster

Listed under each cluster. The out-set, the chronological split, and the group count are the checks.

## 15. Multiple testing

28 pre-registered conditions were scored on one reconstructed sample. Weekdays and sessions are included in that count. Conditions were not crossed, and no threshold was moved after the results. A green cell in this list is still exploratory.

## 16. Data limits

1h history is about two years. 15m is about one year. 5m is about one year. Funding cashflow is not inside the Frozen, Phase 8, or Final Five replays. Phase 4 trades do include recorded funding. Open interest, liquidations, and the order book were not used. ATR percentile is a 0–1 fraction. The larger symbol universe was not rebuilt. The unmodified `rsi_pullback` template produced 8 trades on 15m across the three coins, so this pass does not recreate the older RSI research book trade for trade. MACD and Donchian contribute the largest share of the 11,125 trades.

## Answer

NO — no repeatable condition found

LIVE = OFF. PAPER = OFF. No production, risk, execution, or enablement change.
