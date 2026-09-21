# Model B Wave-6 trade-path report

Existing 43-strategy signals only. No new entries, no router, no FrozenRisk/LOW change, LIVE off, not VALIDATED_FOR_PAPER.
Question: do those signals contain any economically useful information about the **future price path** (MFE/MAE, first-touch, volatility, holding horizon) even when they do not predict final 2%/4% direction?

**Verdict: VOLATILITY INFORMATION FOUND**

This is **not** directional alpha and **not** a tradable Isolated book.

OOS 24h range is +23% to +37% larger after a signal than after a random fill on the same coin/TF (all three timeframes, validation same sign). MFE **and** MAE both inflate. Flip-direction P(+1R before −1R) ≈ signal P (OOS ALL 41.61% vs 40.85%). Short-horizon closes are worse than random. The pre-registered TP/SL grid, time exits, and breakeven/trail rules do not beat random-entry OOS after an IS freeze. Round-trip costs make TP1/SL1 worse than random at every multiplier.

TYPE C means: the existing signals cluster in high-future-range windows. They do not say which side pays. A P(+1R before −1R) model is not justified (direction is symmetric). Do not enable LIVE. Do not mark VALIDATED_FOR_PAPER.

1R = existing LOW stop distance (2% of slipped next-open fill). Path labels do not use the 2%/4% book as an exit. Same-bar both sides → adverse first.

## 1. Dataset

Complete 24h signal paths: **666891**. Random-entry controls: **650107** (same coins, timeframes, phase, long/short counts; signal fill bars excluded).
Window 2024-09-18 → 2026-09-19. Coins: BTC, ETH, BNB, SOL, XRP, DOGE, ADA, AVAX, LINK, LTC. TFs: 5m / 15m / 1h.
IS < 2025-11-30, VAL < 2026-04-25, then OOS. Occupancy matches Wave-5 Isolated one-position harvest.

| Key | n | MFE | MAE | Range | P+1R | P−1R first |
|---|---:|---:|---:|---:|---:|---:|
| IS|15m|sig | 161664 | 2.1241 | 2.0142 | 1.4306 | 46.92% | 47.33% |
| IS|1h|sig | 82578 | 2.0878 | 1.8594 | 1.9271 | 46.54% | 46.81% |
| IS|5m|sig | 218904 | 2.1906 | 2.1269 | 1.1257 | 47.47% | 48.09% |
| IS|ALL|sig | 463146 | 2.1491 | 2.0399 | 1.3750 | 47.11% | 47.60% |
| OOS|15m|sig | 31799 | 1.3668 | 1.3180 | 1.0975 | 40.48% | 41.31% |
| OOS|1h|sig | 19575 | 1.3124 | 1.2297 | 1.3561 | 38.63% | 41.41% |
| OOS|5m|sig | 38367 | 1.4551 | 1.3973 | 0.8985 | 42.29% | 42.13% |
| OOS|ALL|sig | 89741 | 1.3927 | 1.3327 | 1.0689 | 40.85% | 41.68% |
| VALIDATION|15m|sig | 40473 | 1.5270 | 1.5176 | 1.0702 | 43.68% | 45.35% |
| VALIDATION|1h|sig | 23219 | 1.5119 | 1.4651 | 1.4524 | 44.91% | 43.22% |
| VALIDATION|5m|sig | 50312 | 1.6112 | 1.5790 | 0.8729 | 45.78% | 44.79% |
| VALIDATION|ALL|sig | 114004 | 1.5611 | 1.5340 | 1.0610 | 44.86% | 44.67% |

## 2. MFE distribution (24h, R units)

Mean maximum favorable excursion after slipped entry. Signal vs random-entry vs flipped direction.

| Slice | Signal | Random entry | Flip |
|---|---:|---:|---:|
| OOS ALL | 1.3927 | 1.1023 | 1.3327 |
| VALIDATION ALL | 1.5611 | 1.3231 | 1.5340 |
| IS ALL | 2.1491 | 1.6851 | 2.0399 |
| OOS 5m | 1.4551 | 1.1229 | 1.3973 |
| VALIDATION 5m | 1.6112 | 1.3303 | 1.5790 |
| IS 5m | 2.1906 | 1.7191 | 2.1269 |
| OOS 15m | 1.3668 | 1.0998 | 1.3180 |
| VALIDATION 15m | 1.5270 | 1.3272 | 1.5176 |
| IS 15m | 2.1241 | 1.6794 | 2.0142 |
| OOS 1h | 1.3124 | 1.0659 | 1.2297 |
| VALIDATION 1h | 1.5119 | 1.3000 | 1.4651 |
| IS 1h | 2.0878 | 1.5869 | 1.8594 |

## 3. MAE distribution (24h, R units)

Mean maximum adverse excursion. Lower is better for a directional signal.

| Slice | Signal | Random entry | Flip |
|---|---:|---:|---:|
| OOS ALL | 1.3327 | 1.1090 | 1.3927 |
| VALIDATION ALL | 1.5340 | 1.3281 | 1.5611 |
| IS ALL | 2.0399 | 1.7102 | 2.1491 |
| OOS 5m | 1.3973 | 1.1112 | 1.4551 |
| VALIDATION 5m | 1.5790 | 1.3535 | 1.6112 |
| IS 5m | 2.1269 | 1.7357 | 2.1906 |
| OOS 15m | 1.3180 | 1.1131 | 1.3668 |
| VALIDATION 15m | 1.5176 | 1.3172 | 1.5270 |
| IS 15m | 2.0142 | 1.7121 | 2.1241 |
| OOS 1h | 1.2297 | 1.0979 | 1.3124 |
| VALIDATION 1h | 1.4651 | 1.2915 | 1.5119 |
| IS 1h | 1.8594 | 1.6215 | 2.0878 |

## 4. First-touch probabilities

OOS ALL signals n=89741. Rate of **+F R before −A R** (same-bar adverse first).

| +F \ −A | 0.25R | 0.50R | 0.75R | 1.00R | 1.25R | 1.50R | 2.00R |
|---|---:|---:|---:|---:|---:|---:|---:|
| +0,25R | 45.16% | 63.35% | 72.83% | 77.83% | 80.75% | 82.41% | 83.88% |
| +0,50R | 30.70% | 47.20% | 56.95% | 62.58% | 65.99% | 67.92% | 69.63% |
| +0,75R | 22.80% | 36.60% | 45.21% | 50.56% | 53.73% | 55.55% | 57.18% |
| +1,00R | 17.87% | 28.97% | 36.34% | 40.85% | 43.52% | 45.22% | 46.67% |
| +1,25R | 14.33% | 23.40% | 29.47% | 33.18% | 35.47% | 36.96% | 38.22% |
| +1,50R | 11.49% | 19.04% | 24.08% | 27.17% | 29.19% | 30.39% | 31.39% |
| +2,00R | 7.85% | 13.08% | 16.43% | 18.55% | 19.91% | 20.77% | 21.50% |

P(+1R before −1R) by slice:

| Slice | Signal | Random entry | Flip |
|---|---:|---:|---:|
| OOS ALL | 0.4085 | 0.3540 | 0.4161 |
| VALIDATION ALL | 0.4486 | 0.4119 | 0.4454 |
| IS ALL | 0.4711 | 0.4407 | 0.4734 |
| OOS 5m | 0.4229 | 0.3610 | 0.4212 |
| VALIDATION 5m | 0.4578 | 0.4128 | 0.4471 |
| IS 5m | 0.4747 | 0.4450 | 0.4799 |
| OOS 15m | 0.4048 | 0.3518 | 0.4119 |
| VALIDATION 15m | 0.4368 | 0.4140 | 0.4515 |
| IS 15m | 0.4692 | 0.4400 | 0.4720 |
| OOS 1h | 0.3863 | 0.3436 | 0.4128 |
| VALIDATION 1h | 0.4491 | 0.4060 | 0.4309 |
| IS 1h | 0.4654 | 0.4285 | 0.4592 |

## 5. Holding-period analysis

1-bar and 4-bar close in R. Whip = both +0.5R and −0.5R inside 24h. Delay = 1-bar close ≤0 but MFE ≥0.5R.

| Key | n | Ret1R | Ret4R | Cont1 | Whip | Delay |
|---|---:|---:|---:|---:|---:|---:|
| IS|15m|flip | 161664 | 0.0061 | 0.0169 | 54.40% | 61.22% | 33.54% |
| IS|15m|rnd | 161664 | -0.0099 | -0.0110 | 47.26% | 52.57% | 36.68% |
| IS|15m|sig | 161664 | -0.0061 | -0.0169 | 45.60% | 61.22% | 39.86% |
| IS|1h|flip | 82578 | 0.0058 | -0.0133 | 54.30% | 58.11% | 29.89% |
| IS|1h|rnd | 66436 | -0.0079 | -0.0092 | 48.64% | 49.95% | 32.36% |
| IS|1h|sig | 82578 | -0.0058 | 0.0133 | 45.70% | 58.11% | 36.13% |
| IS|5m|flip | 218904 | 0.0096 | 0.0128 | 54.76% | 63.12% | 34.95% |
| IS|5m|rnd | 218904 | -0.0098 | -0.0098 | 45.21% | 53.38% | 39.55% |
| IS|5m|sig | 218904 | -0.0096 | -0.0128 | 45.24% | 63.12% | 42.24% |
| IS|ALL|flip | 463146 | 0.0077 | 0.0096 | 54.55% | 61.57% | 33.55% |
| IS|ALL|rnd | 447004 | -0.0096 | -0.0101 | 46.46% | 52.58% | 37.44% |
| IS|ALL|sig | 463146 | -0.0077 | -0.0096 | 45.45% | 61.57% | 40.32% |
| OOS|15m|flip | 31799 | 0.0107 | 0.0264 | 56.95% | 45.37% | 27.88% |
| OOS|15m|rnd | 31799 | -0.0103 | -0.0127 | 45.03% | 35.94% | 33.20% |
| OOS|15m|sig | 31799 | -0.0107 | -0.0264 | 43.05% | 45.37% | 36.03% |
| OOS|1h|flip | 19575 | 0.0337 | 0.0672 | 55.41% | 41.58% | 25.31% |
| OOS|1h|rnd | 19446 | -0.0106 | -0.0108 | 47.77% | 34.94% | 28.57% |
| OOS|1h|sig | 19575 | -0.0337 | -0.0672 | 44.59% | 41.58% | 31.13% |
| OOS|5m|flip | 38367 | 0.0142 | 0.0173 | 57.52% | 47.75% | 29.00% |
| OOS|5m|rnd | 38367 | -0.0097 | -0.0099 | 42.09% | 36.39% | 36.57% |
| OOS|5m|sig | 38367 | -0.0142 | -0.0173 | 42.48% | 47.75% | 39.12% |
| OOS|ALL|flip | 89741 | 0.0172 | 0.0314 | 56.85% | 45.56% | 27.80% |
| OOS|ALL|rnd | 89612 | -0.0101 | -0.0111 | 44.37% | 35.91% | 33.64% |
| OOS|ALL|sig | 89741 | -0.0172 | -0.0314 | 43.15% | 45.56% | 36.28% |
| VALIDATION|15m|flip | 40473 | 0.0208 | 0.0339 | 58.02% | 49.66% | 28.03% |
| VALIDATION|15m|rnd | 40473 | -0.0101 | -0.0085 | 46.48% | 42.18% | 34.67% |
| VALIDATION|15m|sig | 40473 | -0.0208 | -0.0339 | 41.98% | 49.66% | 39.42% |
| VALIDATION|1h|flip | 23219 | -0.0186 | -0.0061 | 51.79% | 48.38% | 29.24% |
| VALIDATION|1h|rnd | 22706 | -0.0110 | -0.0102 | 48.33% | 41.62% | 31.18% |
| VALIDATION|1h|sig | 23219 | 0.0186 | 0.0061 | 48.21% | 48.38% | 30.78% |
| VALIDATION|5m|flip | 50312 | 0.0075 | 0.0119 | 56.19% | 50.44% | 31.06% |
| VALIDATION|5m|rnd | 50312 | -0.0111 | -0.0121 | 42.96% | 42.71% | 38.14% |
| VALIDATION|5m|sig | 50312 | -0.0075 | -0.0119 | 43.81% | 50.44% | 39.26% |
| VALIDATION|ALL|flip | 114004 | 0.0069 | 0.0160 | 55.94% | 49.74% | 29.61% |
| VALIDATION|ALL|rnd | 113491 | -0.0107 | -0.0104 | 45.29% | 42.31% | 35.51% |
| VALIDATION|ALL|sig | 114004 | -0.0069 | -0.0160 | 44.06% | 49.74% | 37.59% |

## 6. Direction vs random

Flip = same path, opposite signed direction. Random-dir P ≈ 0.5×(P_sig+P_flip).

| Slice | Signal | Random entry | Flip |
|---|---:|---:|---:|
| OOS ALL | 0.4085 | 0.3540 | 0.4161 |
| VALIDATION ALL | 0.4486 | 0.4119 | 0.4454 |
| IS ALL | 0.4711 | 0.4407 | 0.4734 |
| OOS 5m | 0.4229 | 0.3610 | 0.4212 |
| VALIDATION 5m | 0.4578 | 0.4128 | 0.4471 |
| IS 5m | 0.4747 | 0.4450 | 0.4799 |
| OOS 15m | 0.4048 | 0.3518 | 0.4119 |
| VALIDATION 15m | 0.4368 | 0.4140 | 0.4515 |
| IS 15m | 0.4692 | 0.4400 | 0.4720 |
| OOS 1h | 0.3863 | 0.3436 | 0.4128 |
| VALIDATION 1h | 0.4491 | 0.4060 | 0.4309 |
| IS 1h | 0.4654 | 0.4285 | 0.4592 |

## 7. Signal vs random entry

Random fill times in the same phase/coin/TF, same long/short count, signal bars excluded.

| Key | n | P+1R | MFE | MAE | Range | Ret4 |
|---|---:|---:|---:|---:|---:|---:|
| IS|15m|flip | 161664 | 47.20% | 2.0142 | 2.1241 | 1.4306 | 0.0169 |
| IS|15m|rnd | 161664 | 44.00% | 1.6794 | 1.7121 | 1.1375 | -0.0110 |
| IS|15m|sig | 161664 | 46.92% | 2.1241 | 2.0142 | 1.4306 | -0.0169 |
| IS|1h|flip | 82578 | 45.92% | 1.8594 | 2.0878 | 1.9271 | -0.0133 |
| IS|1h|rnd | 66436 | 42.85% | 1.5869 | 1.6215 | 1.5274 | -0.0092 |
| IS|1h|sig | 82578 | 46.54% | 2.0878 | 1.8594 | 1.9271 | 0.0133 |
| IS|5m|flip | 218904 | 47.99% | 2.1269 | 2.1906 | 1.1257 | 0.0128 |
| IS|5m|rnd | 218904 | 44.50% | 1.7191 | 1.7357 | 0.8767 | -0.0098 |
| IS|5m|sig | 218904 | 47.47% | 2.1906 | 2.1269 | 1.1257 | -0.0128 |
| IS|ALL|flip | 463146 | 47.34% | 2.0399 | 2.1491 | 1.3750 | 0.0096 |
| IS|ALL|rnd | 447004 | 44.07% | 1.6851 | 1.7102 | 1.0677 | -0.0101 |
| IS|ALL|sig | 463146 | 47.11% | 2.1491 | 2.0399 | 1.3750 | -0.0096 |
| OOS|15m|flip | 31799 | 41.19% | 1.3180 | 1.3668 | 1.0975 | 0.0264 |
| OOS|15m|rnd | 31799 | 35.18% | 1.0998 | 1.1131 | 0.8279 | -0.0127 |
| OOS|15m|sig | 31799 | 40.48% | 1.3668 | 1.3180 | 1.0975 | -0.0264 |
| OOS|1h|flip | 19575 | 41.28% | 1.2297 | 1.3124 | 1.3561 | 0.0672 |
| OOS|1h|rnd | 19446 | 34.36% | 1.0659 | 1.0979 | 1.1041 | -0.0108 |
| OOS|1h|sig | 19575 | 38.63% | 1.3124 | 1.2297 | 1.3561 | -0.0672 |
| OOS|5m|flip | 38367 | 42.12% | 1.3973 | 1.4551 | 0.8985 | 0.0173 |
| OOS|5m|rnd | 38367 | 36.10% | 1.1229 | 1.1112 | 0.6554 | -0.0099 |
| OOS|5m|sig | 38367 | 42.29% | 1.4551 | 1.3973 | 0.8985 | -0.0173 |
| OOS|ALL|flip | 89741 | 41.61% | 1.3327 | 1.3927 | 1.0689 | 0.0314 |
| OOS|ALL|rnd | 89612 | 35.40% | 1.1023 | 1.1090 | 0.8140 | -0.0111 |
| OOS|ALL|sig | 89741 | 40.85% | 1.3927 | 1.3327 | 1.0689 | -0.0314 |
| VALIDATION|15m|flip | 40473 | 45.15% | 1.5176 | 1.5270 | 1.0702 | 0.0339 |
| VALIDATION|15m|rnd | 40473 | 41.40% | 1.3272 | 1.3172 | 0.9524 | -0.0085 |
| VALIDATION|15m|sig | 40473 | 43.68% | 1.5270 | 1.5176 | 1.0702 | -0.0339 |
| VALIDATION|1h|flip | 23219 | 43.09% | 1.4651 | 1.5119 | 1.4524 | -0.0061 |
| VALIDATION|1h|rnd | 22706 | 40.60% | 1.3000 | 1.2915 | 1.2792 | -0.0102 |
| VALIDATION|1h|sig | 23219 | 44.91% | 1.5119 | 1.4651 | 1.4524 | 0.0061 |
| VALIDATION|5m|flip | 50312 | 44.71% | 1.5790 | 1.6112 | 0.8729 | 0.0119 |
| VALIDATION|5m|rnd | 50312 | 41.28% | 1.3303 | 1.3535 | 0.7544 | -0.0121 |
| VALIDATION|5m|sig | 50312 | 45.78% | 1.6112 | 1.5790 | 0.8729 | -0.0119 |
| VALIDATION|ALL|flip | 114004 | 44.54% | 1.5340 | 1.5611 | 1.0610 | 0.0160 |
| VALIDATION|ALL|rnd | 113491 | 41.19% | 1.3231 | 1.3281 | 0.9300 | -0.0104 |
| VALIDATION|ALL|sig | 114004 | 44.86% | 1.5611 | 1.5340 | 1.0610 | -0.0160 |

## 8. TP/SL grid

Pre-registered 6×6 at **24h** max hold. Gross before extra cost shock. IS is descriptive; OOS is frozen.

|sig PF TP\SL | 0.50 | 0.75 | 1.00 | 1.25 | 1.50 | 2.00 |
|---|---:|---:|---:|---:|---:|---:|
| TP 0.50 | 0.9178 | 0.9476 | 0.9589 | 0.9700 | 0.9759 | 0.9647 |
| TP 0.75 | 0.9289 | 0.9496 | 0.9667 | 0.9851 | 0.9899 | 0.9777 |
| TP 1.00 | 0.9277 | 0.9531 | 0.9706 | 0.9866 | 0.9938 | 0.9847 |
| TP 1.25 | 0.9331 | 0.9573 | 0.9758 | 0.9924 | 0.9999 | 0.9935 |
| TP 1.50 | 0.9362 | 0.9648 | 0.9849 | 1.0061 | 1.0115 | 1.0058 |
| TP 2.00 | 0.9602 | 0.9855 | 1.0058 | 1.0272 | 1.0334 | 1.0288 |

Random-entry OOS ALL 24h grid PF:

|rnd PF TP\SL | 0.50 | 0.75 | 1.00 | 1.25 | 1.50 | 2.00 |
|---|---:|---:|---:|---:|---:|---:|
| TP 0.50 | 0.9606 | 0.9758 | 0.9805 | 0.9899 | 0.9909 | 0.9849 |
| TP 0.75 | 0.9657 | 0.9799 | 0.9877 | 0.9959 | 0.9950 | 0.9885 |
| TP 1.00 | 0.9578 | 0.9705 | 0.9765 | 0.9820 | 0.9811 | 0.9742 |
| TP 1.25 | 0.9561 | 0.9706 | 0.9776 | 0.9828 | 0.9816 | 0.9749 |
| TP 1.50 | 0.9570 | 0.9716 | 0.9776 | 0.9831 | 0.9829 | 0.9754 |
| TP 2.00 | 0.9665 | 0.9821 | 0.9890 | 0.9946 | 0.9947 | 0.9871 |

## 9. Time exits

| Exit | Phase | TF | Kind | n | PF | Exp | WR |
|---|---|---|---|---:|---:|---:|---:|
| TIME-1BAR | IS | ALL | sig | 463146 | 0.9388 | -0.02% | 45.45% |
| TIME-2BAR | IS | ALL | sig | 463146 | 0.9258 | -0.03% | 45.85% |
| TIME-4BAR | IS | ALL | sig | 463146 | 0.9574 | -0.02% | 46.47% |
| TIME-8BAR | IS | ALL | sig | 463146 | 0.9822 | -0.01% | 47.11% |
| TIME-16BAR | IS | ALL | sig | 463146 | 0.9844 | -0.01% | 47.64% |
| TIME-1BAR | VALIDATION | ALL | sig | 114004 | 0.9282 | -0.01% | 44.06% |
| TIME-2BAR | VALIDATION | ALL | sig | 114004 | 0.9421 | -0.02% | 45.03% |
| TIME-4BAR | VALIDATION | ALL | sig | 114004 | 0.9113 | -0.03% | 45.44% |
| TIME-8BAR | VALIDATION | ALL | sig | 114004 | 0.9436 | -0.03% | 46.35% |
| TIME-16BAR | VALIDATION | ALL | sig | 114004 | 0.9497 | -0.03% | 47.63% |
| TIME-1BAR | OOS | ALL | sig | 89741 | 0.8184 | -0.03% | 43.15% |
| TIME-2BAR | OOS | ALL | sig | 89741 | 0.8503 | -0.04% | 44.43% |
| TIME-4BAR | OOS | ALL | sig | 89741 | 0.8137 | -0.06% | 44.72% |
| TIME-8BAR | OOS | ALL | sig | 89741 | 0.8358 | -0.07% | 45.49% |
| TIME-16BAR | OOS | ALL | sig | 89741 | 0.9080 | -0.05% | 46.19% |
| TIME-1BAR | IS | ALL | rnd | 447004 | 0.8707 | -0.02% | 46.46% |
| TIME-2BAR | IS | ALL | rnd | 447004 | 0.9081 | -0.02% | 47.44% |
| TIME-4BAR | IS | ALL | rnd | 447004 | 0.9296 | -0.02% | 48.21% |
| TIME-8BAR | IS | ALL | rnd | 447004 | 0.9485 | -0.02% | 48.67% |
| TIME-16BAR | IS | ALL | rnd | 447004 | 0.9568 | -0.03% | 48.93% |
| TIME-1BAR | VALIDATION | ALL | rnd | 113491 | 0.8222 | -0.02% | 45.29% |
| TIME-2BAR | VALIDATION | ALL | rnd | 113491 | 0.8747 | -0.02% | 46.75% |
| TIME-4BAR | VALIDATION | ALL | rnd | 113491 | 0.9119 | -0.02% | 47.83% |
| TIME-8BAR | VALIDATION | ALL | rnd | 113491 | 0.9473 | -0.02% | 48.58% |
| TIME-16BAR | VALIDATION | ALL | rnd | 113491 | 0.9767 | -0.01% | 49.03% |
| TIME-1BAR | OOS | ALL | rnd | 89612 | 0.8152 | -0.02% | 44.37% |
| TIME-2BAR | OOS | ALL | rnd | 89612 | 0.8680 | -0.02% | 45.81% |
| TIME-4BAR | OOS | ALL | rnd | 89612 | 0.8954 | -0.02% | 46.82% |
| TIME-8BAR | OOS | ALL | rnd | 89612 | 0.9224 | -0.02% | 47.75% |
| TIME-16BAR | OOS | ALL | rnd | 89612 | 0.9390 | -0.03% | 48.50% |

## 10. Breakeven / trailing

| Exit | Phase | TF | Kind | n | PF | Exp | WR |
|---|---|---|---|---:|---:|---:|---:|
| BE-0.5R | IS | ALL | sig | 463146 | 0.6712 | -0.22% | 13.29% |
| BE-1R | IS | ALL | sig | 463146 | 0.8751 | -0.12% | 25.62% |
| TRAIL-0.5R-AFTER-1R | IS | ALL | sig | 463146 | 0.9162 | -0.08% | 49.89% |
| ATR-TRAIL | IS | ALL | sig | 463146 | 0.5935 | -0.13% | 30.27% |
| TP1/SL1-4H | IS | ALL | sig | 463146 | 0.9645 | -0.02% | 48.60% |
| TP1/SL1-12H | IS | ALL | sig | 463146 | 0.9805 | -0.02% | 49.37% |
| BE-0.5R | VALIDATION | ALL | sig | 114004 | 0.7457 | -0.17% | 16.40% |
| BE-1R | VALIDATION | ALL | sig | 114004 | 0.9204 | -0.07% | 29.80% |
| TRAIL-0.5R-AFTER-1R | VALIDATION | ALL | sig | 114004 | 0.8934 | -0.10% | 49.91% |
| ATR-TRAIL | VALIDATION | ALL | sig | 114004 | 0.6521 | -0.08% | 31.92% |
| TP1/SL1-4H | VALIDATION | ALL | sig | 114004 | 0.9872 | -0.01% | 48.40% |
| TP1/SL1-12H | VALIDATION | ALL | sig | 114004 | 0.9943 | -0.00% | 49.59% |
| BE-0.5R | OOS | ALL | sig | 89741 | 0.7179 | -0.18% | 17.68% |
| BE-1R | OOS | ALL | sig | 89741 | 0.8926 | -0.10% | 31.83% |
| TRAIL-0.5R-AFTER-1R | OOS | ALL | sig | 89741 | 0.8627 | -0.12% | 49.17% |
| ATR-TRAIL | OOS | ALL | sig | 89741 | 0.6124 | -0.09% | 30.66% |
| TP1/SL1-4H | OOS | ALL | sig | 89741 | 0.9220 | -0.04% | 47.13% |
| TP1/SL1-12H | OOS | ALL | sig | 89741 | 0.9520 | -0.04% | 48.41% |
| BE-0.5R | IS | ALL | rnd | 447004 | 0.8420 | -0.11% | 18.46% |
| BE-1R | IS | ALL | rnd | 447004 | 0.9515 | -0.05% | 30.45% |
| TRAIL-0.5R-AFTER-1R | IS | ALL | rnd | 447004 | 0.8573 | -0.13% | 49.30% |
| ATR-TRAIL | IS | ALL | rnd | 447004 | 0.7317 | -0.10% | 34.64% |
| TP1/SL1-4H | IS | ALL | rnd | 447004 | 0.9565 | -0.02% | 49.02% |
| TP1/SL1-12H | IS | ALL | rnd | 447004 | 0.9660 | -0.03% | 49.22% |
| BE-0.5R | VALIDATION | ALL | rnd | 113491 | 0.8632 | -0.09% | 21.38% |
| BE-1R | VALIDATION | ALL | rnd | 113491 | 0.9605 | -0.04% | 33.75% |
| TRAIL-0.5R-AFTER-1R | VALIDATION | ALL | rnd | 113491 | 0.8742 | -0.11% | 49.52% |
| ATR-TRAIL | VALIDATION | ALL | rnd | 113491 | 0.7621 | -0.09% | 35.21% |
| TP1/SL1-4H | VALIDATION | ALL | rnd | 113491 | 0.9588 | -0.02% | 49.28% |
| TP1/SL1-12H | VALIDATION | ALL | rnd | 113491 | 0.9762 | -0.02% | 49.54% |
| BE-0.5R | OOS | ALL | rnd | 89612 | 0.8533 | -0.09% | 24.29% |
| BE-1R | OOS | ALL | rnd | 89612 | 0.9416 | -0.05% | 37.39% |
| TRAIL-0.5R-AFTER-1R | OOS | ALL | rnd | 89612 | 0.8643 | -0.11% | 49.49% |
| ATR-TRAIL | OOS | ALL | rnd | 89612 | 0.7212 | -0.11% | 34.74% |
| TP1/SL1-4H | OOS | ALL | rnd | 89612 | 0.9508 | -0.02% | 48.72% |
| TP1/SL1-12H | OOS | ALL | rnd | 89612 | 0.9720 | -0.02% | 49.22% |

## 11. IS / VAL / OOS

| Phase | Kind | n | P+1R | MFE | MAE | Range | Ret4 |
|---|---|---:|---:|---:|---:|---:|---:|
| IS | sig | 463146 | 47.11% | 2.1491 | 2.0399 | 1.3750 | -0.0096 |
| IS | rnd | 447004 | 44.07% | 1.6851 | 1.7102 | 1.0677 | -0.0101 |
| IS | flip | 463146 | 47.34% | 2.0399 | 2.1491 | 1.3750 | 0.0096 |
| VALIDATION | sig | 114004 | 44.86% | 1.5611 | 1.5340 | 1.0610 | -0.0160 |
| VALIDATION | rnd | 113491 | 41.19% | 1.3231 | 1.3281 | 0.9300 | -0.0104 |
| VALIDATION | flip | 114004 | 44.54% | 1.5340 | 1.5611 | 1.0610 | 0.0160 |
| OOS | sig | 89741 | 40.85% | 1.3927 | 1.3327 | 1.0689 | -0.0314 |
| OOS | rnd | 89612 | 35.40% | 1.1023 | 1.1090 | 0.8140 | -0.0111 |
| OOS | flip | 89741 | 41.61% | 1.3327 | 1.3927 | 1.0689 | 0.0314 |

## 12. Cost sensitivity

Round-trip cost = 2 × multiplier × (0.04%+0.02%). Applied to OOS ALL 24h **TP1.00/SL1.00** expectancy (not to path probabilities).

| Mult | Signal net exp | Random net exp |
|---|---:|---:|
| 1.0 | -0.15% | -0.14% |
| 1.25 | -0.18% | -0.17% |
| 1.5 | -0.21% | -0.20% |
| 2.0 | -0.27% | -0.26% |

## 13. 5m / 15m / 1h

| TF | Kind | n | P+1R | Range | Ret4 |
|---|---|---:|---:|---:|---:|
| 5m | sig | 38367 | 42.29% | 0.8985 | -0.0173 |
| 5m | rnd | 38367 | 36.10% | 0.6554 | -0.0099 |
| 5m | flip | 38367 | 42.12% | 0.8985 | 0.0173 |
| 15m | sig | 31799 | 40.48% | 1.0975 | -0.0264 |
| 15m | rnd | 31799 | 35.18% | 0.8279 | -0.0127 |
| 15m | flip | 31799 | 41.19% | 1.0975 | 0.0264 |
| 1h | sig | 19575 | 38.63% | 1.3561 | -0.0672 |
| 1h | rnd | 19446 | 34.36% | 1.1041 | -0.0108 |
| 1h | flip | 19575 | 41.28% | 1.3561 | 0.0672 |

## 14. Symbol robustness

| Coin | n sig | P+1R sig | n rnd | P+1R rnd | Δ |
|---|---:|---:|---:|---:|---:|
| ADAUSDT | 4612 | 45.86% | 4612 | 41.57% | 4.29% |
| AVAXUSDT | 3782 | 44.24% | 3782 | 40.56% | 3.68% |
| BNBUSDT | 2058 | 31.44% | 2058 | 23.66% | 7.77% |
| BTCUSDT | 1817 | 30.27% | 1817 | 22.34% | 7.93% |
| DOGEUSDT | 3457 | 42.09% | 3457 | 35.87% | 6.22% |
| ETHUSDT | 2881 | 35.20% | 2881 | 34.12% | 1.08% |
| LINKUSDT | 3749 | 42.78% | 3749 | 38.14% | 4.64% |
| LTCUSDT | 2778 | 36.54% | 2778 | 30.89% | 5.65% |
| SOLUSDT | 3405 | 42.85% | 3405 | 35.86% | 6.99% |
| XRPUSDT | 3260 | 41.13% | 3260 | 34.05% | 7.09% |

## 15. Final information classification

**VOLATILITY INFORMATION FOUND**

TYPE C — volatility / opportunity information. Not TYPE A (direction) or TYPE B (short-term timing).

The signature is two-sided: OOS ALL MFE 1.39R vs random 1.10R, MAE 1.33R vs random 1.11R, range 1.07R vs 0.81R. Whip (both +0.5R and −0.5R inside 24h) is 45.6% vs 35.9% random. That is a larger future range, not a better path.

Phase 11 (causal predictors of P(+1R before −1R)) was **not** fit. OOS P_signal ≈ P_flip, so that probability is not a directional label. Fitting it would rediscover volatility.

IS-best grid cell TP2/SL2 (IS PF 1.015 vs random 0.984) failed validation (VAL delta −0.020). OOS cells with PF>1 were not used for selection.

If a later stage is run, it must start from **range/opportunity**, not from another RSI/EMA entry, and it must still beat random-entry OOS after costs. This report does not authorize that stage.

Do not create another entry-strategy family from this architecture. Isolated LOW and LIVE were not changed.

- 1. Direction: NO. P(+1R before -1R) vs random direction, OOS, ≥2 TFs, VAL same sign.
- 2. Short-term continuation: NO. 4-bar close R vs random entry.
- 3. Volatility: YES. 24h range vs random entry.
- 4. MFE: YES. OOS mean MFE signal−random = 0.2903R.
- 5. MAE: NO. OOS mean MAE signal−random = 0.2237R (lower MAE would be better).
- 6. Holding period: NO. 4-bar continuation is the pre-registered short-horizon test. Time-to-MFE/MAE are descriptive.
- 7. TP/SL grid OOS: NO. IS-best cell frozen; VAL then OOS vs random.
- 8. Time exit OOS: NO. TIME-4BAR OOS PF vs random, VAL same sign.
- 9. Signal vs random entry: YES. Random times, same coins/TFs/long-short counts.
- 10. Signal vs random direction: NO. Flip/50-50 equivalent: 0.5×(P_sig+P_flip).
- 11. Robust across 5m/15m/1h: YES. dir 0 timing 0 vol 3 TFs OOS.
- 12. Robust across coins: YES. OOS 15m coins beating random P(+1R) by ≥0,02: 9/10.
- 13. Cost +25/+50/+100: NO. IS-best grid net expectancy still beats random at +25% costs.

## Run notes
- Wave-6 trade-path. Existing 43-strategy signals only. No new entries. No router. LIVE=OFF.
- 1R = LOW stop distance 2% of slipped next-open entry. 2%/4% is NOT the path label and is not the exit grid.
- Same-bar favorable+adverse: adverse first. Path values are labels only.
- Pre-registered thresholds: dir 0,02 pp vs random-dir; 4-bar cont 0,02R vs random-entry; vol +10% range; exit PF +0,05 vs random, IS-frozen.
- IS < 2025-11-30; VAL < 2026-04-25; then OOS. No OOS retune.
- Signal paths with complete 24h: 666891.
- Random-entry paths with complete 24h: 650107.
- IS-best 24h grid cell TP2.00/SL2.00 PF 1.0154 vs random 0.9840. VAL delta vs random -0.0202. OOS delta 0.0418.
- OOS TFs beating random-dir by ≥0,02: 0/3 (VAL same sign 2/3).
- OOS TFs 4-bar cont ≥0,02R vs random-entry: 0/3 (VAL same sign 2/3).
- OOS TFs range ≥+10% vs random-entry: 3/3 (VAL same sign 3/3).
- OOS 15m coins with P(+1R) ≥ random+0,02: 9.
- Wave-6 trade-path. Existing signals only. No new entries. No router. LIVE disabled. Isolated LOW unchanged.
- Window 2024-09-18 → 2026-09-19. Timeframes 5m/15m/1h. Coins BTCUSDT,ETHUSDT,BNBUSDT,SOLUSDT,XRPUSDT,DOGEUSDT,ADAUSDT,AVAXUSDT,LINKUSDT,LTCUSDT.
- Path labels: MFE/MAE/first-touch. 1R = 2% slipped entry. 2%/4% is not the path exit.
- Random entry: same coin/TF/phase/long-short counts, signal fills excluded, seed 42.
- Strategy universe 43.
- Harvest 5m: 308111 events.
- Harvest 15m: 234265 events.
- Harvest 1h: 125597 events.
