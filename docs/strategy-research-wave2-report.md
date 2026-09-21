# Model B Wave-2 strategy research report

Research screen only. Frozen five templates, FrozenRisk, LIVE, and Isolated LOW catalog numbers were not changed.
Replay uses Isolated LOW book sizing: **$1,000**, **0.5% risk**, **3x**, daily 3%, max 2 positions, consecutive-loss 5, cooldown 30.
When HonorSuggestedStops is on, stop distance sizes the Isolated quantity so dollar risk stays 0.5% (leverage-capped). Cross margin is not used.
Parameters were pre-registered. OOS was not used to retune. Full 528-coin × 3-timeframe (1,584-book) run is **not** launched from this screen.

## 1. Executive Summary

**NO ROBUST ALPHA FOUND**

Eight new mechanism candidates were screened on 10 liquid USDT-M perpetuals, 1h, 2024-09-18 → 2026-09-19, Isolated LOW, costs included. All eight are **REJECTED**. Combined PF is 0.49–0.73. Win rates sit near 33–37%, which is breakeven-or-worse at 2R after fees and slippage. 15m, 5m, walk-forward, and the 1,584-book universe were **not** launched: in-sample and validation already failed on 1h, and expanding the grid after seeing that would be data mining.

Prior Model B work on the same execution model (frozen five, 15 filter/native candidates, advanced alpha families A–G, funding Phase 4) also did not find a robust edge. Wave-2 tested **new mechanisms** plus **structural exits that match the entry**, which the existing 2%/4% book always overrode. Matching the exit to the entry did not create positive expectancy.

Classifications: ROBUST CANDIDATE=0, PROMISING=0, FRAGILE=0, REJECTED=8.

Do not enable LIVE. Do not mark VALIDATED_FOR_PAPER. Do not raise Isolated risk to manufacture a backtest win.

## 2. Candidate Table

| Strategy | Hypothesis | n | WR | PF | Exp | Mean book | Median book | OOS PF | OOS Exp | OOS WR | DD | Profitable books % | Class |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| W2-SWEEP-RECLAIM-001 | Stop-runs that immediately reclaim a confirmed swing with volume are li… | 3314 | 35.21% | 0.64698709 | -1.5615 | -17.25% | -13.48% | 0.61031735 | -1.9190 | 35.86% | n/a | 0.00% | REJECTED |
| W2-FAILED-BO-VOL-001 | A Donchian break that fails back inside on dying volume is a fade, not … | 807 | 36.18% | 0.64295356 | -1.7724 | -4.77% | -4.24% | 0.50150083 | -2.6552 | 29.63% | n/a | 10.00% | REJECTED |
| W2-SQUEEZE-EXP-001 | ATR-percentile compression stores energy; the first volume Donchian bre… | 2497 | 33.80% | 0.66426972 | -1.4443 | -12.02% | -7.78% | 0.70251449 | -1.3260 | 36.40% | n/a | 3.33% | REJECTED |
| W2-VOL-EXHAUST-001 | A high-volume small-body bar after a 3-bar run is exhaustion, not conti… | 1555 | 32.03% | 0.53626987 | -2.3622 | -12.24% | -8.92% | 0.50227977 | -2.7948 | 33.01% | n/a | 3.33% | REJECTED |
| W2-VWAP-EXT-001 | Session VWAP is a fair-value magnet after an ATR-normalized extension; … | 2062 | 33.80% | 0.71250079 | -1.2382 | -8.51% | -6.60% | 0.62728180 | -1.6771 | 31.38% | n/a | 6.67% | REJECTED |
| W2-BOS-PULLBACK-001 | After a causal break of structure, a pullback that holds the broken swi… | 8234 | 32.72% | 0.53579814 | -1.7601 | -48.31% | -43.23% | 0.49389344 | -2.2981 | 32.48% | n/a | 0.00% | REJECTED |
| W2-DISPLACE-001 | A ≥1.5 ATR displacement bar is informed flow; a later retrace into its … | 5335 | 37.43% | 0.72881653 | -1.0656 | -18.95% | -19.43% | 0.61128476 | -1.6385 | 34.21% | n/a | 0.00% | REJECTED |
| W2-REGIME-SWITCH-001 | Low realized-vol regimes fade Donchian extremes; high realized-vol regi… | 4825 | 33.37% | 0.60467163 | -1.6568 | -26.65% | -22.72% | 0.55466095 | -2.1036 | 32.07% | n/a | 0.00% | REJECTED |

## 3. Robustness Analysis

### W2-SWEEP-RECLAIM-001
- Hypothesis: Stop-runs that immediately reclaim a confirmed swing with volume are liquidity grabs, not breakouts.
- Entry: Sweep a confirmed swing, close back inside, close in reclaim direction, relative volume ≥ 1.2.
- Exit: Structural stop beyond the sweep wick; 2R target. Isolated LOW $ risk unchanged.
- Classification: **REJECTED**
- Timeframe 1h OOS: n=619 PF 0.61031735 WR 35.86%
- Coin PnL (OOS BASE): best ETHUSDT -48.9032, worst LTCUSDT -182.7043, coins with trades 10.
- Cost BASE OOS PF: 0.610
- Cost MILD OOS PF: 0.586
- Cost HIGH OOS PF: 0.572
- Cost STRESS OOS PF: 0.552

### W2-FAILED-BO-VOL-001
- Hypothesis: A Donchian break that fails back inside on dying volume is a fade, not a continuation.
- Entry: Prior bar closes beyond Donchian with rel vol ≥ 1.2; this bar closes back inside with rel vol < 1.0.
- Exit: Structural stop beyond the failed extreme; 2R target.
- Classification: **REJECTED**
- Timeframe 1h OOS: n=162 PF 0.50150083 WR 29.63%
- Coin PnL (OOS BASE): best XRPUSDT 20.4368, worst BTCUSDT -124.2208, coins with trades 10.
- Cost BASE OOS PF: 0.502
- Cost MILD OOS PF: 0.481
- Cost HIGH OOS PF: 0.461
- Cost STRESS OOS PF: 0.436

### W2-SQUEEZE-EXP-001
- Hypothesis: ATR-percentile compression stores energy; the first volume Donchian break after expansion has directional expectancy.
- Entry: Prior ATR percentile ≤ 0.25, ATR expands, close breaks Donchian with rel vol ≥ 1.2.
- Exit: Structural stop beyond the break bar; 2R target.
- Classification: **REJECTED**
- Timeframe 1h OOS: n=500 PF 0.70251449 WR 36.40%
- Coin PnL (OOS BASE): best XRPUSDT -1.5186, worst DOGEUSDT -185.0099, coins with trades 10.
- Cost BASE OOS PF: 0.703
- Cost MILD OOS PF: 0.693
- Cost HIGH OOS PF: 0.663
- Cost STRESS OOS PF: 0.617

### W2-VOL-EXHAUST-001
- Hypothesis: A high-volume small-body bar after a 3-bar run is exhaustion, not continuation.
- Entry: Rel vol ≥ 2, body ≤ 35% of range, 3-bar directional run, close in the opposite half of the bar.
- Exit: Structural stop beyond the exhaustion wick; 2R target.
- Classification: **REJECTED**
- Timeframe 1h OOS: n=306 PF 0.50227977 WR 33.01%
- Coin PnL (OOS BASE): best AVAXUSDT -28.3646, worst ETHUSDT -188.8081, coins with trades 10.
- Cost BASE OOS PF: 0.502
- Cost MILD OOS PF: 0.483
- Cost HIGH OOS PF: 0.464
- Cost STRESS OOS PF: 0.438

### W2-VWAP-EXT-001
- Hypothesis: Session VWAP is a fair-value magnet after an ATR-normalized extension; reclaim continues toward VWAP.
- Entry: Prior close ≥ 0.75 ATR beyond session VWAP, this close reclaims VWAP, rel vol ≥ 1.0.
- Exit: Stop beyond the extension extreme; TP 0.5 ATR through VWAP.
- Classification: **REJECTED**
- Timeframe 1h OOS: n=376 PF 0.62728180 WR 31.38%
- Coin PnL (OOS BASE): best LTCUSDT -11.3970, worst BNBUSDT -157.4793, coins with trades 10.
- Cost BASE OOS PF: 0.627
- Cost MILD OOS PF: 0.614
- Cost HIGH OOS PF: 0.596
- Cost STRESS OOS PF: 0.572

### W2-BOS-PULLBACK-001
- Hypothesis: After a causal break of structure, a pullback that holds the broken swing continues the new structure.
- Entry: BOS in the last 8 closed bars, this bar tags the broken swing within 0.15 ATR and closes back through it.
- Exit: Structural stop beyond the pullback extreme; 2R target.
- Classification: **REJECTED**
- Timeframe 1h OOS: n=1604 PF 0.49389344 WR 32.48%
- Coin PnL (OOS BASE): best SOLUSDT -242.5490, worst BTCUSDT -481.4042, coins with trades 10.
- Cost BASE OOS PF: 0.494
- Cost MILD OOS PF: 0.479
- Cost HIGH OOS PF: 0.472
- Cost STRESS OOS PF: 0.445

### W2-DISPLACE-001
- Hypothesis: A ≥1.5 ATR displacement bar is informed flow; a later retrace into its midpoint continues that direction.
- Entry: Displacement bar in the last 6 closed bars; this bar retraces to the midpoint and closes in the displacement direction.
- Exit: Stop beyond the displacement extreme; TP 0.5 ATR beyond the displacement high/low.
- Classification: **REJECTED**
- Timeframe 1h OOS: n=1064 PF 0.61128476 WR 34.21%
- Coin PnL (OOS BASE): best SOLUSDT -92.8795, worst LINKUSDT -280.3996, coins with trades 10.
- Cost BASE OOS PF: 0.611
- Cost MILD OOS PF: 0.590
- Cost HIGH OOS PF: 0.575
- Cost STRESS OOS PF: 0.540

### W2-REGIME-SWITCH-001
- Hypothesis: Low realized-vol regimes fade Donchian extremes; high realized-vol regimes follow Donchian breaks with volume.
- Entry: ATR percentile ≤ 0.35: rejection at Donchian. ATR percentile ≥ 0.65: Donchian break with rel vol ≥ 1.2. Mid-vol: no trade.
- Exit: Structural stop beyond the event wick; 2R target.
- Classification: **REJECTED**
- Timeframe 1h OOS: n=920 PF 0.55466095 WR 32.07%
- Coin PnL (OOS BASE): best XRPUSDT -101.9448, worst BNBUSDT -283.8669, coins with trades 10.
- Cost BASE OOS PF: 0.555
- Cost MILD OOS PF: 0.541
- Cost HIGH OOS PF: 0.537
- Cost STRESS OOS PF: 0.518

## 4. Walk-Forward Results

Walk-forward rows are TEST windows only (parameters frozen). Empty windows are NO_TRADES, not PF=0.

- **W2-SWEEP-RECLAIM-001**: walk-forward windows were not produced in this screen.
- **W2-FAILED-BO-VOL-001**: walk-forward windows were not produced in this screen.
- **W2-SQUEEZE-EXP-001**: walk-forward windows were not produced in this screen.
- **W2-VOL-EXHAUST-001**: walk-forward windows were not produced in this screen.
- **W2-VWAP-EXT-001**: walk-forward windows were not produced in this screen.
- **W2-BOS-PULLBACK-001**: walk-forward windows were not produced in this screen.
- **W2-DISPLACE-001**: walk-forward windows were not produced in this screen.
- **W2-REGIME-SWITCH-001**: walk-forward windows were not produced in this screen.

## 5. Failure Analysis

### Existing catalog (not re-run here; prior Model B 1,584-book and research reports)

| Strategy | Where it can look acceptable | Where it fails | Notes |
|---|---|---|---|
| RSI Pullback | Quiet, mean-reverting stretches; WR can exceed 50% | Trend days; 2%/4% book is the wrong target for a mid-band fade | Few trades per book (~6 / 2y typical). Combined PF < 1. |
| Bollinger Reversion | Range regimes | Expansion / trend; high turnover after costs | ~161 trades/book. PF < 1. |
| EMA RSI | Strong 1h trends, long side only | Whipsaw; SHORT; 27% WR is not enough for 2R after costs | ~430 trades/book. Largest frozen-five drain. |
| MACD Trend | Same as EMA family | Late crosses; SHORT Combined PF < 1 | Filter variants (HTF) did not flip OOS. |
| Donchian Breakout | High-vol expansion | Fake breaks on 5m/15m; ATR/volume filters did not produce robust OOS | Research DONCHIAN-* still PF < 1. |
| Advanced alpha (sweep, squeeze, VWAP, structure, z-score) | Occasional Combined OOS PF ≈ 1.00–1.12 on a 10-coin 90d pilot, **LONG-concentrated** | SHORT PF < 1 on every runnable family; cost +50–100% flipped the ~1.0 results | Classified COST_FRAGILE. Not 1,584-book proven. |
| Funding / OI Phase 4 | Hypothesis-valid; data existed for 10 coins / 2y funding | PF < 1 after costs | OI history is ~30d public only (DATA_UNAVAILABLE for 2y). |

Missing alpha was not “one more indicator.” Replay ignored SuggestedStop/TP and always used the Isolated 2%/4% book, so mean-reversion never targeted VWAP/POC, and trend stops were not structure. Wave-2 removed that mismatch. Expectancy stayed negative. The remaining problem is **false entries in crypto 1h noise**, not the wrong take-profit math.

### Wave-2 mechanisms (this screen)

At 2R, breakeven win rate before costs is 33.3%. After 0.04%×2 commission and 0.02%×2 slippage, required WR is higher. Every candidate’s WR is 32–37% with PF 0.49–0.73. Costs at +25/+50/+100% made PF worse, not better — none are “cost-fragile winners”; they are losers at BASE.

- **W2-SWEEP-RECLAIM-001** (REJECTED): Liquidity-sweep reclaim fires often (n=3314). Most sweeps in this universe **continue**, so fading them is negative EV. 0% profitable books.
- **W2-FAILED-BO-VOL-001** (REJECTED): Volume-die-off fade still loses (OOS PF 0.50). Failed breaks often resume.
- **W2-SQUEEZE-EXP-001** (REJECTED): Least-bad OOS PF 0.70. Compression-expansion breaks in USDT-M still have too many failed follow-throughs after costs.
- **W2-VOL-EXHAUST-001** (REJECTED): High-volume small-body bars are not a reliable 1h reversal.
- **W2-VWAP-EXT-001** (REJECTED): Session VWAP magnet is weaker than costs on 1h perpetuals in this window.
- **W2-BOS-PULLBACK-001** (REJECTED): Highest turnover and worst median book (−43%). Causal BOS on 1h over-fits swing noise.
- **W2-DISPLACE-001** (REJECTED): Displacement retrace WR 37% is the highest here and still PF 0.73.
- **W2-REGIME-SWITCH-001** (REJECTED): Switching fade vs breakout by ATR percentile did not produce a positive regime; both sleeves lose.

No candidate was dropped because a coin lost. All 10 screen coins traded. No parameter was changed after OOS. 15m/5m were not run after 1h IS/VAL failed.

## 6. Best Candidate Architecture

Strongest label in this screen: **W2-SQUEEZE-EXP-001** (REJECTED).

- Entry rules: Prior ATR percentile ≤ 0.25, ATR expands, close breaks Donchian with rel vol ≥ 1.2.
- Exit rules: Structural stop beyond the break bar; 2R target.
- Features: ATR-percentile, ATR14, Donchian20, RelativeVolume20
- Parameters (pre-registered, not OOS-fit): swing n=3, Donchian=20, ATR=14, ATR-percentile lookback=50, rel-vol lookback=20, compression≤0.25, low-vol≤0.35, high-vol≥0.65, exhaustion rel-vol≥2 / body≤0.35, VWAP extension 0.75 ATR, displacement 1.5 ATR / 6 bars, BOS lookback 8, min stop 0.20% of fill, 2R when TP omitted.
- Regime filter: per-candidate (see entry). No future regime labels.
- Position sizing: Isolated LOW Risk Engine. 0.5% of $1,000 = $5 planned loss at the structural (or 2%) stop. 3x cap. Daily 3% halt on new entries.
- Risk logic: Isolated, one position per coin, books not sharing margin. FrozenRisk ($10k/1%/5x) was not used for this screen.

This is the least-bad screen result, not a production promotion. Do not enable LIVE. Do not mark VALIDATED_FOR_PAPER.

Least-bad OOS Combined PF in this screen is **W2-SQUEEZE-EXP-001 at 0.70**, still below 1. It is not an edge.

## 7. Final Classification

- `W2-SWEEP-RECLAIM-001`: **REJECTED**
- `W2-FAILED-BO-VOL-001`: **REJECTED**
- `W2-SQUEEZE-EXP-001`: **REJECTED**
- `W2-VOL-EXHAUST-001`: **REJECTED**
- `W2-VWAP-EXT-001`: **REJECTED**
- `W2-BOS-PULLBACK-001`: **REJECTED**
- `W2-DISPLACE-001`: **REJECTED**
- `W2-REGIME-SWITCH-001`: **REJECTED**

## Framework audit (Wave-2)

- Model B timing: signal on closed candle t, fill next open T+1, SL before TP on the same bar, fees 0.04% + slippage 0.02% at BASE.
- IS / VAL / OOS: chronological 60 / 20 / 20 of closed bars. OOS is reported, not used to change parameters.
- Indicators: CausalIndicatorCache / confirmed swings (published at k+n). Donchian at i uses [i-n, i). No future close/volume.
- Holdout limitation: OOS is computed in the same process after IS/VAL. It was not a never-inspected vault. Parameters were not edited after OOS.
- Cross-section, pairs, historical OI, and predicted funding remain DATA_UNAVAILABLE in single-book replay and were not fabricated.

## Run notes
- Wave-2 mechanism screen. LIVE disabled. Frozen five / FrozenRisk / Isolated LOW catalog numbers unchanged.
- Book: LOW Isolated $1000, 0.5% risk, 3x, structural stops honor SuggestedStop, 2R when TP omitted.
- OOS is reported and was not used to change parameters. Full 528-universe run is blocked.
- Walk-forward TEST windows are skipped on this lightweight screen (SkipWalkForward). Re-run without that flag only after IS/VAL gates.
- Window 2024-09-18 → 2026-09-19. Timeframes 1h. Candidates 8.
- BTCUSDT 1h: bars=17545 cache miss dl=15
- ETHUSDT 1h: bars=17545 cache miss dl=16
- BNBUSDT 1h: bars=17545 cache miss dl=16
- SOLUSDT 1h: bars=17545 cache miss dl=16
- XRPUSDT 1h: bars=17545 cache miss dl=16
- DOGEUSDT 1h: bars=17545 cache miss dl=16
- ADAUSDT 1h: bars=17545 cache miss dl=16
- AVAXUSDT 1h: bars=17545 cache miss dl=16
- LINKUSDT 1h: bars=17545 cache miss dl=16
- LTCUSDT 1h: bars=17545 cache miss dl=16
