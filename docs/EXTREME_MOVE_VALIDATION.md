# Extreme move validation

Chronological IS / VAL / OOS. No random split. No OOS feature selection. No OOS threshold search.

Hypotheses in the primary family: feature × key threshold × T-1h. Count = 1092.
Features in the registry: 156. Key thresholds: 7. Leads: 7. Interactions are inside the registry, not an extra search.
Benjamini-Hochberg FDR 0.10 is applied to primary-family IS p-values (normal approximation of the Mann-Whitney statistic). Raw and corrected calls are both stored. A small p-value is not an edge.

## Repeatable rule (frozen)

REPEATABLE requires the same effect sign on IS, VAL, and OOS, AUC at least 0.52 on VAL and OOS, at least 3 chronological blocks agreeing, at least 5 coins with 3 positive, BH rejection on the IS test, and more than one threshold existing in the family. These labels are research labels.

## Models

- UNIVERSE T-1h frozen features 23. IS n=994 events=572.
- UNIVERSE A equal-weight IS AUC 0.5242. B rank IS AUC 0.4497. C logistic IS 0.83 VAL 0.7686 OOS 0.8382. D depth-2 stump IS 0.8002.
- MICRO: INSUFFICIENT_DATA for logistic/tree (IS n=6).

Model C logistic weights are fit on IS only. VAL and OOS are scored once. Model D is a depth-2 stump on the same frozen, IS-standardized matrix. IS-mean imputation is used only inside the model matrix when fewer than half the features are missing. Effect-size rows never impute.

## Cost stress

- COST 1x IS fires=3318 eventFires=2904 meanNet=0.0764
- COST 1x VAL fires=3185 eventFires=2906 meanNet=0.0907
- COST 1x OOS fires=3242 eventFires=2968 meanNet=0.1018
- COST 1,25x IS fires=3318 eventFires=2904 meanNet=0.0761
- COST 1,25x VAL fires=3185 eventFires=2906 meanNet=0.0904
- COST 1,25x OOS fires=3242 eventFires=2968 meanNet=0.1015
- COST 1,5x IS fires=3318 eventFires=2904 meanNet=0.0758
- COST 1,5x VAL fires=3185 eventFires=2906 meanNet=0.0901
- COST 1,5x OOS fires=3242 eventFires=2968 meanNet=0.1012
- COST 2x IS fires=3318 eventFires=2904 meanNet=0.0752
- COST 2x VAL fires=3185 eventFires=2906 meanNet=0.0895
- COST 2x OOS fires=3242 eventFires=2968 meanNet=0.1006
- COST 3x IS fires=3318 eventFires=2904 meanNet=0.074
- COST 3x VAL fires=3185 eventFires=2906 meanNet=0.0883
- COST 3x OOS fires=3242 eventFires=2968 meanNet=0.0994
- Cost model is Model B taker fee 0.04% plus slippage 0.02% per fill, round trip, stressed. Entry is the T-1h research score, not an order. Exit proxy is half the labeled maximum excursion for events and flat for controls. This is not a trading backtest and is not an edge.

## Primary labels at T-1h

| Feature | Event | Label | BH | IS AUC | VAL AUC | OOS AUC | IS n |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: |
| adx_14 | DOWN_30 | UNSTABLE | reject | 0.6581 | 0.5587 | 0.6004 | 113 |
| aroon_osc | DOWN_30 | UNSTABLE | reject | 0.7436 | 0.5022 | 0.4604 | 113 |
| atr_accel | DOWN_30 | OOS_ONLY | not rejected | 0.5023 | 0.4834 | 0.5286 | 113 |
| atr_change | DOWN_30 | OOS_ONLY | not rejected | 0.5046 | 0.4804 | 0.5233 | 113 |
| atr_pct | DOWN_30 | UNSTABLE | reject | 0.5994 | 0.6066 | 0.6451 | 113 |
| bb_keltner_squeeze | DOWN_30 | UNSTABLE | reject | 0.6518 | 0.5483 | 0.6033 | 113 |
| bb_pct | DOWN_30 | UNSTABLE | reject | 0.6791 | 0.4971 | 0.4629 | 113 |
| bb_width | DOWN_30 | UNSTABLE | reject | 0.7971 | 0.7574 | 0.8904 | 113 |
| breadth_pos | DOWN_30 | UNSTABLE | reject | 0.6147 | 0.5456 | 0.5005 | 113 |
| btc_ret_24 | DOWN_30 | UNSTABLE | reject | 0.7753 | 0.6547 | 0.4883 | 113 |
| btc_rv_24 | DOWN_30 | UNSTABLE | not rejected | 0.5801 | 0.6173 | 0.5925 | 113 |
| cci_20 | DOWN_30 | UNSTABLE | reject | 0.6811 | 0.4935 | 0.4702 | 113 |
| cmf_20 | DOWN_30 | UNSTABLE | reject | 0.7707 | 0.5855 | 0.505 | 113 |
| di_minus | DOWN_30 | UNSTABLE | reject | 0.7247 | 0.4914 | 0.4813 | 113 |
| di_plus | DOWN_30 | UNSTABLE | reject | 0.6631 | 0.479 | 0.482 | 113 |
| dispersion | DOWN_30 | UNSTABLE | reject | 0.5919 | 0.5911 | 0.5419 | 113 |
| ema_dist_100 | DOWN_30 | UNSTABLE | reject | 0.7012 | 0.5509 | 0.4241 | 113 |
| ema_dist_20 | DOWN_30 | UNSTABLE | reject | 0.7485 | 0.5293 | 0.4706 | 113 |
| ema_dist_200 | DOWN_30 | UNSTABLE | reject | 0.655 | 0.5028 | 0.3839 | 113 |
| ema_dist_50 | DOWN_30 | UNSTABLE | reject | 0.7455 | 0.5683 | 0.4407 | 113 |
| ema_dist_9 | DOWN_30 | UNSTABLE | reject | 0.6729 | 0.5136 | 0.4971 | 113 |
| ema20_slope_1 | DOWN_30 | UNSTABLE | reject | 0.7485 | 0.5293 | 0.4706 | 113 |
| ema20_slope_12 | DOWN_30 | UNSTABLE | reject | 0.7512 | 0.5502 | 0.4637 | 113 |
| ema20_slope_24 | DOWN_30 | UNSTABLE | reject | 0.7167 | 0.5628 | 0.4191 | 113 |
| ema20_slope_3 | DOWN_30 | UNSTABLE | reject | 0.7573 | 0.512 | 0.4668 | 113 |
| ema20_slope_6 | DOWN_30 | UNSTABLE | reject | 0.758 | 0.5219 | 0.4925 | 113 |
| garman_klass_24 | DOWN_30 | UNSTABLE | reject | 0.7752 | 0.8001 | 0.9285 | 113 |
| keltner_width | DOWN_30 | UNSTABLE | reject | 0.7842 | 0.7996 | 0.9207 | 113 |
| macd | DOWN_30 | UNSTABLE | reject | 0.6666 | 0.5575 | 0.4511 | 113 |
| macd_hist | DOWN_30 | UNSTABLE | not rejected | 0.56 | 0.4466 | 0.5104 | 113 |
| macd_hist_accel | DOWN_30 | UNSTABLE | not rejected | 0.558 | 0.4891 | 0.5498 | 113 |
| macd_signal | DOWN_30 | UNSTABLE | reject | 0.652 | 0.5617 | 0.45 | 113 |
| mfi_14 | DOWN_30 | UNSTABLE | reject | 0.6602 | 0.4988 | 0.5222 | 113 |
| mom_accel | DOWN_30 | UNSTABLE | not rejected | 0.5343 | 0.4837 | 0.4492 | 113 |
| obv_slope | DOWN_30 | UNSTABLE | reject | 0.617 | 0.5097 | 0.4917 | 113 |
| parkinson_24 | DOWN_30 | UNSTABLE | reject | 0.7869 | 0.8081 | 0.926 | 113 |
| poc_dist | DOWN_30 | UNSTABLE | reject | 0.6584 | 0.4753 | 0.4842 | 113 |
| ppo | DOWN_30 | UNSTABLE | reject | 0.7501 | 0.5679 | 0.4359 | 113 |
| range_pct | DOWN_30 | UNSTABLE | not rejected | 0.541 | 0.5752 | 0.5829 | 113 |
| rel_volume | DOWN_30 | UNSTABLE | reject | 0.6005 | 0.5636 | 0.5823 | 113 |
| ret_1 | DOWN_30 | UNSTABLE | reject | 0.6034 | 0.5435 | 0.4763 | 113 |
| ret_10 | DOWN_30 | UNSTABLE | reject | 0.6652 | 0.4913 | 0.4909 | 113 |
| ret_20 | DOWN_30 | UNSTABLE | reject | 0.7722 | 0.5309 | 0.4479 | 113 |
| ret_3 | DOWN_30 | UNSTABLE | not rejected | 0.5645 | 0.5205 | 0.4525 | 113 |
| ret_5 | DOWN_30 | UNSTABLE | reject | 0.6757 | 0.4843 | 0.4618 | 113 |
| ret_50 | DOWN_30 | UNSTABLE | reject | 0.7203 | 0.5492 | 0.4153 | 113 |
| ret5_x_volz | DOWN_30 | UNSTABLE | not rejected | 0.5366 | 0.4935 | 0.505 | 113 |
| roc_10 | DOWN_30 | UNSTABLE | reject | 0.6652 | 0.4913 | 0.4909 | 113 |
| rogers_satchell_24 | DOWN_30 | UNSTABLE | reject | 0.7717 | 0.7926 | 0.9298 | 113 |
| rsi_14 | DOWN_30 | UNSTABLE | reject | 0.7513 | 0.5287 | 0.4612 | 113 |
| rsi_21 | DOWN_30 | UNSTABLE | reject | 0.7485 | 0.5432 | 0.4428 | 113 |
| rsi_7 | DOWN_30 | UNSTABLE | reject | 0.6968 | 0.5076 | 0.4777 | 113 |
| rv_24 | DOWN_30 | UNSTABLE | reject | 0.8016 | 0.816 | 0.9126 | 113 |
| stoch_k | DOWN_30 | UNSTABLE | reject | 0.6567 | 0.5213 | 0.5005 | 113 |
| stoch_rsi | DOWN_30 | UNSTABLE | not rejected | 0.5105 | 0.4946 | 0.4973 | 113 |
| tr_pct | DOWN_30 | UNSTABLE | not rejected | 0.5407 | 0.5752 | 0.5829 | 113 |
| trix_15 | DOWN_30 | UNSTABLE | reject | 0.7259 | 0.5733 | 0.4163 | 113 |
| vol_expansion | DOWN_30 | UNSTABLE | reject | 0.6089 | 0.5744 | 0.6285 | 113 |
| vol_of_vol | DOWN_30 | UNSTABLE | not rejected | 0.581 | 0.5283 | 0.5432 | 113 |
| volume | DOWN_30 | UNSTABLE | reject | 0.6135 | 0.6655 | 0.7448 | 113 |
| volume_accel | DOWN_30 | UNSTABLE | not rejected | 0.5517 | 0.5153 | 0.5093 | 113 |
| volume_atr | DOWN_30 | UNSTABLE | not rejected | 0.5419 | 0.5139 | 0.485 | 113 |
| volume_change | DOWN_30 | UNSTABLE | not rejected | 0.5498 | 0.5256 | 0.5096 | 113 |
| volume_climax | DOWN_30 | UNSTABLE | not rejected | 0.57 | 0.5487 | 0.5177 | 113 |
| volume_pct | DOWN_30 | UNSTABLE | reject | 0.635 | 0.6054 | 0.6318 | 113 |
| volume_range | DOWN_30 | UNSTABLE | not rejected | 0.5371 | 0.5116 | 0.4889 | 113 |
| volume_roc | DOWN_30 | UNSTABLE | not rejected | 0.4823 | 0.4519 | 0.451 | 113 |
| volume_z | DOWN_30 | UNSTABLE | reject | 0.6051 | 0.5694 | 0.5813 | 113 |
| volz_x_atr | DOWN_30 | UNSTABLE | reject | 0.6312 | 0.5595 | 0.5902 | 113 |
| vwap_dist | DOWN_30 | UNSTABLE | reject | 0.7543 | 0.5298 | 0.5162 | 113 |
| vwap_slope | DOWN_30 | UNSTABLE | reject | 0.7317 | 0.5589 | 0.454 | 113 |
| willr_14 | DOWN_30 | UNSTABLE | reject | 0.6567 | 0.5213 | 0.5005 | 113 |
| yang_zhang_24 | DOWN_30 | UNSTABLE | reject | 0.79 | 0.8057 | 0.9269 | 113 |
| adx_14 | UP_30 | REPEATABLE | reject | 0.5843 | 0.5793 | 0.5904 | 459 |
| aroon_osc | UP_30 | REPEATABLE | reject | 0.5555 | 0.5418 | 0.5955 | 459 |
| atr_accel | UP_30 | UNSTABLE | not rejected | 0.5025 | 0.4889 | 0.5012 | 459 |
| atr_change | UP_30 | UNSTABLE | not rejected | 0.4885 | 0.4788 | 0.4812 | 459 |
| atr_pct | UP_30 | REGIME_SPECIFIC | reject | 0.6528 | 0.6125 | 0.6606 | 459 |
| bb_pct | UP_30 | REGIME_SPECIFIC | reject | 0.5507 | 0.5328 | 0.601 | 459 |
| bb_width | UP_30 | REPEATABLE | reject | 0.8144 | 0.8179 | 0.8498 | 459 |
| breadth_pos | UP_30 | UNSTABLE | not rejected | 0.5234 | 0.512 | 0.4869 | 459 |
| btc_ret_24 | UP_30 | UNSTABLE | not rejected | 0.5277 | 0.5589 | 0.5058 | 459 |
| btc_rv_24 | UP_30 | UNSTABLE | not rejected | 0.5385 | 0.5619 | 0.5594 | 459 |
| cci_20 | UP_30 | REGIME_SPECIFIC | reject | 0.5523 | 0.5343 | 0.5973 | 459 |
| cmf_20 | UP_30 | UNSTABLE | not rejected | 0.5079 | 0.5099 | 0.4638 | 459 |
| di_minus | UP_30 | REPEATABLE | reject | 0.5836 | 0.5684 | 0.6059 | 459 |
| di_plus | UP_30 | REPEATABLE | reject | 0.593 | 0.5647 | 0.5956 | 459 |
| dispersion | UP_30 | REPEATABLE | reject | 0.638 | 0.6076 | 0.5594 | 459 |
| ema_cross_9_20 | UP_30 | REPEATABLE | reject | 0.5659 | 0.5431 | 0.5795 | 459 |
| ema_dist_100 | UP_30 | REGIME_SPECIFIC | reject | 0.6535 | 0.5604 | 0.6738 | 459 |
| ema_dist_20 | UP_30 | REGIME_SPECIFIC | reject | 0.5595 | 0.5345 | 0.6058 | 459 |
| ema_dist_200 | UP_30 | REGIME_SPECIFIC | reject | 0.6657 | 0.5593 | 0.6822 | 459 |
| ema_dist_50 | UP_30 | REGIME_SPECIFIC | reject | 0.6176 | 0.5594 | 0.645 | 459 |
| ema_dist_9 | UP_30 | OOS_ONLY | not rejected | 0.5208 | 0.5184 | 0.5816 | 459 |
| ema20_slope_1 | UP_30 | REGIME_SPECIFIC | reject | 0.5595 | 0.5345 | 0.6058 | 459 |
| ema20_slope_12 | UP_30 | REGIME_SPECIFIC | reject | 0.5977 | 0.5437 | 0.6133 | 459 |
| ema20_slope_24 | UP_30 | REGIME_SPECIFIC | reject | 0.6386 | 0.5659 | 0.6404 | 459 |
| ema20_slope_3 | UP_30 | REGIME_SPECIFIC | reject | 0.5712 | 0.5406 | 0.606 | 459 |
| ema20_slope_6 | UP_30 | REGIME_SPECIFIC | reject | 0.586 | 0.5364 | 0.6088 | 459 |
| garman_klass_24 | UP_30 | REPEATABLE | reject | 0.8777 | 0.8875 | 0.9078 | 459 |
| keltner_width | UP_30 | REPEATABLE | reject | 0.8614 | 0.8755 | 0.8942 | 459 |
| macd | UP_30 | REGIME_SPECIFIC | reject | 0.6071 | 0.5513 | 0.6213 | 459 |
| macd_hist | UP_30 | UNSTABLE | not rejected | 0.4983 | 0.4789 | 0.4713 | 459 |
| macd_hist_accel | UP_30 | UNSTABLE | not rejected | 0.5095 | 0.4997 | 0.4611 | 459 |
| macd_signal | UP_30 | REGIME_SPECIFIC | reject | 0.6161 | 0.5622 | 0.6214 | 459 |
| mfi_14 | UP_30 | REPEATABLE | reject | 0.5874 | 0.5481 | 0.5901 | 459 |
| mom_accel | UP_30 | UNSTABLE | not rejected | 0.4996 | 0.521 | 0.5351 | 459 |
| obv_slope | UP_30 | OOS_ONLY | not rejected | 0.5118 | 0.5008 | 0.5381 | 459 |
| pa_hh | UP_30 | UNSTABLE | not rejected | 0.5364 | 0.4996 | 0.5268 | 459 |
| parkinson_24 | UP_30 | REPEATABLE | reject | 0.8776 | 0.8869 | 0.9076 | 459 |
| poc_dist | UP_30 | OOS_ONLY | not rejected | 0.5044 | 0.4987 | 0.5386 | 459 |
| ppo | UP_30 | REGIME_SPECIFIC | reject | 0.6207 | 0.5545 | 0.6321 | 459 |
| range_pct | UP_30 | REGIME_SPECIFIC | reject | 0.6015 | 0.604 | 0.6295 | 459 |
| rel_volume | UP_30 | REGIME_SPECIFIC | reject | 0.5468 | 0.5642 | 0.5395 | 459 |
| ret_1 | UP_30 | UNSTABLE | not rejected | 0.5112 | 0.485 | 0.4587 | 459 |
| ret_10 | UP_30 | UNSTABLE | reject | 0.5524 | 0.5185 | 0.5815 | 459 |
| ret_20 | UP_30 | REGIME_SPECIFIC | reject | 0.5564 | 0.5346 | 0.5989 | 459 |
| ret_3 | UP_30 | UNSTABLE | not rejected | 0.5191 | 0.5214 | 0.5609 | 459 |
| ret_5 | UP_30 | UNSTABLE | not rejected | 0.5203 | 0.5326 | 0.5645 | 459 |
| ret_50 | UP_30 | REGIME_SPECIFIC | reject | 0.6214 | 0.5747 | 0.6562 | 459 |
| ret5_x_volz | UP_30 | REPEATABLE | reject | 0.552 | 0.5433 | 0.5609 | 459 |
| roc_10 | UP_30 | UNSTABLE | reject | 0.5524 | 0.5185 | 0.5815 | 459 |
| rogers_satchell_24 | UP_30 | REPEATABLE | reject | 0.8762 | 0.8866 | 0.9072 | 459 |
| rsi_14 | UP_30 | REGIME_SPECIFIC | reject | 0.5824 | 0.5489 | 0.6242 | 459 |
| rsi_21 | UP_30 | REGIME_SPECIFIC | reject | 0.6084 | 0.5623 | 0.6418 | 459 |
| rsi_7 | UP_30 | REGIME_SPECIFIC | not rejected | 0.543 | 0.5301 | 0.596 | 459 |
| rv_24 | UP_30 | REPEATABLE | reject | 0.8625 | 0.8719 | 0.8963 | 459 |
| stoch_k | UP_30 | OOS_ONLY | not rejected | 0.5031 | 0.5031 | 0.5637 | 459 |
| stoch_rsi | UP_30 | UNSTABLE | not rejected | 0.526 | 0.51 | 0.4725 | 459 |
| tr_pct | UP_30 | REGIME_SPECIFIC | reject | 0.6014 | 0.604 | 0.6293 | 459 |
| trix_15 | UP_30 | REGIME_SPECIFIC | reject | 0.632 | 0.5608 | 0.6328 | 459 |
| vol_expansion | UP_30 | UNSTABLE | reject | 0.6064 | 0.6122 | 0.6201 | 459 |
| vol_of_vol | UP_30 | UNSTABLE | not rejected | 0.5311 | 0.5045 | 0.5396 | 459 |
| volume | UP_30 | REPEATABLE | reject | 0.7114 | 0.687 | 0.7345 | 459 |
| volume_accel | UP_30 | UNSTABLE | not rejected | 0.5128 | 0.506 | 0.5075 | 459 |
| volume_atr | UP_30 | REPEATABLE | reject | 0.6298 | 0.5395 | 0.5613 | 459 |
| volume_change | UP_30 | UNSTABLE | not rejected | 0.5066 | 0.4784 | 0.497 | 459 |
| volume_climax | UP_30 | UNSTABLE | not rejected | 0.5303 | 0.5252 | 0.5242 | 459 |
| volume_pct | UP_30 | REGIME_SPECIFIC | reject | 0.6059 | 0.615 | 0.6198 | 459 |
| volume_range | UP_30 | REPEATABLE | reject | 0.6272 | 0.5359 | 0.5582 | 459 |
| volume_roc | UP_30 | UNSTABLE | not rejected | 0.5445 | 0.5606 | 0.539 | 459 |
| volume_z | UP_30 | REGIME_SPECIFIC | reject | 0.5533 | 0.5741 | 0.5589 | 459 |
| volz_x_atr | UP_30 | REGIME_SPECIFIC | not rejected | 0.5405 | 0.5706 | 0.5466 | 459 |
| vwap_dist | UP_30 | OOS_ONLY | not rejected | 0.5104 | 0.5054 | 0.5525 | 459 |
| vwap_slope | UP_30 | REGIME_SPECIFIC | reject | 0.5937 | 0.5287 | 0.6225 | 459 |
| willr_14 | UP_30 | OOS_ONLY | not rejected | 0.5031 | 0.5031 | 0.5637 | 459 |
| yang_zhang_24 | UP_30 | REPEATABLE | reject | 0.8778 | 0.887 | 0.9079 | 459 |
| adx_14 | UP_50 | UNSTABLE | not rejected | 0.5811 | 0.5897 | 0.632 | 137 |
| aroon_osc | UP_50 | UNSTABLE | reject | 0.6071 | 0.5921 | 0.6157 | 137 |
| atr_accel | UP_50 | UNSTABLE | not rejected | 0.5105 | 0.5225 | 0.4889 | 137 |
| atr_change | UP_50 | UNSTABLE | not rejected | 0.5293 | 0.5417 | 0.527 | 137 |
| atr_pct | UP_50 | UNSTABLE | reject | 0.6922 | 0.6383 | 0.6802 | 137 |
| bb_pct | UP_50 | UNSTABLE | reject | 0.5877 | 0.5736 | 0.6116 | 137 |
| bb_width | UP_50 | UNSTABLE | reject | 0.8215 | 0.8596 | 0.8728 | 137 |
| breadth_pos | UP_50 | UNSTABLE | not rejected | 0.5424 | 0.5276 | 0.4522 | 137 |
| btc_ret_24 | UP_50 | UNSTABLE | not rejected | 0.5443 | 0.5699 | 0.4901 | 137 |
| btc_rv_24 | UP_50 | UNSTABLE | not rejected | 0.5468 | 0.4523 | 0.4414 | 137 |
| cci_20 | UP_50 | UNSTABLE | reject | 0.5883 | 0.5774 | 0.6074 | 137 |
| cmf_20 | UP_50 | UNSTABLE | not rejected | 0.4948 | 0.4841 | 0.4446 | 137 |
| di_minus | UP_50 | UNSTABLE | reject | 0.6289 | 0.6239 | 0.6273 | 137 |
| di_plus | UP_50 | UNSTABLE | reject | 0.6363 | 0.6134 | 0.624 | 137 |
| dispersion | UP_50 | UNSTABLE | reject | 0.6482 | 0.6356 | 0.5665 | 137 |
| ema_cross_9_20 | UP_50 | UNSTABLE | reject | 0.5926 | 0.5725 | 0.5829 | 137 |
| ema_dist_100 | UP_50 | UNSTABLE | reject | 0.675 | 0.6179 | 0.6881 | 137 |
| ema_dist_20 | UP_50 | UNSTABLE | reject | 0.584 | 0.5755 | 0.6323 | 137 |
| ema_dist_200 | UP_50 | UNSTABLE | reject | 0.6817 | 0.6161 | 0.6973 | 137 |
| ema_dist_50 | UP_50 | UNSTABLE | reject | 0.643 | 0.6092 | 0.6531 | 137 |
| ema_dist_9 | UP_50 | UNSTABLE | not rejected | 0.54 | 0.5402 | 0.6042 | 137 |
| ema20_slope_1 | UP_50 | UNSTABLE | reject | 0.584 | 0.5755 | 0.6323 | 137 |
| ema20_slope_12 | UP_50 | UNSTABLE | reject | 0.6512 | 0.5984 | 0.6193 | 137 |
| ema20_slope_24 | UP_50 | UNSTABLE | reject | 0.7019 | 0.6013 | 0.6454 | 137 |
| ema20_slope_3 | UP_50 | UNSTABLE | reject | 0.6 | 0.585 | 0.6323 | 137 |
| ema20_slope_6 | UP_50 | UNSTABLE | reject | 0.6274 | 0.5979 | 0.6258 | 137 |
| garman_klass_24 | UP_50 | UNSTABLE | reject | 0.8815 | 0.9316 | 0.9345 | 137 |
| keltner_width | UP_50 | UNSTABLE | reject | 0.8621 | 0.9219 | 0.9201 | 137 |
| macd | UP_50 | UNSTABLE | reject | 0.6413 | 0.5829 | 0.6161 | 137 |
| macd_hist | UP_50 | UNSTABLE | not rejected | 0.537 | 0.5301 | 0.5544 | 137 |
| macd_hist_accel | UP_50 | UNSTABLE | not rejected | 0.5296 | 0.5108 | 0.4694 | 137 |
| macd_signal | UP_50 | UNSTABLE | reject | 0.642 | 0.5998 | 0.6257 | 137 |
| mfi_14 | UP_50 | UNSTABLE | reject | 0.6071 | 0.6087 | 0.6081 | 137 |
| mom_accel | UP_50 | UNSTABLE | not rejected | 0.5224 | 0.5224 | 0.5359 | 137 |
| obv_slope | UP_50 | UNSTABLE | not rejected | 0.5359 | 0.5178 | 0.5535 | 137 |
| pa_hh | UP_50 | UNSTABLE | not rejected | 0.5735 | 0.5119 | 0.5501 | 137 |
| parkinson_24 | UP_50 | UNSTABLE | reject | 0.8802 | 0.9299 | 0.9345 | 137 |
| poc_dist | UP_50 | UNSTABLE | not rejected | 0.5291 | 0.5393 | 0.5572 | 137 |
| ppo | UP_50 | UNSTABLE | reject | 0.674 | 0.6016 | 0.6396 | 137 |
| range_pct | UP_50 | UNSTABLE | reject | 0.6461 | 0.6384 | 0.6403 | 137 |
| rel_volume | UP_50 | UNSTABLE | reject | 0.6063 | 0.5853 | 0.5644 | 137 |
| ret_1 | UP_50 | OOS_ONLY | not rejected | 0.5227 | 0.5129 | 0.5579 | 137 |
| ret_10 | UP_50 | UNSTABLE | reject | 0.5963 | 0.539 | 0.608 | 137 |
| ret_20 | UP_50 | UNSTABLE | reject | 0.5979 | 0.6049 | 0.6075 | 137 |
| ret_3 | UP_50 | UNSTABLE | not rejected | 0.5284 | 0.548 | 0.5673 | 137 |
| ret_5 | UP_50 | UNSTABLE | not rejected | 0.5583 | 0.5402 | 0.5866 | 137 |
| ret_50 | UP_50 | UNSTABLE | reject | 0.6549 | 0.599 | 0.6646 | 137 |
| ret5_x_volz | UP_50 | UNSTABLE | not rejected | 0.5761 | 0.5678 | 0.5669 | 137 |
| roc_10 | UP_50 | UNSTABLE | reject | 0.5963 | 0.539 | 0.608 | 137 |
| rogers_satchell_24 | UP_50 | UNSTABLE | reject | 0.8814 | 0.9308 | 0.9339 | 137 |
| rsi_14 | UP_50 | UNSTABLE | reject | 0.6182 | 0.5931 | 0.642 | 137 |
| rsi_21 | UP_50 | UNSTABLE | reject | 0.6439 | 0.6085 | 0.6566 | 137 |
| rsi_7 | UP_50 | UNSTABLE | not rejected | 0.5729 | 0.5639 | 0.6127 | 137 |
| rv_24 | UP_50 | UNSTABLE | reject | 0.8626 | 0.9108 | 0.9222 | 137 |
| stoch_k | UP_50 | UNSTABLE | not rejected | 0.5156 | 0.5233 | 0.5678 | 137 |
| stoch_rsi | UP_50 | UNSTABLE | not rejected | 0.5164 | 0.4975 | 0.4715 | 137 |
| tr_pct | UP_50 | UNSTABLE | reject | 0.6461 | 0.6384 | 0.6402 | 137 |
| trix_15 | UP_50 | UNSTABLE | reject | 0.6923 | 0.5979 | 0.641 | 137 |
| vol_expansion | UP_50 | UNSTABLE | reject | 0.6637 | 0.6248 | 0.6302 | 137 |
| vol_of_vol | UP_50 | UNSTABLE | reject | 0.6091 | 0.5384 | 0.5651 | 137 |
| volume | UP_50 | UNSTABLE | reject | 0.7085 | 0.718 | 0.7731 | 137 |
| volume_accel | UP_50 | UNSTABLE | not rejected | 0.5011 | 0.5052 | 0.506 | 137 |
| volume_atr | UP_50 | UNSTABLE | reject | 0.6288 | 0.5476 | 0.5726 | 137 |
| volume_change | UP_50 | UNSTABLE | not rejected | 0.5031 | 0.5303 | 0.5099 | 137 |
| volume_climax | UP_50 | UNSTABLE | not rejected | 0.5502 | 0.5382 | 0.5282 | 137 |
| volume_pct | UP_50 | UNSTABLE | reject | 0.6774 | 0.6394 | 0.6338 | 137 |
| volume_range | UP_50 | UNSTABLE | reject | 0.6244 | 0.5421 | 0.5673 | 137 |
| volume_roc | UP_50 | UNSTABLE | not rejected | 0.5706 | 0.5677 | 0.5558 | 137 |
| volume_z | UP_50 | UNSTABLE | reject | 0.6313 | 0.5948 | 0.5765 | 137 |
| volz_x_atr | UP_50 | UNSTABLE | reject | 0.6171 | 0.5926 | 0.5612 | 137 |
| vwap_dist | UP_50 | UNSTABLE | not rejected | 0.533 | 0.5357 | 0.5776 | 137 |
| vwap_slope | UP_50 | UNSTABLE | reject | 0.6538 | 0.5798 | 0.6464 | 137 |
| willr_14 | UP_50 | UNSTABLE | not rejected | 0.5156 | 0.5233 | 0.5678 | 137 |
| yang_zhang_24 | UP_50 | UNSTABLE | reject | 0.8813 | 0.9297 | 0.935 | 137 |
| adx_14 | UP_80 | UNSTABLE | not rejected | 0.5428 | 0.6009 | 0.6496 | 40 |
| aroon_osc | UP_80 | UNSTABLE | reject | 0.6495 | 0.6057 | 0.6789 | 40 |
| atr_accel | UP_80 | UNSTABLE | not rejected | 0.5665 | 0.5372 | 0.4833 | 40 |
| atr_change | UP_80 | UNSTABLE | not rejected | 0.5317 | 0.4494 | 0.4209 | 40 |
| atr_pct | UP_80 | UNSTABLE | reject | 0.6648 | 0.6156 | 0.7236 | 40 |
| bb_pct | UP_80 | UNSTABLE | not rejected | 0.6053 | 0.5718 | 0.6514 | 40 |
| bb_width | UP_80 | UNSTABLE | reject | 0.8459 | 0.8656 | 0.9059 | 40 |
| breadth_pos | UP_80 | OOS_ONLY | not rejected | 0.5198 | 0.4344 | 0.5318 | 40 |
| btc_ret_24 | UP_80 | OOS_ONLY | not rejected | 0.5139 | 0.3845 | 0.5433 | 40 |
| btc_rv_24 | UP_80 | UNSTABLE | not rejected | 0.6447 | 0.4435 | 0.428 | 40 |
| cci_20 | UP_80 | UNSTABLE | not rejected | 0.5936 | 0.5774 | 0.643 | 40 |
| cmf_20 | UP_80 | UNSTABLE | not rejected | 0.5429 | 0.4873 | 0.6218 | 40 |
| di_minus | UP_80 | UNSTABLE | reject | 0.6523 | 0.629 | 0.6873 | 40 |
| di_plus | UP_80 | UNSTABLE | reject | 0.6598 | 0.6146 | 0.6938 | 40 |
| dispersion | UP_80 | UNSTABLE | not rejected | 0.6118 | 0.6623 | 0.5555 | 40 |
| ema_cross_9_20 | UP_80 | UNSTABLE | not rejected | 0.6251 | 0.5923 | 0.6214 | 40 |
| ema_dist_100 | UP_80 | UNSTABLE | reject | 0.7097 | 0.6189 | 0.7337 | 40 |
| ema_dist_20 | UP_80 | UNSTABLE | not rejected | 0.6161 | 0.5841 | 0.6876 | 40 |
| ema_dist_200 | UP_80 | UNSTABLE | reject | 0.7169 | 0.6001 | 0.7277 | 40 |
| ema_dist_50 | UP_80 | UNSTABLE | reject | 0.6711 | 0.6193 | 0.7153 | 40 |
| ema_dist_9 | UP_80 | UNSTABLE | not rejected | 0.5721 | 0.5551 | 0.6295 | 40 |
| ema20_slope_1 | UP_80 | UNSTABLE | not rejected | 0.6161 | 0.5841 | 0.6876 | 40 |
| ema20_slope_12 | UP_80 | UNSTABLE | reject | 0.6864 | 0.5819 | 0.6912 | 40 |
| ema20_slope_24 | UP_80 | UNSTABLE | reject | 0.6856 | 0.6011 | 0.7209 | 40 |
| ema20_slope_3 | UP_80 | UNSTABLE | not rejected | 0.6058 | 0.59 | 0.7039 | 40 |
| ema20_slope_6 | UP_80 | UNSTABLE | reject | 0.6603 | 0.5927 | 0.727 | 40 |
| garman_klass_24 | UP_80 | UNSTABLE | reject | 0.8928 | 0.9399 | 0.9574 | 40 |
| keltner_width | UP_80 | UNSTABLE | reject | 0.8574 | 0.9274 | 0.9482 | 40 |
| macd | UP_80 | UNSTABLE | reject | 0.6622 | 0.5705 | 0.6784 | 40 |
| macd_hist | UP_80 | UNSTABLE | not rejected | 0.5716 | 0.5429 | 0.5987 | 40 |
| macd_hist_accel | UP_80 | UNSTABLE | not rejected | 0.5394 | 0.5342 | 0.5596 | 40 |
| macd_signal | UP_80 | UNSTABLE | reject | 0.6544 | 0.5856 | 0.6995 | 40 |
| mfi_14 | UP_80 | UNSTABLE | not rejected | 0.6376 | 0.6143 | 0.6467 | 40 |
| mom_accel | UP_80 | UNSTABLE | not rejected | 0.5167 | 0.4898 | 0.5117 | 40 |
| obv_slope | UP_80 | OOS_ONLY | not rejected | 0.5136 | 0.4821 | 0.5743 | 40 |
| pa_hh | UP_80 | UNSTABLE | not rejected | 0.569 | 0.5385 | 0.5833 | 40 |
| pa_inside | UP_80 | UNSTABLE | not rejected | 0.5366 | 0.5169 | 0.515 | 40 |
| parkinson_24 | UP_80 | UNSTABLE | reject | 0.8882 | 0.937 | 0.9574 | 40 |
| poc_dist | UP_80 | UNSTABLE | not rejected | 0.5126 | 0.4545 | 0.3948 | 40 |
| ppo | UP_80 | UNSTABLE | reject | 0.6893 | 0.5932 | 0.7128 | 40 |
| range_pct | UP_80 | UNSTABLE | not rejected | 0.6119 | 0.6418 | 0.7107 | 40 |
| rel_volume | UP_80 | UNSTABLE | not rejected | 0.5688 | 0.6117 | 0.6084 | 40 |
| ret_1 | UP_80 | UNSTABLE | not rejected | 0.5878 | 0.4921 | 0.5764 | 40 |
| ret_10 | UP_80 | UNSTABLE | not rejected | 0.5842 | 0.5419 | 0.6567 | 40 |
| ret_20 | UP_80 | UNSTABLE | reject | 0.6477 | 0.5979 | 0.6927 | 40 |
| ret_3 | UP_80 | UNSTABLE | not rejected | 0.4978 | 0.4224 | 0.4544 | 40 |
| ret_5 | UP_80 | UNSTABLE | not rejected | 0.5411 | 0.5415 | 0.5704 | 40 |
| ret_50 | UP_80 | UNSTABLE | not rejected | 0.6364 | 0.5851 | 0.7094 | 40 |
| ret5_x_volz | UP_80 | UNSTABLE | not rejected | 0.5348 | 0.5704 | 0.5984 | 40 |
| roc_10 | UP_80 | UNSTABLE | not rejected | 0.5842 | 0.5419 | 0.6567 | 40 |
| rogers_satchell_24 | UP_80 | UNSTABLE | reject | 0.8971 | 0.9395 | 0.9569 | 40 |
| rsi_14 | UP_80 | UNSTABLE | not rejected | 0.6333 | 0.5833 | 0.6988 | 40 |
| rsi_21 | UP_80 | UNSTABLE | reject | 0.6574 | 0.5964 | 0.7134 | 40 |
| rsi_7 | UP_80 | UNSTABLE | not rejected | 0.5859 | 0.5625 | 0.6464 | 40 |
| rv_24 | UP_80 | UNSTABLE | reject | 0.8584 | 0.9151 | 0.947 | 40 |
| stoch_k | UP_80 | UNSTABLE | not rejected | 0.5245 | 0.5217 | 0.5929 | 40 |
| stoch_rsi | UP_80 | UNSTABLE | not rejected | 0.4848 | 0.5131 | 0.5006 | 40 |
| tr_pct | UP_80 | UNSTABLE | not rejected | 0.6126 | 0.6417 | 0.7105 | 40 |
| trix_15 | UP_80 | UNSTABLE | reject | 0.6868 | 0.5879 | 0.7118 | 40 |
| vol_expansion | UP_80 | UNSTABLE | reject | 0.6843 | 0.6352 | 0.6905 | 40 |
| vol_of_vol | UP_80 | UNSTABLE | not rejected | 0.5995 | 0.5567 | 0.6359 | 40 |
| volume | UP_80 | UNSTABLE | reject | 0.7322 | 0.7245 | 0.8111 | 40 |
| volume_accel | UP_80 | UNSTABLE | not rejected | 0.5419 | 0.483 | 0.4669 | 40 |
| volume_atr | UP_80 | UNSTABLE | reject | 0.673 | 0.5349 | 0.5893 | 40 |
| volume_change | UP_80 | UNSTABLE | not rejected | 0.5857 | 0.4617 | 0.4681 | 40 |
| volume_pct | UP_80 | UNSTABLE | not rejected | 0.6423 | 0.665 | 0.6805 | 40 |
| volume_range | UP_80 | UNSTABLE | reject | 0.6757 | 0.5242 | 0.5765 | 40 |
| volume_roc | UP_80 | UNSTABLE | not rejected | 0.571 | 0.5448 | 0.5683 | 40 |
| volume_z | UP_80 | UNSTABLE | not rejected | 0.6074 | 0.6166 | 0.6201 | 40 |
| volz_x_atr | UP_80 | UNSTABLE | not rejected | 0.6065 | 0.6157 | 0.5789 | 40 |
| vwap_dist | UP_80 | UNSTABLE | not rejected | 0.564 | 0.5314 | 0.6452 | 40 |
| vwap_slope | UP_80 | UNSTABLE | reject | 0.7044 | 0.5577 | 0.7149 | 40 |
| willr_14 | UP_80 | UNSTABLE | not rejected | 0.5245 | 0.5217 | 0.5929 | 40 |
| yang_zhang_24 | UP_80 | UNSTABLE | reject | 0.8919 | 0.9369 | 0.9584 | 40 |

Trading:LiveTradingEnabled=False. Trading:CrossSectionalReversal Enabled=True PaperEnabled=False LiveEnabled=True. Scalping Enabled=False AllowLive=False. This study did not change these flags, did not enable Paper, did not promote a strategy, and did not submit an order. VALIDATED_FOR_PAPER=NONE. LIVE_APPROVED=false. Cross-sectional LiveEnabled or global live is not false in appsettings. Those values were left untouched.
