# Funding carry

Research replay only. No Paper bot, no live order. Thresholds were not searched.

Hedge: long the index, short the mark, same notional. Enter after a settled funding rate above zero. That triggering rate is not collected. Later positive settlements are collected. The first settlement at or below zero is paid, then the hedge exits. A position still open at the last bar is marked and excluded from the closed-trade averages.

Price result is index exit / index entry minus mark exit / mark entry. Funding result is the sum of settled rates while the hedge was on, including the exit settlement. Cost is 0.36% per round trip: spot taker 0.10% and futures taker 0.04% on each side, plus 0.02% slippage on each of the four fills. Cash required is about two units of notional at 1x, so a return on cash is about half the notional return below. The limit-order spread idea is not in this table. Historical top-of-book is not on disk.

Coins with 1h index and mark: 10. Funding hours without both prices, skipped: 357.

## Enter only while funding is positive

  ALL n=2480 funding=0.037 % price=0.010 % net=-0.313 % win=1 % hold=48.7h
  IS  n=958 funding=0.069 % price=0.011 % net=-0.280 % win=3 % hold=70.0h
  VAL n=786 funding=0.014 % price=0.010 % net=-0.337 % win=0 % hold=32.3h
  OOS n=736 funding=0.020 % price=0.009 % net=-0.331 % win=1 % hold=38.6h
  still open 9

## Always hedged, one trade per coin

  ALL n=10 funding=7.782 % price=-0.026 % net=7.397 % win=100 % hold=17538.4h

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.
