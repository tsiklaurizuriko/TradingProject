# Pump ride, live definition

Live entry is only the first closed 15m bar at least 16% above the 24h low, including a bar that jumps past 26% in one print. Later bars of that same cross are not entries. The bar is green and closes in its upper half, 24h turnover is at least 3M USDT, and the coin is already up 20% over 30 days. Hourly volume above 7x the prior 7-day average, and a close above the prior 30-day high, are skips. A wide entry bar is still an entry: the shared 5x range-shock cap does not apply. Exchange stop 12%, trail 25% below the peak high on closed bars, 4-day cap. No 26% rise cap.

The tables below measured that book with the rise also capped at 26%. Live entry leaves the cap off. A replay of the uncapped live book on the same cache (510 coins, through 2026-09-30): all trades mean +1.04%, PF 1.17; OOS mean +0.88%, PF 1.13. Thirty slots compounded to 2.04x with a 43% max drawdown. The 26% cap was slightly worse on both.

## Exchange stop ratchet

After the peak is `arm` above entry, the exchange stop moves to max(breakeven, peak high × (1 − ratchet)). It fills intrabar. The close-based 25% trail stays. 0.3% cost.

| Arm | Ratchet | IS mean / PF | VAL mean / PF | OOS mean / PF | Win |
| ---: | ---: | --- | --- | --- | ---: |
| off | 0 % | 2.97 % / 1.59 | 0.60 % / 1.08 | 2.42 % / 1.35 | 37 % |
| 10 % | 8 % | 1.07 % / 1.25 | -0.20 % / 0.96 | -0.14 % / 0.97 | 55 % |
| 10 % | 15 % | 0.93 % / 1.22 | 0.27 % / 1.05 | 0.16 % / 1.03 | 34 % |
| 20 % | 20 % | 1.71 % / 1.35 | 0.96 % / 1.14 | 0.81 % / 1.12 | 38 % |
| 30 % | 25 % | 2.47 % / 1.49 | 0.78 % / 1.11 | 1.19 % / 1.17 | 38 % |
| 50 % | 25 % | 2.46 % / 1.48 | 0.90 % / 1.12 | 1.26 % / 1.18 | 37 % |
| 20 % | 30 % | 2.63 % / 1.54 | 0.67 % / 1.10 | 2.40 % / 1.37 | 35 % |

| Cost round trip | IS n / mean / PF | VAL n / mean / PF | OOS n / mean / PF | Win | ≥+20% | Portfolio x | Max DD | Losing months |
| ---: | --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| 0.3 % | 807 / 2.97 % / 1.59 | 417 / 0.60 % / 1.08 | 876 / 2.42 % / 1.35 | 37 % | 11 % | 21.18 | 47 % | 11/24 |
| 0.6 % | 807 / 2.67 % / 1.51 | 417 / 0.30 % / 1.04 | 876 / 2.12 % / 1.30 | 37 % | 11 % | 13.47 | 50 % | 11/24 |
| 1.0 % | 807 / 2.27 % / 1.42 | 417 / -0.10 % / 0.99 | 876 / 1.72 % / 1.23 | 36 % | 11 % | 7.35 | 54 % | 11/24 |

Trades per month, mean net at 0.3%:

- 2024-10: n 13, mean -9.33 %, best 7 %
- 2024-11: n 154, mean 4.36 %, best 67 %
- 2024-12: n 175, mean 2.10 %, best 81 %
- 2025-01: n 16, mean -0.24 %, best 53 %
- 2025-02: n 11, mean -9.36 %, best 1 %
- 2025-03: n 8, mean 8.02 %, best 63 %
- 2025-04: n 23, mean 3.12 %, best 75 %
- 2025-05: n 154, mean 3.29 %, best 85 %
- 2025-06: n 22, mean -1.43 %, best 47 %
- 2025-07: n 102, mean 1.42 %, best 249 %
- 2025-08: n 74, mean 8.85 %, best 457 %
- 2025-09: n 70, mean 5.66 %, best 124 %
- 2025-10: n 81, mean -2.83 %, best 74 %
- 2025-11: n 53, mean 0.70 %, best 153 %
- 2025-12: n 44, mean 2.40 %, best 86 %
- 2026-01: n 105, mean -2.86 %, best 153 %
- 2026-02: n 46, mean 6.30 %, best 294 %
- 2026-03: n 104, mean 2.28 %, best 186 %
- 2026-04: n 156, mean -0.30 %, best 88 %
- 2026-05: n 181, mean 0.77 %, best 257 %
- 2026-06: n 67, mean 1.06 %, best 183 %
- 2026-07: n 79, mean -1.06 %, best 155 %
- 2026-08: n 170, mean 2.88 %, best 529 %
- 2026-09: n 192, mean 7.49 %, best 1,254 %

Largest trades:

- LSKUSDT 2026-09-11 02:00 net 1,254 %
- TUTUSDT 2026-08-07 04:15 net 529 %
- MYXUSDT 2025-08-03 08:00 net 457 %
- POWERUSDT 2026-02-24 14:00 net 294 %
- LABUSDT 2026-05-29 05:15 net 257 %
- BANANAS31USDT 2025-07-08 17:00 net 249 %
- USELESSUSDT 2026-08-31 14:45 net 232 %
- STOUSDT 2026-03-31 17:30 net 186 %
- QNTUSDT 2026-09-25 15:45 net 185 %
- VELVETUSDT 2026-06-04 17:45 net 183 %
- BANKUSDT 2026-07-22 07:30 net 155 %
- SOONUSDT 2025-11-05 14:15 net 153 %
