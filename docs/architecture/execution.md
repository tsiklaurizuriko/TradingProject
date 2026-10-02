# Execution architecture

## Role

`TradingPlatform.Execution` turns an approved signal into orders and keeps order/position/trade state consistent.

## Flow

```
Approved signal
  → Order decision (side, type MARKET|LIMIT, sizing from risk)
  → Exchange filter validation (tick, step, minQty, minNotional)
  → Order manager assigns ClientOrderId
  → Connector PlaceOrder
  → State machine driven by user-data / paper fills / REST
```

HTTP 200 on place is **not** a fill. Status becomes `FILLED` only from execution reports or a reconciliation that observes a terminal fill.

## Order state machine

`NEW → SUBMITTING → SUBMITTED → PARTIALLY_FILLED → FILLED`  
Also: `CANCEL_REQUESTED`, `CANCELLED`, `REJECTED`, `EXPIRED`, `FAILED`.

Transitions are explicit. Duplicate exchange events are idempotent (processed event ids).

## Idempotency

On timeout after send:

1. Do not create a second `ClientOrderId`
2. Query `GetOrder` by `ClientOrderId`
3. If found, adopt exchange state
4. If not found after a bounded retry window, mark `FAILED` / `RECONCILIATION_REQUIRED` rather than blindly resending a new order

## Runtime connector

Live is the only runtime connector. `ExchangeConnectorFactory` rejects Paper and Testnet. Historical Paper rows stay in the database and are not executed. They must not be shown as Binance fills.

## Live guard

`LiveTradingGuard` rejects live `PlaceOrder` unless `Trading:LiveTradingEnabled` is true, the account is live-enabled, the bot is `LIVE`, 2FA policy is satisfied, and the kill switch is off.
