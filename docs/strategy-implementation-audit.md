# Strategy implementation audit

Factual inspection of the five closed-candle templates. This is **not** a profitability ranking and not a forecast.

Shared engine: `IStrategyEngine` → `StrategyTemplateEvaluator`. Risk Engine owns size, Isolated SL, and Isolated TP. Templates emit Buy / Sell / Exit / Hold / NoAction only. Forming candles are dropped at ingest. Backtest fills the **next bar open**. Paper/LIVE still fill at last price — that difference is documented, not treated as a strategy bug.

Statuses below describe **code**, not live results. Historical validation lives in `docs/strategy-audit-report.md`.

---

## Shared

| Check | Finding |
|---|---|
| Implementation status | Templates evaluate the last **closed** bar only. |
| Duplicate signals | Cross templates fire on a transition. Donchian previously fired on a level (`close > channel`) and could re-enter every new high after flatten. |
| Cooldown | None inside the strategy. Risk book `CooldownMinutes` still applies after a losing Isolated trade. |
| Conflicting signals | One Isolated position per coin. Opposite Buy/Sell while open flattens only. |
| Parameter handling | Save path calls `Validate`. Evaluate previously did not — inverted periods could still run. |
| Timeframe / coin | Version stores one timeframe and the strategy stores coin scope. Catalog declares supported 5m / 15m / 1h and LONG / SHORT. |
| Missing data | Fewer than two closed candles → NoAction. |
| Look-ahead | No future bars. Donchian channel excludes the current high/low. Bollinger bands include the current close (contemporaneous, not future-bar). |
| Repainting | Signal at closed bar T must stay the same when later bars are appended. Locked by tests. |
| SL / TP / size | Not computed by templates. |

---

## 1. Bollinger Reversion

**Classification:** mean reversion with a slow-EMA side filter (not a pure fade).

| | |
|---|---|
| Implementation status | Implemented. No ADX / band-width / HTF in this pass. |
| LONG entry | Previous close below the lower band, this close back inside (`>= lower`), close still above slow EMA. |
| LONG exit | Close `>=` mid band. |
| SHORT entry | Previous close above the upper band, this close back inside, close still below slow EMA. |
| SHORT exit | Close `<=` mid band. |
| Potential bugs | Slow-EMA filter blocks fades against the local trend — a design choice, not a coding error. |
| Potential look-ahead | Bands at bar `i` include close `i`. Not future information. |
| Potential repainting | Closed-bar BB is stable once the bar is closed. |
| Potential duplicate-entry | Re-entry needs another excursion outside the band. |
| Potential exit issue | Mid-band exit can leave a runner; Isolated SL/TP still apply. |
| Parameter problems | Period >= 5, stddev > 0 at save. Evaluate now rejects inverted EMA. |
| Recommended changes | This pass: tests only. Do not add ADX until after frozen-default validation. |

---

## 2. Donchian Breakout

**Classification:** breakout / trend following.

| | |
|---|---|
| Implementation status | Channel indexing was already correct (`[i-N, i)`). Entry was a **level**, not a **break event**. |
| LONG entry | Close breaks above the prior-N high **and** the previous close was not already outside that prior channel. |
| LONG exit | Close below the prior-N low, or EMA fast crosses below slow. Isolated SL/TP still apply. |
| SHORT entry | Mirror on the prior-N low. |
| SHORT exit | Mirror / EMA reverse. |
| Potential bugs | **Fixed this pass:** every new high while already above the channel could Buy again after flatten. |
| Potential look-ahead | None on the channel. Current bar high/low are excluded. |
| Potential repainting | Closed-bar channel is stable. |
| Potential duplicate-entry | **Fixed this pass** via break-event + “previous bar was inside.” |
| Potential exit issue | Opposite-band exit is still a level while a position is open (missing an exit is worse than a sticky Exit). |
| Parameter problems | Length >= 5. |
| Recommended changes | Event entry (done). No ATR trailing on the strategy — Risk owns stops. |

---

## 3. EMA RSI Trend

**Classification:** trend + momentum. Not `RSI > 50` as the entry.

| | |
|---|---|
| Implementation status | Implemented. Entry is an EMA **cross**, not a standing fast>slow state. |
| LONG entry | Fast EMA crosses above slow, close above slow, RSI in `[min, longMax]` (default 50–68). |
| LONG exit | Fast crosses below slow. |
| SHORT entry | Fast crosses below slow, close below slow, RSI in `[100-longMax, 100-min]`. |
| SHORT exit | Fast crosses above slow. |
| Potential bugs | None found in the cross/RSI-band structure. |
| Potential look-ahead | None. |
| Potential repainting | Closed-bar EMA/RSI are stable. |
| Potential duplicate-entry | Cross prevents a signal on every bar while the trend remains. |
| Potential exit issue | Opposite cross only; Isolated SL/TP still apply. |
| Parameter problems | Slow must be greater than fast. |
| Recommended changes | Keep cross + band. Do not switch to RSI 50. |

---

## 4. MACD Trend

**Classification:** momentum / trend. Crossover is `prev MACD <= prev signal AND curr MACD > curr signal` (and the inverse).

| | |
|---|---|
| Implementation status | Implemented. Histogram sign and slow-EMA side are extra filters. |
| LONG entry | MACD crosses above signal, histogram > 0, close above slow EMA. |
| LONG exit | MACD crosses below signal. |
| SHORT entry | Mirror. |
| SHORT exit | MACD crosses above signal. |
| Potential bugs | None found in crossover detection. |
| Potential look-ahead | None. |
| Potential repainting | Closed-bar MACD is stable. |
| Potential duplicate-entry | Cross prevents standing-state re-entry. |
| Potential exit issue | Opposite cross only. |
| Parameter problems | Slow > fast, signal >= 2. |
| Recommended changes | No EMA200/ADX in this pass. |

---

## 5. RSI Pullback

**Classification:** trend pullback, not blind RSI mean reversion.

| | |
|---|---|
| Implementation status | Implemented. Trend filter is local slow EMA. No HTF in this pass. |
| LONG entry | Close above slow EMA and RSI crosses up through oversold (default 30). |
| LONG exit | RSI crosses below 50 or EMA reverses. |
| SHORT entry | Close below slow EMA and RSI crosses down through overbought (default 70). |
| SHORT exit | RSI crosses above 50 or EMA reverses. |
| Potential bugs | None found in the cross structure. |
| Potential look-ahead | None. |
| Potential repainting | Closed-bar RSI is stable. |
| Potential duplicate-entry | Oversold/overbought **cross** prevents a signal on every bar in the zone. |
| Potential exit issue | Midline recross can exit early; Isolated SL/TP still apply. |
| Parameter problems | Oversold 0–50, overbought 50–100. |
| Recommended changes | Keep configurable thresholds. Do not add HTF until after frozen-default validation. |

---

## Disposition (code)

No template is deleted. None is labeled BEST / PROFITABLE / guaranteed. `ValidationStatus` starts at `VALIDATION_PENDING` until the harness report assigns a factual status. `IsEnabled` can turn a row off without deleting it. Nothing in this audit enables LIVE.
