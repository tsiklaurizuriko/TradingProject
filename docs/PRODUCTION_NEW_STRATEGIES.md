# Production-new strategies

Six research strategies for Binance USDⓈ-M futures. Every external id ends in `-new`. None is marked validated. The default verdict is `INSUFFICIENT_EVIDENCE`.

They emit signals only. Orders still go through the existing path:

`Strategy signal → RiskEngine → position size → execution adapter → Binance → reconciliation`

`market-regime-new` never emits Buy or Sell. It only reports a regime and which other `-new` ids that regime permits.

## Identifiers

| Id | Class | Display name |
| --- | --- | --- |
| `trend-following-new` | `TrendFollowingNewStrategy` | Trend Following-new |
| `breakout-volume-new` | `BreakoutVolumeNewStrategy` | Breakout Volume-new |
| `market-regime-new` | `MarketRegimeNewStrategy` | Market Regime-new |
| `momentum-relative-strength-new` | `MomentumRelativeStrengthNewStrategy` | Momentum Relative Strength-new |
| `mean-reversion-rsi-bollinger-new` | `MeanReversionRsiBollingerNewStrategy` | Mean Reversion RSI Bollinger-new |
| `funding-basis-new` | `FundingBasisNewStrategy` | Funding Basis-new |

The C# types are `ProductionNewEvaluator`, `MarketRegimeClassifier`, `CostAwareQuantity`, and `ProductionNewGate`. The template id is the external name.

## Indicators

Reused implementations, so live and historical code match:

- EMA: SMA seed, then `alpha = 2/(n+1)`.
- ATR(14), RSI(14), ADX(14): Wilder smoothing. ADX is strength, not direction. Flat markets and zero divisors stay uninitialized.
- Bollinger (20, 2): population standard deviation (divide by N, not N-1).
- NATR = `100 * ATR(14) / close` via the existing ATR percent series.
- Relative volume = volume / SMA(volume, 20), current bar included. A zero average is no signal.
- Momentum = `close / close_at_or_before(now - horizon) - 1`, with a gap check. 24h and 7d are timestamps, not a fixed bar count.

Warm-up is the longest series in use. EMA(200) needs 200 closed bars. A NATR percentile needs a full window (default 100) with no nulls inside it. Missing values are not bridged.

## Regime

Calculated per symbol from completed candles.

- `HIGH_VOLATILITY`: NATR percentile at or above `fundingExtremePercentile` (default 0.90).
- `TRENDING`: otherwise ADX above `minimumAdx` (default 20).
- `RANGING`: otherwise ADX below `minimumAdx - 2`.
- `UNCERTAIN`: the band between those ADX levels, or the last `swingLength` bars (default 2) do not agree.
- `INSUFFICIENT_DATA`: warm-up, invalid candle, or a hole in the percentile window.

`TRENDING` permits trend, breakout, momentum, and funding. `RANGING` permits mean reversion and funding. Anything else permits no new entry. An open position is not closed merely because the label changed.

If `requireBenchmark` is true and BTC candles are missing, the regime is `UNCERTAIN`. It is not treated as a pass.

## Strategy rules

Baseline exits are the ones enabled below. Other exits stay off until a separate trial turns them on.

### trend-following-new

Long: EMA(50) > EMA(200), close above EMA(50), ADX(14) > 20. Short is the mirror. This is a persistent EMA relationship, not a crossover on the current bar. Slope filter (`emaSlopeFilter`) is off. Stop is `entry ± 2 * ATR(14)`. Baseline exit is that stop plus an EMA-order flip (`oppositeTrendExit`). EMA(50) trend-break, ATR trail, and max hold are off. An opposite signal closes and does not reverse.

### breakout-volume-new

`H_N` and `L_N` exclude the current candle. N default 20. Long: close above `H_N` and relative volume above 1.5, and the previous bar was not already outside. Entries more than `priceDisplacementAtr` (1.5) ATR beyond the level are rejected. Baseline stop is the ATR stop. A close back inside the broken range exits. Take-profit R and the ATR trail are off.

### market-regime-new

Diagnostics only. Reason text includes the regime, ADX, NATR percentile, and permitted strategy ids.

### momentum-relative-strength-new

Needs a peer map on the signal context. Symbols are scored with `0.5 * 24h + 0.5 * 7d` using candles whose close time is at or before the decision. A name is eligible once, when it newly enters the top 5 (long, score > 0) or bottom 5 (short, score < 0). No peer map means `NO_TRADE`. Listing and delisting history is not in this repository, so survivorship is not corrected. Correlation is applied only when an open-book return sample is supplied; a sample under 30 bars rejects the new exposure instead of assuming independence.

### mean-reversion-rsi-bollinger-new

Baseline is confirmed re-entry, and only in `RANGING`. Long: some bar in the last `exitLookback` (8) closed below the lower band with RSI(14) < 30, and the current close is back at or above the lower band, once per excursion. Short is the mirror. RSI outside 30/70 is not itself an order. Target is the middle band. The trade is rejected when that distance does not clear the default round trip (0.04% taker fee and 0.05% slippage, each side). Stop is ATR. If stop and target are both inside one later candle, the backtest takes the stop first.

### funding-basis-new

Directional mode only. `HedgedMode` true returns `NO_TRADE` and does not fall back to a directional order. Spot execution is not used. Basis is `(futures - spot) / spot` from the supplied mark/index series. Z-scores use a population window of at least 20 settled observations. Missing funding is not zero. A candidate also needs the absolute funding rate to exceed the default round-trip cost, because one payment is the conservative holding assumption. Positive funding with a rich basis can propose a short. Negative funding with a cheap basis can propose a long. That is a research rule, not a proven edge.

## Risk

`CostAwareQuantity` sizes linear USDT contracts from:

`equity * riskFraction / (price distance to stop + entry fee + exit fee + entry slippage + exit slippage + optional funding loss per unit)`

Quantity is floored to `stepSize`. It is not rounded up to a minimum notional. The existing `RiskEngine` remains the authority. For these six ids it is called with `CostAware = true` and with the strategy stop converted to a percent. Defaults: 0.35% risk per trade, 1.5% aggregate planned risk, 2% daily loss. The daily halt is realized PnL since UTC midnight plus open losses. Open gains do not offset realized losses. There is no martingale.

Seeded risk profiles have `AllowLive = false`. `Trading:ProductionNew:LiveEnabled` and each strategy flag also default to false. Both the flag and `LiveEnabled` must be true before a live entry is even offered to the risk engine. Paper and shadow need `PaperEnabled` as well. Exits are not blocked by that gate.

Stops that are missing, on the wrong side, or inside the exchange minimum distance are rejected. The existing bot path still reconciles protective orders; this change does not add a second order router.

## Backtest assumptions

The existing `BacktestReplay` is used:

- Signal on the closed candle.
- Entry and signal exit at the next bar open, then slippage.
- Fees on both sides. Default research settings: 0.04% fee, 0.05% slippage.
- Stop is tested before target in the same candle.
- A gap through the stop fills at the open, not at a price beyond the gap plus slippage again.
- Funding cashflow is applied only when a settlement list is passed and the position is open across that timestamp. Missing funding is not treated as zero. The universe command loads `artifacts/data/futures-history-v1/funding/{symbol}.json` when that file exists and then labels the replay `INCLUDING_FUNDING`.
- Basis for `funding-basis-new` comes from `artifacts/data/futures-history-v1/basis/{symbol}_{timeframe}.json`. Symbols without that file do not get a funding signal.
- Chronological split is 60% in-sample, 20% validation, 20% out-of-sample, by the trade open time on one causal full-series replay. Reset walk-forward windows use `StrategyValidation.WalkForwardWindows` and keep earlier bars only for indicator warm-up. Parameters stay at the documented defaults.
- `ProductionNewAcceptance` can return a single-series `PASS`. That is not a strategy validation. `ProductionNewVerdict` is the strategy-level gate: at least 8 symbols with out-of-sample trades, at least 100 out-of-sample trades, at least 30 long and 30 short, profit factor at least 1.15 and defined, drawdown under 25%, positive in-sample net with at least 30 in-sample trades, and at least 4 executed walk-forward windows of which at least half of the windows that traded are non-negative. Otherwise the status is `REJECTED`, `INSUFFICIENT_EVIDENCE`, or `DATA_BLOCKED`. Nothing in this run is marked `VALIDATED` unless that gate passes.

`dotnet run --project tools/TradingPlatform.StrategyResearch/TradingPlatform.StrategyResearch.csproj -- --production-new` replays every `artifacts/strategy-validation-cache/{symbol}_{1h|15m|5m}.json` one symbol at a time. It writes `artifacts/strategy-research/production-new/{utc-date}/summary.json` plus JSONL files. Reset walk-forward runs for every 1h symbol and for the symbols that have a basis file on 15m and 5m. Momentum on 1h uses every cached 1h symbol as the peer book. Momentum on 15m and 5m uses only the basis-file symbols. Momentum walk-forward is not part of that command. The shared-equity overlay is labeled `DIAGNOSTIC_SHARED_CAPITAL`.

The 2026-10-16 run is in `docs/PRODUCTION_NEW_COMPLETION_REPORT.md` and `artifacts/strategy-research/production-new/2026-10-16/summary.json`. On that run, trend following and breakout are `REJECTED` on 1h, 15m, and 5m. Mean reversion is `REJECTED` on 15m and 5m. Momentum, funding/basis, and the regime filter are `INSUFFICIENT_EVIDENCE`. None is `VALIDATED`.

## Configuration

`src/TradingPlatform.Api/appsettings.json` under `Trading:ProductionNew`. Example with secrets omitted:

```json
"ProductionNew": {
  "LiveEnabled": false,
  "PaperEnabled": false,
  "RiskPerTradePercent": 0.35,
  "MaxAggregateOpenRiskPercent": 1.5,
  "DailyLossLimitPercent": 2,
  "DailyLossBasis": "UtcDayRealizedPlusOpenLosses",
  "Strategies": {
    "trend-following-new": false,
    "breakout-volume-new": false,
    "market-regime-new": false,
    "momentum-relative-strength-new": false,
    "mean-reversion-rsi-bollinger-new": false,
    "funding-basis-new": false
  }
}
```

Strategy parameters live in the template JSON (`emaFast`, `emaSlow`, `minimumAdx`, `atrStopMultiplier`, `donchianLength`, `breakoutRelativeVolume`, `priceDisplacementAtr`, `takeProfitR`, `reentryConfirmation`, `emaSlopeFilter`, `atrTrailEnabled`, `oppositeTrendExit`, `emaTrendBreakExit`, `maxHoldBars`, `requireBenchmark`, `hedgedMode`, `topRankCount`, `bottomRankCount`, `basisZEntry`, `fundingZEntry`, `momentumWeight24h`, `momentumWeight7d`). `hedgedMode` true does not trade.

`Trading:LiveTradingEnabled` is a separate switch for the whole process. Turning it on does not turn these six strategies on.

## Commands

From the repository root:

```text
dotnet test tests/TradingPlatform.UnitTests/TradingPlatform.UnitTests.csproj --filter ProductionNewStrategyTests
dotnet test tests/TradingPlatform.UnitTests/TradingPlatform.UnitTests.csproj --filter CausalReplayEquivalenceTests
dotnet run --project tools/TradingPlatform.StrategyResearch/TradingPlatform.StrategyResearch.csproj -- --production-new
```

Paper or shadow entries for these ids also require `Trading:ProductionNew:PaperEnabled` and the individual strategy flag. The API process is started with the existing host, for example `dotnet run --project src/TradingPlatform.Api/TradingPlatform.Api.csproj`. Venue comes from `Trading:Venue` (`Live`, `Shadow`, or `Testnet`). Testnet still uses the existing Binance testnet URLs in configuration. Do not put API keys in appsettings. The existing encrypted credential store is unchanged.

Live entries additionally need `Trading:LiveTradingEnabled=true`, `Trading:ProductionNew:LiveEnabled=true`, the strategy flag, the strategy row enabled, and `AllowLive` on its risk profile. The seeded profile has `AllowLive=false`.

## Limitations

- The live candle cycle still does not load historical funding, mark, or index series. `funding-basis-new` returns `NO_TRADE` in that cycle until those series are supplied. Research can use the futures-history files when they exist. It does not invent them.
- Hedged spot/futures execution is not implemented. `hedgedMode` stays a no-trade path.
- Momentum on 5m needs about 2016 bars for a 7-day return. One Binance kline request is capped, and the live prefetch asks for 1500 bars, so a live 5m seven-day score is often `NO_TRADE`.
- Survivorship of listings is not corrected.
- The research portfolio overlay scales isolated-book trade pnl onto one equity curve and enforces symbol, position-count, aggregate-risk, and daily-loss limits. It does not re-simulate a different signal after a rejection, and it is not a mark-to-market of open positions. The live coordinator is still what prevents two strategies from taking the same coin on one pass.
- These defaults were not optimized. A result is not evidence of profit unless `ProductionNewVerdict` returns `VALIDATED`.
- Paper entries require `Trading:ProductionNew:PaperEnabled=true` and the individual strategy flag. Leave `LiveEnabled` false. Do not set `AllowLive` on the seeded risk profile. Testnet uses `Trading:Venue=Testnet` and the existing testnet URLs. No API key belongs in these files.
