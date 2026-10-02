# Execution consistency

Status of the historical books: **NOT VALIDATED**. Nothing below is a profitability result.

## Backtest, paper, and live

| Step | Backtest | Paper | Live |
| --- | --- | --- | --- |
| Signal bar | Closed candles only. An unclosed bar is dropped, and v2 also returns no signal if `IsClosed` is false. | The bot evaluates the closed series it loaded. | Same evaluator. `Trading:LiveTradingEnabled` is false, so this process does not send exchange orders. |
| Entry price | Next bar open, then slippage. If that bar does not exist, the order is not invented. | Paper runtime fills were removed. | Uses the exchange order path only when `Trading:LiveTradingEnabled` is true. The default is false. |
| Signal exit | Next bar open, then slippage. | Market-style paper close. | Not enabled. |
| Stop / target | Intrabar high/low. v2 templates set `HonorSuggestedStops` and `PreserveNullTake`, so the fill-adjusted signal stop is the working stop and a missing target is left open. | Protective prices from the signal, rounded with the contract tick. | Same protective path, gated by the live flag. |
| Same-bar stop and target | Stop is tested first. The favorable target is not chosen. | Exchange stop and take can both rest. The replay cannot see which traded first, so it keeps the stop. | Unchanged. |
| Fees | Entry and exit fees from the replay fee percent. | Paper fill fees. | Not sent. |
| Slippage | Replay `SlippagePercent`. Sensitivity cases are 5 bps (`0.05`), 10 bps (`0.10`), and 20 bps (`0.20`). A scripted flat round trip loses more as slippage rises. That test is not a strategy result. | Paper bps setting. | Not sent. |
| Funding | Applied only when settlement rows are passed in and `fundingTime` is at or before the bar close. Otherwise the report says `EXCLUDING_FUNDING`. That omission is not a zero funding rate. | A live funding print can be shown to the signal. It is not invented when missing. | Not sent. |

v1 imported rules still use `BookStopsOff`. That mode still flattens and can open the opposite side on the next open. v2 signals do not. An opposite v2 setup while a position is open returns Exit with `REVERSAL_DEFERRED`. The new entry can only be evaluated after that close, on a later closed bar. Cluc v2 now runs that exit before the higher-timeframe entry gate. A bearish completed 1h EMA closes the long. Missing 1h data leaves the open long under its existing stop and blocks a new long.

## Stops and targets

- The suggested stop is interpreted from the fill, not from the signal close. `TryStructuralStops` rejects a stop on the wrong side of the fill or closer than 0.2% of price. A rejected stop cancels that entry instead of inventing a price.
- Tick rounding, when `ReplaySettings.TickSize` is positive, moves a long stop up and a short stop down. That is tighter, not wider. A tick that would cross the fill rejects the entry. Paper and live already round with the contract tick. `BacktestService` copies `Symbol.TickSize` when that stored value is positive. If the symbol row is missing or the tick is 0, the replay says `TICK_SIZE_UNCONFIGURED` and does not invent a tick.
- Trailing stops on v2 ratchet only tighter on later closed bars (`HonorSuggestedStops`). They are not loosened.
- Breakeven after +1R uses `ProtectiveStopPrice` and the average fill. If that stop is missing, breakeven is not guessed.
- Time stops for v2 are `RefactoredStrategyEvaluator.MaxHoldBars`: Impulse 32, Cluc 24, Combined 24, Flat range 48, Bollinger reversion 12. Other v2 templates have no time stop. The replay also receives `PositionOpenedAt`, so the signal can emit the same time stop when the clock is present.
- Partial scale-out is not simulated. Where the spec asked for a first target and a runner, the backtest keeps one full exit. The report marks that as a limitation, not as a filled partial.

## Idempotency

`CandleIdempotency.Key` is the paper and live entry key: bot id plus the candle open time. The same candle produces the same key. `BotEngine` still refuses a second insert when that client order id already exists.

## Reversal

Default and v2: close first. No opposite entry on the signal bar. The trade reason contains `REVERSAL_DEFERRED` when the exit is caused by an opposite setup.

Imported `BookStopsOff` still reverses on the next open. That path was not retargeted, because it would change every existing imported baseline.

## Position ownership

One Isolated position per coin for the account. Another strategy cannot open a second position in that coin. The order and signal keep `StrategyId` / `StrategyVersionId`. v2 does not add a second book.

## Risk defaults

`StrategyExecutionRules` records the research starting point: 0.25% risk per trade, 1.5% aggregate open stop risk, 2% daily loss lock. These constants are not written onto saved risk profiles. Existing books and their overrides stay as stored.

Consecutive losses stay in `RiskEngine` and `BacktestReplay`. A losing round trip increments the counter. A non-negative round trip resets it to zero. After the configured streak, new entries stay locked until the cooldown ends. Exits stay allowed.

## Cost gate

v2 entries that have a target must clear three times the documented round trip: 4 bps entry fee, 4 bps exit fee, and 10 bps slippage each side. A smaller target is no-trade. This is a research assumption, not a measured exchange schedule.

## Unresolved

- Intrabar sequence when both stop and target trade is still unknowable from OHLC. Stop wins.
- Partial exits are not filled.
- Replay tick size is the stored symbol tick when that value is positive. Otherwise it stays 0 and the assumption text says `TICK_SIZE_UNCONFIGURED`.
- v1 flow and squeeze index paths can still read the last funding or open-interest slot of a full series.
- No walk-forward or OOS run was executed for the v2 templates. Historical candles were not loaded for a score. Every v2 status is `NOT_VALIDATED`.
