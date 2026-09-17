# HTTP API

OpenAPI (dev): `http://localhost:5080/swagger` (also `/scalar` and `/openapi/v1.json`)

All `/api/*` routes except auth register/login/refresh require a Bearer access token.

Standard error envelope:

```json
{
  "code": "ORDER_REJECTED",
  "message": "Order quantity is below the minimum allowed quantity.",
  "traceId": "00-...",
  "details": {}
}
```

Correlation: send `X-Correlation-Id` or the API generates one.

## Auth

| Method | Path | Notes |
| --- | --- | --- |
| POST | `/api/auth/register` | Creates Trader by default |
| POST | `/api/auth/login` | Returns access + refresh; may require TOTP |
| POST | `/api/auth/refresh` | Rotates refresh token |
| POST | `/api/auth/logout` | Revokes refresh token |
| POST | `/api/auth/2fa/enable` | TOTP setup |
| POST | `/api/auth/2fa/verify` | Confirms TOTP |

## Bots

`GET/POST /api/bots`  
`GET/PUT /api/bots/{id}`  
`POST /api/bots/{id}/start|stop|pause|resume|emergency-stop`

## Strategies

`GET/POST /api/strategies`  
`GET /api/strategies/{id}`  
`GET/POST /api/strategies/{id}/versions`

## Trading data

`GET /api/orders`, `GET /api/orders/{id}`  
`GET /api/positions`  
`GET /api/trades`  
`GET /api/portfolio`, `GET /api/balances`  
`GET /api/notifications`

## Backtests

`POST /api/backtests`  
`GET /api/backtests`  
`GET /api/backtests/{id}`

## Exchanges

`GET /api/exchanges`  
`POST /api/exchanges/binance` — secrets accepted once, never returned  
`DELETE /api/exchanges/{id}`

## System

`GET /api/system/health`  
`GET /health/live`  
`GET /health/ready`

## SignalR

Hub: `/hubs/trading`

Groups: market, bot:{id}, orders, positions, portfolio, notifications, health.

DTOs only. EF entities are never serialized from controllers.
