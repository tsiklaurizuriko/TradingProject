# Extreme move feature catalog

Research catalog only. No feature here is a trading signal. No feature is an edge.

feature_manifest_hash `57a6a430c7d5a5af123a3980c047a6c9583a90efa572e647e28cf0a4735e7aef`

Timestamp alignment: the feature at bar T uses the closed 1h bar and earlier closed bars only. Future bars are used only to label the event. Missing prints stay null. They are not filled with zero and they are not forward-filled.

| FeatureId | Family | Description | Formula | Required data | Lookback | Alignment | Causality | Availability | Research status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ret_1 | price_momentum | ret 1 | close[t]/close[t-k]-1 | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ret_3 | price_momentum | ret 3 | close[t]/close[t-k]-1 | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ret_5 | price_momentum | ret 5 | close[t]/close[t-k]-1 | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ret_10 | price_momentum | ret 10 | close[t]/close[t-k]-1 | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ret_20 | price_momentum | ret 20 | close[t]/close[t-k]-1 | OHLCV | 20 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ret_50 | price_momentum | ret 50 | close[t]/close[t-k]-1 | OHLCV | 50 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| roc_10 | price_momentum | roc 10 | close[t]/close[t-k]-1 | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| mom_accel | price_momentum | mom accel | ret_5[t]-ret_5[t-5] | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema_dist_9 | price_momentum | ema dist 9 | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema_dist_20 | price_momentum | ema dist 20 | see feature id; causal window ending at t | OHLCV | 20 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema_dist_50 | price_momentum | ema dist 50 | see feature id; causal window ending at t | OHLCV | 50 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema_dist_100 | price_momentum | ema dist 100 | see feature id; causal window ending at t | OHLCV | 100 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema_dist_200 | price_momentum | ema dist 200 | see feature id; causal window ending at t | OHLCV | 200 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema20_slope_1 | price_momentum | ema20 slope 1 | see feature id; causal window ending at t | OHLCV | 20 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema20_slope_3 | price_momentum | ema20 slope 3 | see feature id; causal window ending at t | OHLCV | 20 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema20_slope_6 | price_momentum | ema20 slope 6 | see feature id; causal window ending at t | OHLCV | 20 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema20_slope_12 | price_momentum | ema20 slope 12 | see feature id; causal window ending at t | OHLCV | 20 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema20_slope_24 | price_momentum | ema20 slope 24 | see feature id; causal window ending at t | OHLCV | 24 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ema_cross_9_20 | price_momentum | ema cross 9 20 | see feature id; causal window ending at t | OHLCV | 20 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| macd | price_momentum | macd | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| macd_signal | price_momentum | macd signal | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| macd_hist | price_momentum | macd hist | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| macd_hist_accel | price_momentum | macd hist accel | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| rsi_7 | price_momentum | rsi 7 | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| rsi_14 | price_momentum | rsi 14 | see feature id; causal window ending at t | OHLCV | 14-15 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| rsi_21 | price_momentum | rsi 21 | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| stoch_k | price_momentum | stoch k | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| stoch_rsi | price_momentum | stoch rsi | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| cci_20 | price_momentum | cci 20 | see feature id; causal window ending at t | OHLCV | 20 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| willr_14 | price_momentum | willr 14 | see feature id; causal window ending at t | OHLCV | 14-15 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| mfi_14 | price_momentum | mfi 14 | see feature id; causal window ending at t | OHLCV | 14-15 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| adx_14 | price_momentum | adx 14 | see feature id; causal window ending at t | OHLCV | 14-15 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| di_plus | price_momentum | di plus | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| di_minus | price_momentum | di minus | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| aroon_osc | price_momentum | aroon osc | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| trix_15 | price_momentum | trix 15 | see feature id; causal window ending at t | OHLCV | 14-15 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| ppo | price_momentum | ppo | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| atr_pct | volatility | atr pct | percentile of ATR(14) in the prior 100 closes including t | OHLCV | 100 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| atr_change | volatility | atr change | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| atr_accel | volatility | atr accel | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| rv_24 | volatility | rv 24 | see feature id; causal window ending at t | OHLCV | 24 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| parkinson_24 | volatility | parkinson 24 | see feature id; causal window ending at t | OHLCV | 24 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| garman_klass_24 | volatility | garman klass 24 | see feature id; causal window ending at t | OHLCV | 24 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| rogers_satchell_24 | volatility | rogers satchell 24 | see feature id; causal window ending at t | OHLCV | 24 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| yang_zhang_24 | volatility | yang zhang 24 | see feature id; causal window ending at t | OHLCV | 24 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| range_pct | volatility | range pct | see feature id; causal window ending at t | OHLCV | 100 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| tr_pct | volatility | tr pct | see feature id; causal window ending at t | OHLCV | 100 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| vol_compression | volatility | vol compression | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| vol_expansion | volatility | vol expansion | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| bb_width | volatility | bb width | (upper-lower)/middle, 20 bar, 2 sd | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| bb_pct | volatility | bb pct | see feature id; causal window ending at t | OHLCV | 100 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| keltner_width | volatility | keltner width | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| bb_keltner_squeeze | volatility | bb keltner squeeze | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| vol_of_vol | volatility | vol of vol | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volume | volume | volume | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volume_change | volume | volume change | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volume_roc | volume | volume roc | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| rel_volume | volume | rel volume | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volume_pct | volume | volume pct | see feature id; causal window ending at t | OHLCV | 100 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volume_z | volume | volume z | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volume_accel | volume | volume accel | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volume_atr | volume | volume atr | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volume_range | volume | volume range | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| obv_slope | volume | obv slope | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| cmf_20 | volume | cmf 20 | see feature id; causal window ending at t | OHLCV | 20 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pv_divergence | volume | pv divergence | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volume_climax | volume | volume climax | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volume_compression | volume | volume compression | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| vwap_dist | vwap_profile | vwap dist | close/rolling24 vwap-1 | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| vwap_slope | vwap_profile | vwap slope | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| poc_dist | vwap_profile | poc dist | close/24-bar volume-mode close bin-1 | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| oi_change_1 | open_interest | oi change 1 | (oi[t]-oi[t-k])/oi[t-k], null if either print missing | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_change_3 | open_interest | oi change 3 | (oi[t]-oi[t-k])/oi[t-k], null if either print missing | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_change_6 | open_interest | oi change 6 | (oi[t]-oi[t-k])/oi[t-k], null if either print missing | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_change_12 | open_interest | oi change 12 | (oi[t]-oi[t-k])/oi[t-k], null if either print missing | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_change_24 | open_interest | oi change 24 | (oi[t]-oi[t-k])/oi[t-k], null if either print missing | Vision sum_open_interest | 24 | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_pct_change_12 | open_interest | oi pct change 12 | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_z | open_interest | oi z | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_pct | open_interest | oi pct | see feature id; causal window ending at t | Vision sum_open_interest | 100 | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_accel | open_interest | oi accel | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_vol | open_interest | oi vol | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_over_volume | open_interest | oi over volume | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| price_up_oi_up | open_interest | price up oi up | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| price_up_oi_down | open_interest | price up oi down | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| price_down_oi_up | open_interest | price down oi up | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| price_down_oi_down | open_interest | price down oi down | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| ret_x_oi | open_interest | ret x oi | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_x_volume | open_interest | oi x volume | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_x_vol | open_interest | oi x vol | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| oi_x_funding | open_interest | oi x funding | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_rate | funding | funding rate | last settled funding at or before bar close | settled funding | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_pct | funding | funding pct | see feature id; causal window ending at t | settled funding | 100 | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_z | funding | funding z | see feature id; causal window ending at t | settled funding | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_change | funding | funding change | see feature id; causal window ending at t | settled funding | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_accel | funding | funding accel | see feature id; causal window ending at t | settled funding | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| cum_funding | price_momentum | cum funding | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| funding_persistence | funding | funding persistence | see feature id; causal window ending at t | settled funding | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_extreme | funding | funding extreme | see feature id; causal window ending at t | settled funding | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_reversal | funding | funding reversal | see feature id; causal window ending at t | settled funding | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_x_oi | funding | funding x oi | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_x_price | funding | funding x price | see feature id; causal window ending at t | settled funding | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_x_mom | funding | funding x mom | see feature id; causal window ending at t | settled funding | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| basis | basis | basis | mark/index-1 on the same hour, else null | mark and index | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| basis_change | basis | basis change | see feature id; causal window ending at t | mark and index | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| basis_accel | basis | basis accel | see feature id; causal window ending at t | mark and index | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| basis_pct | basis | basis pct | see feature id; causal window ending at t | mark and index | 100 | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| basis_z | basis | basis z | see feature id; causal window ending at t | mark and index | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| basis_x_oi | basis | basis x oi | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| basis_x_funding | basis | basis x funding | see feature id; causal window ending at t | mark and index | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| basis_x_price | basis | basis x price | see feature id; causal window ending at t | mark and index | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| taker_buy_ratio | taker | taker buy ratio | see feature id; causal window ending at t | kline taker buy | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| taker_imbalance | taker | taker imbalance | 2*takerBuy/volume-1 when 0<takerBuy<=volume | kline taker buy | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| taker_imbalance_change | taker | taker imbalance change | see feature id; causal window ending at t | kline taker buy | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| taker_imbalance_accel | taker | taker imbalance accel | see feature id; causal window ending at t | kline taker buy | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| taker_imbalance_pct | taker | taker imbalance pct | see feature id; causal window ending at t | kline taker buy | 100 | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| taker_imbalance_z | taker | taker imbalance z | see feature id; causal window ending at t | kline taker buy | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| taker_x_oi | taker | taker x oi | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| taker_x_funding | taker | taker x funding | see feature id; causal window ending at t | kline taker buy | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| taker_x_volume | taker | taker x volume | see feature id; causal window ending at t | kline taker buy | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| taker_x_ret | taker | taker x ret | see feature id; causal window ending at t | kline taker buy | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| depth_imbalance | depth | depth imbalance | last ±1% book imbalance inside the hour, else null | bookDepth ±1% | 1-48 closed bars | closed 1h bar | causal | BTCUSDT ETHUSDT BNBUSDT only | RESEARCHING |
| depth_imbalance_change | depth | depth imbalance change | see feature id; causal window ending at t | bookDepth ±1% | 1-48 closed bars | closed 1h bar | causal | BTCUSDT ETHUSDT BNBUSDT only | RESEARCHING |
| depth_imbalance_accel | depth | depth imbalance accel | see feature id; causal window ending at t | bookDepth ±1% | 1-48 closed bars | closed 1h bar | causal | BTCUSDT ETHUSDT BNBUSDT only | RESEARCHING |
| depth_pct | depth | depth pct | see feature id; causal window ending at t | bookDepth ±1% | 100 | closed 1h bar | causal | BTCUSDT ETHUSDT BNBUSDT only | RESEARCHING |
| depth_z | depth | depth z | see feature id; causal window ending at t | bookDepth ±1% | 1-48 closed bars | closed 1h bar | causal | BTCUSDT ETHUSDT BNBUSDT only | RESEARCHING |
| depth_ratio | depth | depth ratio | see feature id; causal window ending at t | bookDepth ±1% | 1-48 closed bars | closed 1h bar | causal | BTCUSDT ETHUSDT BNBUSDT only | RESEARCHING |
| breadth_pos | cross_section | breadth pos | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | coins present on that hour, minimum 30 | RESEARCHING |
| btc_ret_24 | cross_section | btc ret 24 | see feature id; causal window ending at t | OHLCV | 24 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| btc_rv_24 | cross_section | btc rv 24 | see feature id; causal window ending at t | OHLCV | 24 | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| dispersion | cross_section | dispersion | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | coins present on that hour, minimum 30 | RESEARCHING |
| pa_hh | price_action | pa hh | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_bos | price_action | pa bos | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_choch | price_action | pa choch | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_sweep | price_action | pa sweep | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_compress | price_action | pa compress | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_inside | price_action | pa inside | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_outside | price_action | pa outside | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_engulf | price_action | pa engulf | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_pin | price_action | pa pin | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_hammer | price_action | pa hammer | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_star | price_action | pa star | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_marubozu | price_action | pa marubozu | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| pa_flag | price_action | pa flag | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | existing chart engine, 10-coin set | RESEARCHING |
| pa_hs | price_action | pa hs | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | existing chart engine, 10-coin set | RESEARCHING |
| pa_wedge | price_action | pa wedge | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | existing chart engine, 10-coin set | RESEARCHING |
| pa_triangle | price_action | pa triangle | see feature id; causal window ending at t | OHLCV via existing price-action engine | 1-48 closed bars | closed 1h bar | causal | existing chart engine, 10-coin set | RESEARCHING |
| ret5_x_oi | interaction | ret5 x oi | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| ret5_x_funding | interaction | ret5 x funding | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| ret5_x_oi_x_funding | interaction | ret5 x oi x funding | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| ret5_x_volz | interaction | ret5 x volz | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| volz_x_atr | volatility | volz x atr | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 1h OHLCV cache | RESEARCHING |
| oi_x_taker | open_interest | oi x taker | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| funding_x_basis | funding | funding x basis | see feature id; causal window ending at t | settled funding | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| depth_x_taker | depth | depth x taker | see feature id; causal window ending at t | bookDepth ±1% | 1-48 closed bars | closed 1h bar | causal | BTCUSDT ETHUSDT BNBUSDT only | RESEARCHING |
| cs_rank_x_oi | cross_section | cs rank x oi | see feature id; causal window ending at t | Vision sum_open_interest | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |
| cs_rank_x_funding | cross_section | cs rank x funding | see feature id; causal window ending at t | OHLCV | 1-48 closed bars | closed 1h bar | causal | 10-coin Vision/funding/basis set; taker only if cache field 9 is real | RESEARCHING |

## Not computed

- Liquidations: DATA_UNAVAILABLE. Binance Vision liquidation snapshots are not on disk. They were not reconstructed from price.
- Best bid/ask spread: DATA_UNAVAILABLE. Book-depth files are ±1% notional imbalance, not a spread.
- NR4, NR7: NOT_IMPLEMENTED in the existing price-action engine. A second pattern engine was not added.
- Cup and handle: existing engine marks it not implemented.
- Exchange volume-profile snapshots: DATA_UNAVAILABLE. `poc_dist` is a causal 24-bar close-bin profile built from OHLCV, not an exchange profile.
- REST open-interest history (~30 days) was not used. Open interest is the Vision 5m `sum_open_interest` series, last print inside the closed hour.

Trading:LiveTradingEnabled=False. Trading:CrossSectionalReversal Enabled=True PaperEnabled=False LiveEnabled=True. Scalping Enabled=False AllowLive=False. This study did not change these flags, did not enable Paper, did not promote a strategy, and did not submit an order. VALIDATED_FOR_PAPER=NONE. LIVE_APPROVED=false. Cross-sectional LiveEnabled or global live is not false in appsettings. Those values were left untouched.
