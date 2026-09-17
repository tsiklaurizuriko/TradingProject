# Architecture Decision Records

Decisions that materially affect the platform. Newest first within each ID sequence.

## ADR-001 — Modular monolith, not microservices

**Status:** Accepted

**Decision:** Ship a modular monolith with a separate worker host. Modules have independent projects and clear ports so they can be extracted later.

**Why:** Multiple bots, one exchange, one team, and strong consistency requirements (fills + positions + trades) are simpler in one process boundary with transactions. Microservices would add distributed-transaction and deployment cost before they are needed.

## ADR-002 — Decimal for all money

**Status:** Accepted

**Decision:** Use `decimal` (EF precision 28,8 for prices/qty; 28,8 for balances/PnL/fees) for every monetary and quantity field. Never `double` or `float`.

**Why:** Binary floating point cannot represent many decimal prices exactly. Trading PnL and order sizing must be deterministic.

## ADR-003 — UTC internally

**Status:** Accepted

**Decision:** Persist and compute in UTC. Store exchange-provided timestamps in dedicated fields when they differ from local receipt time.

## ADR-004 — Live trading off by default

**Status:** Accepted

**Decision:** `Trading:LiveTradingEnabled` defaults to `false`. A live bot cannot start unless the global flag, the exchange account, and the bot are all explicitly live-enabled. UI requires confirmation.

**Why:** Prevent accidental real-money orders during development, paper trading, and misconfiguration.

## ADR-005 — Exchange abstraction owns all venue I/O

**Status:** Accepted

**Decision:** `IExchangeConnector` is the only port used by execution and reconciliation. Binance types do not leak into Trading, Strategies, Risk, or the API.

**Why:** Future Bybit/OKX connectors must not require rewriting the bot engine.

## ADR-006 — First-party Binance HTTP/WebSocket clients

**Status:** Accepted

**Decision:** Implement `BinanceRestClient` and `BinanceWebSocketClient` against the official Binance REST and WebSocket APIs. Do not take a third-party Binance SDK as a core dependency.

**Why:** Full control over signing, rate limits, reconnect, idempotent order keys, and error mapping. Avoid coupling the engine to an unofficial SDK's order model.

## ADR-007 — One market-data gateway

**Status:** Accepted

**Decision:** A single market-data worker owns Binance public WebSocket subscriptions and fans events out in-process (and Redis pub/sub when multiple worker replicas exist).

**Why:** Per-bot sockets would hit connection limits, duplicate bandwidth, and complicate reconnect.

## ADR-008 — Strategies are data, not compiled bots

**Status:** Accepted

**Decision:** Strategies are JSON definitions evaluated by `IStrategyEngine`. Indicator math lives in `TradingPlatform.Strategies`. BotEngine only orchestrates.

**Why:** Users must change logic without recompiling. Historical trades must pin an immutable strategy version.

## ADR-009 — Strategy versions are immutable after use

**Status:** Accepted

**Decision:** Editing a used strategy creates a new version. Trades, signals, bots, and backtests reference `StrategyVersionId`.

## ADR-010 — Same pipeline for paper, testnet, and live

**Status:** Accepted

**Decision:** Paper trading is an `IExchangeConnector` implementation (`PaperExchangeConnector`) that consumes real public market data and simulates fills. Testnet/live use `BinanceExchangeConnector` with different base URLs and a live-guard.

**Why:** Three separate engines drift. Bugs would appear only in live.

## ADR-011 — Orders are not filled because HTTP succeeded

**Status:** Accepted

**Decision:** Order state is a state machine synchronized from user-data events and REST reconciliation. `SUBMITTED` ≠ `FILLED`.

## ADR-012 — ClientOrderId is the idempotency key

**Status:** Accepted

**Decision:** Every order gets a deterministic `ClientOrderId` before the first send. Timeouts retry by querying the exchange with that id rather than creating a new order.

## ADR-013 — Custom identity with JWT + refresh + TOTP

**Status:** Accepted

**Decision:** Domain `User`/`Role`/`Permission` tables with JWT access tokens, hashed refresh tokens, and TOTP 2FA. Not ASP.NET Identity.

**Why:** The permission model (Viewer/Trader/Admin plus live-trading gates) is domain-specific. Avoid fighting Identity's schema.

## ADR-014 — AES-256-GCM credential encryption

**Status:** Accepted

**Decision:** `IExchangeCredentialStore` encrypts API secret/passphrase with AES-256-GCM using `Credentials:EncryptionKey` (32-byte key, base64). Production can replace the store with a secret manager without changing callers.

Secrets are never logged, never put in exceptions, never returned in API DTOs.

## ADR-015 — Redis for locks, cache, and short-lived state

**Status:** Accepted

**Decision:** PostgreSQL owns durable trading state. Redis owns bot execution locks, market-data cache, rate-limit counters, kill-switch cache, and SignalR scale-out later.

**Why:** Two workers must not run the same bot. Locks must survive a single process.

## ADR-016 — EF Core + PostgreSQL migrations

**Status:** Accepted

**Decision:** EF Core 10 with Npgsql. Migrations live in `TradingPlatform.Infrastructure`. API/Workers apply migrations on startup in Development; production applies them as an explicit step.

## ADR-017 — SignalR for realtime UI

**Status:** Accepted

**Decision:** Hubs push market snapshots (throttled), bot status, orders, positions, trades, balances, notifications, and health. Angular subscribes per-view and unsubscribes on destroy.

Tick-level market data is not stored at INFO log level and is not broadcast at full firehose rate.

## ADR-018 — FluentValidation + standardized error envelope

**Status:** Accepted

**Decision:** All external commands are validated with FluentValidation. API errors use:

```json
{
  "code": "ORDER_REJECTED",
  "message": "Order quantity is below the minimum allowed quantity.",
  "traceId": "...",
  "details": {}
}
```

No stack traces to clients.

## ADR-019 — Charting: TradingView Lightweight Charts

**Status:** Accepted

**Decision:** Use `lightweight-charts` for candles, equity, and drawdown. It is designed for financial time series.

## ADR-020 — Test doubles at the connector boundary

**Status:** Accepted

**Decision:** Unit and trading-scenario tests use in-memory connectors. Integration tests may hit Binance Testnet only when credentials are provided via environment. Never fake that a paper fill was a Binance fill.

## ADR-021 — Docker Compose for local platform; Dockerfiles are production-ready

**Status:** Accepted

**Decision:** `docker-compose.yml` runs API, workers, UI, Postgres, Redis, and Nginx. Docker may be absent on a given developer machine; the solution must still build and test without it.

## ADR-022 — No brute-force optimizer in MVP

**Status:** Accepted

**Decision:** Expose `IStrategyOptimizer` as an extension point. Do not ship an unbounded parameter search that can overload the host.

## ADR-024 — .NET 10 solution format

**Status:** Accepted

**Decision:** Use `TradingPlatform.slnx` (the .NET 10 XML solution format) rather than the older `.sln`.

**Why:** `dotnet new sln` on SDK 10.0.203 emits `.slnx`. CI and Dockerfiles restore/build that file.

## ADR-025 — Built-in OpenAPI + Swagger UI

**Status:** Accepted (updated)

**Decision:** Use `Microsoft.AspNetCore.OpenApi` for the document (`/openapi/v1.json`) and Swashbuckle **SwaggerUI only** at `/swagger`. Visual Studio / `dotnet run` launches `/swagger` in Development.

**Why:** Swashbuckle 9 SwaggerGen is incompatible with Microsoft.OpenApi 2.x on .NET 10. SwaggerUI can consume the built-in OpenAPI document without SwaggerGen. Scalar remains available at `/scalar`.

## ADR-023 — Correlation IDs everywhere

**Status:** Accepted

**Decision:** HTTP middleware generates or forwards `X-Correlation-Id`. Workers generate one per bot cycle / order / reconciliation pass. Audit logs and Serilog enrich with it.
