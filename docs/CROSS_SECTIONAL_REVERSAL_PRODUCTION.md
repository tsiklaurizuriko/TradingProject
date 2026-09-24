# Cross-sectional reversal production integration

Research factor. Not a validated strategy. The source audit is `docs/CROSS_SECTIONAL_ALPHA_RESEARCH.md`. The first implementation note is `docs/CROSS_SECTIONAL_REVERSAL_IMPLEMENTATION.md`.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false. Production approval = INSUFFICIENT_EVIDENCE.

## 1. Architecture

The path stays Market Data, Strategy Engine, the existing Risk Engine, Isolated sizing, Execution, position management, and reporting. No second bot, risk engine, or execution engine was added.

A cross-sectional rank is not a single-symbol indicator. The single-symbol preview returns no order. Candidates are capped by `CrossSectionalRiskPolicy`, then the existing occupancy check and `RiskEngine.Evaluate` still have to pass. This phase approves no order.

## 2. Signal definition

`return_15m = close[t] / close[t-1] - 1`. `return_1h = close[t] / close[t-4] - 1`. Top decile is SHORT. Bottom decile is LONG. The audit's long-top / short-bottom table is the measurement book, not the signal.

## 3. Universe

USD-M USDT perpetuals on the BTC 15-minute clock. Own closed bar required. 96 own bars required. BTCDOM and stablecoin bases excluded. Minimum 30 names. No future membership and no filled-in missing bars.

## 4. Ranking

Percentile rank on names present at that timestamp. Ties break by symbol name. Ranking version `cross-section-v1`. Manifest `fe81edf56d4af31cc0db0a215c09a6245437ad2a66eeb1f6caec11afb0ef247e`.

## 5. Symbol robustness

Descriptive labels exist (`CONSISTENT`, `MIXED`, `WEAK`, `INSUFFICIENT_DATA`). They are not a trading filter and were not fit on OOS. A full 527-name leave-one-out on the cache was not executed in this pass, so that table is not invented. See `docs/CROSS_SECTIONAL_REVERSAL_SYMBOL_ROBUSTNESS.md`.

## 6. Turnover

The audit's non-overlapping book already showed about 0.8 of each short-horizon leg replaced. Partial rebalance, hysteresis, and retention bands were not promoted.

## 7. Execution cost

Model B remains fee 0.04% and slippage 0.02% per fill, 12 bp round trip per fully replaced leg. Cost multipliers are a research formula only. See `docs/CROSS_SECTIONAL_REVERSAL_EXECUTION_RESEARCH.md`.

## 8. Funding

Settled funding is not on the 527-name universe. It is DATA_UNAVAILABLE. It was not filled in, and long and short funding were not assumed to cancel.

## 9. Risk management

`CrossSectionalRiskPolicy` caps total cross-sectional planned risk, long slots, short slots, total slots, cluster count, directional risk, and basket heat. Extra candidates are rejected with an explicit reason. Priority is current rank only: lowest return first for longs, highest return first for shorts. See `docs/CROSS_SECTIONAL_REVERSAL_RISK_MODEL.md`.

## 10. Position sizing

Default is EQUAL_RISK. Planned risk per position is the fixed `MaxPerPositionRiskPercent` (0.25). A smaller basket does not raise that amount. Signal-strength weighting is off.

## 11. Isolated margin

Margin mode stays Isolated. Default leverage cap is 3x. `PortfolioRisk.IsolatedMargin` is still notional / leverage. Cross margin was not added.

## 12. Rebalancing

The rebalance record is specified as clock, universe, rank, accepts, rejects, size, and stop check. This process does not hold a live snapshot, so the rebalance endpoint returns INSUFFICIENT_DATA instead of a fabricated book.

## 13. Portfolio correlation

A full causal correlation matrix was not built for 527 names in this pass. Cluster limits accept a caller-supplied cluster id. Without that id the name is UNCLUSTERED and is not given a fabricated cluster.

## 14. Failure modes

Fewer than 30 names rejects every candidate with INSUFFICIENT_DATA. A missing own bar drops that name. An occupied coin is SAME_SYMBOL_OCCUPIED. No price, funding, or open interest is invented.

## 15. Paper mode

Paper orders stay blocked. `POST .../paper/enable` returns a conflict and does not arm paper while approval is INSUFFICIENT_EVIDENCE.

## 16. Live mode

Live orders stay blocked. Global live off, strategy live off, missing VALIDATED_FOR_PAPER, and missing LIVE_APPROVED each stop activation. The UI checkbox cannot bypass that.

## 17. Approval gates

States are RESEARCHING, INSUFFICIENT_EVIDENCE, VALIDATED_FOR_PAPER, PAPER_APPROVED, LIVE_APPROVED, LIVE_DISABLED. The current state does not auto-transition.

## 18. Configuration

`Trading:CrossSectionalReversal` in appsettings. Enabled, PaperEnabled, and LiveEnabled are false. Global `LiveTradingEnabled` is false. Existing risk profiles were not edited.

## 19. Monitoring

`GET /api/strategies/cross-sectional-reversal` reports approval, paper, live, and the risk caps. Ranking and rebalance reads do not invent a book.

## 20. Known limitations

The existing Risk Engine stores `MaxDailyLossPercent` and does not apply it to Isolated entries. That comment is on `RiskProfile`. This pass did not change that behavior, so Frozen Five and the other Isolated books keep the same daily-loss path.

When a live protective stop fails, `BotEngine` logs that automatic close is disabled and the stop will be retried. An unprotected position can remain open during that retry. This pass did not change that path. Cross-sectional live orders are blocked before that path is reached.

No Binance order was sent.
