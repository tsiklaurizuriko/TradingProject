import { Component, computed, effect, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TradingService } from '../../core/trading/trading.service';
import {
  BacktestResultDto,
  SaveStrategyRequest,
  StrategyDto,
  formatTime,
  money,
  pnlClass,
  signedMoney,
} from '../../core/trading/trading.models';
import { ListQuery, timeValue } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { ToastService } from '../../core/ui/toast.service';

interface StrategyDraft {
  name: string;
  description: string;
  timeframe: string;
  all: boolean;
  symbols: string;
  emaFast: number;
  emaSlow: number;
  rsiPeriod: number;
  rsiMinimum: number;
  stopLossPercent: number;
  takeProfitPercent: number;
}

const timeframes = ['1m', '3m', '5m', '15m', '30m', '1h', '4h', '1d'];

@Component({
  selector: 'app-strategies-page',
  imports: [FormsModule, NgTemplateOutlet, SortBtnComponent],
  template: `
    <header class="page-header">
      <div class="list-sorts">
        <app-sort-btn column="name" [query]="list">Name</app-sort-btn>
        <app-sort-btn column="tf" [query]="list">TF</app-sort-btn>
      </div>
      <button class="btn" type="button" [disabled]="busy" (click)="beginCreate()">Add strategy</button>
    </header>
    @if (creating(); as form) {
      <section class="panel">
        <h2>New strategy</h2>
        <ng-container [ngTemplateOutlet]="editor" [ngTemplateOutletContext]="{ $implicit: form }" />
      </section>
    }
    @for (row of visible(); track row.id) {
      <section class="panel">
        <div class="section-head">
          <div>
            <strong>{{ row.name }}</strong>
            <div class="tiny">Version {{ row.version }} · {{ row.timeframe }}{{ row.versionUsed ? ' · used versions stay immutable' : '' }}</div>
          </div>
          <span class="badge" [class.badge-running]="row.appliesToAllSymbols" [class.badge-paused]="!row.appliesToAllSymbols">
            {{ row.appliesToAllSymbols ? 'All coins' : ((row.allowedSymbols?.length ?? 0) + ' coins') }}
          </span>
        </div>
        @if (editingId() === row.id && draft(); as form) {
          <ng-container [ngTemplateOutlet]="editor" [ngTemplateOutletContext]="{ $implicit: form }" />
        } @else {
          <p class="muted">{{ row.description || 'No description.' }}</p>
          <section class="kpi-row" style="margin:4px 0 12px">
            <article class="card"><div class="metric-label">EMA</div><div class="metric-value">{{ row.emaFast ?? 20 }}/{{ row.emaSlow ?? 50 }}</div></article>
            <article class="card"><div class="metric-label">RSI</div><div class="metric-value">{{ row.rsiPeriod ?? 14 }} &gt; {{ row.rsiMinimum ?? 50 }}</div></article>
            <article class="card"><div class="metric-label">Stop</div><div class="metric-value">{{ row.stopLossPercent ?? 1.5 }}%</div></article>
            <article class="card"><div class="metric-label">Take profit</div><div class="metric-value">{{ row.takeProfitPercent ?? 3 }}%</div></article>
          </section>
          <p class="tiny">{{ row.appliesToAllSymbols ? 'Assigned to every USD-M USDT perpetual.' : 'Assigned to ' + (row.allowedSymbols ?? []).join(', ') }}</p>
          <div class="btn-row" style="margin-top:12px">
            <button class="btn secondary" type="button" [disabled]="busy" (click)="beginEdit(row)">Edit</button>
          </div>
        }
      </section>
    } @empty {
      @if (!creating()) {
        <section class="panel"><p class="empty-state">No strategies yet. Add one to start assigning coins.</p></section>
      }
    }
    <ng-template #editor let-form>
      <div class="form" style="margin-top:12px;max-width:880px">
        <div class="form-grid">
          <label class="field">Name <input [(ngModel)]="form.name" /></label>
          <label class="field">Timeframe
            <select [(ngModel)]="form.timeframe">
              @for (tf of timeframes; track tf) {
                <option [value]="tf">{{ tf }}</option>
              }
            </select>
          </label>
        </div>
        <label class="field">Description <textarea rows="2" [(ngModel)]="form.description"></textarea></label>
        <div class="form-grid cols-3">
          <label class="field">EMA fast <input type="number" [(ngModel)]="form.emaFast" /></label>
          <label class="field">EMA slow <input type="number" [(ngModel)]="form.emaSlow" /></label>
          <label class="field">RSI period <input type="number" [(ngModel)]="form.rsiPeriod" /></label>
          <label class="field">RSI minimum <input type="number" [(ngModel)]="form.rsiMinimum" /></label>
          <label class="field">Stop loss % <input type="number" step="0.1" [(ngModel)]="form.stopLossPercent" /></label>
          <label class="field">Take profit % <input type="number" step="0.1" [(ngModel)]="form.takeProfitPercent" /></label>
        </div>
        <label class="field">
          <span style="display:flex;gap:8px;align-items:center">
            <input type="checkbox" [(ngModel)]="form.all" />
            Apply to every USD-M USDT perpetual
          </span>
        </label>
        @if (!form.all) {
          <label class="field">Assigned coins
            <textarea rows="3" [(ngModel)]="form.symbols" placeholder="BTCUSDT, ETHUSDT, SOLUSDT"></textarea>
          </label>
          <p class="tiny">Comma-separated Binance symbols. This does not start bots.</p>
        }
        <div class="btn-row">
          <button class="btn" type="button" [disabled]="busy" (click)="save()">Save assignment</button>
          <button class="btn secondary" type="button" [disabled]="busy" (click)="cancel()">Cancel</button>
        </div>
      </div>
    </ng-template>
  `,
})
export class StrategiesPage {
  readonly trading = inject(TradingService);
  private readonly toast = inject(ToastService);
  readonly timeframes = timeframes;
  readonly creating = signal<StrategyDraft | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly draft = signal<StrategyDraft | null>(null);
  readonly list = new ListQuery();
  readonly visible = computed(() =>
    this.list.apply(
      this.trading.strategies(),
      (row) => [row.name, row.description, row.timeframe, row.allowedSymbols?.join(' ')],
      {
        name: (row) => row.name,
        tf: (row) => row.timeframe,
      },
    ),
  );
  busy = false;

  constructor() {
    void this.trading.refreshCatalog();
  }

  beginCreate(): void {
    this.editingId.set(null);
    this.draft.set(null);
    this.creating.set(blankStrategy());
  }

  beginEdit(row: StrategyDto): void {
    this.creating.set(null);
    this.editingId.set(row.id);
    this.draft.set(fromStrategy(row));
  }

  cancel(): void {
    this.creating.set(null);
    this.editingId.set(null);
    this.draft.set(null);
  }

  async save(): Promise<void> {
    const form = this.creating() ?? this.draft();
    const id = this.creating() ? null : this.editingId();
    if (!form) {
      return;
    }
    const body = toRequest(form);
    this.busy = true;
    try {
      const saved = id
        ? await this.trading.updateStrategy(id, body)
        : await this.trading.createStrategy(body);
      await this.trading.refreshCatalog();
      this.cancel();
      this.toast.show(
        id ? 'Strategy saved' : 'Strategy added',
        saved.versionUsed
          ? `Saved as version ${saved.version}. Running bots keep the previous version until you stop and start them.`
          : (form.all ? 'Can run on every USDT-M coin.' : `Assigned to ${body.symbols.length} coin(s).`),
        'success',
      );
    } catch {
      this.toast.show('Save blocked', 'Check the name, EMA/RSI values, and coin assignment.', 'error');
    } finally {
      this.busy = false;
    }
  }
}

function blankStrategy(): StrategyDraft {
  return {
    name: '',
    description: '',
    timeframe: '5m',
    all: true,
    symbols: '',
    emaFast: 20,
    emaSlow: 50,
    rsiPeriod: 14,
    rsiMinimum: 50,
    stopLossPercent: 1.5,
    takeProfitPercent: 3,
  };
}

function fromStrategy(row: StrategyDto): StrategyDraft {
  return {
    name: row.name,
    description: row.description ?? '',
    timeframe: row.timeframe || '5m',
    all: row.appliesToAllSymbols,
    symbols: (row.allowedSymbols ?? []).join(', '),
    emaFast: row.emaFast ?? 20,
    emaSlow: row.emaSlow ?? 50,
    rsiPeriod: row.rsiPeriod ?? 14,
    rsiMinimum: row.rsiMinimum ?? 50,
    stopLossPercent: row.stopLossPercent ?? 1.5,
    takeProfitPercent: row.takeProfitPercent ?? 3,
  };
}

function parseSymbols(value: string): string[] {
  return value.split(/[\s,]+/).map((item) => item.trim().toUpperCase()).filter(Boolean);
}

function toRequest(form: StrategyDraft): SaveStrategyRequest {
  return {
    name: form.name.trim(),
    description: form.description.trim(),
    timeframe: form.timeframe,
    appliesToAllSymbols: form.all,
    symbols: parseSymbols(form.symbols),
    emaFast: Number(form.emaFast),
    emaSlow: Number(form.emaSlow),
    rsiPeriod: Number(form.rsiPeriod),
    rsiMinimum: Number(form.rsiMinimum),
    stopLossPercent: Number(form.stopLossPercent),
    takeProfitPercent: Number(form.takeProfitPercent),
  };
}

const TopMarketCapCoins = [
  { symbol: 'BTCUSDT', displayName: 'Bitcoin' },
  { symbol: 'ETHUSDT', displayName: 'Ethereum' },
  { symbol: 'BNBUSDT', displayName: 'BNB' },
  { symbol: 'SOLUSDT', displayName: 'Solana' },
  { symbol: 'XRPUSDT', displayName: 'XRP' },
  { symbol: 'DOGEUSDT', displayName: 'Dogecoin' },
  { symbol: 'SUIUSDT', displayName: 'Sui' },
  { symbol: 'ADAUSDT', displayName: 'Cardano' },
  { symbol: 'LINKUSDT', displayName: 'Chainlink' },
  { symbol: 'AVAXUSDT', displayName: 'Avalanche' },
  { symbol: 'TRXUSDT', displayName: 'TRON' },
  { symbol: 'TONUSDT', displayName: 'Toncoin' },
  { symbol: 'DOTUSDT', displayName: 'Polkadot' },
  { symbol: 'LTCUSDT', displayName: 'Litecoin' },
  { symbol: 'BCHUSDT', displayName: 'Bitcoin Cash' },
] as const;

@Component({
  selector: 'app-backtesting-page',
  imports: [FormsModule, SortBtnComponent],
  template: `
    <section class="split-2">
      <aside class="panel">
        <h2>Configuration</h2>
        <div class="form" style="max-width:none">
          <label class="field">Coin
            <select [(ngModel)]="symbol">
              @for (row of coins(); track row.symbol) {
                <option [value]="row.symbol">{{ row.symbol }} · {{ row.displayName }}</option>
              }
            </select>
          </label>
          <label class="field">Timeframe
            <select [(ngModel)]="timeframe">
              <option value="5m">5m</option>
              <option value="15m">15m</option>
              <option value="1h">1h</option>
              <option value="1d">1d</option>
            </select>
          </label>
          <label class="field">Strategy
            <select [(ngModel)]="strategyId">
              @for (row of trading.strategies(); track row.id) {
                <option [value]="row.id">{{ row.name }} · v{{ row.version }}</option>
              }
            </select>
          </label>
          <label class="field">From <input type="date" [(ngModel)]="from" /></label>
          <label class="field">To <input type="date" [(ngModel)]="to" /></label>
          <label class="field">Initial capital <input type="number" [(ngModel)]="capital" /></label>
          <label class="field">Risk % <input type="number" step="0.1" [(ngModel)]="risk" /></label>
          <label class="field">Leverage <input type="number" [(ngModel)]="leverage" /></label>
          <label class="field">Fees % <input type="number" step="0.01" [(ngModel)]="fees" /></label>
          <label class="field">Slippage % <input type="number" step="0.01" [(ngModel)]="slippage" /></label>
          <button class="btn accent" type="button" [disabled]="busy() || !strategyId" (click)="run()">
            {{ busy() ? 'Running…' : 'Run backtest' }}
          </button>
        </div>
      </aside>
      <section class="panel">
        <h2>Results</h2>
        @if (result(); as row) {
          <p class="tiny" style="margin:0 0 12px">{{ row.strategyName }} v{{ row.strategyVersion }} · {{ row.symbol }} · {{ row.timeframe }} · {{ row.barsUsed }} bars</p>
          <div class="kpi-row">
            <article class="card"><div class="metric-label">Net Profit</div><div class="metric-value" [class]="pnlCls(row.netProfit)">{{ signed(row.netProfit) }}</div></article>
            <article class="card"><div class="metric-label">Return</div><div class="metric-value" [class]="pnlCls(row.returnPercent)">{{ pct(row.returnPercent) }}</div></article>
            <article class="card"><div class="metric-label">Win Rate</div><div class="metric-value">{{ pct(row.winRate) }}</div></article>
            <article class="card"><div class="metric-label">Max Drawdown</div><div class="metric-value" [class]="row.maximumDrawdown ? 'pnl-neg' : ''">{{ pct(-Math.abs(row.maximumDrawdown)) }}</div></article>
          </div>
          <div class="kpi-row" style="margin-top:12px">
            <article class="card"><div class="metric-label">Trades</div><div class="metric-value">{{ row.numberOfTrades }}</div></article>
            <article class="card"><div class="metric-label">Profit factor</div><div class="metric-value">{{ money(row.profitFactor, 2) }}</div></article>
            <article class="card"><div class="metric-label">Fees</div><div class="metric-value">{{ money(row.feesPaid) }}</div></article>
            <article class="card"><div class="metric-label">Final equity</div><div class="metric-value">{{ money(row.finalBalance) }}</div></article>
          </div>
          @if (row.trades.length === 0) {
            <p class="empty-state">No trades in this window. The engine ran, but entry conditions were not met.</p>
          } @else {
            <div class="table-scroll" style="max-height:360px;margin-top:16px">
              <table class="data-table">
                <thead>
                  <tr>
                    <th><app-sort-btn column="opened" [query]="tradeQuery">Opened</app-sort-btn></th>
                    <th><app-sort-btn column="closed" [query]="tradeQuery">Closed</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="qty" [query]="tradeQuery" align="end">Qty</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="entry" [query]="tradeQuery" align="end">Entry</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="exit" [query]="tradeQuery" align="end">Exit</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="pnl" [query]="tradeQuery" align="end">PnL</app-sort-btn></th>
                    <th><app-sort-btn column="reason" [query]="tradeQuery">Reason</app-sort-btn></th>
                  </tr>
                </thead>
                <tbody>
                  @for (trade of visibleTrades(); track $index) {
                    <tr>
                      <td>{{ formatTime(trade.openedAt) }}</td>
                      <td>{{ formatTime(trade.closedAt) }}</td>
                      <td class="num">{{ money(trade.quantity, 4) }}</td>
                      <td class="num">{{ money(trade.entryPrice, 4) }}</td>
                      <td class="num">{{ money(trade.exitPrice, 4) }}</td>
                      <td class="num" [class]="pnlCls(trade.pnL)">{{ signed(trade.pnL) }}</td>
                      <td>{{ trade.reason }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        } @else {
          <p class="empty-state">Run a backtest to see net profit, win rate, drawdown, and fills from Binance history.</p>
        }
      </section>
    </section>
  `,
})
export class BacktestingPage {
  readonly trading = inject(TradingService);
  private readonly toast = inject(ToastService);
  readonly money = money;
  readonly formatTime = formatTime;
  readonly Math = Math;
  symbol = 'BTCUSDT';
  timeframe = '1h';
  strategyId = '';
  from = dateInput(daysAgo(90));
  to = dateInput(new Date());
  capital = 10000;
  risk = 1;
  leverage = 1;
  fees = 0.04;
  slippage = 0.02;
  readonly busy = signal(false);
  readonly result = signal<BacktestResultDto | null>(null);
  readonly tradeQuery = new ListQuery();
  readonly visibleTrades = computed(() =>
    this.tradeQuery.apply(
      this.result()?.trades ?? [],
      (trade) => [trade.reason, trade.pnL],
      {
        opened: (trade) => timeValue(trade.openedAt),
        closed: (trade) => timeValue(trade.closedAt),
        qty: (trade) => trade.quantity,
        entry: (trade) => trade.entryPrice,
        exit: (trade) => trade.exitPrice,
        pnl: (trade) => trade.pnL,
        reason: (trade) => trade.reason,
      },
    ),
  );
  readonly coins = computed(() => {
    const rows = this.trading.markets();
    if (rows.length >= 15) {
      return rows.slice(0, 15).map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
    }
    return TopMarketCapCoins;
  });

  constructor() {
    effect(() => {
      const first = this.trading.strategies()[0];
      if (first && !this.strategyId) {
        this.strategyId = first.id;
      }
    });
  }

  signed(value: number): string {
    return signedMoney(value);
  }

  pct(value: number): string {
    return `${value >= 0 ? '' : ''}${value.toFixed(2)}%`;
  }

  pnlCls(value: number): string {
    return pnlClass(value);
  }

  async run(): Promise<void> {
    const symbol = this.symbol.trim().toUpperCase();
    if (!this.strategyId) {
      this.toast.show('Pick a strategy', 'Save a strategy first, then run the replay.', 'error');
      return;
    }
    this.busy.set(true);
    try {
      const row = await this.trading.runBacktest({
        strategyId: this.strategyId,
        symbol,
        timeframe: this.timeframe,
        from: `${this.from}T00:00:00.000Z`,
        to: `${this.to}T23:59:59.000Z`,
        initialCapital: Number(this.capital),
        riskPercent: Number(this.risk),
        leverage: Number(this.leverage),
        feesPercent: Number(this.fees),
        slippagePercent: Number(this.slippage),
      });
      this.result.set(row);
      this.toast.show(
        'Backtest finished',
        `${row.numberOfTrades} trades · ${signedMoney(row.netProfit)} · ${row.barsUsed} bars`,
        'success',
      );
    } catch (error) {
      this.toast.show('Backtest blocked', apiMessage(error), 'error');
    } finally {
      this.busy.set(false);
    }
  }
}

function daysAgo(days: number): Date {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() - days);
  return date;
}

function dateInput(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function apiMessage(error: unknown): string {
  const http = error as { status?: number; message?: string; error?: unknown };
  if (http.status === 0 || http.status === 502 || http.status === 504) {
    return 'API is not running on port 5080. Start or restart the backend, then run again.';
  }
  const body = http.error;
  if (typeof body === 'string') {
    const trimmed = body.trim();
    if (trimmed && !trimmed.startsWith('<')) {
      return trimmed;
    }
  }
  if (body && typeof body === 'object') {
    const payload = body as {
      message?: string;
      Message?: string;
      title?: string;
      Title?: string;
      errors?: Record<string, string[] | undefined>;
    };
    const firstError = payload.errors
      ? Object.values(payload.errors).flat().find((row) => !!row)
      : null;
    return payload.message || payload.Message || firstError || payload.title || payload.Title || 'Could not run this backtest.';
  }
  return http.message || 'Could not run this backtest.';
}
