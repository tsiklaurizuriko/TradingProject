# PRICE ACTION IMPLEMENTATION REPORT

**LIVE = OFF. SCALPING LIVE = OFF. PRICE ACTION LIVE = OFF. Isolated = enforced. Risk Engine = authoritative. No VALIDATED_FOR_PAPER. No future leakage.**

## Architecture audit

Phase 6 Scalping remains in place. This pass extends it with a causal Price Action layer inside the same StrategyEngine → RiskEngine → Isolated occupancy → Execution path.

Verified present before this work and preserved:

- 22 `scalp_*` keys, family `SCALPING`, seeded disabled, not in OperatorCatalog
- Causal indicators, `MaxHoldBars`, holding-time percentiles
- `--scalping` / `--scalping-data`, `ResearchKlineCache` (1m/3m/5m/15m)
- `PortfolioOccupancyReplay`
- `GET /api/trading/research/scalping*`
- `/scalping` route
- Frozen Five + Isolated LOW 0.5% R / 2% SL / 4% TP / 3x
- `Trading:LiveTradingEnabled=false`, `Trading:Scalping.AllowLive=false`

## Reused

- `AlphaIndicatorSeries.ConfirmedSwingLow/High` contract (value written at `k+n`)
- `CausalIndicatorCache`, VWAP, ATR, ADX, RSI, relative volume
- Model B `ResearchRunner` (IS / VALIDATION / OOS / walk-forward, cost BASE/1.25/1.5/2.0)
- Isolated occupancy (one coin globally, max 5 unique coins per strategy)
- Artifact JSON under `artifacts/strategy-research/` (no second candle store)
- Existing Angular shell, `/scalping` route, Lightweight Charts

## New

- Causal engines: candle geometry/events, sequences, swings, structure (HH/HL/LH/LL, BOS, CHoCH), liquidity sweeps, chart patterns, research-only outcome labels
- 18 `pa_*` keys, family `SCALPING_PRICE_ACTION`, seeded disabled, RESEARCHING, not in OperatorCatalog
- `--price-action` / `--price-action-data`
- `GET /api/trading/research/scalping/price-action` and `/occurrences`
- `/scalping` tabs: Overview, Strategies, Price Action, Pattern research + occurrence chart debug
- `Trading:PriceAction.AllowLive=false`

## Modified

- `StrategyTemplateKeys` / seeder / `AdvancedStrategyEvaluator` / `ResearchRegistry`
- `CausalIndicatorCache.PriceAction()`
- `TradingController`, `TradingOptions`, `appsettings.json`
- Test counts: All 77, Research 72, Scalping 22, PriceAction 18, Frozen 5 unchanged
- `HealthEndpointTests` for the new read-only endpoint

## Database

No new EF entities or migrations. Occurrences live in research artifacts. `pa_*` strategy rows are seeded like scalping: `IsEnabled=false`, `RESEARCHING`, retired-hidden keep-list includes `IsResearchOnlyFamily`.

## Data ingestion

Reuses `ResearchKlineCache` / fapi klines. Missing 1m/3m is `DATA_UNAVAILABLE`, not replaced by 5m. Taker/OI/funding/basis are not fabricated.

## Cup & Handle

`NOT_IMPLEMENTED`. No subjective detector. No `pa_cup_handle` key.

## Causal / leakage

- Swing confirmation index is `pivot + n`, never earlier
- Pattern `DetectionIndex` is when the structure was knowable
- `ConfirmationIndex` is the first close that breaks the neckline/range
- `PatternOutcomeEngine` is not referenced by `PriceActionStrategyEvaluator`
- Adversarial tests: prefix vs future bars must not rewrite a past confirmation time

## Tests (this pass)

- Unit 172 passed
- Backtesting 48 passed
- Research 75 passed
- Trading 4 passed
- IntegrationTests 4 passed (API rebuilt after stopping the locked process)
- Angular `ng build` green

## Research runtime

Bounded `--price-action --symbol BTCUSDT,ETHUSDT --timeframe 1m,3m,5m,15m --max-parallel 2` finished in ~210s.

- 6336 Model B books (IS / VALIDATION / OOS / WF0–WF7 × BASE/MILD/HIGH/STRESS)
- 120 sequence statistic rows
- 167 pattern statistic rows
- Occupancy: 20 same-coin rejects, 0 slot, 0 heat, chronological portfolio DD 1.59%
- 1m/3m ingested for BTC/ETH (129599 / 43199 cache bars; research used last 8000)
- VALIDATED_FOR_PAPER = none
- COST_FRAGILE = none on this pass (IS_PROMISING on IS is not validation; OOS_FAILED on 52 BASE OOS books)

## Operational

Backend (LIVE off):

```
dotnet run --project src/TradingPlatform.Api
```

Frontend:

```
cd frontend/trading-platform-ui
npm start
```

Then open Analysis → Scalping (`/scalping`). Tabs: Overview, Strategies, Price Action, Pattern research.

Data / research (does not enable LIVE):

```
dotnet run --project tools/TradingPlatform.StrategyResearch -- --price-action-data
dotnet run --project tools/TradingPlatform.StrategyResearch -- --price-action --symbol BTCUSDT,ETHUSDT --timeframe 1m,3m,5m,15m --max-parallel 2
dotnet run --project tools/TradingPlatform.StrategyResearch -- --scalping --symbol BTCUSDT --timeframe 5m --max-parallel 2
```

Reports: `docs/PRICE_ACTION_IMPLEMENTATION_PLAN.md`, `docs/PRICE_ACTION_IMPLEMENTATION_REPORT.md`, `docs/PRICE_ACTION_RESEARCH_REPORT.md`.

