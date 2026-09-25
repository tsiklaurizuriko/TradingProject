# Exhaustion path replay

Research replay only. No Paper bot, no live order. The rule is the frozen `DOWN_EXHAUSTION` flag. Thresholds were not searched again.

Coins: 521. Clock: closed 1h bars. One short at a time per coin. The next entry waits 24 bars.

Signal, on the closed hour: 20-hour return > +20%, RSI(14) >= 75, volume z over 48 hours >= 1.5, close in the bottom 40% of that hour. Entry is the next hour's open. Exit is the open 24 hours later. There is no stop and no take-profit in this replay.

A short's gross return is (entry − exit) / entry. Cost is taker fee 0.04% plus slippage 0.02% per side. Round trip at 1x is 0.12%. Stress uses 1.5x and 2x that cost. Funding is not in the 1h file and is not invented.

MAE is the worst high against the short, measured from the entry. MFE is the best low in favor of the short. Touched −15% means that low reached 15% under the entry. Label hit means a low reached 15% under the signal close inside the same 24 bars. That label is the old classification, not the trade.

| Split | Trades | Coins | Top coin share | Clock gaps | Win rate 1x | Mean gross | Mean net 1x | Mean net 1.5x | Mean net 2x | Median net 1x | Median MAE | Median MFE | Touch −15% | Label hit | Mean gross on touch | Mean gross on miss |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| IS | 616 | 279 | 0.013 | 0 | 0.5795 | -0.0133 | -0.0145 | -0.0151 | -0.0157 | 0.0217 | 0.0994 | 0.0846 | 0.1932 | 0.1948 | 0.1459 | -0.0514 |
| VAL | 481 | 278 | 0.0125 | 0 | 0.6923 | 0.0206 | 0.0194 | 0.0188 | 0.0182 | 0.0652 | 0.0942 | 0.1159 | 0.3243 | 0.3264 | 0.1657 | -0.049 |
| OOS | 614 | 298 | 0.0147 | 0 | 0.645 | -0.0033 | -0.0045 | -0.0051 | -0.0057 | 0.0536 | 0.1122 | 0.1272 | 0.399 | 0.4007 | 0.1419 | -0.0998 |

The path does not stay positive at 1.5x cost on every window. This is not a reason to place orders.

LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.
