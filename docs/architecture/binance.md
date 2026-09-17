# Binance integration

## Boundary

Only `TradingPlatform.Binance` talks to Binance. Everyone else depends on `IExchangeConnector` and market-data ports.

## Clients

| Component | Responsibility |
| --- | --- |
| `BinanceRestClient` | Signed and unsigned REST, timestamp, recvWindow, HMAC SHA256 |
| `BinanceWebSocketClient` | Combined streams, ping/pong, exponential backoff reconnect |
| `BinanceMarketDataClient` | Ticker, trades, klines, depth as required |
| `BinanceUserDataClient` | Listen key create/keepalive, execution reports, outbound accounts |
| `BinanceExchangeConnector` | Maps domain orders to REST; applies symbol filters before send |
| `BinanceRateLimiter` | Respects exchange weight and order-count headers |

## Environments

| Config | REST | WebSocket |
| --- | --- | --- |
| Production | `https://api.binance.com` | `wss://stream.binance.com:9443` |
| Spot Testnet | `https://testnet.binance.vision` | `wss://stream.testnet.binance.vision` |

Paper mode uses **public** production (or configured) market streams only.

## Filters

Before any live/testnet order:

- Price tick size
- Quantity step size
- Min quantity
- Min notional
- Other applicable `exchangeInfo` filters (cached)

Invalid orders are rejected locally with `INVALID_PRICE` / `INVALID_QUANTITY` / `ORDER_REJECTED`.

## Rate limits and resilience

- Read `X-MBX-USED-WEIGHT` and back off
- Retry idempotent GETs with exponential backoff
- POST order is **not** blindly retried; see execution idempotency
- Request timeout is configured (`Binance:RequestTimeoutSeconds`)
- Structured errors map to `EXCHANGE_UNAVAILABLE`, `EXCHANGE_RATE_LIMIT`, etc.

## Secrets

API keys are loaded via `IExchangeCredentialStore`. They are never sent to Angular, never logged, and never included in exception messages.
