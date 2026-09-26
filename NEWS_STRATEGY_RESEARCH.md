# News strategy research

Historical result, robustness, out-of-sample result, and validated result are separate. A profitable backtest is not a validated strategy. This run does not assign `VALIDATED_FOR_PAPER`.

## Data Sources

- CoinGecko news: recent pages when a demo key is configured. Not a guaranteed historical archive. No key is stored in source.
- CryptoPanic: token required. Free history is limited. No token is stored in source.
- GDELT DOC 2.0: historical article lists. Seen time is not always the original publication time. Asset mapping is title-based.
- FRED: CPI, PPI, payrolls, and fed funds actuals. Observation dates are date-only, so they are excluded from intraday bars. Consensus is not provided and is not invented.

## Universe

- Active USD-M USDT perpetuals discovered from the existing universe cache: 527
- Research stage: A
- Symbols considered this pass: 0GUSDT, 1000000BOBUSDT, 1000000MOGUSDT, 1000BONKUSDT, 1000CATUSDT, 1000CHEEMSUSDT, 1000FLOKIUSDT, 1000LUNCUSDT, 1000PEPEUSDT, 1000RATSUSDT, 1000SATSUSDT, 1000SHIBUSDT +8 more
- Stage A sample is the first 20 discovered contracts that already have a local candle file, in universe-cache order. That order is not 24h quote-volume rank. The scanner rank is not stored in the universe file, so large-cap, mid-cap, meme, DeFi, and L1 groups were not invented.
- News is collected once and mapped onto affected assets. The runner does not call a news API per symbol.

## News Event Statistics

- Total articles: 0
- Unique events: 0
- Duplicate ratio: 0.00
- DATASET_SCOPE: EMPTY
- Assets: none
- Categories: none
- Sources: none

## Strategy Results

Sortino and Calmar are N/A. The existing metrics stack does not calculate them.

| Strategy | Timeframe | Asset | Trades | Win Rate | PF | Expectancy | Sharpe | Max DD | OOS PF | OOS Expectancy | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| news_momentum | 1h | 0GUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 0GUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 0GUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 0GUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 0GUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 0GUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 0GUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 0GUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 0GUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000000BOBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000000BOBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000000BOBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000000BOBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000000BOBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000000BOBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000000BOBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000000BOBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000000BOBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000000MOGUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000000MOGUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000000MOGUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000000MOGUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000000MOGUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000000MOGUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000000MOGUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000000MOGUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000000MOGUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000BONKUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000BONKUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000BONKUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000BONKUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000BONKUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000BONKUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000BONKUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000BONKUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000BONKUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000CATUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000CATUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000CATUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000CATUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000CATUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000CATUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000CATUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000CATUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000CATUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000CHEEMSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000CHEEMSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000CHEEMSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000CHEEMSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000CHEEMSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000CHEEMSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000CHEEMSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000CHEEMSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000CHEEMSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000FLOKIUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000FLOKIUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000FLOKIUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000FLOKIUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000FLOKIUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000FLOKIUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000FLOKIUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000FLOKIUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000FLOKIUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000LUNCUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000LUNCUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000LUNCUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000LUNCUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000LUNCUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000LUNCUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000LUNCUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000LUNCUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000LUNCUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000PEPEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000PEPEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000PEPEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000PEPEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000PEPEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000PEPEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000PEPEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000PEPEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000PEPEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000RATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000RATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000RATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000RATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000RATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000RATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000RATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000RATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000RATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000SATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000SATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000SATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000SATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000SATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000SATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000SATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000SATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000SATSUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000SHIBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000SHIBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000SHIBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000SHIBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000SHIBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000SHIBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000SHIBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000SHIBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000SHIBUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1000XECUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1000XECUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1000XECUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1000XECUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1000XECUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1000XECUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1000XECUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1000XECUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1000XECUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1INCHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1INCHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1INCHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1INCHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1INCHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1INCHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1INCHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1INCHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1INCHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 1MBABYDOGEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 1MBABYDOGEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 1MBABYDOGEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 1MBABYDOGEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 1MBABYDOGEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 1MBABYDOGEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 1MBABYDOGEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 1MBABYDOGEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 1MBABYDOGEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 2ZUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 2ZUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 2ZUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 2ZUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 2ZUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 2ZUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 2ZUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 2ZUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 2ZUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | 4USDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | 4USDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | 4USDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | 4USDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | 4USDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | 4USDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | 4USDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | 4USDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | 4USDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | AAVEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | AAVEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | AAVEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | AAVEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | AAVEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | AAVEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | AAVEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | AAVEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | AAVEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | ACEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | ACEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | ACEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | ACEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | ACEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | ACEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | ACEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | ACEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | ACEUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_momentum | 1h | ACHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_overreaction | 1h | ACHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_continuation | 1h | ACHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_short_cover | 1h | ACHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_oi_long_liquidation | 1h | ACHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_liquidation | 1h | ACHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_macro | 1h | ACHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_structure | 1h | ACHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |
| news_taker | 1h | ACHUSDT | 0 | N/A | NoTrades | N/A | N/A | N/A | NoTrades | N/A | DATA_UNAVAILABLE |

No local historical news dataset is stored. Live API responses were not used to invent a past archive.

## Cross-asset research

DATASET_SCOPE = EMPTY.
Q1 affected-asset prediction, Q2 spillover, Q3 BTC-to-alt, Q4 ETH-versus-BTC, Q5 macro differences, Q6 liquidity, and Q7 volatility regime were not measured on this run.
Leader/follower forward returns at 5m, 15m, 30m, 1h, and 4h were not measured. Status: DATA_UNAVAILABLE. No returns were fabricated, and none of these questions were turned into orders.
The universe file has no large-cap, mid-cap, small-cap, meme, DeFi, or L1 labels. Those aggregates were not invented. All-assets status follows the rows above.

## Best Candidate Rules

No rule is called profitable. No rule is validated for paper.
- `0GUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `0GUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `0GUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `0GUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `0GUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `0GUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `0GUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `0GUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `0GUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000BOBUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000BOBUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000BOBUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000BOBUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000BOBUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000BOBUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000BOBUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000BOBUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000BOBUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000MOGUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000MOGUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000MOGUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000MOGUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000MOGUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000MOGUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000MOGUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000MOGUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000000MOGUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000BONKUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000BONKUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000BONKUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000BONKUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000BONKUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000BONKUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000BONKUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000BONKUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000BONKUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CATUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CATUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CATUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CATUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CATUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CATUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CATUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CATUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CATUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CHEEMSUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CHEEMSUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CHEEMSUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CHEEMSUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CHEEMSUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CHEEMSUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CHEEMSUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CHEEMSUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000CHEEMSUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000FLOKIUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000FLOKIUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000FLOKIUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000FLOKIUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000FLOKIUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000FLOKIUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000FLOKIUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000FLOKIUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000FLOKIUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000LUNCUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000LUNCUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000LUNCUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000LUNCUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000LUNCUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000LUNCUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000LUNCUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000LUNCUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000LUNCUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000PEPEUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000PEPEUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000PEPEUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000PEPEUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000PEPEUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000PEPEUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000PEPEUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000PEPEUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000PEPEUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000RATSUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000RATSUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000RATSUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000RATSUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000RATSUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000RATSUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000RATSUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000RATSUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000RATSUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SATSUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SATSUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SATSUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SATSUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SATSUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SATSUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SATSUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SATSUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SATSUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SHIBUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SHIBUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SHIBUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SHIBUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SHIBUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SHIBUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SHIBUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SHIBUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000SHIBUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000XECUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000XECUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000XECUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000XECUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000XECUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000XECUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000XECUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000XECUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1000XECUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1INCHUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1INCHUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1INCHUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1INCHUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1INCHUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1INCHUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1INCHUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1INCHUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1INCHUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1MBABYDOGEUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1MBABYDOGEUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1MBABYDOGEUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1MBABYDOGEUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1MBABYDOGEUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1MBABYDOGEUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1MBABYDOGEUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1MBABYDOGEUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `1MBABYDOGEUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `2ZUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `2ZUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `2ZUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `2ZUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `2ZUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `2ZUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `2ZUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `2ZUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `2ZUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `4USDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `4USDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `4USDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `4USDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `4USDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `4USDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `4USDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `4USDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `4USDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `AAVEUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `AAVEUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `AAVEUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `AAVEUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `AAVEUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `AAVEUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `AAVEUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `AAVEUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `AAVEUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACEUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACEUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACEUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACEUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACEUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACEUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACEUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACEUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACEUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACHUSDT news_momentum`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACHUSDT news_overreaction`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACHUSDT news_oi_continuation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACHUSDT news_oi_short_cover`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACHUSDT news_oi_long_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACHUSDT news_liquidation`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACHUSDT news_macro`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACHUSDT news_structure`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.
- `ACHUSDT news_taker`: DATA_UNAVAILABLE. Historical, robust, out-of-sample, and validated are not the same claim.

Open interest is DATA_UNAVAILABLE unless an aligned series is already present. It was not fabricated. Liquidation history remains DATA_UNAVAILABLE. Taker imbalance is the existing order-flow field. CVD was not added.
