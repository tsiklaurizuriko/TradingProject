# Advanced six strategy pilot

Research templates added to the Strategy Engine. Frozen five baselines were not modified. LIVE = OFF.
This pilot does **not** promote Paper or LIVE. Do not treat any PF as profitable.

## Status
- **Turtle Time-Series Momentum** (`turtle_tsm`): OOS_FAILED; OOS n=1868 PF 0,74585073; Closed kline candles only.
- **VWAP Pullback Trend** (`vwap_pullback_trend`): OOS_FAILED; OOS n=1744 PF 0,57412368; Closed kline candles only.
- **Volatility Breakout** (`volatility_breakout`): OOS_FAILED; OOS n=434 PF 0,77601463; Closed kline candles only.
- **Supertrend EMA Trend** (`supertrend_ema_trend`): OOS_FAILED; OOS n=189 PF 0,96888885; Closed kline candles only.
- **Open Interest Price Momentum** (`oi_price_momentum`): DATA_UNAVAILABLE; OOS n=0 PF N/A; Historical open interest, timestamp-aligned to closed candles. Missing series = DATA_UNAVAILABLE.
- **Funding Rate Price OI Regime** (`funding_oi_regime`): DATA_UNAVAILABLE; OOS n=0 PF N/A; Historical funding rate and open interest, timestamp-aligned to closed candles. Missing series = DATA_UNAVAILABLE.

## LONG / SHORT (OOS BASE)
- turtle_tsm: Combined PF 0,74585073 n=1868 exp -6.73; LONG PF 1,01629143 n=1006 exp 0.43; SHORT PF 0,42537583 n=862 exp -15.09.
- vwap_pullback_trend: Combined PF 0,57412368 n=1744 exp -8.70; LONG PF 0,70391682 n=890 exp -6.36; SHORT PF 0,42357887 n=854 exp -11.13.
- volatility_breakout: Combined PF 0,77601463 n=434 exp -4.32; LONG PF 1,02229209 n=207 exp 0.44; SHORT PF 0,53925054 n=227 exp -8.66.
- supertrend_ema_trend: Combined PF 0,96888885 n=189 exp -0.82; LONG PF 1,08929103 n=118 exp 2.36; SHORT PF 0,76449955 n=71 exp -6.09.
- oi_price_momentum: Combined PF N/A n=0 exp 0.00; LONG PF N/A n=0 exp 0.00; SHORT PF N/A n=0 exp 0.00.
- funding_oi_regime: Combined PF N/A n=0 exp 0.00; LONG PF N/A n=0 exp 0.00; SHORT PF N/A n=0 exp 0.00.

## Cost sensitivity (OOS)
- turtle_tsm
  - BASE: PF 0,74585073 n=1868 exp -6.73
  - HIGH: PF 0,66454060 n=1856 exp -9.51
  - STRESS: PF 0,59376540 n=1850 exp -12.25
- vwap_pullback_trend
  - BASE: PF 0,57412368 n=1744 exp -8.70
  - HIGH: PF 0,49494884 n=1735 exp -11.41
  - STRESS: PF 0,43242395 n=1732 exp -14.05
- volatility_breakout
  - BASE: PF 0,77601463 n=434 exp -4.32
  - HIGH: PF 0,66290628 n=433 exp -7.20
  - STRESS: PF 0,56875524 n=431 exp -10.19
- supertrend_ema_trend
  - BASE: PF 0,96888885 n=189 exp -0.82
  - HIGH: PF 0,86913075 n=189 exp -3.72
  - STRESS: PF 0,78420908 n=189 exp -6.61
- oi_price_momentum
  - BASE: PF N/A n=0 exp 0.00
  - HIGH: PF N/A n=0 exp 0.00
  - STRESS: PF N/A n=0 exp 0.00
- funding_oi_regime
  - BASE: PF N/A n=0 exp 0.00
  - HIGH: PF N/A n=0 exp 0.00
  - STRESS: PF N/A n=0 exp 0.00

## Open interest
Historical OI series is **not** wired into backtest/paper/LIVE. `oi_price_momentum` = DATA_UNAVAILABLE. No OI was fabricated.

## Funding
Historical funding series is **not** wired into backtest/paper/LIVE. `funding_oi_regime` = DATA_UNAVAILABLE. No funding was fabricated.

## LIVE
LIVE remains OFF. Catalog research rows seed as disabled.

## Run notes
- Advanced six pilot. LIVE disabled. Frozen five templates unchanged. No Paper/LIVE promotion.
- Symbols 10. Timeframes 5m,15m,1h.
- OI and funding historical series are not loaded; those two templates stay DATA_UNAVAILABLE.
- BTCUSDT 5m: 25920 bars cache miss downloaded 17
- BTCUSDT 15m: 8640 bars cache miss downloaded 5
- BTCUSDT 1h: 2160 bars cache miss downloaded 2
- ETHUSDT 5m: 25920 bars cache miss downloaded 17
- ETHUSDT 15m: 8640 bars cache miss downloaded 5
- ETHUSDT 1h: 2160 bars cache miss downloaded 2
- BNBUSDT 5m: 25920 bars cache miss downloaded 17
- BNBUSDT 15m: 8640 bars cache miss downloaded 5
- BNBUSDT 1h: 2160 bars cache miss downloaded 2
- SOLUSDT 5m: 25920 bars cache miss downloaded 17
- SOLUSDT 15m: 8640 bars cache miss downloaded 5
- SOLUSDT 1h: 2160 bars cache miss downloaded 2
- XRPUSDT 5m: 25920 bars cache miss downloaded 17
- XRPUSDT 15m: 8640 bars cache miss downloaded 5
- XRPUSDT 1h: 2160 bars cache miss downloaded 2
- DOGEUSDT 5m: 25920 bars cache miss downloaded 17
- DOGEUSDT 15m: 8640 bars cache miss downloaded 5
- DOGEUSDT 1h: 2160 bars cache miss downloaded 2
- ADAUSDT 5m: 25920 bars cache miss downloaded 17
- ADAUSDT 15m: 8640 bars cache miss downloaded 5
- ADAUSDT 1h: 2160 bars cache miss downloaded 2
- AVAXUSDT 5m: 25920 bars cache miss downloaded 17
- AVAXUSDT 15m: 8640 bars cache miss downloaded 5
- AVAXUSDT 1h: 2160 bars cache miss downloaded 2
- LINKUSDT 5m: 25920 bars cache miss downloaded 17
- LINKUSDT 15m: 8640 bars cache miss downloaded 5
- LINKUSDT 1h: 2160 bars cache miss downloaded 2
- LTCUSDT 5m: 25920 bars cache miss downloaded 17
- LTCUSDT 15m: 8640 bars cache miss downloaded 5
- LTCUSDT 1h: 2160 bars cache miss downloaded 2
