# Startup and deployment

Live trading stays off unless an operator sets `Trading:LiveTradingEnabled` to `true`. Startup does not do that. No strategy is started automatically. Bots that were running are stopped and must be started again from the Bots page.

## What startup does

1. Apply EF migrations (`Database.MigrateAsync`) in every environment, not only Development.
2. Run the canonical strategy data migration. It rewrites obsolete template ids, disables a duplicate alias row, and leaves parameter numbers in place.
3. In Development, seed the catalog, then run the canonical migration again so a seed cannot leave two enabled rows for one id.
4. Refuse to continue if the plan has a failure, if an enabled row still has an obsolete id, or if two enabled rows share one canonical id.
5. Log the assembly informational version and the canonical id list.
6. Log a warning for every review item (timeframe outside the canonical default, unknown parameter, disabled duplicate).
7. Stop every running bot.

If PostgreSQL is down, or the migration cannot parse a definition, startup throws and the process stops. It does not keep serving with the previous catalog.

The bot cycle, when an operator later starts a bot, reconciles the isolated futures book before it evaluates entries. New live entries are refused while `Trading:LiveTradingEnabled` is false. An exit of a position that is already open can still send an order.

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
