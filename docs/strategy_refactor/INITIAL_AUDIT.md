# Initial audit

This note records the trading stack as it existed before the versioned strategy templates were added. It is an audit of behavior, not a performance claim.

## Architecture

The bot evaluates a strategy template on closed candles and sends at most one Isolated futures position per coin.

- Template ids live in `StrategyTemplateKeys`. The engine dispatches from `StrategyTemplateEvaluator` to the frozen, advanced, imported, flow, and flat-range evaluators.
- `StrategyEngine.Evaluate` uses only the closed-candle list it is given. `BacktestReplay` drops candles with `IsClosed = false` unless a prebuilt cache is supplied.
- A signal becomes an order in `BotEngine`. The client order id is one key per bot and candle open time. A repeated candle hits `HasClientOrderAsync` and does not create a second order.
- Position size, leverage, daily loss, and the consecutive-loss cooldown come from the saved risk book via `RiskEngine`. Leverage caps notional. It does not replace stop-distance sizing when a structural stop is honored.
- Paper fills use `PaperFillModel` and `TradingOptions.PaperSlippageBps`. Live order send stays behind `Trading:LiveTradingEnabled`. Both `src/TradingPlatform.Api/appsettings.json` and `src/TradingPlatform.Workers/appsettings.json` have that flag set to `false`.
- Backtests use the same strategy engine and `RiskEngine`. Entries and signal exits fill at the next bar open. A pending entry with no following bar is dropped.

## Position ownership

Isolated occupancy is one open position per coin on the account. A position opened by any strategy blocks that coin for every other strategy. `RiskEngine` rejects a new entry when `SymbolAlreadyOpen` is true. Signals store `StrategyId` and `StrategyVersionId`. This ownership rule was left unchanged.

## The fifteen baselines

| # | Template id | What the code actually does |
| --- | --- | --- |
| 1 | `impulse_catch` | 15m long only. Buys when the 16-bar rise first crosses 8%, the bar closes in its upper half, and volume is at least 1.5 times the prior 20-bar average. Exits on a 3% down bar. No pullback. Fixed book stop/target sits outside the signal. |
| 2 | `zigzag_fade` | Confirmed swings. ETHUSDT uses a 6% channel and SOLUSDT a 5% channel. A close through the swing is an entry, including a same-direction flip. ATR stop is replaced from the close every bar. |
| 3 | `triple_supertrend` | Three long Supertrend settings and three different short settings. Exit long on 18/3 down, exit short on 9/7 up. No shared ATR stop in the signal. |
| 4 | `ts_momentum_28_5` | BTC daily, long only. 28-day return in the top third of its own history, held as up to five sleeves. No short. Fixed book stop is a rail, not the signal. |
| 5 | `btc_ema20_ema50_long` | 30m long only. EMA20 cross above EMA50. Exit on the cross back. No ADX, no pullback, no ATR stop. |
| 6 | `fadx_sma` | SMA(12)/SMA(48) with ADX above 30. While a position is open, an opposite cross is Hold unless ADX falls under 30. |
| 7 | `binhv45` | Long only. Close under the prior Bollinger(40, 2) lower band with wick gates. Exit at ±2.5% on the close. |
| 8 | `cluc_may72018` | Long only. Close under EMA50 and under 98.5% of the typical-price lower band, and volume under 20 times the prior average. Exit on the middle band, 1% ROI, or 5% stop. |
| 9 | `combined_binh_cluc` | Either BinHV or Cluc entry. Middle-band exit only while the close is in profit. 5% ROI and 5% stop. |
| 10 | `donchian_breakout` and `donchian_v2_55` | Frozen Donchian uses the prior channel (`DonchianSeries` excludes the current bar) plus an EMA cross exit. `donchian_v2_55` uses a 55-bar entry channel and a 5-bar exit channel, with an ATR stop quoted from the close. The generic backtest applies that stop only when the template is marked imported. |
| 11 | `squeeze_watch` | 1h. Quiet 24-bar price, open interest up 15%, funding at ±0.10%. Missing funding or open interest is no-trade. An open trade exits when funding leaves that static band. |
| 12 | `flow_zone` | 1h, not 5m. Upper/lower quarter of a 24-bar range, taker share, and rising open interest. Missing taker or open interest is no-trade. A profitable exit can be held back by a 0.20% fee band. |
| 13 | `flat_range` | Prior 24 hours, current bar excluded. Width 0.8%–6% and a narrow-rank filter. Entry in the outer 20%. Stop at the bound. A 24-hour time stop. `MaxEntryMarginUsdt = 8` is a constant on the type, not an order-size check in the signal. |
| 14 | `ema_rsi_trend` | EMA cross with RSI in a band. Exit on the opposite cross. No higher-timeframe filter and no ATR stop in the signal. |
| 15 | `bollinger_reversion` | Prior close outside the band, current close back inside, and an EMA50 side filter. Exit at the middle band even if that locks a loss. |

## Execution mismatches found

- Generic backtests size and exit with the risk book's percent stop and percent target. Structural stops from the signal are used for flat range (`HonorSuggestedStops`) and imported rules (`BookStopsOff`). The other ten baselines can quote a stop that the replay never places.
- `BookStopsOff` flattens and opens the opposite side on the same next open. The default path flattens only.
- If stop and target are both inside one bar, the replay closes at the stop. That is the conservative path. Intrabar order is still unknown.
- When funding settlements are absent, funding cashflow is omitted. The result line is not a measured funding rate of zero. Several v1 signals still treat a missing print as "no order", which is the right signal behavior.
- `FlowZone` and `SqueezeWatch` read the last funding or open-interest slot (`[^1]`) on some paths. An index replay that passes the full series can see a later print. v1 was not rewritten.
- Flat-range evaluation disagreed with itself: `Evaluate` applied the generic volume filter and `EvaluateDetailAt` did not. Those two paths are now aligned. That does not change the flat-range rule.
- Tick size is applied on the live and paper protective path. The replay tick size defaulted to zero, so backtests did not round to the exchange tick.
- A suggested target of null was replaced with 2R, and a null target also fell back to the book percent target. A strategy that wants an open target could not say so.
- Consecutive-loss state is real. `BacktestReplay` increments on a negative round trip and resets to zero on a non-negative one. Entries stay locked until cooldown after the configured streak. The stored risk books were not edited.

## What is strategy logic and what is execution

Strategy logic is the template evaluator: when a closed bar is Buy, Sell, Exit, or Hold, and which stop and target it suggests.

Execution infrastructure is `BacktestReplay`, `RiskEngine`, `BotEngine`, paper fill, and the Isolated one-coin lock. The refactor extends those paths. It does not add a second order router.
