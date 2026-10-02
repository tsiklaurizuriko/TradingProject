# Live readiness

Status: **not ready for real-money trading.** `Trading:LiveTradingEnabled` is false. No exchange order was sent while this change was made or tested.

## What this build does

- One evaluator per canonical strategy id. An obsolete `*_v2` id does not run the retired code.
- Startup migrates those ids, preserves parameter numbers, reports incompatible fields, and stops if two enabled rows share an id or a definition cannot be parsed.
- New live entries are blocked until the operator sets the flag. Open-position exits are not blocked by that flag.
- The isolated-book reconciler runs before entries. The kill switch stops running bots. Client order ids are per bot, candle, and mode.

## What this build now enforces

- A partial fill is stored as `PartiallyFilled` with the executed quantity and the remainder. `Filled` requires the exchange quantity to equal the request. A repeated report does not book the same delta twice.
- A lost response is queried by the client order id. The order is not sent again. An unresolved order blocks a new entry on that coin until lookup succeeds.
- A stale or incomplete futures snapshot blocks new live entries. Unknown exchange positions and orders are logged and are not closed. A missing local position is not represented by a fabricated `Filled` order.
- `RiskLiveGuard` rejects a live order when a required limit or input is missing or outside the profile. It does not invent a default.
- Obsolete alias ids do not run the retired private methods. Those unused methods were removed. Migration stays idempotent and fails startup when it cannot finish safely.

## Remaining blockers

1. No test placed or observed a real Binance order. Stop and take-profit fills are applied only when the REST order or algo query returns an executed quantity and price. There is no separate user-data socket consumer.
2. A local position missing from a fresh snapshot is left open and blocks new entries. It is not closed from a mark price.
3. Daily realized loss is enforced. A peak-to-trough equity drawdown series is not stored. If a caller cannot say whether drawdown is known, the risk check fails closed.
4. Every canonical strategy stays `NOT VALIDATED`. Backtests that do run still assume the stop wins when stop and target are both inside one bar, and they do not model partial exits.
5. A stored timeframe that is not the canonical default is left as-is and only logged.
6. PostgreSQL Testcontainers tests were not executed. Docker is not installed on this machine, and the integration project does not start a PostgreSQL container. Migration behavior was tested in memory.
7. There is no Dockerfile in this repository, so no image was built.
8. GitHub Actions was not run. These changes are local and uncommitted. Do not treat the local test run as a CI result.

## Tests run on this machine

No test uses a live Binance order connector. `dotnet restore` completed. `dotnet build TradingPlatform.slnx -c Release` succeeded with 0 errors and 10 warnings.

| Command | Result |
| --- | --- |
| `dotnet test TradingPlatform.slnx -c Release` | UnitTests 317 passed, 0 failed. ResearchTests 130 passed, 0 failed. BacktestingTests 52 passed, 0 failed. TradingTests 4 passed, 0 failed. IntegrationTests 4 passed, 0 failed. |
| `dotnet test tests/TradingPlatform.NewsTests/TradingPlatform.NewsTests.csproj -c Release` | 45 passed, 0 failed. This project is not in the solution test pass above. |
| `npm run build` in `frontend/trading-platform-ui` | Succeeded. One budget warning: `strategies.page.scss` is 5.19 kB against a 4.00 kB budget. |
| PostgreSQL Testcontainers | Not executed. Docker is not installed. |
| Docker image build | Not executed. No Dockerfile. |

The bots page was not clicked through in a browser in this pass.

## Operator catalog

The operator catalog count is 27. `donchian_breakout` was added so the 4h Donchian book is selectable. A Development seed can enable that row. It does not start a bot and it does not enable live orders.
