# Canonical strategies

There is one runtime implementation per strategy. The old parallel `*_v2` ids are migration aliases. They are not in the strategy list, they are not seeded, and the evaluator refuses to run them.

`Trading:LiveTradingEnabled` stays `false` in `src/TradingPlatform.Api/appsettings.json` and `src/TradingPlatform.Workers/appsettings.json`. This change does not send an exchange order.

## Stable ids

| Canonical id | Default timeframe | Side | Notes |
| --- | --- | --- | --- |
| `impulse_catch` | 15m | LONG | Max impulse age 32 bars. |
| `zigzag_fade` | 15m | LONG and SHORT | |
| `triple_supertrend` | 1h | LONG and SHORT | |
| `ts_momentum_28_5` | 1d | LONG | |
| `btc_ema20_ema50_long` | 30m | LONG and SHORT | This is the former EMA-cross v2 book, on the existing id. |
| `fadx_sma` | 1h | LONG and SHORT | |
| `binhv45` | 5m | LONG and SHORT | Needs a completed 1h EMA. Missing data is not a signal. |
| `cluc_may72018` | 15m | LONG | The 30m twin was folded into this id. |
| `combined_binh_cluc` | 15m | LONG and SHORT | |
| `donchian_breakout` | 4h | LONG and SHORT | 20-bar channel. Now in the operator catalog. |
| `donchian_v2_55` | 1d | LONG and SHORT | 55-bar channel. Same Donchian method, different lookbacks. |
| `squeeze_watch` | 1h | LONG and SHORT | Missing funding or open interest is not zero. |
| `flow_zone` | 5m | LONG and SHORT | |
| `flat_range` | 1h | LONG and SHORT | |
| `ema_rsi_trend` | 15m | LONG and SHORT | The 30m twin was folded into this id. |
| `bollinger_reversion` | 15m | LONG and SHORT | |

`donchian_v2_55` keeps that name because it is a different lookback from `donchian_breakout`, not a second copy of the same strategy.

Strategies that never had a v2 twin are unchanged. That includes MACD, RSI pullback, HLHB, the scalping set, near-miss, and cross-sectional reversal.

## Aliases removed from the runtime

These strings are recognized only by the migration. Evaluating one returns `Obsolete strategy id` and does not fall through to an older method.

`impulse_catch_v2`, `zigzag_fade_v2`, `triple_supertrend_v2`, `ts_momentum_v2`, `ema_cross_v2`, `fadx_sma_v2`, `binhv45_v2`, `cluc_may72018_v2`, `cluc_may72018_v2_30m`, `combined_binh_cluc_v2`, `donchian_breakout_v2_4h`, `donchian_breakout_v2_1d`, `squeeze_watch_v2`, `flow_zone_v2`, `flat_range_v2`, `ema_rsi_trend_v2`, `ema_rsi_trend_v2_30m`, `bollinger_reversion_v2`.

`ema_cross_v2` maps to `btc_ema20_ema50_long`. `donchian_breakout_v2_1d` maps to `donchian_v2_55`. Both 30m twins map to the unsuffixed id.

## What was removed from the dispatch

- `StrategyTemplates.ForLive` and `LiveCounterpart`. Paper and live use the saved definition.
- The separate v2 seed list. `StrategyTemplateKeys.Refactored` is empty.
- The template dispatcher no longer calls `FlatRangeStrategy` or the retired arms for these ids.
- `ImportedRuleEvaluator` and `AdvancedStrategyEvaluator` hand a canonical id to `RefactoredStrategyEvaluator` and do not select the old switch arms.
- The bots screen no longer prefers a template key that contains `_v2`.

The old private methods are still in those two files. They are not selected. `FlatRangeStrategy` is still in the tree for its direct tests. The bot cycle does not call it.

## Status

Every canonical strategy is `NOT VALIDATED`. There is no local out-of-sample candle archive in this repo, so this change does not publish a backtest score.
