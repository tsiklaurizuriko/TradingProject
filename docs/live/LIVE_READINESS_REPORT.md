# Live readiness

Status: **not ready for real-money trading.** `Trading:LiveTradingEnabled` is false. No exchange order was sent while this change was made or tested.

## What this build does

- One evaluator per canonical strategy id. An obsolete `*_v2` id does not run the retired code.
- Startup migrates those ids, preserves parameter numbers, reports incompatible fields, and stops if two enabled rows share an id or a definition cannot be parsed.
- New live entries are blocked until the operator sets the flag. Open-position exits are not blocked by that flag.
- The isolated-book reconciler runs before entries. The kill switch stops running bots. Client order ids are per bot, candle, and mode.

## Blockers before a controlled live test

1. Partial fills are stored as full fills (`RemainingQuantity` set to 0). That can overstate the position.
2. A timeout or an uncertain status is not reconciled by a dedicated client-order query before the next entry decision. The candle idempotency key only stops a second submit of the same id.
3. `RiskLiveGuard.EnsureAllowed` is empty.
4. No out-of-sample candle archive was run. Every canonical strategy stays `NOT VALIDATED`. Backtests that do run still assume the stop wins when stop and target are both inside one bar, and they do not model partial exits.
5. A stored timeframe that is not the canonical default is left as-is and only logged. A 5m Cluc book evaluates the canonical rules on 5m bars. Review those warnings before starting the bot.
6. Protective orders are only as good as the exchange acknowledgement. A failed protect call must be treated as unprotected.
7. GitHub Actions was not run. These commits are local.

## Tests run on this machine

Commands used `--artifacts-path` so they did not write into a locked API output directory. No test uses a live Binance order connector.

| Project | Result |
| --- | --- |
| `TradingPlatform.UnitTests` | Passed 284, Failed 0, Skipped 0 |
| `TradingPlatform.ResearchTests` | Passed 130, Failed 0, Skipped 0 |
| `TradingPlatform.BacktestingTests` | Passed 52, Failed 0, Skipped 0 |
| `TradingPlatform.TradingTests` | Passed 4, Failed 0, Skipped 0 |
| `TradingPlatform.IntegrationTests` | Passed 4, Failed 0, Skipped 0 |
| `TradingPlatform.NewsTests` | Not run. This change does not touch the news collector. |

```text
dotnet test tests/TradingPlatform.UnitTests/TradingPlatform.UnitTests.csproj --artifacts-path artifacts/canonical-test --nologo
dotnet test tests/TradingPlatform.ResearchTests/TradingPlatform.ResearchTests.csproj --artifacts-path artifacts/canonical-research --nologo
dotnet test tests/TradingPlatform.BacktestingTests/TradingPlatform.BacktestingTests.csproj --artifacts-path artifacts/canonical-backtest --nologo
dotnet test tests/TradingPlatform.TradingTests/TradingPlatform.TradingTests.csproj --artifacts-path artifacts/canonical-trading --nologo
dotnet test tests/TradingPlatform.IntegrationTests/TradingPlatform.IntegrationTests.csproj --artifacts-path artifacts/canonical-integration --nologo
```

The bots page change (it no longer sorts `_v2` keys first) was not clicked through in a browser in this pass.

## Operator catalog

The operator catalog count is 27. `donchian_breakout` was added so the 4h Donchian book is selectable. A Development seed can enable that row. It does not start a bot and it does not enable live orders.
