# Alpha Discovery Report (Phase 2)

Date: 2026-10-05. Scope: a systematic search for crypto-specific edges in Binance USD-M perpetuals. Every result in this report is research only. No order was placed by this work, no risk control was changed, and the sealed out-of-sample (OOS) window was not opened.

Artifacts: `artifacts/research/alpha-discovery/`. Every number below comes from those files.

---

## 1. Executive Summary

**Verdict: C. NO VALIDATED EDGE FOUND.**

- **Coverage.** 13 families were run, 11 pre-registered and 2 added in phase 2b. They used 83 information-screen rows and 273 registered in-sample (IS) trials on an hourly panel of 674 coins, including 158 that are no longer trading, from 2022-01-01 to 2026-10-01.
- **Information exists; profit after costs does not.** Several signals carry real cross-sectional information: short-term reversal (IC t ≈ 26), BTC→alt catch-up (t ≈ 14) and a linear rank composite (t ≈ 17). None of them survives CONSERVATIVE costs in IS. **0 families had a net-positive plateau, so 0 configurations reached Validation and the sealed OOS window was opened 0 times.**
- **Why they fail: cost.** The signals that carry information turn over fast, and their break-even cost is about 1–5 bp per side. The model charges 13–27 bp per side at CONSERVATIVE, and the taker fee alone is 5 bp. The most cost-tolerant configuration, F volatility-compression breakout with a 72h hold, breaks even at about 19 bp per side. It is positive only at BASE costs (Sharpe +0.07/yr) and negative at CONSERVATIVE (−0.17/yr).
- **Funding premia are carry, not alpha.** Being long high-funding coins earns price drift (C, X), but the funding paid cancels it.
- **Engineering.** The StrategyEngine rule path was O(n²). It is now linear and checked against a prefix oracle: 3.49 s → 7 ms at 4,000 bars. Tests went from 851 to 894, all passing in Release.
- **⚠ Operational warning, found during this session and not caused by it.** The API in this working tree is running in **Live** mode against the real Binance account. The default launch profile was switched to `ASPNETCORE_ENVIRONMENT=Live`, and `appsettings.Live.json` sets `LiveTradingEnabled: true` with the kill switch off. The log shows 17 open positions and 34 orders on a wallet of about 132 USDT. These files were created outside this research work (18:17–18:28 local); I did not modify or stop anything. Phase 1 and this phase both found **no strategy with a validated edge**, so the live bots are trading strategies that the evidence says lose money after costs. Whether to stop them is the operator's decision. See section 23.

## 2. Starting State

Phase 1 is documented in `docs/engineering/FULL_OPUS_AUDIT.md`.

- **Strategies.** 41 strategy/timeframe rows were backtested on 150 coins. 39 were REJECTED, 1 was RESEARCH with 9 trades, and 2 were untestable. None reached PROMISING, and none had positive gross minus fees plus funding in both IS and Validation.
- **Research framework.** It provided cost profiles (BASE / CONSERVATIVE / STRESS, with the corrected daily impact model), an experiment registry, Deflated Sharpe (DSR), Probabilistic Sharpe (PSR), Benjamini–Hochberg (BH), a sealed OOS vault, perturbation, block bootstrap and Monte Carlo.
- **Data.** The local caches covered about 529 coins. Open-interest history covered only about 40 days. Delisted coins were absent, so the universe had survivorship bias.
- **Engine.** `StrategyEngine.EvaluateAt` for rule strategies re-evaluated the whole prefix at each bar, which is O(n²).
- **Tests.** 851 passing.

## 3. Available Data

All data came from the Binance public archive (`data.binance.vision`), monthly and daily USD-M files, including delisted contracts.

| Dataset | Files | Failed | Coverage |
|---|---:|---:|---|
| 1h klines (OHLC, quote volume, taker-buy quote volume) | 19,320 | 0 | 2022-01 → 2026-09 |
| Funding rate history | 18,172 | 0 | 2022-01 → 2026-09 |
| Daily `metrics` (OI value, top-trader and global long/short ratios, taker ratio; 5-minute prints) | 420,255 | 0 | 2024-01 → 2026-09 |

Price panel (`artifacts/data/vision/panel-1h.bin`, fingerprint `8f91419288bcadcc`):

- **Size:** 674 coins × 41,616 hours, with 13.89M bars present and 2.46M funding settlements.
- **Taker-buy volume** is non-zero on 90.4% of bars.
- **Survivorship:** 158 coins are no longer trading, and 156 of them were eligible at some point (for example LUNA, FTT, SRM, MATIC, FTM, EOS, WAVES, MKR). 665 coins were eligible on at least one day.
- **Eligible coins per month:** 134 in 2022-01, peaking at 336 in 2025-08, and 161 in 2026-09.

The full list is in `data-summary.json`.

## 4. Data Limitations

- **No liquidation feed.** Binance's public archive has no historical liquidations. Family E uses price/volume shock proxies only.
- **No order book or real spreads.** Half-spread is a liquidity-bucket constant (Major 0.5 bp, Mid 2 bp, Small 5 bp), and impact is daily vol % × √(Q/V), capped at 0.5%. Maker execution and queue position are not modelled; every fill is taker.
- **Positioning data starts in 2024.** Open interest and long/short ratios exist only from 2024-01. Family D therefore uses its own shorter split (section 5), and its IS is 602 days.
- **1h resolution.** Intrabar paths are unknown. Signals are formed on closed bars and executed at the next bar's open price proxy (the close of the signal bar), plus one more bar of delay under STRESS.
- **News.** The collector ran about once a day, and 82% of stories arrived more than 60 minutes late (phase 1). It is documented in section 20, not tested.
- **Funding.** The archive gives the realized rate per settlement. Predicted funding is not available historically.
- **Delisting.** If a held coin's bars stop, the position is closed at the last available close. The real exit could be worse, so delisted-coin results are optimistic. 0–87 delist exits occurred per configuration.

## 5. Research Protocol

1. **Pre-registration before any run.** `preregistration.jsonl` has 237 lines (families A–K, mechanisms, data, the grids, and the stop / sign / plateau rules), with body hash `2d998bfbccfb9b59`. `preregistration-2b.jsonl` has 28 lines (families X and M), with body hash `81c1fd460bdc4fe0`. Both were written before their families ran, and the runner refuses to run if a body hash changes.
2. **Splits.** All families except D use days 0..1734 of the panel. Family D uses its own split from 2024-01-01 (day 730).

   | Window | A–C, E–M, X | D |
   |---|---|---|
   | IS | days 0–1040, 2022-01-01 → 2024-11-05 | days 730–1332, 2024-01-01 → 2025-08-24 |
   | Validation | → day 1387, 2025-10-18 | → day 1533, 2026-03-13 |
   | Sealed OOS (`OosVault`) | → 2026-10-01 | → 2026-10-01 |

   Reading the OOS window requires an `OosTicket` that matches the split; this is enforced in code and tested.
3. **Point-in-time universe.** A coin is eligible on day d if, using only days d−30..d−1, it has ≥ 30 days of history, ≥ 20 bars per day and a median daily quote volume ≥ $5M. Family X uses ≥ $50M, which leaves about 59 coins.
4. **Causal features only.** Prefix sums are computed on closed bars. Normalisations such as z-scores, trailing medians and beta use trailing windows only. Tests check that no feature changes when future bars change.
5. **Stages per family.**
   - **IS information screen:** Spearman IC with t-stats, plus a skip-1 variant that guards against bid-ask bounce. Event families use excess return over the equal-weight market with t-stats clustered by day.
   - **Stop rule:** a family with no screen row at |t| ≥ 2 stops.
   - **Sign rule:** if the strongest row has t ≤ −2 against the hypothesis, the whole grid is also run flipped, and every flipped point counts as a trial.
   - **IS grid:** coarse, run at CONSERVATIVE costs, with every point registered in `experiments.jsonl`.
   - **Plateau choice:** the chosen point must be positive and its grid neighbours must also be positive.
   - **Validation:** one run per cost profile, followed by ±20% neighbours and a block bootstrap.
   - **Gates:** DSR (family and all-trial), BH-FDR at q = 0.10, and `ResearchVerdict`.
   - **OOS:** only for PROMISING.
6. **Acceptance gates (unchanged from phase 1).** PROMISING requires all of: profit factor > 1.15; DSR > 0.9; breadth > 55%; stable neighbours; ≥ 30 trades; positive net under CONSERVATIVE and STRESS; a BH discovery; bootstrap loss probability < 10%; and top-coin share < 50%.
7. **Accounting.** Net = gross price PnL − fees − slippage/impact − funding paid. The book is $100k at gross exposure 1. Every result is reported at portfolio level, including beta and correlation to BTC, breadth, turnover and concentration.

## 6. Alpha Families Tested

| ID | Family | Priority | Hypothesis (pre-registered) | Outcome |
|---|---|---|---|---|
| A | Cross-sectional momentum | P0 | Past winners keep outperforming (raw, vol-adjusted, residual-to-BTC; 4h–30d) | Opposite sign in the screen; sign flipped by rule; REJECTED IN IS |
| B | Cross-sectional reversal | P0 | Short-term losers outperform (4h–3d; raw, residual, low-volume) | Strong information; REJECTED IN IS (costs) |
| C | Funding and price divergence | P0 | Crowded high-funding coins underperform; funding/price divergence predicts reversal | Opposite sign; flipped; REJECTED IN IS |
| D | Open interest and positioning | P0 | OI build-up with price moves, crowd positioning and taker ratio predict returns | REJECTED IN IS |
| E | Shock / cascade proxy | P1 | Large vol-scaled shocks with volume spikes revert (liquidation proxy) | Continuation negative; flipped; REJECTED IN IS |
| F | Volatility regimes | P0 | Low-vol anomaly; compression breakouts; shock reversion | REJECTED IN IS (best is positive only at BASE) |
| G | Market-regime conditioner | P0 | Gating screened signals by BTC trend, breadth, dispersion or funding improves them | REJECTED IN IS |
| H | BTC → alt lead/lag | P0 | After a BTC impulse, lagging alts catch up | Strong information; REJECTED IN IS (costs) |
| I | Volume and taker-flow anomalies | P1 | Abnormal volume or taker imbalance predicts continuation | Contrarian sign; flipped; REJECTED IN IS |
| J | Breakout quality | P1 | Breakouts with volume, range and taker confirmation follow through | STOPPED at the screen |
| K | Multi-timeframe | P1 | Higher-timeframe trend alignment improves 1h entries | STOPPED at the screen |
| X | Liquid-universe cross-section (2b) | P1 | Families A/C/F restricted to coins ≥ $50M/day, with lower costs | REJECTED IN IS |
| M | Linear rank composite (2b) | P1 | Equal-weight mean of six IS-screened ranks | Strong information; REJECTED IN IS |
| L | News | — | — | Documented only (section 20) |

## 7. Feature Catalog

The full catalog is in `artifacts/research/alpha-discovery/feature-catalog.md`. It gives the definition, window, timestamp semantics and missing-data rule for each feature. All features are computed from bars that closed at or before the decision time t.

- **Returns:** raw return over L hours; vol-adjusted return (return ÷ trailing hourly vol × √L); residual return against BTC, using a beta from the prior 720h of daily returns.
- **Volatility:** trailing hourly vol; average true range; bar range; compression ratio (short-window range ÷ long-window range).
- **Funding:** funding sum over 24h and 7d; funding z-score; funding minus price-move divergence.
- **Flow:** taker-buy imbalance (taker buy quote ÷ quote volume − 0.5); volume ratio (short-window ÷ long-window quote volume).
- **Positioning (metrics, from 2024):** OI value change over L hours; z-scores of the top-trader and global long/short ratios; taker long/short ratio. The rule is that the last 5-minute print strictly before the bar close is used.
- **Structure:** prior high and low; SMA; breakout distance.
- **Market state:** eligible coins; breadth (share of coins up over 168h); cross-sectional dispersion; mean return; median 24h funding; BTC impulse (|BTC return over L| ÷ (30-day hourly vol × √L)).
- **Composite (M):** the equal-weight mean of six cross-sectional ranks, requiring at least four to be present. Signs come from the IS screens:
  - −vol-adjusted return over 168h;
  - −volatility over 720h;
  - −taker imbalance over 24h;
  - −(vol-adjusted return over 72h − funding z);
  - +funding sum over 168h;
  - −volume ratio over 168h.

## 8. Cross-Sectional Results

All results are on IS. "SR" means the annualised Sharpe of daily net returns at CONSERVATIVE costs. Portfolios are quantile long/short, dollar neutral, with overlapping sleeves.

**A: momentum.**

- **Screen:** every momentum row has a *negative* IC. 72h→24h has t = −10.6; vol-adjusted 168h has t = −12.6; 1h→1h has t = −52.9, or −17.6 with skip-1. In this market, at these horizons, cross-sectional momentum is reversal.
- **Grid:** the sign rule flipped the grid, giving 56 points.
- **Best:** `A.raw-1d.hold168.lb720.q0.2.sign-1.step24` with SR −0.25, gross $14,467, fees $5,894, slippage $13,885, funding paid $6,502, net −$11,814, MaxDD 30.7%, 5,422 entries, beta to BTC 0.04, breadth 52%.

**B: reversal.**

- **Screen:** −ret 4h→4h has IC t = 25.7, or 14.6 with skip-1, so the effect is not only bid-ask bounce. Quantile spread is about 3.6 bp per 4h.
- **Grid:** 27 points.
- **Best:** `B.raw.hold24.lb24.q0.2.sign1.step4` with SR −6.29, gross $13,926 against fees $77,518 and slippage $188,976, net −$259,147, turnover 1.49× book per day, 85,212 entries.
- The 4h-hold variants produce about $116k gross, but turnover is about 9× per day.

**X: liquid universe (≥ $50M/day, about 59 coins).**

- **Screen:** low-vol has IC t = 4.5.
- **Best:** `X.liquid-low-vol.hold168.lb720` with SR −0.24, beta −0.22, correlation to BTC −0.41.

**M: rank composite.**

- **Screen:** IC t = 16.9 at 24h and 9.5 at 72h.
- **Best:** `M.rank-composite.hold168.liq1` with SR −1.00. Gross is only $2,120 and funding paid is $29,017, because the composite ends up long high-funding coins.

## 9. Funding and Open-Interest Results

**C: funding.**

- **Screen:** −funding 7d has IC t = −3.0, so high funding predicts *higher* returns, the opposite of the hypothesis. Funding/price divergence has t = −9.4.
- **Grid:** the sign rule flipped it, giving 46 points.
- **Long high funding:** `C.carry7d.hold24.lb168.q0.1.sign-1` earns about $62k gross price drift (gross SR 1.06) but pays $71k in funding.
- **Best net:** `C.carry7d.hold168.lb168.q0.1.sign1` with SR −0.38. It is short high funding and receives $51,933 in funding, but loses $36,494 on price.
- **Reading:** the funding premium and the price drift roughly offset. This is the expected carry equilibrium, not an exploitable edge.
- **X funding-level (liquid):** gross $59k, funding $52k.

**D: open interest and positioning (2024-01 → 2025-08 IS, 602 days, 262 coins).**

| Screen row | IC t | skip-1 t |
|---|---:|---:|
| OI change 24h → 24h | −3.03 | −2.14 |
| OI change 72h → 72h | −2.02 | −2.20 |
| −sign(ret24) × OI change 24h | +5.78 | +5.26 |
| −top-trader ratio z | +0.18 | +0.91 |
| −global long/short z | −6.41 | −4.70 |
| Taker long/short ratio 24h | +7.02 | +6.42 |

- **What the screen says:** a crowd that is long on the global ratio predicts *higher* returns, so the contrarian hypothesis is wrong in sign. Taker buying predicts continuation.
- **Sign rule:** the strongest row (taker ratio) agreed with the hypothesis, so no flip. 16 points.
- **Best:** `D.toptrader-contrarian.hold72.q0.2.sign1.step24` with SR −0.19, gross $27,487, fees $9,742, slippage $27,016, funding *received* $6,059, net −$3,212, MaxDD 14.4%, 12,484 entries.
- **Other configurations:** the OI-change and price/OI-quadrant configurations have *negative gross* (SR −3 to −10).

## 10. Market-Regime Results

**G:** 8 regime gates were applied to the best IS configuration of each of the 7 screened families with a viable grid, giving 56 trials. The gates are BTC above or below its SMA, breadth above or below 50%, and dispersion or median funding above or below its trailing median.

- No gated configuration is positive.
- **Best:** `G.F.compression-breakout|breadth-low` with SR −0.30, which is *worse* than the ungated F (−0.17).
- Gating cuts exposure, and therefore gross PnL, by roughly as much as it cuts cost. Where gates flip on and off often (dispersion, funding), it *adds* churn.
- Ex-post, the A momentum-reversal signal does least badly in BTC downtrends (SR −0.53 vs −0.83 in uptrends). That difference is within noise and is not a usable conditioner.

## 11. Volatility Results

**F:**

- **Low-vol cross-section:** IC t ≈ 4.9 at 168h, but the top-minus-bottom quantile spread is negative (−13.9 bp). The rank information sits in the middle of the distribution, not the tails.
- **Event screens:** compression breakout and shock reversion show no significant excess return (|t| < 0.6).
- **Best:** `F.compression-breakout.c0.5.hold72.sign1.step4` with SR −0.17, gross $23,649, fees $6,264, slippage $27,201, funding $155, net −$9,970, 1,371 entries, MaxDD 38.0%. Its event profit factor is 0.88.
- **Cost sensitivity:** this is the only configuration in the study that is net positive at BASE (SR +0.07). At about 0.12× book per day it has the lowest turnover, and it breaks even at about 19 bp per side.

## 12. Lead-Lag Results

**H: BTC → alt catch-up.**

- **Screen:** after a BTC impulse ≥ 1.5σ, the residual gap of an alt predicts its next 1–4h relative return. IC t is 12.9–13.9 at 1h and 5.3–6.5 at 4h.
- **Unconditional:** the 1h gap → 1h has IC t = −8.9, so the effect exists only after impulses.
- **Grid:** 12 points.
- **Best:** `H.catch-up.hold4.k2.5.lb4` with SR −4.96, gross $8,401 against $68,745 of cost, 33,881 entries.
- **Break-even:** about 1–2 bp per side, below any realistic fee level.

## 13. Volume and Liquidity Results

**I: volume and taker flow.**

- **Screen:**
  - taker imbalance 24h → 24h has IC t = −5.1, and 4h → 4h has t = −19.1, both contrarian;
  - volume-confirmed moves have t = −6.5;
  - abnormal attention (volume ratio) has t = −2.7 at 168h.
- **Grid:** flipped, 16 points.
- **Best:** `I.attention.hold168.q0.2.sign1.step24` with SR −0.72, gross −$8,361, net −$27,640.

**E: shock / cascade proxy.**

- **Screen:** continuation after a ≥ k-σ shock with a volume spike is negative (day-clustered t from −1.9 to −3.5), which is consistent with forced flows reverting.
- **Grid:** flipped, 16 points.
- **Duplicates:** "continue, sign −1" is mechanically identical to "fade, sign +1", so 8 of the 16 trials are duplicates. They were counted anyway, which makes the multiple-testing count more conservative, not less.
- **Best:** `E.fade.hold24.k5` with SR −0.73, gross $103,351, fees $32,146, slippage $133,216, funding $17,049, net −$79,061. Its event profit factor of 1.12 is still below the 1.15 gate.
- **Most cost-tolerant:** `E.fade.hold24.k3` has gross $215k and breaks even at about 10 bp per side. Shocks happen in small, illiquid coins where the modelled cost is about 24 bp.

## 14. Multi-Timeframe Results

- **K:** higher-timeframe trend alignment for 1h entries. Event excess t is +1.04 and −0.77. STOPPED at the screen, with no grid run and no trials.
- **J:** breakout quality, with volume, range and taker confirmation. Event t is between −1.49 and +0.80. STOPPED at the screen.

Both are negative results under a pre-registered rule, so they are not missing results. They are recorded in `families/J.json` and `families/K.json`.

## 15. Portfolio-Level Results

- **Market neutrality.** Every cross-sectional configuration is close to market-neutral by construction: beta to BTC between −0.22 and +0.05, and correlation between −0.41 and +0.18. Losses are therefore not hidden market exposure.
- **Breadth** is 19–59% of coins traded with positive net, below the 55% gate everywhere except X liquid low-vol (59%), which is net negative.
- **Concentration.** Top-coin share is defined only for net-positive results, and no configuration had one.
- **Turnover** runs from 0.05× (F low-vol) to 9.1× book per day (B 4h).
- **Combination.** With no IS-positive component, a combination portfolio has nothing to combine. Family M is the pre-registered combination test: a linear composite of six screened ranks. It raises IC to t = 16.9 but cannot overcome cost and funding.
- **Not built:** ML models were not built. The protocol requires that raw features first produce IS-profitable signals, and none did. A model trained to predict returns better would still pay the same per-side cost at the same turnover.

## 16. Multiple-Testing Corrections

- **Trial count.** The registry records 273 distinct IS configurations under `alpha:*` (phase 2 + 2b), including flipped grids, G gates and E duplicates. The cross-trial variance of daily Sharpe is 0.153. Cost-diagnostic re-runs (`costcurve`) are registered as split `IS-diagnostic` and excluded from the count.
- **Not reached.** DSR (family-level and all-trial), PSR p-values and BH-FDR at q = 0.10 apply to Validation results. **No configuration reached Validation, so none was computed.** `gates.json` records 0 validated families and 0 OOS openings.
- **Effect of these corrections.** They only remove candidates. With 273 trials and this variance, the DSR hurdle on any surviving Validation Sharpe would be severe. No IS result came close to needing it, since the best IS net Sharpe was −0.17.

## 17. Robustness Results

- **Not run.** Neighbour perturbation (±20% on each parameter), the stability classification and the 2,000-draw block bootstrap are implemented and run automatically for every Validation configuration. Because no family produced a positive IS plateau, none was triggered.
- **Robustness checks inside IS:**
  - **Bid-ask bounce:** skip-1 IC was computed for every cross-sectional screen. The reversal and lead-lag information survives skipping a bar, at reduced strength.
  - **Cost curve:** BASE, CONSERVATIVE and gross results for the top gross configurations of each family are in `cost-sensitivity.json` and `break-even.md`. The conclusion, that costs exceed gross, holds at every profile except one configuration at BASE.
  - **Universe:** restricting to liquid coins (X), which lowers modelled cost to 13–14 bp, removes most of the gross as well.
  - **Sign:** where a hypothesis had the wrong sign, the flipped grid was tested. Both directions failed net.

## 18. Candidate Strategies

**None.** No configuration met even the first stage, a positive net plateau under CONSERVATIVE costs in IS. No candidate report was produced. The fields the user asked for per candidate are recorded in section 19 for the best configuration of each family.

## 19. Rejected Strategies

The best IS configuration per family is listed below, all at CONSERVATIVE costs. Every grid point is in `families/*.json` and `rejected-summary.md`. For all of them, Validation, stress, robustness and multiple-testing were not run, the OOS window is sealed, and the verdict is REJECTED (IS).

Dollar amounts are over IS on the $100k book. Funding is positive when paid and negative when received. "Entries" is the trade count. "PF" is the daily profit factor; for E and F the event profit factor is in brackets.

| ID | Universe / Hold | Entries | Gross | Fees | Slippage | Funding | Net | PF | SR/yr | MaxDD |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A.raw-1d.hold168.lb720.q0.2.sign-1.step24 | ≥$5M, 168h | 5,422 | 14,467 | 5,894 | 13,885 | 6,502 | −11,814 | 0.96 | −0.25 | 30.7% |
| B.raw.hold24.lb24.q0.2.sign1.step4 | ≥$5M, 24h | 85,212 | 13,926 | 77,518 | 188,976 | 6,578 | −259,147 | 0.37 | −6.29 | 93.0% |
| C.carry7d.hold168.lb168.q0.1.sign1.step24 | ≥$5M, 168h | 4,126 | −36,494 | 8,388 | 24,196 | −51,933 | −17,145 | 0.95 | −0.38 | 31.1% |
| D.toptrader-contrarian.hold72.q0.2.sign1.step24 | ≥$5M (2024+), 72h | 12,484 | 27,487 | 9,742 | 27,016 | −6,059 | −3,212 | 0.97 | −0.19 | 14.4% |
| E.fade.hold24.k5.mode-1.sign1.step1 | ≥$5M events, 24h | 12,496 | 103,351 | 32,146 | 133,216 | 17,049 | −79,061 | 0.87 (1.12) | −0.73 | 68.3% |
| F.compression-breakout.c0.5.hold72.sign1.step4 | ≥$5M events, 72h | 1,371 | 23,649 | 6,264 | 27,201 | 155 | −9,970 | 0.96 (0.88) | −0.17 | 38.0% |
| G.F.compression-breakout\|breadth-low | ≥$5M events, 72h | 1,340 | 17,039 | 5,945 | 25,752 | −360 | −14,298 | 0.91 | −0.30 | 30.9% |
| H.catch-up.hold4.k2.5.lb4.q0.2.sign1.step1 | ≥$5M, 4h | 33,881 | 8,401 | 19,948 | 48,797 | 169 | −60,514 | 0.13 | −4.96 | 45.6% |
| I.attention.hold168.q0.2.sign1.step24 | ≥$5M, 168h | 6,866 | −8,361 | 8,210 | 20,480 | −9,410 | −27,640 | 0.89 | −0.72 | 27.9% |
| X.liquid-low-vol.hold168.lb720.q0.2.sign1.step24 | ≥$50M, 168h | 792 | 4,408 | 3,477 | 5,814 | 14,468 | −19,351 | 0.97 | −0.24 | 53.0% |
| M.rank-composite.hold168.liq1.q0.2.sign1.step24 | ≥$50M, 168h | 3,953 | 2,120 | 10,207 | 15,859 | 29,017 | −52,963 | 0.86 | −1.00 | 50.8% |
| J (breakout quality) | — | — | — | — | — | — | — | — | — | STOPPED at screen |
| K (multi-timeframe) | — | — | — | — | — | — | — | — | — | STOPPED at screen |

Features, entry and exit rules: each ID encodes its parameters (lookback `lb`, quantile `q`, hold, rebalance step, sign, threshold `k` or compression `c`). The families are defined in `tools/TradingPlatform.StrategyResearch/AlphaDiscovery/AlphaFamilies.cs` and the pre-registration files.

- **Entry:** at the decision hour, take the quantile ranks (long top q, short bottom q) or the event trigger.
- **Exit:** after the fixed hold, or by a forced exit at the last close if the coin's data stops.
- **No stops or targets.** None were used, so the results measure the signal itself.

## 20. Missing-Data Research

These datasets could change the conclusion. They are listed in rough order of expected value.

1. **Real spreads and order-book depth (top-of-book snapshots or L2).** This is the binding constraint. If realistic small-coin taker costs are well below the modelled 20–27 bp, the most cost-tolerant signals (F compression at about 19 bp break-even, E fade at about 10 bp) could become viable. They need recording of `bookTicker` / depth for the universe for months before any test.
2. **Maker execution.** The signals with the strongest information (B reversal, H catch-up) need about 1 bp per side. Passive execution with maker rebates is the only way they could work, and it needs queue and adverse-selection modelling with tick-level data.
3. **Liquidation feed.** The `forceOrder` stream is not archived. Recording it forward would let family E test real cascades instead of proxies.
4. **Positioning before 2024.** OI and ratio history starts in 2024-01. The 602-day IS for D is short, and longer history would give a fairer test.
5. **News with low latency.** Phase 1 found 82% of stories more than 60 minutes late. A real-time collector is a prerequisite for any news study; family L was not tested.
6. **Predicted funding** (`premiumIndex`), recorded forward. Realized funding is a lagging proxy for crowding.

## 21. Performance Improvements

The full record is in `artifacts/research/alpha-discovery/benchmark.md`.

- **StrategyEngine rule path: O(n²) → O(n).**
  - Each indicator series is now computed once per candle list and read at the index.
  - Numeric thresholds use a constant series instead of a full-length array.
  - `BacktestReplay` always builds the causal cache.
  - At 4,000 bars, a replay took 3,492 ms before and 7 ms after. It is linear at about 1.5 ms per 1,000 bars up to 32,000 bars.
- **Latent look-ahead fixed.** The old rule path in `EvaluateAt` read `context.ClosedCandles` and ignored `index`. A caller that passed the full list would have seen future bars. The new path reads only up to `index`, and a test asserts this.
- **Parity.** `tests/TradingPlatform.BacktestingTests/RuleEngineParityTests.cs` adds 20 tests:
  - indicator causality;
  - indexed vs prefix evaluation at every bar for four definitions covering every indicator and comparison;
  - identical replay trades vs the prefix oracle, with a check that each definition trades;
  - future bars ignored.
- **Alpha panel engine** (`src/TradingPlatform.Research/Alpha`). The binary panel loads in about 1 s. The whole family D run took 39.7 s wall-clock, including parsing 420,255 metrics files; its screen plus 16 simulations took 1 s. 18 unit tests cover:
  - parsing and timestamp mapping;
  - feature and universe causality;
  - the PnL, cost, funding and STRESS-delay mechanics;
  - delist exits;
  - the OOS ticket guard;
  - IC screens never reading past the window.

## 22. Final Verdict

**C. NO VALIDATED EDGE FOUND.**

- **IS results.** In-sample, 273 pre-registered trials across 13 families found no configuration with positive net PnL under CONSERVATIVE costs. Nothing advanced to Validation, so none of the multiple-testing, robustness or OOS gates was reached. The sealed OOS window remains unopened.
- **The pattern is consistent.** Crypto perps show strong, statistically robust cross-sectional *information*: short-term reversal, post-impulse catch-up, and contrarian taker flow. It sits at horizons and turnover where realistic taker costs of 13–27 bp per side are 5–20× the gross edge. Slower signals have lower costs but also little gross.
- **Funding.** Funding-based price drift is offset by the funding paid.
- **No candidate status.** No result is labelled PROMISING or RESEARCH-candidate. The acceptance criteria were not loosened.

## 23. Recommended Next Steps

1. **Operational, urgent, and the operator's decision.** The API is currently running in Live mode with `LiveTradingEnabled: true`, the kill switch off, 17 open positions and 34 orders. Neither phase 1 nor phase 2 found any strategy with a validated edge.
   - Consider using the emergency stop / `FlattenAll`, or at least stopping new entries.
   - Then return the default launch profile to `Development` / Shadow.
   - The relevant files are `src/TradingPlatform.Api/Properties/launchSettings.json`, `src/TradingPlatform.Workers/Properties/launchSettings.json`, and `appsettings.Live.json` in the Api and Workers projects.
   - This research work did not change them.
2. **Use Shadow mode for forward data, not for alpha.** Run Shadow to record real `bookTicker` spreads, depth and the `forceOrder` liquidation stream for the eligible universe (section 20, items 1 and 3). After 3–6 months, rebuild the cost model from measured spreads. Then re-run only the pre-registered F compression and E fade grids, as a new registered phase so the trial count accrues.
3. **Maker-execution feasibility study.** Before any work on the fast signals (B, H), measure fill probability and adverse selection of passive quotes in Shadow. If the measured cost is not below about 2 bp per side, retire those families.
4. **Do not tune the rejected families further on this IS.** That would be data mining against a known-negative result. Any new family must be pre-registered and add to the trial count.
5. **Keep the OOS window sealed** until a configuration passes Validation and every gate in section 5.
