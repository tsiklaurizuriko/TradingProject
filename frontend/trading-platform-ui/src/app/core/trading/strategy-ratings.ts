/** Closeness to a cost-inclusive profit. None are profitable. None are LIVE. Never 5 stars. */
export type StrategyUseVerdict = 'paper' | 'weak' | 'avoid' | 'blocked' | 'near-miss';

export interface StrategyRating {
  stars: number;
  verdict: StrategyUseVerdict;
  note: string;
  pf: number | null;
  bookReturnPct: number | null;
  sample: string;
}

export const verdictLabels: Record<StrategyUseVerdict, string> = {
  paper: 'გამოიყენე PAPER-ზე',
  weak: 'სუსტი',
  avoid: 'არ გამოიყენო',
  blocked: 'ვერ გაეშვება',
  'near-miss': 'NEAR-MISS · არ არის დადასტურებული',
};

export const starSlots = [1, 2, 3, 4, 5] as const;

/** Frozen five: 528 coins × 3 TF Model B, equal-book return. Research: 10×3 90d unless noted. */
const ratings: Record<string, StrategyRating> = {
  flat_range: {
    stars: 4,
    verdict: 'weak',
    note: 'ყველაზე ახლოს. OOS 67556 ტრეიდი, საშუალო +0.08% 0.12% ხარჯის შემდეგ. 1.5× ხარჯზე საშუალო 0ა, 2×-ზე უარყოფითი. გარიგებების 89% სტოპზე იხურება. ორდერის საფუძველი არაა.',
    pf: null,
    bookReturnPct: null,
    sample: '527×1h OOS',
  },
  mac_contrarian_7_10: {
    stars: 3,
    verdict: 'weak',
    note: 'MAc(7,10,0.01), 5 წუთი. სწრაფი SMA ნელ ზოლს 1%-ით რომ სცდება, პოზიცია ტრიალდება საწინააღმდეგოდ. სტოპი არ აქვს. 2019–2022 walk-forward პლუსშია; 2023–2026 სუსტია.',
    pf: null,
    bookReturnPct: null,
    sample: 'BTC 5m 2017–2022',
  },
  zigzag_fade: {
    stars: 3,
    verdict: 'weak',
    note: 'სვინგის გარღვევის საწინააღმდეგო შესვლა. ნაგულისხმევი BTC 30m: deviation 2%, ATR 1.5. ETH-ზე 6%, SOL-ზე 5%.',
    pf: 1.289,
    bookReturnPct: null,
    sample: 'BTC 30m published',
  },
  binhv45: {
    stars: 2,
    verdict: 'weak',
    note: 'BinHV45, 1 წუთი, მხოლოდ ლონგი. ქვედა Bollinger-ის ქვეშ პატარა ჩრდილით. გასვლის სიგნალი არაა: ტეიკი 1.25%, სტოპი 5%. ჩვენს ფიუჩერსებზე არ არის გაზომილი.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade BinHV45',
  },
  cluc_may72018: {
    stars: 2,
    verdict: 'weak',
    note: 'Cluc, 5 წუთი, მხოლოდ ლონგი. EMA(50) და ქვედა ზოლის 98.5%-ის ქვეშ, წყნარი მოცულობა. გასვლა შუა ზოლზე. ტეიკი 1%, სტოპი 5%.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade Cluc',
  },
  combined_binh_cluc: {
    stars: 2,
    verdict: 'weak',
    note: 'BinHV45 ან Cluc, 5 წუთი, მხოლოდ ლონგი. შუა ზოლი მხოლოდ მოგებაში ხურავს. ტეიკი 5%, სტოპი 5%. 2018-ის სპოტის წესია, აქ არ არის გაზომილი.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade Combined',
  },
  fadx_sma: {
    stars: 2,
    verdict: 'weak',
    note: 'ADX SMA, 1 საათი, ორივე მხარე. SMA(12) კვეთს SMA(48)-ს, ADX 30-ზე მეტია. გასვლა ADX 30-ის ქვემოთ. ტეიკი 5%, სტოპი 5%.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade FAdxSma',
  },
  triple_supertrend: {
    stars: 2,
    verdict: 'weak',
    note: 'სამი Supertrend, 1 საათი, ორივე მხარე. ტეიკი 10%, სტოპი 26.5%, მხოლოდ 1x. Hyperopt-ის რიცხვებია, აქ არ არის გაზომილი.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade FSupertrend',
  },
  hlhb: {
    stars: 2,
    verdict: 'weak',
    note: 'HLHB, 4 საათი, მხოლოდ ლონგი. RSI და EMA ერთ ბარზე კვეთენ, ADX 25-ზე მეტია. გამოქვეყნებული hyperopt: ტეიკი 62%, სტოპი 32%, მხოლოდ 1x.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade HLHB',
  },
  donchian_v2_55: {
    stars: 3,
    verdict: 'weak',
    note: 'დღიური Donchian. შესვლა 55, გასვლა 5, ATR სტოპი 1.5. ტეიკი გამორთულია. ETH-ზე PF 2.10, buy-and-hold არ არის გამოკლებული.',
    pf: 2.099,
    bookReturnPct: null,
    sample: 'ETH daily 2017–2026',
  },
  flow_zone: {
    stars: 2,
    verdict: 'weak',
    note: 'ლოგიკური სიგნალი, წარსულზე არ არის გაზომილი. 24 საათის ზონა, taker-ის უმრავლესობა და მზარდი ღია პოზიცია. Live-ზე შენ ამოწმებ. თავისით არ ეშვება.',
    pf: null,
    bookReturnPct: null,
    sample: 'არ არის გაზომილი',
  },
  btc_daily_max_10: {
    stars: 2,
    verdict: 'weak',
    note: 'BTC დღიური, მხოლოდ ლონგი, 10 დღის მაქსიმუმი. IS −1%, VAL +8%, OOS −2% 12 bp ხარჯის შემდეგ. Live შეგიძლია ჩართო Bots-ზე; თავისით არ ეშვება. რისკის წიგნი 1x, სტოპი 8%.',
    pf: null,
    bookReturnPct: null,
    sample: 'BTC 1d IS/VAL/OOS',
  },
  ts_momentum_28_5: {
    stars: 2,
    verdict: 'weak',
    note: 'BTC დღიური, მხოლოდ ლონგი. 28 დღის ამონაგები საკუთარ ზედა მესამედში. IS +47%, VAL −11%, OOS +7% 12 bp ხარჯის შემდეგ. Live შეგიძლია ჩართო Bots-ზე; თავისით არ ეშვება. რისკის წიგნი 1x, სტოპი 8%.',
    pf: null,
    bookReturnPct: null,
    sample: 'BTC 1d IS/VAL/OOS',
  },
  btc_ema20_ema50_long: {
    stars: 3,
    verdict: 'weak',
    note: '22 მონეტა, სრული 2 წელი, თანაბარი წილი +16%, PF 1.06. 17 მოგებაშია, 5 ზარალში. ეს იგივე ფანჯარაა, OOS არ არის. ვარდნა ≈ 49%.',
    pf: 1.06,
    bookReturnPct: 15.97,
    sample: '22×30m full window',
  },
  market_structure_trend: {
    stars: 3,
    verdict: 'weak',
    note: 'მცირე ნიმუშზე PF ≈ 1.12, მაინც OOS_FAILED. SHORT კლავს. ფულს ნუ ენდობი.',
    pf: 1.12478747,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  rsi_pullback: {
    stars: 2,
    verdict: 'weak',
    note: '528 წიგნზე ყველაზე ნაკლები ზიანი (−1.18%), მაგრამ PF 0.61. წიგნი თითქმის ბრტყელია, მოგებული ტრეიდი ზარალს ვერ ფარავს.',
    pf: 0.61471419,
    bookReturnPct: -1.18,
    sample: '528×3 Model B',
  },
  bollinger_reversion: {
    stars: 1,
    verdict: 'avoid',
    note: '528 წიგნზე ≈ −14%, PF 0.59. მოგებასთან ახლოს არაა.',
    pf: 0.58519221,
    bookReturnPct: -14.33,
    sample: '528×3 Model B',
  },
  ema_rsi_trend: {
    stars: 1,
    verdict: 'avoid',
    note: '528 წიგნზე ≈ −40%. არ გაუშვა.',
    pf: 0.75374781,
    bookReturnPct: -39.57,
    sample: '528×3 Model B',
  },
  macd_trend: {
    stars: 1,
    verdict: 'avoid',
    note: 'Frozen, მაგრამ 528 წიგნზე ≈ −69%. არ გაუშვა.',
    pf: 0.694762,
    bookReturnPct: -69.39,
    sample: '528×3 Model B',
  },
  donchian_breakout: {
    stars: 1,
    verdict: 'avoid',
    note: 'Frozen, მაგრამ 528 წიგნზე ≈ −89%. არ გაუშვა.',
    pf: 0.70481684,
    bookReturnPct: -89.27,
    sample: '528×3 Model B',
  },
  supertrend_ema_trend: {
    stars: 2,
    verdict: 'weak',
    note: '10 ქოინზე PF ≈ 0.97, მაინც OOS_FAILED. ფულს ნუ ენდობი.',
    pf: 0.96888885,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  turtle_tsm: {
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED. SHORT კლავს. არ გაუშვა.',
    pf: 0.74585073,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  vwap_pullback_trend: {
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED, PF ≈ 0.57. არ გაუშვა.',
    pf: 0.57412368,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  volatility_breakout: {
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED. SHORT კლავს. არ გაუშვა.',
    pf: 0.77601463,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  oi_price_momentum: {
    stars: 0,
    verdict: 'blocked',
    note: 'ისტორიული OI ბოტის გზაზე არაა. არ გაეშვება.',
    pf: null,
    bookReturnPct: null,
    sample: 'DATA_UNAVAILABLE',
  },
  funding_oi_regime: {
    stars: 0,
    verdict: 'blocked',
    note: 'Funding+OI ბოტის გზაზე არაა. არ გაეშვება.',
    pf: null,
    bookReturnPct: null,
    sample: 'DATA_UNAVAILABLE',
  },
  liq_sweep_continuation: {
    stars: 2,
    verdict: 'weak',
    note: 'RESEARCHING, PF ≈ 1.00, 1.5× ხარჯზე იშლება. SHORT კლავს.',
    pf: 1.00255927,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  vol_squeeze_structure: {
    stars: 2,
    verdict: 'weak',
    note: 'PF ≈ 1.02, OOS_FAILED, COST_FRAGILE. SHORT კლავს.',
    pf: 1.01958553,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  vwap_breakout_volume: {
    stars: 2,
    verdict: 'weak',
    note: 'PF ≈ 1.01, OOS_FAILED. LONG-ზეა მთელი ეჯი. არ გააფართოო.',
    pf: 1.0124606,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  vp_vwap_reversion: {
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED, PF ≈ 0.59. არ გაუშვა.',
    pf: 0.59172023,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  liq_sweep_reversal: {
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED. SHORT კლავს. არ გაუშვა.',
    pf: 0.6550814,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  vwap_deviation_reversion: {
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED. არ გაუშვა.',
    pf: 0.80144577,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  failed_breakout_reversal: {
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED. არ გაუშვა.',
    pf: 0.77893165,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  market_structure_pullback: {
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED. არ გაუშვა.',
    pf: 0.84132655,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  atr_normalized_momentum: {
    stars: 2,
    verdict: 'weak',
    note: 'PF 0.91. 1-თან ახლოსაა და მაინც ზარალია. OOS_FAILED.',
    pf: 0.914046,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  mtf_trend_structure: {
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED. არ გაუშვა.',
    pf: 0.88702767,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  zscore_mean_reversion: {
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED. არ გაუშვა.',
    pf: 0.75065472,
    bookReturnPct: null,
    sample: '10×3 90d',
  },
  funding_basis_rv: {
    stars: 2,
    verdict: 'weak',
    note: '2 წელზე PF 0.90. 1-თან ახლოსაა და მაინც ზარალია. OOS_FAILED.',
    pf: 0.89629023,
    bookReturnPct: null,
    sample: 'Phase 4 ~2y',
  },
  funding_extreme_momentum_exhaustion: {
    stars: 2,
    verdict: 'weak',
    note: '2 წელზე PF 0.94. 1-თან ახლოსაა და მაინც ზარალია. OOS_FAILED.',
    pf: 0.93616736,
    bookReturnPct: null,
    sample: 'Phase 4 ~2y',
  },
  funding_price_momentum: {
    stars: 2,
    verdict: 'weak',
    note: 'PF ≈ 1.01 მოკლე ფანჯარაზე. SHORT კლავს. დაახლოებით ნულია, არა მოგება.',
    pf: 1.00875788,
    bookReturnPct: null,
    sample: 'Phase 3 10×3 ~90d',
  },
  basis_mean_reversion: {
    stars: 1,
    verdict: 'avoid',
    note: 'Phase 3 OOS_FAILED. არ გაუშვა.',
    pf: 0.77253489,
    bookReturnPct: null,
    sample: 'Phase 3 10×3 ~90d',
  },
  funding_basis_vwap: {
    stars: 0,
    verdict: 'blocked',
    note: 'ნიმუში მცირეა (n<50). დასკვნა არ იკითხება.',
    pf: null,
    bookReturnPct: null,
    sample: 'INSUFFICIENT_DATA',
  },
  funding_oi_reversal: {
    stars: 0,
    verdict: 'blocked',
    note: 'OI მხოლოდ ~29 დღე. 3-დღიან PF არ ენდო.',
    pf: null,
    bookReturnPct: null,
    sample: 'OI_SAMPLE_LIMITED',
  },
  oi_price_volume_regime: {
    stars: 0,
    verdict: 'blocked',
    note: 'OI მხოლოდ ~29 დღე. არ გაუშვა.',
    pf: null,
    bookReturnPct: null,
    sample: 'OI_SAMPLE_LIMITED',
  },
  oi_breakout_confirmation: {
    stars: 0,
    verdict: 'blocked',
    note: 'OI მხოლოდ ~29 დღე. არ გაუშვა.',
    pf: null,
    bookReturnPct: null,
    sample: 'OI_SAMPLE_LIMITED',
  },
  taker_flow_momentum: {
    stars: 0,
    verdict: 'blocked',
    note: 'taker n=7. INSUFFICIENT_DATA.',
    pf: 0,
    bookReturnPct: null,
    sample: 'INSUFFICIENT_DATA',
  },
  crypto_pairs_arb: {
    stars: 0,
    verdict: 'blocked',
    note: 'წყვილების სამყარო არ არის ჩართული. არ გაეშვება.',
    pf: null,
    bookReturnPct: null,
    sample: 'DATA_UNAVAILABLE',
  },
  xs_relative_strength: {
    stars: 0,
    verdict: 'blocked',
    note: 'ჯვარედინი რანკი არ არის ჩართული. არ გაეშვება.',
    pf: null,
    bookReturnPct: null,
    sample: 'DATA_UNAVAILABLE',
  },
  regime_strategy_router: {
    stars: 0,
    verdict: 'blocked',
    note: 'როუტერი არ არის ჩართული. n=0.',
    pf: null,
    bookReturnPct: null,
    sample: 'RESEARCHING n=0',
  },
  vol_spike_ema_trend: {
    stars: 2,
    verdict: 'weak',
    note: 'BTC 15m-ზე ისტორიას მოერგო: +14% იმავე ფანჯარაზე. OOS არ აქვს.',
    pf: null,
    bookReturnPct: 13.82,
    sample: 'BTCUSDT 15m fitted 2y',
  },
  bb20_2_break: {
    stars: 2,
    verdict: 'weak',
    note: 'BTC 15m-ზე ისტორიას მოერგო: +11% იმავე ფანჯარაზე. OOS არ აქვს.',
    pf: null,
    bookReturnPct: 11.26,
    sample: 'BTCUSDT 15m fitted 2y',
  },
  cpa_near_miss_sweep_contextual_5m: nearMiss('VAL 88 ტრეიდი, PF 1.15, net +44. IS PF 0.36-ზე ჩავარდა. არ არის validated.'),
  cpa_near_miss_sweep_strict_5m: nearMiss('OOS 78 ტრეიდი, PF 1.07, net +19. Walk-forward 2 ტრეიდი. არ არის validated.'),
  cpa_near_miss_pullback_contextual_5m: nearMiss('VAL 52 ტრეიდი, PF 1.11, net +21. IS ნიმუში 30-ზე ნაკლებია. არ არის validated.'),
  cpa_near_miss_wm_contextual_5m: nearMiss('VAL 514 ტრეიდი, PF 1.04, net +80. IS PF 0.61. არ არის validated.'),
  cpa_near_miss_wm_strict_5m: nearMiss('VAL 115 ტრეიდი, PF 1.02, net +6. IS PF 0.71. არ არის validated.'),
  cpa_near_miss_compression_continuation_5m: nearMiss('OOS 50 ტრეიდი, PF 1.08, net +14. ბეისლაინია, walk-forward 2. არ არის validated.'),
  cpa_near_miss_mtf_strict_5m: nearMiss('VAL 76 ტრეიდი, PF 1.03, net +9. IS PF 0.62. არ არის validated.'),
};

function nearMiss(note: string): StrategyRating {
  return {
    stars: 1,
    verdict: 'near-miss',
    note,
    pf: null,
    bookReturnPct: null,
    sample: 'Phase 8 NEAR_MISS',
  };
}

const unrated: StrategyRating = {
  stars: 0,
  verdict: 'blocked',
  note: 'რეიტინგი არაა. არ გაუშვა.',
  pf: null,
  bookReturnPct: null,
  sample: 'unrated',
};

const scalpRating: StrategyRating = {
  stars: 1,
  verdict: 'blocked',
  note: 'კვლევის სკალპია. სიაშია იმავე ბარათით. ბოტს არ სტარტავს.',
  pf: null,
  bookReturnPct: null,
  sample: 'research',
};

const reversalRating: StrategyRating = {
  stars: 1,
  verdict: 'avoid',
  note: 'გაზომილი წიგნი OOS-ზე ხარჯამდეც უარყოფითია. 24სთ რევერსალი VAL-ზე PF 0.75, OOS-ზე 1.56 — არასტაბილურია. არ გაუშვა.',
  pf: 1.0271,
  bookReturnPct: null,
  sample: '521×1h 24h book',
};

const priceActionRating: StrategyRating = {
  stars: 1,
  verdict: 'avoid',
  note: 'ფასის მოქმედების წიგნები ხარჯის შემდეგ ზარალიანია. NEAR-MISS რიგები ცალკეა და ისინიც ვერ გავიდა.',
  pf: null,
  bookReturnPct: null,
  sample: 'price action research',
};

export function isOperatorCatalog(templateKey: string | undefined): boolean {
  const verdict = ratingFor(templateKey).verdict;
  return verdict === 'paper' || verdict === 'weak';
}

export function isNearMiss(templateKey: string | undefined): boolean {
  return !!templateKey && templateKey.startsWith('cpa_near_miss_');
}

export function ratingFor(templateKey: string | undefined): StrategyRating {
  if (!templateKey) {
    return unrated;
  }
  if (templateKey.startsWith('scalp_')) {
    return scalpRating;
  }
  if (templateKey.startsWith('cross_sectional_reversal')) {
    return reversalRating;
  }
  if (templateKey.startsWith('pa_')) {
    return priceActionRating;
  }
  return ratings[templateKey] ?? unrated;
}

export function verdictLabel(templateKey: string | undefined): string {
  return verdictLabels[ratingFor(templateKey).verdict];
}

export function ratingSortValue(templateKey: string | undefined): number {
  const rate = ratingFor(templateKey);
  return rate.stars * 1000 + (rate.pf ?? 0) * 10 + (rate.bookReturnPct ?? 0);
}

export function starText(stars: number): string {
  const n = Math.min(5, Math.max(0, Math.floor(stars)));
  return `${'★'.repeat(n)}${'☆'.repeat(5 - n)}`;
}

export function formatPf(value: number | null): string {
  return value == null ? 'N/A' : value.toFixed(3);
}

export function formatBookReturn(value: number): string {
  const abs = Math.abs(value).toFixed(2);
  return `${value < 0 ? '−' : value > 0 ? '+' : ''}${abs}%`;
}

export function ratingMeta(rate: StrategyRating): string {
  const pf = `PF ${formatPf(rate.pf)}`;
  const book = rate.bookReturnPct == null ? '' : ` · წიგნი ${formatBookReturn(rate.bookReturnPct)}`;
  return `${pf}${book} · ${rate.sample}`;
}
