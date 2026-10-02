# Second pass results

Statuses below mean: **IMPLEMENTED** the code change is in the tree, **TESTED** a fixed fixture asserts it, **NOT VALIDATED** no chronological historical score was run, **KNOWN LIMITATION** the behavior is intentional and still different from a live fill, **BLOCKED** the check could not be executed.

No strategy is marked profitable. No Binance order was sent. `Trading:LiveTradingEnabled` is still false in `src/TradingPlatform.Api/appsettings.json` and `src/TradingPlatform.Workers/appsettings.json`. Baseline template ids are unchanged. No migration was added. Secrets were not written into these notes.

## Problems and fixes

| Item | Status | What changed |
| --- | --- | --- |
| Impulse Catch could enter on an old thrust | IMPLEMENTED, TESTED | `MaxImpulseAgeBars` defaults to 32. The impulse is the newest close that breaks the prior window high inside that age. The pullback must be after that bar. A later close through the pullback low is no-trade. The reason and snapshot carry `impulseIndex`, `impulseAge`, `pullback`, and `expired`. |
| Cluc skipped an open long when 1h was not bullish | IMPLEMENTED, TESTED | Open positions are managed first. Bearish completed 1h EMA exits the long. Missing 1h data keeps the position and blocks a new long. The two reasons are different strings. |
| v2 missing from the research picker | IMPLEMENTED, TESTED | `IsResearchWorkflow` is true for every v2 id. They stay out of `OperatorCatalog`. The seeder inserts a missing v2 row with `IsEnabled = false`, not archived, status `NOT_VALIDATED`. A second pass skips a template key that already exists and does not rewrite its definition. `RetireHiddenStrategiesAsync` does not archive or enable those rows. |
| Replay tick stuck at 0 | IMPLEMENTED, TESTED | `BacktestService` passes `Symbol.TickSize` when it is positive. Otherwise the assumption text contains `TICK_SIZE_UNCONFIGURED`. A tick is not invented. |
| Imported same-next-open reversal | KNOWN LIMITATION | `BookStopsOff` still reverses imported rules on the next open. v2 still exits with `REVERSAL_DEFERRED` and does not flip on the signal bar. |
| Partial exits | KNOWN LIMITATION | v2 does not emit a partial. The replay still closes the whole position once. |
| Fees, slippage, funding | TESTED for the scripted path, NOT VALIDATED on history | 5/10/20 bps (`0.05`, `0.10`, `0.20` percent) fee and slippage cases on a flat scripted round trip get worse as the cost rises. Missing funding is still `EXCLUDING_FUNDING`, not a zero rate. |
| Idempotency | TESTED | `CandleIdempotency.Key` is bot id plus candle open time. One bot is one strategy, symbol, and timeframe, so a retry of that candle keeps the same key. |
| Historical walk-forward | NOT VALIDATED, BLOCKED | The repo has no candle files. A score was not invented and Binance was not queried for one. |

## How to select a v2 strategy

Restart the API so the seeder runs. The strategy list then includes each v2 display name, marked disabled. Turn that row on only for research. Create or start a bot separately, in Paper. Enabling the row does not set `LiveTradingEnabled` and does not send an order. Baseline names are unchanged.

`MaxImpulseAgeBars` is a research default of 32 closed bars. It is not a fitted value.

## Tests run

| Project | Result |
| --- | --- |
| `TradingPlatform.UnitTests` | 277 passed, 0 failed, 0 skipped |
| `TradingPlatform.BacktestingTests` | 52 passed, 0 failed, 0 skipped |
| `TradingPlatform.ResearchTests` | 130 passed, 0 failed, 0 skipped |
| `TradingPlatform.TradingTests` | 4 passed, 0 failed, 0 skipped |
| `TradingPlatform.IntegrationTests` | 4 passed, 0 failed, 0 skipped |
| `TradingPlatform.NewsTests` | Not re-run. This pass does not change the news projects. |

New fixtures live in `SecondPassStrategyTests` and the fee/tick cases in `BacktestReplayTests`.

## CI

`.github/workflows/ci.yml` builds the solution, runs `dotnet test`, and builds the frontend on push or pull request. `gh` is not installed here, and these edits are not pushed, so that workflow was not executed for this pass. Status: **BLOCKED** for a remote CI result. Local test projects above were executed.

## Files

- `src/TradingPlatform.Strategies/Engine/RefactoredStrategyEvaluator.cs`
- `src/TradingPlatform.Strategies/Engine/StrategyTemplates.cs`
- `src/TradingPlatform.Infrastructure/Persistence/DatabaseSeeder.cs`
- `src/TradingPlatform.Backtesting/BacktestService.cs`
- `src/TradingPlatform.Backtesting/BacktestReplay.cs`
- `tests/TradingPlatform.UnitTests/SecondPassStrategyTests.cs`
- `tests/TradingPlatform.UnitTests/RefactoredStrategyTests.cs`
- `tests/TradingPlatform.BacktestingTests/BacktestReplayTests.cs`
- `docs/strategy_refactor/STRATEGY_REFACTOR_REPORT.md`
- `docs/strategy_refactor/EXECUTION_CONSISTENCY.md`
- `docs/strategy_refactor/SECOND_PASS_AUDIT.md`
- `docs/strategy_refactor/SECOND_PASS_RESULTS.md`

## Still open

- No per-template entry fixture for every v2 id beyond Donchian, ZigZag, Impulse Catch, and Cluc. The other ids are covered for short history, an unclosed bar, and a repeated evaluation.
- No train, validation, or untouched OOS numbers. Every v2 book stays **NOT VALIDATED**.
- OHLC still cannot order a stop and a target inside one bar. The replay keeps the stop.
- Imported `BookStopsOff` reversal is unchanged on purpose.
- A symbol with no stored tick is not tick-rounded in the backtest.
- The running API process, if it was started before this seed change, will not show the new rows until it restarts and seeds again.
