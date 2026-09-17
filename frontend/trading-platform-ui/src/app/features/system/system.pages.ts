import { Component, computed, effect, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { TradingService } from '../../core/trading/trading.service';
import { ExchangeConnectionDto, RiskProfileDto, SaveRiskProfileRequest } from '../../core/trading/trading.models';
import { ListQuery } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { ToastService } from '../../core/ui/toast.service';
import { UiStateService } from '../../core/ui/ui-state.service';

interface RiskDraft {
  name: string;
  riskPerTradePercent: number;
  maxPositionPercent: number;
  maxDailyLossPercent: number;
  maxOpenPositions: number;
  maxDailyTrades: number;
  cooldownAfterLossMinutes: number;
  maxConsecutiveLosses: number;
  maxLeverage: number;
  stopBotOnDailyLoss: boolean;
  stopAccountOnDailyLoss: boolean;
  marginMode: 'Isolated' | 'Cross';
  maxPortfolioHeatPercent: number;
  maxTotalExposurePercent: number;
  correlationFactor: number;
  minFreeMarginPercent: number;
  all: boolean;
  symbols: string;
}

@Component({
  selector: 'app-risk-page',
  imports: [FormsModule, NgTemplateOutlet, SortBtnComponent],
  template: `
    <header class="page-header">
      <div class="list-sorts">
        <app-sort-btn column="name" [query]="list">Book</app-sort-btn>
        <app-sort-btn column="margin" [query]="list">Margin</app-sort-btn>
        <app-sort-btn column="r" [query]="list">R</app-sort-btn>
      </div>
      <button class="btn" type="button" [disabled]="busy" (click)="beginCreate()">Add profile</button>
    </header>
    @if (creating(); as form) {
      <section class="panel">
        <h2>New risk profile</h2>
        <ng-container [ngTemplateOutlet]="editor" [ngTemplateOutletContext]="{ $implicit: form, system: false }" />
      </section>
    }
    @for (row of visible(); track row.id ?? row.name) {
      <section class="panel">
        <div class="section-head">
          <div>
            <strong>{{ row.name }}</strong>
            <div class="tiny">{{ row.isSystem ? 'System book' : 'Custom book' }} · {{ row.marginMode || 'Isolated' }} · {{ row.riskPerTradePercent }}% R · {{ row.maxPortfolioHeatPercent ?? 4 }}% heat · {{ row.maxDailyLossPercent }}% daily halt</div>
          </div>
          <span class="badge" [class.badge-running]="row.appliesToAllSymbols !== false" [class.badge-paused]="row.appliesToAllSymbols === false">
            {{ row.appliesToAllSymbols === false ? ((row.allowedSymbols?.length ?? 0) + ' coins') : 'All coins' }}
          </span>
        </div>
        @if (editingId() === row.id && draft(); as form) {
          <ng-container [ngTemplateOutlet]="editor" [ngTemplateOutletContext]="{ $implicit: form, system: row.isSystem }" />
        } @else {
          <section class="kpi-row cols-3" style="margin:4px 0 12px">
            <article class="card"><div class="metric-label">R / trade</div><div class="metric-value">{{ row.riskPerTradePercent }}%</div></article>
            <article class="card"><div class="metric-label">Margin</div><div class="metric-value">{{ row.marginMode || 'Isolated' }}</div></article>
            <article class="card"><div class="metric-label">Heat cap</div><div class="metric-value">{{ row.maxPortfolioHeatPercent ?? 4 }}%</div></article>
            <article class="card"><div class="metric-label">Daily halt</div><div class="metric-value">{{ row.maxDailyLossPercent }}%</div></article>
            <article class="card"><div class="metric-label">Leverage</div><div class="metric-value">{{ row.maxLeverage }}x</div></article>
            <article class="card"><div class="metric-label">Book size</div><div class="metric-value">{{ row.maxOpenPositions }}</div></article>
          </section>
          <p class="tiny">{{ row.appliesToAllSymbols === false ? 'Assigned to ' + (row.allowedSymbols ?? []).join(', ') : 'Assigned to every USD-M USDT perpetual.' }}</p>
          <div class="btn-row" style="margin-top:12px">
            <button class="btn secondary" type="button" [disabled]="busy || !row.id" (click)="beginEdit(row)">Edit</button>
          </div>
        }
      </section>
    }
    <ng-template #editor let-form let-system="system">
      <div class="form" style="margin-top:12px;max-width:880px">
        <label class="field">Name <input [(ngModel)]="form.name" [disabled]="system" /></label>
        <div class="form-grid cols-3">
          <label class="field">Margin
            <select [(ngModel)]="form.marginMode">
              <option value="Isolated">Isolated — one coin can die, others live</option>
              <option value="Cross">Cross — whole futures wallet is collateral</option>
            </select>
          </label>
          <label class="field">R / trade % (loss if SL hits) <input type="number" step="0.05" [(ngModel)]="form.riskPerTradePercent" /></label>
          <label class="field">Max notional / name % <input type="number" step="0.1" [(ngModel)]="form.maxPositionPercent" /></label>
          <label class="field">Daily loss halt % <input type="number" step="0.1" [(ngModel)]="form.maxDailyLossPercent" /></label>
          <label class="field">Portfolio heat % <input type="number" step="0.1" [(ngModel)]="form.maxPortfolioHeatPercent" /></label>
          <label class="field">Total exposure % <input type="number" step="0.1" [(ngModel)]="form.maxTotalExposurePercent" /></label>
          <label class="field">Correlation 0–1 <input type="number" step="0.05" min="0" max="1" [(ngModel)]="form.correlationFactor" /></label>
          <label class="field">Cash reserve % <input type="number" step="1" [(ngModel)]="form.minFreeMarginPercent" /></label>
          <label class="field">Max leverage <input type="number" step="1" min="1" [(ngModel)]="form.maxLeverage" /></label>
          <label class="field">Max open names (this book) <input type="number" [(ngModel)]="form.maxOpenPositions" /></label>
          <label class="field">Max daily trades (this book) <input type="number" [(ngModel)]="form.maxDailyTrades" /></label>
          <label class="field">Cooldown after loss (min) <input type="number" [(ngModel)]="form.cooldownAfterLossMinutes" /></label>
          <label class="field">Max consecutive losses <input type="number" [(ngModel)]="form.maxConsecutiveLosses" /></label>
        </div>
        <p class="tiny">Cross is capped at 5 names and 5x. Isolated is for a coin universe. Starting bots is still manual.</p>
        <label class="field">
          <span style="display:flex;gap:8px;align-items:center">
            <input type="checkbox" [(ngModel)]="form.stopBotOnDailyLoss" />
            Stop this bot when the daily halt hits
          </span>
        </label>
        <label class="field">
          <span style="display:flex;gap:8px;align-items:center">
            <input type="checkbox" [(ngModel)]="form.stopAccountOnDailyLoss" />
            Stop the whole paper/live book when the daily halt hits (positions stay open; Binance SL/TP remain)
          </span>
        </label>
        <label class="field">
          <span style="display:flex;gap:8px;align-items:center">
            <input type="checkbox" [(ngModel)]="form.all" />
            Apply to every USD-M USDT perpetual
          </span>
        </label>
        @if (!form.all) {
          <label class="field">Assigned coins
            <textarea rows="3" [(ngModel)]="form.symbols" placeholder="BTCUSDT, ETHUSDT"></textarea>
          </label>
        }
        <div class="btn-row">
          <button class="btn" type="button" [disabled]="busy" (click)="save()">Save assignment</button>
          <button class="btn secondary" type="button" [disabled]="busy" (click)="cancel()">Cancel</button>
        </div>
      </div>
    </ng-template>
  `,
})
export class RiskPage {
  readonly trading = inject(TradingService);
  private readonly toast = inject(ToastService);
  readonly creating = signal<RiskDraft | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly draft = signal<RiskDraft | null>(null);
  readonly list = new ListQuery();
  readonly visible = computed(() =>
    this.list.apply(
      this.trading.riskProfiles(),
      (row) => [row.name, row.marginMode, row.allowedSymbols?.join(' ')],
      {
        name: (row) => row.name,
        margin: (row) => row.marginMode || 'Isolated',
        r: (row) => row.riskPerTradePercent,
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
    this.creating.set(blankRisk());
  }

  beginEdit(row: RiskProfileDto): void {
    this.creating.set(null);
    this.editingId.set(row.id);
    this.draft.set(fromRisk(row));
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
    const body = toRiskRequest(form);
    this.busy = true;
    try {
      if (id) {
        await this.trading.updateRiskProfile(id, body);
      } else {
        await this.trading.createRiskProfile(body);
      }
      await this.trading.refreshCatalog();
      this.cancel();
      this.toast.show(
        id ? 'Risk saved' : 'Risk profile added',
        form.all ? `${form.name || 'Profile'} can apply to every coin.` : `Assigned to ${body.symbols.length} coin(s).`,
        'success',
        'risk',
      );
    } catch {
      this.toast.show('Save blocked', 'Check the name, limits, and coin assignment.', 'error', 'risk');
    } finally {
      this.busy = false;
    }
  }
}

function blankRisk(): RiskDraft {
  return {
    name: '',
    riskPerTradePercent: 0.5,
    maxPositionPercent: 20,
    maxDailyLossPercent: 3,
    maxOpenPositions: 8,
    maxDailyTrades: 24,
    cooldownAfterLossMinutes: 15,
    maxConsecutiveLosses: 4,
    maxLeverage: 2,
    stopBotOnDailyLoss: true,
    stopAccountOnDailyLoss: true,
    marginMode: 'Isolated',
    maxPortfolioHeatPercent: 4,
    maxTotalExposurePercent: 60,
    correlationFactor: 0.75,
    minFreeMarginPercent: 20,
    all: true,
    symbols: '',
  };
}

function fromRisk(row: RiskProfileDto): RiskDraft {
  return {
    name: row.name,
    riskPerTradePercent: row.riskPerTradePercent,
    maxPositionPercent: row.maxPositionPercent,
    maxDailyLossPercent: row.maxDailyLossPercent,
    maxOpenPositions: row.maxOpenPositions,
    maxDailyTrades: row.maxDailyTrades,
    cooldownAfterLossMinutes: row.cooldownAfterLossMinutes,
    maxConsecutiveLosses: row.maxConsecutiveLosses,
    maxLeverage: row.maxLeverage,
    stopBotOnDailyLoss: row.stopBotOnDailyLoss,
    stopAccountOnDailyLoss: row.stopAccountOnDailyLoss !== false,
    marginMode: row.marginMode === 'Cross' ? 'Cross' : 'Isolated',
    maxPortfolioHeatPercent: row.maxPortfolioHeatPercent ?? 4,
    maxTotalExposurePercent: row.maxTotalExposurePercent ?? 60,
    correlationFactor: row.correlationFactor ?? 0.75,
    minFreeMarginPercent: row.minFreeMarginPercent ?? 20,
    all: row.appliesToAllSymbols !== false,
    symbols: (row.allowedSymbols ?? []).join(', '),
  };
}

function toRiskRequest(form: RiskDraft): SaveRiskProfileRequest {
  return {
    name: form.name.trim(),
    riskPerTradePercent: Number(form.riskPerTradePercent),
    maxPositionPercent: Number(form.maxPositionPercent),
    maxDailyLossPercent: Number(form.maxDailyLossPercent),
    maxOpenPositions: Number(form.maxOpenPositions),
    maxDailyTrades: Number(form.maxDailyTrades),
    cooldownAfterLossMinutes: Number(form.cooldownAfterLossMinutes),
    maxConsecutiveLosses: Number(form.maxConsecutiveLosses),
    maxLeverage: Number(form.maxLeverage),
    stopBotOnDailyLoss: form.stopBotOnDailyLoss,
    stopAccountOnDailyLoss: form.stopAccountOnDailyLoss,
    marginMode: form.marginMode,
    maxPortfolioHeatPercent: Number(form.maxPortfolioHeatPercent),
    maxTotalExposurePercent: Number(form.maxTotalExposurePercent),
    correlationFactor: Number(form.correlationFactor),
    minFreeMarginPercent: Number(form.minFreeMarginPercent),
    appliesToAllSymbols: form.all,
    symbols: form.symbols.split(/[\s,]+/).map((item) => item.trim().toUpperCase()).filter(Boolean),
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
      (row) => [row.name, row.marginMode, row.appliesToAllSymbols === false ? 'coins' : 'All'],
      {
        name: (row) => row.name,
        margin: (row) => row.marginMode || 'Isolated',
        r: (row) => row.riskPerTradePercent,
        heat: (row) => row.maxPortfolioHeatPercent ?? 4,
        halt: (row) => row.maxDailyLossPercent,
        coins: (row) => (row.appliesToAllSymbols === false ? (row.allowedSymbols?.length ?? 0) : 9999),
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
