# Extreme move open-interest research

Open interest is a research family. A state such as price up and OI up is not a signal.

Vision metrics on disk for 10 coins. A 1h feature uses the last 5m print whose create_time falls inside that closed hour. Hours without a print are null.

| Feature | Event | Lead | IS effect | VAL effect | OOS effect | OOS AUC | Label |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| — | — | — | — | — | — | — | NO_EVIDENCE or INSUFFICIENT_DATA |


Sample sizes below 30 in a split are INSUFFICIENT_DATA. Effects are median(event) - median(control) at T-1h, oriented on IS.

The 10 coins with Vision open interest rarely produce an independent +30% or -30% episode inside the IS window. The microstructure logistic matrix had 6 IS rows, which is below the frozen minimum of 30 events. That is INSUFFICIENT_DATA for OI, funding, basis, taker, and depth as precursors of these thresholds. It is not a test of those features on the altcoins where the +30% episodes actually occur, because those coins have no Vision open-interest history in this cache. Taker buy in the 1h kline cache is zero on about 17547 of 17649 BTCUSDT bars, and the side cache stores the same zeros, so taker flow is DATA_UNAVAILABLE rather than a failed predictor.

Trading:LiveTradingEnabled=False. Trading:CrossSectionalReversal Enabled=True PaperEnabled=False LiveEnabled=True. Scalping Enabled=False AllowLive=False. This study did not change these flags, did not enable Paper, did not promote a strategy, and did not submit an order. VALIDATED_FOR_PAPER=NONE. LIVE_APPROVED=false. Cross-sectional LiveEnabled or global live is not false in appsettings. Those values were left untouched.
