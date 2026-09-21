/** Operator guidance only. None are profitable. None are LIVE. Never 5 stars. */
export type StrategyUseVerdict = 'paper' | 'weak' | 'avoid' | 'blocked';

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
};

const starSlots = [1, 2, 3, 4, 5] as const;

/** Frozen five: 528 coins × 3 TF Model B, equal-book return. Research: 10×3 90d unless noted. */
const ratings: Record<string, StrategyRating> = {
  rsi_pullback: {
    stars: 4,
    verdict: 'paper',
    note: '528 წიგნზე ყველაზე ნაკლები ზიანი. PF მაინც < 1. PAPER-ზე ამით დაიწყე.',
    pf: 0.61471419,
    bookReturnPct: -1.18,
    sample: '528×3 Model B',
  },
  bollinger_reversion: {
    stars: 3,
    verdict: 'paper',
    note: 'მეორე ყველაზე ნაკლებად მავნე frozen. PAPER-ზე შეგიძლია. არ არის მომგებიანი.',
    pf: 0.58519221,
    bookReturnPct: -14.33,
    sample: '528×3 Model B',
  },
  ema_rsi_trend: {
    stars: 2,
    verdict: 'paper',
    note: 'Frozen, გაშვებადი. 528 წიგნზე ≈ −40%. PAPER მხოლოდ თუ RSI/Bollinger არ გინდა.',
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
  market_structure_trend: {
    stars: 2,
    verdict: 'weak',
    note: '10 ქოინზე PF ≈ 1.12, მაგრამ OOS_FAILED და SHORT კლავს. ფულს ნუ ენდობი.',
    pf: 1.12478747,
    bookReturnPct: null,
    sample: '10×3 90d',
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
    stars: 1,
    verdict: 'avoid',
    note: 'OOS_FAILED. არ გაუშვა.',
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
    stars: 1,
    verdict: 'avoid',
    note: 'Phase 3 კარგად ჩანდა; 2 წელზე OOS_FAILED (PF 0.90). არ გაუშვა.',
    pf: 0.89629023,
    bookReturnPct: null,
    sample: 'Phase 4 ~2y',
  },
  funding_extreme_momentum_exhaustion: {
    stars: 1,
    verdict: 'avoid',
    note: 'Phase 3 კარგად ჩანდა; 2 წელზე OOS_FAILED (PF 0.94). არ გაუშვა.',
    pf: 0.93616736,
    bookReturnPct: null,
    sample: 'Phase 4 ~2y',
  },
  funding_price_momentum: {
    stars: 1,
    verdict: 'avoid',
    note: 'Phase 3 PF ≈ 1.00, SHORT კლავს, 3 კვირა. არ გაუშვა.',
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
    note: 'ისტორიულად მორგებული BTC 15m. 476 ტრეიდი / +$138 IS. არ არის validated. PAPER-ზე მხოლოდ დასაკვირვებლად. LIVE არა.',
    pf: null,
    bookReturnPct: 13.82,
    sample: 'BTCUSDT 15m fitted 2y',
  },
  bb20_2_break: {
    stars: 1,
    verdict: 'weak',
    note: 'ისტორიულად მორგებული BTC 15m. 408 ტრეიდი / +$112 IS. არ არის validated. PAPER-ზე მხოლოდ დასაკვირვებლად. LIVE არა.',
    pf: null,
    bookReturnPct: 11.26,
    sample: 'BTCUSDT 15m fitted 2y',
  },
};

const unrated: StrategyRating = {
  stars: 0,
  verdict: 'blocked',
  note: 'რეიტინგი არაა. არ გაუშვა.',
  pf: null,
  bookReturnPct: null,
  sample: 'unrated',
};

export function isOperatorCatalog(templateKey: string | undefined): boolean {
  const verdict = ratingFor(templateKey).verdict;
  return verdict === 'paper' || verdict === 'weak';
}

export function ratingFor(templateKey: string | undefined): StrategyRating {
  if (!templateKey) {
    return unrated;
  }
  return ratings[templateKey] ?? unrated;
}

export function verdictLabel(templateKey: string | undefined): string {
  return verdictLabels[ratingFor(templateKey).verdict];
}

export function ratingSortValue(templateKey: string | undefined): number {
  const rate = ratingFor(templateKey);
  const bucket = rate.verdict === 'paper' ? 400 : rate.verdict === 'weak' ? 300 : rate.verdict === 'avoid' ? 100 : 0;
  return bucket + rate.stars * 10 + (rate.pf ?? -1);
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
  return `${pf}${book} · ${rate.sample} · არ არის LIVE`;
}

export { starSlots };
