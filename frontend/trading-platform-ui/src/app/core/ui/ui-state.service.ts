import { Injectable, computed, signal } from '@angular/core';

export type TradingMode = 'paper' | 'testnet' | 'live';
export type WorkspaceMode = 'Paper' | 'Live';
export type NotifyTopic = 'bots' | 'trades' | 'risk' | 'connection' | 'system';
export interface NotifyPrefs {
  bots: boolean;
  trades: boolean;
  risk: boolean;
  connection: boolean;
}

const GOAL_KEY = 'tp.monthlyIncomeGoal';
const MODE_KEY = 'tp.workspaceMode';
const COLLAPSE_KEY = 'tp.collapsedPanels';
const DEFAULT_MONTHLY_GOAL = 1000;
const WATCH_KEY = 'tp.watchlist';
const SYMBOL_KEY = 'tp.selectedSymbol';
const TF_KEY = 'tp.timeframe';
const COMPACT_KEY = 'tp.compact';
const HOUR12_KEY = 'tp.hour12';
const NOTIFY_KEY = 'tp.notify';
const CONFIRM_START_KEY = 'tp.confirmStartAll';
const STRATEGY_KEY = 'tp.preferredStrategyId';
const RISK_KEY = 'tp.preferredRiskId';

const DEFAULT_NOTIFY: NotifyPrefs = { bots: true, trades: true, risk: true, connection: true };

@Injectable({ providedIn: 'root' })
export class UiStateService {
  readonly mode = signal<TradingMode>('live');
  readonly navOpen = signal(false);
  readonly compact = signal(readFlag(COMPACT_KEY, false));
  readonly hour12 = signal(readFlag(HOUR12_KEY, false));
  readonly now = signal(new Date());
  readonly selectedSymbol = signal(readString(SYMBOL_KEY, 'BTCUSDT'));
  readonly timeframe = signal(readString(TF_KEY, '15m'));
  readonly search = signal('');
  readonly goalTarget = signal(readGoal());
  readonly watchlist = signal<string[]>(readWatchlist());
  readonly collapsedPanels = signal<Record<string, boolean>>(readCollapsed());
  readonly notify = signal<NotifyPrefs>(readNotify());
  readonly confirmStartAll = signal(readFlag(CONFIRM_START_KEY, false));
  readonly preferredStrategyId = signal(readString(STRATEGY_KEY, ''));
  readonly preferredRiskId = signal(readString(RISK_KEY, ''));
  readonly isLive = computed(() => true);
  readonly liveSetup = signal(false);
  readonly showLiveChrome = computed(() => true);
  readonly liveWarning = computed(() => true);
  readonly workspace = computed<WorkspaceMode>(() => 'Live');

  constructor() {
    window.setInterval(() => this.now.set(new Date()), 1000);
  }

  setMode(mode: TradingMode, _liveEnabled: boolean): { accepted: boolean; message: string } {
    if (mode !== 'live') {
      return { accepted: false, message: 'Paper and testnet are not supported. Live is the only trading mode, and order submission stays off until it is enabled.' };
    }
    this.mode.set('live');
    localStorage.setItem(MODE_KEY, 'live');
    this.liveSetup.set(false);
    return { accepted: true, message: 'Live mode. Order submission stays off until Trading:LiveTradingEnabled is true.' };
  }

  beginLiveSetup(): void {
    this.liveSetup.set(true);
  }

  endLiveSetup(): void {
    this.liveSetup.set(false);
  }

  setGoal(value: number): void {
    const n = Number(value);
    const next = Number.isFinite(n) && n > 0 ? n : DEFAULT_MONTHLY_GOAL;
    this.goalTarget.set(next);
    localStorage.setItem(GOAL_KEY, String(next));
  }

  setSymbol(symbol: string): void {
    const next = (symbol || 'BTCUSDT').toUpperCase();
    this.selectedSymbol.set(next);
    localStorage.setItem(SYMBOL_KEY, next);
  }

  setTimeframe(timeframe: string): void {
    const next = timeframe || '15m';
    this.timeframe.set(next);
    localStorage.setItem(TF_KEY, next);
  }

  setCompact(value: boolean): void {
    this.compact.set(!!value);
    localStorage.setItem(COMPACT_KEY, String(!!value));
  }

  setHour12(value: boolean): void {
    this.hour12.set(!!value);
    localStorage.setItem(HOUR12_KEY, String(!!value));
  }

  setConfirmStartAll(value: boolean): void {
    this.confirmStartAll.set(!!value);
    localStorage.setItem(CONFIRM_START_KEY, String(!!value));
  }

  setPreferredStrategyId(id: string): void {
    this.preferredStrategyId.set(id ?? '');
    localStorage.setItem(STRATEGY_KEY, id ?? '');
  }

  setPreferredRiskId(id: string): void {
    this.preferredRiskId.set(id ?? '');
    localStorage.setItem(RISK_KEY, id ?? '');
  }

  patchNotify(partial: Partial<NotifyPrefs>): void {
    const next = { ...this.notify(), ...partial };
    this.notify.set(next);
    localStorage.setItem(NOTIFY_KEY, JSON.stringify(next));
  }

  allowsToast(topic: NotifyTopic): boolean {
    if (topic === 'system') {
      return true;
    }
    return this.notify()[topic];
  }

  resetCollapsed(): void {
    this.collapsedPanels.set({});
    localStorage.removeItem(COLLAPSE_KEY);
  }

  toggleWatch(symbol: string): void {
    const current = this.watchlist();
    const next = current.includes(symbol) ? current.filter((item) => item !== symbol) : [...current, symbol];
    this.watchlist.set(next);
    localStorage.setItem(WATCH_KEY, JSON.stringify(next));
  }

  isCollapsed(id: string): boolean {
    return !!this.collapsedPanels()[id];
  }

  toggleCollapsed(id: string): void {
    const next = { ...this.collapsedPanels(), [id]: !this.collapsedPanels()[id] };
    this.collapsedPanels.set(next);
    localStorage.setItem(COLLAPSE_KEY, JSON.stringify(next));
  }
}

function readMode(): TradingMode {
  return localStorage.getItem(MODE_KEY) === 'live' ? 'live' : 'paper';
}

function readFlag(key: string, fallback: boolean): boolean {
  const raw = localStorage.getItem(key);
  if (raw === 'true') {
    return true;
  }
  if (raw === 'false') {
    return false;
  }
  return fallback;
}

function readString(key: string, fallback: string): string {
  const raw = localStorage.getItem(key);
  return raw && raw.trim() ? raw : fallback;
}

function readNotify(): NotifyPrefs {
  try {
    const parsed = JSON.parse(localStorage.getItem(NOTIFY_KEY) ?? 'null') as Partial<NotifyPrefs> | null;
    if (!parsed || typeof parsed !== 'object') {
      return { ...DEFAULT_NOTIFY };
    }
    return {
      bots: parsed.bots !== false,
      trades: parsed.trades !== false,
      risk: parsed.risk !== false,
      connection: parsed.connection !== false,
    };
  } catch {
    return { ...DEFAULT_NOTIFY };
  }
}

function readGoal(): number {
  const stored = localStorage.getItem(GOAL_KEY);
  if (stored) {
    const raw = Number(stored);
    return Number.isFinite(raw) && raw > 0 ? raw : DEFAULT_MONTHLY_GOAL;
  }
  const legacy = Number(localStorage.getItem('tp.paperMonthlyGoal'));
  if (legacy === 300000 || !Number.isFinite(legacy) || legacy <= 0) {
    localStorage.setItem(GOAL_KEY, String(DEFAULT_MONTHLY_GOAL));
    return DEFAULT_MONTHLY_GOAL;
  }
  return legacy;
}

function readWatchlist(): string[] {
  try {
    const parsed = JSON.parse(localStorage.getItem(WATCH_KEY) ?? '[]') as string[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function readCollapsed(): Record<string, boolean> {
  try {
    const parsed = JSON.parse(localStorage.getItem(COLLAPSE_KEY) ?? '{}') as Record<string, boolean>;
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
}
