# Cross-sectional reversal symbol robustness

This is a diagnostic. It does not select coins and it is not a trading filter.

The audit already showed 526–527 names in the out-of-sample tails and a maximum top-decile share under 40% (BEATUSDT 31.6% on the short-horizon returns). That is the evidence that the factor was not a one-coin result. It is still not an executable edge.

## Labels

`CONSISTENT`, `MIXED`, `WEAK`, and `INSUFFICIENT_DATA` are descriptive. A profitable historical symbol is not marked tradable. A full per-symbol IS / Validation / OOS ledger and a 527-name leave-one-out were not recomputed on the cache in this pass, so those cells are not filled with estimated numbers.

Leave-top-N and Herfindahl are implemented as pure functions for later aggregation. They were not used to drop names from the strategy.

## Question

The reversal effect in the audit was broad across the universe. Whether the net result after costs survives without a handful of names is unanswered here, because that leave-one-out was not run. It must not be inferred from the gross spread.
