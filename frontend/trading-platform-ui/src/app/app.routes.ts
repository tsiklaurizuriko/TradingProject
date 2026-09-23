import { Routes } from '@angular/router';
import { Shell } from './layout/shell/shell';
import { liveOnlyGuard } from './live-only.guard';
import { LoginPage } from './features/auth/login.page';
import { DashboardPage } from './features/dashboard/dashboard.page';
import { BotsPage } from './features/bots/bots.page';
import { BotDetailPage } from './features/bots/bot-detail.page';
import { TradingPage } from './features/trading/trading.page';
import { ScannerPage, WatchlistPage } from './features/markets/markets.pages';
import { BacktestingPage, StrategiesPage } from './features/research/research.pages';
import { ScalpingPage } from './features/research/scalping.page';
import { PerformancePage, PortfolioPage } from './features/account/account.pages';
import { OrdersPage, PositionsPage, TradesPage, FillsPage } from './features/ledger/ledger.pages';
import { AdminPage, ExchangesPage, NotificationsPage, RiskPage, SettingsPage } from './features/system/system.pages';

const page = (title: string, subtitle: string) => ({ title, subtitle });

export const routes: Routes = [
  { path: 'login', component: LoginPage, data: page('Sign in', 'TradeBot access') },
  {
    path: '',
    component: Shell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', component: DashboardPage, data: page('Dashboard', '') },
      { path: 'trading', component: TradingPage, data: page('Trading', '') },
      { path: 'bots', component: BotsPage, data: page('Bots', '') },
      { path: 'bots/:id', component: BotDetailPage, data: page('Bot details', 'Chart, signals, risk, and logs for a single bot.') },
      { path: 'scanner', component: ScannerPage, data: page('Market Scanner', '') },
      { path: 'watchlist', component: WatchlistPage, data: page('Watchlist', '') },
      { path: 'strategies', component: StrategiesPage, data: page('Strategies', '') },
      { path: 'scalping', component: ScalpingPage, data: page('Scalping', 'Research-only. LIVE off.') },
      { path: 'strategy-builder', redirectTo: 'strategies' },
      { path: 'backtesting', component: BacktestingPage, data: page('Backtesting', '') },
      { path: 'performance', component: PerformancePage, data: page('Performance', '') },
      { path: 'portfolio', component: PortfolioPage, data: page('Portfolio', '') },
      { path: 'positions', component: PositionsPage, data: page('Positions', '') },
      { path: 'orders', component: OrdersPage, data: page('Orders', 'Open orders and order history, like Binance.') },
      { path: 'fills', component: FillsPage, data: page('Trade History', 'One row per fill.') },
      { path: 'trades', component: TradesPage, data: page('Position History', 'Closed Isolated round-trips.') },
      { path: 'risk', component: RiskPage, data: page('Risk Management', '') },
      { path: 'exchanges', component: ExchangesPage, canActivate: [liveOnlyGuard], data: page('Live Connection', 'Binance USD-M API key. Nothing trades until you start one coin on Bots.') },
      { path: 'notifications', component: NotificationsPage, data: page('Notifications', '') },
      { path: 'settings', component: SettingsPage, data: page('Settings', '') },
      { path: 'admin', component: AdminPage, data: page('Admin', '') },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];
