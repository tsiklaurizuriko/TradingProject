import { Component, computed, effect, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TradingService } from '../../core/trading/trading.service';
import {
  BacktestResultDto,
  SaveStrategyRequest,
  StrategyDto,
  StrategyPreviewDto,
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
}

const timeframes = ['1m', '3m', '5m', '15m', '30m', '1h', '4h', '1d'];
const templateOptions = [
  { key: 'ema_rsi_trend', label: 'EMA RSI Trend' },
  { key: 'macd_trend', label: 'MACD Trend' },
  { key: 'rsi_pullback', label: 'RSI Pullback' },
  { key: 'bollinger_reversion', label: 'Bollinger Reversion' },
  { key: 'donchian_breakout', label: 'Donchian Breakout' },
] as const;
const sideOptions = [
  { key: 'Long', label: 'Long' },
  { key: 'Short', label: 'Short' },
  { key: 'Both', label: 'Both' },
] as const;
const templateLogic: Record<string, string> = {
  ema_rsi_trend:
    'LONG: სწრაფი EMA ნელს ზემოთ კვეთს, ფასი ნელ EMA-ზე მაღალია და RSI min–max შუალედშია (ნაგულისხმევი 50–68). SHORT: პირიქით. გამოსვლა: საპირისპირო EMA გადაკვეთა.',
  macd_trend:
    'LONG: MACD სიგნალს ზემოთ კვეთს, ჰისტოგრამა დადებითია და ფასი ნელ EMA-ზე მაღალია. SHORT: პირიქით. გამოსვლა: საპირისპირო MACD გადაკვეთა.',
  rsi_pullback:
    'LONG მხოლოდ აღმავალ ტრენდში (ფასი ნელ EMA-ზე მაღალია), როცა RSI oversold-ს ქვემოდან კვეთს. SHORT მხოლოდ დაღმავალ ტრენდში overbought-ზე. გამოსვლა: RSI ისევ 50-ს კვეთს.',
  bollinger_reversion:
    'LONG: ფასი ქვედა ბოლინჯერის ზოლში ისევ იხურება და ნელ EMA-ზე მაღალი რჩება. SHORT: სარკისებურად. გამოსვლა: შუა ზოლზე.',
  donchian_breakout:
    'LONG: დახურვა N-სანთლის მაქსიმუმს არღვევს. SHORT: დახურვა N-სანთლის მინიმუმს არღვევს. გამოსვლა: საპირისპირო ზოლზე.',
};

@Component({
  selector: 'app-strategies-page',
  imports: [FormsModule, NgTemplateOutlet, SortBtnComponent],
  styleUrl: './strategies.page.scss',
  template: `
    <div class="strategies-page">
      <header class="strategies-toolbar">
        <div class="strategies-toolbar-copy">
          <div class="list-sorts">
            <app-sort-btn column="name" [query]="list">Name</app-sort-btn>
            <app-sort-btn column="tf" [query]="list">TF</app-sort-btn>
          </div>
          <p class="tiny">Signal only — when to enter or flatten. Size, stop, and take profit stay on Risk.</p>
        </div>
        <div class="strategies-toolbar-actions">
          <label class="field">Coin
            <select [(ngModel)]="previewCoin">
              @for (coin of coins(); track coin.symbol) {
                <option [value]="coin.symbol">{{ coin.symbol }} · {{ coin.displayName }}</option>
              }
            </select>
          </label>
          <button class="btn accent" type="button" [disabled]="busy" (click)="beginCreate()">Add strategy</button>
        </div>
      </header>

      @if (creating(); as form) {
        <section class="panel">
          <div class="section-head">
            <h2>New strategy</h2>
            <button class="btn ghost sm" type="button" (click)="cancel()">Cancel</button>
          </div>
          <ng-container [ngTemplateOutlet]="editor" [ngTemplateOutletContext]="{ $implicit: form }" />
        </section>
      }

      <div class="strategies-grid">
        @for (row of visible(); track row.id) {
          <section class="panel strategy-card" [class.is-editing]="editingId() === row.id">
            <div class="strategy-card-head">
              <div>
                <strong>{{ row.name }}</strong>
                <div class="strategy-pills">
                  <span class="badge badge-stopped">{{ row.timeframe }}</span>
                  <span class="badge" [class.badge-long]="(row.allowedSide || 'Long') === 'Long'" [class.badge-short]="row.allowedSide === 'Short'" [class.badge-paper]="row.allowedSide === 'Both'">{{ row.allowedSide || 'Long' }}</span>
                  <span class="badge" [class.badge-running]="qualityOn(row)" [class.badge-stopped]="!qualityOn(row)">
                    {{ qualityOn(row) ? 'Quality on' : 'Quality off' }}
                  </span>
                  <span class="badge" [class.badge-running]="row.appliesToAllSymbols" [class.badge-paused]="!row.appliesToAllSymbols">
                    {{ row.appliesToAllSymbols ? 'All coins' : ((row.allowedSymbols?.length ?? 0) + ' coins') }}
                  </span>
                  @if (row.versionUsed) {
                    <span class="badge badge-paused">v{{ row.version }} used</span>
                  }
                  <span class="badge" [class.badge-running]="row.isEnabled !== false" [class.badge-stopped]="row.isEnabled === false">
                    {{ row.isEnabled === false ? 'Disabled' : 'Enabled' }}
                  </span>
                  <span class="badge badge-paused">{{ row.validationStatus || 'VALIDATION_PENDING' }}</span>
                </div>
              </div>
              @if (editingId() !== row.id) {
                <div class="strategy-actions">
                  <button class="btn ghost sm" type="button" [disabled]="busy" (click)="beginEdit(row)">Edit</button>
                  <button class="btn ghost sm" type="button" [disabled]="busy" (click)="toggleEnabled(row)">
                    {{ row.isEnabled === false ? 'Enable' : 'Disable' }}
                  </button>
                  <button class="btn accent sm" type="button" [disabled]="previewBusy() === row.id" (click)="preview(row)">
                    {{ previewBusy() === row.id ? 'Preview…' : 'Preview' }}
                  </button>
                </div>
              }
            </div>

            @if (editingId() === row.id && draft(); as form) {
              <ng-container [ngTemplateOutlet]="editor" [ngTemplateOutletContext]="{ $implicit: form }" />
            } @else {
              <p class="strategy-blurb">{{ blurbFor(row.templateKey) }}</p>
              @if (previews()[row.id]; as snap) {
                <div class="strategy-preview">
                  <div class="strategy-preview-head">
                    <span class="tiny">{{ snap.symbol }} · last <span [class]="signalClass(snap.lastSignal)">{{ snap.lastSignal }}</span></span>
                    <span class="tiny">{{ snap.lastReason }}</span>
                  </div>
                  <div class="table-scroll" style="max-height:220px">
                    <table class="data-table">
                      <thead>
                        <tr>
                          <th>Time</th>
                          <th>Signal</th>
                          <th class="num">Close</th>
                          <th>Reason</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (bar of snap.bars; track bar.time) {
                          <tr>
                            <td>{{ formatTime(bar.time) }}</td>
                            <td [class]="signalClass(bar.signal)">{{ bar.signal }}</td>
                            <td class="num">{{ money(bar.close, 4) }}</td>
                            <td>{{ bar.reason }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                </div>
              }
            }
          </section>
        } @empty {
          @if (!creating()) {
            <section class="panel">
              <div class="empty-state">
                <strong>No strategies yet</strong>
                <p>Add one to assign coins. This does not start bots.</p>
              </div>
            </section>
          }
        }
      </div>
    </div>

    <ng-template #editor let-form>
      <div class="form strategy-editor">
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
        <div class="form-grid">
          <label class="field">Template
            <select [(ngModel)]="form.templateKey">
              @for (row of templates; track row.key) {
                <option [value]="row.key">{{ row.label }}</option>
              }
            </select>
          </label>
          <label class="field">Allowed side
            <select [(ngModel)]="form.allowedSide">
              @for (row of sides; track row.key) {
                <option [value]="row.key">{{ row.label }}</option>
              }
            </select>
          </label>
        </div>
        <p class="strategy-hint">{{ blurbFor(form.templateKey) }}</p>
        <label class="field">Note <textarea rows="2" [(ngModel)]="form.description"></textarea></label>
        @if (form.templateKey === 'ema_rsi_trend') {
          <div class="form-grid cols-3">
            <label class="field">EMA fast <input type="number" [(ngModel)]="form.emaFast" /></label>
            <label class="field">EMA slow <input type="number" [(ngModel)]="form.emaSlow" /></label>
            <label class="field">RSI period <input type="number" [(ngModel)]="form.rsiPeriod" /></label>
            <label class="field">RSI min <input type="number" [(ngModel)]="form.rsiMinimum" /></label>
            <label class="field">RSI long max <input type="number" [(ngModel)]="form.rsiLongMax" /></label>
          </div>
        }
        @if (form.templateKey === 'macd_trend') {
          <div class="form-grid cols-3">
            <label class="field">MACD fast <input type="number" [(ngModel)]="form.macdFast" /></label>
            <label class="field">MACD slow <input type="number" [(ngModel)]="form.macdSlow" /></label>
            <label class="field">MACD signal <input type="number" [(ngModel)]="form.macdSignal" /></label>
            <label class="field">Slow EMA <input type="number" [(ngModel)]="form.emaSlow" /></label>
          </div>
        }
        @if (form.templateKey === 'rsi_pullback') {
          <div class="form-grid cols-3">
            <label class="field">Slow EMA <input type="number" [(ngModel)]="form.emaSlow" /></label>
            <label class="field">RSI period <input type="number" [(ngModel)]="form.rsiPeriod" /></label>
            <label class="field">Oversold <input type="number" [(ngModel)]="form.rsiOversold" /></label>
            <label class="field">Overbought <input type="number" [(ngModel)]="form.rsiOverbought" /></label>
          </div>
        }
        @if (form.templateKey === 'bollinger_reversion') {
          <div class="form-grid cols-3">
            <label class="field">BB period <input type="number" [(ngModel)]="form.bbPeriod" /></label>
            <label class="field">BB stddev <input type="number" step="0.1" [(ngModel)]="form.bbStdDev" /></label>
            <label class="field">Slow EMA <input type="number" [(ngModel)]="form.emaSlow" /></label>
          </div>
        }
        @if (form.templateKey === 'donchian_breakout') {
          <div class="form-grid cols-3">
            <label class="field">Donchian length <input type="number" [(ngModel)]="form.donchianLength" /></label>
            <label class="field">EMA fast <input type="number" [(ngModel)]="form.emaFast" /></label>
            <label class="field">EMA slow <input type="number" [(ngModel)]="form.emaSlow" /></label>
          </div>
        }
        <div class="strategy-filters">
          <div class="form-grid cols-3">
            <label class="field">
              <span class="settings-check">
                <input type="checkbox" [(ngModel)]="form.requireVolume" />
                Volume confirmation
              </span>
            </label>
            <label class="field">Volume lookback <input type="number" [(ngModel)]="form.volumeLookback" /></label>
            <label class="field">Min ATR% <input type="number" step="0.01" [(ngModel)]="form.minAtrPercent" /></label>
            <label class="field">Max ATR% <input type="number" step="0.01" [(ngModel)]="form.maxAtrPercent" /></label>
          </div>
          <p class="tiny">Filters skip some noisy bars. They do not cap loss or place SL/TP.</p>
        </div>
        <label class="field">
          <span class="settings-check">
            <input type="checkbox" [(ngModel)]="form.all" />
            Apply to every USD-M USDT perpetual
          </span>
        </label>
        @if (!form.all) {
          <label class="field">Assigned coins
            <textarea rows="3" [(ngModel)]="form.symbols" placeholder="BTCUSDT, ETHUSDT, SOLUSDT"></textarea>
          </label>
          <p class="tiny">Comma-separated coins. This does not start bots.</p>
        }
        <div class="btn-row">
          <button class="btn accent" type="button" [disabled]="busy" (click)="save()">Save strategy</button>
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
  readonly templates = templateOptions;
  readonly sides = sideOptions;
  readonly formatTime = formatTime;
  readonly money = money;
  readonly creating = signal<StrategyDraft | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly draft = signal<StrategyDraft | null>(null);
  readonly previews = signal<Record<string, StrategyPreviewDto>>({});
  readonly previewBusy = signal<string | null>(null);
  previewCoin = 'BTCUSDT';
  readonly list = new ListQuery();
  readonly visible = computed(() =>
    this.list.apply(
      this.trading.strategies(),
      (row) => [row.name, row.description, row.timeframe, row.templateKey, row.allowedSymbols?.join(' ')],
      {
        name: (row) => row.name,
        tf: (row) => row.timeframe,
      },
    ),
  );
  readonly coins = computed(() => {
    const rows = this.trading.markets();
    if (rows.length) {
      return rows.map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
    }
    return this.trading.tickers().map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
  });
  busy = false;

  constructor() {
    void this.trading.refreshCatalog();
    void this.trading.refreshMarkets();
  }

  qualityOn(row: StrategyDto): boolean {
    return !!row.requireVolume || (row.minAtrPercent ?? 0) > 0 || (row.maxAtrPercent ?? 0) > 0;
  }

  signalClass(signal: string | undefined): string {
    switch ((signal || '').toLowerCase()) {
      case 'buy':
        return 'sig-buy';
      case 'sell':
        return 'sig-sell';
      case 'exit':
        return 'sig-exit';
      case 'hold':
        return 'sig-hold';
      default:
        return 'sig-none';
    }
  }

  blurbFor(key: string): string {
    return (
      templateLogic[key] ||
      'Closed-candle signal only. Isolated size, stop loss, and take profit stay on Risk.'
    );
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

  async toggleEnabled(row: StrategyDto): Promise<void> {
    this.busy = true;
    try {
      const enabled = row.isEnabled === false;
      await this.trading.setStrategyEnabled(row.id, enabled);
      await this.trading.refreshCatalog();
      this.toast.show(
        enabled ? 'Strategy enabled' : 'Strategy disabled',
        enabled
          ? 'It can be assigned to bots again. This does not start bots.'
          : 'Bots cannot start on it. The template is not deleted.',
        'success',
      );
    } catch {
      this.toast.show('Update blocked', 'Could not change enabled state.', 'error');
    } finally {
      this.busy = false;
    }
  }

  async preview(row: StrategyDto): Promise<void> {
    this.previewBusy.set(row.id);
    try {
      const snap = await this.trading.previewStrategy(row.id, this.previewCoin);
      this.previews.update((current) => ({ ...current, [row.id]: snap }));
    } catch {
      this.toast.show('Preview blocked', 'Could not load closed candles for that coin.', 'error');
    } finally {
      this.previewBusy.set(null);
    }
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
      this.toast.show('Save blocked', 'Check the name, template values, and coin assignment.', 'error');
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
    templateKey: 'ema_rsi_trend',
    allowedSide: 'Long',
    emaFast: 20,
    emaSlow: 50,
    rsiPeriod: 14,
    rsiMinimum: 50,
    rsiLongMax: 68,
    rsiOversold: 30,
    rsiOverbought: 70,
    macdFast: 12,
    macdSlow: 26,
    macdSignal: 9,
    bbPeriod: 20,
    bbStdDev: 2,
    donchianLength: 20,
    requireVolume: true,
    volumeLookback: 20,
    minAtrPercent: 0.15,
    maxAtrPercent: 4,
  };
}

function fromStrategy(row: StrategyDto): StrategyDraft {
  return {
    name: row.name,
    description: row.description ?? '',
    timeframe: row.timeframe || '5m',
    all: row.appliesToAllSymbols,
    symbols: (row.allowedSymbols ?? []).join(', '),
    templateKey: row.templateKey || 'ema_rsi_trend',
    allowedSide: row.allowedSide || 'Long',
    emaFast: row.emaFast ?? 20,
    emaSlow: row.emaSlow ?? 50,
    rsiPeriod: row.rsiPeriod ?? 14,
    rsiMinimum: row.rsiMinimum ?? 50,
    rsiLongMax: row.rsiLongMax ?? 68,
    rsiOversold: row.rsiOversold ?? 30,
    rsiOverbought: row.rsiOverbought ?? 70,
    macdFast: row.macdFast ?? 12,
    macdSlow: row.macdSlow ?? 26,
    macdSignal: row.macdSignal ?? 9,
    bbPeriod: row.bbPeriod ?? 20,
    bbStdDev: row.bbStdDev ?? 2,
    donchianLength: row.donchianLength ?? 20,
    requireVolume: !!row.requireVolume,
    volumeLookback: row.volumeLookback ?? 20,
    minAtrPercent: row.minAtrPercent ?? 0,
    maxAtrPercent: row.maxAtrPercent ?? 0,
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
    templateKey: form.templateKey,
    allowedSide: form.allowedSide,
    emaFast: Number(form.emaFast),
    emaSlow: Number(form.emaSlow),
    rsiPeriod: Number(form.rsiPeriod),
    rsiMinimum: Number(form.rsiMinimum),
    rsiLongMax: Number(form.rsiLongMax),
    rsiOversold: Number(form.rsiOversold),
    rsiOverbought: Number(form.rsiOverbought),
    macdFast: Number(form.macdFast),
    macdSlow: Number(form.macdSlow),
    macdSignal: Number(form.macdSignal),
    bbPeriod: Number(form.bbPeriod),
    bbStdDev: Number(form.bbStdDev),
    donchianLength: Number(form.donchianLength),
    requireVolume: !!form.requireVolume,
    volumeLookback: Number(form.volumeLookback),
    minAtrPercent: Number(form.minAtrPercent),
    maxAtrPercent: Number(form.maxAtrPercent),
  };
}

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
          <p class="tiny">Sizing uses the active Isolated book {{ trading.risk()?.name || '—' }} · {{ trading.risk()?.riskPerTradePercent ?? '—' }}% R · {{ trading.risk()?.stopLossPercent ?? '—' }}% SL · {{ trading.risk()?.takeProfitPercent ?? '—' }}% TP · {{ trading.risk()?.maxLeverage ?? '—' }}x. Change it on Risk.</p>
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
    if (rows.length) {
      return rows.map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
    }
    return this.trading.tickers().map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
  });

  constructor() {
    void this.trading.refreshCatalog();
    void this.trading.refreshRisk();
    void this.trading.refreshMarkets();
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
        riskPercent: this.trading.risk()?.riskPerTradePercent ?? 1,
        leverage: this.trading.risk()?.maxLeverage ?? 3,
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
