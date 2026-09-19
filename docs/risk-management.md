# Risk management

This module sizes Isolated USD-M entries from **current available futures**, not from the original deposit, not from equity, and not from a fixed dollar amount. Percentages here are starting defaults you can edit. They are not a profit or safety guarantee.

## Planned Risk, Notional, Isolated margin, Leverage

- **Available Balance** is the amount currently free for a new Isolated entry (`USDT.Free` on paper, Binance USD-M available on LIVE).
- **Planned Risk** is `Available × Risk per trade %`. That is the planned dollar loss if the stop fills as assumed. Slippage, fees, gaps, and liquidation can make the actual loss larger. It is **not** a guaranteed maximum loss.
- **Stop Loss %** and **Take Profit %** are distances from entry. LONG SL = entry × (1 − SL%). LONG TP = entry × (1 + TP%). SHORT flips the signs.
- **Position Notional** is `Planned Risk / Stop Loss %`. That is the market value of the position, not the margin.
- **Leverage** only sets Isolated margin = Notional / leverage. Leverage is not the risk.
- **Isolated Margin** is collateral locked on that coin only. It is not Planned Risk and not Notional.

Example at $100 available, HIGH 2% / 3% SL / 8x: Planned Risk $2, Notional ≈ $66.67, Isolated margin ≈ $8.33.

When available becomes $150 the same book plans $3. At $80 it plans $1.60. Every new trade recalculates. Changing LOW / MEDIUM / HIGH does not resize open positions; those keep the snapshot taken at fill.

## How a new trade is sized

One engine (`RiskEngine.Plan` / `Evaluate`) is used for paper, LIVE, and backtest. The strategy only decides *when* to enter or to exit on EMA cross. It does **not** size the order and it does **not** own SL/TP. Leftover STOP_LOSS / TAKE_PROFIT nodes in strategy JSON are ignored. Protective prices come from the active Isolated book at fill (and from the fill snapshot afterwards).

1. Read current available and the active book.
2. Planned Risk = available × R%.
3. Theoretical quantity = Planned Risk / (price × SL%).
4. Floor to the coin step. If that falls below min qty or min notional, the trade is rejected. Size is never increased to meet the exchange minimum.
5. Isolated margin = notional / min(profile max leverage, exchange leverage cap for that coin).
6. Estimated total risk = actual stop loss + entry fee + exit fee + slippage. If that exceeds 1.5× Planned Risk, reject.
7. Reject if Isolated margin + entry fee > available, if the coin is already open, if open count ≥ max positions (default 2), or if open planned risk + this trade > max portfolio planned risk (default 4%).
8. Reject if SL sits within the liquidation safety buffer (default 1%) of estimated Isolated liquidation.
9. LIVE switches the coin to Isolated and sets leverage only if that switch verifies. Cross is rejected. The bot never adds extra margin to an Isolated position.

Protective Binance STOP_MARKET / TAKE_PROFIT_MARKET are placed after a LIVE fill (`closePosition=true`). Trigger source defaults to **MARK_PRICE**, which is the safer supported Futures mode versus last-print spikes. If SL cannot be placed, the position is flattened. TP failure is treated the same by default.

If Binance is unreachable or account state cannot be reconciled, new entries are blocked (Risk Lock). Open Isolated positions stay and keep being monitored. Emergency Stop still does not silently flatten unless you use the flatten action.

## Daily loss, consecutive losses, Risk Lock

Daily loss limit (defaults LOW 3%, MEDIUM 5%, HIGH 7%) is measured against available. When it is hit, **new entries stop**. Open positions stay and keep being monitored.

Five consecutive losing completed trades (configurable) activate Risk Lock and a cooldown (default 30 minutes). Risk Lock also activates on stale market data, engine errors, and exchange disconnect. While locked, no new trades.

There is no Martingale: the next trade always uses current available × current R%. A loss never doubles size, leverage, or risk. If available later becomes $150 on HIGH, Planned Risk is $3. At $80 it is $1.60.

## Snapshot

Each fill stores available, R%, planned risk, SL/TP percents and prices, notional, quantity, leverage, Isolated margin, estimated fees/slippage/total risk, and liquidation price at entry. That row does not change when you edit the book.

## Strategies

Strategies only emit **when**. See [strategies.md](strategies.md) for the five closed-candle templates, quality filters, SHORT, and the no-order preview. Filters skip some noisy setups; they do not guarantee fewer losses and they do not replace Isolated SL/TP.
