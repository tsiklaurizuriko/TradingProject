import { Component, computed, input } from '@angular/core';
import { dailyPnlSeries, money, signedMoney } from '../../core/trading/trading.models';
import { RiskProfileDto, TradeDto } from '../../core/trading/trading.models';

@Component({
  selector: 'app-pnl-chart',
  template: `
    <section class="panel panel-fill compact pnl-panel">
      <div class="section-head">
        <h2>PnL — Last 7 Days</h2>
        <span class="tiny" [class]="total() >= 0 ? 'pnl-pos' : 'pnl-neg'">{{ signedMoney(total()) }}</span>
      </div>
      <div class="pnl-week">
        @for (row of series(); track row.date) {
          <div class="pnl-day">
            <span class="pnl-amt tiny" [class]="row.pnl > 0 ? 'pnl-pos' : row.pnl < 0 ? 'pnl-neg' : 'muted'">{{ barLabel(row.pnl) }}</span>
            <div class="pnl-track">
              <span
                class="pnl-stick"
                [class.is-pos]="row.pnl > 0"
                [class.is-neg]="row.pnl < 0"
                [class.is-flat]="row.pnl === 0"
                [style.height.px]="stick(row.pnl)"
              ></span>
            </div>
            <span class="tiny">{{ dow(row.date) }}</span>
          </div>
        }
      </div>
    </section>
  `,
})
export class PnlChartComponent {
  readonly trades = input<TradeDto[]>([]);
  readonly days = input<{ date: string; pnL: number }[] | null>(null);
  readonly extraToday = input(0);
  readonly signedMoney = signedMoney;
  readonly series = computed(() => {
    const preset = this.days();
    if (preset?.length) {
      let cumulative = 0;
      return preset.map((row) => {
        const pnl = Number((row as { pnL?: number; pnl?: number }).pnL ?? (row as { pnl?: number }).pnl ?? 0);
        cumulative += pnl;
        return { date: row.date, pnl, cumulative, balance: cumulative };
      });
    }
    return dailyPnlSeries(this.trades(), 7, this.extraToday());
  });
  readonly total = computed(() => this.series().at(-1)?.cumulative ?? 0);

  stick(pnl: number): number {
    const peak = Math.max(1, ...this.series().map((row) => Math.abs(row.pnl)));
    if (pnl === 0) {
      return 6;
    }
    return Math.max(10, Math.round((Math.abs(pnl) / peak) * 132));
  }

  barLabel(pnl: number): string {
    if (!pnl) {
      return '0';
    }
    const abs = Math.abs(pnl);
    const sign = pnl > 0 ? '+' : '-';
    if (abs >= 1000) {
      return `${sign}${(abs / 1000).toFixed(1)}k`;
    }
    return `${sign}${abs.toFixed(abs >= 10 ? 0 : 1)}`;
  }

  dow(date: string): string {
    const day = new Date(`${date}T00:00:00Z`);
    return day.toLocaleDateString('en-US', { weekday: 'short', timeZone: 'UTC' });
  }
}

@Component({
  selector: 'app-allocation-chart',
  template: `
    <section class="panel compact alloc-panel">
      <div class="alloc-head">
        <h2>Account Overview</h2>
        <svg class="alloc-ring" viewBox="0 0 42 42" width="56" height="56" aria-label="Account allocation">
          <circle cx="21" cy="21" r="15.9" fill="none" stroke="#1e2b40" stroke-width="6" />
          <circle cx="21" cy="21" r="15.9" fill="none" stroke="#1677ff" stroke-width="6" [attr.stroke-dasharray]="dash().available" stroke-dashoffset="0" />
          <circle cx="21" cy="21" r="15.9" fill="none" stroke="#ffb020" stroke-width="6" [attr.stroke-dasharray]="dash().used" [attr.stroke-dashoffset]="dash().usedOffset" />
          <circle cx="21" cy="21" r="15.9" fill="none" [attr.stroke]="pnlAbs() >= 0 ? '#00c853' : '#ff3d57'" stroke-width="6" [attr.stroke-dasharray]="dash().pnl" [attr.stroke-dashoffset]="dash().pnlOffset" />
        </svg>
      </div>
      <div class="alloc-legend">
        <div class="alloc-row">
          <span class="alloc-name"><span class="swatch" style="background:#1677ff"></span>Available</span>
          <strong class="num">{{ pct(available()) }}</strong>
          <span class="num alloc-amt">{{ money(availableAbs()) }}</span>
        </div>
        <div class="alloc-row">
          <span class="alloc-name"><span class="swatch" style="background:#ffb020"></span>Used margin</span>
          <strong class="num">{{ pct(used()) }}</strong>
          <span class="num alloc-amt">{{ money(usedAbs()) }}</span>
        </div>
        <div class="alloc-row">
          <span class="alloc-name"><span class="swatch" [style.background]="pnlAbs() >= 0 ? '#00c853' : '#ff3d57'"></span>Unrealized PnL</span>
          <strong class="num" [class]="pnlAbs() >= 0 ? 'pnl-pos' : 'pnl-neg'">{{ pct(pnl()) }}</strong>
          <span class="num alloc-amt" [class]="pnlAbs() >= 0 ? 'pnl-pos' : 'pnl-neg'">{{ money(pnlAbs()) }}</span>
        </div>
      </div>
    </section>
  `,
})
export class AllocationChartComponent {
  readonly availableAbs = input(0);
  readonly usedAbs = input(0);
  readonly pnlAbs = input(0);
  readonly money = money;
  readonly total = computed(() => Math.max(this.availableAbs() + this.usedAbs() + Math.abs(this.pnlAbs()), 1));
  readonly available = computed(() => (this.availableAbs() / this.total()) * 100);
  readonly used = computed(() => (this.usedAbs() / this.total()) * 100);
  readonly pnl = computed(() => (Math.abs(this.pnlAbs()) / this.total()) * 100);
  readonly dash = computed(() => {
    const circ = 100;
    const a = this.available();
    const u = this.used();
    const p = this.pnl();
    return {
      available: `${a} ${circ - a}`,
      used: `${u} ${circ - u}`,
      usedOffset: -a,
      pnl: `${p} ${circ - p}`,
      pnlOffset: -(a + u),
    };
  });
  pct(value: number): string {
    return `${value.toFixed(1)}%`;
  }
}

@Component({
  selector: 'app-goal-progress',
  template: `
    <article class="card metric-card goal-kpi">
      <div class="metric-label">
        <span class="metric-icon">$</span>
        Monthly Goal
      </div>
      <div class="metric-value">{{ goalLabel() }}</div>
      <div class="metric-sub">{{ pct.toFixed(1) }}% this month</div>
      <div class="progress"><span [style.width.%]="Math.min(pct, 100)"></span></div>
    </article>
  `,
})
export class GoalProgressComponent {
  readonly current = input(0);
  readonly target = input(1000);
  readonly deposited = input(0);
  readonly profit = input(0);
  readonly money = money;
  readonly signedMoney = signedMoney;
  readonly Math = Math;
  get pct(): number {
    return this.target() ? (this.current() / this.target()) * 100 : 0;
  }
  goalLabel(): string {
    return `${this.signedMoney(this.current())} / $${this.money(this.target())}`;
  }
}

@Component({
  selector: 'app-risk-overview',
  template: `
    <section class="panel panel-fill compact">
      <div class="section-head"><h2>Risk Overview</h2></div>
      <div class="risk-row">
        <span>R / Trade <strong>{{ risk() ? risk()!.riskPerTradePercent.toFixed(2) + '%' : '—' }}</strong></span>
        <div class="progress"><span [style.width.%]="bar(risk()?.riskPerTradePercent, 2)"></span></div>
      </div>
      <div class="risk-row">
        <span>Margin <strong>{{ risk()?.marginMode || 'Isolated' }}</strong></span>
      </div>
      <div class="risk-row">
        <span>Heat cap <strong>{{ risk() ? (risk()!.maxPortfolioHeatPercent ?? 4).toFixed(1) + '%' : '—' }}</strong></span>
      </div>
      <div class="risk-row">
        <span>Daily Loss <strong>{{ dailyLossLabel() }}</strong></span>
        <div class="progress"><span [style.width.%]="dailyLossBar()"></span></div>
      </div>
      <div class="risk-row">
        <span>Leverage <strong>{{ risk() ? risk()!.maxLeverage + 'x' : '—' }}</strong></span>
      </div>
      <div class="risk-row">
        <span>Open Positions <strong>{{ openPositions() }}{{ risk() ? ' / ' + risk()!.maxOpenPositions : '' }}</strong></span>
        <div class="progress"><span [style.width.%]="bar(openPositions(), risk()?.maxOpenPositions ?? 1)"></span></div>
      </div>
    </section>
  `,
})
export class RiskOverviewComponent {
  readonly risk = input<RiskProfileDto | null>(null);
  readonly equity = input(0);
  readonly todaysPnL = input(0);
  readonly openPositions = input(0);

  bar(value: number | null | undefined, max: number): number {
    if (value === null || value === undefined || max <= 0) {
      return 0;
    }
    return Math.min(100, Math.max(0, (value / max) * 100));
  }

  dailyLossLabel(): string {
    const risk = this.risk();
    const loss = Math.max(0, -this.todaysPnL());
    const cap = risk && this.equity() > 0 ? (risk.maxDailyLossPercent / 100) * this.equity() : null;
    if (!this.equity()) {
      return '—';
    }
    const used = this.equity() ? ((loss / this.equity()) * 100).toFixed(1) : '0.0';
    return cap === null ? `${used}%` : `${used}% / ${risk!.maxDailyLossPercent}%`;
  }

  dailyLossBar(): number {
    const risk = this.risk();
    if (!risk || this.equity() <= 0) {
      return 0;
    }
    const lossPct = (Math.max(0, -this.todaysPnL()) / this.equity()) * 100;
    return this.bar(lossPct, risk.maxDailyLossPercent);
  }
}
