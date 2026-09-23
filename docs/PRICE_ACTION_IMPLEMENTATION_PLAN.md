# PRICE ACTION IMPLEMENTATION PLAN

Extend the existing Scalping research subsystem with a causal Price Action / chart-pattern / candle-sequence / market-structure layer. Do not restart the bot. Do not create a second risk, execution, portfolio, or market-data stack.

**LIVE = OFF. Scalping LIVE = OFF. Price Action LIVE = OFF. Isolated = enforced. Risk Engine = authoritative. No VALIDATED_FOR_PAPER.**

## Phase 6 audit (verified in repo)

Present and kept:

- 22 `scalp_*` keys, family `SCALPING`, seeded disabled, outside OperatorCatalog
- Causal indicators + `MaxHoldBars` + holding-time percentiles
- `--scalping` / `--scalping-data` CLI, `ResearchKlineCache` (1m/3m/5m/15m IntervalMs)
- `PortfolioOccupancyReplay`
- Read-only `GET /api/trading/research/scalping*`
- `/scalping` Analysis route
- Frozen Five + Isolated LOW (0.5% R / 2% SL / 4% TP / 3x) unchanged
- `Trading:LiveTradingEnabled=false`, `Trading:Scalping.AllowLive=false`

Incomplete before this pass:

- No `Pattern*` / `PriceAction*` engines
- 1m / 3m coverage not ingested (architecture already supports them)
- IntegrationTests previously skipped on API DLL lock
- No `pa_*` keys, no pattern research artifacts, no PRICE_ACTION reports

Reuse, do not duplicate:

- `AlphaIndicatorSeries.ConfirmedSwingLow/High` (value written at `k+n`)
- `StructureBias` HH/HL vs LH/LL
- `liq_sweep_*`, `failed_breakout_reversal`, `market_structure_*`
- Model B `BacktestReplay`, `ResearchRunner`, Isolated occupancy, cost BASE/1.25/1.5/2.0
- Artifact JSON under `artifacts/strategy-research/` (no second candle store; no extra EF pattern tables)

## Architecture

```
MARKET DATA (existing kline cache)
      ↓
OHLCV + volume (+ futures series only when present)
      ↓
CausalIndicatorCache + PriceActionBook
      ↓
Candle features / events / sequences / structure / sweeps / chart patterns
      ↓
pa_* StrategyEngine keys (RESEARCH_ONLY)
      ↓
EXISTING Risk Engine → Portfolio occupancy → Execution → Isolated SL/TP
```

Live signal features use only information knowable at bar `i`. Research outcome labels (forward return, MFE, MAE, target/stop hit) are computed only by the research CLI and never enter `PriceActionStrategyEvaluator`.

Cup & Handle: `NOT_IMPLEMENTED` (no objective causal detector without subjective geometry).

## Keys (family SCALPING_PRICE_ACTION)

18 `pa_*` templates, seeded disabled, not in OperatorCatalog. Same Isolated book. Same StrategyEngine path.

## Data

Reuse `ResearchKlineCache`. Ingest 1m/3m via existing fapi klines. Missing history → `DATA_UNAVAILABLE`. Do not substitute another timeframe. Do not fabricate taker/OI/funding/basis.

## Order

1. This document.
2. Phase 6 verification (rebuild API, IntegrationTests, LIVE-off).
3. 1m/3m coverage through existing cache.
4. Causal engines + leakage tests.
5. Registry / seeder / evaluator.
6. `--price-action` / `--price-action-data`.
7. API + `/scalping` Price Action views + research chart debug.
8. Bounded research + reports.

## Constraints

- Do not enable LIVE / PAPER automatically.
- Do not change Frozen / LOW numbers.
- Do not add a PatternRiskEngine.
- PF = SUM(wins) / ABS(SUM(losses)); 0 trades → PF 0.
- PROMISING on IS is not validation.
