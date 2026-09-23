# SCALPING IMPLEMENTATION PLAN

Research-only SCALPING family inside the existing modular monolith. Runtime is .NET 10. LIVE stays off. Isolated USD-M stays one Binance position per coin. The Risk Engine remains the only sizer and SL/TP authority.

**LIVE = OFF. Scalping LIVE = OFF. Isolated = enforced. Risk Engine = authoritative. No VALIDATED_FOR_PAPER.**

## Reuse

- Strategy templates and `StrategyEngine` (closed candles only).
- Causal indicators (`CausalIndicatorCache`, VWAP, ADX, ATR, RSI, MACD, Bollinger, Donchian, rel-vol).
- `RiskEngine` + `IsolatedOccupancy` (0.5% R / 2% SL / 4% TP / 3x / max 5 coins per strategy / one Isolated coin globally). Frozen LOW is not changed.
- Model B `BacktestReplay` (next-open fill, fees + slippage, `MaxHoldBars`).
- Research CLI, statuses (`RESEARCHING`, `INSUFFICIENT_DATA`, `DATA_UNAVAILABLE`, `COST_FRAGILE`, `IS_PROMISING`). `VALIDATED_FOR_PAPER` is never assigned.

## Gaps this work fills

- SCALPING family keys (not in OperatorCatalog).
- 1m / 3m research timeframes (bots stay on 5m / fitted 15m).
- Missing causal indicators (Stoch, HMA, Aroon, PSAR, Williams, MFI, CMF, AO, session high/low).
- Coverage CLI on BTC/ETH + top-20 liquid USD-M.
- Chronological occupancy portfolio replay (account equity, same-coin rejects).
- Read-only API + `/scalping` UI. No LIVE control.

## Order

1. This document.
2. Coverage + kline cache for 1m/3m.
3. Indicators.
4. Scalping keys + evaluators (disabled, RESEARCHING).
5. MaxHoldBars + holding-time percentiles.
6. `PortfolioOccupancyReplay`.
7. `--scalping` / `--scalping-data`.
8. API + UI.
9. Tests and reports.

## Constraints

- Do not enable LIVE. Do not seed `IsEnabled=true` for scalping. Do not add keys to `OperatorCatalog`.
- Do not change Frozen / LOW R, SL, TP, or leverage.
- Do not fabricate taker / OI / funding. Missing → `DATA_UNAVAILABLE` / `INSUFFICIENT_DATA`.
- Do not start the API from this workstream (Visual Studio owns the process).
- Stop still does not flatten Isolated.

## Universe (v1)

BTCUSDT, ETHUSDT, and a volume-ranked top 20 USD-M perps. Do not ingest 528 coins at 1m.

## Operational

```
dotnet run --project tools/TradingPlatform.StrategyResearch -- --scalping-data
dotnet run --project tools/TradingPlatform.StrategyResearch -- --scalping
```

Frontend: `npm start` in `frontend/trading-platform-ui`. Backend: existing Visual Studio / shortcut with `Trading:LiveTradingEnabled=false`.
