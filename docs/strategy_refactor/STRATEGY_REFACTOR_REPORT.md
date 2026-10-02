# Strategy refactor report

Every new template is **NOT VALIDATED**. Compilation, unit tests, and a scripted slippage check do not show that a strategy improved or made money. No in-sample or out-of-sample backtest was scored. Baseline template ids are unchanged and are not in this table as replacements.

v2 ids stay on the existing engine and stay out of `OperatorCatalog`, so the seeder does not force them on. A second pass adds them as disabled research rows. An existing row with the same template key is left as stored. `Trading:LiveTradingEnabled` remains false. No migration was required. No secrets were added. Historical comparison is still **NOT VALIDATED**.

Shared v2 rules:

- Signal on the closed bar only.
- Opposite setup while in a position exits and does not flip.
- Stop is ATR or swing structure from the signal, then re-anchored to the fill by the replay.
- A missing target stays open (`PreserveNullTake`). Fixed percent targets from the old rules are not emitted.
- Missing higher-timeframe, funding, open interest, or taker volume is no-trade. It is not filled with zero.
- Research risk constants are 0.25% / 1.5% / 2% and are not applied to stored books.

| Template | Timeframe | Direction | Entry | Exit | Stop / target | Data | Tests | Backtest |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `impulse_catch_v2` | 15m | Long only | Newest close that breaks the prior window high, with return at least `PriceDisplacementAtr` times ATR% and usable relative volume. Pullback to EMA must happen after that bar. `MaxImpulseAgeBars` default 32. A close through the pullback low invalidates the setup. One signal per closed bar. | 2.5× ATR trail. Time stop 32 bars. No fixed 20% target. | Tighter of the pullback low and 1.8× ATR. | OHLCV | Recent pullback entry, expired impulse, pullback before completion, invalidation, duplicate candle, short history, future bar. | NOT VALIDATED |
| `zigzag_fade_v2` | 15m | Long and short | Confirmed swing only (`ConfirmedSwingLow/High`, visible n bars later). Sweep beyond 0.3× ATR and a close back through the swing. The next bar must not continue the sweep. ETH/SOL channel overrides are gone. | `REVERSAL_DEFERRED` on an opposite setup. 2× ATR trail. | Stop beyond the sweep by 0.3× ATR. Full exit at 1.5R. The runner is not split. | OHLCV | Long reclaim, unconfirmed pivot is not the last low, no same-bar flip. | NOT VALIDATED |
| `triple_supertrend_v2` | 1h | Long and short | One Supertrend (10, 3). Long only above EMA200, short only below. Pullback to EMA20 and a close back with the trend. | Supertrend flip, or 3× ATR trail. Breakeven after +1R when the fill stop is known. | Initial stop 2× ATR. No fixed 8/10. | OHLCV | Warmup on a short series. | NOT VALIDATED |
| `ts_momentum_v2` | 1d | Long only, BTC when the symbol is set | Close above EMA100 and a positive 28-bar return on the closed bar. | Close back under EMA100 or a non-positive return. | 3× ATR guard. No fixed 30% target. Size still comes from the risk engine and this stop, using only the history in the stop distance. | OHLCV | Non-BTC symbol is flat. No short. | NOT VALIDATED |
| `ema_cross_v2` | 30m | Long and short | EMA200 regime, EMA20 vs EMA50, pullback into that zone, close back through EMA20, ADX above 18. | Opposite EMA cross (`REVERSAL_DEFERRED`) or 2.5× ATR trail. | Swing or 1.5× ATR, the tighter valid one. 1.5R partial is not enabled and has no separate fill test. No fixed 1%/3%. | OHLCV | Warmup. | NOT VALIDATED |
| `fadx_sma_v2` | 1h | Long and short | SMA20/SMA50 cross, ADX above 20 and rising for three bars, EMA200 regime. | Opposite cross is Exit, not Hold. ADX under 18 for two bars. 2× ATR trail. Breakeven after +1R when the stop is known. | 2× ATR. No same-bar flip. | OHLCV | Warmup. | NOT VALIDATED |
| `binhv45_v2` | 5m | Long and short | 1h EMA200 regime. Bollinger(40, 2) reclaim, close through the prior bar extreme, volume above 1.2× the prior average. A close under the band alone is not an entry. | Middle band only in profit. Opposite setup defers. | Swing or 1.5× ATR. Target must clear 1.5R and 3× round-trip cost. No fixed ±2.5%. | OHLCV + completed 1h | Missing 1h regime is `DATA_UNAVAILABLE`. | NOT VALIDATED |
| `cluc_may72018_v2` | 15m | Long only | Open positions are managed before the entry gate. A bearish completed 1h EMA exits the long. Missing 1h data does not. A new long still needs a bullish 1h EMA, a close back above the lower band, RSI under 35 and rising, and a bullish close. | Bearish higher timeframe, middle band in profit, or 24-bar time stop. | 0.3× ATR beyond the sweep low. Target is the middle band when it pays 1.5R, otherwise 1.5R. Neither is used if cost or reward fails. No 5%/1% pair. | OHLCV + completed 1h | Bearish exit, missing data while open, bearish flat, missing flat, bullish reclaim, duplicate candle. | NOT VALIDATED |
| `cluc_may72018_v2_30m` | 30m | Long only | Same rules as the 15m Cluc v2. Separate id so a later split can be scored alone. | Same. | Same. | Same | Same code path. | NOT VALIDATED |
| `combined_binh_cluc_v2` | 15m | Long, and a short BinHV mirror | Flat regime: ADX under 20 and a small EMA200 slope. BinHV45 and Cluc stay separate reasons (`setup_type`). Both on one bar open one position. | Range break, 24-bar time stop, or middle band only while in profit. A losing middle-band touch is not an exit. | Swing or ATR. Minimum reward 1.3R after the cost gate. No fixed ±5%. | OHLCV | Warmup. | NOT VALIDATED |
| `donchian_breakout_v2_4h` | 4h | Long and short | Close beyond the prior 20-bar channel. The channel excludes the signal bar. EMA100 filter. | 10-bar exit channel, or opposite break as `REVERSAL_DEFERRED`. | 2× ATR initial, 3× ATR trail, both suggested into the replay. No fixed 8%/30%. | OHLCV | Prior-channel break and a future spike do not change the closed bar. | NOT VALIDATED |
| `donchian_breakout_v2_1d` | 1d | Long and short | Same, with a 55-bar entry channel and a 20-bar exit channel. | Same structure. | Same ATR model. | OHLCV | Key and timeframe registration. | NOT VALIDATED |
| `squeeze_watch_v2` | 1h | Long and short | Funding at or beyond the rolling 90th / 10th percentile of `FundingLookback` (720 bars). Open interest must exist at 4 and 24 bars. Entry also needs a failed break. Funding alone does not enter or exit. Stale funding (more than two missing bars) is no-trade. | Failed break plus the crowding flipping, or the ATR stop. | Swing or 1.5× ATR. Target 1.5R if it clears cost. No ±0.10% band. | OHLCV, funding, open interest | Missing series says not zero. | NOT VALIDATED. Funding impact was not scored. |
| `flow_zone_v2` | 5m signal, 1h regime | Long and short | Break of the prior 20-bar high or low, then the next bar confirms or retests. Taker buy share above 60% or below 40%. Open interest not down. 1h EMA200 must agree. | Opposite confirmation defers.  ATR trail. Breakeven after +1R when the stop is known. | 1.5× ATR stop, 2R target. No +8% breakeven, no 15% target, no 0.20% fee hold. | OHLCV, taker volume, open interest, completed 1h | Missing taker, open interest, or 1h regime is no-trade. | NOT VALIDATED |
| `flat_range_v2` | 1h | Long and short | ADX under 18. Prior 24 bars, signal bar excluded. Width 0.8%–6%. Long in the bottom 15% after a bullish rejection. Short in the top 15% after a bearish rejection. A close outside the range is not an entry. | Range break, stop, midpoint in profit, or 48-bar maximum hold. The hold is not the only exit. | 0.25× ATR beyond the range edge. Far target only when reward is at least 1.5R. The 8 USDT margin constant is not part of v2. | OHLCV | Warmup. | NOT VALIDATED |
| `ema_rsi_trend_v2` | 15m | Long and short | 1h EMA200, EMA20/EMA50 aligned, RSI pullback through 40–50 then back through 50, close through EMA20, volume above 1.2× the prior median. | Opposite EMA cross, 2R target, ATR trail. | Swing or 1.5× ATR. No fixed 3%/9%. | OHLCV + completed 1h | Missing 1h regime is no-trade. | NOT VALIDATED |
| `ema_rsi_trend_v2_30m` | 30m | Long and short | Same rules as the 15m EMA RSI v2. | Same. | Same. | Same | Same code path. | NOT VALIDATED |
| `bollinger_reversion_v2` | 15m | Long and short | Flat regime only (ADX under 20 and a small EMA200 slope). Prior close outside Bollinger(20, 2), current close back inside, RSI under 35 and rising for a long (mirror for a short), bullish or bearish confirmation. The EMA50 side filter is not used. | Middle band, 12-bar time stop, or a regime break. | Swing, capped at 1.5× ATR. Middle band must clear 3× round-trip cost. No fixed 2.5%/2%. | OHLCV | Warmup. | NOT VALIDATED |

## Config

Each v2 id has defaults in `StrategyTemplates.DefaultsFor`. Impulse Catch v2 adds `MaxImpulseAgeBars` (default 32). Old JSON that omits the field reads as 32. `StrategyTemplates.Validate` still rejects impossible periods. Saved profiles are not rewritten.

## Research selection

v2 ids are `IsResearchWorkflow` and are not in `OperatorCatalog`. On the next database seed, each missing v2 template is inserted disabled, not archived, with status `NOT_VALIDATED`. A second seed does not insert another row and does not change an existing definition. Enabling a row in the strategy list does not set `Trading:LiveTradingEnabled` and does not start a bot. Baseline ids stay in the operator catalog.

## Unit tests

`RefactoredStrategyTests` covers registration, unclosed candles, duplicate evaluation, Donchian prior channel versus a future spike, ZigZag confirmation lag and a long reclaim, missing funding, missing 1h regime, the BTC filter, short warmup, tick rounding that does not widen the stop, the cost gate, and a stable candle order key.

`SecondPassStrategyTests` covers Impulse Catch age, Cluc position management, research registration without live activation, and a flat/unclosed/duplicate check across every v2 id. It does not add a hand-built entry fixture for every template.

`BacktestReplayTests` covers next-open fills, stop-before-target on one bar, 5/10/20 bps slippage and the same fee steps on a scripted flat round trip, a missing tick reported as `TICK_SIZE_UNCONFIGURED`, and a missing target that is not replaced with 2R or the book percent target.

These tests use fixed candles. They do not use a live market.

## Baseline versus v2

No trade list was produced for either side. There is no win rate, profit factor, drawdown, or fee sensitivity number to compare. A comparison that is not run is not a pass.

## Limitations

- One full exit. No partial at 1.5R.
- OHLC cannot order a stop and a target inside the same bar.
- v2 backtests omit funding unless the caller passes settlements.
- Squeeze percentiles need 30 real funding prints. A short file does not trade.
- Cluc does not report how often the old 1% target would have clipped the winner, because that exit was removed and not re-scored.
- The 30m Cluc and 30m EMA RSI variants are the same rules on a different label until a separate historical run exists.
