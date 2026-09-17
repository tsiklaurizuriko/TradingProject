# Strategies

Strategies are JSON documents evaluated by the strategy engine. Users build them in the Angular Strategy Builder; experts may edit JSON in the advanced editor. The API validates both.

## Rules

- A version that has been used by a bot, fill, or backtest is immutable.
- Indicators only see **closed** candles at decision time.
- `STOP_LOSS` / `TAKE_PROFIT` percents are evaluated against average entry, not as Binance native stop orders in MVP (spot market/limit only).

## Sample: EMA RSI Strategy (v1)

Timeframe: 5m. Example only — not a profitability claim.

```json
{
  "name": "EMA RSI Strategy",
  "version": 1,
  "symbol": "BTCUSDT",
  "timeframe": "5m",
  "entry": {
    "operator": "AND",
    "conditions": [
      {
        "indicator": "EMA",
        "period": 20,
        "comparison": "CROSSES_ABOVE",
        "value": { "indicator": "EMA", "period": 50 }
      },
      {
        "indicator": "RSI",
        "period": 14,
        "comparison": "GREATER_THAN",
        "value": 50
      }
    ]
  },
  "exit": {
    "operator": "OR",
    "conditions": [
      {
        "indicator": "EMA",
        "period": 20,
        "comparison": "CROSSES_BELOW",
        "value": { "indicator": "EMA", "period": 50 }
      },
      {
        "type": "STOP_LOSS",
        "percent": 1.5
      },
      {
        "type": "TAKE_PROFIT",
        "percent": 3
      }
    ]
  }
}
```

## Sample risk profile: Conservative

- risk per trade: 1%
- max position: 10%
- max daily loss: 5%
- max open positions: 3
- max daily trades: 20
- cooldown after loss: 15 minutes

Configuration example, not financial advice.
