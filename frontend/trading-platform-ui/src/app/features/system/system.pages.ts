import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { TradingService } from '../../core/trading/trading.service';
import { ExchangeConnectionDto, SaveRiskProfileRequest } from '../../core/trading/trading.models';
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
  selector: 'app-risk-page',
  imports: [FormsModule],
  styleUrl: './risk.page.scss',
  template: `
    <div class="risk-page">
      <header class="risk-head">
        <h1>Strategy risk</h1>
        <p>Each strategy keeps its own stop, take profit, and size. A bot uses the risk of the strategy you pick. Open positions keep the stop written at fill.</p>
      </header>
      <div class="risk-list">
        @for (row of trading.strategies(); track row.id) {
          <article class="risk-card">
            <div class="risk-card-head">
              <div class="risk-card-title">
                <strong>{{ row.name }}</strong>
                <span class="chip">{{ row.timeframe }}</span>
              </div>
              <button class="btn sm" type="button" [class.secondary]="!dirty(row.id)" [class.accent]="dirty(row.id)" [disabled]="busy || !dirty(row.id)" (click)="save(row)">Save</button>
            </div>
            <div class="risk-fields">
              <label>Risk %<input type="number" step="0.1" [ngModel]="draftOf(row).riskPerTradePercent" (ngModelChange)="patch(row, 'riskPerTradePercent', $event)" /></label>
              <label>Stop %<input type="number" step="0.1" [ngModel]="draftOf(row).stopLossPercent" (ngModelChange)="patch(row, 'stopLossPercent', $event)" /></label>
              <label>Take %<input type="number" step="0.1" [ngModel]="draftOf(row).takeProfitPercent" (ngModelChange)="patch(row, 'takeProfitPercent', $event)" /></label>
              <label>Leverage<input type="number" step="1" [ngModel]="draftOf(row).maxLeverage" (ngModelChange)="patch(row, 'maxLeverage', $event)" /></label>
              <label>Positions<input type="number" step="1" [ngModel]="draftOf(row).maxSimultaneousPositions" (ngModelChange)="patch(row, 'maxSimultaneousPositions', $event)" /></label>
              <label>Loss streak<input type="number" step="1" [ngModel]="draftOf(row).maxConsecutiveLosses" (ngModelChange)="patch(row, 'maxConsecutiveLosses', $event)" /></label>
              <label>Cooldown<input type="number" step="1" [ngModel]="draftOf(row).cooldownMinutes" (ngModelChange)="patch(row, 'cooldownMinutes', $event)" /></label>
            </div>
          </article>
        }
      </div>
    </div>
  `,
})
export class RiskPage {
  readonly trading = inject(TradingService);
  private readonly toast = inject(ToastService);
  readonly drafts = signal<Record<string, RiskDraft>>({});
  busy = false;

  constructor() {
    void this.trading.refreshCatalog();
  }

  draftOf(row: {
    id: string;
    riskPerTradePercent?: number;
    stopLossPercent?: number;
    takeProfitPercent?: number;
    maxLeverage?: number;
    maxSimultaneousPositions?: number;
    maxConsecutiveLosses?: number;
    cooldownMinutes?: number;
  }): RiskDraft {
    const existing = this.drafts()[row.id];
    if (existing) {
      return existing;
    }
    return {
      riskPerTradePercent: row.riskPerTradePercent || 0.5,
      stopLossPercent: row.stopLossPercent || 2,
      takeProfitPercent: row.takeProfitPercent || 4,
      maxLeverage: row.maxLeverage || 3,
      maxDailyLossPercent: 3,
      maxPortfolioRiskPercent: 4,
      maxSimultaneousPositions: row.maxSimultaneousPositions || 5,
      maxConsecutiveLosses: row.maxConsecutiveLosses || 5,
      cooldownMinutes: row.cooldownMinutes || 30,
      minimumLiquidationSafetyBufferPercent: 1,
      allowLive: true,
    };
  }

  dirty(id: string): boolean {
    return !!this.drafts()[id];
  }

  patch(row: { id: string; riskPerTradePercent?: number; stopLossPercent?: number; takeProfitPercent?: number; maxLeverage?: number }, key: keyof RiskDraft, value: number): void {
    const next = { ...this.draftOf(row), [key]: Number(value) };
    this.drafts.update((map) => ({ ...map, [row.id]: next }));
  }

  async save(row: { id: string; riskProfileId?: string | null; name: string; riskPerTradePercent?: number; stopLossPercent?: number; takeProfitPercent?: number; maxLeverage?: number }): Promise<void> {
    const id = row.riskProfileId;
    if (!id) {
      this.toast.show('Risk missing', row.name + ' has no risk row yet. Restart the API so the seed can attach one.', 'error', 'risk');
      return;
    }
    this.busy = true;
    try {
      await this.trading.updateRiskProfile(id, toRiskRequest(this.draftOf(row)));
      await this.trading.refreshCatalog();
      this.drafts.update((map) => {
        const next = { ...map };
        delete next[row.id];
        return next;
      });
      this.toast.show('Risk saved', row.name + ' will size the next entry.', 'success', 'risk');
    } catch {
      this.toast.show('Save blocked', 'Could not save this strategy risk.', 'error', 'risk');
    } finally {
      this.busy = false;
    }
  }
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
        time: (item) => item.id,
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
      this.trading.strategies(),
      (row) => [row.name],
      {
        name: (row) => row.name,
        r: (row) => row.riskPerTradePercent ?? 0,
        sl: (row) => row.stopLossPercent ?? 0,
        tp: (row) => row.takeProfitPercent ?? 0,
        slots: (row) => row.maxSimultaneousPositions ?? 0,
        lev: (row) => row.maxLeverage ?? 0,
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
      ? rows
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
