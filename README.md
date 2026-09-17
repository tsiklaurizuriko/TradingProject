# Cryptocurrency Algorithmic Trading Platform

Production-oriented **modular monolith** for Binance Spot algorithmic trading: configurable versioned strategies, independent risk, paper/testnet/live execution (live **off** by default), backtesting, and an Angular dashboard.

This is not a hardcoded trading bot. Strategies are data. The bot engine only orchestrates.

## Status

The platform is being built in the phases described in [docs/architecture/implementation-plan.md](docs/architecture/implementation-plan.md).

**Safety:** `TradingMode` defaults to `PAPER`. `Trading:LiveTradingEnabled` defaults to `false`. The system will not place real Binance orders unless live trading is explicitly enabled in configuration **and** on the account and bot.

Sample strategies and risk profiles are **examples**, not financial advice. No performance claims.

## Architecture

See:

- [Architecture overview](docs/architecture/overview.md)
- [Decisions](docs/architecture/decisions.md)
- [Trading engine](docs/architecture/trading-engine.md)
- [Strategy engine](docs/architecture/strategy-engine.md)
- [Execution](docs/architecture/execution.md)
- [Binance](docs/architecture/binance.md)
- [API](docs/api/README.md)
- [Strategies](docs/strategies/README.md)
- [Deployment](docs/deployment/README.md)

## Solution layout

```
src/        Backend modules (API, application, domain, engines, Binance, workers)
tests/      Unit, integration, trading scenario, and backtesting tests
frontend/   Angular 22 dashboard
deploy/     Dockerfiles and Nginx
docs/       Architecture and operations
```

## Prerequisites

- .NET 10 SDK
- Node.js 24+
- Docker + Docker Compose (optional for local UI/API; required for the full stack)
- PostgreSQL 16 and Redis 7 if not using Compose

## Quick start (without Docker)

1. Copy environment template and edit locally (never commit `.env`):

   ```bash
   copy .env.example .env
   ```

2. PostgreSQL database `TradingProject` (user `admin` / password `admin`) and Redis if you use cache/locks.

3. Restore and build:

   ```bash
   dotnet restore TradingPlatform.slnx
   dotnet build TradingPlatform.slnx
   dotnet test TradingPlatform.slnx
   ```

4. Run API and workers (two terminals):

   ```bash
   dotnet run --project src/TradingPlatform.Api
   dotnet run --project src/TradingPlatform.Workers
   ```

5. Run Angular:

   ```bash
   cd frontend/trading-platform-ui
   npm install
   npm start
   ```

API: `http://localhost:5080`  
Swagger (dev): `http://localhost:5080/swagger`  
UI: `http://localhost:4200`  
OpenAPI: `http://localhost:5080/scalar` (`/openapi/v1.json`)

Default seeded admin after Phase 2: see `.env.example` (`ADMIN_EMAIL` / `ADMIN_PASSWORD`). Change immediately.

## Quick start (Docker)

```bash
docker compose up --build
```

Nginx serves the UI and proxies `/api` and `/hubs` to the API.

## Trading modes

| Mode | Behavior |
| --- | --- |
| Paper | Real public market data, simulated execution, no Binance orders |
| Testnet | Binance Spot Testnet |
| Live | Production Binance — globally disabled until you opt in |

## License

Private / unpublished unless otherwise specified.
