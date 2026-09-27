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
  nearMiss?: boolean;
  paperEnabled?: boolean;
  liveEnabled?: boolean;
  hypothesisId?: string;
  riskProfileId?: string | null;
  stopLossPercent?: number;
  takeProfitPercent?: number;
  riskPerTradePercent?: number;
  maxLeverage?: number;
  maxSimultaneousPositions?: number;
  maxConsecutiveLosses?: number;
  cooldownMinutes?: number;
}

export interface PriceActionCandidateArmDto {
  templateKey: string;
  name: string;
  hypothesisId: string;
  failure: string;
  candidateEnabled: boolean;
  strategyEnabled: boolean;
  strategyId?: string | null;
}

export interface PriceActionArmDto {
  enabled: boolean;
  paperEnabled: boolean;
  liveEnabled: boolean;
  globalLive: boolean;
  candidates: PriceActionCandidateArmDto[];
}

export interface SetPriceActionArmRequest {
  enabled?: boolean;
  paperEnabled?: boolean;
  liveEnabled?: boolean;
  templateKey?: string;
  candidateEnabled?: boolean;
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

export interface StopBotsResult {
  stopped: number;
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

export function hasBotId(id: string | null | undefined): boolean {
  return !!id && id !== '00000000-0000-0000-0000-000000000000';
}

export function isolatedOwners(rows: PositionDto[]): PositionDto[] {
  const byCoin = new Map<string, PositionDto>();
  for (const row of rows) {
    if ((row.quantity ?? 0) <= 0 || !row.symbol) {
      continue;
    }
    const coin = row.symbol.trim().toUpperCase();
    const existing = byCoin.get(coin);
    if (!existing || isolatedOwnerRank(row) < isolatedOwnerRank(existing)) {
      byCoin.set(coin, row);
    }
  }
  return [...byCoin.values()];
}

function isolatedOwnerRank(row: PositionDto): number {
  const empty = hasBotId(row.botId) ? 0 : 1_000_000_000_000;
  const opened = Date.parse(row.openedAt);
  return empty + (Number.isFinite(opened) ? opened : 0);
}

export function resolvePositionStrategy(
  row: PositionDto,
  bots: BotDto[],
): { key: string; name: string } {
  const byId = hasBotId(row.botId)
    ? bots.find((item) => item.id === row.botId)
    : undefined;
  if (byId) {
    const name = (byId.strategyName || '').trim() || 'Unassigned strategy';
    return { key: byId.strategyId || name, name };
  }

  const coin = row.symbol.trim().toUpperCase();
  const running = bots.filter(
    (item) => item.status === 'Running' && item.symbol.trim().toUpperCase() === coin,
  );
  const strategyKeys = new Set(running.map((item) => item.strategyId || item.strategyName || item.id));
  if (strategyKeys.size === 1) {
    const bot = running[0];
    const name = (bot.strategyName || '').trim() || 'Unassigned strategy';
    return { key: bot.strategyId || name, name };
  }

  return { key: 'unassigned', name: 'Unassigned strategy' };
}

export function groupPositionsByStrategy(
  rows: PositionDto[],
  bots: BotDto[],
): { key: string; name: string; rows: PositionDto[]; pnl: number; notional: number }[] {
  const groups = new Map<string, { key: string; name: string; rows: PositionDto[] }>();
  for (const row of isolatedOwners(rows)) {
    const { key, name } = resolvePositionStrategy(row, bots);
    const current = groups.get(key) ?? { key, name, rows: [] };
    current.rows.push(row);
    groups.set(key, current);
  }
  return [...groups.values()]
    .map((group) => ({
      ...group,
      pnl: group.rows.reduce((sum, row) => sum + (row.unrealizedPnL ?? 0), 0),
      notional: group.rows.reduce(
        (sum, row) => sum + (row.notionalUsdt || row.quantity * row.averageEntryPrice),
        0,
      ),
    }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

export function uniqueOpenCoins(rows: PositionDto[]): number {
  const coins = new Set<string>();
  for (const row of rows) {
    if ((row.quantity ?? 0) > 0 && row.symbol) {
      coins.add(row.symbol.trim().toUpperCase());
    }
  }
  return coins.size;
}

export interface StrategyOccupancyRow {
  key: string;
  name: string;
  runningBots: number;
  openCoins: number;
  maxPositions: number;
  plannedRiskUsdt: number;
  plannedRiskPercent: number;
  capPercent: number;
  stopLossPercent: number | null;
  takeProfitPercent: number | null;
  riskPerTradePercent: number | null;
  maxLeverage: number | null;
}

export function strategyOccupancy(
  bots: BotDto[],
  positions: PositionDto[],
  strategies: StrategyDto[],
  available: number,
): StrategyOccupancyRow[] {
  const byId = new Map(strategies.map((row) => [row.id, row]));
  const map = new Map<string, StrategyOccupancyRow>();
  const ensure = (key: string, name: string, strategyId?: string): StrategyOccupancyRow => {
    const existing = map.get(key);
    if (existing) {
      return existing;
    }
    const strategy = (strategyId ? byId.get(strategyId) : undefined) ?? strategies.find((row) => row.name === name);
    const row: StrategyOccupancyRow = {
      key,
      name,
      runningBots: 0,
      openCoins: 0,
      maxPositions: Math.max(1, strategy?.maxSimultaneousPositions ?? 1),
      plannedRiskUsdt: 0,
      plannedRiskPercent: 0,
      capPercent: 4,
      stopLossPercent: strategy?.stopLossPercent ?? null,
      takeProfitPercent: strategy?.takeProfitPercent ?? null,
      riskPerTradePercent: strategy?.riskPerTradePercent ?? null,
      maxLeverage: strategy?.maxLeverage ?? null,
    };
    map.set(key, row);
    return row;
  };

  for (const bot of bots) {
    if (bot.status !== 'Running') {
      continue;
    }
    const key = bot.strategyId || bot.strategyName || bot.id;
    const name = (bot.strategyName || '').trim() || 'Unassigned strategy';
    ensure(key, name, bot.strategyId).runningBots += 1;
  }

  for (const group of groupPositionsByStrategy(
    isolatedOwners(positions.filter((row) => (row.quantity ?? 0) > 0)),
    bots,
  )) {
    const row = ensure(group.key, group.name, group.key);
    row.openCoins = uniqueOpenCoins(group.rows);
    row.plannedRiskUsdt = group.rows.reduce((sum, item) => sum + (item.initialRiskUsdt ?? 0), 0);
    row.plannedRiskPercent = available > 0 ? (row.plannedRiskUsdt / available) * 100 : 0;
  }

  return [...map.values()].sort((a, b) => {
    if (b.openCoins !== a.openCoins) {
      return b.openCoins - a.openCoins;
    }
    return a.name.localeCompare(b.name);
  });
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
  mode?: string;
  kind?: string;
}

export function isProtectionOrder(row: OrderDto): boolean {
  const kind = (row.kind || '').toLowerCase();
  if (kind === 'stop' || kind === 'takeprofit') {
    return true;
  }
  const type = (row.type || '').replace(/[_-\s]/g, '').toUpperCase();
  return type.includes('STOP') || type.includes('TAKEPROFIT');
}

export function orderKindLabel(row: OrderDto): string {
  if (!isProtectionOrder(row)) {
    return 'Market';
  }
  const kind = (row.kind || row.type || '').toLowerCase();
  return kind.includes('take') ? 'Take Profit Market' : 'Stop Market';
}

export function orderStatusLabel(row: OrderDto): string {
  const status = (row.status || '').toUpperCase();
  if (isProtectionOrder(row) && (status === 'SUBMITTED' || status === 'NEW' || status === 'WORKING')) {
    return 'Waiting';
  }
  if (status === 'FILLED' || status.includes('PARTIAL')) {
    return status.includes('PARTIAL') ? 'Partial' : 'Filled';
  }
  if (status === 'CANCELLED' || status === 'CANCELED') {
    return 'Cancelled';
  }
  if (status === 'REJECTED' || status === 'FAILED') {
    return 'Failed';
  }
  return row.status;
}

export function isWorkingOrder(row: OrderDto): boolean {
  const status = (row.status || '').toUpperCase();
  const label = orderStatusLabel(row).toUpperCase();
  if (label === 'WAITING') {
    return true;
  }
  return status === 'SUBMITTED' || status === 'NEW' || status === 'WORKING' || status === 'OPEN' || status === 'PENDING';
}

export function isFillOrder(row: OrderDto): boolean {
  if (isProtectionOrder(row) || isWorkingOrder(row)) {
    return false;
  }
  const client = (row.clientOrderId || '').toUpperCase();
  if (client.startsWith('BNT')) {
    return true;
  }
  const status = (row.status || '').toUpperCase();
  return status.includes('FILL');
}

export function isHistoryOrder(row: OrderDto): boolean {
  return !isWorkingOrder(row) && !(row.clientOrderId || '').toUpperCase().startsWith('BNT');
}

export function tradeHistoryFills(rows: OrderDto[]): OrderDto[] {
  const tagged = rows.filter((row) => (row.clientOrderId || '').toUpperCase().startsWith('BNT'));
  return tagged.length ? tagged : rows.filter(isFillOrder);
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
  side?: string;
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

export interface StrategyResultDto {
  name: string;
  entries: number;
  wins: number;
  losses: number;
  openEntries: number;
  realizedPnL: number;
  unrealizedPnL: number;
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
  monthlyPnL?: number;
  strategyResults?: StrategyResultDto[] | null;
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
  positionBooks?: PositionDto[];
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

export function isLongSide(side: string | null | undefined): boolean {
  const value = (side || '').toUpperCase();
  return value !== 'SELL' && value !== 'SHORT';
}

export function sideLabel(side: string | null | undefined): 'LONG' | 'SHORT' {
  return isLongSide(side) ? 'LONG' : 'SHORT';
}

export function holdDuration(openedAt: string | null | undefined, closedAt: string | null | undefined): string {
  if (!openedAt || !closedAt) {
    return '—';
  }
  const ms = Date.parse(closedAt) - Date.parse(openedAt);
  if (!Number.isFinite(ms) || ms < 0) {
    return '—';
  }
  const minutes = Math.round(ms / 60000);
  if (minutes < 1) {
    return '<1m';
  }
  const days = Math.floor(minutes / (60 * 24));
  const hours = Math.floor((minutes % (60 * 24)) / 60);
  const mins = minutes % 60;
  if (days > 0) {
    return `${days}d ${hours}h ${mins}m`;
  }
  if (hours > 0) {
    return `${hours}h ${mins}m`;
  }
  return `${mins}m`;
}

export function isolatedRoi(pnl: number | null | undefined, margin: number | null | undefined): string | null {
  if (pnl == null || !margin) {
    return null;
  }
  return pct((pnl / margin) * 100);
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

export interface ScalpingCoverageDto {
  coin: string;
  timeframe: string;
  start: string | null;
  end: string | null;
  source: string;
  gaps: number;
  bars: number;
  takerCoverage: number;
  status: string;
  notes: string;
}

export interface ScalpingStrategyStatusDto {
  templateKey: string;
  name: string;
  family: string;
  status: string;
  blurb: string;
  operatorCatalog: boolean;
  enabled: boolean;
}

export interface ScalpingBookDto {
  candidateId: string;
  coin: string;
  timeframe: string;
  phase: string;
  costLabel: string;
  status: string;
  tradeCount: number;
  profitFactor: number | null;
  medianHoldingMinutes: number | null;
  p25HoldingMinutes: number | null;
  p75HoldingMinutes: number | null;
  netPnl: number;
}

export interface ScalpingRejectDto {
  time: string;
  strategyKey: string;
  coin: string;
  reason: string;
}

export interface NewsDeskRow {
  name: string;
  status: string;
}

export interface NewsFeedItem {
  publishedAt: string;
  publisher: string;
  providers: string;
  title: string;
  url: string;
  coin: string | null;
  classification: string | null;
  impact: number | null;
  confidence: number | null;
  evaluated: boolean;
  detail: string;
}

export interface NewsProviderHealthItem {
  provider: string;
  enabled: boolean;
  status: string;
  lastAttemptUtc: string | null;
  lastSuccessUtc: string | null;
  lastError: string | null;
  lastErrorUtc: string | null;
  nextEligibleUtc: string | null;
  fetched: number;
  inserted: number;
  deduplicated: number;
  rejected: number;
}

export interface NewsDeskDto {
  enabled: boolean;
  running: boolean;
  mode: string;
  universeCount: number;
  articles: number;
  events: number;
  latestNews: string | null;
  signal: string | null;
  newsScore: number | null;
  marketScore: number | null;
  finalScore: number | null;
  risk: string | null;
  order: string | null;
  entry: number | null;
  quantity: number | null;
  stopLoss: number | null;
  takeProfit: number | null;
  reason: string | null;
  items?: NewsFeedItem[];
  providers?: NewsProviderHealthItem[];
}

export interface ScalpingResearchSummaryDto {
  confirmation: string;
  liveOff: boolean;
  scalpingLiveOff: boolean;
  isolatedEnforced: boolean;
  riskEngineAuthoritative: boolean;
  validatedForPaperAssigned: boolean;
  lastRunId: string | null;
  strategies: ScalpingStrategyStatusDto[];
  coverage: ScalpingCoverageDto[];
  books: ScalpingBookDto[];
  occupancyRejects: ScalpingRejectDto[];
  sameCoinRejects: number;
  slotRejects: number;
  heatRejects: number;
}

export interface ScalpingResearchRunDto {
  id: string;
  summary: ScalpingResearchSummaryDto;
}

export interface PriceActionSequenceDto {
  coin: string;
  timeframe: string;
  name: string;
  occurrences: number;
  meanFwd1: number;
  meanFwd3: number;
  meanFwd5: number;
  medianMfe: number;
  medianMae: number;
  hitPos50: number;
  hitNeg50: number;
}

export interface PriceActionPatternStatDto {
  coin: string;
  timeframe: string;
  patternType: string;
  status: string;
  occurrences: number;
  meanFwd3: number;
  medianMfe: number;
  medianMae: number;
}

export interface PriceActionPointDto {
  role: string;
  price: number;
  time: string | null;
}

export interface PriceActionBarDto {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface PriceActionOccurrenceDto {
  coin: string;
  timeframe: string;
  patternType: string;
  version: string;
  start: string | null;
  detection: string | null;
  confirmation: string | null;
  entry: string | null;
  neckline: number | null;
  level: number | null;
  direction: string;
  status: string;
  points: PriceActionPointDto[];
  bars: PriceActionBarDto[];
}

export interface PriceActionDataCoverageDto {
  coin: string;
  timeframe: string;
  requestedFrom: string;
  requestedTo: string;
  actualFirstBar: string | null;
  actualLastBar: string | null;
  barCount: number;
  expectedBarCount: number;
  gapCount: number;
  missingBarCount: number;
  duplicateCount: number;
  coveragePercent: number;
  downloadedPages: number;
  cacheHits: number;
  cacheMisses: number;
  continuous: boolean;
  qualityPassed: boolean;
  status: string;
}

export interface PriceActionResearchSummaryDto {
  confirmation: string;
  liveOff: boolean;
  scalpingLiveOff: boolean;
  priceActionLiveOff: boolean;
  isolatedEnforced: boolean;
  riskEngineAuthoritative: boolean;
  validatedForPaperAssigned: boolean;
  cupAndHandle: string;
  lastRunId: string | null;
  strategies: ScalpingStrategyStatusDto[];
  coverage: ScalpingCoverageDto[];
  books: ScalpingBookDto[];
  sequences: PriceActionSequenceDto[];
  patterns: PriceActionPatternStatDto[];
  occurrences: PriceActionOccurrenceDto[];
  hypotheses: string[];
  dataExpansion: PriceActionDataCoverageDto[];
  occupancyRejects: ScalpingRejectDto[];
  sameCoinRejects: number;
  slotRejects: number;
  heatRejects: number;
}

export interface CrossSectionalReversalVariantDto {
  key: string;
  name: string;
  feature: string;
  status: string;
  enabled: boolean;
}

export interface CrossSectionalReversalStatusDto {
  strategyKey: string;
  family: string;
  variants: CrossSectionalReversalVariantDto[];
  status: string;
  enabled: boolean;
  universe: string;
  minimumUniverse: number;
  historyBarsRequired: number;
  rankingClock: string;
  topDecilePercent: number;
  bottomDecilePercent: number;
  validationStatus: string;
  paperStatus: string;
  liveStatus: string;
  notice: string;
  manifestSha256: string;
  rankingVersion: string;
  productionApproval: string;
  liveApproved: string;
  globalLiveTradingEnabled: boolean;
  maxLongPositions: number;
  maxShortPositions: number;
  maxTotalPositions: number;
  maxCrossSectionalRiskPercent: number;
  maxPerPositionRiskPercent: number;
  maxLeverage: number;
}

export interface PatternOverlay {
  neckline?: number | null;
  detection?: string | null;
  confirmation?: string | null;
  entry?: string | null;
  points: PriceActionPointDto[];
}
