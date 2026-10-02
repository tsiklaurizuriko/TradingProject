# Order lifecycle and recovery

This describes the current bot cycle. It is not a claim that live trading is safe to enable. Tests use fakes. No Binance order was sent.

## Gates

New entries go through `LiveEntryGate.Block`. Paper and testnet are rejected. They are not mapped to live. An exit of an already open live position is not blocked by the entry flag.

The gate refuses a new live entry unless all of these are true:

- Mode is Live.
- `Trading:LiveTradingEnabled` is true. The default in `src/TradingPlatform.Api/appsettings.json` and `src/TradingPlatform.Workers/appsettings.json` is false. Startup does not turn it on.
- `Trading:KillSwitchEnabled` is false.
- The last exchange reconciliation succeeded, names an account, and is newer than `Trading:ReconciliationMaxAgeSeconds` (default 90). The futures book must also be fresh.
- The account or coin is not blocked by a reconciliation exception or by an unresolved market order (`Uncertain`, `PartiallyFilled`, or `Submitting`).
- The strategy row is enabled.
- `RiskLiveGuard.Reject` accepts the order.
- Symbol filters have a positive tick, step, minimum quantity, and minimum notional.

`NewsTradeAdapter.SubmitAsync` uses the same gate and the global flag. A running news session cannot set `liveTradingEnabled: true` on its own. Cross-sectional reversal does not call `PlaceOrder`. There is no manual order endpoint that calls the exchange.

## Fills

`PlaceAndFillAsync` writes the order, including the client order id, before it calls the exchange. `FillAccounting.Apply` then stores the exchange status, cumulative executed quantity, remaining quantity, and cumulative average price.

The price booked on the new quantity is not the cumulative average. It is the quote delta divided by the quantity delta:

`newCumulativeQty * newCumulativeAvg - oldCumulativeQty * oldCumulativeAvg`, divided by the new quantity.

If the report also has cumulative quote value, that difference is used, and it must agree with the cumulative average. A backwards quantity, a non-positive quote delta, a missing price, or a disagreement is `Uncertain`. No fill price is invented.

`Filled` is stored only when the executed quantity equals the requested quantity and is greater than zero. A report labeled filled with a smaller quantity is stored as `PartiallyFilled`. `NEW`, `PARTIALLY_FILLED`, `FILLED`, `CANCELED`, `REJECTED`, `EXPIRED`, and an unknown status are mapped explicitly. An unknown status becomes `Uncertain`.

A position, execution, and realized PnL change only for the new executed delta. The same cumulative quantity applied again does not move the position. A partial entry books only the executed quantity and blocks another entry on that coin. A cancel after that partial keeps the executed quantity and leaves the remainder unfilled. A partial exit reduces the open quantity, keeps the original average entry on the remainder, and adds realized PnL for that slice only. The same `PositionFillBook.Exit` math is used for a normal exit and a recovered exit. If more than one open position matches the coin, the fill is not applied.

## Fees

On the Binance connector, `ExchangeOrder.Fee` is the cumulative commission for that order. The asset is read from `commissionAsset` on the order payload or from `GET /fapi/v1/userTrades`. It is not a per-fill delta and it is not assumed to be USDT. A missing amount is `Unknown`. An amount with no asset is `AssetMissing`. A known zero is `Known` with amount 0 and one asset. Mixed assets, or a known amount together with an unknown amount, are `Uncertain`. Those four states are not added into one number, and none of the non-known states is shown as `$0`.

Closed-trip aggregation uses the same rule. `Trades.FeeStatus` and `Executions.FeeStatus` default to `0` (`Unknown`). The migration does not rewrite stored fee amounts and does not mark old rows known. A row that already had `Fees = 0` stays unknown until a new report certifies the amount and the asset.

`FillAccounting` subtracts the fee already booked in the same asset and applies only the positive delta. A cumulative fee that moves backwards, or an asset that changes, is `Uncertain`. Replaying the same cumulative fee books nothing. A fill whose commission amount is missing still books the executed quantity, leaves the order `Uncertain`, and keeps blocking a new entry on that coin until a later report certifies the fee. Booking ids use a `local-fill:` or `local-fee:` prefix so they are not exchange trade ids.

## Recovery

`GetOrder` returns `OrderLookup`. `ConfirmedAbsent` is used only when Binance says the id does not exist (`-2013` or `-2011`) on both the order endpoint and the algo endpoint. A timeout, 429, 5xx, empty body, or other error is `Unavailable`. That keeps the order `Uncertain`, keeps the entry block, and does not send the order again. An authoritative absence marks the order `Failed` only when nothing has been filled. A partial fill that then disappears stays `Uncertain` and keeps the booked quantity.

Each cycle, including the first after a process restart, polls live orders in `Submitting`, `Uncertain`, or `PartiallyFilled`, including stop and take-profit orders. The same `RecoverUnresolvedOrdersAsync` path runs at startup and on every cycle. The wait is based on the order's last update: 5 seconds while it is under 1 minute old, 15 seconds while it is under 5 minutes, 60 seconds while it is under 30 minutes, then 5 minutes. Nothing is resubmitted. A confirmed report goes through the same `FillAccounting` and exit booking as a normal fill. A partial protective fill is applied once for the new executed quantity; the same cumulative quantity and cumulative commission replay books nothing. Polling stops once the status is terminal. A partial order, or a fill whose commission is still unknown, keeps blocking a new entry on that coin until then. If the exchange lookup is unavailable, the order stays `Uncertain`, the entry stays blocked, and the discrepancy remains visible on the order and on health.

## Protective orders

Stop and take-profit orders are queried on the algo endpoint as well as the order endpoint. A confirmed executed quantity is booked with the same fill and exit accounting. A trigger status with no executed quantity or price stays unresolved. Reaching a local trigger price does not mark the order filled.

## Reconciliation

`LiveIsolatedReconciler` runs before entries.

- A missing, stale, or incomplete futures snapshot calls `ReconciliationState.Fail` and does not close local positions. Absence in an incomplete snapshot is not treated as flat.
- A fresh snapshot with an exchange position or open order that has no local row is logged as a reconciliation exception. Those positions are not closed and no fill is invented. New entries stay blocked.
- A local position that is absent from a fresh book is closed in the database. Its quantity becomes zero and the open trade is marked closed. No order is inserted, and no mark price is stored as realized PnL. A stale or incomplete snapshot does not close it.
- A clean fresh snapshot calls `Succeed` with the account hint and the clock time.

`GET /api/system/health` reports `applicationStarted`, `databaseReady`, `exchangeReady`, `reconciliationReady`, `riskConfigurationValid`, `liveEntryGateOpen`, `unresolvedOrderCount`, and `blockedReason`. The process being up does not set `liveEntryGateOpen`. With the default flag the gate is closed.

## Risk

`RiskLiveGuard.EnsureAllowed` rejects a start when the mode is not live, when the profile is missing, when `AllowLive` is false, or when risk-per-trade, stop, leverage, daily loss, exposure, or position count is missing or out of range. It does not fill those in with a silent substitute.

`RiskLiveGuard.Reject`, used before a live submit, also checks the kill switch, equity, available margin, daily realized loss, open exposure, position counts, leverage, stop distance, minimum quantity, minimum notional, required margin, and the loss cooldown. A missing equity, missing margin, or unknown drawdown flag fails closed and names the missing input. The bot cycle enforces daily realized loss. It does not have a peak-equity drawdown series, so that separate metric is not calculated.

## What is still limited

This build is not fully live-ready. There is no user-data socket. Stop and take-profit recovery is REST polling only, on the schedule above. A fill is booked only when that query returns an executed quantity and a usable price. A local price cross does not create a fill, a synthetic execution, or realized PnL.

- No test talked to Binance. Recovery of a stop or take-profit depends on the REST order or algo query returning an executed quantity and price. A user-data stream event is not ingested on its own socket in this build; the same accounting runs when that query returns the fill.
- A local position missing from a fresh snapshot stays open and blocks new entries. It is not auto-closed.
- There is no unique database index on the strategy template key. Startup throws if two enabled rows share one canonical id. Adding the index in a migration that runs before the data rewrite would fail on existing duplicates.
