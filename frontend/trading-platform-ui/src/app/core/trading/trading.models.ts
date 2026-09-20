export interface TickerDto {
  symbol: string;
  displayName: string;
  marketCapRank: number;
  price: number;
  timestamp: string;
}

export interface MarketQuoteDto {
  symbol: string;
  displayName: string;
  marketCapRank: number;
  price: number;
  changePercent24h: number;
  quoteVolume: number;
  timestamp: string;
  highPrice24h?: number;
  lowPrice24h?: number;
  trades24h?: number;
  scanScore?: number;
  spreadBps?: number;
  fundingRate?: number;
  volatilityPercent?: number;
  openInterest?: number;
  eligible?: boolean;
  eligibilityReason?: string;
  watchable?: boolean;
  contractType?: string;
}

export interface KlineBarDto {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface RiskProfileDto {
  id: string;
  name: string;
  riskPerTradePercent: number;
  stopLossPercent: number;
  takeProfitPercent: number;
  maxLeverage: number;
  maxDailyLossPercent: number;
  maxPortfolioRiskPercent: number;
  maxSimultaneousPositions: number;
  maxConsecutiveLosses: number;
  cooldownMinutes: number;
  minimumLiquidationSafetyBufferPercent: number;
  isActive: boolean;
  allowLive: boolean;
  isSystem: boolean;
}

export interface SaveRiskProfileRequest {
  riskPerTradePercent: number;
  stopLossPercent: number;
  takeProfitPercent: number;
  maxLeverage: number;
  maxDailyLossPercent: number;
  maxPortfolioRiskPercent: number;
  maxSimultaneousPositions: number;
  maxConsecutiveLosses: number;
  cooldownMinutes: number;
  minimumLiquidationSafetyBufferPercent: number;
  allowLive: boolean;
}

export interface RiskPreviewDto {
  profileName: string;
  availableBalance: number;
  riskPerTradePercent: number;
  riskAmount: number;
  entryPrice: number;
  stopLossPercent: number;
  stopLossPrice: number;
  takeProfitPercent: number;
  takeProfitPrice: number;
  positionNotional: number;
  leverage: number;
  isolatedMargin: number;
  estimatedFee: number;
  estimatedEntryFee?: number;
  estimatedExitFee?: number;
  estimatedSlippage: number;
  estimatedTotalRisk: number;
  liquidationPrice: number;
  portfolioRiskBefore?: number;
  portfolioRiskAfter?: number;
  allowed: boolean;
  reason: string;
}

export interface StrategyDto {
  id: string;
  name: string;
  description: string;
  version: number;
  timeframe: string;
  appliesToAllSymbols: boolean;
  allowedSymbols: string[];
  templateKey: string;
  templateLabel: string;
  allowedSide: string;
  blurb: string;
  emaFast: number;
  emaSlow: number;
  rsiPeriod: number;
  rsiMinimum: number;
  rsiLongMax: number;
  rsiOversold: number;
  rsiOverbought: number;
  macdFast: number;
  macdSlow: number;
  macdSignal: number;
  bbPeriod: number;
  bbStdDev: number;
  donchianLength: number;
  requireVolume: boolean;
  volumeLookback: number;
  minAtrPercent: number;
  maxAtrPercent: number;
  versionUsed: boolean;
  isEnabled: boolean;
  validationStatus: string;
  supportedTimeframes: string[];
  supportedDirections: string[];
  dataDependencies?: string;
  entryLookback?: number;
  exitLookback?: number;
  atrPeriod?: number;
  atrStopMultiplier?: number;
  trendEmaPeriod?: number;
  volumeFilterEnabled?: boolean;
  relativeVolumePeriod?: number;
  minimumRelativeVolume?: number;
  maxVwapDistanceAtr?: number;
  stopAtrMultiplier?: number;
  volatilityLookback?: number;
  compressionPercentile?: number;
  atrExpansionLookback?: number;
  breakoutRelativeVolume?: number;
  supertrendPeriod?: number;
  supertrendMultiplier?: number;
  adxPeriod?: number;
  minimumAdx?: number;
  family?: string;
}

export interface SaveStrategyRequest {
  name: string;
  description: string;
  timeframe: string;
  appliesToAllSymbols: boolean;
  symbols: string[];
  templateKey: string;
  allowedSide: string;
  emaFast: number;
  emaSlow: number;
  rsiPeriod: number;
  rsiMinimum: number;
  rsiLongMax: number;
  rsiOversold: number;
  rsiOverbought: number;
  macdFast: number;
  macdSlow: number;
  macdSignal: number;
  bbPeriod: number;
  bbStdDev: number;
  donchianLength: number;
  requireVolume: boolean;
  volumeLookback: number;
  minAtrPercent: number;
  maxAtrPercent: number;
  entryLookback?: number;
  exitLookback?: number;
  atrPeriod?: number;
  atrStopMultiplier?: number;
  trendEmaPeriod?: number;
  volumeFilterEnabled?: boolean;
  relativeVolumePeriod?: number;
  minimumRelativeVolume?: number;
  maxVwapDistanceAtr?: number;
  stopAtrMultiplier?: number;
  volatilityLookback?: number;
  compressionPercentile?: number;
  atrExpansionLookback?: number;
  breakoutRelativeVolume?: number;
  supertrendPeriod?: number;
  supertrendMultiplier?: number;
  adxPeriod?: number;
  minimumAdx?: number;
}

export interface StrategyPreviewBarDto {
  time: string;
  signal: string;
  close: number;
  reason: string;
}

export interface StrategyPreviewDto {
  strategyId: string;
  name: string;
  templateKey: string;
  symbol: string;
  timeframe: string;
  lastSignal: string;
  lastReason: string;
  bars: StrategyPreviewBarDto[];
}

export interface ExchangeConnectionDto {
  hasKeys: boolean;
  liveReady: boolean;
  canTrade: boolean;
  apiKeyHint: string | null;
  usdtFree: number | null;
  message: string | null;
  spotUsdt?: number;
  fundingUsdt?: number;
  futuresUsdt?: number;
  totalEquity?: number;
}

export interface SystemHealthDto {
  status: string;
  liveTradingEnabled: boolean;
  defaultMode: string;
  entries: Record<string, { status: string; description?: string }>;
}

export interface BotDto {
  id: string;
  name: string;
  status: string;
  mode: string;
  symbol: string;
  displayName: string;
  marketCapRank: number;
  timeframe: string;
  strategyName: string;
  strategyVersion: number;
  riskProfileName: string;
  lastError: string | null;
  startedAt: string | null;
  strategyId?: string;
  riskProfileId?: string;
}

export interface CreateBotsResult {
  created: number;
  skipped: number;
}

export interface StartBotsResult {
  started: number;
  failed: number;
  detail?: string | null;
}

export interface DeleteBotsResult {
  deleted: number;
  skipped: number;
}

export interface RunBacktestRequest {
  strategyId: string | null;
  symbol: string;
  timeframe: string;
  from: string;
  to: string;
  initialCapital: number;
  riskPercent: number;
  leverage: number;
  feesPercent: number;
  slippagePercent: number;
}

export interface BacktestTradeDto {
  openedAt: string;
  closedAt: string;
  quantity: number;
  entryPrice: number;
  exitPrice: number;
  pnL: number;
  fees: number;
  reason: string;
}

export interface BacktestResultDto {
  id: string;
  status: string;
  symbol: string;
  timeframe: string;
  strategyName: string;
  strategyVersion: number;
  from: string;
  to: string;
  initialBalance: number;
  finalBalance: number;
  netProfit: number;
  returnPercent: number;
  numberOfTrades: number;
  winRate: number;
  profitFactor: number;
  averageWin: number;
  averageLoss: number;
  maximumDrawdown: number;
  sharpeRatio: number | null;
  feesPaid: number;
  largestWinningTrade: number;
  largestLosingTrade: number;
  barsUsed: number;
  assumptions: string;
  equity: { time: number; equity: number }[];
  trades: BacktestTradeDto[];
}

export interface PositionDto {
  id: string;
  botId: string;
  symbol: string;
  side: string;
  quantity: number;
  averageEntryPrice: number;
  currentPrice: number;
  unrealizedPnL: number;
  realizedPnL: number;
  fees: number;
  openedAt: string;
  source?: string;
  initialRiskUsdt?: number;
  marginUsdt?: number;
  notionalUsdt?: number;
  leverage?: number;
  stopLossPercent?: number;
  takeProfitPercent?: number;
  stopLossPrice?: number;
  takeProfitPrice?: number;
  liquidationPrice?: number;
  riskPerTradePercent?: number;
}

export interface OrderDto {
  id: string;
  clientOrderId: string;
  botId: string;
  symbol: string;
  side: string;
  type: string;
  price: number | null;
  quantity: number;
  filledQuantity: number;
  status: string;
  createdAt: string;
  exchangeOrderId: string | null;
  source?: string;
  pnL?: number | null;
  fee?: number | null;
}

export interface TradeDto {
  id: string;
  botId: string;
  symbol: string;
  quantity: number;
  entryPrice: number;
  exitPrice: number | null;
  pnL: number;
  pnLPercent: number;
  fees: number;
  openedAt: string;
  closedAt: string | null;
  mode?: string;
}

export interface PerformanceDayDto {
  date: string;
  pnL: number;
  cumulative: number;
}

export interface PerformanceSliceDto {
  name: string;
  closed: number;
  wins: number;
  realized: number;
  winRate: number;
  expectancy: number;
  bots: number;
}

export interface PerformanceDto {
  mode: string;
  closedTrades: number;
  openTrades: number;
  openPositions: number;
  wins: number;
  losses: number;
  weekClosed: number;
  realizedPnL: number;
  unrealizedPnL: number;
  todaysPnL: number;
  feesPaid: number;
  winRate: number;
  expectancy: number;
  profitFactor: number;
  averageWin: number;
  averageLoss: number;
  best: number;
  worst: number;
  maxDrawdown: number;
  returnPercent: number;
  averageHoldHours: number | null;
  winStreak: number;
  lossStreak: number;
  days: PerformanceDayDto[];
  strategies: PerformanceSliceDto[];
  coins: PerformanceSliceDto[];
  recentTrades: TradeDto[];
}

export interface SignalDto {
  id: string;
  botId: string;
  symbol: string;
  signalType: string;
  price: number;
  reason: string;
  timestamp: string;
}

export interface PortfolioDto {
  portfolioValue: number;
  availableBalance: number;
  unrealizedPnL: number;
  realizedPnL: number;
  todaysPnL: number;
  liveTradingEnabled: boolean;
  health: string;
  ticker: TickerDto | null;
  tickers: TickerDto[];
  bots: BotDto[];
  positions: PositionDto[];
  orders: OrderDto[];
  trades: TradeDto[];
  signals: SignalDto[];
  liveHasKeys?: boolean;
  liveCanTrade?: boolean;
  liveEquity?: number;
  liveAvailable?: number;
  liveUnrealizedPnL?: number;
  liveTodaysPnL?: number;
  liveSpotUsdt?: number;
  liveFundingUsdt?: number;
  liveFuturesUsdt?: number;
  liveMessage?: string | null;
}

export type Tone = 'ok' | 'warn' | 'bad' | 'neutral';

export function money(value: number | null | undefined, digits = 2): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }
  return value.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits });
}

export function notionalUsdt(quantity: number | null | undefined, entry: number | null | undefined): number {
  return (quantity ?? 0) * (entry ?? 0);
}

export function qty(value: number | null | undefined): string {
  return money(value, 5);
}

export function price(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }
  const digits = Math.abs(value) >= 100 ? 2 : Math.abs(value) >= 1 ? 4 : 6;
  return value.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits });
}

export function pct(value: number | null | undefined, digits = 2): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }
  const sign = value > 0 ? '+' : '';
  return `${sign}${value.toFixed(digits)}%`;
}

export function rate(value: number | null | undefined, digits = 1): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }
  return `${value.toFixed(digits)}%`;
}

export function holdLabel(hours: number | null | undefined): string {
  if (hours === null || hours === undefined || Number.isNaN(hours)) {
    return '—';
  }
  if (hours < 1) {
    return `${Math.max(1, Math.round(hours * 60))}m`;
  }
  if (hours < 48) {
    return `${hours.toFixed(hours >= 10 ? 0 : 1)}h`;
  }
  return `${(hours / 24).toFixed(1)}d`;
}

export function compact(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }
  const abs = Math.abs(value);
  if (abs >= 1_000_000_000) {
    return `${(value / 1_000_000_000).toFixed(1)}B`;
  }
  if (abs >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(1)}M`;
  }
  if (abs >= 1_000) {
    return `${(value / 1_000).toFixed(1)}K`;
  }
  return money(value, 0);
}

export function pnlClass(value: number | null | undefined): string {
  if (!value) {
    return 'pnl-flat';
  }
  return value > 0 ? 'pnl-pos' : 'pnl-neg';
}

export function signedMoney(value: number | null | undefined, digits = 2): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }
  const sign = value > 0 ? '+' : '';
  return `${sign}${money(value, digits)}`;
}

/** Stored fee is a cost when positive and a rebate when negative. Show as cash (paid = −). */
export function feeCash(value: number | null | undefined): number | null {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return null;
  }
  return -value;
}

export function previewRisk(
  row: Pick<RiskProfileDto, 'riskPerTradePercent' | 'stopLossPercent' | 'takeProfitPercent' | 'maxLeverage'>,
  available: number,
  price = 100_000,
): {
  risk: number;
  notional: number;
  margin: number;
  stopPrice: number;
  takePrice: number;
} {
  const r = Math.max(0, row.riskPerTradePercent ?? 0);
  const sl = Math.max(0, row.stopLossPercent ?? 0);
  const tp = Math.max(0, row.takeProfitPercent ?? 0);
  const leverage = Math.max(1, row.maxLeverage ?? 1);
  const risk = (available * r) / 100;
  const notional = sl > 0 ? risk / (sl / 100) : 0;
  return {
    risk,
    notional,
    margin: notional / leverage,
    stopPrice: price * (1 - sl / 100),
    takePrice: price * (1 + tp / 100),
  };
}

export function formatClock(date: Date, hour12 = false): string {
  return date.toLocaleString('en-US', {
    month: 'short',
    day: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12,
  });
}

export function formatTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString('en-US', {
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  });
}

export function botStatus(status: string): { label: string; cls: string } {
  const key = status.toLowerCase();
  if (key.includes('run')) {
    return { label: 'RUNNING', cls: 'badge-running' };
  }
  if (key.includes('pause')) {
    return { label: 'PAUSED', cls: 'badge-paused' };
  }
  if (key.includes('lock') || key.includes('risk')) {
    return { label: 'RISK LOCKED', cls: 'badge-locked' };
  }
  if (key.includes('error') || key.includes('fail')) {
    return { label: 'ERROR', cls: 'badge-error' };
  }
  return { label: 'STOPPED', cls: 'badge-stopped' };
}

export function modeBadge(mode: string): string {
  const key = mode.toLowerCase();
  if (key.includes('live')) {
    return 'badge-live';
  }
  if (key.includes('test')) {
    return 'badge-testnet';
  }
  return 'badge-paper';
}

export function signalLabel(type: string): 'LONG' | 'SHORT' | 'FLAT' {
  const key = type.toLowerCase();
  if (key.includes('buy') || key.includes('long') || key.includes('entry')) {
    return 'LONG';
  }
  if (key.includes('sell') || key.includes('short') || key.includes('exit')) {
    return 'SHORT';
  }
  return 'FLAT';
}

export function dailyPnlSeries(
  trades: TradeDto[],
  days = 7,
  extraToday = 0,
): { date: string; pnl: number; cumulative: number; balance: number }[] {
  const start = new Date();
  start.setUTCHours(0, 0, 0, 0);
  start.setUTCDate(start.getUTCDate() - (days - 1));
  const buckets = new Map<string, number>();
  for (let i = 0; i < days; i += 1) {
    const day = new Date(start);
    day.setUTCDate(start.getUTCDate() + i);
    buckets.set(day.toISOString().slice(0, 10), 0);
  }
  for (const trade of trades) {
    if (!trade.closedAt) {
      continue;
    }
    const key = new Date(trade.closedAt).toISOString().slice(0, 10);
    if (buckets.has(key)) {
      buckets.set(key, (buckets.get(key) ?? 0) + trade.pnL);
    }
  }
  const today = new Date().toISOString().slice(0, 10);
  if (extraToday && buckets.has(today)) {
    buckets.set(today, (buckets.get(today) ?? 0) + extraToday);
  }
  let cumulative = 0;
  return [...buckets.entries()].map(([date, pnl]) => {
    cumulative += pnl;
    return { date, pnl, cumulative, balance: cumulative };
  });
}
