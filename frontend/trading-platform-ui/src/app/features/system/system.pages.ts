import { Component, computed, effect, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { TradingService } from '../../core/trading/trading.service';
import { ExchangeConnectionDto, RiskProfileDto, SaveRiskProfileRequest, money, previewRisk, price } from '../../core/trading/trading.models';
import { ListQuery } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { ToastService } from '../../core/ui/toast.service';
import { UiStateService } from '../../core/ui/ui-state.service';

interface RiskDraft {
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

@Component({
  selector: 'app-risk-page',
  imports: [FormsModule, NgTemplateOutlet],
  template: `
    <header class="page-header">
      <div>
        <strong>Active book {{ active()?.name || '—' }}</strong>
        <div class="tiny">New Isolated entries use this book on current {{ ui.workspace() }} available. Existing positions keep the snapshot from fill.</div>
      </div>
    </header>
    <section class="panel">
      <div class="section-head">
        <div>
          <h2>Calculator</h2>
          <p class="tiny">{{ ui.workspace() }} available · sample entry {{ price(samplePrice()) }}</p>
        </div>
      </div>
      <section class="kpi-row cols-3" style="margin:4px 0 12px">
        <article class="card"><div class="metric-label">Available</div><div class="metric-value">{{ money(available()) }}</div></article>
        <article class="card"><div class="metric-label">Planned Risk</div><div class="metric-value">{{ money(calc().risk) }}</div></article>
        <article class="card"><div class="metric-label">Notional</div><div class="metric-value">{{ money(calc().notional) }}</div></article>
        <article class="card"><div class="metric-label">Isolated margin</div><div class="metric-value">{{ money(calc().margin) }}</div></article>
        <article class="card"><div class="metric-label">SL price</div><div class="metric-value">{{ price(calc().stopPrice) }}</div></article>
        <article class="card"><div class="metric-label">TP price</div><div class="metric-value">{{ price(calc().takePrice) }}</div></article>
      </section>
      <p class="tiny">Planned Risk = available × R% if the stop fills as assumed. It is not a guaranteed maximum loss. Notional and Isolated margin are different numbers.</p>
    </section>
    @for (row of books(); track row.id ?? row.name) {
      <section class="panel">
        <div class="section-head">
          <div>
            <strong>{{ row.name }}</strong>
            <div class="tiny">Isolated · {{ row.riskPerTradePercent }}% R · {{ row.stopLossPercent }}% SL · {{ row.takeProfitPercent }}% TP · {{ row.maxLeverage }}x · {{ row.maxDailyLossPercent }}% daily halt{{ row.allowLive ? '' : ' · paper only' }}</div>
          </div>
          <span class="badge" [class.badge-running]="row.isActive" [class.badge-paused]="!row.isActive">{{ row.isActive ? 'Active' : 'Idle' }}</span>
        </div>
        @if (editingId() === row.id && draft(); as form) {
          <ng-container [ngTemplateOutlet]="editor" [ngTemplateOutletContext]="{ $implicit: form }" />
        } @else {
          <section class="kpi-row cols-3" style="margin:4px 0 12px">
            <article class="card"><div class="metric-label">Risk per trade</div><div class="metric-value">{{ row.riskPerTradePercent }}%</div></article>
            <article class="card"><div class="metric-label">Stop loss</div><div class="metric-value">{{ row.stopLossPercent }}%</div></article>
            <article class="card"><div class="metric-label">Take profit</div><div class="metric-value">{{ row.takeProfitPercent }}%</div></article>
            <article class="card"><div class="metric-label">Max leverage</div><div class="metric-value">{{ row.maxLeverage }}x</div></article>
            <article class="card"><div class="metric-label">Daily loss limit</div><div class="metric-value">{{ row.maxDailyLossPercent }}%</div></article>
            <article class="card"><div class="metric-label">Portfolio risk cap</div><div class="metric-value">{{ row.maxPortfolioRiskPercent }}%</div></article>
            <article class="card"><div class="metric-label">Max positions</div><div class="metric-value">{{ row.maxSimultaneousPositions }}</div></article>
            <article class="card"><div class="metric-label">Cooldown</div><div class="metric-value">{{ row.cooldownMinutes }}m</div></article>
            <article class="card"><div class="metric-label">Liq. buffer</div><div class="metric-value">{{ row.minimumLiquidationSafetyBufferPercent }}%</div></article>
            <article class="card"><div class="metric-label">LIVE</div><div class="metric-value">{{ row.allowLive ? 'Allowed' : 'Paper' }}</div></article>
          </section>
          <p class="tiny">LIVE places Binance SL/TP with the fill. Paper simulates them. Daily halt stops new entries only.</p>
          <div class="btn-row" style="margin-top:12px">
            <button class="btn" type="button" [disabled]="busy || row.isActive" (click)="activate(row)">Set active</button>
            <button class="btn secondary" type="button" [disabled]="busy || !row.id" (click)="beginEdit(row)">Edit</button>
          </div>
        }
      </section>
    }
    <ng-template #editor let-form>
      <div class="form" style="margin-top:12px;max-width:880px">
        <div class="form-grid cols-3">
          <label class="field">Risk per trade % <input type="number" step="0.1" [(ngModel)]="form.riskPerTradePercent" /></label>
          <label class="field">Stop loss % <input type="number" step="0.1" [(ngModel)]="form.stopLossPercent" /></label>
          <label class="field">Take profit % <input type="number" step="0.1" [(ngModel)]="form.takeProfitPercent" /></label>
          <label class="field">Max leverage <input type="number" step="1" min="1" [(ngModel)]="form.maxLeverage" /></label>
          <label class="field">Daily loss limit % <input type="number" step="0.1" [(ngModel)]="form.maxDailyLossPercent" /></label>
          <label class="field">Max portfolio risk % <input type="number" step="0.1" [(ngModel)]="form.maxPortfolioRiskPercent" /></label>
          <label class="field">Max positions <input type="number" step="1" min="1" [(ngModel)]="form.maxSimultaneousPositions" /></label>
          <label class="field">Consecutive losses <input type="number" step="1" min="1" [(ngModel)]="form.maxConsecutiveLosses" /></label>
          <label class="field">Cooldown minutes <input type="number" step="1" min="1" [(ngModel)]="form.cooldownMinutes" /></label>
          <label class="field">Liq. safety buffer % <input type="number" step="0.1" [(ngModel)]="form.minimumLiquidationSafetyBufferPercent" /></label>
        </div>
        <label class="field">
          <span style="display:flex;gap:8px;align-items:center">
            <input type="checkbox" [(ngModel)]="form.allowLive" />
            Allow LIVE bots on this book
          </span>
        </label>
        <div class="btn-row">
          <button class="btn" type="button" [disabled]="busy" (click)="save()">Save</button>
          <button class="btn secondary" type="button" [disabled]="busy" (click)="cancel()">Cancel</button>
        </div>
      </div>
    </ng-template>
  `,
})
export class RiskPage {
  readonly trading = inject(TradingService);
  readonly ui = inject(UiStateService);
  private readonly toast = inject(ToastService);
  readonly money = money;
  readonly price = price;
  readonly editingId = signal<string | null>(null);
  readonly draft = signal<RiskDraft | null>(null);
  readonly books = computed(() => this.trading.riskProfiles());
  readonly active = computed(() => this.books().find((row) => row.isActive) ?? this.trading.risk() ?? this.books()[0] ?? null);
  readonly available = computed(() => {
    const overview = this.trading.overview();
    if (this.ui.isLive()) {
      return overview?.liveAvailable ?? 0;
    }
    return overview?.availableBalance ?? 0;
  });
  readonly samplePrice = computed(() => {
    const ticker = this.trading.overview()?.ticker ?? this.trading.tickers().find((row) => row.symbol === 'BTCUSDT') ?? this.trading.tickers()[0];
    return ticker?.price || 100_000;
  });
  readonly calc = computed(() => {
    const active = this.active();
    const draft = this.editingId() === active?.id ? this.draft() : null;
    const row = draft ?? active;
    if (!row) {
      return { risk: 0, notional: 0, margin: 0, stopPrice: 0, takePrice: 0 };
    }
    return previewRisk(row, this.available(), this.samplePrice());
  });
  busy = false;

  constructor() {
    void this.trading.refreshCatalog();
    void this.trading.refreshRisk();
  }

  beginEdit(row: RiskProfileDto): void {
    this.editingId.set(row.id);
    this.draft.set(fromRisk(row));
  }

  cancel(): void {
    this.editingId.set(null);
    this.draft.set(null);
  }

  async activate(row: RiskProfileDto): Promise<void> {
    if (!row.id) {
      return;
    }
    this.busy = true;
    try {
      await this.trading.activateRiskProfile(row.id);
      await this.trading.refreshCatalog();
      await this.trading.refreshRisk();
      this.toast.show('Active book', `${row.name} sizes every new Isolated entry.`, 'success', 'risk');
    } catch {
      this.toast.show('Activate blocked', 'Could not switch the active risk book.', 'error', 'risk');
    } finally {
      this.busy = false;
    }
  }

  async save(): Promise<void> {
    const form = this.draft();
    const id = this.editingId();
    if (!form || !id) {
      return;
    }
    this.busy = true;
    try {
      await this.trading.updateRiskProfile(id, toRiskRequest(form));
      await this.trading.refreshCatalog();
      await this.trading.refreshRisk();
      this.cancel();
      this.toast.show('Risk saved', 'New entries use the active book. Open positions keep their snapshot.', 'success', 'risk');
    } catch {
      this.toast.show('Save blocked', 'Check R%, stop, take profit, leverage, and daily halt.', 'error', 'risk');
    } finally {
      this.busy = false;
    }
  }
}

function fromRisk(row: RiskProfileDto): RiskDraft {
  return {
    riskPerTradePercent: row.riskPerTradePercent,
    stopLossPercent: row.stopLossPercent,
    takeProfitPercent: row.takeProfitPercent,
    maxLeverage: row.maxLeverage,
    maxDailyLossPercent: row.maxDailyLossPercent,
    maxPortfolioRiskPercent: row.maxPortfolioRiskPercent ?? 4,
    maxSimultaneousPositions: row.maxSimultaneousPositions ?? 2,
    maxConsecutiveLosses: row.maxConsecutiveLosses ?? 5,
    cooldownMinutes: row.cooldownMinutes ?? 30,
    minimumLiquidationSafetyBufferPercent: row.minimumLiquidationSafetyBufferPercent ?? 1,
    allowLive: row.allowLive !== false,
  };
}

function toRiskRequest(form: RiskDraft): SaveRiskProfileRequest {
  return {
    riskPerTradePercent: Number(form.riskPerTradePercent),
    stopLossPercent: Number(form.stopLossPercent),
    takeProfitPercent: Number(form.takeProfitPercent),
    maxLeverage: Number(form.maxLeverage),
    maxDailyLossPercent: Number(form.maxDailyLossPercent),
    maxPortfolioRiskPercent: Number(form.maxPortfolioRiskPercent),
    maxSimultaneousPositions: Number(form.maxSimultaneousPositions),
    maxConsecutiveLosses: Number(form.maxConsecutiveLosses),
    cooldownMinutes: Number(form.cooldownMinutes),
    minimumLiquidationSafetyBufferPercent: Number(form.minimumLiquidationSafetyBufferPercent),
    allowLive: form.allowLive,
  };
}

@Component({
  selector: 'app-exchanges-page',
  imports: [FormsModule],
  template: `
    <section class="panel" style="max-width:640px">
      <div class="section-head">
        <div>
          <h2>BINANCE USD-M</h2>
          <p class="tiny">{{ status()?.message || 'Save a key, then start one USDT perpetual from Bots. Nothing auto-starts.' }}</p>
        </div>
        <span class="badge" [class.badge-ok]="status()?.liveReady" [class.badge-error]="!status()?.liveReady">{{ status()?.liveReady ? 'Live ready' : 'Not live' }}</span>
      </div>
      <div class="connection-meta">
        <div><span>API key</span><span>{{ status()?.apiKeyHint || 'Not saved' }}</span></div>
        <div><span>Spot USDT</span><span>{{ status()?.spotUsdt ?? status()?.usdtFree ?? '—' }}</span></div>
        <div><span>Funding USDT</span><span>{{ status()?.fundingUsdt ?? '—' }}</span></div>
        <div><span>Futures USDT</span><span>{{ status()?.futuresUsdt ?? '—' }}</span></div>
        <div><span>Can trade</span><span>{{ status()?.canTrade ? 'Yes' : 'No' }}</span></div>
        <div><span>Auto-start</span><span>Off</span></div>
      </div>
      <label class="field" style="margin-top:16px">API key
        <input type="text" autocomplete="off" [(ngModel)]="apiKey" />
      </label>
      <label class="field">API secret
        <input type="password" autocomplete="new-password" [(ngModel)]="apiSecret" />
      </label>
      <div class="btn-row" style="margin-top:16px">
        <button class="btn danger" type="button" [disabled]="busy" (click)="save()">Save live key</button>
        <button class="btn secondary" type="button" [disabled]="busy" (click)="reload()">Test</button>
      </div>
    </section>
  `,
})
export class ExchangesPage {
  readonly trading = inject(TradingService);
  private readonly toast = inject(ToastService);
  apiKey = '';
  apiSecret = '';
  busy = false;
  readonly status = signal<ExchangeConnectionDto | null>(null);

  constructor() {
    void this.reload();
  }

  async reload(): Promise<void> {
    try {
      this.status.set(await this.trading.exchangeStatus());
    } catch {
      this.status.set(null);
    }
  }

  async save(): Promise<void> {
    this.busy = true;
    try {
      this.status.set(await this.trading.saveExchangeKeys(this.apiKey, this.apiSecret));
      this.apiSecret = '';
      this.toast.show('Binance key saved', 'Live is ready only after you start a coin on Bots.', 'success', 'connection');
    } catch {
      this.toast.show('Key rejected', 'Binance did not accept this key. Enable Spot trade, keep withdraw off.', 'error', 'connection');
    } finally {
      this.busy = false;
    }
  }
}

@Component({
  selector: 'app-notifications-page',
  imports: [SortBtnComponent],
  styles: [`
    .watch-row { grid-template-columns: 0.9fr 1.4fr; cursor: default; }
  `],
  template: `
    <section class="panel">
      @if (toast.history().length === 0) {
        <p class="empty-state">No notifications in this session.</p>
      } @else {
        <div class="watch-row watch-head">
          <app-sort-btn column="title" [query]="list">Title</app-sort-btn>
          <app-sort-btn column="message" [query]="list">Message</app-sort-btn>
        </div>
        @for (item of rows(); track item.id) {
          <div class="watch-row">
            <span>{{ item.title }}</span>
            <span class="tiny">{{ item.message }}</span>
          </div>
        }
      }
    </section>
  `,
})
export class NotificationsPage {
  readonly toast = inject(ToastService);
  readonly list = new ListQuery();
  readonly rows = computed(() =>
    this.list.apply(
      this.toast.history(),
      (item) => [item.title, item.message],
      {
        title: (item) => item.title,
        message: (item) => item.message,
      },
    ),
  );
}

@Component({
  selector: 'app-settings-page',
  imports: [FormsModule, RouterLink, ExchangesPage, SortBtnComponent],
  templateUrl: './settings.page.html',
  styleUrl: './settings.page.scss',
})
export class SettingsPage {
  readonly ui = inject(UiStateService);
  readonly trading = inject(TradingService);
  readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  readonly current = signal('General');
  readonly timeframes = ['1m', '5m', '15m', '1h', '4h', '1d'];
  readonly riskQuery = new ListQuery();
  readonly riskRows = computed(() =>
    this.riskQuery.apply(
      this.trading.riskProfiles(),
      (row) => [row.name],
      {
        name: (row) => row.name,
        r: (row) => row.riskPerTradePercent,
        halt: (row) => row.maxDailyLossPercent,
        lev: (row) => row.maxLeverage,
      },
    ),
  );
  currentPassword = '';
  newPassword = '';
  confirmPassword = '';
  busy = false;
  readonly tabs = computed(() => {
    const tabs = ['General', 'Trading', 'Risk', 'Notifications', 'Security', 'Appearance'];
    if (this.ui.isLive()) {
      tabs.splice(1, 0, 'Exchange');
    }
    return tabs;
  });
  readonly coins = computed(() => {
    const rows = this.trading.markets();
    const list = (rows.length
      ? rows.slice(0, 40)
      : this.trading.tickers()
    ).map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
    const current = this.ui.selectedSymbol();
    if (current && !list.some((row) => row.symbol === current)) {
      return [{ symbol: current, displayName: current }, ...list];
    }
    return list;
  });

  constructor() {
    void this.trading.refreshCatalog();
    effect(() => {
      if (!this.ui.isLive() && this.current() === 'Exchange') {
        this.current.set('General');
      }
    });
  }

  testToast(): void {
    this.toast.show('Test toast', 'If this appears, popups for this topic are on.', 'info');
  }

  resetLayout(): void {
    this.ui.resetCollapsed();
    this.toast.show('Layout reset', 'Collapsed panels were expanded.', 'success');
  }

  async changePassword(): Promise<void> {
    if (this.newPassword.length < 8) {
      this.toast.show('Password too short', 'Use at least 8 characters.', 'error');
      return;
    }
    if (this.newPassword !== this.confirmPassword) {
      this.toast.show('Passwords do not match', 'Confirm the new password.', 'error');
      return;
    }
    this.busy = true;
    try {
      await this.auth.changePassword(this.currentPassword, this.newPassword);
      this.currentPassword = '';
      this.newPassword = '';
      this.confirmPassword = '';
      this.toast.show('Password changed', 'Use the new password next time you sign in.', 'success');
    } catch {
      this.toast.show(
        'Password not changed',
        'Current password is wrong, or restart the API so change-password is available.',
        'error',
      );
    } finally {
      this.busy = false;
    }
  }

  signOut(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}

@Component({
  selector: 'app-admin-page',
  template: `
    <section class="kpi-row cols-3">
      @if (ui.isLive()) {
        <article class="card">
          <div class="metric-label">Live trading</div>
          <div class="metric-value">{{ trading.overview()?.liveTradingEnabled ? 'ON' : 'OFF' }}</div>
        </article>
      }
      <article class="card">
        <div class="metric-label">Default mode</div>
        <div class="metric-value">{{ trading.health()?.defaultMode ?? 'Paper' }}</div>
      </article>
      <article class="card">
        <div class="metric-label">Kill switch</div>
        <div class="metric-value">Sidebar</div>
        <p class="tiny">Emergency Stop sits under your name. It stops running bots and leaves positions open.</p>
      </article>
    </section>
  `,
})
export class AdminPage {
  readonly trading = inject(TradingService);
  readonly ui = inject(UiStateService);
}
