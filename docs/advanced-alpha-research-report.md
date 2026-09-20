# Advanced alpha research report

Phase 1 implementation pilot only. Frozen five and advanced six were not modified. LIVE = OFF.
Do **not** treat any candidate as profitable. Combined PF is cost-inclusive. Win rate is not the selection criterion.
Phase 2 / 528-universe / parameter freeze / untouched OOS retune are **not** run here.

## Phase 1 verdict

No candidate is VALIDATED_FOR_PAPER. None is LIVE-ready. Combined OOS PF around 1.0 is not a robust edge.

Pilot: 10 liquid coins × 5m/15m/1h × ~90 days. Books=5010. Universe PF = Σ winning net PnL / |Σ losing net PnL|. Empty WF windows = NO_TRADES. No PF=99 sentinel.

Closest Combined OOS BASE figures (`market_structure_trend` 1.12, `vol_squeeze_structure` 1.02, `vwap_breakout_volume` 1.01, `liq_sweep_continuation` 1.00) are LONG-concentrated and mostly COST_FRAGILE at 1.5×/2× fees. SHORT Combined PF is below 1 on every runnable family. That is not a repeatable cost-inclusive edge.

## Execution (Model B, unchanged)

- Signal on closed candle t. Fill at next open (T+1).
- Same-bar SL and TP: stop before take-profit (conservative).
- Fees + slippage on every fill. Funding EXCLUDING_FUNDING unless a timestamp-aligned series exists (it does not).
- SuggestedStop / SuggestedTakeProfit are diagnostics. Risk Engine sizes and may override.
- Isolated margin. No martingale. No auto LIVE.

## Default parameters (alpha families)

- EntryLookback = 20 (profile window 40 for VP)
- ValueAreaPercent = 0.70
- ZScoreEntry = 2.0
- SweepDepthAtr = 0.15
- SwingLength = 3 (causal: pivot at i-n confirmed at i)
- MinimumAdx = 25 (mean-reversion skip when ADX ≥ 25)
- MaxVwapDistanceAtr = 1.5
- StopAtrMultiplier = 1.5
- CompressionPercentile = 0.20
- BreakoutRelativeVolume = 1.2
- RelativeVolumePeriod = 20
- MinimumRelativeVolume = 0.8
- AllowedSide = Both (LONG and SHORT evaluated independently)

## Formulas

- Volume profile: typical = (H+L+C)/3; 24 bins on window; POC = max-volume bin; VA expands from POC until 70% volume; VAL/VAH = edges.
- VWAPDistanceATR = |Close − sessionVWAP| / ATR
- FlowImbalance = (TakerBuy − TakerSell) / (TakerBuy + TakerSell) when TakerBuy > 0
- MomentumN = (Close[t] − Close[t−N]) / ATR[t]
- Z = (Close − rollingMean) / rollingStdDev
- Causal swing: low at k confirmed at k+n iff Low[k] is min of [k−n, k+n] and k+n ≤ signal index
- HTF: last closed HTF candle with CloseTime ≤ signal CloseTime
- PF = Σ positive net PnL / |Σ negative net PnL|
- Expectancy = NetPnL / trades (cost-inclusive)

## Entry / stop (runnable families)

| Id | LONG | SHORT | Stop |
| --- | --- | --- | --- |
| vp_vwap_reversion | Low < VAL, close back above VAL, close < VWAP, ADX < 25 | High > VAH, close back below VAH, close > VWAP | Beyond rejection ± ATR×SweepDepth; TP = POC |
| liq_sweep_reversal | Sweep confirmed swing low then close back above | Sweep confirmed swing high then close back below | Sweep extreme ± 0.25 ATR |
| liq_sweep_continuation | Sweep high, close holds above, close > EMA, volume | Inverse | Level ∓ ATR×Stop |
| vwap_deviation_reversion | Dist ≥ 1.5 ATR below VWAP + rejection candle, ADX < 25 | Dist ≥ 1.5 ATR above + rejection | Rejection extreme |
| vwap_breakout_volume | Close > VWAP, VWAP/EMA slope up, Donchian break, rel vol, transition | Inverse | Risk Engine |
| failed_breakout_reversal | Prior close broke Donchian low then reclaim | Prior close broke Donchian high then fail | Prior extreme |
| vol_squeeze_structure | BB/Keltner squeeze + ATR expansion + Donchian break + volume, transition | Inverse | Close ± ATR×Stop |
| market_structure_trend | HH/HL + new HH transition | LH/LL + new LL | Last opposite swing |
| market_structure_pullback | Bullish structure + pullback to EMA/VWAP + confirmation | Inverse | Pullback extreme |
| atr_normalized_momentum | MomentumN ≥ 1, close > trend EMA, transition | ≤ −1 and close < EMA | Close ± ATR×Stop |
| mtf_trend_structure | Last completed HTF EMA20>EMA50 + LTF pullback confirm | Inverse HTF | LTF extreme |
| zscore_mean_reversion | Z ≤ −2, ADX < 25 | Z ≥ +2 | Close ± ATR×Stop |

Exit while in a position: Hold; Risk Engine owns SL/TP. No averaging down.

## Dataset inventory
- Available: OHLCV, volume, reconstructed volume profile (typical-price × volume), session VWAP, ATR, EMA, RSI, ADX, Bollinger, Keltner, Donchian, causal swings.
- Unavailable (not fabricated): historical open interest, funding rate, mark/index/basis, order book, liquidations, causal pair-universe snapshots.
- Taker buy volume: Binance kline index 9. 5m/15m re-download parsed taker; 1h cache hit often has TakerBuy=0. Tiny sample (n=7) is INSUFFICIENT_DATA, not an edge.

## Status (OOS BASE Combined)
- **Volume Profile VWAP Mean Reversion** (`vp_vwap_reversion`): OOS_FAILED; family MEAN REVERSION; OOS n=328 PF 0,59172023; OHLCV and volume. Volume profile reconstructed from typical-price × volume bins.
- **Liquidity Sweep Reversal** (`liq_sweep_reversal`): OOS_FAILED; family REVERSAL; OOS n=458 PF 0,65508140; Closed kline candles only.
- **Liquidity Sweep Breakout Continuation** (`liq_sweep_continuation`): RESEARCHING; family BREAKOUT / TREND; OOS n=600 PF 1,00255927; Closed kline candles only.
- **Funding Basis Carry Relative Value** (`funding_basis_rv`): DATA_UNAVAILABLE; family FUTURES / FLOW; OOS n=0 PF N/A; Historical funding, mark, and index/basis, timestamp-aligned. Missing series = DATA_UNAVAILABLE.
- **Funding Extreme OI Price Reversal** (`funding_oi_reversal`): DATA_UNAVAILABLE; family REVERSAL; OOS n=0 PF N/A; Historical funding and open interest, timestamp-aligned. Missing series = DATA_UNAVAILABLE.
- **Taker Flow Volume Imbalance Momentum** (`taker_flow_momentum`): INSUFFICIENT_DATA; family FUTURES / FLOW; OOS n=7 PF 0,00000000; Kline taker buy volume (Binance index 9). Missing field = DATA_UNAVAILABLE. Not fabricated.
- **OI Price Volume Regime** (`oi_price_volume_regime`): DATA_UNAVAILABLE; family FUTURES / FLOW; OOS n=0 PF N/A; Historical open interest plus OHLCV, timestamp-aligned. Missing OI = DATA_UNAVAILABLE.
- **VWAP Deviation Reversion** (`vwap_deviation_reversion`): OOS_FAILED; family MEAN REVERSION; OOS n=334 PF 0,80144577; Closed kline candles only.
- **VWAP Breakout Volume** (`vwap_breakout_volume`): OOS_FAILED; family BREAKOUT / TREND; OOS n=510 PF 1,01246060; Closed kline candles only.
- **Failed Breakout Reversal** (`failed_breakout_reversal`): OOS_FAILED; family REVERSAL; OOS n=381 PF 0,77893165; Closed kline candles only.
- **Volatility Squeeze Structure Break** (`vol_squeeze_structure`): OOS_FAILED; family BREAKOUT / TREND; OOS n=163 PF 1,01958553; Closed kline candles only.
- **Market Structure Trend Continuation** (`market_structure_trend`): OOS_FAILED; family TREND / STRUCTURE; OOS n=348 PF 1,12478747; Closed kline candles only.
- **Market Structure Pullback** (`market_structure_pullback`): OOS_FAILED; family TREND / STRUCTURE; OOS n=469 PF 0,84132655; Closed kline candles only.
- **ATR-Normalized Momentum** (`atr_normalized_momentum`): OOS_FAILED; family BREAKOUT / TREND; OOS n=404 PF 0,91404600; Closed kline candles only.
- **Multi-Timeframe Trend LTF Structure** (`mtf_trend_structure`): OOS_FAILED; family TREND / STRUCTURE; OOS n=369 PF 0,88702767; Entry timeframe OHLCV plus last completed HTF candles only.
- **Z-Score Statistical Mean Reversion** (`zscore_mean_reversion`): OOS_FAILED; family MEAN REVERSION; OOS n=400 PF 0,75065472; Closed kline candles only.
- **Crypto Pairs Statistical Arbitrage** (`crypto_pairs_arb`): DATA_UNAVAILABLE; family MEAN REVERSION; OOS n=0 PF N/A; Multi-symbol OHLCV with causal pair selection windows. Single-book replay = DATA_UNAVAILABLE.
- **Cross-Sectional Relative Strength Momentum** (`xs_relative_strength`): DATA_UNAVAILABLE; family FUTURES / FLOW; OOS n=0 PF N/A; Universe snapshot at each timestamp. Single-book replay = DATA_UNAVAILABLE.
- **Regime-Adaptive Strategy Router** (`regime_strategy_router`): RESEARCHING; family ROUTER; OOS n=0 PF N/A; Closed kline candles only.

## Per-strategy research notes
### Volume Profile VWAP Mean Reversion
- Hypothesis / formula / entry / exit / stop: see engine `vp_vwap_reversion` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: OHLCV and volume. Volume profile reconstructed from typical-price × volume bins.
- Status: OOS_FAILED
- Combined OOS: n=328 winRate=28.96% exp=-35.11 PF 0,59172023 net=-11515.02 fees=1288.46
- LONG PF 0,79202725 n=119 exp -16.53 net -1967.23; SHORT PF 0,49063980 n=209 exp -45.68 net -9547.79.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Liquidity Sweep Reversal
- Hypothesis / formula / entry / exit / stop: see engine `liq_sweep_reversal` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: OOS_FAILED
- Combined OOS: n=458 winRate=31.00% exp=-29.21 PF 0,65508140 net=-13376.48 fees=1793.59
- LONG PF 0,95141207 n=226 exp -3.61 net -815.84; SHORT PF 0,42881489 n=232 exp -54.14 net -12560.64.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Liquidity Sweep Breakout Continuation
- Hypothesis / formula / entry / exit / stop: see engine `liq_sweep_continuation` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: RESEARCHING
- Combined OOS: n=600 winRate=39.83% exp=0.19 PF 1,00255927 net=111.82 fees=2371.75
- LONG PF 1,38101731 n=358 exp 24.10 net 8628.16; SHORT PF 0,59535504 n=242 exp -35.19 net -8516.35.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Funding Basis Carry Relative Value
- Hypothesis / formula / entry / exit / stop: see engine `funding_basis_rv` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Historical funding, mark, and index/basis, timestamp-aligned. Missing series = DATA_UNAVAILABLE.
- Status: DATA_UNAVAILABLE
- Combined OOS: n=0 winRate=0.00% exp=0.00 PF N/A net=0.00 fees=0.00
- LONG PF N/A n=0 exp 0.00 net 0.00; SHORT PF N/A n=0 exp 0.00 net 0.00.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Funding Extreme OI Price Reversal
- Hypothesis / formula / entry / exit / stop: see engine `funding_oi_reversal` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Historical funding and open interest, timestamp-aligned. Missing series = DATA_UNAVAILABLE.
- Status: DATA_UNAVAILABLE
- Combined OOS: n=0 winRate=0.00% exp=0.00 PF N/A net=0.00 fees=0.00
- LONG PF N/A n=0 exp 0.00 net 0.00; SHORT PF N/A n=0 exp 0.00 net 0.00.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Taker Flow Volume Imbalance Momentum
- Hypothesis / formula / entry / exit / stop: see engine `taker_flow_momentum` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Kline taker buy volume (Binance index 9). Missing field = DATA_UNAVAILABLE. Not fabricated.
- Status: INSUFFICIENT_DATA
- Combined OOS: n=7 winRate=0.00% exp=-19.43 PF 0,00000000 net=-136.04 fees=28.01
- LONG PF 0,00000000 n=2 exp -25.48 net -50.96; SHORT PF 0,00000000 n=5 exp -17.02 net -85.08.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### OI Price Volume Regime
- Hypothesis / formula / entry / exit / stop: see engine `oi_price_volume_regime` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Historical open interest plus OHLCV, timestamp-aligned. Missing OI = DATA_UNAVAILABLE.
- Status: DATA_UNAVAILABLE
- Combined OOS: n=0 winRate=0.00% exp=0.00 PF N/A net=0.00 fees=0.00
- LONG PF N/A n=0 exp 0.00 net 0.00; SHORT PF N/A n=0 exp 0.00 net 0.00.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### VWAP Deviation Reversion
- Hypothesis / formula / entry / exit / stop: see engine `vwap_deviation_reversion` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: OOS_FAILED
- Combined OOS: n=334 winRate=34.13% exp=-15.71 PF 0,80144577 net=-5246.48 fees=1331.02
- LONG PF 0,98535686 n=160 exp -1.12 net -179.92; SHORT PF 0,64159774 n=174 exp -29.12 net -5066.56.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### VWAP Breakout Volume
- Hypothesis / formula / entry / exit / stop: see engine `vwap_breakout_volume` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: OOS_FAILED
- Combined OOS: n=510 winRate=40.39% exp=0.90 PF 1,01246060 net=461.24 fees=2019.34
- LONG PF 1,52409617 n=288 exp 31.72 net 9135.04; SHORT PF 0,55714498 n=222 exp -39.07 net -8673.80.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Failed Breakout Reversal
- Hypothesis / formula / entry / exit / stop: see engine `failed_breakout_reversal` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: OOS_FAILED
- Combined OOS: n=381 winRate=34.38% exp=-18.00 PF 0,77893165 net=-6856.82 fees=1510.53
- LONG PF 1,10173330 n=144 exp 7.48 net 1077.43; SHORT PF 0,61156051 n=237 exp -33.48 net -7934.25.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Volatility Squeeze Structure Break
- Hypothesis / formula / entry / exit / stop: see engine `vol_squeeze_structure` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: OOS_FAILED
- Combined OOS: n=163 winRate=39.88% exp=1.45 PF 1,01958553 net=236.13 fees=653.69
- LONG PF 1,62863445 n=89 exp 36.34 net 3234.63; SHORT PF 0,56610425 n=74 exp -40.52 net -2998.51.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Market Structure Trend Continuation
- Hypothesis / formula / entry / exit / stop: see engine `market_structure_trend` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: OOS_FAILED
- Combined OOS: n=348 winRate=43.10% exp=8.74 PF 1,12478747 net=3042.35 fees=1386.47
- LONG PF 1,66521926 n=195 exp 38.24 net 7457.36; SHORT PF 0,66476525 n=153 exp -28.86 net -4415.01.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Market Structure Pullback
- Hypothesis / formula / entry / exit / stop: see engine `market_structure_pullback` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: OOS_FAILED
- Combined OOS: n=469 winRate=36.67% exp=-12.25 PF 0,84132655 net=-5746.64 fees=1847.94
- LONG PF 1,11271733 n=246 exp 7.67 net 1886.69; SHORT PF 0,60811562 n=223 exp -34.23 net -7633.32.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### ATR-Normalized Momentum
- Hypothesis / formula / entry / exit / stop: see engine `atr_normalized_momentum` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: OOS_FAILED
- Combined OOS: n=404 winRate=38.37% exp=-6.51 PF 0,91404600 net=-2629.11 fees=1585.63
- LONG PF 1,27804154 n=221 exp 18.20 net 4022.11; SHORT PF 0,58743343 n=183 exp -36.35 net -6651.22.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Multi-Timeframe Trend LTF Structure
- Hypothesis / formula / entry / exit / stop: see engine `mtf_trend_structure` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Entry timeframe OHLCV plus last completed HTF candles only.
- Status: OOS_FAILED
- Combined OOS: n=369 winRate=37.67% exp=-8.39 PF 0,88702767 net=-3095.58 fees=1440.03
- LONG PF 1,08617139 n=221 exp 5.91 net 1306.07; SHORT PF 0,64052260 n=148 exp -29.74 net -4401.65.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Z-Score Statistical Mean Reversion
- Hypothesis / formula / entry / exit / stop: see engine `zscore_mean_reversion` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: OOS_FAILED
- Combined OOS: n=400 winRate=33.25% exp=-20.66 PF 0,75065472 net=-8263.48 fees=1589.41
- LONG PF 0,96267679 n=201 exp -2.85 net -572.23; SHORT PF 0,56812512 n=199 exp -38.65 net -7691.25.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Crypto Pairs Statistical Arbitrage
- Hypothesis / formula / entry / exit / stop: see engine `crypto_pairs_arb` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Multi-symbol OHLCV with causal pair selection windows. Single-book replay = DATA_UNAVAILABLE.
- Status: DATA_UNAVAILABLE
- Combined OOS: n=0 winRate=0.00% exp=0.00 PF N/A net=0.00 fees=0.00
- LONG PF N/A n=0 exp 0.00 net 0.00; SHORT PF N/A n=0 exp 0.00 net 0.00.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Cross-Sectional Relative Strength Momentum
- Hypothesis / formula / entry / exit / stop: see engine `xs_relative_strength` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Universe snapshot at each timestamp. Single-book replay = DATA_UNAVAILABLE.
- Status: DATA_UNAVAILABLE
- Combined OOS: n=0 winRate=0.00% exp=0.00 PF N/A net=0.00 fees=0.00
- LONG PF N/A n=0 exp 0.00 net 0.00; SHORT PF N/A n=0 exp 0.00 net 0.00.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.

### Regime-Adaptive Strategy Router
- Hypothesis / formula / entry / exit / stop: see engine `regime_strategy_router` in `AlphaStrategyEvaluator`.
- Parameters (defaults): lookback=20, VA=70%, Z=2, sweepDepthATR=0.15, swing=3, ADX skip≥25, VWAP dist=1.5 ATR.
- Data: Closed kline candles only.
- Status: RESEARCHING
- Combined OOS: n=0 winRate=0.00% exp=0.00 PF N/A net=0.00 fees=0.00
- LONG PF N/A n=0 exp 0.00 net 0.00; SHORT PF N/A n=0 exp 0.00 net 0.00.
- Failure modes: cost-inclusive edge not claimed; concentration and OOS must agree before Paper.


## Cost sensitivity (OOS Combined)
- vp_vwap_reversion
  - BASE: PF 0,59172023 n=328 exp -35.11 net -11515.02
  - HIGH: PF 0,55759516 n=330 exp -38.97 net -12860.60
  - STRESS: PF 0,54822013 n=330 exp -40.32 net -13305.71
- liq_sweep_reversal
  - BASE: PF 0,65508140 n=458 exp -29.21 net -13376.48
  - HIGH: PF 0,61847655 n=458 exp -33.16 net -15188.33
  - STRESS: PF 0,59943034 n=462 exp -35.33 net -16323.49
- liq_sweep_continuation
  - BASE: PF 1,00255927 n=600 exp 0.19 net 111.82
  - HIGH: PF 0,92423533 n=610 exp -5.69 net -3469.09
  - STRESS: PF 0,87301540 n=616 exp -9.74 net -6002.07
- funding_basis_rv
  - BASE: PF N/A n=0 exp 0.00 net 0.00
  - HIGH: PF N/A n=0 exp 0.00 net 0.00
  - STRESS: PF N/A n=0 exp 0.00 net 0.00
- funding_oi_reversal
  - BASE: PF N/A n=0 exp 0.00 net 0.00
  - HIGH: PF N/A n=0 exp 0.00 net 0.00
  - STRESS: PF N/A n=0 exp 0.00 net 0.00
- taker_flow_momentum
  - BASE: PF 0,00000000 n=7 exp -19.43 net -136.04
  - HIGH: PF 0,00000000 n=7 exp -22.44 net -157.05
  - STRESS: PF 0,00000000 n=7 exp -25.44 net -178.06
- oi_price_volume_regime
  - BASE: PF N/A n=0 exp 0.00 net 0.00
  - HIGH: PF N/A n=0 exp 0.00 net 0.00
  - STRESS: PF N/A n=0 exp 0.00 net 0.00
- vwap_deviation_reversion
  - BASE: PF 0,80144577 n=334 exp -15.71 net -5246.48
  - HIGH: PF 0,77907367 n=336 exp -17.75 net -5965.43
  - STRESS: PF 0,73364435 n=335 exp -22.01 net -7373.23
- vwap_breakout_volume
  - BASE: PF 1,01246060 n=510 exp 0.90 net 461.24
  - HIGH: PF 0,93384183 n=512 exp -4.95 net -2536.22
  - STRESS: PF 0,90903479 n=512 exp -6.91 net -3540.02
- failed_breakout_reversal
  - BASE: PF 0,77893165 n=381 exp -18.00 net -6856.82
  - HIGH: PF 0,74713531 n=383 exp -21.00 net -8041.45
  - STRESS: PF 0,71963347 n=387 exp -23.69 net -9166.34
- vol_squeeze_structure
  - BASE: PF 1,01958553 n=163 exp 1.45 net 236.13
  - HIGH: PF 0,99503913 n=163 exp -0.37 net -60.44
  - STRESS: PF 0,92138245 n=162 exp -6.08 net -985.16
- market_structure_trend
  - BASE: PF 1,12478747 n=348 exp 8.74 net 3042.35
  - HIGH: PF 1,07136731 n=349 exp 5.11 net 1784.02
  - STRESS: PF 1,01657887 n=350 exp 1.22 net 426.27
- market_structure_pullback
  - BASE: PF 0,84132655 n=469 exp -12.25 net -5746.64
  - HIGH: PF 0,80882395 n=467 exp -15.07 net -7036.39
  - STRESS: PF 0,78382911 n=468 exp -17.35 net -8118.76
- atr_normalized_momentum
  - BASE: PF 0,91404600 n=404 exp -6.51 net -2629.11
  - HIGH: PF 0,88555379 n=406 exp -8.77 net -3558.63
  - STRESS: PF 0,86785784 n=407 exp -10.27 net -4181.28
- mtf_trend_structure
  - BASE: PF 0,88702767 n=369 exp -8.39 net -3095.58
  - HIGH: PF 0,85533373 n=369 exp -10.95 net -4040.76
  - STRESS: PF 0,82581401 n=369 exp -13.42 net -4953.77
- zscore_mean_reversion
  - BASE: PF 0,75065472 n=400 exp -20.66 net -8263.48
  - HIGH: PF 0,72318473 n=401 exp -23.39 net -9378.94
  - STRESS: PF 0,66714613 n=401 exp -29.02 net -11638.23
- crypto_pairs_arb
  - BASE: PF N/A n=0 exp 0.00 net 0.00
  - HIGH: PF N/A n=0 exp 0.00 net 0.00
  - STRESS: PF N/A n=0 exp 0.00 net 0.00
- xs_relative_strength
  - BASE: PF N/A n=0 exp 0.00 net 0.00
  - HIGH: PF N/A n=0 exp 0.00 net 0.00
  - STRESS: PF N/A n=0 exp 0.00 net 0.00
- regime_strategy_router
  - BASE: PF N/A n=0 exp 0.00 net 0.00
  - HIGH: PF N/A n=0 exp 0.00 net 0.00
  - STRESS: PF N/A n=0 exp 0.00 net 0.00

## Walk-forward
TEST windows only. Empty window = NO_TRADES, not PF=0. No-loss window = NO_LOSSES, not PF=99.
- vp_vwap_reversion: windows=120 empty=23 PF 0,27012712 n=123 exp -50.06 net -6156.85
- liq_sweep_reversal: windows=120 empty=3 PF 0,49333448 n=217 exp -36.08 net -7829.98
- liq_sweep_continuation: windows=120 empty=0 PF 0,78871853 n=288 exp -15.32 net -4413.44
- funding_basis_rv: walk-forward not evaluated (DATA_UNAVAILABLE or deferred).
- funding_oi_reversal: walk-forward not evaluated (DATA_UNAVAILABLE or deferred).
- taker_flow_momentum: windows=80 empty=73 PF 0,00000000 n=7 exp -19.43 net -136.04
- oi_price_volume_regime: walk-forward not evaluated (DATA_UNAVAILABLE or deferred).
- vwap_deviation_reversion: windows=120 empty=39 PF 0,20106174 n=98 exp -51.87 net -5082.81
- vwap_breakout_volume: windows=120 empty=2 PF 0,96155052 n=205 exp -2.46 net -505.19
- failed_breakout_reversal: windows=120 empty=26 PF 0,29615564 n=129 exp -49.70 net -6411.62
- vol_squeeze_structure: windows=120 empty=97 PF 0,76312254 n=24 exp -11.10 net -266.50
- market_structure_trend: windows=120 empty=14 PF 1,02767498 n=142 exp 1.70 net 241.56
- market_structure_pullback: windows=120 empty=2 PF 0,88810567 n=237 exp -6.54 net -1549.59
- atr_normalized_momentum: windows=120 empty=5 PF 0,96773674 n=169 exp -1.77 net -298.75
- mtf_trend_structure: windows=80 empty=1 PF 1,46440504 n=118 exp 20.46 net 2413.71
- zscore_mean_reversion: windows=120 empty=5 PF 0,54445728 n=167 exp -30.93 net -5164.70
- crypto_pairs_arb: walk-forward not evaluated (DATA_UNAVAILABLE or deferred).
- xs_relative_strength: walk-forward not evaluated (DATA_UNAVAILABLE or deferred).
- regime_strategy_router: walk-forward not evaluated (DATA_UNAVAILABLE or deferred).

## LIVE / Risk / existing strategies
- LIVE remains OFF. No AUTO_LIVE status exists.
- Risk Engine was not modified. Isolated margin. No martingale.
- Existing 11 strategy evaluators (frozen 5 + advanced 6) were not rewritten.

## Run notes
- Advanced alpha Phase 1 pilot. LIVE disabled. Frozen five and advanced six unchanged. No Paper/LIVE promotion.
- OI, funding, basis, pair-universe, and cross-section snapshots are not fabricated.
- Regime router is deferred and not fit on OOS.
- BTCUSDT 5m: 25920 bars cache miss downloaded 10
- BTCUSDT 15m: 8640 bars cache miss downloaded 4
- BTCUSDT 1h: 2159 bars cache hit downloaded 0
- ETHUSDT 5m: 25920 bars cache miss downloaded 10
- ETHUSDT 15m: 8640 bars cache miss downloaded 4
- ETHUSDT 1h: 2159 bars cache hit downloaded 0
- BNBUSDT 5m: 25920 bars cache miss downloaded 10
- BNBUSDT 15m: 8640 bars cache miss downloaded 4
- BNBUSDT 1h: 2159 bars cache hit downloaded 0
- SOLUSDT 5m: 25920 bars cache miss downloaded 10
- SOLUSDT 15m: 8640 bars cache miss downloaded 4
- SOLUSDT 1h: 2159 bars cache hit downloaded 0
- XRPUSDT 5m: 25920 bars cache miss downloaded 10
- XRPUSDT 15m: 8640 bars cache miss downloaded 4
- XRPUSDT 1h: 2159 bars cache hit downloaded 0
- DOGEUSDT 5m: 25920 bars cache miss downloaded 10
- DOGEUSDT 15m: 8640 bars cache miss downloaded 4
- DOGEUSDT 1h: 2159 bars cache hit downloaded 0
- ADAUSDT 5m: 25920 bars cache miss downloaded 10
- ADAUSDT 15m: 8640 bars cache miss downloaded 4
- ADAUSDT 1h: 2159 bars cache hit downloaded 0
- AVAXUSDT 5m: 25920 bars cache miss downloaded 10
- AVAXUSDT 15m: 8640 bars cache miss downloaded 4
- AVAXUSDT 1h: 2159 bars cache hit downloaded 0
- LINKUSDT 5m: 25920 bars cache miss downloaded 10
- LINKUSDT 15m: 8640 bars cache miss downloaded 4
- LINKUSDT 1h: 2159 bars cache hit downloaded 0
- LTCUSDT 5m: 25920 bars cache miss downloaded 10
- LTCUSDT 15m: 8640 bars cache miss downloaded 4
- LTCUSDT 1h: 2159 bars cache hit downloaded 0
