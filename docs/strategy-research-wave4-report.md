# Model B Wave-4 information research report

Wave-1/2/3 found no robust Isolated LOW alpha in OHLCV, mechanism entries, relative ranks, or funding extremes.
Wave-4 acquired missing market information and tested whether it has predictive value **before** any new trading strategy.
Frozen five, FrozenRisk, Isolated LOW ($1,000 / 0.5% / 3x), Model B timing, and LIVE were not changed.
No candidate was marked VALIDATED_FOR_PAPER. Parameters were pre-registered. OOS was not used to mutate hypotheses.

## 1. Data availability audit

| Dataset | Status |
|---|---|
| AGGTRADE | ARCHIVE_AVAILABLE_NOT_INGESTED (Vision daily/aggTrades; redundant with kline taker for 1h) |
| BTC_OHLCV | AVAILABLE |
| DEPTH | ARCHIVE_AVAILABLE_NOT_INGESTED (Vision daily/bookDepth from 2023; ~0.5MB/coin/day; not used this wave) |
| FUNDING | AVAILABLE settled fundingTime <= close |
| LIQUIDATION | DATA_UNAVAILABLE (USD-M Vision liquidationSnapshot prefix empty; Binance stopped publishing) |
| LS_RATIO | AVAILABLE Vision count_long_short_ratio |
| METRICS_TAKER_LS | AVAILABLE Vision sum_taker_long_short_vol_ratio |
| OHLCV | AVAILABLE (Wave-4 versioned kline cache /fapi/v1/klines) |
| OI | AVAILABLE Binance Vision daily/metrics 5m last-observation <= close, finite 173450 |
| OI_VALUE | AVAILABLE sum_open_interest_value |
| PREDICTED_FUNDING | DATA_UNAVAILABLE |
| SYNC_OHLCV | AVAILABLE (inner-join panel on OpenTime) |
| TAKER | AVAILABLE kline field 9, valid 173450/173450 |
| TOP_LS | AVAILABLE Vision sum_toptrader_long_short_ratio |
| VOLUME | AVAILABLE |
| XS_RANKS | DERIVED at t from the aligned panel |

Infrastructure inspected before any new download: `BinancePublicMarketDataClient` (`/fapi/v1/klines`, ticker, exchangeInfo, premiumIndex snapshot); `BinanceFuturesHistoryClient` (settled funding, mark/index klines, `openInterestHist` ~30d); `ResearchKlineCache` / `KlineDiskCache`; `FuturesHistoryCache`. No aggTrade, liquidation, or depth client existed.

## 2. New datasets acquired

- **Taker buy base volume** re-downloaded from `GET /fapi/v1/klines` field 9 into a **versioned** Wave-4 kline cache. 10 coins × 17,545 1h bars, **taker coverage 100%**. The existing `artifacts/strategy-validation-cache` was not overwritten.
- **Open interest and positioning ratios** from Binance Vision `data/futures/um/daily/metrics/{symbol}` (5-minute CSV inside daily zip). 10 coins × 210,811 rows spanning 2024-09-18 00:00 → 2026-09-19 23:55 UTC. Last 5m print ≤ candle close; not the 30-day REST hist.
- Settled **funding** reused from the existing 2-year `FuturesHistoryCache` ingest (n=2,194 / coin).
- USD-M **liquidations** were not acquired (archive gone).
- Full-universe OHLCV ranks used **527** existing 1h cache files (presence-ranked; no taker re-download).

## 3. Exact source and coverage

| Dataset | Source | Resolution | Window | Notes |
|---|---|---|---|---|
| OHLCV + taker field 9 | `https://fapi.binance.com/fapi/v1/klines` | 1h | 2024-09-18 → 2026-09-19 | 10 liquid coins; versioned cache `artifacts/strategy-research/wave-4/klines` |
| Open interest | `https://data.binance.vision/data/futures/um/daily/metrics/` column `sum_open_interest` | 5m, last print ≤ candle close | same 10 coins / 2y | Archive exists from 2020-09 for BTCUSDT; not the 30-day REST hist |
| Account LS ratio | same metrics zip `count_long_short_ratio` | 5m | same | |
| Top-trader LS | `sum_toptrader_long_short_ratio` | 5m | same | |
| Metrics taker LS vol | `sum_taker_long_short_vol_ratio` | 5m | same | distinct from kline field 9 |
| Funding | `GET /fapi/v1/fundingRate` settled `fundingTime` | ~8h | same | Predicted next rate not used |
| Liquidations | Vision `daily/liquidationSnapshot` | — | — | Prefix empty. Binance: data no longer provided after 2024-03-31 |
| Depth | Vision `daily/bookDepth` | snapshots | from 2023 | Archive exists (~0.5MB/coin/day). Not ingested this wave |

## 4. Data quality checks

Legacy 1h cache (`strategy-validation-cache`): TakerBuyVolume>0 on ~18/17565 BTC bars. Root cause: `KlineDiskCache.Parse` never stored field 9; `ResearchKlineCache` then cache-hit the covered range and did not backfill. Schema did not drop the property — historical values were stored as 0. Field 9 is present on live `/fapi/v1/klines` and was re-downloaded into the versioned cache.
OI alignment is last 5m `create_time` ≤ kline `CloseTime` (the 01:00 print is not used on the 00:00–00:59 bar). Missing zips are skipped, not interpolated. Zero taker buy is DATA_UNAVAILABLE, not imbalance 0.

- **TAKER_KLINES**: ACQUIRED. GET /fapi/v1/klines field 9. versioned cache D:\Sources\TradingProject\artifacts\strategy-research\wave-4\klines
- **VISION_METRICS**: ACQUIRED. https://data.binance.vision/data/futures/um/daily/metrics/{symbol}/{symbol}-metrics-{yyyy-MM-dd}.zip. symbols=10 dir=D:\Sources\TradingProject\artifacts\strategy-research\wave-4\vision\metrics
- **LIQUIDATION**: DATA_UNAVAILABLE. Vision um/daily/liquidationSnapshot. prefix empty; Binance no longer provides USD-M snapshots
- **DEPTH**: ARCHIVE_AVAILABLE_NOT_INGESTED. Vision um/daily/bookDepth. exists from 2023; not downloaded this wave

## 5. Taker-flow research

| Hypothesis | Phase | H | n | CS IC | TS IC | LONG mean | SHORT mean | Spread | Exec spread |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| W4_M_TAKER_LS | IS | 4 | 10327 | -0.0010 | -0.0003 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_M_TAKER_LS | IS | 8 | 10327 | -0.0037 | -0.0002 | 0.05% | -0.06% | -0.01% | -0.01% |
| W4_M_TAKER_LS | IS | 24 | 10327 | -0.0031 | 0.0079 | 0.14% | -0.14% | 0.00% | 0.00% |
| W4_M_TAKER_LS | OOS | 4 | 3505 | -0.0152 | -0.0066 | 0.00% | -0.03% | -0.02% | -0.02% |
| W4_M_TAKER_LS | OOS | 8 | 3501 | -0.0178 | -0.0106 | 0.01% | -0.04% | -0.03% | -0.03% |
| W4_M_TAKER_LS | OOS | 24 | 3485 | -0.0126 | -0.0079 | 0.04% | -0.10% | -0.06% | -0.06% |
| W4_M_TAKER_LS | VALIDATION | 4 | 3509 | -0.0080 | -0.0015 | -0.03% | 0.04% | 0.00% | 0.00% |
| W4_M_TAKER_LS | VALIDATION | 8 | 3509 | -0.0114 | 0.0019 | -0.07% | 0.07% | -0.00% | -0.00% |
| W4_M_TAKER_LS | VALIDATION | 24 | 3509 | -0.0070 | 0.0031 | -0.18% | 0.15% | -0.03% | -0.03% |
| W4_T1_IMB_CONT | IS | 4 | 10327 | 0.0006 | -0.0138 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_T1_IMB_CONT | IS | 8 | 10327 | 0.0038 | -0.0014 | 0.06% | -0.05% | 0.01% | 0.01% |
| W4_T1_IMB_CONT | IS | 24 | 10327 | 0.0038 | -0.0038 | 0.15% | -0.15% | 0.00% | 0.00% |
| W4_T1_IMB_CONT | OOS | 4 | 3505 | -0.0080 | -0.0245 | 0.02% | -0.01% | 0.00% | 0.00% |
| W4_T1_IMB_CONT | OOS | 8 | 3501 | -0.0057 | -0.0057 | 0.02% | -0.02% | 0.00% | 0.00% |
| W4_T1_IMB_CONT | OOS | 24 | 3485 | -0.0047 | 0.0074 | 0.05% | -0.05% | 0.01% | 0.01% |
| W4_T1_IMB_CONT | VALIDATION | 4 | 3509 | -0.0114 | -0.0036 | -0.04% | 0.03% | -0.01% | -0.01% |
| W4_T1_IMB_CONT | VALIDATION | 8 | 3509 | -0.0178 | -0.0113 | -0.07% | 0.06% | -0.01% | -0.01% |
| W4_T1_IMB_CONT | VALIDATION | 24 | 3509 | -0.0139 | -0.0027 | -0.18% | 0.15% | -0.03% | -0.03% |
| W4_T2_IMB_REV | IS | 4 | 10327 | -0.0006 | 0.0138 | 0.02% | -0.03% | -0.01% | -0.01% |
| W4_T2_IMB_REV | IS | 8 | 10327 | -0.0038 | 0.0014 | 0.05% | -0.06% | -0.01% | -0.01% |
| W4_T2_IMB_REV | IS | 24 | 10327 | -0.0038 | 0.0038 | 0.13% | -0.15% | -0.02% | -0.02% |
| W4_T2_IMB_REV | OOS | 4 | 3505 | 0.0080 | 0.0245 | 0.01% | -0.02% | -0.00% | -0.00% |
| W4_T2_IMB_REV | OOS | 8 | 3501 | 0.0057 | 0.0057 | 0.02% | -0.02% | 0.00% | 0.00% |
| W4_T2_IMB_REV | OOS | 24 | 3485 | 0.0047 | -0.0074 | 0.06% | -0.03% | 0.03% | 0.03% |
| W4_T2_IMB_REV | VALIDATION | 4 | 3509 | 0.0114 | 0.0036 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_T2_IMB_REV | VALIDATION | 8 | 3509 | 0.0178 | 0.0113 | -0.06% | 0.07% | 0.02% | 0.02% |
| W4_T2_IMB_REV | VALIDATION | 24 | 3509 | 0.0139 | 0.0027 | -0.14% | 0.18% | 0.04% | 0.04% |
| W4_T3_DIMB | IS | 4 | 10327 | 0.0016 | -0.0044 | 0.03% | -0.02% | 0.00% | 0.00% |
| W4_T3_DIMB | IS | 8 | 10327 | -0.0015 | -0.0050 | 0.05% | -0.05% | -0.01% | -0.01% |
| W4_T3_DIMB | IS | 24 | 10327 | 0.0019 | -0.0034 | 0.14% | -0.14% | 0.00% | 0.00% |
| W4_T3_DIMB | OOS | 4 | 3505 | -0.0020 | 0.0041 | 0.01% | -0.01% | 0.00% | 0.00% |
| W4_T3_DIMB | OOS | 8 | 3501 | -0.0006 | -0.0033 | 0.02% | -0.01% | 0.01% | 0.01% |
| W4_T3_DIMB | OOS | 24 | 3485 | -0.0021 | 0.0003 | 0.05% | -0.04% | 0.01% | 0.01% |
| W4_T3_DIMB | VALIDATION | 4 | 3509 | 0.0055 | 0.0019 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_T3_DIMB | VALIDATION | 8 | 3509 | -0.0014 | -0.0004 | -0.06% | 0.07% | 0.01% | 0.01% |
| W4_T3_DIMB | VALIDATION | 24 | 3509 | -0.0018 | 0.0027 | -0.17% | 0.17% | -0.00% | -0.00% |
| W4_T4_FLOW_DIV | IS | 4 | 10327 | 0.0010 | -0.0100 | 0.02% | -0.02% | 0.00% | 0.00% |
| W4_T4_FLOW_DIV | IS | 8 | 10327 | 0.0042 | 0.0001 | 0.05% | -0.05% | -0.01% | -0.01% |
| W4_T4_FLOW_DIV | IS | 24 | 10327 | 0.0058 | -0.0028 | 0.15% | -0.12% | 0.03% | 0.03% |
| W4_T4_FLOW_DIV | OOS | 4 | 3505 | -0.0024 | 0.0014 | 0.01% | -0.01% | 0.00% | 0.00% |
| W4_T4_FLOW_DIV | OOS | 8 | 3501 | -0.0026 | -0.0021 | 0.02% | -0.03% | -0.01% | -0.01% |
| W4_T4_FLOW_DIV | OOS | 24 | 3485 | -0.0012 | 0.0029 | 0.06% | -0.06% | -0.00% | -0.00% |
| W4_T4_FLOW_DIV | VALIDATION | 4 | 3509 | 0.0036 | 0.0098 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_T4_FLOW_DIV | VALIDATION | 8 | 3509 | 0.0010 | -0.0058 | -0.06% | 0.07% | 0.01% | 0.01% |
| W4_T4_FLOW_DIV | VALIDATION | 24 | 3509 | 0.0016 | 0.0002 | -0.16% | 0.17% | 0.02% | 0.02% |
| W4_T5_IMB_VOL | IS | 4 | 10327 | -0.0007 | -0.0169 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_T5_IMB_VOL | IS | 8 | 10327 | 0.0027 | -0.0048 | 0.06% | -0.05% | 0.01% | 0.01% |
| W4_T5_IMB_VOL | IS | 24 | 10327 | 0.0030 | -0.0050 | 0.15% | -0.14% | 0.01% | 0.01% |
| W4_T5_IMB_VOL | OOS | 4 | 3505 | -0.0083 | -0.0276 | 0.02% | -0.02% | -0.00% | -0.00% |
| W4_T5_IMB_VOL | OOS | 8 | 3501 | -0.0019 | -0.0081 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_T5_IMB_VOL | OOS | 24 | 3485 | -0.0002 | 0.0069 | 0.06% | -0.06% | 0.00% | 0.00% |
| W4_T5_IMB_VOL | VALIDATION | 4 | 3509 | -0.0128 | -0.0033 | -0.04% | 0.03% | -0.01% | -0.01% |
| W4_T5_IMB_VOL | VALIDATION | 8 | 3509 | -0.0156 | -0.0145 | -0.07% | 0.06% | -0.01% | -0.01% |
| W4_T5_IMB_VOL | VALIDATION | 24 | 3509 | -0.0129 | -0.0079 | -0.17% | 0.15% | -0.02% | -0.02% |
| W4_T6_IMB_REL | IS | 4 | 10327 | 0.0044 | 0.0069 | 0.03% | -0.02% | 0.00% | 0.00% |
| W4_T6_IMB_REL | IS | 8 | 10327 | 0.0052 | 0.0126 | 0.06% | -0.05% | 0.01% | 0.01% |
| W4_T6_IMB_REL | IS | 24 | 10327 | 0.0080 | 0.0066 | 0.13% | -0.12% | 0.01% | 0.01% |
| W4_T6_IMB_REL | OOS | 4 | 3505 | -0.0063 | 0.0040 | 0.01% | -0.02% | -0.01% | -0.01% |
| W4_T6_IMB_REL | OOS | 8 | 3501 | 0.0046 | 0.0145 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_T6_IMB_REL | OOS | 24 | 3485 | 0.0064 | 0.0121 | 0.07% | -0.05% | 0.02% | 0.02% |
| W4_T6_IMB_REL | VALIDATION | 4 | 3509 | 0.0116 | 0.0043 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_T6_IMB_REL | VALIDATION | 8 | 3509 | 0.0011 | 0.0011 | -0.07% | 0.08% | 0.01% | 0.01% |
| W4_T6_IMB_REL | VALIDATION | 24 | 3509 | 0.0016 | -0.0088 | -0.15% | 0.19% | 0.03% | 0.04% |
| W4_TO_IMB_OI | IS | 4 | 10323 | 0.0006 | 0.0085 | 0.03% | -0.02% | 0.02% | 0.02% |
| W4_TO_IMB_OI | IS | 8 | 10323 | 0.0008 | 0.0047 | 0.06% | -0.04% | 0.02% | 0.02% |
| W4_TO_IMB_OI | IS | 24 | 10323 | -0.0073 | -0.0036 | 0.14% | -0.13% | 0.01% | 0.01% |
| W4_TO_IMB_OI | OOS | 4 | 3505 | -0.0053 | 0.0037 | 0.02% | -0.02% | -0.01% | -0.01% |
| W4_TO_IMB_OI | OOS | 8 | 3501 | 0.0013 | 0.0015 | 0.03% | -0.03% | 0.00% | 0.00% |
| W4_TO_IMB_OI | OOS | 24 | 3485 | -0.0015 | -0.0025 | 0.06% | -0.06% | -0.00% | -0.00% |
| W4_TO_IMB_OI | VALIDATION | 4 | 3509 | 0.0020 | 0.0023 | -0.04% | 0.04% | 0.00% | 0.00% |
| W4_TO_IMB_OI | VALIDATION | 8 | 3509 | -0.0007 | -0.0016 | -0.07% | 0.08% | 0.01% | 0.01% |
| W4_TO_IMB_OI | VALIDATION | 24 | 3509 | 0.0033 | -0.0064 | -0.19% | 0.18% | -0.01% | -0.01% |

## 6. OI research

| Hypothesis | Phase | H | n | CS IC | TS IC | LONG mean | SHORT mean | Spread | Exec spread |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| W4_OI_CONFIRM | IS | 4 | 10323 | -0.0051 | 0.0110 | 0.04% | -0.01% | 0.03% | 0.03% |
| W4_OI_CONFIRM | IS | 8 | 10323 | -0.0084 | 0.0140 | 0.07% | -0.03% | 0.04% | 0.04% |
| W4_OI_CONFIRM | IS | 24 | 10323 | -0.0027 | 0.0049 | 0.19% | -0.11% | 0.07% | 0.07% |
| W4_OI_CONFIRM | OOS | 4 | 3505 | -0.0041 | -0.0036 | 0.02% | -0.01% | 0.01% | 0.02% |
| W4_OI_CONFIRM | OOS | 8 | 3501 | -0.0047 | -0.0135 | 0.04% | -0.01% | 0.02% | 0.03% |
| W4_OI_CONFIRM | OOS | 24 | 3485 | -0.0064 | -0.0333 | 0.07% | -0.06% | 0.01% | 0.01% |
| W4_OI_CONFIRM | VALIDATION | 4 | 3509 | -0.0392 | -0.0257 | -0.05% | 0.02% | -0.03% | -0.03% |
| W4_OI_CONFIRM | VALIDATION | 8 | 3509 | -0.0420 | -0.0317 | -0.11% | 0.04% | -0.06% | -0.06% |
| W4_OI_CONFIRM | VALIDATION | 24 | 3509 | -0.0442 | -0.0692 | -0.26% | 0.09% | -0.17% | -0.17% |
| W4_OI_D1 | IS | 4 | 10323 | -0.0011 | -0.0213 | 0.03% | -0.02% | 0.00% | 0.00% |
| W4_OI_D1 | IS | 8 | 10323 | 0.0070 | -0.0145 | 0.06% | -0.03% | 0.02% | 0.02% |
| W4_OI_D1 | IS | 24 | 10323 | 0.0064 | -0.0040 | 0.15% | -0.12% | 0.03% | 0.03% |
| W4_OI_D1 | OOS | 4 | 3505 | 0.0040 | 0.0082 | 0.02% | -0.01% | 0.01% | 0.01% |
| W4_OI_D1 | OOS | 8 | 3501 | 0.0105 | 0.0168 | 0.03% | -0.03% | 0.00% | 0.00% |
| W4_OI_D1 | OOS | 24 | 3485 | 0.0106 | 0.0159 | 0.07% | -0.07% | 0.01% | 0.01% |
| W4_OI_D1 | VALIDATION | 4 | 3509 | -0.0040 | -0.0077 | -0.03% | 0.03% | -0.00% | -0.00% |
| W4_OI_D1 | VALIDATION | 8 | 3509 | 0.0031 | -0.0107 | -0.07% | 0.07% | 0.01% | 0.01% |
| W4_OI_D1 | VALIDATION | 24 | 3509 | -0.0067 | -0.0075 | -0.17% | 0.17% | -0.00% | -0.00% |
| W4_OI_D24 | IS | 4 | 10323 | -0.0013 | -0.0076 | 0.03% | -0.02% | 0.02% | 0.02% |
| W4_OI_D24 | IS | 8 | 10323 | -0.0045 | -0.0062 | 0.07% | -0.04% | 0.03% | 0.03% |
| W4_OI_D24 | IS | 24 | 10323 | 0.0005 | -0.0067 | 0.19% | -0.13% | 0.06% | 0.06% |
| W4_OI_D24 | OOS | 4 | 3505 | 0.0177 | 0.0134 | 0.02% | 0.01% | 0.03% | 0.03% |
| W4_OI_D24 | OOS | 8 | 3501 | 0.0241 | 0.0227 | 0.05% | 0.01% | 0.06% | 0.06% |
| W4_OI_D24 | OOS | 24 | 3485 | 0.0331 | 0.0193 | 0.13% | 0.03% | 0.16% | 0.16% |
| W4_OI_D24 | VALIDATION | 4 | 3509 | 0.0013 | -0.0229 | -0.04% | 0.03% | -0.01% | -0.01% |
| W4_OI_D24 | VALIDATION | 8 | 3509 | -0.0021 | -0.0217 | -0.08% | 0.07% | -0.01% | -0.01% |
| W4_OI_D24 | VALIDATION | 24 | 3509 | -0.0076 | -0.0270 | -0.18% | 0.14% | -0.04% | -0.04% |
| W4_OI_SHOCK | IS | 4 | 10323 | 0.0013 | -0.0209 | 0.03% | -0.03% | 0.00% | 0.00% |
| W4_OI_SHOCK | IS | 8 | 10323 | 0.0081 | -0.0156 | 0.06% | -0.04% | 0.02% | 0.02% |
| W4_OI_SHOCK | IS | 24 | 10323 | 0.0062 | -0.0056 | 0.16% | -0.12% | 0.03% | 0.03% |
| W4_OI_SHOCK | OOS | 4 | 3505 | 0.0020 | 0.0092 | 0.02% | -0.02% | 0.00% | 0.00% |
| W4_OI_SHOCK | OOS | 8 | 3501 | 0.0069 | 0.0162 | 0.03% | -0.03% | -0.01% | -0.01% |
| W4_OI_SHOCK | OOS | 24 | 3485 | 0.0061 | 0.0138 | 0.07% | -0.08% | -0.01% | -0.01% |
| W4_OI_SHOCK | VALIDATION | 4 | 3509 | -0.0050 | -0.0073 | -0.04% | 0.03% | -0.01% | -0.00% |
| W4_OI_SHOCK | VALIDATION | 8 | 3509 | 0.0022 | -0.0104 | -0.07% | 0.07% | 0.00% | 0.00% |
| W4_OI_SHOCK | VALIDATION | 24 | 3509 | -0.0077 | -0.0090 | -0.17% | 0.17% | -0.00% | -0.00% |

## 7. Liquidation research

**DATA_UNAVAILABLE.** USD-M `data/futures/um/daily/liquidationSnapshot/` currently lists no objects. Binance public-data issue #361: *this data is no longer provided*. COIN-M snapshots are a different contract and were not substituted. REST force-order endpoints are recent-only and were not treated as a 2-year history.

## 8. Funding + positioning research

| Hypothesis | Phase | H | n | CS IC | TS IC | LONG mean | SHORT mean | Spread | Exec spread |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| W4_FO_CROWD_REV | IS | 4 | 1340 | 0.0070 | -0.0052 | 0.01% | -0.03% | -0.02% | -0.02% |
| W4_FO_CROWD_REV | IS | 8 | 1340 | 0.0057 | -0.0062 | 0.03% | -0.06% | -0.03% | -0.03% |
| W4_FO_CROWD_REV | IS | 24 | 1340 | 0.0199 | -0.0084 | 0.10% | -0.15% | -0.05% | -0.05% |
| W4_FO_CROWD_REV | OOS | 4 | 403 | 0.0297 | -0.0164 | 0.00% | -0.06% | -0.06% | -0.06% |
| W4_FO_CROWD_REV | OOS | 8 | 399 | 0.0324 | -0.0120 | 0.04% | -0.10% | -0.06% | -0.06% |
| W4_FO_CROWD_REV | OOS | 24 | 383 | -0.0153 | 0.0137 | 0.13% | -0.20% | -0.06% | -0.06% |
| W4_FO_CROWD_REV | VALIDATION | 4 | 493 | 0.0862 | 0.0496 | -0.00% | 0.06% | 0.06% | 0.06% |
| W4_FO_CROWD_REV | VALIDATION | 8 | 493 | 0.1114 | 0.0845 | -0.00% | 0.11% | 0.11% | 0.11% |
| W4_FO_CROWD_REV | VALIDATION | 24 | 493 | 0.1036 | 0.1074 | -0.02% | 0.23% | 0.20% | 0.20% |
| W4_M_LS_REV | IS | 4 | 10325 | 0.0187 | 0.0068 | 0.04% | -0.01% | 0.04% | 0.04% |
| W4_M_LS_REV | IS | 8 | 10325 | 0.0230 | 0.0068 | 0.08% | -0.02% | 0.07% | 0.07% |
| W4_M_LS_REV | IS | 24 | 10325 | 0.0355 | 0.0134 | 0.22% | -0.02% | 0.20% | 0.20% |
| W4_M_LS_REV | OOS | 4 | 3505 | 0.0136 | 0.0169 | 0.03% | -0.01% | 0.02% | 0.02% |
| W4_M_LS_REV | OOS | 8 | 3501 | 0.0226 | 0.0238 | 0.04% | -0.01% | 0.03% | 0.03% |
| W4_M_LS_REV | OOS | 24 | 3485 | 0.0228 | 0.0444 | 0.08% | -0.05% | 0.04% | 0.04% |
| W4_M_LS_REV | VALIDATION | 4 | 3509 | -0.0044 | 0.0020 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_M_LS_REV | VALIDATION | 8 | 3509 | -0.0009 | 0.0050 | -0.06% | 0.07% | 0.02% | 0.02% |
| W4_M_LS_REV | VALIDATION | 24 | 3509 | 0.0059 | 0.0087 | -0.14% | 0.21% | 0.07% | 0.07% |
| W4_M_TOP_LS_REV | IS | 4 | 10325 | 0.0101 | 0.0183 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_M_TOP_LS_REV | IS | 8 | 10325 | 0.0132 | 0.0251 | 0.06% | -0.03% | 0.03% | 0.03% |
| W4_M_TOP_LS_REV | IS | 24 | 10325 | 0.0154 | 0.0397 | 0.17% | -0.09% | 0.07% | 0.07% |
| W4_M_TOP_LS_REV | OOS | 4 | 3505 | 0.0111 | 0.0036 | 0.02% | 0.00% | 0.02% | 0.02% |
| W4_M_TOP_LS_REV | OOS | 8 | 3501 | 0.0169 | 0.0154 | 0.03% | 0.00% | 0.04% | 0.04% |
| W4_M_TOP_LS_REV | OOS | 24 | 3485 | 0.0183 | 0.0367 | 0.09% | 0.00% | 0.09% | 0.09% |
| W4_M_TOP_LS_REV | VALIDATION | 4 | 3509 | 0.0016 | 0.0234 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_M_TOP_LS_REV | VALIDATION | 8 | 3509 | 0.0018 | 0.0322 | -0.07% | 0.08% | 0.01% | 0.01% |
| W4_M_TOP_LS_REV | VALIDATION | 24 | 3509 | 0.0044 | 0.0675 | -0.16% | 0.19% | 0.03% | 0.03% |

## 9. Full-universe cross-sectional research

OHLCV-only presence ranks on the existing 1h universe cache (no taker re-download, no inner join). A coin is ranked only if it has bars at t, t−24, and t+h. Deciles are not claimed; terciles among coins present at t.

| Hypothesis | Phase | H | n | CS IC | LONG mean | SHORT mean | Spread |
|---|---|---:|---:|---:|---:|---:|---:|
| W4_U_REL24 | IS | 4 | 10327 | -0.0367 | -0.01% | 0.01% | -0.00% |
| W4_U_REL24 | IS | 24 | 10327 | -0.0418 | -0.11% | 0.04% | -0.07% |
| W4_U_REL24 | OOS | 4 | 3505 | -0.0388 | 0.01% | -0.03% | -0.02% |
| W4_U_REL24 | OOS | 24 | 3485 | -0.0377 | 0.13% | -0.07% | 0.05% |
| W4_U_REL24 | VALIDATION | 4 | 3509 | -0.0444 | -0.02% | -0.01% | -0.03% |
| W4_U_REL24 | VALIDATION | 24 | 3509 | -0.0491 | -0.07% | -0.06% | -0.14% |
| W4_U_VOLSHOCK | IS | 4 | 10327 | -0.0088 | -0.01% | 0.01% | -0.00% |
| W4_U_VOLSHOCK | IS | 24 | 10327 | -0.0040 | -0.07% | 0.05% | -0.02% |
| W4_U_VOLSHOCK | OOS | 4 | 3505 | -0.0150 | 0.01% | -0.03% | -0.02% |
| W4_U_VOLSHOCK | OOS | 24 | 3485 | -0.0147 | 0.06% | -0.15% | -0.09% |
| W4_U_VOLSHOCK | VALIDATION | 4 | 3509 | -0.0063 | 0.00% | 0.01% | 0.01% |
| W4_U_VOLSHOCK | VALIDATION | 24 | 3509 | -0.0012 | -0.00% | 0.01% | 0.01% |

## 10. Incremental information-value analysis

Baseline = `W4_BASE_REL24` (OHLCV relative-to-BTC). `IcVsBaseline` is CS IC at 24h minus that baseline. A feature that only lifts IS is not useful.

| Hypothesis | Phase | IC 24h | Spread | Exec spread | IC vs OHLCV baseline |
|---|---|---:|---:|---:|---:|
| W4_BASE_REL24 | IS | -0.0309 | 0.05% | 0.05% | 0.0000 |
| W4_BASE_REL24 | OOS | 0.0234 | 0.20% | 0.20% | 0.0000 |
| W4_BASE_REL24 | VALIDATION | -0.0704 | -0.18% | -0.18% | 0.0000 |
| W4_FO_CROWD_REV | IS | 0.0199 | -0.05% | -0.05% | 0.0508 |
| W4_FO_CROWD_REV | OOS | -0.0153 | -0.06% | -0.06% | -0.0387 |
| W4_FO_CROWD_REV | VALIDATION | 0.1036 | 0.20% | 0.20% | 0.1741 |
| W4_M_LS_REV | IS | 0.0355 | 0.20% | 0.20% | 0.0664 |
| W4_M_LS_REV | OOS | 0.0228 | 0.04% | 0.04% | -0.0006 |
| W4_M_LS_REV | VALIDATION | 0.0059 | 0.07% | 0.07% | 0.0763 |
| W4_M_TAKER_LS | IS | -0.0031 | 0.00% | 0.00% | 0.0279 |
| W4_M_TAKER_LS | OOS | -0.0126 | -0.06% | -0.06% | -0.0360 |
| W4_M_TAKER_LS | VALIDATION | -0.0070 | -0.03% | -0.03% | 0.0634 |
| W4_M_TOP_LS_REV | IS | 0.0154 | 0.07% | 0.07% | 0.0463 |
| W4_M_TOP_LS_REV | OOS | 0.0183 | 0.09% | 0.09% | -0.0051 |
| W4_M_TOP_LS_REV | VALIDATION | 0.0044 | 0.03% | 0.03% | 0.0748 |
| W4_OI_CONFIRM | IS | -0.0027 | 0.07% | 0.07% | 0.0282 |
| W4_OI_CONFIRM | OOS | -0.0064 | 0.01% | 0.01% | -0.0298 |
| W4_OI_CONFIRM | VALIDATION | -0.0442 | -0.17% | -0.17% | 0.0263 |
| W4_OI_D1 | IS | 0.0064 | 0.03% | 0.03% | 0.0374 |
| W4_OI_D1 | OOS | 0.0106 | 0.01% | 0.01% | -0.0128 |
| W4_OI_D1 | VALIDATION | -0.0067 | -0.00% | -0.00% | 0.0637 |
| W4_OI_D24 | IS | 0.0005 | 0.06% | 0.06% | 0.0314 |
| W4_OI_D24 | OOS | 0.0331 | 0.16% | 0.16% | 0.0097 |
| W4_OI_D24 | VALIDATION | -0.0076 | -0.04% | -0.04% | 0.0629 |
| W4_OI_SHOCK | IS | 0.0062 | 0.03% | 0.03% | 0.0371 |
| W4_OI_SHOCK | OOS | 0.0061 | -0.01% | -0.01% | -0.0173 |
| W4_OI_SHOCK | VALIDATION | -0.0077 | -0.00% | -0.00% | 0.0627 |
| W4_T1_IMB_CONT | IS | 0.0038 | 0.00% | 0.00% | 0.0347 |
| W4_T1_IMB_CONT | OOS | -0.0047 | 0.01% | 0.01% | -0.0281 |
| W4_T1_IMB_CONT | VALIDATION | -0.0139 | -0.03% | -0.03% | 0.0565 |
| W4_T2_IMB_REV | IS | -0.0038 | -0.02% | -0.02% | 0.0271 |
| W4_T2_IMB_REV | OOS | 0.0047 | 0.03% | 0.03% | -0.0187 |
| W4_T2_IMB_REV | VALIDATION | 0.0139 | 0.04% | 0.04% | 0.0843 |
| W4_T3_DIMB | IS | 0.0019 | 0.00% | 0.00% | 0.0328 |
| W4_T3_DIMB | OOS | -0.0021 | 0.01% | 0.01% | -0.0255 |
| W4_T3_DIMB | VALIDATION | -0.0018 | -0.00% | -0.00% | 0.0687 |
| W4_T4_FLOW_DIV | IS | 0.0058 | 0.03% | 0.03% | 0.0367 |
| W4_T4_FLOW_DIV | OOS | -0.0012 | -0.00% | -0.00% | -0.0246 |
| W4_T4_FLOW_DIV | VALIDATION | 0.0016 | 0.02% | 0.02% | 0.0720 |
| W4_T5_IMB_VOL | IS | 0.0030 | 0.01% | 0.01% | 0.0339 |
| W4_T5_IMB_VOL | OOS | -0.0002 | 0.00% | 0.00% | -0.0236 |
| W4_T5_IMB_VOL | VALIDATION | -0.0129 | -0.02% | -0.02% | 0.0576 |
| W4_T6_IMB_REL | IS | 0.0080 | 0.01% | 0.01% | 0.0389 |
| W4_T6_IMB_REL | OOS | 0.0064 | 0.02% | 0.02% | -0.0170 |
| W4_T6_IMB_REL | VALIDATION | 0.0016 | 0.03% | 0.04% | 0.0721 |
| W4_TO_IMB_OI | IS | -0.0073 | 0.01% | 0.01% | 0.0236 |
| W4_TO_IMB_OI | OOS | -0.0015 | -0.00% | -0.00% | -0.0249 |
| W4_TO_IMB_OI | VALIDATION | 0.0033 | -0.01% | -0.01% | 0.0738 |

## 11. Forward-return analysis

Close-to-close CS IC and tercile spread at 1/2/4/8/12/24h. LONG = top tercile of the signed signal. SHORT = −bottom tercile. MAE/MFE are path min/max vs signal close for the top/bottom tercile at 24h.

| Hypothesis | Phase | H | n | CS IC | TS IC | LONG mean | SHORT mean | Spread | Exec spread |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| W4_BASE_REL24 | IS | 1 | 10327 | -0.0238 | -0.0232 | 0.01% | -0.01% | 0.00% | 0.00% |
| W4_BASE_REL24 | IS | 4 | 10327 | -0.0239 | -0.0243 | 0.04% | -0.02% | 0.02% | 0.02% |
| W4_BASE_REL24 | IS | 8 | 10327 | -0.0236 | -0.0255 | 0.07% | -0.04% | 0.03% | 0.03% |
| W4_BASE_REL24 | IS | 24 | 10327 | -0.0309 | -0.0133 | 0.16% | -0.11% | 0.05% | 0.05% |
| W4_BASE_REL24 | OOS | 1 | 3508 | -0.0090 | -0.0147 | 0.01% | 0.00% | 0.01% | 0.01% |
| W4_BASE_REL24 | OOS | 4 | 3505 | -0.0069 | -0.0267 | 0.03% | 0.00% | 0.04% | 0.04% |
| W4_BASE_REL24 | OOS | 8 | 3501 | 0.0074 | -0.0312 | 0.06% | 0.01% | 0.08% | 0.08% |
| W4_BASE_REL24 | OOS | 24 | 3485 | 0.0234 | -0.0349 | 0.16% | 0.04% | 0.20% | 0.20% |
| W4_BASE_REL24 | VALIDATION | 1 | 3509 | -0.0386 | -0.0190 | -0.02% | -0.00% | -0.02% | -0.02% |
| W4_BASE_REL24 | VALIDATION | 4 | 3509 | -0.0626 | -0.0307 | -0.06% | 0.00% | -0.06% | -0.06% |
| W4_BASE_REL24 | VALIDATION | 8 | 3509 | -0.0729 | -0.0416 | -0.11% | 0.02% | -0.09% | -0.09% |
| W4_BASE_REL24 | VALIDATION | 24 | 3509 | -0.0704 | -0.0538 | -0.27% | 0.09% | -0.18% | -0.18% |
| W4_FO_CROWD_REV | IS | 1 | 1340 | 0.0203 | -0.0002 | -0.00% | -0.01% | -0.01% | -0.01% |
| W4_FO_CROWD_REV | IS | 4 | 1340 | 0.0070 | -0.0052 | 0.01% | -0.03% | -0.02% | -0.02% |
| W4_FO_CROWD_REV | IS | 8 | 1340 | 0.0057 | -0.0062 | 0.03% | -0.06% | -0.03% | -0.03% |
| W4_FO_CROWD_REV | IS | 24 | 1340 | 0.0199 | -0.0084 | 0.10% | -0.15% | -0.05% | -0.05% |
| W4_FO_CROWD_REV | OOS | 1 | 406 | 0.0356 | -0.0028 | 0.01% | -0.01% | -0.01% | -0.01% |
| W4_FO_CROWD_REV | OOS | 4 | 403 | 0.0297 | -0.0164 | 0.00% | -0.06% | -0.06% | -0.06% |
| W4_FO_CROWD_REV | OOS | 8 | 399 | 0.0324 | -0.0120 | 0.04% | -0.10% | -0.06% | -0.06% |
| W4_FO_CROWD_REV | OOS | 24 | 383 | -0.0153 | 0.0137 | 0.13% | -0.20% | -0.06% | -0.06% |
| W4_FO_CROWD_REV | VALIDATION | 1 | 493 | 0.0542 | 0.0142 | -0.00% | 0.01% | 0.01% | 0.01% |
| W4_FO_CROWD_REV | VALIDATION | 4 | 493 | 0.0862 | 0.0496 | -0.00% | 0.06% | 0.06% | 0.06% |
| W4_FO_CROWD_REV | VALIDATION | 8 | 493 | 0.1114 | 0.0845 | -0.00% | 0.11% | 0.11% | 0.11% |
| W4_FO_CROWD_REV | VALIDATION | 24 | 493 | 0.1036 | 0.1074 | -0.02% | 0.23% | 0.20% | 0.20% |
| W4_M_LS_REV | IS | 1 | 10325 | 0.0101 | -0.0009 | 0.01% | -0.00% | 0.01% | 0.01% |
| W4_M_LS_REV | IS | 4 | 10325 | 0.0187 | 0.0068 | 0.04% | -0.01% | 0.04% | 0.04% |
| W4_M_LS_REV | IS | 8 | 10325 | 0.0230 | 0.0068 | 0.08% | -0.02% | 0.07% | 0.07% |
| W4_M_LS_REV | IS | 24 | 10325 | 0.0355 | 0.0134 | 0.22% | -0.02% | 0.20% | 0.20% |
| W4_M_LS_REV | OOS | 1 | 3508 | 0.0025 | 0.0011 | 0.01% | -0.00% | 0.00% | 0.00% |
| W4_M_LS_REV | OOS | 4 | 3505 | 0.0136 | 0.0169 | 0.03% | -0.01% | 0.02% | 0.02% |
| W4_M_LS_REV | OOS | 8 | 3501 | 0.0226 | 0.0238 | 0.04% | -0.01% | 0.03% | 0.03% |
| W4_M_LS_REV | OOS | 24 | 3485 | 0.0228 | 0.0444 | 0.08% | -0.05% | 0.04% | 0.04% |
| W4_M_LS_REV | VALIDATION | 1 | 3509 | 0.0000 | 0.0032 | -0.01% | 0.01% | 0.00% | 0.00% |
| W4_M_LS_REV | VALIDATION | 4 | 3509 | -0.0044 | 0.0020 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_M_LS_REV | VALIDATION | 8 | 3509 | -0.0009 | 0.0050 | -0.06% | 0.07% | 0.02% | 0.02% |
| W4_M_LS_REV | VALIDATION | 24 | 3509 | 0.0059 | 0.0087 | -0.14% | 0.21% | 0.07% | 0.07% |
| W4_M_TAKER_LS | IS | 1 | 10327 | -0.0007 | -0.0167 | 0.01% | -0.00% | 0.01% | 0.01% |
| W4_M_TAKER_LS | IS | 4 | 10327 | -0.0010 | -0.0003 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_M_TAKER_LS | IS | 8 | 10327 | -0.0037 | -0.0002 | 0.05% | -0.06% | -0.01% | -0.01% |
| W4_M_TAKER_LS | IS | 24 | 10327 | -0.0031 | 0.0079 | 0.14% | -0.14% | 0.00% | 0.00% |
| W4_M_TAKER_LS | OOS | 1 | 3508 | -0.0183 | 0.0001 | -0.00% | -0.01% | -0.01% | -0.01% |
| W4_M_TAKER_LS | OOS | 4 | 3505 | -0.0152 | -0.0066 | 0.00% | -0.03% | -0.02% | -0.02% |
| W4_M_TAKER_LS | OOS | 8 | 3501 | -0.0178 | -0.0106 | 0.01% | -0.04% | -0.03% | -0.03% |
| W4_M_TAKER_LS | OOS | 24 | 3485 | -0.0126 | -0.0079 | 0.04% | -0.10% | -0.06% | -0.06% |
| W4_M_TAKER_LS | VALIDATION | 1 | 3509 | 0.0004 | 0.0077 | -0.01% | 0.01% | 0.01% | 0.01% |
| W4_M_TAKER_LS | VALIDATION | 4 | 3509 | -0.0080 | -0.0015 | -0.03% | 0.04% | 0.00% | 0.00% |
| W4_M_TAKER_LS | VALIDATION | 8 | 3509 | -0.0114 | 0.0019 | -0.07% | 0.07% | -0.00% | -0.00% |
| W4_M_TAKER_LS | VALIDATION | 24 | 3509 | -0.0070 | 0.0031 | -0.18% | 0.15% | -0.03% | -0.03% |
| W4_M_TOP_LS_REV | IS | 1 | 10325 | 0.0060 | 0.0033 | 0.01% | -0.00% | 0.00% | 0.00% |
| W4_M_TOP_LS_REV | IS | 4 | 10325 | 0.0101 | 0.0183 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_M_TOP_LS_REV | IS | 8 | 10325 | 0.0132 | 0.0251 | 0.06% | -0.03% | 0.03% | 0.03% |
| W4_M_TOP_LS_REV | IS | 24 | 10325 | 0.0154 | 0.0397 | 0.17% | -0.09% | 0.07% | 0.07% |
| W4_M_TOP_LS_REV | OOS | 1 | 3508 | 0.0047 | 0.0014 | 0.00% | -0.00% | 0.00% | 0.00% |
| W4_M_TOP_LS_REV | OOS | 4 | 3505 | 0.0111 | 0.0036 | 0.02% | 0.00% | 0.02% | 0.02% |
| W4_M_TOP_LS_REV | OOS | 8 | 3501 | 0.0169 | 0.0154 | 0.03% | 0.00% | 0.04% | 0.04% |
| W4_M_TOP_LS_REV | OOS | 24 | 3485 | 0.0183 | 0.0367 | 0.09% | 0.00% | 0.09% | 0.09% |
| W4_M_TOP_LS_REV | VALIDATION | 1 | 3509 | -0.0005 | 0.0120 | -0.01% | 0.01% | 0.00% | 0.00% |
| W4_M_TOP_LS_REV | VALIDATION | 4 | 3509 | 0.0016 | 0.0234 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_M_TOP_LS_REV | VALIDATION | 8 | 3509 | 0.0018 | 0.0322 | -0.07% | 0.08% | 0.01% | 0.01% |
| W4_M_TOP_LS_REV | VALIDATION | 24 | 3509 | 0.0044 | 0.0675 | -0.16% | 0.19% | 0.03% | 0.03% |
| W4_OI_CONFIRM | IS | 1 | 10323 | -0.0007 | 0.0062 | 0.01% | -0.00% | 0.01% | 0.01% |
| W4_OI_CONFIRM | IS | 4 | 10323 | -0.0051 | 0.0110 | 0.04% | -0.01% | 0.03% | 0.03% |
| W4_OI_CONFIRM | IS | 8 | 10323 | -0.0084 | 0.0140 | 0.07% | -0.03% | 0.04% | 0.04% |
| W4_OI_CONFIRM | IS | 24 | 10323 | -0.0027 | 0.0049 | 0.19% | -0.11% | 0.07% | 0.07% |
| W4_OI_CONFIRM | OOS | 1 | 3508 | -0.0049 | -0.0021 | 0.01% | -0.00% | 0.00% | 0.00% |
| W4_OI_CONFIRM | OOS | 4 | 3505 | -0.0041 | -0.0036 | 0.02% | -0.01% | 0.01% | 0.02% |
| W4_OI_CONFIRM | OOS | 8 | 3501 | -0.0047 | -0.0135 | 0.04% | -0.01% | 0.02% | 0.03% |
| W4_OI_CONFIRM | OOS | 24 | 3485 | -0.0064 | -0.0333 | 0.07% | -0.06% | 0.01% | 0.01% |
| W4_OI_CONFIRM | VALIDATION | 1 | 3509 | -0.0097 | -0.0073 | -0.01% | 0.01% | -0.01% | -0.01% |
| W4_OI_CONFIRM | VALIDATION | 4 | 3509 | -0.0392 | -0.0257 | -0.05% | 0.02% | -0.03% | -0.03% |
| W4_OI_CONFIRM | VALIDATION | 8 | 3509 | -0.0420 | -0.0317 | -0.11% | 0.04% | -0.06% | -0.06% |
| W4_OI_CONFIRM | VALIDATION | 24 | 3509 | -0.0442 | -0.0692 | -0.26% | 0.09% | -0.17% | -0.17% |
| W4_OI_D1 | IS | 1 | 10323 | -0.0066 | -0.0169 | 0.01% | -0.01% | -0.00% | -0.00% |
| W4_OI_D1 | IS | 4 | 10323 | -0.0011 | -0.0213 | 0.03% | -0.02% | 0.00% | 0.00% |
| W4_OI_D1 | IS | 8 | 10323 | 0.0070 | -0.0145 | 0.06% | -0.03% | 0.02% | 0.02% |
| W4_OI_D1 | IS | 24 | 10323 | 0.0064 | -0.0040 | 0.15% | -0.12% | 0.03% | 0.03% |
| W4_OI_D1 | OOS | 1 | 3508 | -0.0103 | -0.0069 | 0.00% | -0.01% | -0.00% | -0.00% |
| W4_OI_D1 | OOS | 4 | 3505 | 0.0040 | 0.0082 | 0.02% | -0.01% | 0.01% | 0.01% |
| W4_OI_D1 | OOS | 8 | 3501 | 0.0105 | 0.0168 | 0.03% | -0.03% | 0.00% | 0.00% |
| W4_OI_D1 | OOS | 24 | 3485 | 0.0106 | 0.0159 | 0.07% | -0.07% | 0.01% | 0.01% |
| W4_OI_D1 | VALIDATION | 1 | 3509 | -0.0044 | -0.0117 | -0.01% | 0.01% | -0.00% | -0.00% |
| W4_OI_D1 | VALIDATION | 4 | 3509 | -0.0040 | -0.0077 | -0.03% | 0.03% | -0.00% | -0.00% |
| W4_OI_D1 | VALIDATION | 8 | 3509 | 0.0031 | -0.0107 | -0.07% | 0.07% | 0.01% | 0.01% |
| W4_OI_D1 | VALIDATION | 24 | 3509 | -0.0067 | -0.0075 | -0.17% | 0.17% | -0.00% | -0.00% |
| W4_OI_D24 | IS | 1 | 10323 | -0.0051 | -0.0076 | 0.01% | -0.00% | 0.01% | 0.01% |
| W4_OI_D24 | IS | 4 | 10323 | -0.0013 | -0.0076 | 0.03% | -0.02% | 0.02% | 0.02% |
| W4_OI_D24 | IS | 8 | 10323 | -0.0045 | -0.0062 | 0.07% | -0.04% | 0.03% | 0.03% |
| W4_OI_D24 | IS | 24 | 10323 | 0.0005 | -0.0067 | 0.19% | -0.13% | 0.06% | 0.06% |
| W4_OI_D24 | OOS | 1 | 3508 | 0.0062 | 0.0110 | 0.01% | 0.00% | 0.01% | 0.01% |
| W4_OI_D24 | OOS | 4 | 3505 | 0.0177 | 0.0134 | 0.02% | 0.01% | 0.03% | 0.03% |
| W4_OI_D24 | OOS | 8 | 3501 | 0.0241 | 0.0227 | 0.05% | 0.01% | 0.06% | 0.06% |
| W4_OI_D24 | OOS | 24 | 3485 | 0.0331 | 0.0193 | 0.13% | 0.03% | 0.16% | 0.16% |
| W4_OI_D24 | VALIDATION | 1 | 3509 | -0.0013 | -0.0138 | -0.01% | 0.01% | -0.00% | -0.00% |
| W4_OI_D24 | VALIDATION | 4 | 3509 | 0.0013 | -0.0229 | -0.04% | 0.03% | -0.01% | -0.01% |
| W4_OI_D24 | VALIDATION | 8 | 3509 | -0.0021 | -0.0217 | -0.08% | 0.07% | -0.01% | -0.01% |
| W4_OI_D24 | VALIDATION | 24 | 3509 | -0.0076 | -0.0270 | -0.18% | 0.14% | -0.04% | -0.04% |
| W4_OI_SHOCK | IS | 1 | 10323 | -0.0042 | -0.0166 | 0.01% | -0.01% | 0.00% | 0.00% |
| W4_OI_SHOCK | IS | 4 | 10323 | 0.0013 | -0.0209 | 0.03% | -0.03% | 0.00% | 0.00% |
| W4_OI_SHOCK | IS | 8 | 10323 | 0.0081 | -0.0156 | 0.06% | -0.04% | 0.02% | 0.02% |
| W4_OI_SHOCK | IS | 24 | 10323 | 0.0062 | -0.0056 | 0.16% | -0.12% | 0.03% | 0.03% |
| W4_OI_SHOCK | OOS | 1 | 3508 | -0.0081 | -0.0067 | 0.00% | -0.01% | -0.00% | -0.00% |
| W4_OI_SHOCK | OOS | 4 | 3505 | 0.0020 | 0.0092 | 0.02% | -0.02% | 0.00% | 0.00% |
| W4_OI_SHOCK | OOS | 8 | 3501 | 0.0069 | 0.0162 | 0.03% | -0.03% | -0.01% | -0.01% |
| W4_OI_SHOCK | OOS | 24 | 3485 | 0.0061 | 0.0138 | 0.07% | -0.08% | -0.01% | -0.01% |
| W4_OI_SHOCK | VALIDATION | 1 | 3509 | -0.0006 | -0.0110 | -0.01% | 0.01% | -0.00% | -0.00% |
| W4_OI_SHOCK | VALIDATION | 4 | 3509 | -0.0050 | -0.0073 | -0.04% | 0.03% | -0.01% | -0.00% |
| W4_OI_SHOCK | VALIDATION | 8 | 3509 | 0.0022 | -0.0104 | -0.07% | 0.07% | 0.00% | 0.00% |
| W4_OI_SHOCK | VALIDATION | 24 | 3509 | -0.0077 | -0.0090 | -0.17% | 0.17% | -0.00% | -0.00% |
| W4_T1_IMB_CONT | IS | 1 | 10327 | -0.0041 | -0.0273 | 0.01% | -0.01% | 0.00% | 0.00% |
| W4_T1_IMB_CONT | IS | 4 | 10327 | 0.0006 | -0.0138 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_T1_IMB_CONT | IS | 8 | 10327 | 0.0038 | -0.0014 | 0.06% | -0.05% | 0.01% | 0.01% |
| W4_T1_IMB_CONT | IS | 24 | 10327 | 0.0038 | -0.0038 | 0.15% | -0.15% | 0.00% | 0.00% |
| W4_T1_IMB_CONT | OOS | 1 | 3508 | -0.0191 | -0.0252 | 0.00% | -0.01% | -0.01% | -0.01% |
| W4_T1_IMB_CONT | OOS | 4 | 3505 | -0.0080 | -0.0245 | 0.02% | -0.01% | 0.00% | 0.00% |
| W4_T1_IMB_CONT | OOS | 8 | 3501 | -0.0057 | -0.0057 | 0.02% | -0.02% | 0.00% | 0.00% |
| W4_T1_IMB_CONT | OOS | 24 | 3485 | -0.0047 | 0.0074 | 0.05% | -0.05% | 0.01% | 0.01% |
| W4_T1_IMB_CONT | VALIDATION | 1 | 3509 | -0.0064 | -0.0247 | -0.01% | 0.01% | 0.00% | 0.00% |
| W4_T1_IMB_CONT | VALIDATION | 4 | 3509 | -0.0114 | -0.0036 | -0.04% | 0.03% | -0.01% | -0.01% |
| W4_T1_IMB_CONT | VALIDATION | 8 | 3509 | -0.0178 | -0.0113 | -0.07% | 0.06% | -0.01% | -0.01% |
| W4_T1_IMB_CONT | VALIDATION | 24 | 3509 | -0.0139 | -0.0027 | -0.18% | 0.15% | -0.03% | -0.03% |
| W4_T2_IMB_REV | IS | 1 | 10327 | 0.0041 | 0.0273 | 0.00% | -0.01% | -0.01% | -0.01% |
| W4_T2_IMB_REV | IS | 4 | 10327 | -0.0006 | 0.0138 | 0.02% | -0.03% | -0.01% | -0.01% |
| W4_T2_IMB_REV | IS | 8 | 10327 | -0.0038 | 0.0014 | 0.05% | -0.06% | -0.01% | -0.01% |
| W4_T2_IMB_REV | IS | 24 | 10327 | -0.0038 | 0.0038 | 0.13% | -0.15% | -0.02% | -0.02% |
| W4_T2_IMB_REV | OOS | 1 | 3508 | 0.0191 | 0.0252 | 0.01% | 0.00% | 0.01% | 0.01% |
| W4_T2_IMB_REV | OOS | 4 | 3505 | 0.0080 | 0.0245 | 0.01% | -0.02% | -0.00% | -0.00% |
| W4_T2_IMB_REV | OOS | 8 | 3501 | 0.0057 | 0.0057 | 0.02% | -0.02% | 0.00% | 0.00% |
| W4_T2_IMB_REV | OOS | 24 | 3485 | 0.0047 | -0.0074 | 0.06% | -0.03% | 0.03% | 0.03% |
| W4_T2_IMB_REV | VALIDATION | 1 | 3509 | 0.0064 | 0.0247 | -0.01% | 0.01% | 0.00% | 0.00% |
| W4_T2_IMB_REV | VALIDATION | 4 | 3509 | 0.0114 | 0.0036 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_T2_IMB_REV | VALIDATION | 8 | 3509 | 0.0178 | 0.0113 | -0.06% | 0.07% | 0.02% | 0.02% |
| W4_T2_IMB_REV | VALIDATION | 24 | 3509 | 0.0139 | 0.0027 | -0.14% | 0.18% | 0.04% | 0.04% |
| W4_T3_DIMB | IS | 1 | 10327 | 0.0010 | -0.0033 | 0.01% | -0.00% | 0.01% | 0.01% |
| W4_T3_DIMB | IS | 4 | 10327 | 0.0016 | -0.0044 | 0.03% | -0.02% | 0.00% | 0.00% |
| W4_T3_DIMB | IS | 8 | 10327 | -0.0015 | -0.0050 | 0.05% | -0.05% | -0.01% | -0.01% |
| W4_T3_DIMB | IS | 24 | 10327 | 0.0019 | -0.0034 | 0.14% | -0.14% | 0.00% | 0.00% |
| W4_T3_DIMB | OOS | 1 | 3508 | -0.0128 | -0.0072 | -0.00% | -0.01% | -0.01% | -0.01% |
| W4_T3_DIMB | OOS | 4 | 3505 | -0.0020 | 0.0041 | 0.01% | -0.01% | 0.00% | 0.00% |
| W4_T3_DIMB | OOS | 8 | 3501 | -0.0006 | -0.0033 | 0.02% | -0.01% | 0.01% | 0.01% |
| W4_T3_DIMB | OOS | 24 | 3485 | -0.0021 | 0.0003 | 0.05% | -0.04% | 0.01% | 0.01% |
| W4_T3_DIMB | VALIDATION | 1 | 3509 | -0.0011 | -0.0054 | -0.01% | 0.01% | 0.00% | 0.00% |
| W4_T3_DIMB | VALIDATION | 4 | 3509 | 0.0055 | 0.0019 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_T3_DIMB | VALIDATION | 8 | 3509 | -0.0014 | -0.0004 | -0.06% | 0.07% | 0.01% | 0.01% |
| W4_T3_DIMB | VALIDATION | 24 | 3509 | -0.0018 | 0.0027 | -0.17% | 0.17% | -0.00% | -0.00% |
| W4_T4_FLOW_DIV | IS | 1 | 10327 | -0.0021 | -0.0160 | 0.00% | -0.01% | -0.00% | -0.00% |
| W4_T4_FLOW_DIV | IS | 4 | 10327 | 0.0010 | -0.0100 | 0.02% | -0.02% | 0.00% | 0.00% |
| W4_T4_FLOW_DIV | IS | 8 | 10327 | 0.0042 | 0.0001 | 0.05% | -0.05% | -0.01% | -0.01% |
| W4_T4_FLOW_DIV | IS | 24 | 10327 | 0.0058 | -0.0028 | 0.15% | -0.12% | 0.03% | 0.03% |
| W4_T4_FLOW_DIV | OOS | 1 | 3508 | -0.0081 | -0.0101 | 0.00% | -0.01% | -0.01% | -0.01% |
| W4_T4_FLOW_DIV | OOS | 4 | 3505 | -0.0024 | 0.0014 | 0.01% | -0.01% | 0.00% | 0.00% |
| W4_T4_FLOW_DIV | OOS | 8 | 3501 | -0.0026 | -0.0021 | 0.02% | -0.03% | -0.01% | -0.01% |
| W4_T4_FLOW_DIV | OOS | 24 | 3485 | -0.0012 | 0.0029 | 0.06% | -0.06% | -0.00% | -0.00% |
| W4_T4_FLOW_DIV | VALIDATION | 1 | 3509 | -0.0007 | 0.0113 | -0.01% | 0.01% | 0.00% | 0.00% |
| W4_T4_FLOW_DIV | VALIDATION | 4 | 3509 | 0.0036 | 0.0098 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_T4_FLOW_DIV | VALIDATION | 8 | 3509 | 0.0010 | -0.0058 | -0.06% | 0.07% | 0.01% | 0.01% |
| W4_T4_FLOW_DIV | VALIDATION | 24 | 3509 | 0.0016 | 0.0002 | -0.16% | 0.17% | 0.02% | 0.02% |
| W4_T5_IMB_VOL | IS | 1 | 10327 | -0.0055 | -0.0306 | 0.01% | -0.00% | 0.01% | 0.01% |
| W4_T5_IMB_VOL | IS | 4 | 10327 | -0.0007 | -0.0169 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_T5_IMB_VOL | IS | 8 | 10327 | 0.0027 | -0.0048 | 0.06% | -0.05% | 0.01% | 0.01% |
| W4_T5_IMB_VOL | IS | 24 | 10327 | 0.0030 | -0.0050 | 0.15% | -0.14% | 0.01% | 0.01% |
| W4_T5_IMB_VOL | OOS | 1 | 3508 | -0.0185 | -0.0278 | 0.00% | -0.01% | -0.01% | -0.01% |
| W4_T5_IMB_VOL | OOS | 4 | 3505 | -0.0083 | -0.0276 | 0.02% | -0.02% | -0.00% | -0.00% |
| W4_T5_IMB_VOL | OOS | 8 | 3501 | -0.0019 | -0.0081 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_T5_IMB_VOL | OOS | 24 | 3485 | -0.0002 | 0.0069 | 0.06% | -0.06% | 0.00% | 0.00% |
| W4_T5_IMB_VOL | VALIDATION | 1 | 3509 | -0.0073 | -0.0227 | -0.01% | 0.01% | -0.00% | -0.00% |
| W4_T5_IMB_VOL | VALIDATION | 4 | 3509 | -0.0128 | -0.0033 | -0.04% | 0.03% | -0.01% | -0.01% |
| W4_T5_IMB_VOL | VALIDATION | 8 | 3509 | -0.0156 | -0.0145 | -0.07% | 0.06% | -0.01% | -0.01% |
| W4_T5_IMB_VOL | VALIDATION | 24 | 3509 | -0.0129 | -0.0079 | -0.17% | 0.15% | -0.02% | -0.02% |
| W4_T6_IMB_REL | IS | 1 | 10327 | 0.0057 | 0.0123 | 0.01% | -0.00% | 0.00% | 0.00% |
| W4_T6_IMB_REL | IS | 4 | 10327 | 0.0044 | 0.0069 | 0.03% | -0.02% | 0.00% | 0.00% |
| W4_T6_IMB_REL | IS | 8 | 10327 | 0.0052 | 0.0126 | 0.06% | -0.05% | 0.01% | 0.01% |
| W4_T6_IMB_REL | IS | 24 | 10327 | 0.0080 | 0.0066 | 0.13% | -0.12% | 0.01% | 0.01% |
| W4_T6_IMB_REL | OOS | 1 | 3508 | 0.0013 | 0.0150 | 0.01% | -0.00% | 0.01% | 0.01% |
| W4_T6_IMB_REL | OOS | 4 | 3505 | -0.0063 | 0.0040 | 0.01% | -0.02% | -0.01% | -0.01% |
| W4_T6_IMB_REL | OOS | 8 | 3501 | 0.0046 | 0.0145 | 0.03% | -0.02% | 0.01% | 0.01% |
| W4_T6_IMB_REL | OOS | 24 | 3485 | 0.0064 | 0.0121 | 0.07% | -0.05% | 0.02% | 0.02% |
| W4_T6_IMB_REL | VALIDATION | 1 | 3509 | 0.0072 | 0.0040 | -0.01% | 0.01% | 0.00% | 0.00% |
| W4_T6_IMB_REL | VALIDATION | 4 | 3509 | 0.0116 | 0.0043 | -0.03% | 0.04% | 0.01% | 0.01% |
| W4_T6_IMB_REL | VALIDATION | 8 | 3509 | 0.0011 | 0.0011 | -0.07% | 0.08% | 0.01% | 0.01% |
| W4_T6_IMB_REL | VALIDATION | 24 | 3509 | 0.0016 | -0.0088 | -0.15% | 0.19% | 0.03% | 0.04% |
| W4_TO_IMB_OI | IS | 1 | 10323 | 0.0065 | 0.0075 | 0.01% | -0.00% | 0.01% | 0.01% |
| W4_TO_IMB_OI | IS | 4 | 10323 | 0.0006 | 0.0085 | 0.03% | -0.02% | 0.02% | 0.02% |
| W4_TO_IMB_OI | IS | 8 | 10323 | 0.0008 | 0.0047 | 0.06% | -0.04% | 0.02% | 0.02% |
| W4_TO_IMB_OI | IS | 24 | 10323 | -0.0073 | -0.0036 | 0.14% | -0.13% | 0.01% | 0.01% |
| W4_TO_IMB_OI | OOS | 1 | 3508 | -0.0005 | -0.0003 | 0.01% | -0.00% | 0.00% | 0.00% |
| W4_TO_IMB_OI | OOS | 4 | 3505 | -0.0053 | 0.0037 | 0.02% | -0.02% | -0.01% | -0.01% |
| W4_TO_IMB_OI | OOS | 8 | 3501 | 0.0013 | 0.0015 | 0.03% | -0.03% | 0.00% | 0.00% |
| W4_TO_IMB_OI | OOS | 24 | 3485 | -0.0015 | -0.0025 | 0.06% | -0.06% | -0.00% | -0.00% |
| W4_TO_IMB_OI | VALIDATION | 1 | 3509 | -0.0058 | 0.0050 | -0.01% | 0.01% | -0.00% | -0.00% |
| W4_TO_IMB_OI | VALIDATION | 4 | 3509 | 0.0020 | 0.0023 | -0.04% | 0.04% | 0.00% | 0.00% |
| W4_TO_IMB_OI | VALIDATION | 8 | 3509 | -0.0007 | -0.0016 | -0.07% | 0.08% | 0.01% | 0.01% |
| W4_TO_IMB_OI | VALIDATION | 24 | 3509 | 0.0033 | -0.0064 | -0.19% | 0.18% | -0.01% | -0.01% |

| Hypothesis | Phase | LONG MAE | LONG MFE | SHORT MAE | SHORT MFE |
|---|---|---:|---:|---:|---:|
| W4_BASE_REL24 | IS | -3.37% | 3.43% | -3.31% | 3.18% |
| W4_BASE_REL24 | OOS | -2.15% | 2.39% | -2.19% | 2.10% |
| W4_BASE_REL24 | VALIDATION | -2.73% | 2.39% | -2.71% | 2.58% |
| W4_FO_CROWD_REV | IS | -3.17% | 3.11% | -3.60% | 3.79% |
| W4_FO_CROWD_REV | OOS | -2.14% | 2.26% | -2.18% | 2.36% |
| W4_FO_CROWD_REV | VALIDATION | -2.73% | 2.58% | -2.93% | 2.43% |
| W4_M_LS_REV | IS | -2.96% | 3.03% | -3.58% | 3.34% |
| W4_M_LS_REV | OOS | -2.20% | 2.28% | -2.03% | 2.09% |
| W4_M_LS_REV | VALIDATION | -2.71% | 2.52% | -2.63% | 2.36% |
| W4_M_TAKER_LS | IS | -3.34% | 3.28% | -3.29% | 3.23% |
| W4_M_TAKER_LS | OOS | -2.15% | 2.19% | -2.11% | 2.22% |
| W4_M_TAKER_LS | VALIDATION | -2.71% | 2.48% | -2.71% | 2.48% |
| W4_M_TOP_LS_REV | IS | -3.01% | 3.01% | -3.59% | 3.45% |
| W4_M_TOP_LS_REV | OOS | -2.06% | 2.18% | -2.18% | 2.12% |
| W4_M_TOP_LS_REV | VALIDATION | -2.64% | 2.41% | -2.70% | 2.39% |
| W4_OI_CONFIRM | IS | -3.49% | 3.53% | -3.34% | 3.24% |
| W4_OI_CONFIRM | OOS | -2.19% | 2.33% | -2.16% | 2.19% |
| W4_OI_CONFIRM | VALIDATION | -2.80% | 2.49% | -2.72% | 2.56% |
| W4_OI_D1 | IS | -3.39% | 3.36% | -3.42% | 3.33% |
| W4_OI_D1 | OOS | -2.15% | 2.25% | -2.17% | 2.25% |
| W4_OI_D1 | VALIDATION | -2.75% | 2.52% | -2.73% | 2.51% |
| W4_OI_D24 | IS | -3.38% | 3.42% | -3.38% | 3.26% |
| W4_OI_D24 | OOS | -2.15% | 2.29% | -2.18% | 2.18% |
| W4_OI_D24 | VALIDATION | -2.75% | 2.50% | -2.72% | 2.54% |
| W4_OI_SHOCK | IS | -3.36% | 3.33% | -3.33% | 3.24% |
| W4_OI_SHOCK | OOS | -2.14% | 2.22% | -2.13% | 2.22% |
| W4_OI_SHOCK | VALIDATION | -2.73% | 2.50% | -2.69% | 2.47% |
| W4_T1_IMB_CONT | IS | -3.31% | 3.27% | -3.30% | 3.23% |
| W4_T1_IMB_CONT | OOS | -2.14% | 2.21% | -2.12% | 2.17% |
| W4_T1_IMB_CONT | VALIDATION | -2.71% | 2.48% | -2.70% | 2.48% |
| W4_T2_IMB_REV | IS | -3.33% | 3.26% | -3.29% | 3.25% |
| W4_T2_IMB_REV | OOS | -2.12% | 2.18% | -2.15% | 2.19% |
| W4_T2_IMB_REV | VALIDATION | -2.70% | 2.49% | -2.71% | 2.47% |
| W4_T3_DIMB | IS | -3.32% | 3.26% | -3.30% | 3.23% |
| W4_T3_DIMB | OOS | -2.13% | 2.19% | -2.14% | 2.18% |
| W4_T3_DIMB | VALIDATION | -2.71% | 2.49% | -2.69% | 2.47% |
| W4_T4_FLOW_DIV | IS | -3.29% | 3.24% | -3.50% | 3.44% |
| W4_T4_FLOW_DIV | OOS | -2.11% | 2.19% | -2.21% | 2.28% |
| W4_T4_FLOW_DIV | VALIDATION | -2.68% | 2.46% | -2.77% | 2.54% |
| W4_T5_IMB_VOL | IS | -3.32% | 3.28% | -3.33% | 3.25% |
| W4_T5_IMB_VOL | OOS | -2.14% | 2.22% | -2.13% | 2.19% |
| W4_T5_IMB_VOL | VALIDATION | -2.70% | 2.48% | -2.70% | 2.48% |
| W4_T6_IMB_REL | IS | -3.42% | 3.35% | -3.43% | 3.38% |
| W4_T6_IMB_REL | OOS | -2.19% | 2.27% | -2.18% | 2.26% |
| W4_T6_IMB_REL | VALIDATION | -2.75% | 2.54% | -2.75% | 2.50% |
| W4_TO_IMB_OI | IS | -3.40% | 3.36% | -3.34% | 3.25% |
| W4_TO_IMB_OI | OOS | -2.17% | 2.24% | -2.12% | 2.20% |
| W4_TO_IMB_OI | VALIDATION | -2.73% | 2.49% | -2.74% | 2.50% |

## 12. Candidate strategy results

| Id | Hypothesis | Data | Class |
|---|---|---|---|
| W4_BASE_REL24 | 24h return minus BTC; OHLCV-only baseline for incremental tests. | OHLCV+BTC | **REJECTED** |
| W4_T1_IMB_CONT | Taker buy imbalance (2*buy/vol-1) continues. | TakerBuyVolume | **REJECTED** |
| W4_T2_IMB_REV | Taker buy imbalance mean-reverts. | TakerBuyVolume | **REJECTED** |
| W4_T3_DIMB | Change in taker imbalance (acceleration) continues. | TakerBuyVolume | **REJECTED** |
| W4_T4_FLOW_DIV | Price up while takers sell (and vice versa) then reverses: -ret1 * imbalance. | OHLCV+Taker | **REJECTED** |
| W4_T5_IMB_VOL | Imbalance scaled by volume shock (vol/SMA20). | Taker+volume | **REJECTED** |
| W4_T6_IMB_REL | Imbalance scaled by 24h relative-to-BTC return. | Taker+OHLCV | **REJECTED** |
| W4_OI_D1 | 1h open-interest change continues. | Vision OI | **REJECTED** |
| W4_OI_D24 | 24h open-interest change continues. | Vision OI | **REJECTED** |
| W4_OI_SHOCK | OI change / trailing 24h std continues. | Vision OI | **REJECTED** |
| W4_OI_CONFIRM | Price and OI move together (ret24 * dOI24). | OHLCV+OI | **REJECTED** |
| W4_M_LS_REV | Account long/short ratio crowding reverses: -LS. | Vision LS ratio | **FRAGILE** |
| W4_M_TOP_LS_REV | Top-trader long/short crowding reverses. | Vision top LS | **REJECTED** |
| W4_M_TAKER_LS | Vision 5m taker long/short volume ratio continues. | Vision taker LS | **REJECTED** |
| W4_FO_CROWD_REV | Positive funding with rising OI reverses: -funding * max(dOI24,0). | Funding+OI | **REJECTED** |
| W4_TO_IMB_OI | Taker imbalance times 1h OI change. | Taker+OI | **REJECTED** |

No signal cleared IS IC>0.02 with positive tercile spread, same-sign VALIDATION, and execution spread above the 0.12% round-trip hurdle. **Stage 2 Isolated replay was not launched.**

Per-candidate trading rows (trades / WR / PF / expectancy / OOS PF / drawdown / cost sensitivity) are **not applicable**: no strategy was built.

Paired long-strong / short-weak remains a **statistical spread**, not an Isolated Cross book. LOW still sizes each position independently. Single-book replay cannot execute a pair.

## 13. OOS results

OOS is the last 20% of the aligned 1h panel. Lookbacks (1/24, vol 20, OI shock 24, terciles) were not edited after seeing OOS.

- **W4_BASE_REL24** OOS 24h: CS IC 0.0234; LONG 0.16%; SHORT 0.04%; exec spread 0.20%; **REJECTED**.
- **W4_T1_IMB_CONT** OOS 24h: CS IC -0.0047; LONG 0.05%; SHORT -0.05%; exec spread 0.01%; **REJECTED**.
- **W4_T2_IMB_REV** OOS 24h: CS IC 0.0047; LONG 0.06%; SHORT -0.03%; exec spread 0.03%; **REJECTED**.
- **W4_T3_DIMB** OOS 24h: CS IC -0.0021; LONG 0.05%; SHORT -0.04%; exec spread 0.01%; **REJECTED**.
- **W4_T4_FLOW_DIV** OOS 24h: CS IC -0.0012; LONG 0.06%; SHORT -0.06%; exec spread -0.00%; **REJECTED**.
- **W4_T5_IMB_VOL** OOS 24h: CS IC -0.0002; LONG 0.06%; SHORT -0.06%; exec spread 0.00%; **REJECTED**.
- **W4_T6_IMB_REL** OOS 24h: CS IC 0.0064; LONG 0.07%; SHORT -0.05%; exec spread 0.02%; **REJECTED**.
- **W4_OI_D1** OOS 24h: CS IC 0.0106; LONG 0.07%; SHORT -0.07%; exec spread 0.01%; **REJECTED**.
- **W4_OI_D24** OOS 24h: CS IC 0.0331; LONG 0.13%; SHORT 0.03%; exec spread 0.16%; **REJECTED**.
- **W4_OI_SHOCK** OOS 24h: CS IC 0.0061; LONG 0.07%; SHORT -0.08%; exec spread -0.01%; **REJECTED**.
- **W4_OI_CONFIRM** OOS 24h: CS IC -0.0064; LONG 0.07%; SHORT -0.06%; exec spread 0.01%; **REJECTED**.
- **W4_M_LS_REV** OOS 24h: CS IC 0.0228; LONG 0.08%; SHORT -0.05%; exec spread 0.04%; **FRAGILE**.
- **W4_M_TOP_LS_REV** OOS 24h: CS IC 0.0183; LONG 0.09%; SHORT 0.00%; exec spread 0.09%; **REJECTED**.
- **W4_M_TAKER_LS** OOS 24h: CS IC -0.0126; LONG 0.04%; SHORT -0.10%; exec spread -0.06%; **REJECTED**.
- **W4_FO_CROWD_REV** OOS 24h: CS IC -0.0153; LONG 0.13%; SHORT -0.20%; exec spread -0.06%; **REJECTED**.
- **W4_TO_IMB_OI** OOS 24h: CS IC -0.0015; LONG 0.06%; SHORT -0.06%; exec spread -0.00%; **REJECTED**.

## 14. Cost sensitivity

Exec spread is next-open → close[t+h] top-minus-bottom. BASE round-trip is 0.12 % (0.04%×2 commission + 0.02%×2 slippage). +25% / +50% / +100% cost stress applies only after a signal clears BASE. None did, so cost-grid Isolated books were not run.

## 15. Walk-forward results

Not run. Walk-forward and 1,584-book validation are gated on PROMISING information-value plus a constructed Isolated strategy.

## 16. Full 1,584-book validation for survivors

Not run. No survivor.

## 17. Final conclusion

**NO ROBUST ALPHA FOUND**

Available market data does not currently demonstrate a robust tradable alpha under Model B costs and LOW Isolated risk.
Taker flow and Vision OI/LS were tested as information, not as another RSI combination. Machine learning was not applied (no stable individual feature). Strategy generation is stopped at this gate.

FRAGILE means IS/VAL/OOS IC signs can agree but the execution spread does not clear BASE costs on OOS, or OOS confirmation is incomplete. That is not a ROBUST CANDIDATE and Isolated replay was not launched.

### Failure reasons / data limitations

- 10 mega-caps are not the 528-coin rank universe; Phase 8 used the existing 1h cache when loaded.
- Inner join drops hours missing any of the 10 coins.
- Liquidations: DATA_UNAVAILABLE (Binance stopped USD-M snapshots).
- Predicted funding and live depth snapshots are not historical series.
- Do not raise Isolated 0.5%/3x to manufacture PF. Do not enable LIVE.

## Run notes
- Wave-4 Stage 1: information-value of newly acquired series. No Isolated strategy promoted. LIVE=OFF.
- Ranks, OI, taker, and funding at t use only observations with timestamp <= candle close.
- Forward returns are labels, not features. 10-coin inner join on OpenTime. Terciles, not deciles.
- Round-trip cost hurdle 0.12 %.
- Taker imbalance valid on 173450/173450 bar-coins after warmup.
- OI aligned finite after warmup: 173450. LS ratio finite: 173430.
- Re-rendered classifications from cached Wave-4 artifacts. No re-download.
