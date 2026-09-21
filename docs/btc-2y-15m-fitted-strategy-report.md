# BTCUSDT 15m historically fitted strategy

RESEARCH ONLY. Intentionally fitted on the historical BTCUSDT 15m sample. **Not validated alpha. Not paper-validated. Not live-ready.** Isolated LOW Risk Engine, LIVE, and the 43-strategy registry were not changed.

## 1. Executive Summary

**NO STRATEGY FOUND UNDER CONSTRAINTS**

No legitimate combination reached ≥2,000 completed trades **and** ≥$100 net profit after fees and slippage on Isolated LOW $1,000 / 0.5% / 3x.

The two constraints conflict on this sample: combinations that completed ≥2,000 trades all finished net-negative after costs; combinations that finished ≥$100 net did so with far fewer than 2,000 trades. The gap was not closed by searching LONG, SHORT, and BOTH, nor by the SL/TP/hold grid.

## 2. Exact Historical Dataset

| Field | Value |
|---|---|
| Symbol | BTCUSDT USDⓈ-M perpetual |
| Timeframe | 15m only |
| First open | 2024-09-18 00:00 UTC |
| Last close | 2026-09-19 21:14 UTC |
| Closed bars | 70,261 |
| Span | 731.89 days |
| Source | `artifacts/strategy-validation-cache/BTCUSDT_15m.json` (Binance klines, closed bars) |

## 3. Search Space

Pre-registered conventional features only (EMA, SMA, RSI, MACD, Bollinger, Keltner, Donchian, ADX, ATR/relative volume, ROC, stochastic, z-score). Max 5 decision components. No neural nets.

- Entry recipes: **21**
- Sides: LONG / SHORT / BOTH
- SL grid: 0.25%, 0.50%, 0.75%, 1.00%, 1.50%, 2.00%, 2.50%, 3.00%, 4.00%
- TP grid: 0.25%, 0.50%, 0.75%, 1.00%, 1.50%, 2.00%, 2.50%, 3.00%, 4.00%, 5.00%, 6.00%
- Time exits (15m bars): 8, 16, 32, 48, 96, 192
- R:R pairs are included as SL/TP combinations (e.g. 1% / 2% = 1:2).

## 4. Number of Candidates Tested

**37,422** full Isolated-book backtests (recipe × side × SL × TP × hold).
Feasible (≥2,000 trades and ≥$100 net): **0**.
Combinations with ≥2000 trades: 10925.
Combinations with ≥$100 net: 2.
Intersection (feasible): 0.

## 5. Optimization Method

Cartesian grid on the **entire** 2-year sample (intentional fit). Score among feasible names: net $ + 80×(PF−1) − 8×DD% + 0.02×trades + 40×monthly win rate − 12×complexity − 30×|Y1−Y2| gap. Tie-break prefers simpler recipes. Parameters were **not** retuned on Year 1 / Year 2 / quarters.

Execution: closed-bar signal, next 15m open fill, SL before TP, taker 0.04%/side, 0.02% slip in prices, Isolated one position, 0.5% of current equity, 3x cap, UTC daily 3% halt. Funding not in this kline replay.

## 6. Final Strategy Rules

None selected.

## 7. Exact Parameters

n/a

## 8. Final 2-Year Backtest

n/a — no strategy selected.

## 9. Equity Curve Statistics

No logged equity curve (no selected strategy).

## 10. Drawdown

n/a — no strategy selected.

## 11. Trade Statistics

n/a — no strategy selected.

## 12. Cost Breakdown

n/a — no strategy selected.

## 13. Monthly Results

n/a

## 14. Quarterly Results

n/a

## 15. Year 1 vs Year 2

n/a — no strategy selected.

## 16. Long vs Short

n/a — no strategy selected.

## 17. Regime Breakdown

Causal at the signal bar (fill−1): BTC ATR% 14 tercile over the last 50 closed bars; trend = EMA20 vs EMA50. Descriptive only.

n/a — no strategy selected.

## 18. Random Baselines

n/a — no strategy selected.

## 19. Overfitting Risk

This search tested **37,422** combinations on the **same** sample used to pick the winner. That is an in-sample fit. Multiple-testing is severe. Year/quarter tables are descriptive, not a confirmation set. Forward behavior is the only honest test, and it has not been measured yet.

Best 20 by score (feasible first, else overall):

| Recipe | Side | SL | TP | Hold | n | Net $ | PF | DD% | Score |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| ema21_rsi_pullback | SHORT | 1.00% | 0.75% | 192 | 9 | 20.67 | 4.7557 | 0.55 | 285.7 |
| ema21_rsi_pullback | SHORT | 1.00% | 0.75% | 96 | 9 | 20.67 | 4.7557 | 0.55 | 285.7 |
| ema21_rsi_pullback | SHORT | 1.00% | 0.75% | 48 | 9 | 20.67 | 4.7557 | 0.55 | 285.7 |
| ema21_rsi_pullback | SHORT | 1.00% | 0.75% | 32 | 9 | 17.46 | 4.1725 | 0.55 | 231.1 |
| ema21_rsi_pullback | LONG | 1.50% | 1.50% | 16 | 12 | 17.78 | 3.9666 | 0.54 | 223.6 |
| ema21_rsi_pullback | LONG | 0.75% | 1.50% | 16 | 12 | 33.62 | 3.6364 | 0.58 | 222.6 |
| ema21_rsi_pullback | LONG | 1.50% | 1.00% | 16 | 12 | 16.78 | 3.8015 | 0.54 | 208.7 |
| ema21_rsi_pullback | LONG | 0.75% | 1.00% | 16 | 12 | 31.61 | 3.4819 | 0.58 | 207.8 |
| ema21_rsi_pullback | LONG | 3.00% | 0.50% | 96 | 12 | 5.41 | 3.7946 | 0.19 | 207.8 |
| ema21_rsi_pullback | LONG | 4.00% | 0.50% | 96 | 12 | 4.06 | 3.7961 | 0.15 | 206.9 |
| ema21_rsi_pullback | LONG | 1.50% | 6.00% | 16 | 12 | 15.89 | 3.6535 | 0.54 | 194.9 |
| ema21_rsi_pullback | LONG | 1.50% | 5.00% | 16 | 12 | 15.89 | 3.6535 | 0.54 | 194.9 |
| ema21_rsi_pullback | LONG | 1.50% | 4.00% | 16 | 12 | 15.89 | 3.6535 | 0.54 | 194.9 |
| ema21_rsi_pullback | LONG | 1.50% | 3.00% | 16 | 12 | 15.89 | 3.6535 | 0.54 | 194.9 |
| ema21_rsi_pullback | LONG | 1.50% | 2.50% | 16 | 12 | 15.89 | 3.6535 | 0.54 | 194.9 |
| ema21_rsi_pullback | LONG | 1.50% | 2.00% | 16 | 12 | 15.89 | 3.6535 | 0.54 | 194.9 |
| ema21_rsi_pullback | LONG | 0.75% | 6.00% | 16 | 12 | 29.79 | 3.3409 | 0.58 | 193.5 |
| ema21_rsi_pullback | LONG | 0.75% | 5.00% | 16 | 12 | 29.79 | 3.3409 | 0.58 | 193.5 |
| ema21_rsi_pullback | LONG | 0.75% | 4.00% | 16 | 12 | 29.79 | 3.3409 | 0.58 | 193.5 |
| ema21_rsi_pullback | LONG | 0.75% | 3.00% | 16 | 12 | 29.79 | 3.3409 | 0.58 | 193.5 |

Closest 10 (constraint failures):

| Recipe | Side | SL | TP | Hold | n | Net $ | Why short |
|---|---|---:|---:|---:|---:|---:|---|
| vol_spike_ema_trend | BOTH | 4.00% | 0.50% | 48 | 2322 | -205.94 | net $-205.94 < $100 |
| bb20_2_break | BOTH | 4.00% | 0.75% | 32 | 2041 | -210.77 | net $-210.77 < $100 |
| rsi7_mr_25_75 | BOTH | 4.00% | 1.50% | 16 | 2020 | -213.66 | net $-213.66 < $100 |
| macd_cross | BOTH | 4.00% | 0.75% | 32 | 2083 | -217.31 | net $-217.31 < $100 |
| ema_cross_5_13 | LONG | 4.00% | 6.00% | 16 | 2008 | -230.15 | net $-230.15 < $100 |
| vol_spike_ema_trend | BOTH | 2.50% | 5.00% | 192 | 476 | 138.16 | trades 476 < 2000 |
| bb20_2_break | BOTH | 4.00% | 5.00% | 192 | 408 | 112.58 | trades 408 < 2000 |
| ema21_rsi_pullback | SHORT | 1.00% | 0.75% | 192 | 9 | 20.67 | trades 9 < 2000 and net $20.67 < $100 |
| ema21_rsi_pullback | SHORT | 1.00% | 0.75% | 96 | 9 | 20.67 | trades 9 < 2000 and net $20.67 < $100 |
| ema21_rsi_pullback | SHORT | 1.00% | 0.75% | 48 | 9 | 20.67 | trades 9 < 2000 and net $20.67 < $100 |

## 20. Frozen Strategy Specification

No trading rules were frozen. `docs/btc-2y-15m-fitted-strategy-frozen.json` records the **NO STRATEGY FOUND UNDER CONSTRAINTS** classification, the dataset bounds, Isolated LOW sizing, Model B execution, and costs. There is nothing to forward-test as a fitted entry/exit spec.

## Forward-test questions

1. Did you find ONE BTCUSDT 15m strategy satisfying ≥2,000 trades and ≥$100 net after costs? **NO**
2. Exact rules: n/a
3. Exact parameters: n/a
4. Final equity from $1,000: n/a
5. Maximum drawdown: n/a
6. Profit Factor: n/a
7. Trades: n/a
8. Fees / slippage: n/a
9. Year 1 vs Year 2: n/a
10. vs random entry: n/a
11. Candidates tested: 37,422
12. Frozen config: `docs/btc-2y-15m-fitted-strategy-frozen.json`

- BTCUSDT 15m historically fitted strategy. RESEARCH ONLY. Not validated alpha. LIVE off. Registry unchanged.
- Model B: signal on closed 15m, fill next open, SL before TP on the same bar.
- Sizing: Isolated LOW $1,000 start, 0.5% of current equity, 3x cap, daily 3% halt, one position.
- Costs: taker 0.04% per side + 0.02% slip in fill prices. Funding not applied (not in this kline replay).
- Dataset 2024-09-18 00:00 UTC → 2026-09-19 21:14 UTC. Bars=70261.
- Entry recipes: 21. Sides: long/short/both. SL 9 × TP 11 × hold 6.
- Declared combinations: 37422.
- Combinations with ≥2000 trades: 10925.
- Combinations with ≥$100 net: 2.
- Intersection (feasible): 0.
- NO STRATEGY FOUND UNDER CONSTRAINTS.
