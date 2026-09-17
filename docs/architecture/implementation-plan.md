# Implementation Plan

Work proceeds in the ten phases defined by the product brief. A phase is done only when the relevant code compiles, tests pass, and there are no fake implementations for in-scope features.

## Current environment

| Tool | Status |
| --- | --- |
| .NET SDK | 10.0.203 |
| Node.js | 24.15.0 |
| Angular CLI | 22.1.8 |
| Docker | Not on PATH on the initial development machine; Compose files are still produced |

## Current environment

- Solution + all projects + central package management
- Architecture documentation
- Docker Compose, Dockerfiles, Nginx, `.env.example`
- Angular 22 application shell with routing
- API boots with health endpoint and OpenAPI
- Worker host boots
- Unit test project has a smoke test
- Backend and frontend build

## Phase 2 — Domain, persistence, authentication

- All domain entities and enums
- EF Core model, indexes, decimal precision, migrations
- Seed: roles, permissions, Conservative risk profile, sample EMA+RSI strategy, timeframes, BTCUSDT
- Application services + FluentValidation
- JWT register/login/refresh/logout, RBAC, password reset token architecture, TOTP fields
- Audit log writer

## Phase 3 — Exchange + market data

- `IExchangeConnector` and related market/user ports
- Binance REST (signing, recvWindow, timeouts, rate limiter, retries)
- Binance WebSocket (heartbeat, reconnect, connection state)
- Symbol filter cache
- Market data engine, candle processor, in-memory cache, event bus
- User-data listen-key lifecycle
- Workers: `MarketDataWorker`, `UserDataWorker`, `CandleWorker`

## Phase 4 — Indicators + strategy engine

- `IIndicator` implementations: SMA, EMA, WMA, RSI, MACD, Bollinger, ATR, ADX, Stochastic, VWAP, Volume, Average Volume
- JSON strategy schema + validator
- Nested AND/OR/NOT condition evaluator
- Crosses-above/below without look-ahead
- Strategy CRUD + immutable versions
- Sample EMA RSI strategy seed

## Phase 5 — Risk, execution, paper trading

- `IRiskEngine` with the Conservative profile rules
- Order state machine
- `IExecutionEngine` + order manager
- Filter validation (tick, step, minQty, minNotional)
- Paper connector: real market data, simulated fees/slippage, virtual balances/positions
- Idempotent `ClientOrderId` + timeout query-before-resend
- `BotEngineWorker`, `OrderExecutionWorker`

## Phase 6 — Testnet, reconciliation, live guardrails

- Testnet base URLs via configuration
- Live mode architecture with default OFF
- Kill switch
- `OrderReconciliationWorker`, `AccountReconciliationWorker`
- Emergency stop semantics

## Phase 7 — Angular product UI

- Auth, layout, route guards
- Dashboard, Bots, Strategies, Strategy Builder, Orders, Positions, Trades, Portfolio, Exchanges, Notifications, Settings, Admin
- SignalR subscriptions with teardown
- LIVE badges and confirmation dialogs
- Strategy builder generates JSON; advanced editor available

## Phase 8 — Backtesting

- Replay engine reusing strategy/indicator/risk
- No look-ahead; fills modeled at conservative prices
- Equity/drawdown stats stored
- Angular backtest page + charts
- `IStrategyOptimizer` interface only

## Phase 9 — Notifications, audit completeness, observability

- In-app + email notification channels (Telegram reserved)
- Serilog enrichers
- Health/readiness/liveness
- OpenTelemetry traces + Prometheus-compatible metrics endpoint

## Phase 10 — Tests, hardening, docs

- Indicator, strategy, risk, order, position, PnL, backtest unit tests
- Duplicate-order, reconnect, risk-limit scenario tests
- Integration tests with Testcontainers when Docker is available; otherwise in-memory/fakes
- CI workflow
- End-to-end acceptance path documented

## Engineering constraints while implementing

1. Inspect, then implement; do not dump an unbuildable tree.
2. After each phase: build backend, build frontend, run tests, fix, update docs.
3. Prefer correctness, safety, maintainability, testability, extensibility over speed.
4. Do not stub in-scope behavior with `NotImplementedException` or `return true`.
5. Never commit secrets. Never log secrets. Never send live orders unless explicitly enabled.
