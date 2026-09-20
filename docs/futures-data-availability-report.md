# Futures data availability report

Phase 2 data infrastructure. No strategy parameters were tuned. LIVE = OFF. No 528-universe strategy run.
No missing series was fabricated.

## Timestamp alignment
An observation is visible on candle `i` only when `observation.Timestamp <= candle.CloseTime`.
Funding uses **settled** `fundingTime` from `GET /fapi/v1/fundingRate`, not the next predicted rate from `premiumIndex.lastFundingRate`.
Mark/index use the **closed** kline `closeTime` and **close** price.
Basis = MarkClose − IndexClose. NormalizedBasis = Basis / IndexClose. Same closeTime only; unmatched bars are dropped.
Taker flow: Binance kline field 9 = taker buy base volume. TakerSell = Volume − TakerBuy only when buy > 0 and buy ≤ volume. Denominator 0 → DATA_UNAVAILABLE, not 0.

## Binance sources currently used

| Source | Endpoint | Symbol | Interval | Timestamp | Pagination | Max / request | Raw or derived | Deterministic replay |
|---|---|---|---|---|---|---|---|---|
| OHLCV | `GET /fapi/v1/klines` | `symbol` | 5m/15m/1h | openTime + closeTime | startTime cursor, limit 1500 | 1500 | raw | yes |
| Taker buy | kline field 9 | `symbol` | same as kline | kline closeTime | with klines | 1500 | raw | yes when field present |
| Taker sell | Volume − field 9 | `symbol` | same as kline | kline closeTime | derived | n/a | derived | yes when buy is valid |
| Funding (settled) | `GET /fapi/v1/fundingRate` | `symbol` | settlement (~8h) | `fundingTime` | startTime cursor, limit 1000 | 1000 | raw settled interval rate | yes |
| Mark price | `GET /fapi/v1/markPriceKlines` | `symbol` | 5m/15m/1h | closed closeTime | startTime cursor, limit 1000 | 1000 | raw close | yes |
| Index price | `GET /fapi/v1/indexPriceKlines` | USDT-M `pair` = symbol | 5m/15m/1h | closed closeTime | startTime cursor, limit 1000 | 1000 | raw close | yes |
| Basis | derived | `symbol` | matching mark/index | matching closeTime | n/a | n/a | derived MarkClose−IndexClose | yes |
| Open interest hist | `GET /futures/data/openInterestHist` | `symbol` | period 5m/15m/1h | `timestamp` | startTime cursor, limit 500 | 500 | raw `sumOpenInterest` | only latest ~30 days |
| Premium snapshot | `GET /fapi/v1/premiumIndex` | all / symbol | n/a | request time | none | snapshot | raw mark + lastFundingRate | **not historical** |
| OI snapshot | `GET /fapi/v1/openInterest` | `symbol` | n/a | request time | none | snapshot | raw | **not historical** |

`BinancePublicMarketDataClient` currently supports: `GET /fapi/v1/klines` (range + latest), `GET /fapi/v1/ticker/price`, `GET /fapi/v1/ticker/24hr`, `GET /fapi/v1/exchangeInfo`, `GET /fapi/v1/ticker/bookTicker`, `GET /fapi/v1/premiumIndex` (snapshot). It does **not** download fundingRate, markPriceKlines, indexPriceKlines, or openInterestHist.

PostgreSQL `MarketCandles` stores OHLCV. `TakerBuyVolume` is ignored by EF (`TradingModelConfiguration`). Research kline disk cache is `artifacts/strategy-validation-cache/{symbol}_{tf}.json`.

Rate limits (Binance public USD-M, not independently measured here): klines / fundingRate / mark-index klines are request-weight endpoints; `openInterestHist` is documented as IP weight 0. The research client pauses ~80ms between pages and retries 429.

## Coverage
### Funding
- BTCUSDT settlement: AVAILABLE from 2026-06-22 00:00 to 2026-09-19 16:00 (n=270). GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime.
- ETHUSDT settlement: AVAILABLE from 2026-06-22 00:00 to 2026-09-19 16:00 (n=270). GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime.
- BNBUSDT settlement: AVAILABLE from 2026-06-22 00:00 to 2026-09-19 16:00 (n=270). GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime.
- SOLUSDT settlement: AVAILABLE from 2026-06-22 00:00 to 2026-09-19 16:00 (n=270). GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime.
- XRPUSDT settlement: AVAILABLE from 2026-06-22 00:00 to 2026-09-19 16:00 (n=270). GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime.
- DOGEUSDT settlement: AVAILABLE from 2026-06-22 00:00 to 2026-09-19 16:00 (n=270). GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime.
- ADAUSDT settlement: AVAILABLE from 2026-06-22 00:00 to 2026-09-19 16:00 (n=270). GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime.
- AVAXUSDT settlement: AVAILABLE from 2026-06-22 00:00 to 2026-09-19 16:00 (n=270). GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime.
- LINKUSDT settlement: AVAILABLE from 2026-06-22 00:00 to 2026-09-19 16:00 (n=270). GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime.
- LTCUSDT settlement: AVAILABLE from 2026-06-22 00:00 to 2026-09-19 16:00 (n=270). GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime.

### MarkPrice
- BTCUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:04 (n=25916). GET /fapi/v1/markPriceKlines close. Closed bar only.
- BTCUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/markPriceKlines close. Closed bar only.
- BTCUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/markPriceKlines close. Closed bar only.
- ETHUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:04 (n=25916). GET /fapi/v1/markPriceKlines close. Closed bar only.
- ETHUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/markPriceKlines close. Closed bar only.
- ETHUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/markPriceKlines close. Closed bar only.
- BNBUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:04 (n=25916). GET /fapi/v1/markPriceKlines close. Closed bar only.
- BNBUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/markPriceKlines close. Closed bar only.
- BNBUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/markPriceKlines close. Closed bar only.
- SOLUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/markPriceKlines close. Closed bar only.
- SOLUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/markPriceKlines close. Closed bar only.
- SOLUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/markPriceKlines close. Closed bar only.
- XRPUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/markPriceKlines close. Closed bar only.
- XRPUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/markPriceKlines close. Closed bar only.
- XRPUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/markPriceKlines close. Closed bar only.
- DOGEUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/markPriceKlines close. Closed bar only.
- DOGEUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/markPriceKlines close. Closed bar only.
- DOGEUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/markPriceKlines close. Closed bar only.
- ADAUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/markPriceKlines close. Closed bar only.
- ADAUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/markPriceKlines close. Closed bar only.
- ADAUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/markPriceKlines close. Closed bar only.
- AVAXUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/markPriceKlines close. Closed bar only.
- AVAXUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/markPriceKlines close. Closed bar only.
- AVAXUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/markPriceKlines close. Closed bar only.
- LINKUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/markPriceKlines close. Closed bar only.
- LINKUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/markPriceKlines close. Closed bar only.
- LINKUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/markPriceKlines close. Closed bar only.
- LTCUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/markPriceKlines close. Closed bar only.
- LTCUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:14 (n=8640). GET /fapi/v1/markPriceKlines close. Closed bar only.
- LTCUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/markPriceKlines close. Closed bar only.

### IndexPrice
- BTCUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:04 (n=25916). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- BTCUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- BTCUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- ETHUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:04 (n=25916). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- ETHUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- ETHUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- BNBUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:04 (n=25916). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- BNBUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- BNBUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- SOLUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- SOLUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- SOLUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- XRPUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- XRPUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- XRPUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- DOGEUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- DOGEUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- DOGEUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- ADAUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- ADAUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- ADAUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- AVAXUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- AVAXUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- AVAXUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- LINKUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- LINKUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- LINKUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- LTCUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- LTCUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:14 (n=8640). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.
- LTCUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M.

### Basis
- BTCUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:04 (n=25916). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- BTCUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- BTCUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- ETHUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:04 (n=25916). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- ETHUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- ETHUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- BNBUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:04 (n=25916). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- BNBUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- BNBUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- SOLUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- SOLUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- SOLUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- XRPUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- XRPUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- XRPUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- DOGEUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- DOGEUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- DOGEUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- ADAUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- ADAUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- ADAUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- AVAXUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- AVAXUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- AVAXUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- LINKUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- LINKUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 19:59 (n=8639). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- LINKUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- LTCUSDT 5m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:09 (n=25917). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- LTCUSDT 15m: AVAILABLE from 2026-06-21 20:29 to 2026-09-19 20:14 (n=8640). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.
- LTCUSDT 1h: AVAILABLE from 2026-06-21 21:59 to 2026-09-19 19:59 (n=2159). Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close.

### OpenInterest
- BTCUSDT 5m: DATA_UNAVAILABLE from 2026-08-21 20:30 to 2026-09-19 20:25 (n=8352). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=8352 span=29.0d. Not fabricated.
- BTCUSDT 15m: DATA_UNAVAILABLE from 2026-08-21 20:30 to 2026-09-19 20:15 (n=2784). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=2784 span=29.0d. Not fabricated.
- BTCUSDT 1h: DATA_UNAVAILABLE from 2026-08-21 21:00 to 2026-09-19 20:00 (n=696). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=696 span=29.0d. Not fabricated.
- ETHUSDT 5m: DATA_UNAVAILABLE from 2026-08-21 20:30 to 2026-09-19 20:25 (n=8352). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=8352 span=29.0d. Not fabricated.
- ETHUSDT 15m: DATA_UNAVAILABLE from 2026-08-21 20:30 to 2026-09-19 20:15 (n=2784). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=2784 span=29.0d. Not fabricated.
- ETHUSDT 1h: DATA_UNAVAILABLE from 2026-08-21 21:00 to 2026-09-19 20:00 (n=696). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=696 span=29.0d. Not fabricated.
- BNBUSDT 5m: DATA_UNAVAILABLE from 2026-08-21 20:30 to 2026-09-19 20:25 (n=8352). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=8352 span=29.0d. Not fabricated.
- BNBUSDT 15m: DATA_UNAVAILABLE from 2026-08-21 20:30 to 2026-09-19 20:15 (n=2784). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=2784 span=29.0d. Not fabricated.
- BNBUSDT 1h: DATA_UNAVAILABLE from 2026-08-21 21:00 to 2026-09-19 20:00 (n=696). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=696 span=29.0d. Not fabricated.
- SOLUSDT 5m: DATA_UNAVAILABLE from 2026-08-21 20:30 to 2026-09-19 20:25 (n=8352). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=8352 span=29.0d. Not fabricated.
- SOLUSDT 15m: DATA_UNAVAILABLE from 2026-08-21 20:30 to 2026-09-19 20:15 (n=2784). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=2784 span=29.0d. Not fabricated.
- SOLUSDT 1h: DATA_UNAVAILABLE from 2026-08-21 21:00 to 2026-09-19 20:00 (n=696). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=696 span=29.0d. Not fabricated.
- XRPUSDT 5m: DATA_UNAVAILABLE from 2026-08-21 20:30 to 2026-09-19 20:25 (n=8352). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=8352 span=29.0d. Not fabricated.
- XRPUSDT 15m: DATA_UNAVAILABLE from 2026-08-21 20:45 to 2026-09-19 20:15 (n=2783). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=2783 span=29.0d. Not fabricated.
- XRPUSDT 1h: DATA_UNAVAILABLE from 2026-08-21 21:00 to 2026-09-19 20:00 (n=696). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=696 span=29.0d. Not fabricated.
- DOGEUSDT 5m: DATA_UNAVAILABLE from 2026-08-21 20:35 to 2026-09-19 20:25 (n=8351). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=8351 span=29.0d. Not fabricated.
- DOGEUSDT 15m: DATA_UNAVAILABLE from 2026-08-21 20:45 to 2026-09-19 20:15 (n=2783). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=2783 span=29.0d. Not fabricated.
- DOGEUSDT 1h: DATA_UNAVAILABLE from 2026-08-21 21:00 to 2026-09-19 20:00 (n=696). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=696 span=29.0d. Not fabricated.
- ADAUSDT 5m: DATA_UNAVAILABLE from 2026-08-21 20:35 to 2026-09-19 20:25 (n=8351). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=8351 span=29.0d. Not fabricated.
- ADAUSDT 15m: DATA_UNAVAILABLE from 2026-08-21 20:45 to 2026-09-19 20:15 (n=2783). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=2783 span=29.0d. Not fabricated.
- ADAUSDT 1h: DATA_UNAVAILABLE from 2026-08-21 21:00 to 2026-09-19 20:00 (n=696). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=696 span=29.0d. Not fabricated.
- AVAXUSDT 5m: DATA_UNAVAILABLE from 2026-08-21 20:35 to 2026-09-19 20:25 (n=8351). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=8351 span=29.0d. Not fabricated.
- AVAXUSDT 15m: DATA_UNAVAILABLE from 2026-08-21 20:45 to 2026-09-19 20:15 (n=2783). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=2783 span=29.0d. Not fabricated.
- AVAXUSDT 1h: DATA_UNAVAILABLE from 2026-08-21 21:00 to 2026-09-19 20:00 (n=696). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=696 span=29.0d. Not fabricated.
- LINKUSDT 5m: DATA_UNAVAILABLE from 2026-08-21 20:35 to 2026-09-19 20:25 (n=8351). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=8351 span=29.0d. Not fabricated.
- LINKUSDT 15m: DATA_UNAVAILABLE from 2026-08-21 20:45 to 2026-09-19 20:15 (n=2783). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=2783 span=29.0d. Not fabricated.
- LINKUSDT 1h: DATA_UNAVAILABLE from 2026-08-21 21:00 to 2026-09-19 20:00 (n=696). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=696 span=29.0d. Not fabricated.
- LTCUSDT 5m: DATA_UNAVAILABLE from 2026-08-21 20:35 to 2026-09-19 20:25 (n=8351). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=8351 span=29.0d. Not fabricated.
- LTCUSDT 15m: DATA_UNAVAILABLE from 2026-08-21 20:45 to 2026-09-19 20:15 (n=2783). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=2783 span=29.0d. Not fabricated.
- LTCUSDT 1h: DATA_UNAVAILABLE from 2026-08-21 21:00 to 2026-09-19 20:00 (n=696). OI_HISTORICAL_DATA_LIMITATION. GET /futures/data/openInterestHist latest ~30 days. Requested 90d; returned n=696 span=29.0d. Not fabricated.

### Liquidation
- BTCUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). No reliable public full-history liquidation series ingested. Not fabricated.
- ETHUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). No reliable public full-history liquidation series ingested. Not fabricated.
- BNBUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). No reliable public full-history liquidation series ingested. Not fabricated.
- SOLUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). No reliable public full-history liquidation series ingested. Not fabricated.
- XRPUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). No reliable public full-history liquidation series ingested. Not fabricated.
- DOGEUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). No reliable public full-history liquidation series ingested. Not fabricated.
- ADAUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). No reliable public full-history liquidation series ingested. Not fabricated.
- AVAXUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). No reliable public full-history liquidation series ingested. Not fabricated.
- LINKUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). No reliable public full-history liquidation series ingested. Not fabricated.
- LTCUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). No reliable public full-history liquidation series ingested. Not fabricated.

### CausalPairUniverse
- BTCUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated.
- ETHUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated.
- BNBUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated.
- SOLUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated.
- XRPUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated.
- DOGEUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated.
- ADAUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated.
- AVAXUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated.
- LINKUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated.
- LTCUSDT n/a: DATA_UNAVAILABLE from n/a to n/a (n=0). EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated.

### TakerFlow
- ADAUSDT 15m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=4). PARTIAL only. Positive taker-buy bars 4/70193 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- ADAUSDT 1h: DATA_UNAVAILABLE from n/a to n/a (n=0). Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated.
- ADAUSDT 5m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=10). PARTIAL only. Positive taker-buy bars 10/210576 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- AVAXUSDT 15m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=4). PARTIAL only. Positive taker-buy bars 4/70192 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- AVAXUSDT 1h: DATA_UNAVAILABLE from n/a to n/a (n=0). Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated.
- AVAXUSDT 5m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=10). PARTIAL only. Positive taker-buy bars 10/210576 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- BNBUSDT 15m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=4). PARTIAL only. Positive taker-buy bars 4/70193 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- BNBUSDT 1h: DATA_UNAVAILABLE from n/a to n/a (n=0). Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated.
- BNBUSDT 5m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=10). PARTIAL only. Positive taker-buy bars 10/210579 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- BTCUSDT 15m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=4). PARTIAL only. Positive taker-buy bars 4/70193 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- BTCUSDT 1h: DATA_UNAVAILABLE from n/a to n/a (n=0). Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated.
- BTCUSDT 5m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=10). PARTIAL only. Positive taker-buy bars 10/210579 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- DOGEUSDT 15m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=4). PARTIAL only. Positive taker-buy bars 4/70193 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- DOGEUSDT 1h: DATA_UNAVAILABLE from n/a to n/a (n=0). Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated.
- DOGEUSDT 5m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=10). PARTIAL only. Positive taker-buy bars 10/210579 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- ETHUSDT 15m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=4). PARTIAL only. Positive taker-buy bars 4/70193 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- ETHUSDT 1h: DATA_UNAVAILABLE from n/a to n/a (n=0). Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated.
- ETHUSDT 5m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=10). PARTIAL only. Positive taker-buy bars 10/210579 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- LINKUSDT 15m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=4). PARTIAL only. Positive taker-buy bars 4/70192 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- LINKUSDT 1h: DATA_UNAVAILABLE from n/a to n/a (n=0). Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated.
- LINKUSDT 5m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=10). PARTIAL only. Positive taker-buy bars 10/210576 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- LTCUSDT 15m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=4). PARTIAL only. Positive taker-buy bars 4/70193 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- LTCUSDT 1h: DATA_UNAVAILABLE from n/a to n/a (n=0). Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated.
- LTCUSDT 5m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=10). PARTIAL only. Positive taker-buy bars 10/210579 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- SOLUSDT 15m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=4). PARTIAL only. Positive taker-buy bars 4/70193 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- SOLUSDT 1h: DATA_UNAVAILABLE from n/a to n/a (n=0). Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated.
- SOLUSDT 5m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=10). PARTIAL only. Positive taker-buy bars 10/210579 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- XRPUSDT 15m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=4). PARTIAL only. Positive taker-buy bars 4/70193 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- XRPUSDT 1h: DATA_UNAVAILABLE from n/a to n/a (n=0). Kline cache JSON has no TakerBuyVolume field on older bars. Not fabricated.
- XRPUSDT 5m: DATA_UNAVAILABLE from 2026-09-19 18:44 to 2026-09-19 19:29 (n=10). PARTIAL only. Positive taker-buy bars 10/210579 (0.0%). First positive 2026-09-19 18:44 last positive 2026-09-19 19:29. Zeros = DATA_UNAVAILABLE, not imbalance 0.
- UNIVERSE 15m: DATA_UNAVAILABLE from n/a to n/a (n=529). Kline cache files=529; head contains TakerBuyVolume in 10. Full bar scan limited to requested coins. Do not treat FIELD_PRESENT as full historical coverage.
- UNIVERSE 1h: DATA_UNAVAILABLE from n/a to n/a (n=529). Kline cache files=529; head contains TakerBuyVolume in 0. Full bar scan limited to requested coins. Do not treat FIELD_PRESENT as full historical coverage.
- UNIVERSE 5m: DATA_UNAVAILABLE from n/a to n/a (n=529). Kline cache files=529; head contains TakerBuyVolume in 10. Full bar scan limited to requested coins. Do not treat FIELD_PRESENT as full historical coverage.

## Missing coverage
- Historical open interest older than the public ~30-day window: `OI_HISTORICAL_DATA_LIMITATION`. Not fabricated. A longer series would require `EXTERNAL_DATA_SOURCE_REQUIRED`.
- Historical liquidation prints: DATA_UNAVAILABLE. Not ingested. Not fabricated.
- Historical order book / depth snapshots: DATA_UNAVAILABLE.
- Causal pair-universe snapshots: `EXTERNAL_DATA_SOURCE_REQUIRED`.
- Predicted next funding (`premiumIndex.lastFundingRate`): not used as a historical series.
- Taker buy on older kline cache files that predate the field, and bars where the stored value is 0: DATA_UNAVAILABLE, not imbalance 0.

## Cache
Persistent research cache root: `artifacts/data/futures-history-v1/`.
Keys: `{dataset}/{symbol}_{timeframe}.json` plus `funding/{symbol}.json`. Dataset version `futures-history-v1`.
Resume: skip Binance when the file already covers the requested start/end. Dedup by timestamp. Checkpoint: `meta/checkpoint.jsonl`.

## Strategy RequiredData
- `ema_rsi_trend`: OHLCV
- `macd_trend`: OHLCV
- `rsi_pullback`: OHLCV
- `bollinger_reversion`: OHLCV
- `donchian_breakout`: OHLCV
- `turtle_tsm`: OHLCV
- `vwap_pullback_trend`: OHLCV
- `volatility_breakout`: OHLCV
- `supertrend_ema_trend`: OHLCV
- `oi_price_momentum`: OHLCV + OpenInterest
- `funding_oi_regime`: OHLCV + Funding + OpenInterest
- `vp_vwap_reversion`: OHLCV
- `liq_sweep_reversal`: OHLCV
- `liq_sweep_continuation`: OHLCV
- `funding_basis_rv`: OHLCV + Funding + MarkPrice + IndexPrice + Basis
- `funding_oi_reversal`: OHLCV + Funding + OpenInterest
- `taker_flow_momentum`: OHLCV + TakerFlow
- `oi_price_volume_regime`: OHLCV + OpenInterest
- `vwap_deviation_reversion`: OHLCV
- `vwap_breakout_volume`: OHLCV
- `failed_breakout_reversal`: OHLCV
- `vol_squeeze_structure`: OHLCV
- `market_structure_trend`: OHLCV
- `market_structure_pullback`: OHLCV
- `atr_normalized_momentum`: OHLCV
- `mtf_trend_structure`: OHLCV + CompletedHtf
- `zscore_mean_reversion`: OHLCV
- `crypto_pairs_arb`: OHLCV + CausalPairUniverse
- `xs_relative_strength`: OHLCV + CrossSectionUniverse
- `regime_strategy_router`: OHLCV

## Research skip states
- `DATA_UNAVAILABLE`: required historical series does not exist for the window (not fabricated, not replaced with zeros).
- `INSUFFICIENT_DATA`: series exists but is too short to evaluate (warmup / window).
- `IMPLEMENTATION_ERROR`: ingest or evaluation threw.
- `NO_TRADES`: evaluation ran and produced zero trades.
- `VALIDATION_FAILED` / `OOS_FAILED`: evaluated PF below 1 on that split.

## Run notes
- Futures historical data ingest. LIVE disabled. Frozen five / Risk Engine / strategy parameters unchanged.
- No 528-universe strategy run. No parameter tuning from Phase 1 OOS.
- FundingRate from GET /fapi/v1/fundingRate is the settled interval rate at fundingTime, not the next predicted rate.
- OI public hist is latest ~30 days only (OI_HISTORICAL_DATA_LIMITATION). Nothing was fabricated.
- BTCUSDT funding n=270
- BTCUSDT 5m mark=25916 index=25916 basis=25916
- BTCUSDT 5m oi n=8352 DATA_UNAVAILABLE
- BTCUSDT 15m mark=8639 index=8639 basis=8639
- BTCUSDT 15m oi n=2784 DATA_UNAVAILABLE
- BTCUSDT 1h mark=2159 index=2159 basis=2159
- BTCUSDT 1h oi n=696 DATA_UNAVAILABLE
- ETHUSDT funding n=270
- ETHUSDT 5m mark=25916 index=25916 basis=25916
- ETHUSDT 5m oi n=8352 DATA_UNAVAILABLE
- ETHUSDT 15m mark=8639 index=8639 basis=8639
- ETHUSDT 15m oi n=2784 DATA_UNAVAILABLE
- ETHUSDT 1h mark=2159 index=2159 basis=2159
- ETHUSDT 1h oi n=696 DATA_UNAVAILABLE
- BNBUSDT funding n=270
- BNBUSDT 5m mark=25916 index=25916 basis=25916
- BNBUSDT 5m oi n=8352 DATA_UNAVAILABLE
- BNBUSDT 15m mark=8639 index=8639 basis=8639
- BNBUSDT 15m oi n=2784 DATA_UNAVAILABLE
- BNBUSDT 1h mark=2159 index=2159 basis=2159
- BNBUSDT 1h oi n=696 DATA_UNAVAILABLE
- SOLUSDT funding n=270
- SOLUSDT 5m mark=25917 index=25917 basis=25917
- SOLUSDT 5m oi n=8352 DATA_UNAVAILABLE
- SOLUSDT 15m mark=8639 index=8639 basis=8639
- SOLUSDT 15m oi n=2784 DATA_UNAVAILABLE
- SOLUSDT 1h mark=2159 index=2159 basis=2159
- SOLUSDT 1h oi n=696 DATA_UNAVAILABLE
- XRPUSDT funding n=270
- XRPUSDT 5m mark=25917 index=25917 basis=25917
- XRPUSDT 5m oi n=8352 DATA_UNAVAILABLE
- XRPUSDT 15m mark=8639 index=8639 basis=8639
- XRPUSDT 15m oi n=2783 DATA_UNAVAILABLE
- XRPUSDT 1h mark=2159 index=2159 basis=2159
- XRPUSDT 1h oi n=696 DATA_UNAVAILABLE
- DOGEUSDT funding n=270
- DOGEUSDT 5m mark=25917 index=25917 basis=25917
- DOGEUSDT 5m oi n=8351 DATA_UNAVAILABLE
- DOGEUSDT 15m mark=8639 index=8639 basis=8639
- DOGEUSDT 15m oi n=2783 DATA_UNAVAILABLE
- DOGEUSDT 1h mark=2159 index=2159 basis=2159
- DOGEUSDT 1h oi n=696 DATA_UNAVAILABLE
- ADAUSDT funding n=270
- ADAUSDT 5m mark=25917 index=25917 basis=25917
- ADAUSDT 5m oi n=8351 DATA_UNAVAILABLE
- ADAUSDT 15m mark=8639 index=8639 basis=8639
- ADAUSDT 15m oi n=2783 DATA_UNAVAILABLE
- ADAUSDT 1h mark=2159 index=2159 basis=2159
- ADAUSDT 1h oi n=696 DATA_UNAVAILABLE
- AVAXUSDT funding n=270
- AVAXUSDT 5m mark=25917 index=25917 basis=25917
- AVAXUSDT 5m oi n=8351 DATA_UNAVAILABLE
- AVAXUSDT 15m mark=8639 index=8639 basis=8639
- AVAXUSDT 15m oi n=2783 DATA_UNAVAILABLE
- AVAXUSDT 1h mark=2159 index=2159 basis=2159
- AVAXUSDT 1h oi n=696 DATA_UNAVAILABLE
- LINKUSDT funding n=270
- LINKUSDT 5m mark=25917 index=25917 basis=25917
- LINKUSDT 5m oi n=8351 DATA_UNAVAILABLE
- LINKUSDT 15m mark=8639 index=8639 basis=8639
- LINKUSDT 15m oi n=2783 DATA_UNAVAILABLE
- LINKUSDT 1h mark=2159 index=2159 basis=2159
- LINKUSDT 1h oi n=696 DATA_UNAVAILABLE
- LTCUSDT funding n=270
- LTCUSDT 5m mark=25917 index=25917 basis=25917
- LTCUSDT 5m oi n=8351 DATA_UNAVAILABLE
- LTCUSDT 15m mark=8640 index=8640 basis=8640
- LTCUSDT 15m oi n=2783 DATA_UNAVAILABLE
- LTCUSDT 1h mark=2159 index=2159 basis=2159
- LTCUSDT 1h oi n=696 DATA_UNAVAILABLE
- BTCUSDT 5m taker-cache n=25909 validImbalance=10
- BTCUSDT 15m taker-cache n=8637 validImbalance=4
- BTCUSDT 1h taker-cache n=2158 validImbalance=0
- ETHUSDT 5m taker-cache n=25909 validImbalance=10
- ETHUSDT 15m taker-cache n=8637 validImbalance=4
- ETHUSDT 1h taker-cache n=2158 validImbalance=0
- BNBUSDT 5m taker-cache n=25909 validImbalance=10
- BNBUSDT 15m taker-cache n=8637 validImbalance=4
- BNBUSDT 1h taker-cache n=2158 validImbalance=0
- SOLUSDT 5m taker-cache n=25909 validImbalance=10
- SOLUSDT 15m taker-cache n=8637 validImbalance=4
- SOLUSDT 1h taker-cache n=2158 validImbalance=0
- XRPUSDT 5m taker-cache n=25909 validImbalance=10
- XRPUSDT 15m taker-cache n=8637 validImbalance=4
- XRPUSDT 1h taker-cache n=2158 validImbalance=0
- DOGEUSDT 5m taker-cache n=25909 validImbalance=10
- DOGEUSDT 15m taker-cache n=8637 validImbalance=4
- DOGEUSDT 1h taker-cache n=2158 validImbalance=0
- ADAUSDT 5m taker-cache n=25909 validImbalance=10
- ADAUSDT 15m taker-cache n=8637 validImbalance=4
- ADAUSDT 1h taker-cache n=2158 validImbalance=0
- AVAXUSDT 5m taker-cache n=25909 validImbalance=10
- AVAXUSDT 15m taker-cache n=8637 validImbalance=4
- AVAXUSDT 1h taker-cache n=2158 validImbalance=0
- LINKUSDT 5m taker-cache n=25909 validImbalance=10
- LINKUSDT 15m taker-cache n=8637 validImbalance=4
- LINKUSDT 1h taker-cache n=2158 validImbalance=0
- LTCUSDT 5m taker-cache n=25909 validImbalance=10
- LTCUSDT 15m taker-cache n=8637 validImbalance=4
- LTCUSDT 1h taker-cache n=2158 validImbalance=0
- Taker audit rows kept: 33. Full bar scan limited to Phase 1 coins.
