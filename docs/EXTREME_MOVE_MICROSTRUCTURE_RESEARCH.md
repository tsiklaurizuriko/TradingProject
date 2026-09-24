# Extreme move microstructure research

Funding, basis, taker flow, and ±1% depth. Liquidations and spread are DATA_UNAVAILABLE.

Taker buy volume is used only when a coin's 1h cache has taker buy > 0 and <= volume on most bars. Coins passing that check: 0. Stored zeros are not flow.
Depth imbalance is on disk for BTCUSDT, ETHUSDT, and BNBUSDT only. Other coins are DATA_UNAVAILABLE for depth.

| Feature | Event | Lead | IS effect | VAL effect | OOS effect | OOS AUC | Label |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| — | — | — | — | — | — | — | NO_EVIDENCE or INSUFFICIENT_DATA |


Whether a feature moves before, at, or after the excursion is judged by comparing T-4h, T-1h, T0, and the mid-path bar. T0 is still before the future window. The mid-path bar is during the move and is not a precursor.

Trading:LiveTradingEnabled=False. Trading:CrossSectionalReversal Enabled=True PaperEnabled=False LiveEnabled=True. Scalping Enabled=False AllowLive=False. This study did not change these flags, did not enable Paper, did not promote a strategy, and did not submit an order. VALIDATED_FOR_PAPER=NONE. LIVE_APPROVED=false. Cross-sectional LiveEnabled or global live is not false in appsettings. Those values were left untouched.
