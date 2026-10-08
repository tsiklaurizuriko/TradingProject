# Canonical strategies

There is one runtime implementation per strategy. The old parallel `*_v2` ids are migration aliases. They are not in the strategy list, they are not seeded, and the evaluator refuses to run them.

`Trading:LiveTradingEnabled` stays `false` in `src/TradingPlatform.Api/appsettings.json` and `src/TradingPlatform.Workers/appsettings.json`. This change does not send an exchange order.

## Stable ids

| Canonical id | Default timeframe | Side | Notes |
| --- | --- | --- | --- |
| `impulse_catch` | 15m | LONG | Pump ride. Only the first close at least +16% above the 24h low, including a bar that jumps past 26%. 30-day trend from daily candles. Skips hourly volume above 7x the 7-day average and a close above the prior 30-day high. A wide entry bar is still an entry. 25% trail from the peak, 4-day cap. |
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
- `ImportedRuleEvaluator` and `AdvancedStrategyEvaluator` hand a canonical id or an obsolete alias to `RefactoredStrategyEvaluator`.
- Unused private methods that only those retired arms called were removed from both files. Methods still used by MACD-adjacent, HLHB, Turtle, and the other non-canonical books were kept. `FlatRangeStrategy` remains for its direct tests. The bot cycle does not call it.
- The bots screen no longer prefers a template key that contains `_v2`.

## Migration

`CanonicalStrategyMigration.Plan` is idempotent. A second plan on an already rewritten row has no updates. Compatible parameter numbers stay. Unknown parameter names and a timeframe that is not the canonical default are listed in `Reviews` and are not overwritten. Two enabled rows that already share a canonical id produce a failure and `EnsureSafe` throws. The failed plan is not applied. Duplicate alias rows are disabled and archived. Their ids and historical trades are not deleted. Related updates are saved in one transaction on PostgreSQL. In-memory tests cannot open that transaction and save without it.

## Status

Every canonical strategy is `NOT VALIDATED`. There is no local out-of-sample candle archive in this repo, so this change does not publish a backtest score.
