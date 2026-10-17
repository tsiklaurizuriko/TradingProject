# Impulse Catch mirror

Research replay only. No Paper bot, no live order. The long is the current Impulse Catch rule. The short is the same rule with the sign flipped. Thresholds were not searched.

Long: first closed 15m bar at least 16% above the 24h low, green, close in the upper half, 24h quote volume at least 3M USDT, coin already up 20% over 30 days. Hourly volume above 7x the prior 7-day average, and a close above the prior 30-day high, are skips.

Short: first closed 15m bar at least 16% below the 24h high, red, close in the lower half, 24h quote volume at least 3M USDT, coin already down 20% over 30 days. Hourly volume above 7x, and a close below the prior 30-day low, are skips.

Exit, both sides: 12% exchange stop, after +20% in favor the stop ratchets to the tighter of breakeven and 30% beyond the extreme, a close 25% back through that extreme exits, and the hold ends after 4 days. One position per coin in the combined book: an open long blocks a later short on that coin, and the reverse. A bar that is both a long and a short is skipped. Cost is 0.3% round trip. Funding is not in the 15m file and is not invented.

Files 529. Coins with a trade 514.

## Long only

  ALL n=6132 mean=1.04 % pf=1.17 win=33 % ge20=10 %
  IS  n=2272 mean=2.14 % pf=1.41 win=44 % ge20=10 %
  VAL n=1291 mean=-0.57 % pf=0.92 win=25 % ge20=10 %
  OOS n=2569 mean=0.88 % pf=1.13 win=27 % ge20=10 %
  slots8 x0.51 maxDD 77 % losing months 14/24 taken 2168 skipped 3964
  slots30 x1.96 maxDD 46 % losing months 14/24 taken 4821 skipped 1311
    LSKUSDT 12.14
    VELVETUSDT 5.09
    PUMPBTCUSDT 5.01
    SIRENUSDT 4.85
    PIPPINUSDT 4.80

## Short only

  ALL n=4443 mean=-1.94 % pf=0.68 win=37 % ge20=5 %
  IS  n=1803 mean=-1.68 % pf=0.70 win=40 % ge20=4 %
  VAL n=1788 mean=-2.52 % pf=0.61 win=34 % ge20=5 %
  OOS n=852 mean=-1.25 % pf=0.78 win=39 % ge20=6 %
  slots8 x0.23 maxDD 85 % losing months 17/24 taken 1408 skipped 3035
  slots30 x0.51 maxDD 56 % losing months 16/24 taken 2795 skipped 1648
    SKYAIUSDT 1.59
    MYXUSDT 1.59
    GRIFFAINUSDT 1.25
    LABUSDT 1.23
    PTBUSDT 1.07

## Both, one position per coin

Same-bar long and short skipped: 0. Shorts blocked by an open position: 7. Longs blocked by an open position: 4.

  ALL n=10564 mean=-0.21 % pf=0.97 win=35 % ge20=8 %
  IS  n=4073 mean=0.45 % pf=1.08 win=42 % ge20=8 %
  VAL n=3074 mean=-1.69 % pf=0.75 win=30 % ge20=7 %
  OOS n=3417 mean=0.34 % pf=1.05 win=30 % ge20=9 %
  slots8 x0.07 maxDD 96 % losing months 16/24 taken 2360 skipped 8204
  slots30 x0.65 maxDD 73 % losing months 16/24 taken 6760 skipped 3804
    LSKUSDT 11.65
    PUMPBTCUSDT 5.19
    MYXUSDT 5.19
    SIRENUSDT 5.08
    VELVETUSDT 4.64
  LONG  n=6128 mean=1.04 % pf=1.17 win=33 % ge20=10 %
  SHORT n=4436 mean=-1.94 % pf=0.68 win=37 % ge20=5 %

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false. This replay does not change Impulse Catch.
