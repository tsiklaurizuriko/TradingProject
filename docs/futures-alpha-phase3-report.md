# Futures alpha Phase 3 report

Focused futures-specific pilot. LIVE = OFF. Risk Engine unchanged. Frozen five unchanged. No 528-universe run.
No OOS parameter tuning. No VALIDATED_FOR_PAPER. No fabricated data. Taker flow was not used.

## 1. Data availability
- AVAILABLE (90d, 10 coins, 5m/15m/1h): OHLCV, Funding (settled `fundingTime`), Mark, Index, Basis.
- OI: ~29 days only. Results that use OI are labeled **OI_SAMPLE_LIMITED**. Not compared as equal-confidence 90-day books.
- Taker flow: INSUFFICIENT_DATA. Not a Phase 3 candidate.
- Liquidations, depth, causal pair universe: DATA_UNAVAILABLE.

## 2. Sample period
- Funding / basis / mark / index / OHLCV candidates: requested ~90 days.
- OI candidates: last 29 days of the same end date. Window was not silently treated as 90 days.

## 3. Candidate definitions
- `funding_oi_reversal`: extreme funding + extreme OI + ATR displacement + exhaustion + reversal candle.
- `funding_basis_rv`: co-extreme funding z and basis z. Directional only; market-neutral pairs not testable on a single book.
- `funding_price_momentum`: EMA trend + volume + price momentum + funding extreme.
- `oi_price_volume_regime`: expansion-state continuation vs contrarian vs price/volume baseline. Eight-state table is not a cost-inclusive trade book; empty cells = INSUFFICIENT_DATA.
- `funding_extreme_momentum_exhaustion`: funding tail + weakening momentum + reversal candle (no OI).
- `basis_mean_reversion`: normalized-basis z at 2.0 and 2.5, reversion vs continuation separately.
- `funding_basis_vwap`: funding tail + basis z + VWAP deviation.
- `oi_breakout_confirmation`: Donchian + volume, with vs without OI expansion.
- `vp_vwap_reversion`: secondary check with frozen/default parameters. Not retuned on Phase 1 OOS.
- Taker flow was not run (INSUFFICIENT_DATA).

## 4. Formulas
- Continuation vs contrarian are **independent** hypotheses. Positive funding is not hard-coded SHORT.
- Funding z/percentile/cumulative use settled rates with `fundingTime <= CloseTime`. Lookback is at least 3 calendar days of bars per timeframe (not OOS-tuned).
- Basis = MarkClose − IndexClose; NormalizedBasis = Basis / IndexClose; matching closeTime only. No large-gap fill.
- PF = Σ winning net / |Σ losing net|. Empty = NO_TRADES. No PF=99. Combined PF is summed PnL, never an average of PFs.

## 5. Baselines
Each futures-enhanced candidate is compared to the same price/volume rules with `UseFuturesFilter=false` (no Funding/OI/Basis required). Incremental value is enhanced − baseline, not the raw enhanced PF.

## 6–7. Enhanced strategies and OOS BASE Combined
### funding_oi_reversal
- `funding_oi_reversal|enhanced|contrarian` role=enhanced hyp=contrarian: OOS_FAILED OI_SAMPLE_LIMITED; n=72 PF 0.16296036; exp=-79.12; LONG PF 0.00000000 n=1; SHORT PF 0.16646111 n=71; symbols=10 days=3 weeks=1; fees=286.02
- `funding_oi_reversal|enhanced|continuation` role=enhanced hyp=continuation: RESEARCHING OI_SAMPLE_LIMITED; n=60 PF 4.75561320; exp=91.02; LONG PF 4.62159378 n=59; SHORT PF Infinity / NoLosses n=1; symbols=10 days=3 weeks=1; fees=245.09
- `funding_oi_reversal|baseline|contrarian` role=baseline hyp=contrarian: OOS_FAILED; n=368 PF 0.73231236; exp=-21.30; LONG PF 1.03530495 n=140; SHORT PF 0.56591221 n=228; symbols=10 days=18 weeks=3; fees=1456.42
- `funding_oi_reversal|baseline|continuation` role=baseline hyp=continuation: OOS_FAILED; n=359 PF 0.95934959; exp=-2.90; LONG PF 1.31106772 n=209; SHORT PF 0.60687569 n=150; symbols=10 days=16 weeks=3; fees=1419.20
- `funding_oi_reversal|enhanced|contrarian_p05` role=enhanced hyp=contrarian_p05: OOS_FAILED OI_SAMPLE_LIMITED; n=72 PF 0.16296036; exp=-79.12; LONG PF 0.00000000 n=1; SHORT PF 0.16646111 n=71; symbols=10 days=3 weeks=1; fees=286.02

Incremental (enhanced − baseline) OOS BASE Combined:
- `funding_oi_reversal|enhanced|contrarian` vs `funding_oi_reversal|baseline|contrarian`: ΔPF=-0.569 ΔExp=-57.82 ΔWR=-14.25 ΔNet=2141.59 ΔDD=-12.64
- `funding_oi_reversal|enhanced|continuation` vs `funding_oi_reversal|baseline|continuation`: ΔPF=3.796 ΔExp=93.92 ΔWR=32.95 ΔNet=6502.25 ΔDD=-12.97
- `funding_oi_reversal|enhanced|contrarian_p05` vs `funding_oi_reversal|baseline|contrarian`: ΔPF=-0.569 ΔExp=-57.82 ΔWR=-14.25 ΔNet=2141.59 ΔDD=-12.64

### funding_basis_rv
- `funding_basis_rv|enhanced|contrarian` role=enhanced hyp=contrarian: OOS_FAILED; n=73 PF 0.44791879; exp=-52.24; LONG PF 0.77495532 n=35; SHORT PF 0.19687181 n=38; symbols=10 days=5 weeks=3; fees=292.35
- `funding_basis_rv|enhanced|continuation` role=enhanced hyp=continuation: RESEARCHING; n=71 PF 1.57667013; exp=34.29; LONG PF 2.01280428 n=37; SHORT PF 1.20028495 n=34; symbols=10 days=5 weeks=3; fees=285.07
- `funding_basis_rv|baseline|contrarian` role=baseline hyp=contrarian: OOS_FAILED; n=629 PF 0.67306118; exp=-27.96; LONG PF 0.91423234 n=245; SHORT PF 0.53995501 n=384; symbols=10 days=17 weeks=3; fees=2461.06
- `funding_basis_rv|baseline|continuation` role=baseline hyp=continuation: OOS_FAILED; n=560 PF 0.98715863; exp=-0.94; LONG PF 1.29303831 n=347; SHORT PF 0.60302127 n=213; symbols=10 days=18 weeks=3; fees=2215.52

Incremental (enhanced − baseline) OOS BASE Combined:
- `funding_basis_rv|enhanced|contrarian` vs `funding_basis_rv|baseline|contrarian`: ΔPF=-0.225 ΔExp=-24.28 ΔWR=-9.88 ΔNet=13773.59 ΔDD=-12.52
- `funding_basis_rv|enhanced|continuation` vs `funding_basis_rv|baseline|continuation`: ΔPF=0.590 ΔExp=35.24 ΔWR=10.19 ΔNet=2963.71 ΔDD=-11.41

### funding_price_momentum
- `funding_price_momentum|enhanced|contrarian` role=enhanced hyp=contrarian: OOS_FAILED; n=466 PF 0.52775718; exp=-42.43; LONG PF 0.71707753 n=157; SHORT PF 0.43980490 n=309; symbols=10 days=14 weeks=3; fees=1819.31
- `funding_price_momentum|enhanced|continuation` role=enhanced hyp=continuation: RESEARCHING; n=420 PF 1.00875788; exp=0.64; LONG PF 1.36547955 n=281; SHORT PF 0.51504554 n=139; symbols=10 days=14 weeks=3; fees=1659.66
- `funding_price_momentum|baseline|contrarian` role=baseline hyp=contrarian: OOS_FAILED; n=709 PF 0.67160929; exp=-27.87; LONG PF 0.87178088 n=297; SHORT PF 0.54264677 n=412; symbols=10 days=19 weeks=3; fees=2757.76
- `funding_price_momentum|baseline|continuation` role=baseline hyp=continuation: OOS_FAILED; n=621 PF 0.89357749; exp=-8.09; LONG PF 1.15955207 n=383; SHORT PF 0.55734615 n=238; symbols=10 days=17 weeks=3; fees=2439.18

Incremental (enhanced − baseline) OOS BASE Combined:
- `funding_price_momentum|enhanced|contrarian` vs `funding_price_momentum|baseline|contrarian`: ΔPF=-0.144 ΔExp=-14.55 ΔWR=-4.40 ΔNet=-8.94 ΔDD=-0.58
- `funding_price_momentum|enhanced|continuation` vs `funding_price_momentum|baseline|continuation`: ΔPF=0.115 ΔExp=8.72 ΔWR=2.56 ΔNet=5290.05 ΔDD=-1.47

### oi_price_volume_regime
- `oi_price_volume_regime|enhanced|contrarian` role=enhanced hyp=contrarian: OOS_FAILED OI_SAMPLE_LIMITED; n=285 PF 0.47919244; exp=-45.79; LONG PF 1.31884503 n=72; SHORT PF 0.27035351 n=213; symbols=10 days=6 weeks=1; fees=1126.51
- `oi_price_volume_regime|enhanced|continuation` role=enhanced hyp=continuation: RESEARCHING OI_SAMPLE_LIMITED; n=230 PF 1.78349339; exp=40.97; LONG PF 2.98484596 n=159; SHORT PF 0.60133545 n=71; symbols=10 days=6 weeks=1; fees=932.97
- `oi_price_volume_regime|baseline|contrarian` role=baseline hyp=contrarian: OOS_FAILED; n=734 PF 0.65566303; exp=-29.50; LONG PF 0.85637309 n=308; SHORT PF 0.52699651 n=426; symbols=10 days=18 weeks=3; fees=2847.02
- `oi_price_volume_regime|baseline|continuation` role=baseline hyp=continuation: OOS_FAILED; n=658 PF 0.91708740; exp=-6.17; LONG PF 1.18488098 n=399; SHORT PF 0.59352041 n=259; symbols=10 days=17 weeks=3; fees=2566.40

Incremental (enhanced − baseline) OOS BASE Combined:
- `oi_price_volume_regime|enhanced|contrarian` vs `oi_price_volume_regime|baseline|contrarian`: ΔPF=-0.176 ΔExp=-16.29 ΔWR=-4.73 ΔNet=8605.97 ΔDD=-7.41
- `oi_price_volume_regime|enhanced|continuation` vs `oi_price_volume_regime|baseline|continuation`: ΔPF=0.866 ΔExp=47.15 ΔWR=15.03 ΔNet=13485.29 ΔDD=-13.01

### funding_extreme_momentum_exhaustion
- `funding_extreme_momentum_exhaustion|enhanced|contrarian` role=enhanced hyp=contrarian: OOS_FAILED; n=185 PF 0.44954074; exp=-48.65; LONG PF 0.69992997 n=27; SHORT PF 0.40515125 n=158; symbols=10 days=10 weeks=3; fees=728.29
- `funding_extreme_momentum_exhaustion|enhanced|continuation` role=enhanced hyp=continuation: RESEARCHING; n=169 PF 1.70391780; exp=38.38; LONG PF 1.88316512 n=141; SHORT PF 1.06107321 n=28; symbols=10 days=12 weeks=3; fees=684.83
- `funding_extreme_momentum_exhaustion|baseline|contrarian` role=baseline hyp=contrarian: OOS_FAILED; n=368 PF 0.73231236; exp=-21.30; LONG PF 1.03530495 n=140; SHORT PF 0.56591221 n=228; symbols=10 days=18 weeks=3; fees=1456.42
- `funding_extreme_momentum_exhaustion|baseline|continuation` role=baseline hyp=continuation: OOS_FAILED; n=359 PF 0.95934959; exp=-2.90; LONG PF 1.31106772 n=209; SHORT PF 0.60687569 n=150; symbols=10 days=16 weeks=3; fees=1419.20

Incremental (enhanced − baseline) OOS BASE Combined:
- `funding_extreme_momentum_exhaustion|enhanced|contrarian` vs `funding_extreme_momentum_exhaustion|baseline|contrarian`: ΔPF=-0.283 ΔExp=-27.35 ΔWR=-6.13 ΔNet=-1162.04 ΔDD=-4.67
- `funding_extreme_momentum_exhaustion|enhanced|continuation` vs `funding_extreme_momentum_exhaustion|baseline|continuation`: ΔPF=0.745 ΔExp=41.28 ΔWR=14.54 ΔNet=7527.47 ΔDD=-9.57

### basis_mean_reversion
- `basis_mean_reversion|enhanced|contrarian` role=enhanced hyp=contrarian: OOS_FAILED; n=531 PF 0.77253489; exp=-18.07; LONG PF 1.20693311 n=212; SHORT PF 0.56077598 n=319; symbols=10 days=16 weeks=3; fees=2075.92
- `basis_mean_reversion|enhanced|continuation` role=enhanced hyp=continuation: OOS_FAILED; n=503 PF 0.82614509; exp=-13.69; LONG PF 1.06731129 n=306; SHORT PF 0.52256183 n=197; symbols=10 days=16 weeks=3; fees=1974.06
- `basis_mean_reversion|enhanced|contrarian_z25` role=enhanced hyp=contrarian_z25: OOS_FAILED; n=377 PF 0.74129048; exp=-20.82; LONG PF 1.16117796 n=138; SHORT PF 0.56014464 n=239; symbols=10 days=16 weeks=3; fees=1486.26
- `basis_mean_reversion|baseline|contrarian` role=baseline hyp=contrarian: OOS_FAILED; n=629 PF 0.67306118; exp=-27.96; LONG PF 0.91423234 n=245; SHORT PF 0.53995501 n=384; symbols=10 days=17 weeks=3; fees=2461.06
- `basis_mean_reversion|baseline|continuation` role=baseline hyp=continuation: OOS_FAILED; n=560 PF 0.98715863; exp=-0.94; LONG PF 1.29303831 n=347; SHORT PF 0.60302127 n=213; symbols=10 days=18 weeks=3; fees=2215.52

Incremental (enhanced − baseline) OOS BASE Combined:
- `basis_mean_reversion|enhanced|contrarian` vs `basis_mean_reversion|baseline|contrarian`: ΔPF=0.099 ΔExp=9.89 ΔWR=2.48 ΔNet=7992.77 ΔDD=-6.37
- `basis_mean_reversion|enhanced|continuation` vs `basis_mean_reversion|baseline|continuation`: ΔPF=-0.161 ΔExp=-12.74 ΔWR=-3.12 ΔNet=-6354.81 ΔDD=3.97
- `basis_mean_reversion|enhanced|contrarian_z25` vs `basis_mean_reversion|baseline|contrarian`: ΔPF=0.068 ΔExp=7.14 ΔWR=1.36 ΔNet=9737.97 ΔDD=-7.29

### funding_basis_vwap
- `funding_basis_vwap|enhanced|contrarian` role=enhanced hyp=contrarian: INSUFFICIENT_DATA; n=47 PF 0.47781752; exp=-45.94; LONG PF 0.49885381 n=8; SHORT PF 0.47303333 n=39; symbols=10 days=5 weeks=3; fees=186.59
- `funding_basis_vwap|enhanced|continuation` role=enhanced hyp=continuation: INSUFFICIENT_DATA; n=48 PF 2.40969244; exp=64.60; LONG PF 2.46724407 n=39; SHORT PF 2.18382391 n=9; symbols=10 days=5 weeks=3; fees=194.44
- `funding_basis_vwap|baseline|contrarian` role=baseline hyp=contrarian: OOS_FAILED; n=557 PF 0.73353505; exp=-21.71; LONG PF 0.90070717 n=241; SHORT PF 0.61621539 n=316; symbols=10 days=18 weeks=3; fees=2191.43
- `funding_basis_vwap|baseline|continuation` role=baseline hyp=continuation: OOS_FAILED; n=478 PF 0.95838993; exp=-3.03; LONG PF 1.29239818 n=284; SHORT PF 0.59324746 n=194; symbols=10 days=17 weeks=3; fees=1879.96

Incremental (enhanced − baseline) OOS BASE Combined:
- `funding_basis_vwap|enhanced|contrarian` vs `funding_basis_vwap|baseline|contrarian`: ΔPF=-0.256 ΔExp=-24.24 ΔWR=-10.17 ΔNet=9932.10 ΔDD=-16.23
- `funding_basis_vwap|enhanced|continuation` vs `funding_basis_vwap|baseline|continuation`: ΔPF=1.451 ΔExp=67.62 ΔWR=20.26 ΔNet=4547.61 ΔDD=-15.43

### oi_breakout_confirmation
- `oi_breakout_confirmation|enhanced|continuation` role=enhanced hyp=continuation: RESEARCHING OI_SAMPLE_LIMITED; n=112 PF 2.21302210; exp=56.69; LONG PF 4.77205856 n=82; SHORT PF 0.28353193 n=30; symbols=10 days=4 weeks=1; fees=457.64
- `oi_breakout_confirmation|enhanced|contrarian` role=enhanced hyp=contrarian: OOS_FAILED OI_SAMPLE_LIMITED; n=134 PF 0.50872167; exp=-41.25; LONG PF 4.01606682 n=27; SHORT PF 0.21862652 n=107; symbols=10 days=5 weeks=1; fees=535.14
- `oi_breakout_confirmation|baseline|continuation` role=baseline hyp=continuation: OOS_FAILED; n=533 PF 0.96264018; exp=-2.76; LONG PF 1.35133585 n=305; SHORT PF 0.57384395 n=228; symbols=10 days=16 weeks=3; fees=2100.47
- `oi_breakout_confirmation|baseline|contrarian` role=baseline hyp=contrarian: OOS_FAILED; n=541 PF 0.70368126; exp=-25.26; LONG PF 0.98236577 n=224; SHORT PF 0.53541698 n=317; symbols=10 days=16 weeks=3; fees=2132.52

Incremental (enhanced − baseline) OOS BASE Combined:
- `oi_breakout_confirmation|enhanced|continuation` vs `oi_breakout_confirmation|baseline|continuation`: ΔPF=1.250 ΔExp=59.45 ΔWR=17.93 ΔNet=7822.17 ΔDD=-9.76
- `oi_breakout_confirmation|enhanced|contrarian` vs `oi_breakout_confirmation|baseline|contrarian`: ΔPF=-0.195 ΔExp=-15.99 ΔWR=-4.73 ΔNet=8138.11 ΔDD=-5.61

### vp_vwap_reversion
- `vp_vwap_reversion|enhanced|default` role=enhanced hyp=default: OOS_FAILED; n=326 PF 0.60903775; exp=-33.39; LONG PF 0.78892945 n=117; SHORT PF 0.51892411 n=209; symbols=10 days=16 weeks=3; fees=1282.57

Incremental (enhanced − baseline) OOS BASE Combined:

## 8. Cost stress (OOS Combined)
BASE = frozen Model B fees/slippage. HIGH = 1.5x. STRESS = 2x. A book that is PF>1 at BASE and PF<1 at HIGH is **COST_FRAGILE**.
- `funding_oi_reversal|enhanced|contrarian` | BASE n=72 PF 0.16296036 | HIGH n=72 PF 0.15363987 | STRESS n=73 PF 0.14509359
- `funding_oi_reversal|enhanced|continuation` | BASE n=60 PF 4.75561320 | HIGH n=60 PF 4.51591264 | STRESS n=60 PF 4.28716535
- `funding_oi_reversal|enhanced|contrarian_p05` | BASE n=72 PF 0.16296036 | HIGH n=72 PF 0.15363987 | STRESS n=73 PF 0.14509359
- `funding_basis_rv|enhanced|contrarian` | BASE n=73 PF 0.44791879 | HIGH n=73 PF 0.43469535 | STRESS n=73 PF 0.42156100
- `funding_basis_rv|enhanced|continuation` | BASE n=71 PF 1.57667013 | HIGH n=71 PF 1.52454452 | STRESS n=71 PF 1.48961737
- `funding_price_momentum|enhanced|contrarian` | BASE n=466 PF 0.52775718 | HIGH n=465 PF 0.49262014 | STRESS n=466 PF 0.46991198
- `funding_price_momentum|enhanced|continuation` | BASE n=420 PF 1.00875788 | HIGH n=421 PF 0.96303743 | STRESS n=420 PF 0.93494708 **COST_FRAGILE**
- `oi_price_volume_regime|enhanced|contrarian` | BASE n=285 PF 0.47919244 | HIGH n=285 PF 0.46341900 | STRESS n=286 PF 0.43852194
- `oi_price_volume_regime|enhanced|continuation` | BASE n=230 PF 1.78349339 | HIGH n=231 PF 1.69965609 | STRESS n=229 PF 1.60730693
- `funding_extreme_momentum_exhaustion|enhanced|contrarian` | BASE n=185 PF 0.44954074 | HIGH n=185 PF 0.43279017 | STRESS n=183 PF 0.39354826
- `funding_extreme_momentum_exhaustion|enhanced|continuation` | BASE n=169 PF 1.70391780 | HIGH n=169 PF 1.66386932 | STRESS n=169 PF 1.56130643
- `basis_mean_reversion|enhanced|contrarian` | BASE n=531 PF 0.77253489 | HIGH n=532 PF 0.74615507 | STRESS n=536 PF 0.71230334
- `basis_mean_reversion|enhanced|continuation` | BASE n=503 PF 0.82614509 | HIGH n=501 PF 0.79549473 | STRESS n=504 PF 0.76084286
- `basis_mean_reversion|enhanced|contrarian_z25` | BASE n=377 PF 0.74129048 | HIGH n=375 PF 0.71993680 | STRESS n=375 PF 0.68694339
- `funding_basis_vwap|enhanced|contrarian` | BASE n=47 PF 0.47781752 | HIGH n=47 PF 0.46375713 | STRESS n=47 PF 0.44839095
- `funding_basis_vwap|enhanced|continuation` | BASE n=48 PF 2.40969244 | HIGH n=48 PF 2.31371818 | STRESS n=48 PF 2.22273034
- `oi_breakout_confirmation|enhanced|continuation` | BASE n=112 PF 2.21302210 | HIGH n=112 PF 2.04894410 | STRESS n=112 PF 1.95183136
- `oi_breakout_confirmation|enhanced|contrarian` | BASE n=134 PF 0.50872167 | HIGH n=133 PF 0.49246760 | STRESS n=135 PF 0.47092530
- `vp_vwap_reversion|enhanced|default` | BASE n=326 PF 0.60903775 | HIGH n=328 PF 0.57382635 | STRESS n=328 PF 0.56416232

## 9. Sample size
Combined OOS trade count below 50 is INSUFFICIENT_DATA. Per-book n < 20 is not treated as evidence. 10–30 trades are not accepted.
- `funding_oi_reversal|enhanced|contrarian`: n=72 symbols=10 days=3 weeks=1 LONG=1 SHORT=71 OI_SAMPLE_LIMITED
- `funding_oi_reversal|enhanced|continuation`: n=60 symbols=10 days=3 weeks=1 LONG=59 SHORT=1 OI_SAMPLE_LIMITED
- `funding_oi_reversal|baseline|contrarian`: n=368 symbols=10 days=18 weeks=3 LONG=140 SHORT=228
- `funding_oi_reversal|baseline|continuation`: n=359 symbols=10 days=16 weeks=3 LONG=209 SHORT=150
- `funding_oi_reversal|enhanced|contrarian_p05`: n=72 symbols=10 days=3 weeks=1 LONG=1 SHORT=71 OI_SAMPLE_LIMITED
- `funding_basis_rv|enhanced|contrarian`: n=73 symbols=10 days=5 weeks=3 LONG=35 SHORT=38
- `funding_basis_rv|enhanced|continuation`: n=71 symbols=10 days=5 weeks=3 LONG=37 SHORT=34
- `funding_basis_rv|baseline|contrarian`: n=629 symbols=10 days=17 weeks=3 LONG=245 SHORT=384
- `funding_basis_rv|baseline|continuation`: n=560 symbols=10 days=18 weeks=3 LONG=347 SHORT=213
- `funding_price_momentum|enhanced|contrarian`: n=466 symbols=10 days=14 weeks=3 LONG=157 SHORT=309
- `funding_price_momentum|enhanced|continuation`: n=420 symbols=10 days=14 weeks=3 LONG=281 SHORT=139
- `funding_price_momentum|baseline|contrarian`: n=709 symbols=10 days=19 weeks=3 LONG=297 SHORT=412
- `funding_price_momentum|baseline|continuation`: n=621 symbols=10 days=17 weeks=3 LONG=383 SHORT=238
- `oi_price_volume_regime|enhanced|contrarian`: n=285 symbols=10 days=6 weeks=1 LONG=72 SHORT=213 OI_SAMPLE_LIMITED
- `oi_price_volume_regime|enhanced|continuation`: n=230 symbols=10 days=6 weeks=1 LONG=159 SHORT=71 OI_SAMPLE_LIMITED
- `oi_price_volume_regime|baseline|contrarian`: n=734 symbols=10 days=18 weeks=3 LONG=308 SHORT=426
- `oi_price_volume_regime|baseline|continuation`: n=658 symbols=10 days=17 weeks=3 LONG=399 SHORT=259
- `funding_extreme_momentum_exhaustion|enhanced|contrarian`: n=185 symbols=10 days=10 weeks=3 LONG=27 SHORT=158
- `funding_extreme_momentum_exhaustion|enhanced|continuation`: n=169 symbols=10 days=12 weeks=3 LONG=141 SHORT=28
- `funding_extreme_momentum_exhaustion|baseline|contrarian`: n=368 symbols=10 days=18 weeks=3 LONG=140 SHORT=228
- `funding_extreme_momentum_exhaustion|baseline|continuation`: n=359 symbols=10 days=16 weeks=3 LONG=209 SHORT=150
- `basis_mean_reversion|enhanced|contrarian`: n=531 symbols=10 days=16 weeks=3 LONG=212 SHORT=319
- `basis_mean_reversion|enhanced|continuation`: n=503 symbols=10 days=16 weeks=3 LONG=306 SHORT=197
- `basis_mean_reversion|enhanced|contrarian_z25`: n=377 symbols=10 days=16 weeks=3 LONG=138 SHORT=239
- `basis_mean_reversion|baseline|contrarian`: n=629 symbols=10 days=17 weeks=3 LONG=245 SHORT=384
- `basis_mean_reversion|baseline|continuation`: n=560 symbols=10 days=18 weeks=3 LONG=347 SHORT=213
- `funding_basis_vwap|enhanced|contrarian`: n=47 symbols=9 days=5 weeks=3 LONG=8 SHORT=39
- `funding_basis_vwap|enhanced|continuation`: n=48 symbols=9 days=5 weeks=3 LONG=39 SHORT=9
- `funding_basis_vwap|baseline|contrarian`: n=557 symbols=10 days=18 weeks=3 LONG=241 SHORT=316
- `funding_basis_vwap|baseline|continuation`: n=478 symbols=10 days=17 weeks=3 LONG=284 SHORT=194
- `oi_breakout_confirmation|enhanced|continuation`: n=112 symbols=10 days=4 weeks=1 LONG=82 SHORT=30 OI_SAMPLE_LIMITED
- `oi_breakout_confirmation|enhanced|contrarian`: n=134 symbols=10 days=5 weeks=1 LONG=27 SHORT=107 OI_SAMPLE_LIMITED
- `oi_breakout_confirmation|baseline|continuation`: n=533 symbols=10 days=16 weeks=3 LONG=305 SHORT=228
- `oi_breakout_confirmation|baseline|contrarian`: n=541 symbols=10 days=16 weeks=3 LONG=224 SHORT=317
- `vp_vwap_reversion|enhanced|default`: n=326 symbols=10 days=16 weeks=3 LONG=117 SHORT=209

## 10. LONG / SHORT
Side PFs are summed from side totals. A candidate dominated by one side is not interesting for Phase 4.
- `funding_oi_reversal|enhanced|contrarian` LONG n=1 PF 0.00000000 n=1 exp=-143.13; SHORT n=71 PF 0.16646111 n=71 exp=-78.22
- `funding_oi_reversal|enhanced|continuation` LONG n=59 PF 4.62159378 n=59 exp=89.26; SHORT n=1 PF Infinity / NoLosses n=1 exp=194.88
- `funding_oi_reversal|enhanced|contrarian_p05` LONG n=1 PF 0.00000000 n=1 exp=-143.13; SHORT n=71 PF 0.16646111 n=71 exp=-78.22
- `funding_basis_rv|enhanced|contrarian` LONG n=35 PF 0.77495532 n=35 exp=-19.29; SHORT n=38 PF 0.19687181 n=38 exp=-82.60
- `funding_basis_rv|enhanced|continuation` LONG n=37 PF 2.01280428 n=37 exp=53.54; SHORT n=34 PF 1.20028495 n=34 exp=13.35
- `funding_price_momentum|enhanced|contrarian` LONG n=157 PF 0.71707753 n=157 exp=-23.93; SHORT n=309 PF 0.43980490 n=309 exp=-51.82
- `funding_price_momentum|enhanced|continuation` LONG n=281 PF 1.36547955 n=281 exp=23.08; SHORT n=139 PF 0.51504554 n=139 exp=-44.72
- `oi_price_volume_regime|enhanced|contrarian` LONG n=72 PF 1.31884503 n=72 exp=22.10; SHORT n=213 PF 0.27035351 n=213 exp=-68.74
- `oi_price_volume_regime|enhanced|continuation` LONG n=159 PF 2.98484596 n=159 exp=74.47; SHORT n=71 PF 0.60133545 n=71 exp=-34.04
- `funding_extreme_momentum_exhaustion|enhanced|contrarian` LONG n=27 PF 0.69992997 n=27 exp=-27.36; SHORT n=158 PF 0.40515125 n=158 exp=-52.29
- `funding_extreme_momentum_exhaustion|enhanced|continuation` LONG n=141 PF 1.88316512 n=141 exp=45.13; SHORT n=28 PF 1.06107321 n=28 exp=4.38
- `basis_mean_reversion|enhanced|contrarian` LONG n=212 PF 1.20693311 n=212 exp=13.49; SHORT n=319 PF 0.56077598 n=319 exp=-39.04
- `basis_mean_reversion|enhanced|continuation` LONG n=306 PF 1.06731129 n=306 exp=4.85; SHORT n=197 PF 0.52256183 n=197 exp=-42.48
- `basis_mean_reversion|enhanced|contrarian_z25` LONG n=138 PF 1.16117796 n=138 exp=10.68; SHORT n=239 PF 0.56014464 n=239 exp=-39.01
- `funding_basis_vwap|enhanced|contrarian` LONG n=8 PF 0.49885381 n=8 exp=-48.00; SHORT n=39 PF 0.47303333 n=39 exp=-45.52
- `funding_basis_vwap|enhanced|continuation` LONG n=39 PF 2.46724407 n=39 exp=65.94; SHORT n=9 PF 2.18382391 n=9 exp=58.75
- `oi_breakout_confirmation|enhanced|continuation` LONG n=82 PF 4.77205856 n=82 exp=103.51; SHORT n=30 PF 0.28353193 n=30 exp=-71.27
- `oi_breakout_confirmation|enhanced|contrarian` LONG n=27 PF 4.01606682 n=27 exp=96.02; SHORT n=107 PF 0.21862652 n=107 exp=-75.89
- `vp_vwap_reversion|enhanced|default` LONG n=117 PF 0.78892945 n=117 exp=-16.76; SHORT n=209 PF 0.51892411 n=209 exp=-42.69

## 11. Funding impact
Replay applies settled funding to open Isolated notional when `fundingTime` falls in `(prevClose, close]` and `fundingTime >= fillTime`. CostNotes=INCLUDING_FUNDING. Baseline price-only books still include funding cashflows while a position is open (same Isolated carrying cost).
- Combined OOS BASE fundingPaid (sum of book notes) = -31.82 (positive = paid).

## 12. OI limitations
Public Binance `openInterestHist` ≈ 29 days. OI families are **OI_SAMPLE_LIMITED**. They are not 90-day evidence. A candidate that would require 90 days of OI is DATA_UNAVAILABLE. OI was not extrapolated.

## 13. Basis results
- `funding_basis_rv|enhanced|contrarian`: OOS_FAILED; n=73 PF 0.44791879 exp=-52.24
- `funding_basis_rv|enhanced|continuation`: RESEARCHING; n=71 PF 1.57667013 exp=34.29
- `funding_basis_rv|baseline|contrarian`: OOS_FAILED; n=629 PF 0.67306118 exp=-27.96
- `funding_basis_rv|baseline|continuation`: OOS_FAILED; n=560 PF 0.98715863 exp=-0.94
- `basis_mean_reversion|enhanced|contrarian`: OOS_FAILED; n=531 PF 0.77253489 exp=-18.07
- `basis_mean_reversion|enhanced|continuation`: OOS_FAILED; n=503 PF 0.82614509 exp=-13.69
- `basis_mean_reversion|enhanced|contrarian_z25`: OOS_FAILED; n=377 PF 0.74129048 exp=-20.82
- `basis_mean_reversion|baseline|contrarian`: OOS_FAILED; n=629 PF 0.67306118 exp=-27.96
- `basis_mean_reversion|baseline|continuation`: OOS_FAILED; n=560 PF 0.98715863 exp=-0.94
- `funding_basis_vwap|enhanced|contrarian`: INSUFFICIENT_DATA; n=47 PF 0.47781752 exp=-45.94
- `funding_basis_vwap|enhanced|continuation`: INSUFFICIENT_DATA; n=48 PF 2.40969244 exp=64.60
- `funding_basis_vwap|baseline|contrarian`: OOS_FAILED; n=557 PF 0.73353505 exp=-21.71
- `funding_basis_vwap|baseline|continuation`: OOS_FAILED; n=478 PF 0.95838993 exp=-3.03

## 14. Incremental information
Futures features are useful only if enhanced − baseline is robust after costs. See Δ lines under each family above.

## 15. Failure modes
- Look-ahead: features use index i only; fundingTime must be ≤ CloseTime; OI/basis nulls stay null.
- Sparse unique funding prints on 5m (aligned step function). 3-day bar lookback is the frozen default.
- OI 29d vs 90d OHLCV: do not rank OI books against 90-day books.
- One-symbol or one-side domination, tiny OOS windows, and COST_FRAGILE at 1.5x.
- Market-neutral C2 pairs were not fabricated.

## 16–18. Phase 4 gate
VALIDATED_FOR_PAPER is not assigned in this phase. Interesting only if: n≥50 combined OOS, positive expectancy, cost-inclusive, not 1.5x-fragile, both sides not empty, not one-symbol dominated, incremental Δ vs baseline.
### 16. Worth Phase 4 research (not promotion)
- funding_basis_rv|enhanced|continuation (RESEARCHING, n=71)
- funding_extreme_momentum_exhaustion|enhanced|continuation (RESEARCHING, n=169)
### 17. Rejected for Phase 4
- funding_basis_rv|enhanced|contrarian (OOS_FAILED, n=73, exp=-52.24)
- funding_price_momentum|enhanced|contrarian (OOS_FAILED, n=466, exp=-42.43)
- funding_price_momentum|enhanced|continuation (COST_FRAGILE, n=420)
- funding_extreme_momentum_exhaustion|enhanced|contrarian (OOS_FAILED, n=185, exp=-48.65)
- basis_mean_reversion|enhanced|contrarian (OOS_FAILED, n=531, exp=-18.07)
- basis_mean_reversion|enhanced|continuation (OOS_FAILED, n=503, exp=-13.69)
- basis_mean_reversion|enhanced|contrarian_z25 (OOS_FAILED, n=377, exp=-20.82)
- vp_vwap_reversion|enhanced|default (OOS_FAILED, n=326, exp=-33.39)
### 18. Requiring more data
- funding_oi_reversal|enhanced|contrarian (OOS_FAILED, OI_SAMPLE_LIMITED)
- funding_oi_reversal|enhanced|continuation (RESEARCHING, OI_SAMPLE_LIMITED)
- funding_oi_reversal|enhanced|contrarian_p05 (OOS_FAILED, OI_SAMPLE_LIMITED)
- oi_price_volume_regime|enhanced|contrarian (OOS_FAILED, OI_SAMPLE_LIMITED)
- oi_price_volume_regime|enhanced|continuation (RESEARCHING, OI_SAMPLE_LIMITED)
- funding_basis_vwap|enhanced|contrarian (INSUFFICIENT_DATA)
- funding_basis_vwap|enhanced|continuation (INSUFFICIENT_DATA)
- oi_breakout_confirmation|enhanced|continuation (RESEARCHING, OI_SAMPLE_LIMITED)
- oi_breakout_confirmation|enhanced|contrarian (OOS_FAILED, OI_SAMPLE_LIMITED)

## LIVE / Risk
- LIVE remains OFF.
- Risk Engine was not modified.

## Run notes
- Phase 3 futures-alpha pilot. LIVE disabled. Frozen five / Risk Engine unchanged. No 528-universe run.
- No OOS parameter tuning. No VALIDATED_FOR_PAPER. Taker flow not used (INSUFFICIENT_DATA).
- OI books use the last 29 days and are labeled OI_SAMPLE_LIMITED.
- BTCUSDT funding n=270
- BTCUSDT 5m: bars=25920 cache hit dl=0 mark=25904 basis=25904 oi=8340
- BTCUSDT 15m: bars=8640 cache hit dl=0 mark=8635 basis=8635 oi=2780
- BTCUSDT 1h: bars=2160 cache hit dl=0 mark=2159 basis=2159 oi=695
- ETHUSDT funding n=270
- ETHUSDT 5m: bars=25920 cache hit dl=0 mark=25904 basis=25904 oi=8340
- ETHUSDT 15m: bars=8640 cache hit dl=0 mark=8635 basis=8635 oi=2780
- ETHUSDT 1h: bars=2160 cache hit dl=0 mark=2159 basis=2159 oi=695
- BNBUSDT funding n=270
- BNBUSDT 5m: bars=25920 cache hit dl=0 mark=25904 basis=25904 oi=8340
- BNBUSDT 15m: bars=8640 cache hit dl=0 mark=8635 basis=8635 oi=2780
- BNBUSDT 1h: bars=2160 cache hit dl=0 mark=2159 basis=2159 oi=695
- SOLUSDT funding n=270
- SOLUSDT 5m: bars=25920 cache hit dl=0 mark=25905 basis=25905 oi=8340
- SOLUSDT 15m: bars=8640 cache hit dl=0 mark=8635 basis=8635 oi=2780
- SOLUSDT 1h: bars=2160 cache hit dl=0 mark=2159 basis=2159 oi=695
- XRPUSDT funding n=270
- XRPUSDT 5m: bars=25920 cache hit dl=0 mark=25905 basis=25905 oi=8340
- XRPUSDT 15m: bars=8640 cache hit dl=0 mark=8635 basis=8635 oi=2780
- XRPUSDT 1h: bars=2160 cache hit dl=0 mark=2159 basis=2159 oi=695
- DOGEUSDT funding n=270
- DOGEUSDT 5m: bars=25920 cache hit dl=0 mark=25905 basis=25905 oi=8340
- DOGEUSDT 15m: bars=8640 cache hit dl=0 mark=8635 basis=8635 oi=2780
- DOGEUSDT 1h: bars=2160 cache hit dl=0 mark=2159 basis=2159 oi=695
- ADAUSDT funding n=270
- ADAUSDT 5m: bars=25920 cache hit dl=0 mark=25905 basis=25905 oi=8340
- ADAUSDT 15m: bars=8640 cache hit dl=0 mark=8635 basis=8635 oi=2780
- ADAUSDT 1h: bars=2160 cache hit dl=0 mark=2159 basis=2159 oi=695
- AVAXUSDT funding n=270
- AVAXUSDT 5m: bars=25920 cache hit dl=0 mark=25905 basis=25905 oi=8340
- AVAXUSDT 15m: bars=8640 cache hit dl=0 mark=8635 basis=8635 oi=2780
- AVAXUSDT 1h: bars=2160 cache hit dl=0 mark=2159 basis=2159 oi=695
- LINKUSDT funding n=270
- LINKUSDT 5m: bars=25920 cache hit dl=0 mark=25905 basis=25905 oi=8340
- LINKUSDT 15m: bars=8640 cache hit dl=0 mark=8635 basis=8635 oi=2780
- LINKUSDT 1h: bars=2160 cache hit dl=0 mark=2159 basis=2159 oi=695
- LTCUSDT funding n=270
- LTCUSDT 5m: bars=25920 cache hit dl=0 mark=25905 basis=25905 oi=8340
- LTCUSDT 15m: bars=8640 cache hit dl=0 mark=8636 basis=8636 oi=2780
- LTCUSDT 1h: bars=2160 cache hit dl=0 mark=2159 basis=2159 oi=695
