import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { TradingHubService } from '../../core/realtime/trading-hub.service';
import { TradingService } from '../../core/trading/trading.service';
import { formatClock } from '../../core/trading/trading.models';
import { ToastService } from '../../core/ui/toast.service';
import { TradingMode, UiStateService } from '../../core/ui/ui-state.service';
import { IconComponent } from '../../shared/icon/icon';
import { EmergencyStopComponent } from '../../shared/emergency/emergency-stop';
import { ToastHostComponent } from '../../shared/toast/toast-host';

interface NavItem {
  path: string;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent, EmergencyStopComponent, ToastHostComponent],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  readonly trading = inject(TradingService);
  readonly hub = inject(TradingHubService);
  readonly ui = inject(UiStateService);
  readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  readonly formatClock = formatClock;

  readonly groups = computed(() => [
    {
      label: 'Overview',
      items: [
        { path: '/dashboard', label: 'Dashboard', icon: 'dashboard' },
        { path: '/trading', label: 'Trading', icon: 'candles' },
        { path: '/bots', label: 'Bots', icon: 'bot' },
      ],
    },
    {
      label: 'Markets',
      items: [
        { path: '/scanner', label: 'Market Scanner', icon: 'scan' },
        { path: '/watchlist', label: 'Watchlist', icon: 'star' },
      ],
    },
    {
      label: 'Analysis',
      items: [
        { path: '/strategies', label: 'Strategies', icon: 'layers' },
        { path: '/risk', label: 'Risk Management', icon: 'shield' },
        { path: '/backtesting', label: 'Backtesting', icon: 'flask' },
        { path: '/performance', label: 'Performance', icon: 'chart' },
      ],
    },
    {
      label: 'Account',
      items: [
        { path: '/portfolio', label: 'Portfolio', icon: 'pie' },
        { path: '/positions', label: 'Positions', icon: 'positions' },
        { path: '/orders', label: 'Orders', icon: 'orders' },
        { path: '/fills', label: 'Trade History', icon: 'trades' },
        { path: '/trades', label: 'Position History', icon: 'chart' },
      ],
    },
    {
      label: 'System',
      items: [
        ...(this.ui.showLiveChrome() ? [{ path: '/exchanges', label: 'Live Connection', icon: 'plug' }] : []),
        { path: '/notifications', label: 'Notifications', icon: 'bell' },
        { path: '/settings', label: 'Settings', icon: 'settings' },
        { path: '/admin', label: 'Admin', icon: 'admin' },
      ],
    },
  ]);

  readonly page = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(() => this.routeMeta()),
    ),
    { initialValue: this.routeMeta() },
  );

  readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  readonly connection = computed(() => {
    const restOk = !this.trading.error();
    const hubOk = this.hub.connected();
    if (!restOk) {
      return { label: 'Disconnected', tone: 'bad' as const };
    }
    if (!hubOk) {
      return { label: 'Degraded', tone: 'warn' as const };
    }
    return { label: 'Connected', tone: 'ok' as const };
  });

  readonly userLabel = computed(() => this.auth.current()?.email ?? 'Administrator');
  private readonly enteredLiveSetupPage = signal(false);

  constructor() {
    effect(() => {
      const url = this.currentUrl();
      if (this.ui.liveSetup() && url.startsWith('/exchanges')) {
        this.enteredLiveSetupPage.set(true);
      }
      if (!this.ui.showLiveChrome() && url.startsWith('/exchanges')) {
        void this.router.navigateByUrl('/dashboard');
      } else if (
        this.enteredLiveSetupPage() &&
        this.ui.liveSetup() &&
        !this.ui.isLive() &&
        !url.startsWith('/exchanges')
      ) {
        this.ui.endLiveSetup();
        this.enteredLiveSetupPage.set(false);
      }
      if (!this.ui.liveSetup()) {
        this.enteredLiveSetupPage.set(false);
      }
    });
    void this.bootstrap();
  }

  private async bootstrap(): Promise<void> {
    await Promise.all([this.trading.refresh(), this.trading.refreshMarkets(), this.trading.refreshHealth(), this.trading.refreshRisk(), this.trading.refreshCatalog(), this.trading.refreshPerformance()]);
    if (this.ui.isLive() && !this.trading.overview()?.liveHasKeys) {
      this.ui.setMode('paper', true);
    }
    try {
      await this.hub.connect();
    } catch {
      this.toast.show('Realtime offline', 'REST data still loads. SignalR hub is not connected.', 'error', 'connection');
    }
    this.trading.startOverviewPoll(() => this.hub.connected());
  }

  private routeMeta(): { title: string; subtitle: string } {
    let route = this.router.routerState.snapshot.root;
    while (route.firstChild) {
      route = route.firstChild;
    }
    return {
      title: String(route.data['title'] ?? 'TradeBot'),
      subtitle: String(route.data['subtitle'] ?? 'Automate. Backtest. Trade.'),
    };
  }

  async setMode(mode: TradingMode): Promise<void> {
    if (mode === 'live') {
      try {
        const status = await this.trading.exchangeStatus();
        if (!status.hasKeys) {
          this.ui.setMode('paper', true);
          this.ui.beginLiveSetup();
          this.toast.show('Mode blocked', 'Save your Binance API key on Live Connection first.', 'error', 'connection');
          void this.router.navigateByUrl('/exchanges');
          return;
        }
        const result = this.ui.setMode('live', true);
        await Promise.all([this.trading.refresh(), this.trading.refreshCatalog(), this.trading.refreshRisk(), this.trading.refreshPerformance()]);
        const usdt = this.trading.overview()?.liveAvailable ?? status.usdtFree;
        this.toast.show(
          result.accepted ? 'LIVE Binance USD-M' : 'Mode blocked',
          result.accepted
            ? `Futures USDT: ${usdt ?? 0}. This is your real USD-M wallet. Nothing trades until you start a coin.`
            : result.message,
          result.accepted ? 'info' : 'error',
        );
      } catch {
        this.ui.setMode('paper', true);
        this.toast.show('Live unavailable', 'Could not read Binance account. Check the API and your key on Live Connection.', 'error', 'connection');
      }
      return;
    }

    const result = this.ui.setMode(mode, true);
    if (result.accepted) {
      await Promise.all([this.trading.refresh(), this.trading.refreshCatalog(), this.trading.refreshRisk(), this.trading.refreshPerformance()]);
    }
    this.toast.show(result.accepted ? 'Trading mode' : 'Mode blocked', result.message, result.accepted ? 'info' : 'error');
  }

  logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}
