# Futures microstructure data audit

This is a coverage audit. It does not create a strategy and it does not call any series an edge.

The comparison window is the same OHLCV book used for the 11,125 reconstructed trades: BTCUSDT, ETHUSDT, and BNBUSDT, 5m and 15m for about one year, 1h for about two years.

## Coverage

| Dataset | Coin | Timeframe | Status | From | To | Records | Gaps | Duplicates | Missing | Causal | Source | Limitation |
| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- |
| OHLCV | BTCUSDT | 5m | AVAILABLE | 2025-09-23 09:10 | 2026-09-23 09:09 | 105120 | 0 | 0 | 0.0% | yes | artifacts/strategy-validation-cache fapi klines | Same window as the reconstructed book. |
| OHLCV | BTCUSDT | 15m | AVAILABLE | 2025-09-23 09:00 | 2026-09-23 08:59 | 35040 | 0 | 0 | 0.0% | yes | artifacts/strategy-validation-cache fapi klines | Same window as the reconstructed book. |
| OHLCV | BTCUSDT | 1h | AVAILABLE | 2024-09-23 09:00 | 2026-09-23 08:59 | 17520 | 0 | 0 | 0.0% | yes | artifacts/strategy-validation-cache fapi klines | Same window as the reconstructed book. |
| OHLCV | ETHUSDT | 5m | AVAILABLE | 2025-09-23 09:10 | 2026-09-23 09:09 | 105120 | 0 | 0 | 0.0% | yes | artifacts/strategy-validation-cache fapi klines | Same window as the reconstructed book. |
| OHLCV | ETHUSDT | 15m | AVAILABLE | 2025-09-23 09:00 | 2026-09-23 08:59 | 35040 | 0 | 0 | 0.0% | yes | artifacts/strategy-validation-cache fapi klines | Same window as the reconstructed book. |
| OHLCV | ETHUSDT | 1h | AVAILABLE | 2024-09-23 09:00 | 2026-09-23 08:59 | 17520 | 0 | 0 | 0.0% | yes | artifacts/strategy-validation-cache fapi klines | Same window as the reconstructed book. |
| OHLCV | BNBUSDT | 5m | AVAILABLE | 2025-09-23 09:10 | 2026-09-23 09:09 | 105120 | 0 | 0 | 0.0% | yes | artifacts/strategy-validation-cache fapi klines | Same window as the reconstructed book. |
| OHLCV | BNBUSDT | 15m | AVAILABLE | 2025-09-23 09:00 | 2026-09-23 08:59 | 35040 | 0 | 0 | 0.0% | yes | artifacts/strategy-validation-cache fapi klines | Same window as the reconstructed book. |
| OHLCV | BNBUSDT | 1h | AVAILABLE | 2024-09-23 09:00 | 2026-09-23 08:59 | 17520 | 0 | 0 | 0.0% | yes | artifacts/strategy-validation-cache fapi klines | Same window as the reconstructed book. |
| Funding | BTCUSDT | 8h | AVAILABLE | 2024-09-23 16:00 | 2026-09-23 08:00 | 2190 | 0 | 0 | 0.0% | yes | GET /fapi/v1/fundingRate settled fundingTime | Predicted next funding rate is not used. |
| Open interest | BTCUSDT | 5m | AVAILABLE | 2024-09-23 00:00 | 2026-09-22 23:55 | 210235 | 2 | 0 | 0.0% | yes | https://data.binance.vision/data/futures/um/daily/metrics/ sum_open_interest | REST openInterestHist is a separate ~30 day source and is not used here. |
| Depth | BTCUSDT | snapshot | AVAILABLE | 2024-09-23 00:00 | 2026-09-22 23:59 | 2051188 | 1 | 0 | 0.1% | yes | Vision um/daily/bookDepth notional at ±1% | Best bid/ask spread is not in this file. Missing days are not interpolated. |
| Basis | BTCUSDT | 5m | AVAILABLE | 2025-09-23 09:10 | 2026-09-23 09:09 | 105120 | 0 | 0 | 0.0% | yes | markPriceKlines and indexPriceKlines close, same bar | No forward fill. A bar without both closes is null. |
| Taker flow | BTCUSDT | 5m | AVAILABLE | 2025-09-23 09:10 | 2026-09-23 09:09 | 105120 | 0 | 0 | 0.0% | yes | GET /fapi/v1/klines field 9, side cache only | Matched 105120/105120 OHLCV bars. Old cache zeros were not treated as flow. |
| Basis | BTCUSDT | 15m | AVAILABLE | 2025-09-23 09:00 | 2026-09-23 08:59 | 35040 | 0 | 0 | 0.0% | yes | markPriceKlines and indexPriceKlines close, same bar | No forward fill. A bar without both closes is null. |
| Taker flow | BTCUSDT | 15m | AVAILABLE | 2025-09-23 09:00 | 2026-09-23 08:59 | 35040 | 0 | 0 | 0.0% | yes | GET /fapi/v1/klines field 9, side cache only | Matched 35040/35040 OHLCV bars. Old cache zeros were not treated as flow. |
| Basis | BTCUSDT | 1h | AVAILABLE | 2024-09-23 09:00 | 2026-09-23 08:59 | 17520 | 0 | 0 | 0.0% | yes | markPriceKlines and indexPriceKlines close, same bar | No forward fill. A bar without both closes is null. |
| Taker flow | BTCUSDT | 1h | AVAILABLE | 2024-09-23 09:00 | 2026-09-23 08:59 | 17520 | 0 | 0 | 0.0% | yes | GET /fapi/v1/klines field 9, side cache only | Matched 17520/17520 OHLCV bars. Old cache zeros were not treated as flow. |
| Funding | ETHUSDT | 8h | AVAILABLE | 2024-09-23 16:00 | 2026-09-23 08:00 | 2190 | 0 | 0 | 0.0% | yes | GET /fapi/v1/fundingRate settled fundingTime | Predicted next funding rate is not used. |
| Open interest | ETHUSDT | 5m | AVAILABLE | 2024-09-23 00:00 | 2026-09-22 23:55 | 210235 | 2 | 0 | 0.0% | yes | https://data.binance.vision/data/futures/um/daily/metrics/ sum_open_interest | REST openInterestHist is a separate ~30 day source and is not used here. |
| Depth | ETHUSDT | snapshot | AVAILABLE | 2024-09-23 00:00 | 2026-09-22 23:59 | 2051188 | 1 | 0 | 0.1% | yes | Vision um/daily/bookDepth notional at ±1% | Best bid/ask spread is not in this file. Missing days are not interpolated. |
| Basis | ETHUSDT | 5m | AVAILABLE | 2025-09-23 09:10 | 2026-09-23 09:09 | 105120 | 0 | 0 | 0.0% | yes | markPriceKlines and indexPriceKlines close, same bar | No forward fill. A bar without both closes is null. |
| Taker flow | ETHUSDT | 5m | AVAILABLE | 2025-09-23 09:10 | 2026-09-23 09:09 | 105120 | 0 | 0 | 0.0% | yes | GET /fapi/v1/klines field 9, side cache only | Matched 105120/105120 OHLCV bars. Old cache zeros were not treated as flow. |
| Basis | ETHUSDT | 15m | AVAILABLE | 2025-09-23 09:00 | 2026-09-23 08:59 | 35040 | 0 | 0 | 0.0% | yes | markPriceKlines and indexPriceKlines close, same bar | No forward fill. A bar without both closes is null. |
| Taker flow | ETHUSDT | 15m | AVAILABLE | 2025-09-23 09:00 | 2026-09-23 08:59 | 35040 | 0 | 0 | 0.0% | yes | GET /fapi/v1/klines field 9, side cache only | Matched 35040/35040 OHLCV bars. Old cache zeros were not treated as flow. |
| Basis | ETHUSDT | 1h | AVAILABLE | 2024-09-23 09:00 | 2026-09-23 08:59 | 17520 | 0 | 0 | 0.0% | yes | markPriceKlines and indexPriceKlines close, same bar | No forward fill. A bar without both closes is null. |
| Taker flow | ETHUSDT | 1h | AVAILABLE | 2024-09-23 09:00 | 2026-09-23 08:59 | 17520 | 0 | 0 | 0.0% | yes | GET /fapi/v1/klines field 9, side cache only | Matched 17520/17520 OHLCV bars. Old cache zeros were not treated as flow. |
| Funding | BNBUSDT | 8h | AVAILABLE | 2024-09-23 16:00 | 2026-09-23 08:00 | 2190 | 0 | 0 | 0.0% | yes | GET /fapi/v1/fundingRate settled fundingTime | Predicted next funding rate is not used. |
| Open interest | BNBUSDT | 5m | AVAILABLE | 2024-09-23 00:00 | 2026-09-22 23:55 | 210235 | 2 | 0 | 0.0% | yes | https://data.binance.vision/data/futures/um/daily/metrics/ sum_open_interest | REST openInterestHist is a separate ~30 day source and is not used here. |
| Depth | BNBUSDT | snapshot | AVAILABLE | 2024-09-23 00:00 | 2026-09-22 23:59 | 2051189 | 1 | 0 | 0.1% | yes | Vision um/daily/bookDepth notional at ±1% | Best bid/ask spread is not in this file. Missing days are not interpolated. |
| Basis | BNBUSDT | 5m | AVAILABLE | 2025-09-23 09:10 | 2026-09-23 09:09 | 105120 | 0 | 0 | 0.0% | yes | markPriceKlines and indexPriceKlines close, same bar | No forward fill. A bar without both closes is null. |
| Taker flow | BNBUSDT | 5m | AVAILABLE | 2025-09-23 09:10 | 2026-09-23 09:09 | 105120 | 0 | 0 | 0.0% | yes | GET /fapi/v1/klines field 9, side cache only | Matched 105120/105120 OHLCV bars. Old cache zeros were not treated as flow. |
| Basis | BNBUSDT | 15m | AVAILABLE | 2025-09-23 09:00 | 2026-09-23 08:59 | 35040 | 0 | 0 | 0.0% | yes | markPriceKlines and indexPriceKlines close, same bar | No forward fill. A bar without both closes is null. |
| Taker flow | BNBUSDT | 15m | AVAILABLE | 2025-09-23 09:00 | 2026-09-23 08:59 | 35040 | 0 | 0 | 0.0% | yes | GET /fapi/v1/klines field 9, side cache only | Matched 35040/35040 OHLCV bars. Old cache zeros were not treated as flow. |
| Basis | BNBUSDT | 1h | AVAILABLE | 2024-09-23 09:00 | 2026-09-23 08:59 | 17520 | 0 | 0 | 0.0% | yes | markPriceKlines and indexPriceKlines close, same bar | No forward fill. A bar without both closes is null. |
| Taker flow | BNBUSDT | 1h | AVAILABLE | 2024-09-23 09:00 | 2026-09-23 08:59 | 17520 | 0 | 0 | 0.0% | yes | GET /fapi/v1/klines field 9, side cache only | Matched 17520/17520 OHLCV bars. Old cache zeros were not treated as flow. |
| Liquidations | BTCUSDT ETHUSDT BNBUSDT | n/a | DATA_UNAVAILABLE | n/a | n/a | 0 | 0 | 0 | 100.0% | n/a | Vision um/daily/liquidationSnapshot | liquidation 2023-06-01 status 404. liquidation 2024-03-15 status 404. liquidation 2024-03-31 status 404. liquidation 2025-01-01 status 404. |
| Spread | BTCUSDT ETHUSDT BNBUSDT | n/a | DATA_UNAVAILABLE | n/a | n/a | 0 | 0 | 0 | 100.0% | n/a | not in bookDepth | The depth archive has percentage notional, not best bid/ask. |
| Open interest REST | BTCUSDT | 5m | PARTIAL | 2026-08-21 20:30 | 2026-09-19 20:25 | 8352 | 0 | 0 | 0.0% | yes | GET /futures/data/openInterestHist | On-disk span 29.0 days. This is not the Vision series and was not used for features. |
| Open interest REST | BTCUSDT | 15m | PARTIAL | 2026-08-21 20:30 | 2026-09-19 20:15 | 2784 | 0 | 0 | 0.0% | yes | GET /futures/data/openInterestHist | On-disk span 29.0 days. This is not the Vision series and was not used for features. |
| Open interest REST | BTCUSDT | 1h | PARTIAL | 2026-08-21 21:00 | 2026-09-19 20:00 | 696 | 0 | 0 | 0.0% | yes | GET /futures/data/openInterestHist | On-disk span 29.0 days. This is not the Vision series and was not used for features. |
| Open interest REST | ETHUSDT | 5m | PARTIAL | 2026-08-21 20:30 | 2026-09-19 20:25 | 8352 | 0 | 0 | 0.0% | yes | GET /futures/data/openInterestHist | On-disk span 29.0 days. This is not the Vision series and was not used for features. |
| Open interest REST | ETHUSDT | 15m | PARTIAL | 2026-08-21 20:30 | 2026-09-19 20:15 | 2784 | 0 | 0 | 0.0% | yes | GET /futures/data/openInterestHist | On-disk span 29.0 days. This is not the Vision series and was not used for features. |
| Open interest REST | ETHUSDT | 1h | PARTIAL | 2026-08-21 21:00 | 2026-09-19 20:00 | 696 | 0 | 0 | 0.0% | yes | GET /futures/data/openInterestHist | On-disk span 29.0 days. This is not the Vision series and was not used for features. |
| Open interest REST | BNBUSDT | 5m | PARTIAL | 2026-08-21 20:30 | 2026-09-19 20:25 | 8352 | 0 | 0 | 0.0% | yes | GET /futures/data/openInterestHist | On-disk span 29.0 days. This is not the Vision series and was not used for features. |
| Open interest REST | BNBUSDT | 15m | PARTIAL | 2026-08-21 20:30 | 2026-09-19 20:15 | 2784 | 0 | 0 | 0.0% | yes | GET /futures/data/openInterestHist | On-disk span 29.0 days. This is not the Vision series and was not used for features. |
| Open interest REST | BNBUSDT | 1h | PARTIAL | 2026-08-21 21:00 | 2026-09-19 20:00 | 696 | 0 | 0 | 0.0% | yes | GET /futures/data/openInterestHist | On-disk span 29.0 days. This is not the Vision series and was not used for features. |

## A. Data available

- OHLCV.
- Funding.
- Open interest.
- Depth.
- Basis.
- Taker flow.

## B. Data partially available

- Open interest REST: BTCUSDT 5m PARTIAL; BTCUSDT 15m PARTIAL; BTCUSDT 1h PARTIAL; ETHUSDT 5m PARTIAL; ETHUSDT 15m PARTIAL; ETHUSDT 1h PARTIAL; BNBUSDT 5m PARTIAL; BNBUSDT 15m PARTIAL; BNBUSDT 1h PARTIAL.

## C. Data unavailable

- Liquidations: liquidation 2023-06-01 status 404. liquidation 2024-03-15 status 404. liquidation 2024-03-31 status 404. liquidation 2025-01-01 status 404.
- Spread: The depth archive has percentage notional, not best bid/ask.

## Probes

- REST OI probe status 400. Body was not used as history.
- liquidation 2023-06-01 status 404.
- liquidation 2024-03-15 status 404.
- liquidation 2024-03-31 status 404.
- liquidation 2025-01-01 status 404.
- Depth archive probe BTCUSDT found 2023-01-01. Absent 2022-01-01=404, 2022-06-01=404, 2022-12-01=404. Local ingest starts at the OHLCV window, not at the archive origin.

Vision metrics also contain count and sum long/short ratios and `sum_taker_long_short_vol_ratio`. Those columns were not added to the feature list. Kline taker-buy is the taker definition in this phase.

No microstructure value was interpolated. A current order-book snapshot was not written back into history.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.
