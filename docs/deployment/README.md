# Deployment

## Local Docker Compose

From the repository root:

```bash
cp .env.example .env   # or copy on Windows
docker compose up --build
```

Services:

| Service | Role | Port |
| --- | --- | --- |
| `postgres` | PostgreSQL 16 | 5432 |
| `redis` | Redis 7 | 6379 |
| `api` | ASP.NET Core API + SignalR | 5080 (internal) |
| `workers` | Background trading/market workers | none public |
| `web` | Angular production build via Nginx | 8080 |

Nginx terminates HTTP, serves the SPA, and proxies `/api` and `/hubs` to the API.

## Configuration

All secrets come from environment variables / `.env`. See `.env.example`.

Critical production settings:

- Strong `Jwt__SigningKey`
- Unique `Credentials__EncryptionKey` (32-byte base64)
- `Trading__LiveTradingEnabled=false` until intentionally enabled
- Restrict `Cors__AllowedOrigins`
- Do not put Binance secrets in the frontend image

## CI

GitHub Actions workflow (`.github/workflows/ci.yml`):

1. Checkout
2. Build backend
3. Run tests
4. Build frontend
5. Build Docker images

Production deploy jobs require `environment: production` approval.

## Health

- Liveness: `/health/live`
- Readiness: `/health/ready` (DB + Redis)
- Detail (auth/admin): `/api/system/health`

## Migrations

Development can apply EF migrations on API startup. Production should run:

```bash
dotnet ef database update --project src/TradingPlatform.Infrastructure --startup-project src/TradingPlatform.Api
```

or an init container using the same command.
