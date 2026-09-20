# Strategy audit report — Model B metrics fix

Factual historical simulation only. **Not** a profit forecast. Nothing here enables LIVE.
Parameters, templates, and Model B methodology are unchanged. LIVE remains disabled.

This file is the **metrics-fixed** record. The previous universe write-up is **PRE-METRICS-FIX**:

`docs/strategy-audit-report-model-b.md`

Do not reuse that file’s universe-level LONG/SHORT profit factors (including RSI 4.70 / 4.01).

---

## 1. Bugs fixed

| Bug | Old behavior | Fix |
| --- | --- | --- |
| Universe LONG/SHORT PF | Trade-count weighted mean of per-book PFs | `sum(PnL>0) / abs(sum(PnL<0))` from exact side totals |
| Combined PF | Same definition in intent, but reconstructed from rounded win rate × average win/loss after `Strip()` | Persist `PnlTotals` (gross positive/negative PnL, W/L/Z counts) through `Strip()` |
| PF = 99 | No-loss books stored as 99 and leaked into median/best/regimes | `N/A` / `Infinity` / numeric. Never 99 |
| Return % | `sum(net) / 10,000` treated as one account | Equal-book return A + per-book distribution B |
| Drawdown | Max of per-book DDs labeled as if portfolio | Per-book mean/median/p90/p95/max. Portfolio DD = N/A unless aligned equity exists |
| Sharpe / Sortino / Calmar | First book’s Sharpe could remain on the merge | Universe values = N/A (no common time series after merge) |
| Walk-forward | Empty windows as PF=0 dominated the median; no-loss as 99 | Empty = N/A; no-loss = Infinity; stats on non-empty windows |
| IS / Validation / OOS | Same merge path as Combined, including the PF bugs | Same `PnlTotals` operator on every slice |

Execution, fees, slippage, strategy logic, and frozen parameters were not changed.

---

## 2. Exact formulas

**Unit of observation (PF, net, fees, expectancy):** closed `ReplayTrade`. Costs are already inside `PnL`.

```
PF = sum(PnL > 0) / abs(sum(PnL < 0))
```

Same operator for LONG, SHORT, Combined, IS, Validation, OOS.

| Case | Rendered PF |
| --- | --- |
| No trades | N/A |
| Trades, no losses, some wins | Infinity |
| Trades, no wins (including all-zero PnL) | 0 |
| Wins and losses | W / \|L\| (8 decimal places) |
| Stripped artifact without totals | unavailable (not approximated) |

**Weighting**

- PF, net, fees, expectancy, trade counts: **trade-weighted** (sum of money / counts).
- Return A: **equal-book** `sum(net) / (included books × initial balance)`. Empty books count as 0 return.
- Return B: **per-book** mean, median, positive-book %.
- Drawdown (reported): **per-book** distribution. Equal-weight normalized portfolio DD requires aligned equity curves starting at 1.0; those curves are stripped after merge, so universe portfolio DD = **N/A**.
- Sharpe / Sortino / Calmar (universe): **N/A**.

These are independent 10,000 USDT Isolated books (528 coins × 3 timeframes = 1584 books). They are not one portfolio.

**Costs**

- `ReplayTrade.PnL` = direction × (exit − entry) × qty − entry fee − exit fee.
- Slippage is inside entry/exit prices (0.02% default). Not subtracted again.
- Gross after slippage before fees = Net + Fees.
- Slippage vs mid cannot be reconstructed from stored trades. Not estimated.
- Funding: `EXCLUDING_FUNDING`.

Mandatory identity (tests):

```
Book A: +100, +50, −75 → PF = 150/75 = 2
Book B: +10, −100     → PF = 10/100 = 0.1
Combined PF           = 160/175 ≈ 0.91428571
NOT (2 + 0.1) / 2     = 1.05
```

---

## 3. Which existing Model B metrics remain valid

From the PRE-METRICS-FIX job (`job-b`, 1584/1584 datasets, IndicatorModel=B), these **sums** are still usable. They do not require the trade list:

| Template | Combined net (USDT) | Fees (USDT) | Trades |
| --- | ---: | ---: | ---: |
| Bollinger Reversion | −2,267,402.32 | 908,532.01 | 254,854 |
| Donchian Breakout | −14,141,328.58 | 2,913,228.84 | 2,533,889 |
| EMA RSI Trend | −6,264,540.59 | 1,885,570.24 | 680,799 |
| MACD Trend | −10,985,370.48 | 3,895,606.99 | 2,282,011 |
| RSI Pullback | −186,656.92 | 35,807.98 | 9,066 |

Also valid: per-side **trade counts**, **net/expectancy** (they reconciled with Combined in the audit), IS/Validation/OOS **net and trade counts**, implementation/look-ahead/repaint notes, `VALIDATION_PENDING`, LIVE off.

**Equal-book return A** can be computed from those nets without a rerun, assuming every finished dataset is one 10,000 USDT book (1584 books):

`return A = combined net / (1584 × 10,000)`

| Template | Invalid old “return” (`sum(net)/10,000`) | Equal-book return A |
| --- | ---: | ---: |
| Bollinger Reversion | −22,674.02% | −14.3144% |
| Donchian Breakout | −141,413.29% | −89.2761% |
| EMA RSI Trend | −62,645.41% | −39.5488% |
| MACD Trend | −109,853.70% | −69.3521% |
| RSI Pullback | −1,866.57% | −1.1784% |

Per-book mean/median/positive-book % **cannot** be recovered from the stored checkpoint (no per-book return list).

---

## 4. Which old metrics were invalid

Do not use again:

- Universe LONG/SHORT PF (RSI 4.70 / 4.01 is the exhibit; it mixed averaged PFs and the 99 cap).
- Combined / IS / Validation / OOS PF as published, if treated as exact (reconstructed from rounded summaries after `Strip()`).
- Combined return % as a portfolio or single-account return.
- Maximum drawdown as a portfolio DD (it was max-of-max; Donchian 1,567% is a per-book spike, not a book of books).
- Any universe Sharpe copied from a single replay.
- Walk-forward median PF (empty windows as 0; best PF 99).
- Regime median PF (same 0 / 99 contamination).

---

## 5. Can exact corrected PF be produced from existing artifacts?

**No.**

`artifacts/strategy-validation-cache/job-b/checkpoint.json` (111 MB, 1584 done keys):

- `"Trades":[]` (stripped)
- no `Totals` / `PositivePnlSum`
- `"ProfitFactor":99` present

Exact PF needs gross positive and gross negative PnL (or the trade list). Those were discarded. Reconstructing W/L from rounded win rate × average win/loss is the bug this fix removes. This report does **not** do that.

Equal-book return A above uses stored **net** only. That part is exact under the 1584 × 10,000 assumption.

---

## 6. Full Model B rerun required?

**Yes, for exact universe PF, per-book return/DD distributions, walk-forward PF without 0/99, and regime trade-level PF.**

The next job must persist `PnlTotals` (and per-book return/DD lists) through `Strip()`. It does **not** need to store millions of trades.

This task did **not** start that job.

After a rerun, write results here (or a successor file). Do not overwrite the PRE-METRICS-FIX markdown.

---

## 7. Tests passed

`TradingPlatform.BacktestingTests`: 43 passed (includes `ValidationMetricsAggregationTests`).

`CausalReplayEquivalenceTests`: 3 passed.

Covered:

1. LONG PF  
2. SHORT PF  
3. Combined PF  
4. No-loss → Infinity, not 99  
5. No-trade → N/A  
6. Zero-PnL trades counted, excluded from W/L sums  
7. Multiple books  
8. Unequal trade counts (150/75 vs 10/100 → 160/175, not 1.05)  
9. Equal-book return  
10. Normalized equal-weight DD ≠ max-of-max  
11. Walk-forward empty windows excluded from PF median  
12. Fees inside net exactly once  
13. Slippage in fill prices, not subtracted again  
14. LONG + SHORT aggregation  
15. No 99 sentinel on merge or walk-forward  

---

## 8. Status

All five templates remain in the catalog. None were deleted. None are auto-started. LIVE is not enabled.

Current validation status remains `VALIDATION_PENDING`.
