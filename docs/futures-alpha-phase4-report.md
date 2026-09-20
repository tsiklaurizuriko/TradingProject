# Futures alpha Phase 4 report

Deep validation of two Phase 3 continuation hypotheses. LIVE = OFF. Risk Engine unchanged. Frozen five unchanged. No 528-universe run. No VALIDATED_FOR_PAPER. No OOS parameter tuning. No OI.

## 1. Frozen Phase 3 replication
Parameters are the Phase 3 pre-registered defaults. Continuation direction logic is unchanged. Neighborhood values were evaluated on IS only.
- OOS BASE frozen `funding_basis_rv|enhanced|continuation`: n=477 PF 0.89629023 exp=-7.88 net=-3759.48 dd=14.28 LONG PF 1.14278653 n=197 SHORT PF 0.74947090 n=280
- OOS BASE frozen `funding_extreme_momentum_exhaustion|enhanced|continuation`: n=1031 PF 0.93616736 exp=-4.74 net=-4891.91 dd=26.78 LONG PF 1.02769240 n=719 SHORT PF 0.74513935 n=312

## 2. Historical coverage
- COVERAGE requestedDays=730 fundingSpan=2024-09-20..2026-09-19. OI not loaded.
- BTCUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- BTCUSDT 5m: bars=210240 cache miss dl=3 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- BTCUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- BTCUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- ETHUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- ETHUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- ETHUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- ETHUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- BNBUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- BNBUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- BNBUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- BNBUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- SOLUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- SOLUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- SOLUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- SOLUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- XRPUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- XRPUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- XRPUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- XRPUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- DOGEUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- DOGEUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- DOGEUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- DOGEUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- ADAUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- ADAUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- ADAUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- ADAUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- AVAXUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- AVAXUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- AVAXUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- AVAXUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- LINKUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- LINKUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- LINKUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- LINKUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- LTCUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- LTCUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- LTCUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- LTCUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19

## 3. Chronological blocks
Four contiguous blocks over the full available sample. PF is summed win/loss PnL, never an average of PFs.
### funding_basis_rv|enhanced|continuation
- BLOCK1 `funding_basis_rv|enhanced|continuation`: n=804 PF 0.74297306 exp=-21.48 net=-17272.43 dd=28.34 LONG PF 0.52592988 n=316 SHORT PF 0.91328070 n=488
- BLOCK2 `funding_basis_rv|enhanced|continuation`: n=605 PF 0.77528737 exp=-18.00 net=-10890.84 dd=16.66 LONG PF 0.88287334 n=134 SHORT PF 0.74608097 n=471
- BLOCK3 `funding_basis_rv|enhanced|continuation`: n=625 PF 1.11157123 exp=8.12 net=5074.76 dd=16.03 LONG PF 1.08249453 n=186 SHORT PF 1.12400349 n=439
- BLOCK4 `funding_basis_rv|enhanced|continuation`: n=594 PF 0.77410765 exp=-17.84 net=-10595.47 dd=19.40 LONG PF 1.00025975 n=238 SHORT PF 0.64701000 n=356
### funding_extreme_momentum_exhaustion|enhanced|continuation
- BLOCK1 `funding_extreme_momentum_exhaustion|enhanced|continuation`: n=1763 PF 0.78800574 exp=-16.38 net=-28876.49 dd=39.15 LONG PF 0.75511936 n=1408 SHORT PF 0.92963813 n=355
- BLOCK2 `funding_extreme_momentum_exhaustion|enhanced|continuation`: n=1815 PF 0.91339914 exp=-6.69 net=-12145.40 dd=28.01 LONG PF 0.92010227 n=1422 SHORT PF 0.88865582 n=393
- BLOCK3 `funding_extreme_momentum_exhaustion|enhanced|continuation`: n=1214 PF 0.71584681 exp=-22.26 net=-27018.15 dd=27.32 LONG PF 0.60818152 n=809 SHORT PF 0.95961189 n=405
- BLOCK4 `funding_extreme_momentum_exhaustion|enhanced|continuation`: n=1267 PF 0.78416987 exp=-16.27 net=-20609.11 dd=34.27 LONG PF 0.88171683 n=854 SHORT PF 0.61155213 n=413

## 4. Walk-forward
Test windows only. Empty window = NO_TRADES.
### funding_basis_rv|enhanced|continuation
- WF0: n=227 PF 0.72841082 exp=-22.07 net=-5010.88 dd=11.35 LONG PF 0.64833833 n=55 SHORT PF 0.75582621 n=172
- WF1: n=245 PF 0.89804538 exp=-7.93 net=-1943.99 dd=10.84 LONG PF 1.79707679 n=52 SHORT PF 0.72736488 n=193
- WF2: n=225 PF 1.00119998 exp=0.09 net=19.79 dd=9.19 LONG PF 0.73644902 n=83 SHORT PF 1.17608115 n=142
- WF3: n=261 PF 1.02572088 exp=1.94 net=505.97 dd=13.67 LONG PF 0.73108554 n=53 SHORT PF 1.11250237 n=208
- WF4: n=224 PF 0.70743096 exp=-24.65 net=-5522.05 dd=17.10 LONG PF 1.18245642 n=93 SHORT PF 0.47367484 n=131
- WF5: n=254 PF 0.69987734 exp=-24.53 net=-6231.57 dd=11.72 LONG PF 0.64835905 n=104 SHORT PF 0.73694780 n=150
### funding_extreme_momentum_exhaustion|enhanced|continuation
- WF0: n=774 PF 0.84617164 exp=-11.91 net=-9217.14 dd=18.77 LONG PF 0.85827152 n=621 SHORT PF 0.79603214 n=153
- WF1: n=669 PF 0.83303626 exp=-13.17 net=-8811.46 dd=21.48 LONG PF 0.87189440 n=512 SHORT PF 0.71337793 n=157
- WF2: n=447 PF 0.43781160 exp=-50.75 net=-22684.09 dd=21.59 LONG PF 0.33458011 n=301 SHORT PF 0.68257430 n=146
- WF3: n=494 PF 0.95518611 exp=-3.39 net=-1675.46 dd=12.93 LONG PF 0.80341646 n=316 SHORT PF 1.28843538 n=178
- WF4: n=481 PF 0.62448425 exp=-31.69 net=-15240.77 dd=16.54 LONG PF 0.69672587 n=308 SHORT PF 0.51517152 n=173
- WF5: n=422 PF 0.66285868 exp=-27.73 net=-11702.37 dd=16.92 LONG PF 0.58492110 n=251 SHORT PF 0.78990165 n=171

## 5–6. LONG / SHORT and trade count (OOS BASE frozen enhanced)
- `funding_basis_rv|enhanced|continuation` trades=477 uniqueSymbols=10 uniqueDays=119 uniqueWeeks=21
  LONG n=197 PF 1.14278653 n=197 exp=9.81 wr=41.12; SHORT n=280 PF 0.74947090 n=280 exp=-20.33 wr=31.79
- `funding_extreme_momentum_exhaustion|enhanced|continuation` trades=1031 uniqueSymbols=10 uniqueDays=142 uniqueWeeks=21
  LONG n=719 PF 1.02769240 n=719 exp=2.00 wr=40.75; SHORT n=312 PF 0.74513935 n=312 exp=-20.28 wr=30.77

## 7. Symbols
### funding_basis_rv|enhanced|continuation
- symbols with trades=10 profitable=4 losing=6 medianPF=0.95 meanPF=0.93 best=1.21 worst=0.58 topTwoShare=40.80%
  - ADAUSDT: n=41 PF 1.20757418 n=41 exp=14.57 net=597.42 LONG PF 3.53708496 n=10 SHORT PF 0.85195321 n=31
  - AVAXUSDT: n=49 PF 0.58465751 n=49 exp=-38.02 net=-1862.97 LONG PF 0.25934825 n=8 SHORT PF 0.65092175 n=41
  - BNBUSDT: n=41 PF 1.21239818 n=41 exp=14.38 net=589.47 LONG PF 1.81364722 n=33 SHORT PF 0.00000000 n=8
  - BTCUSDT: n=51 PF 1.21496990 n=51 exp=14.83 net=756.49 LONG PF 1.16225741 n=27 SHORT PF 1.27660652 n=24
  - DOGEUSDT: n=58 PF 0.75360845 n=58 exp=-19.11 net=-1108.28 LONG PF 0.59740309 n=22 SHORT PF 0.86604927 n=36
  - ETHUSDT: n=56 PF 0.90465014 n=56 exp=-7.23 net=-405.02 LONG PF 1.28221739 n=32 SHORT PF 0.53858756 n=24
  - LINKUSDT: n=39 PF 1.16332448 n=39 exp=10.98 net=428.29 LONG PF 3.42116762 n=9 SHORT PF 0.83520496 n=30
  - LTCUSDT: n=37 PF 0.69075166 n=37 exp=-24.91 net=-921.49 LONG PF 1.11373507 n=20 SHORT PF 0.34477482 n=17
  - SOLUSDT: n=44 PF 0.58149881 n=44 exp=-36.50 net=-1605.89 LONG PF 0.00000000 n=7 SHORT PF 0.75030014 n=37
  - XRPUSDT: n=61 PF 0.94837842 n=61 exp=-3.73 net=-227.50 LONG PF 0.89806094 n=29 SHORT PF 0.99540185 n=32
### funding_extreme_momentum_exhaustion|enhanced|continuation
- symbols with trades=10 profitable=5 losing=5 medianPF=1.06 meanPF=0.94 best=1.22 worst=0.38 topTwoShare=38.43%
  - ADAUSDT: n=154 PF 1.13396019 n=154 exp=9.45 net=1455.15 LONG PF 1.23533635 n=119 SHORT PF 0.83945749 n=35
  - AVAXUSDT: n=107 PF 0.84752590 n=107 exp=-12.11 net=-1296.24 LONG PF 0.97491428 n=72 SHORT PF 0.62285760 n=35
  - BNBUSDT: n=57 PF 1.21304424 n=57 exp=13.97 net=796.11 LONG PF 1.54622664 n=48 SHORT PF 0.21434181 n=9
  - BTCUSDT: n=70 PF 0.37713947 n=70 exp=-53.89 net=-3772.23 LONG PF 0.43092367 n=53 SHORT PF 0.22694587 n=17
  - DOGEUSDT: n=114 PF 0.74545128 n=114 exp=-20.72 net=-2362.28 LONG PF 0.69396439 n=82 SHORT PF 0.90539624 n=32
  - ETHUSDT: n=91 PF 1.05668456 n=91 exp=4.11 net=374.04 LONG PF 1.26377655 n=57 SHORT PF 0.77996936 n=34
  - LINKUSDT: n=135 PF 0.88919278 n=135 exp=-7.89 net=-1065.40 LONG PF 0.96712876 n=97 SHORT PF 0.70628138 n=38
  - LTCUSDT: n=91 PF 0.73623602 n=91 exp=-21.22 net=-1931.21 LONG PF 0.89210933 n=59 SHORT PF 0.47887859 n=32
  - SOLUSDT: n=98 PF 1.22235986 n=98 exp=16.17 net=1584.87 LONG PF 1.28815571 n=66 SHORT PF 1.09389640 n=32
  - XRPUSDT: n=114 PF 1.17584589 n=114 exp=11.63 net=1325.26 LONG PF 1.27975805 n=66 SHORT PF 1.04256501 n=48

## 8. Regimes
Causal 40-bar classifier at fill time. HIGH_VOL / LOW_VOL take precedence over trend labels. n<20 = INSUFFICIENT_DATA.
### funding_basis_rv|enhanced|continuation
- STRONG_BULL: INSUFFICIENT_DATA n=3
- BULL: INSUFFICIENT_DATA n=18
- RANGE: n=25 PF 0.85885380 n=25 exp=-11.23
- BEAR: INSUFFICIENT_DATA n=12
- STRONG_BEAR: INSUFFICIENT_DATA n=0
- HIGH_VOL: INSUFFICIENT_DATA n=0
- LOW_VOL: n=394 PF 0.89609627 n=394 exp=-7.77
- TRANSITION: n=25 PF 0.61449881 n=25 exp=-38.24
### funding_extreme_momentum_exhaustion|enhanced|continuation
- STRONG_BULL: INSUFFICIENT_DATA n=5
- BULL: n=109 PF 1.07301355 n=109 exp=5.81
- RANGE: n=35 PF 0.99141106 n=35 exp=-0.62
- BEAR: INSUFFICIENT_DATA n=14
- STRONG_BEAR: INSUFFICIENT_DATA n=0
- HIGH_VOL: INSUFFICIENT_DATA n=7
- LOW_VOL: n=753 PF 0.87639985 n=753 exp=-9.13
- TRANSITION: n=108 PF 0.95643915 n=108 exp=-3.35

## 9. Cost stress (OOS Combined)
- `funding_basis_rv|enhanced|continuation` | BASE n=477 PF 0.89629023 exp=-7.88 | MILD n=477 PF 0.87260493 exp=-9.79 | HIGH n=477 PF 0.85667733 exp=-11.13 | STRESS n=477 PF 0.82275499 exp=-14.01
- `funding_extreme_momentum_exhaustion|enhanced|continuation` | BASE n=1031 PF 0.93616736 exp=-4.74 | MILD n=1031 PF 0.92355424 exp=-5.71 | HIGH n=1029 PF 0.90523671 exp=-7.14 | STRESS n=1028 PF 0.87205674 exp=-9.80

## 10. Funding costs
Identity: GrossPnl − Fees − Slippage − FundingPaid = NetPnl. FundingPaid > 0 means the book paid funding.
- `funding_basis_rv|enhanced|continuation` gross=-794.35 fees=1892.53 slippage=946.26 fundingPaid=126.34 net=-3759.48 recon=-3759.48 gap=0.00
- `funding_extreme_momentum_exhaustion|enhanced|continuation` gross=1603.08 fees=4008.55 slippage=2004.28 fundingPaid=482.17 net=-4891.91 recon=-4891.91 gap=0.00

## 11. Baseline comparison (OOS BASE)
- `funding_basis_rv|enhanced|continuation` vs `funding_basis_rv|baseline|continuation`: ΔPF=0.16 ΔExp=12.35 ΔWR=2.94 ΔNet=72611.98 ΔDrawdown=-40.56 enhanced PF 0.89629023 baseline PF 0.73188852
- `funding_extreme_momentum_exhaustion|enhanced|continuation` vs `funding_extreme_momentum_exhaustion|baseline|continuation`: ΔPF=0.11 ΔExp=8.49 ΔWR=3.78 ΔNet=27344.41 ΔDrawdown=-15.54 enhanced PF 0.93616736 baseline PF 0.82243342

## 12. Parameter stability (IS only)
Neighborhood was not used to pick OOS parameters. Frozen defaults remain the OOS configuration.
- `funding_basis_rv|enhanced|continuation` IS frozen n=1644 PF 0.83110198 n=1644 exp=-13.09
  - `funding_basis_rv|enhanced|z1.75` IS_PARAM n=2891 PF 0.84996841 n=2891 exp=-11.43
  - `funding_basis_rv|enhanced|z2.25` IS_PARAM n=889 PF 0.79003714 n=889 exp=-17.11
  - PARAMETER_STABLE on IS expectancy sign.
- `funding_extreme_momentum_exhaustion|enhanced|continuation` IS frozen n=4080 PF 0.81799612 n=4080 exp=-13.24
  - `funding_extreme_momentum_exhaustion|enhanced|p075` IS_PARAM n=3941 PF 0.80730177 n=3941 exp=-13.97
  - `funding_extreme_momentum_exhaustion|enhanced|p125` IS_PARAM n=4787 PF 0.83477325 n=4787 exp=-11.94
  - PARAMETER_STABLE on IS expectancy sign.

## 13. Bootstrap uncertainty
Seeded bootstrap of OOS BASE trade PnL. Not a proof of future profitability.
- `funding_basis_rv|enhanced|continuation` n=477 draws=1000 seed=4; exp -7.88 [-21.42, 5.54]; WR 35.64 [31.24, 40.04]; net -3759.48 [-10218.65, 2640.22]
- `funding_extreme_momentum_exhaustion|enhanced|continuation` n=1031 draws=1000 seed=4; exp -4.74 [-13.68, 4.14]; WR 37.73 [34.92, 40.45]; net -4891.91 [-14106.37, 4266.76]

## 14. Multiple-testing caveat
Phase 3 tested 19 enhanced hypotheses (8 families × continuation/contrarian plus small extras, plus `vp_vwap_reversion`). 2 continuation hypotheses entered Phase 4. Observed edge can plausibly include selection bias. This is not statistical significance and not a profitability claim.

## 15. Failure modes
- Phase 3 OOS was ~3 weeks; Phase 4 uses the maximum ingested history but OOS is still the last 20% of that window.
- One-direction or one-symbol concentration. SHORT sample on exhaustion may remain thin.
- Cost fragility at 1.5x. Parameter neighborhood evaluated on IS only.
- No OI. Taker not used. No fabricated series.

## 16. Final research status
- `funding_basis_rv|enhanced|continuation`: OOS_FAILED n=477 PF 0.89629023 exp=-7.88
- `funding_extreme_momentum_exhaustion|enhanced|continuation`: OOS_FAILED n=1031 PF 0.93616736 exp=-4.74

VALIDATED_FOR_PAPER is not assigned. LIVE remains OFF. Risk Engine was not modified.

## Run notes
- COVERAGE requestedDays=730 fundingSpan=2024-09-20..2026-09-19. OI not loaded.
- Phase 4 deep validation. LIVE disabled. Frozen five / Risk Engine unchanged. No 528-universe run.
- Frozen Phase 3 continuation parameters. No OOS tuning. No OI. No VALIDATED_FOR_PAPER.
- Phase 3 tested 19 enhanced hypotheses; 2 entered Phase 4.
- BTCUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- BTCUSDT 5m: bars=210240 cache miss dl=3 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- BTCUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- BTCUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- ETHUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- ETHUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- ETHUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- ETHUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- BNBUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- BNBUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- BNBUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- BNBUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- SOLUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- SOLUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- SOLUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- SOLUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- XRPUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- XRPUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- XRPUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- XRPUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- DOGEUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- DOGEUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- DOGEUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- DOGEUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- ADAUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- ADAUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- ADAUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- ADAUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- AVAXUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- AVAXUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- AVAXUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- AVAXUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- LINKUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- LINKUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- LINKUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- LINKUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
- LTCUSDT funding n=2190 first=2024-09-20 last=2026-09-19
- LTCUSDT 5m: bars=210241 cache miss dl=4 mark=210239 index=210239 basis=210239 firstBar=2024-09-19
- LTCUSDT 15m: bars=70079 cache hit dl=0 mark=70079 index=70079 basis=70079 firstBar=2024-09-19
- LTCUSDT 1h: bars=17520 cache hit dl=0 mark=17519 index=17519 basis=17519 firstBar=2024-09-19
