/** Stars follow what the shared repos and public write-ups treat as preferred. Not a profit score. */
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
  paper: 'Paper runtime is removed. This is not a live approval.',
  weak: 'სუსტი',
  avoid: 'არ გამოიყენო',
  blocked: 'ვერ გაეშვება',
  'near-miss': 'NEAR-MISS · არ არის დადასტურებული',
};

export const starSlots = [1, 2, 3, 4, 5] as const;

/** Frozen five: 528 coins × 3 TF Model B, equal-book return. Research: 10×3 90d unless noted. */
const ratings: Record<string, StrategyRating> = {
  flat_range: {
    stars: 1,
    verdict: 'weak',
    note: 'ამ რეპოებში არ დგას. საჯაროდაც არ არის ის სტრატეგია, რომელსაც უპირატესობას ანიჭებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'არ არის რეპოებში',
  },
  mac_contrarian_7_10: {
    stars: 1,
    verdict: 'weak',
    note: 'ამ რეპოებში არ დგას. საჯაროდაც არ არის ის სტრატეგია, რომელსაც უპირატესობას ანიჭებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'არ არის რეპოებში',
  },
  zigzag_fade: {
    stars: 2,
    verdict: 'weak',
    note: 'საჯაროდ ცნობილი სვინგის ინსტრუმენტია, მაგრამ ამ რეპოებში მას უპირატესობას არ ანიჭებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'საჯარო, არა რეპოს ფავორიტი',
  },
  binhv45: {
    stars: 4,
    verdict: 'paper',
    note: 'freqtrade-strategies-ის კლასიკა და davidzr-ის BinH ოჯახის მთავარი. მარტო კომბინაციაზე დაბლა აყენებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade-strategies · davidzr',
  },
  cluc_may72018: {
    stars: 4,
    verdict: 'paper',
    note: 'ყველაზე განტოტილი ხაზი: davidzr-ში ClucHAnix ოჯახია. საჯაროდაც ამ დიპს უფრო აგრძელებენ, ვიდრე BinHV45-ს მარტო.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade-strategies · davidzr',
  },
  combined_binh_cluc: {
    stars: 5,
    verdict: 'paper',
    note: 'რეპოებიც და საჯარო წერილებიც ამას ანიჭებენ უპირატესობას: BinHV45 და Cluc ერთად, ორივეს ნაცვლად.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade-strategies · davidzr · საჯარო',
  },
  fadx_sma: {
    stars: 3,
    verdict: 'weak',
    note: 'freqtrade-strategies-ში დგას როგორც მაგალითი. ცალკე ფავორიტად არ წერენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade-strategies',
  },
  triple_supertrend: {
    stars: 4,
    verdict: 'paper',
    note: 'ოფიციალურ რეპოში FSupertrend-ია. Supertrend ინტერნეტშიც ერთ-ერთი ყველაზე კოპირებული ტრენდის ინსტრუმენტია.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade-strategies · საჯარო',
  },
  hlhb: {
    stars: 2,
    verdict: 'weak',
    note: 'ოფიციალურ რეპოში დგას, მაგრამ ახლა მასზე იშვიათად ჩერდებიან.',
    pf: null,
    bookReturnPct: null,
    sample: 'freqtrade-strategies',
  },
  donchian_v2_55: {
    stars: 3,
    verdict: 'weak',
    note: 'კუს სისტემა საჯაროდ პატივცემულია. ამ freqtrade რეპოებში მას წინ არ წევენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'საჯარო turtle',
  },
  squeeze_watch: {
    stars: 2,
    verdict: 'weak',
    note: 'საჯარო ფიუჩერსების აღწერაა: მშვიდ ფასზე მზარდი open interest და უკიდურესი funding. ამ freqtrade რეპოების ფაილი არ არის.',
    pf: null,
    bookReturnPct: null,
    sample: 'საჯარო funding + OI',
  },
  flow_zone: {
    stars: 1,
    verdict: 'weak',
    note: 'ამ რეპოებში არ დგას. საჯაროდაც არ არის ის სტრატეგია, რომელსაც უპირატესობას ანიჭებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'არ არის რეპოებში',
  },
  btc_daily_max_10: {
    stars: 1,
    verdict: 'weak',
    note: 'ამ რეპოებში არ დგას. საჯაროდაც არ არის ის სტრატეგია, რომელსაც უპირატესობას ანიჭებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'არ არის რეპოებში',
  },
  ts_momentum_28_5: {
    stars: 2,
    verdict: 'weak',
    note: 'დროითი მომენტუმი საჯარო კვლევაში ცნობილია. ამ რეპოების ფავორიტი არ არის.',
    pf: null,
    bookReturnPct: null,
    sample: 'საჯარო, არა რეპოს ფავორიტი',
  },
  btc_ema20_ema50_long: {
    stars: 4,
    verdict: 'paper',
    note: 'EMA კვეთა ისაა, რომლითაც ინტერნეტში ტრენდის სისტემას იწყებენ. რეპოებში ცალკე სახელით არ დგას.',
    pf: null,
    bookReturnPct: null,
    sample: 'საჯარო EMA cross',
  },
  market_structure_trend: {
    stars: 2,
    verdict: 'weak',
    note: 'სტრუქტურის გაგრძელება საჯაროდ ხშირია. ამ რეპოებში ცალკე ფავორიტად არ წერენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'საჯარო, არა რეპოს ფავორიტი',
  },
  rsi_pullback: {
    stars: 3,
    verdict: 'weak',
    note: 'RSI უკანდახევა სახელმძღვანელოს სტრატეგიაა. რეპოები მასზე ნაკლებად ჩერდებიან, ვიდრე Cluc-სა და BinHV-ზე.',
    pf: null,
    bookReturnPct: null,
    sample: 'საჯარო RSI',
  },
  bollinger_reversion: {
    stars: 3,
    verdict: 'weak',
    note: 'Bollinger ის ინსტრუმენტია, რაზეც BinHV და Cluc დგას. შიშველ დაბრუნებას იმ კომბინაციაზე დაბლა აყენებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'საჯარო Bollinger',
  },
  ema_rsi_trend: {
    stars: 3,
    verdict: 'weak',
    note: 'EMA და RSI ერთად ყველგან ისწავლება. რეპოების სახელობით ფავორიტი არ არის.',
    pf: null,
    bookReturnPct: null,
    sample: 'საჯარო EMA+RSI',
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
    stars: 3,
    verdict: 'weak',
    note: 'Supertrend საჯაროდ ძალიან კოპირებულია. ეს EMA-სთან შეწყვილება რეპოს გამოქვეყნებული FSupertrend არ არის.',
    pf: null,
    bookReturnPct: null,
    sample: 'საჯარო Supertrend',
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
    stars: 1,
    verdict: 'weak',
    note: 'ამ რეპოებში არ დგას. საჯაროდაც არ არის ის სტრატეგია, რომელსაც უპირატესობას ანიჭებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'არ არის რეპოებში',
  },
  vol_squeeze_structure: {
    stars: 1,
    verdict: 'weak',
    note: 'ამ რეპოებში არ დგას. საჯაროდაც არ არის ის სტრატეგია, რომელსაც უპირატესობას ანიჭებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'არ არის რეპოებში',
  },
  vwap_breakout_volume: {
    stars: 1,
    verdict: 'weak',
    note: 'ამ რეპოებში არ დგას. საჯაროდაც არ არის ის სტრატეგია, რომელსაც უპირატესობას ანიჭებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'არ არის რეპოებში',
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
    stars: 1,
    verdict: 'weak',
    note: 'ამ რეპოებში არ დგას. საჯაროდაც არ არის ის სტრატეგია, რომელსაც უპირატესობას ანიჭებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'არ არის რეპოებში',
  },
  bb20_2_break: {
    stars: 1,
    verdict: 'weak',
    note: 'ამ რეპოებში არ დგას. საჯაროდაც არ არის ის სტრატეგია, რომელსაც უპირატესობას ანიჭებენ.',
    pf: null,
    bookReturnPct: null,
    sample: 'არ არის რეპოებში',
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
