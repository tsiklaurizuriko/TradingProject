# Cross-sectional alpha research

Feature manifest SHA-256 `fe81edf56d4af31cc0db0a215c09a6245437ad2a66eeb1f6caec11afb0ef247e`.

This is a research portfolio. It does not create a strategy and it does not call any relationship an edge.

## 1. Universe construction

The clock is the BTCUSDT 15-minute grid in the existing USD-M USDT perpetual cache. A symbol joins a timestamp only when that symbol has its own closed bar there. Names enter after 96 of their own bars, so a new listing is not ranked on its first day. BTCDOMUSDT and stablecoin bases are excluded before any return is measured. Today's exchange listing was not projected onto earlier dates beyond the bars that exist.

Symbols loaded: 527. Grid: 2024-09-18 00:00:00Z to 2026-09-23 08:45:00Z, 70596 bars. Timestamps with at least 30 priced names: 70156.

## 2. Data coverage

Median priced names per bar: 408.

| Series | Local files | Note |
| --- | ---: | --- |
| oi | 10 | Vision metrics are on the 10-coin research set. REST open interest is 29 days. Below the 30-name decile minimum. |
| funding | 10 | Settled funding is stored for the 10-coin set only. |
| basis | 10 | Mark and index basis are stored for the 10-coin set only. |
| taker | 3 | Re-downloaded taker-buy covers BTC, ETH, and BNB. The universe cache stores historical taker-buy as zero. |
| depth | 3 | ±1% depth was ingested for BTC, ETH, and BNB only. |

## 3. Feature definitions

Returns are close-to-close on the 15-minute grid: 1, 4, 16, 48, and 96 bars. `return_1h_over_atr` divides the 1-hour return by ATR(14) / price. The volatility-adjusted 4-hour and 24-hour returns divide by the prior 96-bar realized volatility. Relative volume is the current bar divided by the mean of the previous 20 bars. Volume acceleration is that ratio minus its value four bars earlier. A missing input is left missing.

## 4. Ranking methodology

Fixed before aggregation. The primary basket is the top decile minus the bottom decile of the cross-sectional percentile rank. A window passes when it has at least 200 timestamps, the mean and the median spread share a sign, and the absolute mean spread is at least 1 basis point. A horizon is stable when in-sample, validation, and out-of-sample pass with that same sign and at least 3 of 4 chronological blocks agree. REPEATABLE requires at least 3 of the 5 pre-registered horizons to be stable with one shared sign, both BTC volatility regimes to share that sign on those horizons, at least 20 distinct symbols in the out-of-sample baskets, and no single symbol in more than 40 percent of out-of-sample top-decile timestamps. UNIVERSE_SPECIFIC: the horizons are stable but participation fails. OOS_ONLY: out-of-sample passes on at least 3 horizons with one sign and in-sample does not share it. UNSTABLE: windows or blocks or regimes disagree. NO_EVIDENCE: otherwise. Quintiles are reported and are not used to choose the label. None of these labels is an edge.

Deciles and quintiles use only names with a finite feature and a finite forward return at that horizon. Ties break by symbol name. A cross-sectional z-score has the same order as the raw feature, so it does not create a second basket.

## 5. Future return definitions

The forward return is the close `h` bars later divided by the close at the signal, minus one. Horizons are 15m, 1h, 4h, 12h, and 24h. They were fixed before aggregation. The absolute number is the basket's own average return. The cross-sectional spread is the top basket minus the bottom basket.

## 6. Top versus bottom results

Decile spread in basis points. Positive means the high-feature basket beat the low-feature basket.

| Feature | Horizon | IS n | IS mean | IS median | IS hit | VAL mean | OOS n | OOS mean | OOS median | OOS hit | OOS vol |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| return_15m | 15m | 42261 | -4.16 | -4.64 | 42.7% | -4.98 | 13747 | -5.70 | -6.02 | 40.1% | 28.69 |
| return_15m | 1h | 42261 | -4.89 | -5.69 | 45.4% | -6.62 | 13744 | -8.24 | -9.01 | 42.5% | 54.41 |
| return_15m | 4h | 42261 | -3.57 | -6.10 | 47.5% | -6.92 | 13732 | -8.45 | -10.04 | 45.8% | 103.61 |
| return_15m | 12h | 42261 | -2.91 | -3.87 | 49.1% | -8.82 | 13700 | -7.14 | -8.29 | 47.9% | 174.98 |
| return_15m | 24h | 42261 | -2.22 | -4.63 | 49.3% | -12.84 | 13652 | -7.36 | -10.36 | 48.0% | 251.10 |
| return_1h | 15m | 42261 | -2.30 | -2.48 | 46.5% | -2.73 | 13747 | -4.04 | -4.23 | 43.0% | 28.93 |
| return_1h | 1h | 42261 | -3.17 | -4.55 | 46.5% | -3.28 | 13744 | -6.48 | -7.01 | 44.4% | 55.95 |
| return_1h | 4h | 42261 | -1.08 | -3.23 | 48.8% | -4.82 | 13732 | -9.33 | -10.45 | 45.6% | 109.21 |
| return_1h | 12h | 42261 | 0.25 | -0.07 | 50.0% | -7.42 | 13700 | -8.41 | -10.33 | 47.6% | 178.90 |
| return_1h | 24h | 42261 | 2.21 | -2.14 | 49.6% | -15.82 | 13652 | -7.92 | -11.27 | 48.0% | 256.37 |
| return_4h | 15m | 42261 | -1.18 | -1.00 | 48.5% | -1.28 | 13747 | -2.41 | -2.10 | 46.4% | 29.33 |
| return_4h | 1h | 42261 | -1.15 | -1.57 | 48.8% | -1.85 | 13744 | -4.79 | -5.86 | 45.5% | 57.48 |
| return_4h | 4h | 42261 | -0.75 | -2.36 | 49.1% | -3.05 | 13732 | -8.88 | -8.84 | 46.3% | 110.68 |
| return_4h | 12h | 42261 | 1.92 | -1.25 | 49.7% | -4.58 | 13700 | -10.34 | -10.52 | 47.4% | 182.08 |
| return_4h | 24h | 42261 | 5.21 | -4.49 | 49.3% | -15.32 | 13652 | -3.05 | -11.99 | 48.1% | 260.86 |
| return_12h | 15m | 42241 | -0.58 | -0.08 | 49.9% | -0.88 | 13747 | -1.12 | -0.95 | 48.5% | 29.25 |
| return_12h | 1h | 42241 | -0.29 | -0.37 | 49.7% | -1.31 | 13744 | -1.82 | -1.83 | 48.6% | 57.87 |
| return_12h | 4h | 42241 | 0.96 | -0.49 | 49.8% | -1.85 | 13732 | -3.88 | -2.68 | 48.9% | 111.71 |
| return_12h | 12h | 42241 | 7.32 | 0.30 | 50.1% | 0.13 | 13700 | -1.23 | -6.61 | 48.5% | 185.94 |
| return_12h | 24h | 42241 | 1.52 | -13.50 | 48.0% | -19.33 | 13652 | 5.28 | -0.35 | 49.9% | 260.38 |
| return_24h | 15m | 42193 | -0.19 | 0.29 | 50.4% | -0.89 | 13747 | -0.70 | -0.43 | 49.3% | 29.51 |
| return_24h | 1h | 42193 | 0.16 | 0.41 | 50.3% | -2.52 | 13744 | -1.70 | -1.74 | 48.6% | 60.04 |
| return_24h | 4h | 42193 | 0.18 | -1.00 | 49.6% | -8.54 | 13732 | -2.97 | -4.53 | 48.3% | 118.47 |
| return_24h | 12h | 42193 | -4.72 | -5.35 | 48.7% | -20.84 | 13700 | 1.61 | -7.79 | 48.6% | 198.75 |
| return_24h | 24h | 42193 | -12.05 | -19.85 | 47.2% | -24.19 | 13652 | 8.71 | 4.15 | 50.6% | 278.50 |
| return_1h_over_atr | 15m | 42261 | -2.22 | -2.66 | 45.1% | -2.50 | 13747 | -3.90 | -4.14 | 41.6% | 23.93 |
| return_1h_over_atr | 1h | 42261 | -2.81 | -4.46 | 45.8% | -2.45 | 13744 | -6.02 | -6.94 | 42.8% | 45.66 |
| return_1h_over_atr | 4h | 42261 | -1.48 | -3.86 | 48.1% | -2.26 | 13732 | -8.10 | -9.41 | 45.0% | 88.03 |
| return_1h_over_atr | 12h | 42261 | -0.73 | -4.35 | 48.8% | -5.56 | 13700 | -8.92 | -11.34 | 46.1% | 141.48 |
| return_1h_over_atr | 24h | 42261 | 1.31 | -4.89 | 49.1% | -11.92 | 13652 | -10.98 | -14.68 | 46.7% | 202.20 |
| return_4h_over_vol | 15m | 42193 | -0.96 | -1.27 | 47.9% | -0.86 | 13747 | -2.05 | -2.14 | 45.8% | 24.96 |
| return_4h_over_vol | 1h | 42193 | -0.70 | -1.73 | 48.5% | -0.38 | 13744 | -3.58 | -4.46 | 45.7% | 48.56 |
| return_4h_over_vol | 4h | 42193 | 0.08 | -2.30 | 48.9% | 0.53 | 13732 | -6.30 | -9.96 | 45.3% | 91.59 |
| return_4h_over_vol | 12h | 42193 | 0.49 | -5.32 | 48.6% | -1.03 | 13700 | -9.47 | -12.97 | 45.9% | 147.22 |
| return_4h_over_vol | 24h | 42193 | 6.01 | -7.67 | 48.6% | -12.25 | 13652 | -4.17 | -16.47 | 46.4% | 213.17 |
| return_24h_over_vol | 15m | 42193 | -0.07 | -0.12 | 49.8% | -0.71 | 13747 | -0.77 | -0.64 | 48.9% | 25.44 |
| return_24h_over_vol | 1h | 42193 | 0.50 | 0.09 | 50.1% | -1.79 | 13744 | -1.20 | -1.62 | 48.3% | 51.65 |
| return_24h_over_vol | 4h | 42193 | 2.57 | -0.66 | 49.7% | -6.93 | 13732 | -0.64 | -3.94 | 48.3% | 104.48 |
| return_24h_over_vol | 12h | 42193 | 5.31 | 0.67 | 50.2% | -17.44 | 13700 | 3.21 | -9.13 | 47.8% | 179.00 |
| return_24h_over_vol | 24h | 42193 | 3.26 | -14.82 | 47.3% | -21.01 | 13652 | 16.71 | 2.35 | 50.4% | 253.71 |
| relative_volume | 15m | 42261 | -0.15 | -0.44 | 49.0% | -0.11 | 13747 | 0.37 | 0.09 | 50.2% | 19.53 |
| relative_volume | 1h | 42261 | -0.28 | -0.79 | 49.0% | 0.03 | 13744 | 0.61 | -0.13 | 49.9% | 36.63 |
| relative_volume | 4h | 42261 | -1.01 | -2.82 | 48.2% | 0.00 | 13732 | -0.78 | -2.27 | 48.5% | 69.82 |
| relative_volume | 12h | 42261 | -1.46 | -4.79 | 48.3% | -5.52 | 13700 | -5.26 | -6.87 | 47.3% | 118.28 |
| relative_volume | 24h | 42261 | -2.76 | -7.15 | 48.2% | -6.80 | 13652 | -6.65 | -9.81 | 47.3% | 167.91 |
| volume_acceleration | 15m | 42261 | -0.23 | -0.37 | 49.1% | -0.03 | 13747 | -0.06 | -0.05 | 49.9% | 20.16 |
| volume_acceleration | 1h | 42261 | -0.44 | -0.77 | 49.0% | -0.17 | 13744 | 0.32 | -0.35 | 49.6% | 37.46 |
| volume_acceleration | 4h | 42261 | -0.34 | -0.78 | 49.5% | 1.27 | 13732 | -0.26 | -0.93 | 49.3% | 69.82 |
| volume_acceleration | 12h | 42261 | 0.21 | -0.99 | 49.6% | -0.31 | 13700 | 0.06 | -1.07 | 49.6% | 117.17 |
| volume_acceleration | 24h | 42261 | 0.51 | 0.03 | 50.0% | 0.46 | 13652 | -0.27 | -0.84 | 49.7% | 166.01 |

Quintile spread in basis points. This table does not choose the label.

| Feature | Horizon | IS n | IS mean | IS median | IS hit | VAL mean | OOS n | OOS mean | OOS median | OOS hit | OOS vol |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| return_15m | 15m | 42261 | -3.58 | -3.84 | 40.7% | -3.93 | 13747 | -4.46 | -4.61 | 37.2% | 17.28 |
| return_15m | 1h | 42261 | -3.82 | -4.44 | 44.5% | -4.69 | 13744 | -6.44 | -6.97 | 40.5% | 32.85 |
| return_15m | 4h | 42261 | -3.10 | -4.34 | 47.2% | -4.87 | 13732 | -6.84 | -7.66 | 44.9% | 62.28 |
| return_15m | 12h | 42261 | -3.26 | -3.33 | 48.8% | -6.37 | 13700 | -6.73 | -7.70 | 46.9% | 106.51 |
| return_15m | 24h | 42261 | -2.47 | -3.51 | 49.0% | -9.67 | 13652 | -6.97 | -9.79 | 47.2% | 152.12 |
| return_1h | 15m | 42261 | -1.91 | -2.08 | 45.2% | -2.06 | 13747 | -3.07 | -3.11 | 41.6% | 17.73 |
| return_1h | 1h | 42261 | -2.51 | -3.14 | 46.2% | -2.23 | 13744 | -4.92 | -5.28 | 42.9% | 33.97 |
| return_1h | 4h | 42261 | -1.51 | -2.85 | 48.2% | -2.70 | 13732 | -6.47 | -6.84 | 45.2% | 66.44 |
| return_1h | 12h | 42261 | -0.99 | -0.89 | 49.7% | -4.38 | 13700 | -6.42 | -7.61 | 46.9% | 109.43 |
| return_1h | 24h | 42261 | 0.83 | -0.91 | 49.8% | -10.86 | 13652 | -6.12 | -7.57 | 47.7% | 156.70 |
| return_4h | 15m | 42261 | -1.06 | -0.99 | 47.5% | -1.09 | 13747 | -1.72 | -1.58 | 45.9% | 17.86 |
| return_4h | 1h | 42261 | -1.08 | -1.61 | 48.2% | -1.03 | 13744 | -3.39 | -3.64 | 44.8% | 34.90 |
| return_4h | 4h | 42261 | -0.80 | -1.49 | 49.0% | -0.41 | 13732 | -7.01 | -7.72 | 44.8% | 67.61 |
| return_4h | 12h | 42261 | -0.05 | -1.07 | 49.6% | -1.13 | 13700 | -8.33 | -8.15 | 46.8% | 112.19 |
| return_4h | 24h | 42261 | 2.30 | -3.70 | 49.1% | -11.30 | 13652 | -5.52 | -8.52 | 47.5% | 159.39 |
| return_12h | 15m | 42241 | -0.61 | -0.35 | 49.2% | -0.73 | 13747 | -0.94 | -0.73 | 48.1% | 17.84 |
| return_12h | 1h | 42241 | -0.59 | -0.52 | 49.4% | -0.55 | 13744 | -1.69 | -1.39 | 47.9% | 35.31 |
| return_12h | 4h | 42241 | -0.32 | -1.22 | 49.3% | 0.42 | 13732 | -3.81 | -3.12 | 48.0% | 69.09 |
| return_12h | 12h | 42241 | 2.61 | -1.97 | 49.3% | 1.44 | 13700 | -5.00 | -8.60 | 46.7% | 115.33 |
| return_12h | 24h | 42241 | -2.81 | -12.61 | 47.3% | -15.31 | 13652 | 0.32 | -2.71 | 49.3% | 161.50 |
| return_24h | 15m | 42193 | -0.32 | 0.08 | 50.2% | -0.62 | 13747 | -0.60 | -0.40 | 48.8% | 17.93 |
| return_24h | 1h | 42193 | -0.14 | 0.00 | 50.0% | -1.32 | 13744 | -1.03 | -0.97 | 48.7% | 36.48 |
| return_24h | 4h | 42193 | -0.03 | -0.16 | 49.9% | -4.65 | 13732 | -2.40 | -3.65 | 47.6% | 72.02 |
| return_24h | 12h | 42193 | -4.12 | -3.68 | 48.8% | -14.18 | 13700 | -0.97 | -6.35 | 47.7% | 122.23 |
| return_24h | 24h | 42193 | -10.15 | -9.83 | 47.9% | -15.59 | 13652 | 6.52 | 1.81 | 50.5% | 172.24 |
| return_1h_over_atr | 15m | 42261 | -1.70 | -2.04 | 44.6% | -1.87 | 13747 | -3.08 | -3.27 | 40.3% | 15.88 |
| return_1h_over_atr | 1h | 42261 | -2.12 | -3.17 | 45.6% | -1.90 | 13744 | -4.84 | -5.21 | 42.1% | 30.65 |
| return_1h_over_atr | 4h | 42261 | -1.43 | -2.83 | 47.9% | -1.57 | 13732 | -5.82 | -6.46 | 44.7% | 59.54 |
| return_1h_over_atr | 12h | 42261 | -1.06 | -3.39 | 48.6% | -2.94 | 13700 | -5.97 | -8.05 | 46.6% | 98.18 |
| return_1h_over_atr | 24h | 42261 | 1.14 | -3.62 | 48.9% | -8.46 | 13652 | -6.36 | -7.92 | 47.5% | 141.76 |
| return_4h_over_vol | 15m | 42193 | -0.82 | -1.02 | 47.1% | -0.87 | 13747 | -1.55 | -1.53 | 45.4% | 16.22 |
| return_4h_over_vol | 1h | 42193 | -0.65 | -1.52 | 48.0% | -0.29 | 13744 | -2.77 | -3.18 | 45.4% | 31.55 |
| return_4h_over_vol | 4h | 42193 | 0.20 | -1.50 | 49.0% | 1.31 | 13732 | -4.98 | -6.70 | 45.3% | 61.07 |
| return_4h_over_vol | 12h | 42193 | 0.45 | -2.97 | 48.8% | 1.02 | 13700 | -6.18 | -7.74 | 46.3% | 101.67 |
| return_4h_over_vol | 24h | 42193 | 4.63 | -3.32 | 49.0% | -8.42 | 13652 | -3.26 | -9.41 | 47.0% | 145.94 |
| return_24h_over_vol | 15m | 42193 | -0.14 | -0.17 | 49.5% | -0.60 | 13747 | -0.62 | -0.59 | 48.1% | 16.45 |
| return_24h_over_vol | 1h | 42193 | 0.22 | -0.15 | 49.8% | -1.15 | 13744 | -0.94 | -1.16 | 48.2% | 33.67 |
| return_24h_over_vol | 4h | 42193 | 1.29 | -0.12 | 49.9% | -4.08 | 13732 | -1.79 | -3.93 | 47.6% | 68.49 |
| return_24h_over_vol | 12h | 42193 | 1.22 | -1.24 | 49.6% | -13.25 | 13700 | 0.62 | -5.58 | 47.9% | 116.51 |
| return_24h_over_vol | 24h | 42193 | -1.67 | -10.23 | 47.5% | -14.47 | 13652 | 9.97 | 0.61 | 50.2% | 165.20 |
| relative_volume | 15m | 42261 | 0.06 | -0.12 | 49.6% | -0.11 | 13747 | 0.39 | 0.16 | 50.6% | 12.80 |
| relative_volume | 1h | 42261 | 0.21 | -0.18 | 49.7% | 0.28 | 13744 | 0.64 | 0.42 | 50.8% | 24.63 |
| relative_volume | 4h | 42261 | 0.11 | -0.99 | 49.1% | 0.42 | 13732 | 0.17 | -0.10 | 49.9% | 47.89 |
| relative_volume | 12h | 42261 | -0.44 | -2.17 | 48.9% | -2.16 | 13700 | -3.37 | -3.61 | 48.0% | 83.50 |
| relative_volume | 24h | 42261 | -0.61 | -3.30 | 48.9% | -3.40 | 13652 | -4.21 | -5.74 | 47.7% | 118.64 |
| volume_acceleration | 15m | 42261 | -0.05 | -0.17 | 49.4% | -0.05 | 13747 | 0.14 | 0.05 | 50.2% | 12.94 |
| volume_acceleration | 1h | 42261 | -0.04 | -0.14 | 49.7% | -0.11 | 13744 | 0.43 | 0.05 | 50.2% | 24.56 |
| volume_acceleration | 4h | 42261 | 0.38 | -0.08 | 49.9% | 0.97 | 13732 | 0.52 | 0.01 | 50.0% | 46.72 |
| volume_acceleration | 12h | 42261 | 0.75 | 0.07 | 50.1% | 0.47 | 13700 | 1.28 | 1.71 | 51.0% | 81.09 |
| volume_acceleration | 24h | 42261 | 0.75 | 0.75 | 50.2% | 1.61 | 13652 | 1.18 | 0.82 | 50.3% | 116.50 |

## 7. Market-neutral spread results

Equal dollar long the top decile and short the bottom decile. The spread above is that portfolio's gross return. Non-overlapping holds, used for costs, are in section 11.

## 8. IS, validation, OOS

The phase split is the existing 60/20/20 chronological split of the 15-minute grid. The table in section 6 is the phase result.

Absolute basket returns at the 4-hour horizon, in basis points. This is the directional comparison.

| Feature | IS top | IS bottom | VAL top | VAL bottom | OOS top | OOS bottom |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| return_15m | -1.86 | 1.71 | -3.66 | 3.26 | -3.80 | 4.65 |
| return_1h | -0.25 | 0.83 | -2.73 | 2.09 | -4.34 | 4.99 |
| return_4h | 0.01 | 0.76 | -2.25 | 0.80 | -3.71 | 5.17 |
| return_12h | 0.29 | -0.67 | -2.22 | -0.37 | -1.30 | 2.58 |
| return_24h | -1.16 | -1.34 | -6.77 | 1.78 | -2.29 | 0.67 |
| return_1h_over_atr | -0.33 | 1.14 | -0.85 | 1.41 | -1.95 | 6.15 |
| return_4h_over_vol | 0.50 | 0.42 | 0.92 | 0.39 | -2.11 | 4.19 |
| return_24h_over_vol | 0.16 | -2.41 | -5.21 | 1.72 | -0.37 | 0.27 |
| relative_volume | 0.02 | 1.03 | 0.49 | 0.49 | 0.74 | 1.52 |
| volume_acceleration | -0.09 | 0.25 | 0.96 | -0.31 | 1.32 | 1.59 |

## 9. Chronological blocks

Mean 4-hour decile spread by quarter of the grid, in basis points.

| Feature | Block 1 | Block 2 | Block 3 | Block 4 |
| --- | ---: | ---: | ---: | ---: |
| return_15m | -2.67 | -5.67 | -4.26 | -8.25 |
| return_1h | 0.18 | -2.40 | -3.07 | -8.62 |
| return_4h | 0.33 | -2.25 | -1.71 | -7.69 |
| return_12h | 0.96 | 2.10 | -3.19 | -2.11 |
| return_24h | 0.29 | 2.58 | -9.17 | -2.46 |
| return_1h_over_atr | -0.57 | -3.47 | -0.25 | -7.52 |
| return_4h_over_vol | -0.34 | -1.63 | 2.75 | -5.17 |
| return_24h_over_vol | 2.97 | 2.12 | -3.87 | -1.11 |
| relative_volume | 0.68 | -1.91 | -2.96 | 1.20 |
| volume_acceleration | -0.77 | 0.25 | -0.14 | 0.66 |

## 10. Symbol participation

| Feature | OOS ranks | Distinct names in tails | Max top-decile share | Most frequent top name |
| --- | ---: | ---: | ---: | --- |
| return_15m | 13748 | 527 | 31.6% | BEATUSDT |
| return_1h | 13748 | 526 | 31.6% | BEATUSDT |
| return_4h | 13748 | 526 | 32.0% | UBUSDT |
| return_12h | 13748 | 526 | 33.2% | UBUSDT |
| return_24h | 13748 | 526 | 34.6% | UBUSDT |
| return_1h_over_atr | 13748 | 527 | 20.4% | JSTUSDT |
| return_4h_over_vol | 13748 | 527 | 22.0% | JSTUSDT |
| return_24h_over_vol | 13748 | 527 | 29.9% | JSTUSDT |
| relative_volume | 13748 | 527 | 13.9% | AINUSDT |
| volume_acceleration | 13748 | 527 | 14.6% | HANAUSDT |

## 11. Turnover and cost impact

Non-overlapping rebalance. Gross and net  are basis points per hold. Turnover is the average replaced fraction across both legs. One fully replaced leg costs 12 basis points round trip.

| Feature | Horizon | OOS holds | OOS gross | OOS turnover | OOS net |
| --- | --- | ---: | ---: | ---: | ---: |
| return_15m | 15m | 13747 | -5.70 | 0.80 | -24.84 |
| return_15m | 1h | 3436 | -6.97 | 0.79 | -26.00 |
| return_15m | 4h | 858 | -16.80 | 0.80 | -36.05 |
| return_15m | 12h | 285 | -37.26 | 0.81 | -56.76 |
| return_15m | 24h | 142 | -34.03 | 0.81 | -53.45 |
| return_1h | 15m | 13747 | -4.04 | 0.41 | -13.96 |
| return_1h | 1h | 3436 | -5.64 | 0.79 | -24.65 |
| return_1h | 4h | 858 | -10.80 | 0.80 | -30.09 |
| return_1h | 12h | 285 | -21.63 | 0.81 | -41.14 |
| return_1h | 24h | 142 | -46.16 | 0.82 | -65.72 |
| return_4h | 15m | 13747 | -2.41 | 0.21 | -7.54 |
| return_4h | 1h | 3436 | -4.68 | 0.41 | -14.60 |
| return_4h | 4h | 858 | -9.92 | 0.80 | -29.13 |
| return_4h | 12h | 285 | -2.12 | 0.81 | -21.49 |
| return_4h | 24h | 142 | -14.49 | 0.82 | -34.11 |
| return_12h | 15m | 13747 | -1.12 | 0.13 | -4.14 |
| return_12h | 1h | 3436 | -2.23 | 0.24 | -8.11 |
| return_12h | 4h | 858 | -6.09 | 0.48 | -17.59 |
| return_12h | 12h | 285 | -6.38 | 0.80 | -25.58 |
| return_12h | 24h | 142 | 3.85 | 0.80 | -15.44 |
| return_24h | 15m | 13747 | -0.70 | 0.09 | -2.87 |
| return_24h | 1h | 3436 | -1.99 | 0.17 | -6.18 |
| return_24h | 4h | 858 | -4.57 | 0.34 | -12.75 |
| return_24h | 12h | 285 | -1.11 | 0.58 | -14.92 |
| return_24h | 24h | 142 | 5.00 | 0.80 | -14.28 |
| return_1h_over_atr | 15m | 13747 | -3.90 | 0.48 | -15.53 |
| return_1h_over_atr | 1h | 3436 | -4.99 | 0.88 | -26.12 |
| return_1h_over_atr | 4h | 858 | -9.90 | 0.89 | -31.15 |
| return_1h_over_atr | 12h | 285 | -19.48 | 0.88 | -40.58 |
| return_1h_over_atr | 24h | 142 | -17.29 | 0.87 | -38.18 |
| return_4h_over_vol | 15m | 13747 | -2.05 | 0.25 | -7.99 |
| return_4h_over_vol | 1h | 3436 | -3.54 | 0.47 | -14.84 |
| return_4h_over_vol | 4h | 858 | -7.96 | 0.87 | -28.92 |
| return_4h_over_vol | 12h | 285 | -5.35 | 0.88 | -26.58 |
| return_4h_over_vol | 24h | 142 | -21.12 | 0.88 | -42.29 |
| return_24h_over_vol | 15m | 13747 | -0.77 | 0.12 | -3.55 |
| return_24h_over_vol | 1h | 3436 | -1.32 | 0.22 | -6.63 |
| return_24h_over_vol | 4h | 858 | -2.64 | 0.42 | -12.73 |
| return_24h_over_vol | 12h | 285 | 3.16 | 0.67 | -12.97 |
| return_24h_over_vol | 24h | 142 | 29.62 | 0.88 | 8.58 |
| relative_volume | 15m | 13747 | 0.37 | 0.70 | -16.42 |
| relative_volume | 1h | 3436 | 0.13 | 0.79 | -18.74 |
| relative_volume | 4h | 858 | -7.87 | 0.90 | -29.49 |
| relative_volume | 12h | 285 | -21.66 | 0.88 | -42.68 |
| relative_volume | 24h | 142 | -37.27 | 0.81 | -56.66 |
| volume_acceleration | 15m | 13747 | -0.06 | 0.75 | -18.15 |
| volume_acceleration | 1h | 3436 | 0.00 | 0.96 | -22.96 |
| volume_acceleration | 4h | 858 | -5.74 | 0.91 | -27.59 |
| volume_acceleration | 12h | 285 | -20.12 | 0.89 | -41.50 |
| volume_acceleration | 24h | 142 | -41.55 | 0.83 | -61.58 |

## 12. Microstructure comparison

Open interest, funding, taker imbalance, basis, and depth were not ranked. Each local series covers fewer than 30 names, so a decile would be one or two contracts. Those values were not filled in from the coins that do have history, and they were not invented for the rest of the universe.

## 13. Repeatability classification

- REPEATABLE: return_15m, return_1h.
- UNIVERSE_SPECIFIC: none.
- OOS_ONLY: return_4h, return_12h, return_4h_over_vol.
- UNSTABLE: return_24h, return_1h_over_atr, return_24h_over_vol, relative_volume.
- NO_EVIDENCE: volume_acceleration.

## 14. Failure modes

A positive spread can be long and short both rising, or both falling. Section 8 separates those cases. A spread that changes sign by block, or that appears only out of sample, is not a stable ranking. A spread smaller than the pre-registered round-trip cost does not survive implementation even when the sign is stable.

## 15. Data limitations

The cache is the current USD-M USDT perpetual file set. Contracts that were delisted before that cache was built are absent. That is a survivorship limit, and it is not repaired by backfilling. A name with no bar at the exit is left out of that basket rather than given a zero return. The BTC grid drops a timestamp when BTC itself has no bar. Microstructure deciles are not identified.

## 16. Final conclusion

Features that met the pre-registered bar: return_15m, return_1h. The stable sign is negative. The high-return decile underperforms the low-return decile on every pre-registered horizon for return_15m, and on the 15-minute, 1-hour, and 4-hour horizons for return_1h. At 4 hours, the top basket's own average return is negative and the bottom basket's is positive in sample, in validation, and out of sample. The pre-registered long-top short-bottom portfolio is negative before costs, and the non-overlapping out-of-sample holds for these two features stay negative after the pre-registered fee and slippage. The label is not an edge.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.
