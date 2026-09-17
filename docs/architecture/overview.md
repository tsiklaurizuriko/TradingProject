# Trading Platform Architecture Overview

## Goal

This platform is a **modular monolith** for cryptocurrency algorithmic trading. It connects to Binance (Spot for MVP), consumes real-time market and user-data streams, runs versioned configurable strategies, applies independent risk checks, executes orders, tracks positions and balances, and exposes a professional Angular dashboard.

It is **not** a single-purpose trading bot. Bots, strategies, symbols, accounts, and (later) exchanges are first-class, independently managed entities.

## Logical architecture

```
Angular (HTTPS + SignalR)
        |
ASP.NET Core API  ---- SignalR Hub
        |
Application Layer (use cases, DTOs, ports)
        |
+---------------+----------------+----------------+
|               |                |                |
Bot Engine   Strategy Engine  Risk Engine   Market Data Engine
|               |                |                |
+---------------+----------------+----------------+
                    |
              Execution Engine
                    |
              Order Manager
                    |
           Exchange Abstraction (IExchangeConnector)
                    |
           Binance Connector
              /           \
           REST         WebSocket
```

Supporting systems: PostgreSQL, Redis, background workers, Serilog, OpenTelemetry, audit, notifications, health checks.

## Solution modules

| Project | Responsibility |
| --- | --- |
| `TradingPlatform.Domain` | Entities, value objects, enums, domain events, domain errors. No infrastructure. |
| `TradingPlatform.Application` | Use cases, DTOs, validators, ports (repositories, `IExchangeConnector`, `INotificationService`). |
| `TradingPlatform.Infrastructure` | EF Core, PostgreSQL, Redis, encryption, auth persistence, email, credential store. |
| `TradingPlatform.Trading` | Bot lifecycle, trading pipeline orchestration, distributed bot locks. |
| `TradingPlatform.Strategies` | Configurable JSON strategy engine, condition evaluator, indicators. |
| `TradingPlatform.Risk` | Independent risk evaluation and risk-state tracking. |
| `TradingPlatform.Execution` | Order decisions, validation, paper simulation, order state machine. |
| `TradingPlatform.MarketData` | Shared market-data gateway, candle processing, in-memory cache, event bus. |
| `TradingPlatform.Backtesting` | Historical replay using the same strategy/indicator/risk engines. |
| `TradingPlatform.Binance` | REST, WebSocket, signing, rate limits, exchange filters. Implements the connector port. |
| `TradingPlatform.Workers` | Hosted background workers (market data, bots, reconciliation, cleanup). |
| `TradingPlatform.Api` | HTTP API, SignalR, auth, composition root. |

## Non-negotiable pipeline

```
Market Data → Strategy Engine → Signal → Risk Engine → Order Decision
    → Execution Engine → Order Manager → Exchange Connector → Exchange
```

Strategies never call Binance. Angular never calls Binance. Controllers never contain trading logic.

## Trading modes

| Mode | Market data | Orders |
| --- | --- | --- |
| `PAPER` (default) | Real Binance public market data | Simulated locally. Never sent to Binance. |
| `TESTNET` | Binance Spot Testnet | Real testnet orders. |
| `LIVE` | Binance production | Real orders. **Disabled globally by default.** |

All three modes use the same strategy, risk, and execution pipeline. Only the final exchange adapter differs.

## Safety defaults

- `TradingMode = PAPER`
- `Trading:LiveTradingEnabled = false`
- LIVE requires global flag + account enablement + bot enablement + confirmation
- Sensitive operations (enable LIVE) require TOTP 2FA when configured
- Global kill switch blocks new orders
- Emergency stop is a first-class bot and system operation
- Exchange secrets are encrypted at rest and never returned to the UI

## Runtime topology (MVP)

Two process types, not microservices:

1. **API** — REST, SignalR, authentication
2. **Workers** — market data, user data, bot engine, execution, reconciliation, notifications, cleanup

Both share the same modules and database. Docker Compose also runs PostgreSQL, Redis, Nginx, and the Angular UI.

## Data principles

- `decimal` for all prices, quantities, balances, fees, and PnL
- UTC for all internal timestamps; store exchange timestamps alongside
- Correlation IDs on all important operations
- PostgreSQL is the source of truth for trading state
- Redis is for cache, locks, short-lived state, rate limiting, and pub/sub
- One shared Binance market-data WebSocket fan-out, not one connection per bot
