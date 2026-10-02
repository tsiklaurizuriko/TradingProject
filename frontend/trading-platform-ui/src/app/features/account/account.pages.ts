import { Component, computed, effect, inject, untracked } from '@angular/core';
import { TradingService } from '../../core/trading/trading.service';
import {
  holdLabel,
  money,
  pct,
  pnlClass,
  rate,
  signedMoney,
  PerformanceDayDto,
  PerformanceSliceDto,
  TradeDto,
} from '../../core/trading/trading.models';
import { UiStateService } from '../../core/ui/ui-state.service';
import { AllocationChartComponent, PnlChartComponent } from '../../shared/charts/overview-charts';
import { ListQuery } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { PositionTableComponent, TradeTableComponent } from '../../shared/tables/tables';
import { MetricCardComponent } from '../../shared/ui/ui-kit';

@Component({
  selector: 'app-performance-page',
  imports: [PnlChartComponent, TradeTableComponent, MetricCardComponent, SortBtnComponent],
  templateUrl: './performance.page.html',
  styleUrl: './performance.page.scss',
})
export class PerformancePage {
  readonly trading = inject(TradingService);
  readonly ui = inject(UiStateService);
  readonly money = money;
  readonly signedMoney = signedMoney;
  readonly pnlClass = pnlClass;
  readonly rate = rate;
  readonly strategyQuery = new ListQuery();
  readonly coinQuery = new ListQuery();
  readonly snap = computed(() => {
    const row = this.trading.performance();
    return row && row.mode === this.ui.workspace() ? row : null;
  });
  readonly closed = computed(() => this.trading.workspaceTrades().filter((t) => t.closedAt));
  readonly isLive = computed(() => this.ui.isLive());
  readonly equity = computed(() => {
    const overview = this.trading.overview();
    const exchange = this.trading.exchange();
    if (this.isLive()) {
      const fromOverview = overview?.liveHasKeys ? overview.liveEquity : null;
      if (fromOverview != null && fromOverview > 0) {
        return fromOverview;
      }
      const usdt = (overview?.liveSpotUsdt ?? 0) + (overview?.liveFundingUsdt ?? 0) + (overview?.liveFuturesUsdt ?? 0);
      if (usdt > 0) {
        return usdt;
      }
      return exchange?.totalEquity ?? (exchange?.spotUsdt ?? 0) + (exchange?.fundingUsdt ?? 0) + (exchange?.futuresUsdt ?? 0);
    }
    return overview?.portfolioValue ?? 0;
  });
  readonly unrealized = computed(() => {
    if (this.snap()) {
      return this.snap()!.unrealizedPnL;
    }
    return this.isLive()
      ? (this.trading.overview()?.liveUnrealizedPnL ?? 0)
      : (this.trading.overview()?.unrealizedPnL ?? 0);
  });
  readonly realized = computed(() =>
    this.snap() ? this.snap()!.realizedPnL : this.closed().reduce((sum, trade) => sum + trade.pnL, 0),
  );
  readonly todaysPnl = computed(() => {
    if (this.snap()) {
      return this.snap()!.todaysPnL;
    }
    return this.isLive() ? (this.trading.overview()?.liveTodaysPnL ?? 0) : (this.trading.overview()?.todaysPnL ?? 0);
  });
  readonly netPnl = computed(() => this.realized() + this.unrealized());
  readonly wins = computed(() => this.snap()?.wins ?? this.closed().filter((t) => t.pnL > 0).length);
  readonly losses = computed(() => this.snap()?.losses ?? this.closed().filter((t) => t.pnL < 0).length);
  readonly closedCount = computed(() => this.snap()?.closedTrades ?? this.closed().length);
  readonly weekClosed = computed(() => {
    if (this.snap()) {
      return this.snap()!.weekClosed;
    }
    const start = Date.now() - 7 * 24 * 60 * 60 * 1000;
    return this.closed().filter((t) => new Date(t.closedAt ?? t.openedAt).getTime() >= start).length;
  });
  readonly openPositions = computed(() => this.snap()?.openPositions ?? this.trading.workspacePositions().length);
  readonly openTrades = computed(() =>
    this.snap()?.openTrades ?? this.trading.workspaceTrades().filter((t) => !t.closedAt).length,
  );
  readonly fees = computed(() => {
    const snap = this.snap();
    if (snap) {
      return snap.feesStatus === 'Known' ? snap.feesPaid : null;
    }
    const trades = this.trading.workspaceTrades();
    if (trades.some((trade) => trade.feeStatus !== 'Known')) {
      return null;
    }
    const assets = new Set(trades.map((trade) => (trade.feeAsset || '').toUpperCase()));
    if (assets.size > 1) {
      return null;
    }
    return trades.reduce((sum, trade) => sum + (trade.fees ?? 0), 0);
  });
  readonly drawdown = computed(() => this.snap()?.maxDrawdown ?? 0);

  constructor() {
    effect(() => {
      const mode = this.ui.workspace();
      untracked(() => void this.trading.refreshPerformance(mode));
    });
  }

  feesLabel(): string {
    const status = this.snap()?.feesStatus;
    if (status === 'Uncertain') {
      return 'Fees uncertain';
    }
    if (status === 'AssetMissing') {
      return 'Fee asset unknown';
    }
    if (status !== 'Known' || this.fees() === null) {
      return 'Fees unknown';
    }
    const asset = this.snap()?.feeAsset;
    return 'Fees ' + money(this.fees()) + (asset ? ' ' + asset : '');
  }

  equitySub(): string {
    const net = this.netPnl();
    const ret = this.snap()?.returnPercent;
    if (this.closedCount() === 0 && !net) {
      return this.ui.workspace() + ' workspace';
    }
    if (ret || ret === 0) {
      return `${signedMoney(net)} · ${pct(ret)}`;
    }
    const equity = this.equity() || 1;
    return `${signedMoney(net)} · ${pct((net / equity) * 100)}`;
  }

  winRate(): string {
    if (!this.closedCount()) {
      return '—';
    }
    return rate(this.snap() ? this.snap()!.winRate : (this.wins() / this.closedCount()) * 100);
  }

  expectancy(): string {
    if (!this.closedCount()) {
      return '—';
    }
    return money(this.snap() ? this.snap()!.expectancy : this.realized() / this.closedCount());
  }

  profitFactor(): string {
    if (!this.closedCount()) {
      return '—';
    }
    if (this.snap()) {
      const pf = this.snap()!.profitFactor;
      return pf >= 999 ? '∞' : pf.toFixed(2);
    }
    const wins = this.closed().filter((t) => t.pnL > 0).reduce((s, t) => s + t.pnL, 0);
    const loss = Math.abs(this.closed().filter((t) => t.pnL < 0).reduce((s, t) => s + t.pnL, 0));
    if (loss === 0) {
      return wins > 0 ? '∞' : '0.00';
    }
    return (wins / loss).toFixed(2);
  }

  avgWin(): string {
    if (this.snap()) {
      return this.snap()!.wins ? money(this.snap()!.averageWin) : '—';
    }
    const rows = this.closed().filter((t) => t.pnL > 0);
    return rows.length ? money(rows.reduce((s, t) => s + t.pnL, 0) / rows.length) : '—';
  }

  avgLoss(): string {
    if (this.snap()) {
      return this.snap()!.losses ? money(this.snap()!.averageLoss) : '—';
    }
    const rows = this.closed().filter((t) => t.pnL < 0);
    return rows.length ? money(rows.reduce((s, t) => s + t.pnL, 0) / rows.length) : '—';
  }

  best(): number {
    return this.snap() ? this.snap()!.best : this.closed().length ? Math.max(...this.closed().map((t) => t.pnL)) : 0;
  }

  worst(): number {
    return this.snap() ? this.snap()!.worst : this.closed().length ? Math.min(...this.closed().map((t) => t.pnL)) : 0;
  }

  winShare(): number {
    const n = this.closedCount();
    return n ? (this.wins() / n) * 100 : 0;
  }

  hold(): string {
    return holdLabel(this.snap()?.averageHoldHours);
  }

  streakSub(): string {
    const snap = this.snap();
    if (snap?.winStreak) {
      return `Win streak ${snap.winStreak}`;
    }
    if (snap?.lossStreak) {
      return `Loss streak ${snap.lossStreak}`;
    }
    return this.openTrades() ? `${this.openTrades()} still open` : 'No streak yet';
  }

  chartDays(): PerformanceDayDto[] | null {
    const days = this.snap()?.days;
    return days?.length ? days : null;
  }

  strategyRows(): PerformanceSliceDto[] {
    const rows = this.snap()?.strategies;
    if (rows?.length) {
      return rows;
    }
    const groups = new Map<string, number>();
    for (const bot of this.trading.workspaceBots()) {
      const name = bot.strategyName || 'Strategy';
      groups.set(name, (groups.get(name) ?? 0) + 1);
    }
    return [...groups.entries()].map(([name, bots]) => ({
      name,
      closed: 0,
      wins: 0,
      realized: 0,
      winRate: 0,
      expectancy: 0,
      bots,
    }));
  }

  readonly visibleStrategyRows = computed(() =>
    this.strategyQuery.apply(
      this.strategyRows(),
      (row) => [row.name],
      {
        name: (row) => row.name,
        bots: (row) => row.bots,
        closed: (row) => row.closed,
        win: (row) => row.winRate,
        pnl: (row) => row.realized,
      },
    ),
  );

  coinRows(): PerformanceSliceDto[] {
    return this.snap()?.coins ?? [];
  }

  readonly visibleCoinRows = computed(() =>
    this.coinQuery.apply(
      this.coinRows(),
      (row) => [row.name],
      {
        name: (row) => row.name,
        closed: (row) => row.closed,
        win: (row) => row.winRate,
        expectancy: (row) => row.expectancy,
        pnl: (row) => row.realized,
      },
    ),
  );

  recentTrades(): TradeDto[] {
    const rows = this.snap()?.recentTrades;
    return rows?.length ? rows : this.trading.workspaceTrades();
  }
}

@Component({
  selector: 'app-portfolio-page',
  imports: [AllocationChartComponent, PositionTableComponent, TradeTableComponent],
  template: `
    <section class="kpi-row cols-3">
      <article class="card">
        <div class="metric-label">{{ trading.workspace() }} equity</div>
        <div class="metric-value">{{ money(equity()) }}</div>
      </article>
      <article class="card">
        <div class="metric-label">Available</div>
        <div class="metric-value">{{ money(available()) }}</div>
      </article>
      <article class="card">
        <div class="metric-label">Unrealized</div>
        <div class="metric-value" [class]="(unrealized() >= 0) ? 'pnl-pos' : 'pnl-neg'">{{ signedMoney(unrealized()) }}</div>
      </article>
    </section>
    <app-allocation-chart [availableAbs]="available()" [usedAbs]="used()" [pnlAbs]="unrealized()" />
    <app-position-table [positions]="trading.workspacePositions()" />
    <app-trade-table [trades]="trading.workspaceTrades()" />
  `,
})
export class PortfolioPage {
  readonly trading = inject(TradingService);
  readonly ui = inject(UiStateService);
  readonly money = money;
  readonly signedMoney = signedMoney;
  readonly equity = computed(() =>
    this.ui.isLive() ? (this.trading.overview()?.liveEquity ?? 0) : (this.trading.overview()?.portfolioValue ?? 0),
  );
  readonly available = computed(() =>
    this.ui.isLive() ? (this.trading.overview()?.liveAvailable ?? 0) : (this.trading.overview()?.availableBalance ?? 0),
  );
  readonly unrealized = computed(() =>
    this.ui.isLive() ? (this.trading.overview()?.liveUnrealizedPnL ?? 0) : (this.trading.overview()?.unrealizedPnL ?? 0),
  );
  used(): number {
    return Math.max(0, this.equity() - this.available() - this.unrealized());
  }
}
