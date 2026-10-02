# Startup and deployment

Live trading stays off unless an operator sets `Trading:LiveTradingEnabled` to `true`. Startup does not do that. No strategy is started automatically. Bots that were running are stopped and must be started again from the Bots page.

## What startup does

1. Load configuration. `Trading:DefaultMode` is `PAPER`. `Trading:LiveTradingEnabled` is false. `Trading:ReconciliationMaxAgeSeconds` defaults to 90 when it is omitted.
2. Apply EF migrations (`Database.MigrateAsync`) in every environment, not only Development.
3. Run the canonical strategy data migration inside a transaction on a relational database. It rewrites obsolete template ids, disables a duplicate alias row, and leaves parameter numbers in place. Incompatible parameters and timeframe mismatches are warnings, not silent rewrites.
4. In Development, seed the catalog, then run the canonical migration again so a seed cannot leave two enabled rows for one id.
5. Refuse to continue if the plan has a failure, if an enabled row still has an obsolete id, or if two enabled rows share one canonical id. The exception is logged and rethrown. Secrets are not written in that log.
6. Log the assembly informational version and the canonical id list.
7. Stop every running bot. Startup does not submit an order.

The bot cycle starts only after that. Each cycle refreshes the futures book, reconciles, and recovers unresolved client order ids before it evaluates entries. A kill switch stops bots after that recovery, so a fill that already exists can be booked and a new entry is not sent. New live entries stay refused while the flag is false, reconciliation is stale, or risk checks fail. An exit of a position that is already open can still send an order.

`GET /api/system/health` separates a running process from database health, exchange connectivity, reconciliation freshness, risk configuration, and whether the live-entry gate is open. The default response has the gate closed.

## Deploy

1. Stop the API and the worker so they are not writing strategy rows during the migration.
2. Take a database backup. The migration updates `Strategies.TemplateKey`, `IsEnabled`, `IsArchived`, and the `template` string inside `StrategyVersions.DefinitionJson`. It does not delete bots, orders, fills, positions, or backtest rows.
3. Deploy the build. Leave `Trading:LiveTradingEnabled` false.
4. Start the API. Read the log for `Build` and `Canonical strategies`.
5. If startup throws `Canonical strategy migration cannot finish safely`, do not turn live on. The message names the strategy ids. Disable one of the two enabled rows that share an id, or fix the definition JSON that could not be parsed, then start again. The failed plan is not applied.
6. Read every `Canonical strategy review` warning. A 5m book on a strategy whose default is 15m is left on 5m. The numbers are not rewritten. Decide whether that book should stay before you start it.
7. Start one paper bot and confirm the template id in the definition has no `_v2` suffix.
8. Only after that, and only with an explicit decision, set `Trading:LiveTradingEnabled` to true and restart. That flag is the gate for new live entries. This repository does not set it.

## Rollback

Restore the database backup and deploy the previous build together. Reversing only the binary leaves rewritten template ids that the old build does not understand. Reversing only the database leaves the new binary, which will migrate the ids again on the next start.

Do not hand-edit historical orders to the old ids. Those rows are keyed by strategy and version guids, which the migration does not change.
