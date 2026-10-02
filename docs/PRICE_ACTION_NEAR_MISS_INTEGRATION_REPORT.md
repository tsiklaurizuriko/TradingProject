# Price Action near-miss integration

Status: NEAR_MISS. Not VALIDATED_FOR_PAPER. Paper and LIVE stay off.

The frozen Phase 8 definitions were not retuned. Thresholds, ATR, BOS, CHoCH, swing length, relative volume, timeframe hierarchy, stop, take-profit, leverage, and risk percent are unchanged. Out-of-sample numbers were not used to edit a definition.

## Safety

At implementation:

- Global LIVE trading stays off (`Trading:LiveTradingEnabled` = false).
- Price Action stays off (`Enabled`, `PaperEnabled`, `LiveEnabled`, `AllowLive` = false).
- Each near-miss candidate stays off (`Candidates.*.Enabled` = false).
- Scalping stays off (`Enabled` and `AllowLive` = false).
- Seeded strategy rows are disabled. Nothing is started.

A near-miss order can be sent only when the strategy row is enabled, the Price Action master switch is on, that candidate is on, and the mode switch is on. LIVE also requires the global LIVE switch. The order then uses the existing signal, risk, Isolated margin, sizing, Binance or paper execution, and protective stop path. There is no second execution path.

## Audit

Phase 8 hypotheses audited: 25.

Selection rule, applied in table order:

- Validation trades >= 40 and validation profit factor > 1 and validation net > 0, or
- Out-of-sample trades >= 40 and out-of-sample profit factor > 1 and out-of-sample net > 0.

The Phase 8 rule that a contextual row must keep at least half of the baseline sample is not used here. That rule belonged to the strict research gate. Nothing in this basket is called validated.

Selected: 7. Rejected: 18.

| Hypothesis | Template key | Why selected | Why strict validation failed |
| --- | --- | --- | --- |
| CPA-SWEEP\|CONTEXTUAL\|5m | cpa_near_miss_sweep_contextual_5m | VAL n=88, PF 1.148, net +44.30 | IS PF 0.36 is below 0.90, so the pre-OOS gate failed |
| CPA-SWEEP\|STRICT\|5m | cpa_near_miss_sweep_strict_5m | VAL n=81, PF 1.256, net +67.57 and OOS n=78, PF 1.069, net +18.81 | OOS was opened, then the interesting bar failed: walk-forward had 2 trades, 2.0x cost PF was below 1, and one block held almost the whole sample |
| CPA-PULLBACK\|CONTEXTUAL\|5m | cpa_near_miss_pullback_contextual_5m | VAL n=52, PF 1.113, net +20.84 | IS n=24 is below 30 |
| CPA-WM\|CONTEXTUAL\|5m | cpa_near_miss_wm_contextual_5m | VAL n=514, PF 1.044, net +80.10 | IS PF 0.61 is below 0.90 |
| CPA-WM\|STRICT\|5m | cpa_near_miss_wm_strict_5m | VAL n=115, PF 1.016, net +6.35 | IS PF 0.71 is below 0.90 |
| CPA-COMPRESSION\|CONTINUATION\|5m | cpa_near_miss_compression_continuation_5m | OOS n=50, PF 1.082, net +14.08 | OOS was opened. This row is the family baseline, walk-forward had 2 trades, and the short side was not a robust edge |
| CPA-MTF\|STRICT\|5m | cpa_near_miss_mtf_strict_5m | VAL n=76, PF 1.033, net +8.76 | IS PF 0.62 is below 0.90 |

`CPA-WM|STRICT|5m` was not in the six names listed up front. The same numeric rule selects it, so it is in the basket. The other 18 fail the rule because profit factor is at or below 1, net is not positive, or the trade count is below 40 on both validation and out-of-sample.

Original in-sample, validation, and out-of-sample figures are the Phase 8 table in `docs/CONTEXTUAL_PRICE_ACTION_ALPHA_REPORT.md`. They are copied into `NearMissAudit` and were not refit.

## Configuration

`Trading:PriceAction` in `src/TradingPlatform.Api/appsettings.json`:

- `Enabled`: false
- `AllowLive`: false (existing key, still off)
- `PaperEnabled`: false
- `LiveEnabled`: false
- `Candidates.<template key>.Enabled`: false for each of the seven keys

Missing candidate config is treated as off. Scalping config is unchanged and off.

## Paper and LIVE

Paper runtime execution was removed. A near-miss candidate does not send a Binance order unless live submission is explicitly enabled. Sizing, Isolated semantics, and portfolio checks stay on the existing risk engine.

LIVE uses the existing market order, then the existing `STOP_MARKET` and `TAKE_PROFIT_MARKET` close-position orders. If the stop cannot be placed, the existing path leaves the position open and retries the stop. It does not add a new flatten. The global LIVE switch blocks near-miss LIVE even when Price Action LIVE is later turned on.

The strategy card preview does not invent an EMA signal. The running bot, once armed, evaluates `ContextualPriceActionSignals.AtLastClosed` on closed 1m, 3m, 5m, 15m, and 1h bars. A missing series stays missing. The signal is the last closed 5m bar. The following cycle sends the existing market order. That is the production form of next-open entry. Historical next-bar open prices are not a second fill model.

## Risk, collision, attribution

Same risk engine as the other bots: one Isolated coin, simultaneous slots, portfolio planned risk, consecutive-loss lock, margin and fee fit, and the exchange minimum size. The engine does not currently read `MaxDailyLossPercent`. That was left unchanged.

Same-symbol collision uses the existing one-position-per-coin claim. Near-miss rejects are labeled `RejectedSameSymbol`, `RejectedSlot`, `RejectedHeat`, or `RejectedPortfolioRisk`. The signal row is still stored.

Each near-miss signal stores hypothesis id, family, and signal time in `MetadataJson`. Each near-miss trade stores `HypothesisId`, `StrategyFamily`, and `SignalAt`. Open positions track max favorable and max adverse excursion and copy them onto the trade at exit. Frozen Five trades leave the hypothesis fields empty.

## UI

Strategies lists the seven rows with status NEAR-MISS, Paper off, and Live off. The enable button only marks the strategy row. It does not arm paper or LIVE and does not start a bot. Bots can select them. Start is rejected until the config switches above are on.

## Tests

- Default configuration is off, including every candidate in paper and LIVE.
- The audit counts 25 and selects the seven ids above.
- Frozen Five, scalping, and Phase 7 price-action keys do not overlap the basket.
- LIVE is rejected while the global LIVE switch is off.
- Paper is rejected while Price Action paper is off.
- A near-miss buy reaches `RiskEngine` and the reject labels match same-coin, slot, and portfolio heat.
- The paper connector is `PaperSimulator`. LIVE without a live factory throws and does not place an order.
- The strategy engine does not turn a near-miss template into an EMA signal.
- The last closed contextual bar matches the frozen book, and a later bar does not change the earlier signal.
- Occupancy, risk, template-key, trading, and contextual price-action tests passed.

Integration tests that need PostgreSQL were not started. The API was not started. No paper bot and no live bot was started.

## Confirmation

- LIVE = OFF
- PAPER = OFF for these strategies
- Price Action LIVE = OFF
- Scalping LIVE = OFF
- Frozen Five unchanged
- Risk Engine unchanged
- Execution path unchanged
- Isolated margin unchanged
- Portfolio risk unchanged
- Phase 7 price-action templates unchanged
- Phase 8 research numbers unchanged
