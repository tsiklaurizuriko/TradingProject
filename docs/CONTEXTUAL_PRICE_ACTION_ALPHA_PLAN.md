# Contextual Price Action Alpha — implementation plan

Research only. LIVE = OFF. Scalping LIVE = OFF. Price Action LIVE = OFF. PAPER promotion = OFF. No VALIDATED_FOR_PAPER.

Phase 7 stays intact: 162 frozen hypotheses, descending triangle 5m OOS failure, symmetrical triangle 1h left as INSUFFICIENT_DATA. That triangle is not retuned and is not a Phase 8 hypothesis.

## What is reused

Existing Price Action book (geometry, swings, HH/HL/LH/LL, BOS, CHoCH, sweeps, failed breakouts, breakout-retest, W/M, flags, pennants, compression/expansion), `CausalIndicatorCache` ATR(14) and relative volume(20), `ResearchKlineCache`, Model B replay, chronological 60/20/20 split, walk-forward, cost multipliers BASE / 1.25 / 1.5 / 2.0, and `PortfolioOccupancyReplay`. No second candle store and no new download. OI, funding, taker flow, liquidations, order book, and basis are DATA_UNAVAILABLE and are not fabricated.

## Frozen question

Does a price-action event gain incremental information after causal higher-timeframe context and a later-or-equal closed confirmation, relative to the same event alone?

Hierarchy is fixed: 1h context, 15m structure, 5m event, 3m or 1m confirmation. No timeframe grid.

## Frozen hypotheses (25)

Eight families. Variants are baseline, contextual, and strict, except compression, which pre-registers continuation and reversal with and without 1h trend.

| Family | Baseline | Contextual | Strict |
| --- | --- | --- | --- |
| SWEEP | 5m sweep | sweep + 3m BOS + 1h HH/HL or LH/LL | sweep + 1m BOS + 15m bias + 1h trend + relative volume >= 1 |
| FAILED_BREAKOUT | 5m failed breakout | failure + 15m still inside its range + 3m BOS/CHoCH | + 1m BOS/CHoCH + 15m bias + 1h trend + relative volume |
| BREAKOUT_RETEST | 5m breakout-retest | retest + breakout displacement (body/range >= 0.55 and range >= ATR(14)) + 3m BOS + 1h trend | + 1m BOS + 15m trend + relative volume |
| PULLBACK | 5m sweep while 5m bias already agrees | + 3m BOS + 1h trend | + 1m BOS + 15m trend + relative volume |
| WM | W/M at second-swing detection, no neckline wait | neckline break + 3m BOS + 1h trend | neckline break + 1m BOS + 15m bias + 1h trend + relative volume |
| FLAG | existing flag/pennant breakout | + displacement + 3m BOS + 1h trend | + 1m BOS + 15m trend + relative volume |
| COMPRESSION | continuation BOS, and reversal CHoCH, after 3 compressed bars and an expansion bar | each plus 1h trend | not used |
| MTF | 5m BOS | 1h trend + 15m trend + 5m BOS + 3m BOS | 1h trend + 15m trend + 5m BOS + 1m BOS + relative volume |

Both sides are inside each hypothesis. Symmetrical triangle is absent on purpose.

Entry is the completed 5m candle. Fill is the next 5m open. Stops, targets, fees, and size stay on the existing Isolated LOW Model B book. No OOS stop search.

A higher-timeframe feature is the last candle with `CloseTime <=` the 5m close. Missing volume is not treated as zero. A missing required series is DATA_UNAVAILABLE for that book.

## Pre-OOS gate

Aggregate IS trades >= 30, IS PF >= 0.90 or no losses, validation trades >= 20, validation PF > 1, validation net > 0, and at least 5 symbols with validation trades. The survivor list is hashed before any OOS read. Definitions are not edited after that.

## Reporting gates

Interesting, still not VALIDATED_FOR_PAPER, only if OOS trades >= 50, OOS expectancy > 0, BASE PF > 1, HIGH 1.5x is not COST_FRAGILE, both sides have at least 15 OOS trades, symbol concentration is below the existing fragility rule, regime absolute-net share is below 0.80 when two or more regimes exist, and walk-forward is not a single-window artifact. Context must also beat its family baseline on OOS expectancy without keeping less than half of the baseline's OOS trades. Anything short of that stays OOS_FAILED, COST_FRAGILE, SYMBOL_FRAGILE, REGIME_FRAGILE, INSUFFICIENT_DATA, VALIDATION_FAILED, NO_TRADES, or RESEARCHING. VALIDATED_FOR_PAPER is not assigned.

## Run

`dotnet run --project tools/TradingPlatform.StrategyResearch -- --contextual-pa-discover --max-parallel 2`

Artifacts: `artifacts/strategy-research/contextual-price-action-alpha/`. Report: `docs/CONTEXTUAL_PRICE_ACTION_ALPHA_REPORT.md`.
