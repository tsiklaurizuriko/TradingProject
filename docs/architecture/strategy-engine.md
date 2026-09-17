# Strategy engine

## Role

`TradingPlatform.Strategies` evaluates versioned JSON definitions against normalized candles and indicator values. It emits a `Signal` (`BUY`, `SELL`, `HOLD`, `EXIT`, `NO_ACTION`).

The engine is exchange-agnostic. It never references Binance types.

## Definition format

JSON stored on `StrategyVersion.Definition`. Validated before save (`STRATEGY_INVALID` if malformed).

Nested boolean groups (`AND`, `OR`, `NOT`) are supported. Comparisons include `GREATER_THAN`, `LESS_THAN`, `GREATER_OR_EQUAL`, `LESS_OR_EQUAL`, `CROSSES_ABOVE`, `CROSSES_BELOW`, `EQUALS`.

Crosses use the previous closed candle versus the newly closed candle. Unclosed candles are not used for decisions (no look-ahead).

## Indicators

`IIndicator` implementations live in this module and are reused by live trading and backtesting:

SMA, EMA, WMA, RSI, MACD, Bollinger Bands, ATR, ADX, Stochastic, VWAP, Volume, Average Volume.

## Versioning

A strategy has many versions. When a version has been referenced by a bot run, fill, or backtest, it cannot be mutated. Changes create `vN+1`.

Signals, orders, and trades persist `StrategyId` and `StrategyVersionId`.

## Sample (not a performance claim)

EMA(20) crosses above EMA(50) AND RSI(14) > 50 → BUY  
EMA(20) crosses below EMA(50) OR stop-loss 1.5% OR take-profit 3% → EXIT

See [docs/strategies/README.md](../strategies/README.md).
