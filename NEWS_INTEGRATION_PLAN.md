# News Intelligence integration plan

This document is the architecture assessment for adding News Intelligence. The subsystem is a new project. Existing strategy evaluation, backtest math, validation assignment, risk, fees, and slippage stay as they are. The feature defaults to off.

## 1. Existing architecture

Strategies are template keys, not subclasses. `StrategyEngine` dispatches through `StrategyTemplateEvaluator` into frozen, advanced, alpha, scalping, and price-action evaluators. Definitions live in `StrategyDefinition` JSON.

`BacktestReplay` evaluates a closed bar, fills at the next bar open, and sizes with `RiskEngine`. Fees and slippage come from `ReplaySettings`. Research uses the same replay through `ResearchStrategyEngine` and `ResearchRunner`.

In-sample, validation, and out-of-sample are a 60/20/20 chronological split (`StrategyValidation.ChronologicalSplitIndices`). Status strings already include `RESEARCHING`, `OOS_FAILED`, `VALIDATION_FAILED`, `DATA_UNAVAILABLE`, `INSUFFICIENT_DATA`, and `NO_TRADES`. The research runner does not assign `VALIDATED_FOR_PAPER`. Sortino and Calmar are not calculated.

Futures series use `AlignedMarketSeries`: an observation is visible on bar `i` only when its timestamp is at or before that candle's `CloseTime`.

There is no news code. There is no CVD series. Liquidation history is `DATA_UNAVAILABLE`. Open interest is absent unless an existing aligned series is supplied. Order-flow confirmation that already exists is `TakerImbalance`. Structure that already exists is `MarketStructureEngine` and `LiquiditySweepEngine`.

## 2. Strategy extension points

News strategies are not added to `StrategyTemplateKeys` and `EvaluateTemplate` is not edited. Production `IStrategyEngine` remains `StrategyEngine`.

`NewsStrategyEngine` implements `IStrategyEngine` and is constructed only by the news research command. It calls the existing `BacktestReplay`. It is not registered in `AddStrategies`.

`UseNewsFilter` defaults to false. Nothing wraps `StrategyEngine`, so existing templates cannot see news.

Hypotheses are fixed published rules: news momentum, news overreaction, news plus open interest, news plus taker flow, news plus structure, and macro releases. Ablation turns one input off and reruns the same rule. Liquidation history is not invented.

## 3. Data extension points

New project: `src/TradingPlatform.News`. Domain types stay free of news models.

`INewsProvider` returns `RawNewsItem`. Adapters: CoinGecko, CryptoPanic, GDELT, and FRED. Credentials come from configuration, not source.

Live mode may call providers. Historical mode reads only the local store. A live response is not treated as a complete past archive.

Store path: `artifacts/data/news/raw`, `normalized`, and `events`. Cache identity is provider, UTC day, and asset filter.

Dedup is deterministic: canonical URL, provider id, normalized title, timestamp proximity, and asset overlap. One `NewsEvent` keeps many articles. There is no embedding model.

The default classifier is a deterministic rule set. Optional JSON classification is schema-checked. Invalid output becomes `Unknown` and keeps the original source and time. The classifier does not emit `SignalType`. `AiClassificationEnabled` defaults to false.

Macro `actual`, `consensus`, and `previous` are stored only when a provider sent them. Missing consensus stays null.

Point-in-time rule: at candle open `T`, an event is visible only when `PublishedAtUtc <= T`. A date-only timestamp is unreliable for intraday bars and is withheld until the next daily bar.

Decayed impact is `impact * exp(-lambda * ageMinutes)`. `lambda` is configuration, not fit on out-of-sample data.

## 4. Backtest extension points

`BacktestReplay` and `ReplayTrade` are unchanged. News audit fields are written into `StrategySignalDetail.Reason` and `Snapshot`, which replay already copies into the trade reason. Entry and exit timestamps are already on `ReplayTrade`.

`NewsEnabled` is not a replay flag. Existing commands never construct the news engine.

## 5. Validation extension points

The news runner uses `ChronologicalSplitIndices` and `BacktestReplay`. Status mapping follows the research runner: no trades, validation failure, and out-of-sample failure. It does not invent `OOS_PASSED` and does not assign `VALIDATED_FOR_PAPER`.

Stage 1 runs published defaults only. Each rule is compared with buy and hold and with the same rule after the news input is removed.

## 6. Configuration extension points

`NewsOptions`, section `News`, lives in the news project. Defaults: disabled, no providers, cache on, dedup on, AI off. These fields are not added to `TradingOptions`. The API host does not register news.

The research tool gains one flag, `--news-research`, which returns before the existing research path.

## 7. Proposed News Intelligence architecture

```text
News providers
    -> collector
    -> normalize
    -> deterministic dedup
    -> schema classifier
    -> file event store
    -> INewsFeatureProvider
    -> NewsStrategyEngine
    -> existing BacktestReplay
    -> existing IS / validation / OOS split
```

## 8. Files to create

- `src/TradingPlatform.News/`
- `tests/TradingPlatform.NewsTests/`
- `NEWS_INTEGRATION_PLAN.md`
- `NEWS_STRATEGY_RESEARCH.md` after a real research run

## 9. Existing files that change

- `tools/TradingPlatform.StrategyResearch/Program.cs` (one early flag)
- `tools/TradingPlatform.StrategyResearch/TradingPlatform.StrategyResearch.csproj` (project reference)

`StrategyEngine`, `BacktestReplay`, `RiskEngine`, seed data, and trading appsettings stay unchanged.

## 10. Why existing behavior stays the same

News code is not on the `IStrategyEngine` dependency-injection graph. Existing templates still hit the same evaluator switch. Replay math, risk, fees, slippage, and validation assignment are not edited. With `News.Enabled=false`, and without `--news-research`, no provider is constructed and no news file is required.
