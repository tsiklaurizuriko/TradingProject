# Second pass audit

Branch `main`, commit `cb9d906` (`v2`). The audit read the evaluator, templates, seeder, replay, and tests. It did not treat the first-pass docs as the source of behavior.

## Already in the tree

- Eighteen v2 template ids in `StrategyTemplateKeys.Refactored` and `All`. Baseline ids are still in `OperatorCatalog` (26 keys).
- `RefactoredStrategyEvaluator` dispatches those ids. Signals require a closed bar.
- `StrategyExecutionRules` holds the research cost gate, tick rounding, and status `NOT_VALIDATED`.
- Backtest replay fills the next open, checks the stop before the target, and leaves a missing v2 target open (`PreserveNullTake`).
- `Trading:LiveTradingEnabled` is false in the API and Workers appsettings.
- Unit coverage already included Donchian excluding the signal bar, ZigZag confirmation lag, missing funding, missing 1h regime, tick rounding, and a stable candle order key.

## What an operator could actually select

`DatabaseSeeder.SeedStrategiesAsync` inserts only `OperatorCatalog` keys. `RetireHiddenStrategiesAsync` archives a known key that is not in that catalog. v2 ids were therefore not created, and a hand-inserted v2 row would be archived on the next seed. The strategy list reads non-archived rows, so v2 was not in the research picker. The engine could still evaluate the ids if a definition JSON named them.

## Tests that existed

`RefactoredStrategyTests` asserted that every v2 id was known, not in the operator catalog, and `NOT_VALIDATED`. It did not assert an Impulse Catch entry, an impulse age limit, or Cluc management while a higher-timeframe regime was bearish or missing.

## Gaps found in code

1. Impulse Catch v2 walked backward without an age cap. The first return-and-volume match could be an old thrust. A later pullback could still enter. The pullback scan also included the impulse bar itself.
2. Cluc v2 returned `NoAction` when the 1h EMA was not bullish, before it looked at an open long. A bearish regime or missing 1h series skipped the time stop, the middle-band exit, and any other management.
3. v2 was not seeded and was retired if present. There is an `IsEnabled` flag. New research rows should use it and stay off.
4. `BacktestService` did not pass a symbol tick. Replay tick defaulted to 0, which skips rounding. Paper and live already use the contract tick when the symbol row has one.
5. Imported `BookStopsOff` still opens the opposite side on the next open. v2 already returns `Exit` (`REVERSAL_DEFERRED`) and does not flip on that bar. Changing the imported path would move the baselines.
6. No v2 path emits a partial exit. The replay still books one full close.
7. No local candle store exists for a walk-forward score. Funding is still omitted unless settlements are passed, and that omission is already labeled `EXCLUDING_FUNDING`.

## Planned changes

- Cap Impulse Catch with `MaxImpulseAgeBars` (default 32), require the pullback after the impulse, invalidate a close through the pullback low, and record index, age, pullback, and expiry.
- Run Cluc management before the entry gate. Bearish 1h exits the long. Missing 1h does not.
- Seed missing v2 rows disabled and skip them in the retire/enable pass. Do not rewrite an existing template row. Do not add them to `OperatorCatalog`.
- Copy a positive stored `Symbol.TickSize` into the replay. Say `TICK_SIZE_UNCONFIGURED` when it is absent.
- Add regression tests. Do not score a historical book without candles.
