# Order lifecycle and recovery

This describes the current bot cycle. It is not a claim that live trading is safe to enable.

## Gates

- `Trading:DefaultMode` is `PAPER`.
- `Trading:LiveTradingEnabled` defaults to false. `LiveEntryGate` blocks a new live entry and sets the bot error to that fact. A flatten of an open position is not blocked by this gate.
- `Trading:KillSwitchEnabled` stops running bots at the start of the cycle and does not evaluate entries.
- The cycle calls `LiveIsolatedReconciler` before it walks running bots.
- A second signal on the same closed candle uses `CandleIdempotency.Key` and is skipped when that client order id already exists.
- Quantity is floored to the symbol step. A size below the step is rejected. Risk sizing also sees min quantity and min notional.
- A live entry calls `PrepareSymbolRiskAsync` with isolated margin and the planned leverage before the order. That call does not run when the live-entry gate has already returned.
- The process does not treat a request as a fill. `PlaceAndFillAsync` waits for the connector result. A status other than `Filled` or `PartiallyFilled` is stored and thrown as `Live order was not filled`.

## What is still wrong

- A `PartiallyFilled` result is then recorded as `Filled` with `RemainingQuantity` 0. The remainder is not tracked. Do not enable live trading until that is fixed and tested.
- A timeout or an unknown status is stored as a failed or non-filled order and the exception is logged on the bot. There is no separate worker that queries the exchange by client order id and only then repairs the position. Restart recovery is the reconciler plus the client-order-id check, not a full uncertain-fill state machine.
- `RiskLiveGuard.EnsureAllowed` does not check the live flag or the risk profile. The entry gate above is the flag check. The empty guard is not a second check.
- If protective stop placement fails, the cycle logs it and does not pretend the stop exists. Confirm on the exchange before treating a live position as protected.
- Symbol tick size falls back when metadata is missing. A missing tick is not a silent zero fill in the backtest report (`TICK_SIZE_UNCONFIGURED`), but a live symbol with no filters should be treated as not tradable.

## Tests

Order tests use the paper connector or fakes. They do not call Binance.
