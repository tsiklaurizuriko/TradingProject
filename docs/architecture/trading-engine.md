# Trading engine

## Role

`TradingPlatform.Trading` orchestrates bot lifecycle. It does **not** contain entry/exit math.

A bot binds:

- Exchange account
- Symbol(s) and timeframe(s)
- Strategy version (immutable)
- Risk profile
- Execution profile (fees/slippage assumptions, order types)
- Trading mode (`PAPER` | `TESTNET` | `LIVE`)

## Lifecycle

`CREATED → STARTING → RUNNING → STOPPING → STOPPED` with `PAUSED` and `ERROR`.

Commands: Start, Stop, Pause, Resume, Emergency Stop.

Emergency stop:

1. Acquires the bot's Redis lock
2. Sets status to `STOPPING`
3. Prevents new order decisions
4. Optionally cancels open orders (configurable)
5. Writes an audit event and notification
6. Reaches `STOPPED`

## Pipeline (mandatory)

```
Market Data Engine
    → Strategy Engine (signal)
    → Risk Engine (approve / reject)
    → Execution Engine (order decision + validation)
    → Order Manager (state machine, idempotency)
    → IExchangeConnector (paper | testnet | live)
```

Bots never skip risk. Strategies never place orders.

## Concurrency

`BotEngineWorker` processes only bots for which it holds `bot:{id}:run` in Redis (expiry with heartbeat). A crash releases the lock via expiry; another worker may recover.

Critical fill handling (execution + position + trade + balance) runs in a PostgreSQL transaction with optimistic concurrency on the order/position rows.

## Correlation

Each bot cycle, order attempt, and reconciliation pass has a correlation id propagated into logs, audit, and SignalR payloads.
