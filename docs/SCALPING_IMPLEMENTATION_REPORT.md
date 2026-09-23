# SCALPING IMPLEMENTATION REPORT

**LIVE = OFF. Scalping LIVE = OFF. Isolated = enforced. Risk Engine = authoritative. No VALIDATED_FOR_PAPER.**

Research-only SCALPING family inside the existing modular monolith. Runtime is .NET 10. Frozen five and Isolated LOW (0.5% R / 2% SL / 4% TP / 3x) were not changed. Scalping keys are not in `OperatorCatalog` and seed as disabled `RESEARCHING`.

## Reused

- `StrategyEngine` / `StrategyTemplateEvaluator` / Advanced + Alpha evaluators (closed candles only).
- Causal indicator cache (EMA, RSI, MACD, ATR, Bollinger, Donchian, VWAP, ADX, Supertrend, rel-vol).
- `RiskEngine` as the only sizer and SL/TP authority. Occupancy replay calls `Evaluate` with `SymbolAlreadyOpen` and per-strategy `OpenPositionCount`.
- Model B `BacktestReplay` (`LowIsolatedRisk`, fees + slippage, `MaxHoldBars`).
- Research CLI, `ResearchRunner`, statuses. `AssignStatus` still never returns `VALIDATED_FOR_PAPER`.
- Disk kline cache (`ResearchKlineCache`) with 1m/3m interval + gap count + taker from kline[9].

## Added

- `StrategyTemplateKeys.Scalping` (22 keys), `Family => SCALPING`, `ScalpingTimeframes` 1m/3m/5m/15m.
- `ScalpingStrategyEvaluator` — remaps most keys to existing VWAP / sweep / squeeze / structure / MTF / Supertrend / vol-spike paths. New: Stochastic momentum, session high/low filter, fast EMA/RSI/MACD/BB scalp variants.
- `ScalpingIndicatorSeries` — Stochastic K/D, StochRSI, HMA, Aroon, Williams %R, MFI, CMF, AO, PSAR, session high/low, UTC hour.
- `PortfolioOccupancyReplay` — sequential account equity, same-coin / slot / heat rejects, portfolio DD from combined equity.
- Holding-time P25/P75 plus `HOLDING_LOOKS_LIKE_SWING` note.
- CLI `--scalping-data` and `--scalping`.
- Read-only `GET /api/trading/research/scalping`, `/coverage`, `/runs/{id}`.
- Angular `/scalping` under Analysis. Coin labels. No LIVE control.
- `Trading:Scalping` config (`Enabled: false`, `AllowLive: false`, MaxHoldBars 1m=15, 3m=12, 5m=8, 15m=6).
- Seeder upserts disabled scalping strategies; `RetireHidden` skips them so they are not archived.

## Modified

- `ResearchKlineCache` 1m/3m caps and `GapCount`.
- `ResearchRegistry.Scalping`, `NextHigherTimeframe` for 1m/3m.
- `ResearchRunner` wires TF `MaxHoldBars` for scalping books.
- `ScalpingResearchQuery` reads Pascal/camel artifacts and overlays `coverage.json` when the summary coverage rows are empty.
- Database seeder catalog + retire skip.
- Frozen/LOW tests updated only for template counts (37→59, research 32→54). Frozen five assertions unchanged.

## Not changed

- LIVE remains off. No Cross margin. No second executor or `ScalpingRiskEngine`.
- Isolated one-coin-globally and 5 unique coins per running strategy (live occupancy).
- Historically fitted BTC 15m LIVE-off guard.
- Postgres still does not store funding/OI in v1.

## Tests

- Unit: scalping indicators (causal/warmup), scalp keys/signals, 1m vs 5m closed-bar evaluator, futures keys `DATA_UNAVAILABLE`, research JSON mapping (Pascal books + camel coverage).
- Backtesting: `MaxHoldBars` TIME exit, occupancy second BTC reject, long+short same-coin reject, PF = wins/|losses|.
- Research: scalping registry, holding percentiles, COST_FRAGILE when BASE PF>1 and HIGH PF<1, `AssignStatus` never `VALIDATED_FOR_PAPER`.
- Angular `/scalping` development build succeeded after wiring `money` and the existing UI kit (metric cards, status badges).
- Test run: Unit 156, Backtesting 48, Research 75, Trading 4 passed. IntegrationTests were not rebuilt because `TradingPlatform.Api` was locked by a running Visual Studio API process (this work does not start or stop the API). Restart the API from Visual Studio to pick up `GET /api/trading/research/scalping*`.

## Rollback

Set `Trading:Scalping:Enabled=false` (default). Keys unused if not in operator catalog. Delete `docs/SCALPING_*.md` and `artifacts/strategy-research/scalping/`. Leave Frozen templates untouched.

**LIVE = OFF. Scalping LIVE = OFF. Isolated = enforced. Risk Engine = authoritative. No VALIDATED_FOR_PAPER.**
