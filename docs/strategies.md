# Strategies

This page is the **signal**. The Isolated book on Risk still sizes every entry and still owns SL/TP. Templates do not place dollar size, do not own stops, and do not reverse a position on the same bar.

They skip some noisy setups. That is not a profit claim and it is not a cap on loss. Actual loss can still exceed Planned Risk because of gaps, fees, slippage, and liquidation.

Paper, LIVE, and Backtest all run the same `IStrategyEngine`. Used strategy versions stay immutable. Running bots keep the old JSON until you Stop, then Start.

## Templates

All five templates fire on **closed** candles only.

1. **EMA RSI Trend** — LONG when EMA fast crosses above slow, close is above the slow EMA, and RSI is between min and max (default 50–68). SHORT is the opposite. Exit on the opposite EMA cross.
2. **MACD Trend** — LONG when MACD crosses above its signal, the histogram is positive, and close is above the slow EMA. SHORT is the opposite. Exit on the opposite MACD cross.
3. **RSI Pullback** — LONG only in an uptrend (close above slow EMA) when RSI crosses up through oversold (default 30). SHORT only in a downtrend at overbought (70). Exit when RSI recrosses 50 or EMA reverses.
4. **Bollinger Reversion** — LONG when the previous close is below the lower band, this close is back inside, and close is still above the slow EMA. SHORT is the mirror. Exit at the mid band.
5. **Donchian Breakout** — LONG when the previous close is still inside the prior-N high and this close breaks above it (default 20). SHORT is the mirror. Exit on the opposite band or an EMA reverse.

Allowed side is Long (default), Short, or Both. Both LONG and SHORT rules exist on every template. A blocked direction while a position is open flattens (`Exit`); it does not reverse. A blocked direction while flat is `NoAction`.

Donchian entry is a **break event**: the previous close must still be inside the prior-N channel, then this close breaks it. Staying outside the channel does not re-fire every bar. The channel at bar T excludes that bar's high/low.

Quality filters skip some noisy setups. That is not a profit claim and it is not a cap on loss.

## Execution model

Backtest (and the validation harness) fills at the **next bar open** after a closed-bar signal, with fees and slippage. Stop is assumed to win if the same bar also prints TP. Paper/LIVE still fill at last price today; that difference is documented and is not a profitability claim.

Funding is **not** applied in replay (`EXCLUDING_FUNDING`).

Used strategy versions stay immutable. Running bots keep the old JSON until you Stop, then Start. Disable a strategy without deleting it (`IsEnabled`). Validation status is factual only (`VALIDATION_PENDING`, `INSUFFICIENT_DATA`, `IMPLEMENTATION_ERROR`, `UNSTABLE`, `OOS_DEGRADATION`, `VALIDATED_FOR_PAPER`, `REJECTED_BY_TESTS`). Nothing auto-starts LIVE.

## Quality filters

Shared, editable, and on by default for new strategies:

- Volume confirmation: bar volume greater than SMA(volume, lookback 20).
- ATR% band: skip if ATR/close is below min (chop) or above max (spike). Defaults about 0.15%–4% on 5m.

The sample EMA RSI row is seeded with filters **off** so a running bot does not change behavior until you save and restart it.

Implementation inspection: [strategy-implementation-audit.md](strategy-implementation-audit.md). Frozen-default historical report: [strategy-audit-report.md](strategy-audit-report.md). Neither is a profit claim.

## Preview

`GET /api/trading/strategies/{id}/preview?symbol=&limit=` runs the engine on closed public klines with no RiskEngine and no orders. The Strategies page shows the last bars as Buy / Sell / Exit / Hold / NoAction. Size, SL, and TP still come from Risk.

## What is out of scope

No visual condition builder, no Martingale, no averaging down, no Cross margin, and no strategy-owned dollar size.
