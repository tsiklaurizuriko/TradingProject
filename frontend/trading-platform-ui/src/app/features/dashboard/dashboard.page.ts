import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TradingHubService } from '../../core/realtime/trading-hub.service';
import { TradingService } from '../../core/trading/trading.service';
import { BotDto, money, pct, pnlClass, price, signedMoney } from '../../core/trading/trading.models';
import { ToastService } from '../../core/ui/toast.service';
import { UiStateService } from '../../core/ui/ui-state.service';
import { TradingChartComponent } from '../../shared/chart/trading-chart';
import { AllocationChartComponent, GoalProgressComponent, PnlChartComponent, RiskOverviewComponent } from '../../shared/charts/overview-charts';
import { MarketWatchlistComponent } from '../../shared/market/market-watchlist';
import { BotTableComponent } from '../../shared/tables/tables';
import { LedgerBookComponent } from '../../shared/tables/ledger-book';
import { MetricCardComponent, ConfirmModalComponent } from '../../shared/ui/ui-kit';

@Component({
  selector: 'app-dashboard-page',
  imports: [
    MetricCardComponent,
    GoalProgressComponent,
    TradingChartComponent,
    MarketWatchlistComponent,
    BotTableComponent,
    LedgerBookComponent,
    PnlChartComponent,
    AllocationChartComponent,
    RiskOverviewComponent,
    RouterLink,
    ConfirmModalComponent,
  ],
  templateUrl: './dashboard.page.html',
  styleUrl: './dashboard.page.scss',
})
export class DashboardPage {
  readonly trading = inject(TradingService);
  readonly hub = inject(TradingHubService);
  readonly ui = inject(UiStateService);
  private readonly toast = inject(ToastService);
  readonly money = money;
  readonly pct = pct;
  readonly price = price;
  readonly signedMoney = signedMoney;
  readonly pnlClass = pnlClass;
  readonly timeframes = ['1m', '5m', '15m', '1h', '4h', '1d'];
  readonly Math = Math;
  busy: false | 'starting' | 'stopping' = false;
  readonly confirmStartAll = signal(false);

  formatUsd(value: number | null | undefined): string {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return '—';
    }
    return `$${money(value)}`;
  }

  displayName(): string {
    const quote = this.selectedQuote();
    return quote && 'displayName' in quote ? quote.displayName : this.ui.selectedSymbol();
  }

  change24h(): number | null {
    const quote = this.selectedQuote();
    if (quote && 'changePercent24h' in quote && typeof quote.changePercent24h === 'number') {
      return quote.changePercent24h;
    }
    return null;
  }

  readonly selectedQuote = computed(() => {
    const symbol = this.ui.selectedSymbol();
    return (
      this.trading.markets().find((item) => item.symbol === symbol) ??
      this.trading.tickers().find((item) => item.symbol === symbol) ??
      this.trading.overview()?.ticker ??
      null
    );
  });

  readonly usedMargin = computed(() => {
    const equity = this.equity();
    const available = this.available();
    const unrealized = this.unrealized();
    return Math.max(0, equity - available - unrealized);
  });

  readonly isLive = computed(() => this.ui.isLive());

  readonly equity = computed(() => {
    const overview = this.trading.overview();
    const exchange = this.trading.exchange();
    if (this.isLive()) {
      const fromOverview = overview?.liveHasKeys ? overview.liveEquity : null;
      if (fromOverview != null && fromOverview > 0) {
        return fromOverview;
      }
      const usdt =
        (overview?.liveSpotUsdt ?? 0) +
        (overview?.liveFundingUsdt ?? 0) +
        (overview?.liveFuturesUsdt ?? 0);
      if (usdt > 0) {
        return usdt;
      }
      const total = exchange?.totalEquity ?? 0;
      if (total > 0) {
        return total;
      }
      return (exchange?.spotUsdt ?? 0) + (exchange?.fundingUsdt ?? 0) + (exchange?.futuresUsdt ?? 0);
    }
    return overview?.portfolioValue ?? 0;
  });

  readonly available = computed(() => {
    const overview = this.trading.overview();
    if (this.isLive()) {
      return overview?.liveAvailable ?? overview?.liveFuturesUsdt ?? this.trading.exchange()?.futuresUsdt ?? this.trading.exchange()?.usdtFree ?? 0;
    }
    return overview?.availableBalance ?? 0;
  });

  readonly liveWalletSub = computed(() => {
    const overview = this.trading.overview();
    const exchange = this.trading.exchange();
    const spot = this.formatUsd(overview?.liveSpotUsdt ?? exchange?.spotUsdt);
    const futures = this.formatUsd(overview?.liveFuturesUsdt ?? exchange?.futuresUsdt);
    const funding = overview?.liveFundingUsdt ?? exchange?.fundingUsdt ?? 0;
    const fundingPart = funding > 0 ? ` · Funding ${this.formatUsd(funding)}` : '';
    return `Spot ${spot} · Futures ${futures}${fundingPart}`;
  });

  readonly unrealized = computed(() =>
    this.isLive() ? (this.trading.overview()?.liveUnrealizedPnL ?? 0) : (this.trading.overview()?.unrealizedPnL ?? 0),
  );

  readonly todaysPnL = computed(() =>
    this.isLive() ? (this.trading.overview()?.liveTodaysPnL ?? 0) : (this.trading.overview()?.todaysPnL ?? 0),
  );

  readonly modeBots = computed(() => this.trading.workspaceBots());
  readonly modePositions = computed(() => this.trading.workspacePositions());
  readonly modeTrades = computed(() => this.trading.workspaceTrades());
  readonly chartDays = computed(() => {
    const snap = this.trading.performance();
    if (!snap?.days?.length || snap.mode !== this.ui.workspace()) {
      return null;
    }
    return snap.days;
  });
  readonly consecutiveLosses = computed(() => {
    const closed = [...this.modeTrades()]
      .filter((row) => row.closedAt)
      .sort((a, b) => (a.closedAt ?? '').localeCompare(b.closedAt ?? ''))
      .reverse();
    let count = 0;
    for (const row of closed) {
      if ((row.pnL ?? 0) >= 0) {
        break;
      }
      count++;
    }
    return count;
  });
  readonly strategyResults = computed(() => {
    const snap = this.trading.performance();
    if (!snap || snap.mode !== this.ui.workspace() || snap.strategyResults == null) {
      return null;
    }
    return snap.strategyResults;
  });

  readonly riskLocked = computed(() =>
    this.modeBots().some((bot) => {
      const err = (bot.lastError ?? '').toLowerCase();
      return err.includes('risk lock') || err.includes('consecutive loss') || err.includes('entries are locked');
    }),
  );

  readonly monthlyPnL = computed(() => {
    const snap = this.trading.performance();
    if (snap && snap.mode === this.ui.workspace() && snap.monthlyPnL != null) {
      return snap.monthlyPnL;
    }
    const now = new Date();
    const prefix = `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(2, '0')}`;
    const closedThisMonth = this.modeTrades()
      .filter((trade) => trade.closedAt?.startsWith(prefix))
      .reduce((sum, trade) => sum + trade.pnL, 0);
    return closedThisMonth + this.unrealized();
  });

  readonly systems = computed(() => {
    const restOk = !this.trading.error();
    const running = this.modeBots().some((bot) => bot.status === 'Running');
    const riskLocked = this.modeBots().some((bot) => (bot.lastError ?? '').toLowerCase().includes('risk'));
    return [
      { label: this.isLive() ? 'Binance' : 'Market data', ok: restOk, tone: restOk ? 'ok' : 'bad' },
      { label: 'WebSocket', ok: this.hub.connected(), tone: this.hub.connected() ? 'ok' : 'warn' },
      ...(this.isLive() ? [{ label: 'User Stream', ok: true, tone: 'ok' as const }] : []),
      { label: 'Trading Engine', ok: running || restOk, tone: running ? 'ok' : 'warn' },
      { label: 'Risk Manager', ok: !riskLocked, tone: riskLocked ? 'bad' : 'ok' },
      { label: 'Database', ok: this.trading.health()?.status === 'Healthy', tone: this.trading.health()?.status === 'Healthy' ? 'ok' : 'warn' },
    ];
  });

  readonly systemHealth = computed(() => {
    if (this.trading.error()) {
      return { label: 'Offline', cls: 'badge-error', tone: 'bad' as const };
    }
    if (!this.hub.connected()) {
      return { label: 'Degraded', cls: 'badge-paused', tone: 'warn' as const };
    }
    return { label: 'Healthy', cls: 'badge-ok', tone: 'ok' as const };
  });

  constructor() {
    void this.loadChart();
  }

  async loadChart(): Promise<void> {
    await this.trading.loadKlines(this.ui.selectedSymbol(), this.ui.timeframe());
  }

  async selectSymbol(symbol: string): Promise<void> {
    this.ui.setSymbol(symbol);
    await this.loadChart();
  }

  async setTimeframe(tf: string): Promise<void> {
    this.ui.setTimeframe(tf);
    await this.loadChart();
  }

  readonly startAllTitle = computed(() => (this.isLive() ? 'LIVE TRADING WARNING' : 'Start All Bots'));
  readonly startAllMessage = computed(() => {
    if (!this.isLive()) {
      return 'This starts every stopped bot in this PAPER workspace. LIVE bots are not touched.';
    }
    return `LIVE bots only. Paper stays stopped. Isolated still allows only one open LIVE position per coin. Each bot uses the stop, take, size, and leverage saved on its strategy. Available Balance ${money(this.available())}.`;
  });
  readonly startAllWarning = computed(() =>
    this.isLive()
      ? 'Real Binance USD-M Isolated orders can fire as soon as a strategy signals. Planned Risk is not a guaranteed maximum loss.'
      : '',
  );

  requestStartAll(): void {
    if (!this.trading.idleWorkspaceBots().length) {
      this.toast.show('Nothing to start', 'Every bot in this workspace is already running, or none exist yet.', 'info', 'bots');
      return;
    }
    if (this.isLive() || this.ui.confirmStartAll()) {
      this.confirmStartAll.set(true);
      return;
    }
    void this.startAll();
  }

  async startAll(): Promise<void> {
    this.confirmStartAll.set(false);
    this.busy = 'starting';
    try {
      const result = await this.trading.startWorkspaceAll();
      await this.trading.refresh();
      if (result.failed && result.started) {
        this.toast.show(
          'Partial start',
          `${result.started} ${this.ui.workspace()} bot(s) started, ${result.failed} skipped.${result.detail ? ' ' + result.detail : ''}`,
          this.isLive() ? 'error' : 'info',
          'bots',
        );
      } else if (result.failed) {
      this.toast.show('Start blocked', result.detail || 'Could not start these bots. Check keys, risk, and coin assignment.', 'error', 'bots');
      } else {
        this.toast.show(
          `${this.ui.workspace()} bots started`,
          `${result.started} bot(s) are running in ${this.ui.workspace()} only.`,
          this.isLive() ? 'error' : 'success',
        );
      }
    } catch {
      this.toast.show('Start failed', 'Could not start bots.', 'error', 'bots');
    } finally {
      this.busy = false;
    }
  }

  async stopAll(): Promise<void> {
    this.busy = 'stopping';
    try {
      const result = await this.trading.stopWorkspaceAll();
      await this.trading.refresh();
      if (result.failed && result.stopped) {
        this.toast.show(
          'Partial stop',
          `${result.stopped} bot(s) stopped, ${result.failed} skipped.${result.detail ? ' ' + result.detail : ''}`,
          'info',
          'bots',
        );
      } else if (result.failed) {
        this.toast.show('Stop blocked', result.detail || 'Could not stop these bots.', 'error', 'bots');
      } else {
        this.toast.show(
          'Bots stopped',
          `${result.stopped} ${this.ui.workspace()} bot(s) were stopped. Positions were not closed.`,
          'success',
          'bots',
        );
      }
    } catch {
      this.toast.show('Stop failed', 'Could not stop one or more bots.', 'error', 'bots');
    } finally {
      this.busy = false;
    }
  }

  async startBot(bot: BotDto): Promise<void> {
    try {
      await this.trading.startWorkspaceBot(bot);
      await this.trading.refresh();
      this.toast.show(`${this.ui.workspace()} bot started`, `${bot.symbol} is running.`, this.isLive() ? 'error' : 'success');
    } catch (error) {
      this.toast.show('Start blocked', error instanceof Error ? error.message : 'Could not start this bot.', 'error');
    }
  }

  async stopBot(bot: BotDto): Promise<void> {
    try {
      await this.trading.stopWorkspaceBot(bot);
      await this.trading.refresh();
      this.toast.show('Bot stopped', `${bot.symbol} was stopped.`, 'success');
    } catch (error) {
      this.toast.show('Stop blocked', error instanceof Error ? error.message : 'Could not stop this bot.', 'error');
    }
  }
}
