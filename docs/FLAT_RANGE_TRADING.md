# Flat inside-range trading

Research replay only. No Paper bot, no live order.

Coins: 527. Clock: closed 1h bars.

A flat is the prior 24 hours, not including the signal bar. The band width must be 0.8% to 6% of price, and that width must sit in the lowest 30% of the prior 100 such bands. Long when the close is in the bottom 20% of that band. Short when it is in the top 20%. Exit at the opposite 20% boundary, on a close through the band, or after 24 hours. One position at a time per coin. The next entry waits until the trade is done.

Cost is taker fee 0.04% plus slippage 0.02% per side. Round trip at 1x is 0.12%. Stress uses 1.5x and 2x that cost. Funding is not in the 1h file and is not invented.

| Split | Trades | Long share | Target exits | Band breaks | Median hold hours | Win rate 1x | Mean net 1x | Mean net 1.5x | Mean net 2x |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| IS | 89040 | 0.4404 | 7189 | 80723 | 1 | 0.3178 | 0.0005 | -0.0001 | -0.0007 |
| VAL | 65040 | 0.5172 | 5779 | 58141 | 1 | 0.3228 | 0.0011 | 0.0005 | -0.0001 |
| OOS | 79095 | 0.52 | 7037 | 70582 | 1 | 0.3125 | 0.0006 | 0 | -0.0006 |

OOS mean net at the base 0.12% round trip is +0.06% per trade. That result does not survive 1.5x cost, where the OOS mean is 0, or 2x cost, where it is negative. About 89% of OOS trades exit because the band breaks, and the median hold is 1 hour. The opposite edge of the flat is reached on about 9% of trades. This is not a reason to place orders.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.

## Bound stop and take profit

Coins: 527. Same flat rule. Stop is the touched bound. Take profit is the opposite bound. A stop closer than 0.20% is skipped. Time exit is 24 hours. Round trip cost is 0.12%.

| Split | Trades | Stops | Takes | Time exits | Median stop | Median take profit | Median R | Win rate | Mean net |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| IS | 76246 | 68670 | 6138 | 1438 | 0.0057 | 0.0421 | 7.3779 | 0.3572 | 0.0007 |
| VAL | 56546 | 50244 | 4987 | 1315 | 0.0057 | 0.041 | 7.1368 | 0.3607 | 0.0013 |
| OOS | 67556 | 59838 | 6046 | 1672 | 0.0053 | 0.0374 | 7.0635 | 0.3531 | 0.0008 |

Position size uses the account risk percent divided by this stop distance. The stop price and the take-profit price are the locked bounds, not a fixed percent book.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.
