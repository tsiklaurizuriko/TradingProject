# Model B Wave-3 signal research report

Stage 1 only: predictive power of **relative / residual / volume-shock / taker / funding** signals.
No Isolated strategy was promoted. Frozen five, FrozenRisk, LOW book numbers, and LIVE were not changed.
Parameters were pre-registered. OOS was not used to mutate hypotheses.

## 1. Data availability audit

| Dataset | Status |
|---|---|
| BTC_OHLCV | AVAILABLE (BTCUSDT in panel) |
| FUNDING | AVAILABLE settled rates, min n=2194, span 731d. Predicted next rate not used. |
| INDEX | AVAILABLE on disk for prior 10-coin ingest; not required for XS OHLCV signals |
| LIQUIDATION | DATA_UNAVAILABLE |
| MARK | AVAILABLE on disk for prior 10-coin ingest; not required for XS OHLCV signals |
| OHLCV | AVAILABLE (research kline cache /fapi/v1/klines) |
| OI | DATA_UNAVAILABLE for 2-year window (public hist ~30d) |
| PREDICTED_FUNDING | DATA_UNAVAILABLE (premiumIndex snapshot is not historical) |
| SYNC_OHLCV | AVAILABLE (inner-join panel on OpenTime) |
| TAKER | DATA_UNAVAILABLE for the 2-year window (TakerBuyVolume>0 on ~16 bars/coin in this cache; older klines stored 0. Not backfilled. Not treated as imbalance 0.) |
| VOLUME | AVAILABLE |
| XS_RANKS | DERIVED at t from the aligned panel; not a stored universe snapshot |

Cross-sectional ranks are **derived** from the aligned 10-coin 1h panel. They were not fabricated as a stored universe snapshot. A bar is ranked only against coins present at the same OpenTime.

## 2. Signal hypotheses

| Id | Mechanism | Direction | Data | Class |
|---|---|---|---|---|
| H1_REL_1H_CONT | 1h return minus BTC 1h return; stronger relative coins continue. | continuation | OHLCV+BTC | **REJECTED** |
| H2_REL_4H_CONT | 4h return minus BTC; relative strength continuation. | continuation | OHLCV+BTC | **REJECTED** |
| H3_REL_12H_CONT | 12h return minus BTC; relative strength continuation. | continuation | OHLCV+BTC | **REJECTED** |
| H4_REL_24H_CONT | 24h return minus BTC; relative strength continuation. | continuation | OHLCV+BTC | **REJECTED** |
| H5_REL_24H_REV | 24h relative strength mean-reverts over the next day. | reversal | OHLCV+BTC | **REJECTED** |
| H6_REL_24H_VOLADJ_CONT | 24h relative return / ATR%. Vol-adjusted residual momentum. | continuation | OHLCV+BTC | **REJECTED** |
| H7_RESID_24H_CONT | 24h return minus beta*BTC, beta from past 168 hourly returns. | continuation | OHLCV+BTC | **REJECTED** |
| H8_RESID_24H_REV | Residual 24h mean-reverts. | reversal | OHLCV+BTC | **REJECTED** |
| H9_VOL_SHOCK_XS | Volume / SMA20 ranked cross-sectionally; high shock continues with the 24h relative sign. | continuation | OHLCV volume | **REJECTED** |
| H10_TAKER_IMB | Taker buy imbalance (2*buy/vol-1) continuation. | continuation | TakerBuyVolume | **REJECTED** |

**NO ROBUST ALPHA FOUND**

No pre-registered signal cleared IS predictive-power gates and validation confirmation with a cost-aware execution spread. Stage 2 trading simulation and the 1,584-book run were not launched.

## 3. Forward-return analysis (close-to-close CS IC and tercile spread)

MeanCsIc = average cross-sectional Spearman(signal, fwd) across timestamps. Spread = top tercile mean fwd − bottom tercile mean fwd. LONG mean = top tercile. SHORT mean = −bottom tercile (shorting the weak).

| Hypothesis | Phase | H | n | CS IC | TS IC | LONG mean | SHORT mean | Spread | Exec spread |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| H1_REL_1H_CONT | IS | 4 | 10327 | -0.0168 | -0.0079 | 0.02% | -0.03% | -0.01% | -0.01% |
| H1_REL_1H_CONT | IS | 8 | 10327 | -0.0044 | -0.0015 | 0.05% | -0.06% | -0.01% | -0.01% |
| H1_REL_1H_CONT | IS | 24 | 10327 | -0.0092 | -0.0099 | 0.15% | -0.14% | 0.00% | 0.00% |
| H1_REL_1H_CONT | OOS | 4 | 3505 | -0.0298 | -0.0080 | 0.01% | -0.01% | -0.00% | -0.00% |
| H1_REL_1H_CONT | OOS | 8 | 3501 | -0.0214 | -0.0127 | 0.03% | -0.03% | 0.00% | 0.00% |
| H1_REL_1H_CONT | OOS | 24 | 3485 | 0.0063 | -0.0062 | 0.10% | -0.03% | 0.07% | 0.07% |
| H1_REL_1H_CONT | VALIDATION | 4 | 3509 | -0.0308 | 0.0003 | -0.04% | 0.03% | -0.01% | -0.01% |
| H1_REL_1H_CONT | VALIDATION | 8 | 3509 | -0.0288 | 0.0030 | -0.08% | 0.06% | -0.01% | -0.01% |
| H1_REL_1H_CONT | VALIDATION | 24 | 3509 | -0.0213 | -0.0140 | -0.20% | 0.16% | -0.04% | -0.04% |
| H10_TAKER_IMB | IS | 4 | 0 | n/a | n/a | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | IS | 8 | 0 | n/a | n/a | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | IS | 24 | 0 | n/a | n/a | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | OOS | 4 | 0 | n/a | n/a | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | OOS | 8 | 0 | n/a | n/a | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | OOS | 24 | 0 | n/a | n/a | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | VALIDATION | 4 | 0 | n/a | n/a | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | VALIDATION | 8 | 0 | n/a | n/a | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | VALIDATION | 24 | 0 | n/a | n/a | n/a | n/a | n/a | n/a |
| H2_REL_4H_CONT | IS | 4 | 10327 | -0.0115 | -0.0087 | 0.03% | -0.03% | 0.01% | 0.01% |
| H2_REL_4H_CONT | IS | 8 | 10327 | -0.0046 | -0.0031 | 0.07% | -0.04% | 0.03% | 0.03% |
| H2_REL_4H_CONT | IS | 24 | 10327 | -0.0112 | -0.0107 | 0.16% | -0.10% | 0.06% | 0.06% |
| H2_REL_4H_CONT | OOS | 4 | 3505 | -0.0412 | -0.0291 | 0.01% | -0.01% | 0.00% | 0.00% |
| H2_REL_4H_CONT | OOS | 8 | 3501 | -0.0259 | -0.0212 | 0.03% | -0.02% | 0.01% | 0.01% |
| H2_REL_4H_CONT | OOS | 24 | 3485 | 0.0093 | -0.0164 | 0.11% | -0.00% | 0.11% | 0.11% |
| H2_REL_4H_CONT | VALIDATION | 4 | 3509 | -0.0515 | 0.0113 | -0.04% | 0.03% | -0.02% | -0.02% |
| H2_REL_4H_CONT | VALIDATION | 8 | 3509 | -0.0402 | -0.0032 | -0.07% | 0.06% | -0.01% | -0.01% |
| H2_REL_4H_CONT | VALIDATION | 24 | 3509 | -0.0426 | -0.0140 | -0.21% | 0.13% | -0.07% | -0.07% |
| H3_REL_12H_CONT | IS | 4 | 10327 | -0.0175 | -0.0154 | 0.04% | -0.02% | 0.02% | 0.02% |
| H3_REL_12H_CONT | IS | 8 | 10327 | -0.0205 | -0.0129 | 0.07% | -0.02% | 0.05% | 0.05% |
| H3_REL_12H_CONT | IS | 24 | 10327 | -0.0232 | -0.0159 | 0.16% | -0.09% | 0.07% | 0.07% |
| H3_REL_12H_CONT | OOS | 4 | 3505 | -0.0194 | -0.0349 | 0.02% | -0.01% | 0.02% | 0.02% |
| H3_REL_12H_CONT | OOS | 8 | 3501 | 0.0055 | -0.0173 | 0.06% | -0.00% | 0.06% | 0.06% |
| H3_REL_12H_CONT | OOS | 24 | 3485 | 0.0126 | -0.0293 | 0.14% | 0.01% | 0.16% | 0.16% |
| H3_REL_12H_CONT | VALIDATION | 4 | 3509 | -0.0448 | -0.0073 | -0.04% | 0.03% | -0.01% | -0.01% |
| H3_REL_12H_CONT | VALIDATION | 8 | 3509 | -0.0503 | -0.0128 | -0.09% | 0.07% | -0.02% | -0.02% |
| H3_REL_12H_CONT | VALIDATION | 24 | 3509 | -0.0606 | -0.0404 | -0.25% | 0.13% | -0.12% | -0.12% |
| H4_REL_24H_CONT | IS | 4 | 10327 | -0.0239 | -0.0243 | 0.04% | -0.02% | 0.02% | 0.02% |
| H4_REL_24H_CONT | IS | 8 | 10327 | -0.0236 | -0.0255 | 0.07% | -0.04% | 0.03% | 0.03% |
| H4_REL_24H_CONT | IS | 24 | 10327 | -0.0309 | -0.0133 | 0.16% | -0.11% | 0.05% | 0.05% |
| H4_REL_24H_CONT | OOS | 4 | 3505 | -0.0069 | -0.0267 | 0.03% | 0.00% | 0.04% | 0.04% |
| H4_REL_24H_CONT | OOS | 8 | 3501 | 0.0074 | -0.0312 | 0.06% | 0.01% | 0.08% | 0.08% |
| H4_REL_24H_CONT | OOS | 24 | 3485 | 0.0234 | -0.0349 | 0.16% | 0.04% | 0.20% | 0.20% |
| H4_REL_24H_CONT | VALIDATION | 4 | 3509 | -0.0626 | -0.0307 | -0.06% | 0.00% | -0.06% | -0.06% |
| H4_REL_24H_CONT | VALIDATION | 8 | 3509 | -0.0729 | -0.0416 | -0.11% | 0.02% | -0.09% | -0.09% |
| H4_REL_24H_CONT | VALIDATION | 24 | 3509 | -0.0704 | -0.0538 | -0.27% | 0.09% | -0.18% | -0.18% |
| H5_REL_24H_REV | IS | 4 | 10327 | 0.0239 | 0.0247 | 0.02% | -0.04% | -0.03% | -0.03% |
| H5_REL_24H_REV | IS | 8 | 10327 | 0.0236 | 0.0250 | 0.03% | -0.07% | -0.04% | -0.04% |
| H5_REL_24H_REV | IS | 24 | 10327 | 0.0309 | 0.0125 | 0.11% | -0.16% | -0.05% | -0.05% |
| H5_REL_24H_REV | OOS | 4 | 3505 | 0.0069 | 0.0220 | -0.00% | -0.03% | -0.04% | -0.04% |
| H5_REL_24H_REV | OOS | 8 | 3501 | -0.0074 | 0.0281 | -0.01% | -0.07% | -0.08% | -0.08% |
| H5_REL_24H_REV | OOS | 24 | 3485 | -0.0234 | 0.0283 | -0.03% | -0.15% | -0.18% | -0.18% |
| H5_REL_24H_REV | VALIDATION | 4 | 3509 | 0.0626 | 0.0221 | -0.01% | 0.07% | 0.06% | 0.06% |
| H5_REL_24H_REV | VALIDATION | 8 | 3509 | 0.0729 | 0.0295 | -0.03% | 0.14% | 0.11% | 0.11% |
| H5_REL_24H_REV | VALIDATION | 24 | 3509 | 0.0704 | 0.0309 | -0.09% | 0.30% | 0.21% | 0.21% |
| H6_REL_24H_VOLADJ_CONT | IS | 4 | 10327 | -0.0212 | -0.0177 | 0.04% | -0.02% | 0.02% | 0.02% |
| H6_REL_24H_VOLADJ_CONT | IS | 8 | 10327 | -0.0210 | -0.0160 | 0.07% | -0.03% | 0.04% | 0.04% |
| H6_REL_24H_VOLADJ_CONT | IS | 24 | 10327 | -0.0271 | -0.0053 | 0.17% | -0.09% | 0.07% | 0.07% |
| H6_REL_24H_VOLADJ_CONT | OOS | 4 | 3505 | -0.0034 | -0.0294 | 0.03% | 0.01% | 0.04% | 0.04% |
| H6_REL_24H_VOLADJ_CONT | OOS | 8 | 3501 | 0.0124 | -0.0374 | 0.07% | 0.01% | 0.08% | 0.08% |
| H6_REL_24H_VOLADJ_CONT | OOS | 24 | 3485 | 0.0289 | -0.0403 | 0.16% | 0.05% | 0.21% | 0.21% |
| H6_REL_24H_VOLADJ_CONT | VALIDATION | 4 | 3509 | -0.0645 | -0.0211 | -0.06% | 0.00% | -0.06% | -0.06% |
| H6_REL_24H_VOLADJ_CONT | VALIDATION | 8 | 3509 | -0.0746 | -0.0261 | -0.12% | 0.02% | -0.10% | -0.10% |
| H6_REL_24H_VOLADJ_CONT | VALIDATION | 24 | 3509 | -0.0714 | -0.0416 | -0.27% | 0.09% | -0.18% | -0.18% |
| H7_RESID_24H_CONT | IS | 4 | 10327 | -0.0174 | -0.0067 | 0.04% | -0.01% | 0.03% | 0.03% |
| H7_RESID_24H_CONT | IS | 8 | 10327 | -0.0150 | -0.0023 | 0.08% | -0.03% | 0.05% | 0.05% |
| H7_RESID_24H_CONT | IS | 24 | 10327 | -0.0227 | 0.0034 | 0.17% | -0.08% | 0.09% | 0.09% |
| H7_RESID_24H_CONT | OOS | 4 | 3505 | 0.0020 | -0.0118 | 0.04% | 0.01% | 0.05% | 0.05% |
| H7_RESID_24H_CONT | OOS | 8 | 3501 | 0.0109 | -0.0116 | 0.06% | 0.02% | 0.08% | 0.08% |
| H7_RESID_24H_CONT | OOS | 24 | 3485 | 0.0267 | -0.0134 | 0.16% | 0.04% | 0.20% | 0.20% |
| H7_RESID_24H_CONT | VALIDATION | 4 | 3509 | -0.0576 | -0.0253 | -0.06% | 0.01% | -0.05% | -0.05% |
| H7_RESID_24H_CONT | VALIDATION | 8 | 3509 | -0.0658 | -0.0331 | -0.12% | 0.04% | -0.08% | -0.08% |
| H7_RESID_24H_CONT | VALIDATION | 24 | 3509 | -0.0604 | -0.0411 | -0.26% | 0.13% | -0.13% | -0.13% |
| H8_RESID_24H_REV | IS | 4 | 10327 | 0.0174 | 0.0071 | 0.01% | -0.05% | -0.04% | -0.04% |
| H8_RESID_24H_REV | IS | 8 | 10327 | 0.0150 | 0.0017 | 0.02% | -0.08% | -0.06% | -0.06% |
| H8_RESID_24H_REV | IS | 24 | 10327 | 0.0227 | -0.0042 | 0.09% | -0.16% | -0.07% | -0.07% |
| H8_RESID_24H_REV | OOS | 4 | 3505 | -0.0020 | 0.0071 | -0.01% | -0.03% | -0.04% | -0.04% |
| H8_RESID_24H_REV | OOS | 8 | 3501 | -0.0109 | 0.0084 | -0.01% | -0.06% | -0.08% | -0.08% |
| H8_RESID_24H_REV | OOS | 24 | 3485 | -0.0267 | 0.0068 | -0.04% | -0.15% | -0.18% | -0.18% |
| H8_RESID_24H_REV | VALIDATION | 4 | 3509 | 0.0576 | 0.0166 | -0.01% | 0.08% | 0.06% | 0.06% |
| H8_RESID_24H_REV | VALIDATION | 8 | 3509 | 0.0658 | 0.0210 | -0.03% | 0.13% | 0.10% | 0.10% |
| H8_RESID_24H_REV | VALIDATION | 24 | 3509 | 0.0604 | 0.0182 | -0.12% | 0.28% | 0.16% | 0.16% |
| H9_VOL_SHOCK_XS | IS | 4 | 10327 | -0.0171 | -0.0145 | 0.04% | -0.01% | 0.02% | 0.02% |
| H9_VOL_SHOCK_XS | IS | 8 | 10327 | -0.0181 | -0.0148 | 0.07% | -0.03% | 0.04% | 0.04% |
| H9_VOL_SHOCK_XS | IS | 24 | 10327 | -0.0212 | -0.0078 | 0.17% | -0.10% | 0.07% | 0.07% |
| H9_VOL_SHOCK_XS | OOS | 4 | 3505 | 0.0006 | -0.0201 | 0.03% | -0.00% | 0.02% | 0.02% |
| H9_VOL_SHOCK_XS | OOS | 8 | 3501 | 0.0083 | -0.0207 | 0.06% | 0.00% | 0.06% | 0.06% |
| H9_VOL_SHOCK_XS | OOS | 24 | 3485 | 0.0243 | -0.0227 | 0.15% | 0.02% | 0.17% | 0.17% |
| H9_VOL_SHOCK_XS | VALIDATION | 4 | 3509 | -0.0328 | -0.0203 | -0.04% | 0.02% | -0.02% | -0.02% |
| H9_VOL_SHOCK_XS | VALIDATION | 8 | 3509 | -0.0339 | -0.0226 | -0.08% | 0.05% | -0.03% | -0.03% |
| H9_VOL_SHOCK_XS | VALIDATION | 24 | 3509 | -0.0556 | -0.0411 | -0.25% | 0.11% | -0.14% | -0.14% |

## 4. Cross-sectional bucket analysis (terciles, horizon 24, IS/VAL/OOS)

| Hypothesis | Phase | Bucket (0=weak) | n | Mean fwd | Median | Hit | Exec mean |
|---|---|---:|---:|---:|---:|---:|---:|
| H1_REL_1H_CONT | IS | 0 | 30981 | 0.14% | 0.01% | 50.07% | 0.14% |
| H1_REL_1H_CONT | IS | 1 | 30981 | 0.11% | 0.05% | 50.66% | 0.11% |
| H1_REL_1H_CONT | IS | 2 | 41308 | 0.15% | 0.02% | 50.18% | 0.15% |
| H1_REL_1H_CONT | OOS | 0 | 10455 | 0.03% | -0.04% | 49.02% | 0.03% |
| H1_REL_1H_CONT | OOS | 1 | 10455 | 0.04% | -0.02% | 49.46% | 0.04% |
| H1_REL_1H_CONT | OOS | 2 | 13940 | 0.10% | -0.02% | 49.46% | 0.10% |
| H1_REL_1H_CONT | VALIDATION | 0 | 10527 | -0.16% | -0.19% | 46.84% | -0.16% |
| H1_REL_1H_CONT | VALIDATION | 1 | 10527 | -0.12% | -0.12% | 47.60% | -0.13% |
| H1_REL_1H_CONT | VALIDATION | 2 | 14036 | -0.20% | -0.19% | 47.07% | -0.20% |
| H10_TAKER_IMB | IS | 0 | 0 | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | IS | 1 | 0 | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | IS | 2 | 0 | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | OOS | 0 | 0 | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | OOS | 1 | 0 | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | OOS | 2 | 0 | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | VALIDATION | 0 | 0 | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | VALIDATION | 1 | 0 | n/a | n/a | n/a | n/a |
| H10_TAKER_IMB | VALIDATION | 2 | 0 | n/a | n/a | n/a | n/a |
| H2_REL_4H_CONT | IS | 0 | 30981 | 0.10% | 0.01% | 50.12% | 0.10% |
| H2_REL_4H_CONT | IS | 1 | 30981 | 0.13% | 0.05% | 50.72% | 0.13% |
| H2_REL_4H_CONT | IS | 2 | 41308 | 0.16% | 0.01% | 50.09% | 0.16% |
| H2_REL_4H_CONT | OOS | 0 | 10455 | 0.00% | -0.05% | 48.92% | 0.00% |
| H2_REL_4H_CONT | OOS | 1 | 10455 | 0.05% | -0.01% | 49.79% | 0.05% |
| H2_REL_4H_CONT | OOS | 2 | 13940 | 0.11% | -0.03% | 49.28% | 0.11% |
| H2_REL_4H_CONT | VALIDATION | 0 | 10527 | -0.13% | -0.17% | 47.00% | -0.13% |
| H2_REL_4H_CONT | VALIDATION | 1 | 10527 | -0.14% | -0.15% | 47.35% | -0.14% |
| H2_REL_4H_CONT | VALIDATION | 2 | 14036 | -0.21% | -0.19% | 47.14% | -0.21% |
| H3_REL_12H_CONT | IS | 0 | 30981 | 0.09% | 0.05% | 50.61% | 0.09% |
| H3_REL_12H_CONT | IS | 1 | 30981 | 0.15% | 0.06% | 50.70% | 0.15% |
| H3_REL_12H_CONT | IS | 2 | 41308 | 0.16% | -0.02% | 49.74% | 0.16% |
| H3_REL_12H_CONT | OOS | 0 | 10455 | -0.01% | -0.03% | 49.13% | -0.02% |
| H3_REL_12H_CONT | OOS | 1 | 10455 | 0.03% | -0.01% | 49.55% | 0.03% |
| H3_REL_12H_CONT | OOS | 2 | 13940 | 0.14% | -0.03% | 49.31% | 0.14% |
| H3_REL_12H_CONT | VALIDATION | 0 | 10527 | -0.13% | -0.16% | 47.20% | -0.13% |
| H3_REL_12H_CONT | VALIDATION | 1 | 10527 | -0.09% | -0.10% | 48.25% | -0.09% |
| H3_REL_12H_CONT | VALIDATION | 2 | 14036 | -0.25% | -0.23% | 46.32% | -0.25% |
| H4_REL_24H_CONT | IS | 0 | 30981 | 0.11% | 0.04% | 50.48% | 0.11% |
| H4_REL_24H_CONT | IS | 1 | 30981 | 0.14% | 0.02% | 50.27% | 0.14% |
| H4_REL_24H_CONT | IS | 2 | 41308 | 0.16% | 0.02% | 50.16% | 0.16% |
| H4_REL_24H_CONT | OOS | 0 | 10455 | -0.04% | -0.05% | 48.53% | -0.04% |
| H4_REL_24H_CONT | OOS | 1 | 10455 | 0.04% | -0.03% | 49.23% | 0.03% |
| H4_REL_24H_CONT | OOS | 2 | 13940 | 0.16% | 0.00% | 50.00% | 0.16% |
| H4_REL_24H_CONT | VALIDATION | 0 | 10527 | -0.09% | -0.14% | 47.62% | -0.09% |
| H4_REL_24H_CONT | VALIDATION | 1 | 10527 | -0.11% | -0.15% | 47.58% | -0.11% |
| H4_REL_24H_CONT | VALIDATION | 2 | 14036 | -0.27% | -0.21% | 46.50% | -0.27% |
| H5_REL_24H_REV | IS | 0 | 30981 | 0.16% | -0.01% | 49.88% | 0.16% |
| H5_REL_24H_REV | IS | 1 | 30981 | 0.16% | 0.05% | 50.66% | 0.16% |
| H5_REL_24H_REV | IS | 2 | 41308 | 0.11% | 0.03% | 50.32% | 0.11% |
| H5_REL_24H_REV | OOS | 0 | 10455 | 0.15% | -0.01% | 49.78% | 0.15% |
| H5_REL_24H_REV | OOS | 1 | 10455 | 0.09% | 0.00% | 49.74% | 0.09% |
| H5_REL_24H_REV | OOS | 2 | 13940 | -0.03% | -0.05% | 48.69% | -0.03% |
| H5_REL_24H_REV | VALIDATION | 0 | 10527 | -0.30% | -0.25% | 46.02% | -0.30% |
| H5_REL_24H_REV | VALIDATION | 1 | 10527 | -0.13% | -0.11% | 47.99% | -0.13% |
| H5_REL_24H_REV | VALIDATION | 2 | 14036 | -0.09% | -0.16% | 47.39% | -0.09% |
| H6_REL_24H_VOLADJ_CONT | IS | 0 | 30981 | 0.09% | 0.05% | 50.60% | 0.09% |
| H6_REL_24H_VOLADJ_CONT | IS | 1 | 30981 | 0.14% | 0.02% | 50.13% | 0.14% |
| H6_REL_24H_VOLADJ_CONT | IS | 2 | 41308 | 0.17% | 0.02% | 50.17% | 0.17% |
| H6_REL_24H_VOLADJ_CONT | OOS | 0 | 10455 | -0.05% | -0.06% | 48.24% | -0.05% |
| H6_REL_24H_VOLADJ_CONT | OOS | 1 | 10455 | 0.03% | -0.02% | 49.34% | 0.03% |
| H6_REL_24H_VOLADJ_CONT | OOS | 2 | 13940 | 0.16% | 0.01% | 50.14% | 0.16% |
| H6_REL_24H_VOLADJ_CONT | VALIDATION | 0 | 10527 | -0.09% | -0.14% | 47.57% | -0.09% |
| H6_REL_24H_VOLADJ_CONT | VALIDATION | 1 | 10527 | -0.10% | -0.16% | 47.53% | -0.10% |
| H6_REL_24H_VOLADJ_CONT | VALIDATION | 2 | 14036 | -0.27% | -0.21% | 46.58% | -0.27% |
| H7_RESID_24H_CONT | IS | 0 | 30981 | 0.08% | 0.01% | 50.12% | 0.08% |
| H7_RESID_24H_CONT | IS | 1 | 30981 | 0.16% | 0.04% | 50.60% | 0.16% |
| H7_RESID_24H_CONT | IS | 2 | 41308 | 0.17% | 0.02% | 50.18% | 0.17% |
| H7_RESID_24H_CONT | OOS | 0 | 10455 | -0.04% | -0.06% | 48.40% | -0.05% |
| H7_RESID_24H_CONT | OOS | 1 | 10455 | 0.04% | -0.01% | 49.56% | 0.04% |
| H7_RESID_24H_CONT | OOS | 2 | 13940 | 0.16% | 0.00% | 49.85% | 0.15% |
| H7_RESID_24H_CONT | VALIDATION | 0 | 10527 | -0.13% | -0.15% | 47.50% | -0.13% |
| H7_RESID_24H_CONT | VALIDATION | 1 | 10527 | -0.08% | -0.15% | 47.58% | -0.08% |
| H7_RESID_24H_CONT | VALIDATION | 2 | 14036 | -0.26% | -0.20% | 46.59% | -0.26% |
| H8_RESID_24H_REV | IS | 0 | 30981 | 0.16% | -0.02% | 49.75% | 0.16% |
| H8_RESID_24H_REV | IS | 1 | 30981 | 0.18% | 0.08% | 51.12% | 0.18% |
| H8_RESID_24H_REV | IS | 2 | 41308 | 0.09% | 0.01% | 50.06% | 0.09% |
| H8_RESID_24H_REV | OOS | 0 | 10455 | 0.15% | 0.00% | 49.82% | 0.15% |
| H8_RESID_24H_REV | OOS | 1 | 10455 | 0.10% | 0.00% | 49.86% | 0.10% |
| H8_RESID_24H_REV | OOS | 2 | 13940 | -0.04% | -0.06% | 48.56% | -0.04% |
| H8_RESID_24H_REV | VALIDATION | 0 | 10527 | -0.28% | -0.22% | 46.31% | -0.28% |
| H8_RESID_24H_REV | VALIDATION | 1 | 10527 | -0.12% | -0.14% | 47.69% | -0.12% |
| H8_RESID_24H_REV | VALIDATION | 2 | 14036 | -0.12% | -0.16% | 47.41% | -0.12% |
| H9_VOL_SHOCK_XS | IS | 0 | 30981 | 0.10% | 0.05% | 50.63% | 0.10% |
| H9_VOL_SHOCK_XS | IS | 1 | 30981 | 0.13% | 0.01% | 50.08% | 0.13% |
| H9_VOL_SHOCK_XS | IS | 2 | 41308 | 0.17% | 0.02% | 50.19% | 0.17% |
| H9_VOL_SHOCK_XS | OOS | 0 | 10455 | -0.02% | -0.04% | 48.88% | -0.02% |
| H9_VOL_SHOCK_XS | OOS | 1 | 10455 | 0.02% | -0.03% | 49.19% | 0.02% |
| H9_VOL_SHOCK_XS | OOS | 2 | 13940 | 0.15% | -0.00% | 49.77% | 0.15% |
| H9_VOL_SHOCK_XS | VALIDATION | 0 | 10527 | -0.11% | -0.14% | 47.66% | -0.11% |
| H9_VOL_SHOCK_XS | VALIDATION | 1 | 10527 | -0.10% | -0.16% | 47.15% | -0.11% |
| H9_VOL_SHOCK_XS | VALIDATION | 2 | 14036 | -0.25% | -0.19% | 46.79% | -0.25% |

## 5. LONG vs SHORT

LONG = hold the top tercile of the signed signal. SHORT = short the bottom tercile. Combined spread hides nothing: both sides are shown. A side with negative mean is not an Isolated book to enable.

- **H1_REL_1H_CONT** OOS 24h: LONG mean 0.10% hit 50.82%; SHORT mean -0.03% hit 49.90%; class REJECTED.
- **H2_REL_4H_CONT** OOS 24h: LONG mean 0.11% hit 50.79%; SHORT mean -0.00% hit 50.65%; class REJECTED.
- **H3_REL_12H_CONT** OOS 24h: LONG mean 0.14% hit 51.91%; SHORT mean 0.01% hit 49.93%; class REJECTED.
- **H4_REL_24H_CONT** OOS 24h: LONG mean 0.16% hit 51.85%; SHORT mean 0.04% hit 51.19%; class REJECTED.
- **H5_REL_24H_REV** OOS 24h: LONG mean -0.03% hit 49.33%; SHORT mean -0.15% hit 48.24%; class REJECTED.
- **H6_REL_24H_VOLADJ_CONT** OOS 24h: LONG mean 0.16% hit 52.28%; SHORT mean 0.05% hit 50.85%; class REJECTED.
- **H7_RESID_24H_CONT** OOS 24h: LONG mean 0.16% hit 52.11%; SHORT mean 0.04% hit 50.99%; class REJECTED.
- **H8_RESID_24H_REV** OOS 24h: LONG mean -0.04% hit 48.75%; SHORT mean -0.15% hit 48.26%; class REJECTED.
- **H9_VOL_SHOCK_XS** OOS 24h: LONG mean 0.15% hit 50.62%; SHORT mean 0.02% hit 50.50%; class REJECTED.
- **H10_TAKER_IMB** OOS 24h: LONG mean n/a hit n/a; SHORT mean n/a hit n/a; class REJECTED.

## 6. Market regime analysis

BTC trend = sign(EMA20−EMA50) at t. BTC vol = ATR% percentile(50) at t. Low &lt; 0.4, high &gt; 0.6. Conditional IC at horizon 4 and 24.

| Hypothesis | Regime | Phase | H | n | CS IC | Spread |
|---|---|---|---:|---:|---:|---:|
| H1_REL_1H_CONT | BTC_TREND_UP | IS | 24 | 5563 | -0.0023 | 0.04% |
| H1_REL_1H_CONT | BTC_TREND_DOWN | IS | 24 | 4764 | -0.0173 | -0.04% |
| H1_REL_1H_CONT | BTC_VOL_LOW | IS | 24 | 4285 | -0.0082 | 0.02% |
| H1_REL_1H_CONT | BTC_VOL_HIGH | IS | 24 | 4486 | -0.0079 | 0.01% |
| H1_REL_1H_CONT | BTC_TREND_UP | OOS | 24 | 1736 | 0.0105 | 0.07% |
| H1_REL_1H_CONT | BTC_TREND_DOWN | OOS | 24 | 1749 | 0.0021 | 0.07% |
| H1_REL_1H_CONT | BTC_VOL_LOW | OOS | 24 | 1452 | -0.0029 | 0.04% |
| H1_REL_1H_CONT | BTC_VOL_HIGH | OOS | 24 | 1420 | 0.0166 | 0.10% |
| H2_REL_4H_CONT | BTC_TREND_UP | IS | 24 | 5563 | -0.0091 | 0.09% |
| H2_REL_4H_CONT | BTC_TREND_DOWN | IS | 24 | 4764 | -0.0136 | 0.01% |
| H2_REL_4H_CONT | BTC_VOL_LOW | IS | 24 | 4285 | -0.0129 | 0.06% |
| H2_REL_4H_CONT | BTC_VOL_HIGH | IS | 24 | 4486 | -0.0081 | 0.06% |
| H2_REL_4H_CONT | BTC_TREND_UP | OOS | 24 | 1736 | 0.0140 | 0.06% |
| H2_REL_4H_CONT | BTC_TREND_DOWN | OOS | 24 | 1749 | 0.0047 | 0.16% |
| H2_REL_4H_CONT | BTC_VOL_LOW | OOS | 24 | 1452 | -0.0084 | 0.03% |
| H2_REL_4H_CONT | BTC_VOL_HIGH | OOS | 24 | 1420 | 0.0258 | 0.18% |
| H3_REL_12H_CONT | BTC_TREND_UP | IS | 24 | 5563 | -0.0239 | 0.15% |
| H3_REL_12H_CONT | BTC_TREND_DOWN | IS | 24 | 4764 | -0.0225 | -0.01% |
| H3_REL_12H_CONT | BTC_VOL_LOW | IS | 24 | 4285 | -0.0387 | 0.05% |
| H3_REL_12H_CONT | BTC_VOL_HIGH | IS | 24 | 4486 | -0.0122 | 0.08% |
| H3_REL_12H_CONT | BTC_TREND_UP | OOS | 24 | 1736 | 0.0285 | 0.14% |
| H3_REL_12H_CONT | BTC_TREND_DOWN | OOS | 24 | 1749 | -0.0032 | 0.17% |
| H3_REL_12H_CONT | BTC_VOL_LOW | OOS | 24 | 1452 | -0.0056 | 0.06% |
| H3_REL_12H_CONT | BTC_VOL_HIGH | OOS | 24 | 1420 | 0.0276 | 0.22% |
| H4_REL_24H_CONT | BTC_TREND_UP | IS | 24 | 5563 | -0.0225 | 0.21% |
| H4_REL_24H_CONT | BTC_TREND_DOWN | IS | 24 | 4764 | -0.0407 | -0.13% |
| H4_REL_24H_CONT | BTC_VOL_LOW | IS | 24 | 4285 | -0.0598 | 0.00% |
| H4_REL_24H_CONT | BTC_VOL_HIGH | IS | 24 | 4486 | -0.0127 | 0.08% |
| H4_REL_24H_CONT | BTC_TREND_UP | OOS | 24 | 1736 | 0.0387 | 0.21% |
| H4_REL_24H_CONT | BTC_TREND_DOWN | OOS | 24 | 1749 | 0.0082 | 0.19% |
| H4_REL_24H_CONT | BTC_VOL_LOW | OOS | 24 | 1452 | 0.0136 | 0.13% |
| H4_REL_24H_CONT | BTC_VOL_HIGH | OOS | 24 | 1420 | 0.0331 | 0.25% |
| H5_REL_24H_REV | BTC_TREND_UP | IS | 24 | 5563 | 0.0225 | -0.22% |
| H5_REL_24H_REV | BTC_TREND_DOWN | IS | 24 | 4764 | 0.0407 | 0.15% |
| H5_REL_24H_REV | BTC_VOL_LOW | IS | 24 | 4285 | 0.0598 | 0.01% |
| H5_REL_24H_REV | BTC_VOL_HIGH | IS | 24 | 4486 | 0.0127 | -0.10% |
| H5_REL_24H_REV | BTC_TREND_UP | OOS | 24 | 1736 | -0.0387 | -0.17% |
| H5_REL_24H_REV | BTC_TREND_DOWN | OOS | 24 | 1749 | -0.0082 | -0.19% |
| H5_REL_24H_REV | BTC_VOL_LOW | OOS | 24 | 1452 | -0.0136 | -0.12% |
| H5_REL_24H_REV | BTC_VOL_HIGH | OOS | 24 | 1420 | -0.0331 | -0.22% |
| H6_REL_24H_VOLADJ_CONT | BTC_TREND_UP | IS | 24 | 5563 | -0.0232 | 0.21% |
| H6_REL_24H_VOLADJ_CONT | BTC_TREND_DOWN | IS | 24 | 4764 | -0.0316 | -0.08% |
| H6_REL_24H_VOLADJ_CONT | BTC_VOL_LOW | IS | 24 | 4285 | -0.0556 | 0.01% |
| H6_REL_24H_VOLADJ_CONT | BTC_VOL_HIGH | IS | 24 | 4486 | -0.0077 | 0.11% |
| H6_REL_24H_VOLADJ_CONT | BTC_TREND_UP | OOS | 24 | 1736 | 0.0460 | 0.21% |
| H6_REL_24H_VOLADJ_CONT | BTC_TREND_DOWN | OOS | 24 | 1749 | 0.0120 | 0.21% |
| H6_REL_24H_VOLADJ_CONT | BTC_VOL_LOW | OOS | 24 | 1452 | 0.0211 | 0.17% |
| H6_REL_24H_VOLADJ_CONT | BTC_VOL_HIGH | OOS | 24 | 1420 | 0.0394 | 0.25% |
| H7_RESID_24H_CONT | BTC_TREND_UP | IS | 24 | 5563 | -0.0115 | 0.24% |
| H7_RESID_24H_CONT | BTC_TREND_DOWN | IS | 24 | 4764 | -0.0358 | -0.09% |
| H7_RESID_24H_CONT | BTC_VOL_LOW | IS | 24 | 4285 | -0.0518 | 0.04% |
| H7_RESID_24H_CONT | BTC_VOL_HIGH | IS | 24 | 4486 | -0.0019 | 0.12% |
| H7_RESID_24H_CONT | BTC_TREND_UP | OOS | 24 | 1736 | 0.0452 | 0.21% |
| H7_RESID_24H_CONT | BTC_TREND_DOWN | OOS | 24 | 1749 | 0.0082 | 0.19% |
| H7_RESID_24H_CONT | BTC_VOL_LOW | OOS | 24 | 1452 | 0.0219 | 0.16% |
| H7_RESID_24H_CONT | BTC_VOL_HIGH | OOS | 24 | 1420 | 0.0285 | 0.20% |
| H8_RESID_24H_REV | BTC_TREND_UP | IS | 24 | 5563 | 0.0115 | -0.23% |
| H8_RESID_24H_REV | BTC_TREND_DOWN | IS | 24 | 4764 | 0.0358 | 0.11% |
| H8_RESID_24H_REV | BTC_VOL_LOW | IS | 24 | 4285 | 0.0518 | -0.00% |
| H8_RESID_24H_REV | BTC_VOL_HIGH | IS | 24 | 4486 | 0.0019 | -0.12% |
| H8_RESID_24H_REV | BTC_TREND_UP | OOS | 24 | 1736 | -0.0452 | -0.16% |
| H8_RESID_24H_REV | BTC_TREND_DOWN | OOS | 24 | 1749 | -0.0082 | -0.21% |
| H8_RESID_24H_REV | BTC_VOL_LOW | OOS | 24 | 1452 | -0.0219 | -0.15% |
| H8_RESID_24H_REV | BTC_VOL_HIGH | OOS | 24 | 1420 | -0.0285 | -0.17% |
| H9_VOL_SHOCK_XS | BTC_TREND_UP | IS | 24 | 5563 | -0.0171 | 0.17% |
| H9_VOL_SHOCK_XS | BTC_TREND_DOWN | IS | 24 | 4764 | -0.0261 | -0.05% |
| H9_VOL_SHOCK_XS | BTC_VOL_LOW | IS | 24 | 4285 | -0.0348 | 0.04% |
| H9_VOL_SHOCK_XS | BTC_VOL_HIGH | IS | 24 | 4486 | -0.0159 | 0.08% |
| H9_VOL_SHOCK_XS | BTC_TREND_UP | OOS | 24 | 1736 | 0.0232 | 0.14% |
| H9_VOL_SHOCK_XS | BTC_TREND_DOWN | OOS | 24 | 1749 | 0.0253 | 0.21% |
| H9_VOL_SHOCK_XS | BTC_VOL_LOW | OOS | 24 | 1452 | 0.0087 | 0.09% |
| H9_VOL_SHOCK_XS | BTC_VOL_HIGH | OOS | 24 | 1420 | 0.0434 | 0.26% |
| H10_TAKER_IMB | BTC_TREND_UP | IS | 24 | 0 | n/a | n/a |
| H10_TAKER_IMB | BTC_TREND_DOWN | IS | 24 | 0 | n/a | n/a |
| H10_TAKER_IMB | BTC_VOL_LOW | IS | 24 | 0 | n/a | n/a |
| H10_TAKER_IMB | BTC_VOL_HIGH | IS | 24 | 0 | n/a | n/a |
| H10_TAKER_IMB | BTC_TREND_UP | OOS | 24 | 0 | n/a | n/a |
| H10_TAKER_IMB | BTC_TREND_DOWN | OOS | 24 | 0 | n/a | n/a |
| H10_TAKER_IMB | BTC_VOL_LOW | OOS | 24 | 0 | n/a | n/a |
| H10_TAKER_IMB | BTC_VOL_HIGH | OOS | 24 | 0 | n/a | n/a |

## 7. Funding / OI

| Hypothesis | Phase | Horizon hours | n | IC | High funding mean fwd | Low funding mean fwd |
|---|---|---:|---:|---:|---:|---:|
| H_FUND_EXTREME | IS | 8 | 103270 | -0.0071 | 0.26% | 0.21% |
| H_FUND_EXTREME | IS | 24 | 103270 | 0.0041 | n/a | n/a |
| H_FUND_EXTREME | VALIDATION | 8 | 35090 | -0.0648 | n/a | 0.14% |
| H_FUND_EXTREME | VALIDATION | 24 | 35090 | -0.0730 | n/a | n/a |
| H_FUND_EXTREME | OOS | 8 | 35090 | 0.0240 | 0.31% | 1.40% |
| H_FUND_EXTREME | OOS | 24 | 35090 | 0.0179 | n/a | n/a |

Open interest: **DATA_UNAVAILABLE** for a 2-year claim. Public `openInterestHist` is ~30 days. 30-day OI was not substituted.

## 8. Candidate strategies

Stage 2 Isolated replay is gated on PROMISING signals. None were promoted unless classified PROMISING above.
Paired long-strong / short-weak is **not** an Isolated Cross book: LOW allows two independent Isolated positions, but they do not share margin and the current replay is single-book. The Stage 1 spread is the statistical long-short factor, not an executable pair.

## 9. Cost sensitivity

Execution-aware spread (next open → close[t+h]) is reported as Exec spread. Round-trip hurdle is 0.12 %. A signal whose exec spread is below the hurdle cannot survive BASE costs even with a perfect 2R exit.

## 10. OOS results

OOS is the last 20% of aligned 1h bars. It was not used to change lookbacks (1/4/12/24, beta 168, vol 20, ATR 14, terciles).

## 11. Full-universe results

Not run. Stage 3 (528 coins × 5m/15m/1h) is blocked until a Stage 1 signal is PROMISING.

## 12. Failure reasons

Wave-1/2 failed as absolute-price strategies (WR ≈ 33% at 2R after costs). Wave-3 asks whether **relative** information is predictable. A hypothesis fails when IS CS IC ≤ 0.02 or the tercile spread is not positive, or VALIDATION does not keep the IS sign, or the execution spread does not clear costs.

- `H1_REL_1H_CONT`: **REJECTED**. 1h return minus BTC 1h return; stronger relative coins continue.
- `H2_REL_4H_CONT`: **REJECTED**. 4h return minus BTC; relative strength continuation.
- `H3_REL_12H_CONT`: **REJECTED**. 12h return minus BTC; relative strength continuation.
- `H4_REL_24H_CONT`: **REJECTED**. 24h return minus BTC; relative strength continuation.
- `H5_REL_24H_REV`: **REJECTED**. 24h relative strength mean-reverts over the next day.
- `H6_REL_24H_VOLADJ_CONT`: **REJECTED**. 24h relative return / ATR%. Vol-adjusted residual momentum.
- `H7_RESID_24H_CONT`: **REJECTED**. 24h return minus beta*BTC, beta from past 168 hourly returns.
- `H8_RESID_24H_REV`: **REJECTED**. Residual 24h mean-reverts.
- `H9_VOL_SHOCK_XS`: **REJECTED**. Volume / SMA20 ranked cross-sectionally; high shock continues with the 24h relative sign.
- `H10_TAKER_IMB`: **REJECTED** / **DATA_UNAVAILABLE**. Taker buy is present on only ~16 bars per coin in the 2-year kline cache (the Wave-2 head/tail download). The rest are stored 0 and were not treated as imbalance 0. Not a 2-year taker test.

## 13. Data limitations

- Cross-section among 10 mega-caps is not the 528-coin rank. Rerunning 528 without a 10-coin IC would be data mining.
- Taker buy volume is **not** usable for 2 years in the current kline cache (almost all bars stored 0). A full kline re-download would be required; it was not done here.
- Inner join drops hours where any coin is missing.
- Terciles, not deciles.
- Beta is vs BTC only, not a multi-factor market.
- OI 2y, liquidations, depth, predicted funding: DATA_UNAVAILABLE.
- Holdout is chronological 20% in the same process; parameters were not edited after OOS.

## 14. Further research (only if data exists)

- If CS IC is flat: the missing input is probably **full-universe ranks**, **OI**, or **liquidation/flow**, not another RSI period.
- Do not raise Isolated 0.5%/3x to manufacture PF.
- Do not enable LIVE.

## Run notes
- Stage 1 signal research only. No new Isolated strategy was promoted. LIVE=OFF.
- Ranks and beta at t use only bars with OpenTime <= t. Forward returns use t+h, which is labeling, not a feature.
- 10-coin panel is an inner join on OpenTime. Missing coins at a timestamp drop that row.
- Terciles are used instead of deciles because n=10. A 10% decile would be one coin and is not reported as a decile result.
- Round-trip cost hurdle 0.12 % (0.04%*2 commission + 0.02%*2 slippage).
- Taker imbalance valid on 0/173450 bar-coins after warmup.
- Wave-3 Stage 1 signal research. LIVE disabled. Frozen five / FrozenRisk / Isolated LOW unchanged.
- No strategy promotion. No 528-universe run. OOS not used to mutate lookbacks.
- Window 2024-09-18 → 2026-09-19. Panel 1h inner-join. Coins BTCUSDT,ETHUSDT,BNBUSDT,SOLUSDT,XRPUSDT,DOGEUSDT,ADAUSDT,AVAXUSDT,LINKUSDT,LTCUSDT.
- BTCUSDT 1h bars=17545 cache hit dl=0 takerBuy>0 15
- ETHUSDT 1h bars=17545 cache hit dl=0 takerBuy>0 16
- BNBUSDT 1h bars=17545 cache hit dl=0 takerBuy>0 16
- SOLUSDT 1h bars=17545 cache hit dl=0 takerBuy>0 16
- XRPUSDT 1h bars=17545 cache hit dl=0 takerBuy>0 16
- DOGEUSDT 1h bars=17545 cache hit dl=0 takerBuy>0 16
- ADAUSDT 1h bars=17545 cache hit dl=0 takerBuy>0 16
- AVAXUSDT 1h bars=17545 cache hit dl=0 takerBuy>0 16
- LINKUSDT 1h bars=17545 cache hit dl=0 takerBuy>0 16
- LTCUSDT 1h bars=17545 cache hit dl=0 takerBuy>0 16
- Aligned panel rows=17545 coins=10 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
- BTCUSDT funding n=2194 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
- ETHUSDT funding n=2194 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
- BNBUSDT funding n=2194 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
- SOLUSDT funding n=2194 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
- XRPUSDT funding n=2194 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
- DOGEUSDT funding n=2194 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
- ADAUSDT funding n=2194 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
- AVAXUSDT funding n=2194 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
- LINKUSDT funding n=2194 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
- LTCUSDT funding n=2194 first=2024-09-18 00:00:00Z last=2026-09-19 00:00:00Z
