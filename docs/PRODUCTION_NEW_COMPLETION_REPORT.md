# Production-new completion report

Run date: 2026-10-16. Git HEAD at the time of the run was `b67f2db`. The executable was built from the uncommitted working tree, not from a clean checkout of that commit. No parameter was changed after seeing these results. No strategy is `VALIDATED`.

## Executive summary

The six `-new` strategies stay in the existing signal, risk, and backtest path. Live switches stay off. A universe replay of the local candle cache, with fees, slippage, and cached funding when a funding file exists, does not support live trading.

`trend-following-new` and `breakout-volume-new` are `REJECTED` on 1h, 15m, and 5m. `mean-reversion-rsi-bollinger-new` is `REJECTED` on 15m and 5m and `INSUFFICIENT_EVIDENCE` on 1h. `momentum-relative-strength-new` and `funding-basis-new` are `INSUFFICIENT_EVIDENCE`. `market-regime-new` placed no orders.

The earlier BTCUSDT 15m out-of-sample gain for trend following does not survive this universe. Breakout losses remain after costs, and on 15m and 5m the gross result is also negative, so the loss is not only a fee artifact.

## What changed

- Strategy-level gates in `ProductionNewVerdict`. A single-series `PASS` is not `VALIDATED`.
- Shared-capital overlay in `ProductionNewPortfolio`. It is labeled `DIAGNOSTIC_SHARED_CAPITAL`.
- Universe research command. It replays every cached 1h, 15m, and 5m file, writes JSON, and runs reset walk-forward for every 1h symbol and for the 10 symbols that have basis files on 15m and 5m.
- Funding settlements are applied on price-strategy replays when `artifacts/data/futures-history-v1/funding/{symbol}.json` exists. Missing funding is not treated as zero.
- `funding-basis-new` runs only when a basis file exists for that symbol and timeframe.
- Momentum ranking uses a causal score book. The evaluator reuses the replay indicator cache instead of rebuilding it on every bar.
- Configuration tests load the API and Workers appsettings files, including Live and Shadow overlays.
- Host configuration for the six strategies was not switched on.

## Validation gates

Fixed before this run, in `ProductionNewVerdict`:

- At least 8 symbols with out-of-sample trades.
- At least 100 out-of-sample trades, at least 30 long and 30 short.
- Profit factor defined and at least 1.15. No losing trades means profit factor is undefined, not zero.
- Portfolio overlay drawdown under 25%.
- In-sample net positive with at least 30 in-sample trades.
- At least 4 executed reset walk-forward windows, and at least half of the windows that traded are non-negative.

Otherwise the status is `REJECTED`, `INSUFFICIENT_EVIDENCE`, or `DATA_BLOCKED`.

## Data

Cache: `artifacts/strategy-validation-cache`. 529 symbols have 1h, 15m, and 5m files. Inside each file the measured duplicate, unusable, and gap counts were zero. That does not mean every symbol existed for the whole date union, and listing survivorship is not corrected.

| Timeframe | Files | Bars | Union start | Union end | Symbols with funding | Symbols with basis |
|---|---:|---:|---|---|---:|---:|
| 1h | 529 | 354,230 | 2024-09-18 | 2026-09-30 | 527 | 10 |
| 15m | 529 | 28,653,074 | 2024-09-18 | 2026-09-30 | 527 | 10 |
| 5m | 529 | 86,185,125 | 2024-01-01 | 2026-09-30 | 527 | 10 |

Basis, mark, and index files exist for ADA, AVAX, BNB, BTC, DOGE, ETH, LINK, LTC, SOL, and XRP only. The cache ends 2026-09-30. Today in this session is 2026-10-16, so the cache is not current.

Costs, same for every strategy: 0.04% fee per side, 0.05% slippage per side, 0.35% risk, leverage 2, 10,000 starting equity. Signal on the closed bar. Fill at the next bar open. Funding cashflow is included for price strategies when a funding file exists. Momentum replays did not attach funding settlements, so their funding column is zero and is not a funding backtest.

Out-of-sample trades are those opened in the final 20% of one causal full-series replay. Walk-forward rows are separate reset windows.

## Out-of-sample results

Net figures are quote-currency pnl summed across isolated 10,000 books. They are not a shared account.

### 1h

| Strategy | Trades | Long / short | Net | Gross | Fees | Slippage | Funding | PF | Expectancy | In-sample net | WF windows (traded / non-negative) | Verdict |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| trend-following-new | 490 | 121 / 369 | -4,483.46 | -3,678.46 | 428.29 | 535.36 | 158.65 | 0.6243 | -9.15 | +28,863.24 | 4,232 (3,497 / 1,428) | REJECTED |
| breakout-volume-new | 1,023 | 476 / 547 | -5,169.83 | -2,992.85 | 968.31 | 1,210.38 | 1.71 | 0.3989 | -5.05 | -5,811.39 | 4,232 (2,566 / 806) | REJECTED |
| mean-reversion-rsi-bollinger-new | 20 | 9 / 11 | +52.49 | +92.49 | 21.12 | 26.40 | 7.52 | 1.2846 | +2.62 | -525.65 | 4,232 (91 / 50) | INSUFFICIENT_EVIDENCE |
| momentum-relative-strength-new | 34 | 18 / 16 | -378.31 | -331.73 | 20.70 | 25.88 | 0 | 0.5751 | -11.13 | -109.00 | not executed | INSUFFICIENT_EVIDENCE |
| funding-basis-new | 0 | 0 / 0 | 0 | 0 | 0 | 0 | 0 | n/a | 0 | 0 | 80 (0 / 0) | INSUFFICIENT_EVIDENCE |
| market-regime-new | 0 | 0 / 0 | 0 | 0 | 0 | 0 | 0 | n/a | 0 | 0 | n/a | INSUFFICIENT_EVIDENCE |

### 15m

| Strategy | Trades | Long / short | Net | Gross | Fees | Slippage | Funding | PF | Expectancy | In-sample net | WF (traded / non-negative) | Verdict |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| trend-following-new | 54,315 | 26,648 / 27,667 | -236,219.97 | +4,087.74 | 107,813.79 | 134,767.24 | 2,273.32 | 0.8427 | -4.35 | -117,304.86 | 80 (80 / 34) | REJECTED |
| breakout-volume-new | 72,977 | 36,541 / 36,436 | -280,006.96 | -16,986.03 | 116,871.08 | 146,088.85 | -60.99 | 0.4320 | -3.84 | -934,442.46 | 80 (80 / 0) | REJECTED |
| mean-reversion-rsi-bollinger-new | 2,112 | 1,086 / 1,026 | -12,931.51 | -1,273.36 | 5,183.46 | 6,479.33 | 4.64 | 0.6017 | -6.12 | -21,862.46 | 80 (55 / 24) | REJECTED |
| momentum-relative-strength-new | 78 | 42 / 36 | +1,175.57 | +1,750.67 | 255.60 | 319.50 | 0 | 1.4904 | +15.07 | -10,874.78 | not executed | INSUFFICIENT_EVIDENCE |
| funding-basis-new | 0 | 0 / 0 | 0 | 0 | 0 | 0 | 0 | n/a | 0 | 0 | 80 (0 / 0) | INSUFFICIENT_EVIDENCE |
| market-regime-new | 0 | 0 / 0 | 0 | 0 | 0 | 0 | 0 | n/a | 0 | 0 | n/a | INSUFFICIENT_EVIDENCE |

15m trend gross is slightly positive and the net loss is mostly fees and slippage. 15m breakout is negative before those costs as well. 15m momentum’s out-of-sample profit factor is above 1.15, but the sample is 78 trades on 5 symbols, the in-sample net is negative, and reset walk-forward was not run.

### 5m

| Strategy | Trades | Long / short | Net | Gross | Fees | Slippage | Funding | PF | Expectancy | In-sample net | WF (traded / non-negative) | Verdict |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| trend-following-new | 163,231 | 82,449 / 80,782 | -679,800.71 | -123,252.10 | 247,050.94 | 308,813.64 | -684.03 | 0.6974 | -4.16 | -1,796,700.57 | 80 (80 / 12) | REJECTED |
| breakout-volume-new | 207,860 | 101,158 / 106,702 | -417,643.24 | -24,479.76 | 174,717.58 | 218,396.97 | -48.93 | 0.2607 | -2.01 | -3,016,426.42 | 80 (80 / 0) | REJECTED |
| mean-reversion-rsi-bollinger-new | 4,820 | 2,491 / 2,329 | -43,067.40 | +3,674.78 | 20,780.86 | 25,976.08 | 14.76 | 0.4522 | -8.94 | -112,201.23 | 80 (76 / 14) | REJECTED |
| momentum-relative-strength-new | 70 | 26 / 44 | -1,783.27 | -991.54 | 351.88 | 439.85 | 0 | 0.2641 | -25.48 | -13,577.50 | not executed | INSUFFICIENT_EVIDENCE |
| funding-basis-new | 0 | 0 / 0 | 0 | 0 | 0 | 0 | 0 | n/a | 0 | 0 | 80 (0 / 0) | INSUFFICIENT_EVIDENCE |
| market-regime-new | 0 | 0 / 0 | 0 | 0 | 0 | 0 | 0 | n/a | 0 | 0 | n/a | INSUFFICIENT_EVIDENCE |

## Portfolio overlay

Out-of-sample trades only. Each isolated pnl is scaled by equity at entry. A second strategy cannot hold the same coin. Aggregate planned risk is capped at 1.5%. Daily loss is 2%. This does not replace a rejected signal with another one, and the drawdown is trade-close equity, not a full mark-to-market of open positions.

| Timeframe | Accepted | Rejected | Net | Fees | Slippage | Funding | Max DD | Ending equity |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1h | 192 | 1,375 | +430.83 | 198.93 | 248.66 | -7.49 | 10.49% | 10,430.83 |
| 15m | 3,908 | 125,574 | -7,648.35 | 3,943.06 | 4,928.83 | 10.78 | 81.16% | 2,351.65 |
| 5m | 10,239 | 365,742 | -9,231.00 | 3,418.33 | 4,272.91 | -18.84 | 92.48% | 769.00 |

The 1h overlay is positive because most trades were rejected (aggregate risk, daily loss, or a coin already open). That is not evidence the strategies make money. 15m and 5m overlays lose most of the account.

## Why nothing is validated

- `trend-following-new`: out-of-sample net and profit factor fail on all three timeframes. The 1h in-sample gain does not carry into the holdout. 15m and 5m walk-forward windows are mostly negative.
- `breakout-volume-new`: large losses on every timeframe. Every 15m and 5m walk-forward window that traded lost money. This matches the strategy rules. It is not a look-ahead defect. The channel still excludes the current candle.
- `mean-reversion-rsi-bollinger-new`: 1h has 20 out-of-sample trades. 15m and 5m have enough trades and lose money.
- `momentum-relative-strength-new`: 1h uses all 529 cached symbols as peers. 15m and 5m use only the 10 basis symbols. Walk-forward was not executed, so the gate cannot pass. 15m out-of-sample is a small, unstable sample with a negative in-sample.
- `funding-basis-new`: basis exists for 10 symbols. The directional rule produced no trades, including 80 empty walk-forward windows per timeframe. Hedged mode stays `NO_TRADE`. This is not a completed funding strategy test.
- `market-regime-new`: filter only. Zero orders on BTC in each timeframe.

## Build and tests

`dotnet build TradingPlatform.slnx` exits 1. `TradingPlatform.Api` pid 21256 locks `src/TradingPlatform.Api/bin/Debug/net10.0`. That is a file lock, not a compiler error. The process was left running.

Isolated builds and tests that completed:

| Command | Result |
|---|---|
| `dotnet test tests/TradingPlatform.UnitTests/TradingPlatform.UnitTests.csproj` | 630 passed, 0 failed |
| `dotnet test ... --filter ProductionNewStrategyTests` after the momentum cache fix | 30 passed, 0 failed |
| `dotnet test ... --filter CausalReplayEquivalenceTests` after that fix | 3 passed, 0 failed |
| `dotnet test tests/TradingPlatform.BacktestingTests/...` | 85 passed, 0 failed |
| `dotnet test tests/TradingPlatform.TradingTests/...` | 4 passed, 0 failed |
| `dotnet test tests/TradingPlatform.ResearchTests/...` | 183 passed, 0 failed |
| `dotnet test tests/TradingPlatform.IntegrationTests/...` | 25 passed, 0 failed |
| `dotnet build tools/TradingPlatform.StrategyResearch/...` | succeeded, 3 pre-existing CS8604 warnings in `StrategyMarketContext.cs` |

The 630-count unit run was before the last momentum cache edit. The production-new and causal filters were run again after that edit.

## Paper and Testnet

Configuration tests load API and Workers `appsettings.json`, `appsettings.Live.json`, and `appsettings.Shadow.json`. `Trading:ProductionNew:LiveEnabled` and `PaperEnabled` are false. Strategy flags are false or absent. API Live still has `Trading:LiveTradingEnabled=true` for other features. That flag alone does not enable these six strategies.

No Testnet order was sent. No Testnet credential was loaded. Paper fills on an exchange were not exercised. To opt in later, set `Trading:ProductionNew:PaperEnabled` and one strategy flag, keep `LiveEnabled` false, and use `Trading:Venue` of `Shadow` or `Testnet` with the existing testnet URLs. Do not put API keys in appsettings. Do not set the seeded `AllowLive` profile to true.

## Reproduce

```text
dotnet run --project tools/TradingPlatform.StrategyResearch/TradingPlatform.StrategyResearch.csproj -- --production-new
```

Output: `artifacts/strategy-research/production-new/2026-10-16/summary.json`, plus `symbols-{1h,15m,5m}.jsonl` and `walkforward-{1h,15m,5m}.jsonl`.

## Safety

`LiveEnabled` was not set to true. Strategy `AllowLive` was not set to true. No real order was submitted. No leverage or account setting was changed. No API key was written into source or this report.

## Remaining blockers

- Stop `TradingPlatform.Api` pid 21256 before a default-folder solution build, or keep using a separate output directory.
- Momentum reset walk-forward was not executed.
- Basis, mark, and index history exist for 10 symbols. A broader funding test needs those series for more contracts.
- The live bot still does not load funding, mark, or index into the strategy context, so `funding-basis-new` stays `NO_TRADE` in the live cycle.
- Spot hedging is not implemented.
- Survivorship of listings is not corrected.
- The portfolio overlay is diagnostic. It is not a mark-to-market of one live account.
- Cache ends 2026-09-30.
- Exchange paper and Testnet were not run. They need an explicit paper flag and Testnet credentials, and they must not use the live endpoint.
