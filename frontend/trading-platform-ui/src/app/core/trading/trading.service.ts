import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { UiStateService, WorkspaceMode } from '../ui/ui-state.service';
import { byTimeDesc } from '../../shared/lists/list-query';
import {
  BotDto,
  CreateBotsResult,
  DeleteBotsResult,
  StartBotsResult,
  StopBotsResult,
  ExchangeConnectionDto,
  KlineBarDto,
  MarketQuoteDto,
  OrderDto,
  PortfolioDto,
  PositionDto,
  hasBotId,
  RiskPreviewDto,
  RiskProfileDto,
  RunBacktestRequest,
  BacktestResultDto,
  SaveRiskProfileRequest,
  SaveStrategyRequest,
  StrategyDto,
  StrategyPreviewDto,
  SystemHealthDto,
  TradeDto,
  PerformanceDto,
  ScalpingResearchSummaryDto,
  ScalpingCoverageDto,
  ScalpingResearchRunDto,
  PriceActionResearchSummaryDto,
  PriceActionOccurrenceDto,
  CrossSectionalReversalStatusDto,
  PriceActionArmDto,
  SetPriceActionArmRequest,
} from './trading.models';

@Injectable({ providedIn: 'root' })
export class TradingService {
  private readonly http = inject(HttpClient);
  private readonly ui = inject(UiStateService);
  readonly overview = signal<PortfolioDto | null>(null);
  readonly markets = signal<MarketQuoteDto[]>([]);
  readonly klines = signal<KlineBarDto[]>([]);
  readonly risk = signal<RiskProfileDto | null>(null);
  readonly strategies = signal<StrategyDto[]>([]);
  readonly riskProfiles = signal<RiskProfileDto[]>([]);
  readonly health = signal<SystemHealthDto | null>(null);
  readonly exchange = signal<ExchangeConnectionDto | null>(null);
  readonly loading = signal(false);
  readonly chartLoading = signal(false);
  readonly error = signal<string | null>(null);
  readonly restLatencyMs = signal<number | null>(null);
  readonly lastRestAt = signal<Date | null>(null);
  readonly performance = signal<PerformanceDto | null>(null);
  private overviewInFlight = false;
  private overviewPoll: ReturnType<typeof setInterval> | null = null;
  private hubConnected: (() => boolean) | null = null;
  private lastPerfAt = 0;

  readonly tickers = computed(() => this.overview()?.tickers ?? []);
  readonly bots = computed(() => this.overview()?.bots ?? []);
  readonly positions = computed(() => this.overview()?.positions ?? []);
  readonly ledgerOrders = signal<OrderDto[] | null>(null);
  readonly ledgerTrades = signal<TradeDto[] | null>(null);
  readonly trades = computed(() => this.ledgerTrades() ?? this.overview()?.trades ?? []);
  readonly orders = computed(() => this.ledgerOrders() ?? this.overview()?.orders ?? []);
  readonly signals = computed(() => this.overview()?.signals ?? []);
  readonly workspace = computed<WorkspaceMode>(() => this.ui.workspace());
  readonly workspaceBots = computed(() => {
    const live = this.ui.isLive();
    return this.bots().filter((bot) => (bot.mode === 'Live') === live);
  });
  readonly workspaceBotIds = computed(() => new Set(this.workspaceBots().map((bot) => bot.id)));
  readonly runningWorkspaceBots = computed(() =>
    this.workspaceBots().filter((bot) => bot.status === 'Running'),
  );
  readonly idleWorkspaceBots = computed(() =>
    this.workspaceBots().filter((bot) => bot.status !== 'Running'),
  );
  readonly workspacePositions = computed(() => {
    const ids = this.workspaceBotIds();
    const live = this.ui.isLive();
    const books = this.workspacePositionBooks();
    const bookCoins = new Set(books.map((row) => row.symbol.trim().toUpperCase()));
    const rows = this.positions().filter((row) => {
      if ((row.quantity ?? 0) <= 0) {
        return false;
      }
      if (ids.has(row.botId) || (live && row.source === 'Binance')) {
        return true;
      }
      return live && bookCoins.has(row.symbol.trim().toUpperCase());
    });
    if (!live) {
      return [...rows].sort((a, b) => byTimeDesc(a.openedAt, b.openedAt));
    }
    const byCoin = new Map<string, PositionDto>();
    const preferOwner = (a: PositionDto, b: PositionDto): PositionDto => {
      const aBot = hasBotId(a.botId) ? 0 : 1;
      const bBot = hasBotId(b.botId) ? 0 : 1;
      if (aBot !== bBot) {
        return aBot < bBot ? a : b;
      }
      return byTimeDesc(a.openedAt, b.openedAt) >= 0 ? a : b;
    };
    for (const row of rows) {
      const key = row.symbol.trim().toUpperCase();
      const existing = byCoin.get(key);
      if (!existing) {
        byCoin.set(key, row);
        continue;
      }
      const filled = row.source === 'Binance' ? row : existing.source === 'Binance' ? existing : row;
      const snapshot = preferOwner(existing, row);
      const entry = filled.averageEntryPrice || snapshot.averageEntryPrice;
      byCoin.set(key, {
        ...snapshot,
        quantity: filled.quantity,
        averageEntryPrice: entry,
        currentPrice: filled.currentPrice || snapshot.currentPrice,
        unrealizedPnL: filled.unrealizedPnL,
        notionalUsdt: filled.quantity * entry,
      });
    }
    const running = new Set(this.runningWorkspaceBots().map((bot) => bot.id));
    for (const [key, row] of byCoin) {
      if (ids.has(row.botId)) {
        continue;
      }
      const owned = books.filter((book) => book.symbol.trim().toUpperCase() === key);
      const owner = owned.find((book) => running.has(book.botId)) ?? owned[0];
      if (!owner) {
        continue;
      }
      byCoin.set(key, {
        ...owner,
        currentPrice: row.currentPrice || owner.currentPrice,
        unrealizedPnL: row.unrealizedPnL,
        quantity: row.quantity || owner.quantity,
      });
    }
    return [...byCoin.values()].sort((a, b) => byTimeDesc(a.openedAt, b.openedAt));
  });
  readonly workspacePositionBooks = computed(() => {
    const ids = this.workspaceBotIds();
    const books = this.overview()?.positionBooks;
    const source = books && books.length > 0 ? books : this.positions();
    return source.filter((row) => (row.quantity ?? 0) > 0 && ids.has(row.botId));
  });
  readonly workspaceTrades = computed(() => {
    const ids = this.workspaceBotIds();
    const live = this.ui.isLive();
    const snap = this.performance();
    const extra =
      snap && snap.mode === this.ui.workspace()
        ? snap.recentTrades.filter((row) => !this.trades().some((existing) => existing.id === row.id))
        : [];
    const merged = extra.length ? [...this.trades(), ...extra] : this.trades();
    return merged
      .filter((row) => {
        if (row.mode) {
          return (row.mode === 'Live') === live;
        }
        return ids.has(row.botId);
      })
      .sort((a, b) => byTimeDesc(a.closedAt ?? a.openedAt, b.closedAt ?? b.openedAt));
  });
  readonly workspaceOrders = computed(() => {
    const ids = this.workspaceBotIds();
    const live = this.ui.isLive();
    return this.orders()
      .filter((row) => {
        if (row.mode) {
          return (row.mode === 'Live') === live;
        }
        return ids.has(row.botId) || (live && row.source === 'Binance');
      })
      .sort((a, b) => byTimeDesc(a.createdAt, b.createdAt));
  });
  readonly workspaceSignals = computed(() => {
    const ids = this.workspaceBotIds();
    return this.signals()
      .filter((row) => ids.has(row.botId))
      .sort((a, b) => byTimeDesc(a.timestamp, b.timestamp));
  });

  belongsToWorkspace(bot: BotDto): boolean {
    return (bot.mode === 'Live') === this.ui.isLive();
  }

  workspaceMismatchMessage(bot: BotDto): string {
    return `This bot is ${bot.mode}. Switch the header to ${bot.mode.toUpperCase()} first.`;
  }

  startWorkspaceSymbol(symbol: string, strategyId?: string, riskProfileId?: string): Promise<BotDto> {
    return this.startSymbol(symbol, this.ui.workspace(), strategyId, riskProfileId);
  }

  createWorkspaceBots(strategyId: string, riskProfileId: string, symbols: string[]): Promise<CreateBotsResult> {
    return firstValueFrom(
      this.http.post<CreateBotsResult>(`${environment.apiBaseUrl}/trading/bots/create`, {
        mode: this.ui.workspace(),
        strategyId,
        riskProfileId,
        symbols,
      }),
    );
  }

  startWorkspaceBot(bot: BotDto): Promise<BotDto> {
    if (!this.belongsToWorkspace(bot)) {
      return Promise.reject(new Error(this.workspaceMismatchMessage(bot)));
    }
    return this.startBot(bot.id);
  }

  stopWorkspaceBot(bot: BotDto): Promise<BotDto> {
    if (!this.belongsToWorkspace(bot)) {
      return Promise.reject(new Error(this.workspaceMismatchMessage(bot)));
    }
    return this.stopBot(bot.id);
  }

  deleteWorkspaceBots(bots: BotDto[]): Promise<DeleteBotsResult> {
    const ids = bots.filter((bot) => this.belongsToWorkspace(bot)).map((bot) => bot.id);
    if (!ids.length) {
      return Promise.reject(new Error('Switch the header to this bot’s workspace first.'));
    }
    return firstValueFrom(
      this.http.post<DeleteBotsResult>(`${environment.apiBaseUrl}/trading/bots/delete`, {
        mode: this.ui.workspace(),
        ids,
      }),
    );
  }

  deleteWorkspaceBot(bot: BotDto): Promise<DeleteBotsResult> {
    return this.deleteWorkspaceBots([bot]);
  }

  stopWorkspaceAll(): Promise<StopBotsResult> {
    return firstValueFrom(
      this.http.post<StopBotsResult>(`${environment.apiBaseUrl}/trading/bots/stop-all`, {
        mode: this.ui.workspace(),
      }),
    );
  }

  startWorkspaceAll(): Promise<StartBotsResult> {
    return firstValueFrom(
      this.http.post<StartBotsResult>(`${environment.apiBaseUrl}/trading/bots/start-all`, {
        mode: this.ui.workspace(),
      }),
    );
  }

  async refresh(quiet = false): Promise<void> {
    if (this.overviewInFlight) {
      return;
    }
    this.overviewInFlight = true;
    if (!quiet) {
      this.loading.set(true);
    }
    this.error.set(null);
    const started = performance.now();
    try {
      const overview = await firstValueFrom(this.http.get<PortfolioDto>(`${environment.apiBaseUrl}/trading/overview`));
      this.overview.set({ ...overview, tickers: overview.tickers ?? [] });
      this.restLatencyMs.set(Math.round(performance.now() - started));
      this.lastRestAt.set(new Date());
      if (!quiet || !this.performance() || Date.now() - this.lastPerfAt > 60_000) {
        this.lastPerfAt = Date.now();
        void this.refreshPerformance();
      }
    } catch {
      if (!quiet) {
        this.error.set('Binance connection lost. Market data is temporarily unavailable.');
      }
    } finally {
      this.overviewInFlight = false;
      if (!quiet) {
        this.loading.set(false);
      }
    }
  }

  startOverviewPoll(isHubConnected?: () => boolean, periodMs = 12_000): void {
    this.stopOverviewPoll();
    this.hubConnected = isHubConnected ?? null;
    this.overviewPoll = setInterval(() => {
      if (this.hubConnected?.()) {
        const last = this.lastRestAt()?.getTime() ?? 0;
        if (Date.now() - last < 55_000) {
          return;
        }
      }
      void this.refresh(true);
    }, periodMs);
  }

  stopOverviewPoll(): void {
    if (this.overviewPoll !== null) {
      clearInterval(this.overviewPoll);
      this.overviewPoll = null;
    }
  }

  async refreshMarkets(): Promise<void> {
    try {
      const markets = await firstValueFrom(this.http.get<MarketQuoteDto[]>(`${environment.apiBaseUrl}/trading/markets`));
      this.markets.set(markets ?? []);
    } catch {
      this.markets.set([]);
    }
  }

  async refreshHealth(): Promise<void> {
    try {
      this.health.set(await firstValueFrom(this.http.get<SystemHealthDto>(`${environment.apiBaseUrl}/system/health`)));
    } catch {
      this.health.set(null);
    }
  }

  async refreshRisk(): Promise<void> {
    try {
      this.risk.set(await firstValueFrom(this.http.get<RiskProfileDto>(`${environment.apiBaseUrl}/trading/risk-profile`)));
    } catch {
      this.risk.set(null);
    }
  }

  crossSectionalReversal(): Promise<CrossSectionalReversalStatusDto> {
    return firstValueFrom(this.http.get<CrossSectionalReversalStatusDto>(`${environment.apiBaseUrl}/strategies/cross-sectional-reversal`));
  }

  async refreshCatalog(): Promise<void> {
    try {
      const [strategies, profiles] = await Promise.all([
        firstValueFrom(this.http.get<StrategyDto[]>(`${environment.apiBaseUrl}/trading/strategies`)),
        firstValueFrom(this.http.get<RiskProfileDto[]>(`${environment.apiBaseUrl}/trading/risk-profiles`)),
      ]);
      this.strategies.set(strategies ?? []);
      this.riskProfiles.set(profiles ?? []);
      const active = (profiles ?? []).find((row) => row.isActive) ?? profiles?.[0] ?? this.risk();
      if (active) {
        this.risk.set(active);
      }
      this.syncWorkspacePrefs(strategies ?? [], profiles ?? []);
    } catch {
      this.strategies.set([]);
      this.riskProfiles.set([]);
    }
  }

  createStrategy(body: SaveStrategyRequest): Promise<StrategyDto> {
    return firstValueFrom(this.http.post<StrategyDto>(`${environment.apiBaseUrl}/trading/strategies`, body));
  }

  updateStrategy(id: string, body: SaveStrategyRequest): Promise<StrategyDto> {
    return firstValueFrom(this.http.put<StrategyDto>(`${environment.apiBaseUrl}/trading/strategies/${id}`, body));
  }

  priceActionArm(): Promise<PriceActionArmDto> {
    return firstValueFrom(this.http.get<PriceActionArmDto>(`${environment.apiBaseUrl}/trading/price-action/arm`));
  }

  setPriceActionArm(body: SetPriceActionArmRequest): Promise<PriceActionArmDto> {
    return firstValueFrom(this.http.put<PriceActionArmDto>(`${environment.apiBaseUrl}/trading/price-action/arm`, body));
  }

  setStrategyEnabled(id: string, enabled: boolean): Promise<StrategyDto> {
    return firstValueFrom(
      this.http.put<StrategyDto>(`${environment.apiBaseUrl}/trading/strategies/${id}/enabled`, { enabled }),
    );
  }

  previewStrategy(id: string, symbol: string, limit = 80): Promise<StrategyPreviewDto> {
    return firstValueFrom(
      this.http.get<StrategyPreviewDto>(`${environment.apiBaseUrl}/trading/strategies/${id}/preview`, {
        params: { symbol, limit },
      }),
    );
  }

  createRiskProfile(body: SaveRiskProfileRequest): Promise<RiskProfileDto> {
    return firstValueFrom(this.http.post<RiskProfileDto>(`${environment.apiBaseUrl}/trading/risk-profiles`, body));
  }

  updateRiskProfile(id: string, body: SaveRiskProfileRequest): Promise<RiskProfileDto> {
    return firstValueFrom(this.http.put<RiskProfileDto>(`${environment.apiBaseUrl}/trading/risk-profiles/${id}`, body));
  }

  activateRiskProfile(id: string): Promise<RiskProfileDto> {
    return firstValueFrom(this.http.post<RiskProfileDto>(`${environment.apiBaseUrl}/trading/risk-profiles/${id}/activate`, {}));
  }

  fetchRiskPreview(mode: WorkspaceMode, price: number): Promise<RiskPreviewDto> {
    return firstValueFrom(
      this.http.get<RiskPreviewDto>(`${environment.apiBaseUrl}/trading/risk-profiles/preview`, {
        params: { mode, price },
      }),
    );
  }

  async loadKlines(symbol: string, interval: string): Promise<void> {
    this.chartLoading.set(true);
    try {
      const bars = await firstValueFrom(
        this.http.get<KlineBarDto[]>(`${environment.apiBaseUrl}/trading/klines`, {
          params: { symbol, interval, limit: 240 },
        }),
      );
      this.klines.set(bars ?? []);
    } catch {
      this.klines.set([]);
    } finally {
      this.chartLoading.set(false);
    }
  }

  applyOverview(overview: PortfolioDto): void {
    this.overview.set({ ...overview, tickers: overview.tickers ?? [] });
  }

  applyTicker(symbol: string, price: number, timestamp: string): void {
    const current = this.overview();
    if (current) {
      const tickers = [...(current.tickers ?? [])];
      const existing = tickers.find((item) => item.symbol === symbol);
      const next = {
        symbol,
        displayName: existing?.displayName ?? symbol,
        marketCapRank: existing?.marketCapRank ?? 999,
        price,
        timestamp,
      };
      const index = tickers.findIndex((item) => item.symbol === symbol);
      if (index >= 0) {
        tickers[index] = next;
      } else {
        tickers.push(next);
      }
      tickers.sort((a, b) => a.marketCapRank - b.marketCapRank);
      const positions = (current.positions ?? []).map((row) => {
        if (row.quantity <= 0 || row.symbol.trim().toUpperCase() !== symbol.trim().toUpperCase()) {
          return row;
        }
        const long = row.side === 'Long' || row.side === 'Buy' || row.side === 'BUY';
        const unrealized = (long ? price - row.averageEntryPrice : row.averageEntryPrice - price) * row.quantity;
        return { ...row, currentPrice: price, unrealizedPnL: unrealized };
      });
      const unrealizedTotal = uniqueUnrealized(positions);
      this.overview.set({
        ...current,
        ticker: symbol === current.ticker?.symbol ? next : (current.ticker ?? next),
        tickers,
        positions,
        unrealizedPnL: this.ui.isLive() ? current.unrealizedPnL : unrealizedTotal,
        liveUnrealizedPnL: this.ui.isLive() ? unrealizedTotal : current.liveUnrealizedPnL,
        health: 'Healthy',
      });
    }
    this.markets.update((rows) =>
      rows.map((row) => (row.symbol === symbol ? { ...row, price, timestamp } : row)),
    );
  }

  startSamplePaper(): Promise<BotDto[]> {
    return firstValueFrom(this.http.post<BotDto[]>(`${environment.apiBaseUrl}/trading/bots/top-volume-paper/start`, {}));
  }

  startSymbol(symbol: string, mode: 'Paper' | 'Live', strategyId?: string, riskProfileId?: string): Promise<BotDto> {
    return firstValueFrom(
      this.http.post<BotDto>(`${environment.apiBaseUrl}/trading/bots/start-symbol`, {
        symbol,
        mode,
        strategyId: strategyId || null,
        riskProfileId: riskProfileId || null,
      }),
    );
  }

  async exchangeStatus(): Promise<ExchangeConnectionDto> {
    const status = await firstValueFrom(this.http.get<ExchangeConnectionDto>(`${environment.apiBaseUrl}/trading/exchange/status`));
    this.exchange.set(status);
    return status;
  }

  async saveExchangeKeys(apiKey: string, apiSecret: string): Promise<ExchangeConnectionDto> {
    const status = await firstValueFrom(
      this.http.post<ExchangeConnectionDto>(`${environment.apiBaseUrl}/trading/exchange/credentials`, { apiKey, apiSecret }),
    );
    this.exchange.set(status);
    return status;
  }

  startBot(botId: string): Promise<BotDto> {
    return firstValueFrom(this.http.post<BotDto>(`${environment.apiBaseUrl}/trading/bots/${botId}/start`, {}));
  }

  stopBot(botId: string): Promise<BotDto> {
    return firstValueFrom(this.http.post<BotDto>(`${environment.apiBaseUrl}/trading/bots/${botId}/stop`, {}));
  }

  emergencyStop(): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${environment.apiBaseUrl}/trading/emergency-stop`, {}));
  }

  listBots(): Promise<BotDto[]> {
    return firstValueFrom(this.http.get<BotDto[]>(`${environment.apiBaseUrl}/trading/bots`));
  }

  listOrders(): Promise<OrderDto[]> {
    return firstValueFrom(this.http.get<OrderDto[]>(`${environment.apiBaseUrl}/trading/orders`));
  }

  async loadLedgerBook(): Promise<void> {
    try {
      const [orders, trades] = await Promise.all([this.listOrders(), this.listTrades()]);
      this.ledgerOrders.set(orders ?? []);
      this.ledgerTrades.set(trades ?? []);
    } catch {
      /* keep the last full ledger, or overview until the next visit */
    }
  }

  listPositions(): Promise<PositionDto[]> {
    return firstValueFrom(this.http.get<PositionDto[]>(`${environment.apiBaseUrl}/trading/positions`));
  }

  async closePosition(positionId: string): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>(`${environment.apiBaseUrl}/trading/positions/${positionId}/close`, {}));
    } catch (error) {
      throw new Error(readApiMessage(error));
    }
  }

  listTrades(): Promise<TradeDto[]> {
    return firstValueFrom(this.http.get<TradeDto[]>(`${environment.apiBaseUrl}/trading/trades`));
  }

  runBacktest(body: RunBacktestRequest): Promise<BacktestResultDto> {
    return firstValueFrom(this.http.post<BacktestResultDto>(`${environment.apiBaseUrl}/trading/backtests`, body));
  }

  async refreshPerformance(mode: WorkspaceMode = this.ui.workspace()): Promise<void> {
    try {
      const row = await firstValueFrom(
        this.http.get<PerformanceDto>(`${environment.apiBaseUrl}/trading/performance`, { params: { mode } }),
      );
      if (this.ui.workspace() === mode) {
        this.performance.set(row);
      }
    } catch {
      if (this.ui.workspace() === mode) {
        this.performance.set(null);
      }
    }
  }

  scalpingResearch(): Promise<ScalpingResearchSummaryDto> {
    return firstValueFrom(this.http.get<ScalpingResearchSummaryDto>(`${environment.apiBaseUrl}/trading/research/scalping`));
  }

  scalpingCoverage(): Promise<ScalpingCoverageDto[]> {
    return firstValueFrom(this.http.get<ScalpingCoverageDto[]>(`${environment.apiBaseUrl}/trading/research/scalping/coverage`));
  }

  scalpingRun(id: string): Promise<ScalpingResearchRunDto> {
    return firstValueFrom(this.http.get<ScalpingResearchRunDto>(`${environment.apiBaseUrl}/trading/research/scalping/runs/${id}`));
  }

  priceActionResearch(): Promise<PriceActionResearchSummaryDto> {
    return firstValueFrom(this.http.get<PriceActionResearchSummaryDto>(`${environment.apiBaseUrl}/trading/research/scalping/price-action`));
  }

  priceActionOccurrences(): Promise<PriceActionOccurrenceDto[]> {
    return firstValueFrom(this.http.get<PriceActionOccurrenceDto[]>(`${environment.apiBaseUrl}/trading/research/scalping/price-action/occurrences`));
  }

  private syncWorkspacePrefs(strategies: StrategyDto[], profiles: RiskProfileDto[]): void {
    if (!strategies.some((row) => row.id === this.ui.preferredStrategyId())) {
      this.ui.setPreferredStrategyId(strategies.find((row) => row.isEnabled)?.id ?? strategies[0]?.id ?? '');
    }
    if (!profiles.some((row) => row.id === this.ui.preferredRiskId())) {
      this.ui.setPreferredRiskId(profiles.find((row) => row.isActive)?.id ?? profiles[0]?.id ?? '');
    }
  }
}

function uniqueUnrealized(positions: PositionDto[]): number {
  const taken = new Set<string>();
  let sum = 0;
  for (const row of positions) {
    if (row.quantity <= 0) {
      continue;
    }
    const key = row.symbol.trim().toUpperCase();
    if (taken.has(key)) {
      continue;
    }
    taken.add(key);
    sum += row.unrealizedPnL;
  }
  return sum;
}

function readApiMessage(error: unknown): string {
  const http = error as { status?: number; error?: { message?: string } | string };
  if (http.status === 0 || http.status === 502 || http.status === 504) {
    return 'API is not running. Restart the backend, then try again.';
  }
  if (typeof http.error === 'string' && http.error.trim() && !http.error.trim().startsWith('<')) {
    return http.error.trim();
  }
  if (http.error && typeof http.error === 'object' && http.error.message) {
    return http.error.message;
  }
  return error instanceof Error ? error.message : 'Could not close this position.';
}
