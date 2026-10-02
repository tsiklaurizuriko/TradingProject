# Order lifecycle and recovery

This describes the current bot cycle. It is not a claim that live trading is safe to enable. Tests use fakes. No Binance order was sent.

## Gates

New live entries go through `LiveEntryGate.Block`. Paper mode is not blocked. An exit or other exposure-reducing action is not blocked by the gate.

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

`PlaceAndFillAsync` writes the order, including the client order id, before it calls the exchange. `FillAccounting.Apply` then stores the exchange status, cumulative executed quantity, remaining quantity, and average price.

`Filled` is stored only when the executed quantity equals the requested quantity and is greater than zero. A report labeled filled with a smaller quantity is stored as `PartiallyFilled`. `NEW`, `PARTIALLY_FILLED`, `FILLED`, `CANCELED`, `REJECTED`, `EXPIRED`, and an unknown status are mapped explicitly. An unknown status becomes `Uncertain`.

A position, execution, fee, and realized PnL change only for the new executed delta. The same cumulative quantity applied again does not move the position. A partial entry books only the executed quantity and blocks another entry on that coin. A cancel after that partial keeps the executed quantity and leaves the remainder unfilled. A partial exit reduces the open quantity and leaves the rest open.

If the executed quantity moves backwards, exceeds the request, or a new fill has no price, the order becomes `Uncertain` and new entries on that coin stay blocked. Nothing is invented to make the books match.

## Recovery

A timeout or lost response is not a rejection and is not a reason to send the order again. The cycle calls `GetOrder` with the client order id.

- Lookup throws: the order stays `Uncertain`. The reason and correlation id are logged. It is not sent again.
- Lookup returns nothing: the order is `Failed` with the reason that the exchange has no such client id. It is not sent again.
- Lookup returns the order: `FillAccounting` applies that status and only the new fill delta.

On each cycle, including the first cycle after startup, `RecoverUnresolvedOrdersAsync` repeats that lookup for live market orders still in `Uncertain` or `Submitting`. A second pass with the same cumulative quantity does not book another fill. The kill switch still stops bots, but this recovery runs first so a fill that already happened is not dropped.

## Reconciliation

`LiveIsolatedReconciler` runs before entries.

- A missing, stale, or incomplete futures snapshot calls `ReconciliationState.Fail` and does not close local positions. Absence in an incomplete snapshot is not treated as flat.
- A fresh snapshot with an exchange position or open order that has no local row is logged as a reconciliation exception. Those positions are not closed and no fill is invented. New entries stay blocked.
- A local position that is absent from a fresh book, and that has a positive mark or entry price, is closed locally with a `RECONCILE_FLAT` event. No order is inserted, and the status is not set to `Filled`. The PnL on that path uses the mark or last price, not an exchange fill. If that price is missing, the position stays open and entries stay blocked.
- A clean fresh snapshot calls `Succeed` with the account hint and the clock time.

`GET /api/system/health` reports `applicationStarted`, `databaseReady`, `exchangeReady`, `reconciliationReady`, `riskConfigurationValid`, `liveEntryGateOpen`, and `blockedReason`. The process being up does not set `liveEntryGateOpen`. With the default flag the gate is closed.

## Risk

`RiskLiveGuard.EnsureAllowed` rejects a live start when the profile is missing, `AllowLive` is false, or risk-per-trade, stop, leverage, daily loss, exposure, or position count is missing or out of range. It does not fill those in with a silent substitute. Paper mode is not rejected here.

`RiskLiveGuard.Reject`, used before a live submit, also checks the kill switch, equity, available margin, daily realized loss, open exposure, position counts, leverage, stop distance, minimum quantity, minimum notional, required margin, and the loss cooldown. A missing equity, missing margin, or unknown drawdown flag fails closed and names the missing input. The bot cycle enforces daily realized loss. It does not have a peak-equity drawdown series, so that separate metric is not calculated.

## What is still limited

- No test talked to Binance. A user-data stream fill of a resting stop or take-profit is not passed through `FillAccounting`. A newly placed protective order is stored as working, with remaining quantity equal to the requested quantity, until a later cancel or fill path updates it.
- A ghost close uses the last mark. That is not an exchange fill.
- The fee stored on a later delta is the fee on that exchange report. It is not differenced against an earlier partial fee.
- There is no unique database index on the strategy template key. Startup throws if two enabled rows share one canonical id. Adding the index in a migration that runs before the data rewrite would fail on existing duplicates.
