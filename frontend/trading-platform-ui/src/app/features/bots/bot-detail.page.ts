import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TradingService } from '../../core/trading/trading.service';
import { botStatus, formatTime, money, modeBadge, notionalUsdt, price, qty, signedMoney } from '../../core/trading/trading.models';
import { ToastService } from '../../core/ui/toast.service';
import { UiStateService } from '../../core/ui/ui-state.service';
import { TradingChartComponent } from '../../shared/chart/trading-chart';
import { ListQuery, timeValue } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { ConfirmModalComponent, StatusBadgeComponent } from '../../shared/ui/ui-kit';

@Component({
  selector: 'app-bot-detail-page',
  imports: [RouterLink, TradingChartComponent, StatusBadgeComponent, ConfirmModalComponent, SortBtnComponent],
  template: `
    @if (bot(); as bot) {
      <header class="page-header">
        <div>
          <h1>{{ bot.symbol }}</h1>
          <p>{{ bot.strategyName }} v{{ bot.strategyVersion }} · {{ bot.timeframe }} · {{ bot.displayName }}</p>
        </div>
        <div class="btn-row">
          <app-status-badge [label]="botStatus(bot.status).label" [cls]="botStatus(bot.status).cls" />
          <span class="badge" [class]="modeBadge(bot.mode)">{{ bot.mode }}</span>
          <a class="btn secondary sm" routerLink="/bots">Back</a>
          <button class="btn danger sm" type="button" [disabled]="busy()" (click)="askDelete()">Delete</button>
        </div>
      </header>
      @if (!trading.belongsToWorkspace(bot)) {
        <section class="panel" style="border-color:rgba(255,61,87,.45)">
          <p>This is a {{ bot.mode }} bot. Switch the header to {{ bot.mode }} to operate it. Current workspace is {{ ui.workspace() }}.</p>
        </section>
      }
      <section class="dash-grid">
        <article class="panel chart-panel" style="min-height:360px">
          <app-trading-chart [bars]="trading.klines()" [positions]="trading.workspacePositions()" [trades]="trading.workspaceTrades()" [signals]="trading.workspaceSignals()" [symbol]="bot.symbol" />
        </article>
        <section class="panel">
          <h2>Position</h2>
          @if (position(); as pos) {
            <p>{{ pos.symbol }} · {{ pos.side === 'Buy' ? 'LONG' : pos.side }} · Isolated</p>
            <p>Entry {{ price(pos.averageEntryPrice) }} · Current {{ price(pos.currentPrice) }}</p>
            <p>Qty {{ qty(pos.quantity) }} · Notional {{ money(pos.notionalUsdt || notionalUsdt(pos.quantity, pos.averageEntryPrice)) }}</p>
            <p>Isolated Margin {{ pos.marginUsdt ? money(pos.marginUsdt) : '—' }} · Leverage {{ pos.leverage ? pos.leverage + 'x' : '—' }}</p>
            <p>SL {{ pos.stopLossPercent ? pos.stopLossPercent + '%' : '—' }} @ {{ pos.stopLossPrice ? price(pos.stopLossPrice) : '—' }}</p>
            <p>TP {{ pos.takeProfitPercent ? pos.takeProfitPercent + '%' : '—' }} @ {{ pos.takeProfitPrice ? price(pos.takeProfitPrice) : '—' }}</p>
            <p>Planned Risk {{ pos.initialRiskUsdt ? money(pos.initialRiskUsdt) : '—' }} ({{ pos.riskPerTradePercent ? pos.riskPerTradePercent + '%' : '—' }})</p>
            <p>Liquidation {{ pos.liquidationPrice ? price(pos.liquidationPrice) : '—' }}</p>
            <p class="tiny">Planned Risk is not Isolated Margin and not Notional.</p>
            <p [class]="pos.unrealizedPnL >= 0 ? 'pnl-pos' : 'pnl-neg'">Unrealized {{ signedMoney(pos.unrealizedPnL) }}</p>
          } @else {
            <p class="muted">No open position. Bots are monitoring this market.</p>
          }
          <h2 style="margin-top:16px">Risk</h2>
          <p class="tiny">{{ strategyBook() }} An open position keeps the stop written at fill.</p>
          <h2 style="margin-top:16px">Strategy parameters</h2>
          <p class="tiny">SL/TP come from the strategy risk on this bot.</p>
        </section>
      </section>
      <section class="mid-grid">
        <article class="panel">
          <h2>Signals</h2>
          @if (signals().length === 0) {
            <p class="muted">No signals yet.</p>
          } @else {
            <div class="watch-row watch-head">
              <app-sort-btn column="time" [query]="signalQuery">Time</app-sort-btn>
              <app-sort-btn column="type" [query]="signalQuery">Type</app-sort-btn>
              <app-sort-btn column="price" [query]="signalQuery" align="end">Price</app-sort-btn>
              <app-sort-btn column="reason" [query]="signalQuery">Reason</app-sort-btn>
            </div>
            @for (signal of visibleSignals(); track signal.id) {
              <div class="watch-row">
                <span>{{ formatTime(signal.timestamp) }}</span>
                <span>{{ signal.signalType }}</span>
                <span>{{ price(signal.price) }}</span>
                <span class="tiny">{{ signal.reason }}</span>
              </div>
            }
          }
        </article>
        <article class="panel">
          <h2>Recent trades</h2>
          @if (trades().length === 0) {
            <p class="muted">No trades yet.</p>
          } @else {
            <div class="watch-row watch-head">
              <app-sort-btn column="time" [query]="tradeQuery">Time</app-sort-btn>
              <app-sort-btn column="qty" [query]="tradeQuery" align="end">Qty</app-sort-btn>
              <app-sort-btn column="pnl" [query]="tradeQuery" align="end">PnL</app-sort-btn>
            </div>
            @for (trade of visibleTrades(); track trade.id) {
              <div class="watch-row">
                <span>{{ formatTime(trade.closedAt ?? trade.openedAt) }}</span>
                <span>{{ qty(trade.quantity) }}</span>
                <span [class]="trade.pnL >= 0 ? 'pnl-pos' : 'pnl-neg'">{{ signedMoney(trade.pnL) }}</span>
              </div>
            }
          }
        </article>
        <article class="panel">
          <h2>Logs</h2>
          <p class="tiny">{{ bot.lastError || 'Engine notes appear here when a cycle is skipped or blocked.' }}</p>
          <p class="tiny">Started {{ formatTime(bot.startedAt) }}</p>
        </article>
      </section>
    } @else {
      <section class="panel"><p class="muted">Bot not found.</p><a routerLink="/bots">Back to bots</a></section>
    }
    <app-confirm-modal
      [open]="confirmDelete()"
      title="Delete bot"
      [message]="'Remove ' + (bot()?.symbol ?? 'this bot') + ' from this workspace? It will stop if running.'"
      [warning]="ui.isLive() ? 'LIVE positions on Binance stay open.' : 'Paper positions stay open.'"
      confirmLabel="Delete"
      [danger]="true"
      (cancel)="confirmDelete.set(false)"
      (confirm)="deleteBot()"
    />
  `,
})
export class BotDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  readonly trading = inject(TradingService);
  readonly ui = inject(UiStateService);
  readonly botStatus = botStatus;
  readonly modeBadge = modeBadge;
  readonly price = price;
  readonly qty = qty;
  readonly money = money;
  readonly notionalUsdt = notionalUsdt;
  readonly signedMoney = signedMoney;
  readonly formatTime = formatTime;
  readonly confirmDelete = signal(false);
  readonly busy = signal(false);
  readonly bot = computed(() => this.trading.bots().find((item) => item.id === this.route.snapshot.paramMap.get('id')) ?? null);
  readonly strategyBook = computed(() => {
    const bot = this.bot();
    const row = this.trading.strategies().find((item) => item.id === bot?.strategyId);
    if (!row || row.stopLossPercent == null || row.takeProfitPercent == null) {
      return `${bot?.riskProfileName || 'Strategy risk'}.`;
    }
    return `SL ${row.stopLossPercent}% · TP ${row.takeProfitPercent}% · risk ${row.riskPerTradePercent ?? '—'}% · ${row.maxLeverage ?? '—'}x.`;
  });
  readonly position = computed(() => this.trading.positions().find((item) => item.botId === this.bot()?.id) ?? null);
  readonly signals = computed(() => this.trading.signals().filter((item) => item.botId === this.bot()?.id));
  readonly trades = computed(() => this.trading.trades().filter((item) => item.botId === this.bot()?.id));
  readonly signalQuery = new ListQuery();
  readonly tradeQuery = new ListQuery();
  readonly visibleSignals = computed(() =>
    this.signalQuery.apply(
      this.signals(),
      (row) => [row.signalType, row.reason],
      {
        time: (row) => timeValue(row.timestamp),
        type: (row) => row.signalType,
        price: (row) => row.price,
        reason: (row) => row.reason,
      },
    ),
  );
  readonly visibleTrades = computed(() =>
    this.tradeQuery.apply(
      this.trades(),
      (row) => [row.symbol, row.pnL],
      {
        time: (row) => timeValue(row.closedAt ?? row.openedAt),
        qty: (row) => row.quantity,
        pnl: (row) => row.pnL,
      },
    ),
  );

  constructor() {
    const symbol = this.bot()?.symbol ?? this.ui.selectedSymbol();
    void this.trading.loadKlines(symbol, '5m');
  }

  askDelete(): void {
    const bot = this.bot();
    if (!bot || !this.trading.belongsToWorkspace(bot)) {
      this.toast.show('Wrong workspace', 'Switch PAPER/LIVE to match this bot first.', 'error');
      return;
    }
    this.confirmDelete.set(true);
  }

  async deleteBot(): Promise<void> {
    const bot = this.bot();
    if (!bot) {
      return;
    }
    this.busy.set(true);
    try {
      await this.trading.deleteWorkspaceBot(bot);
      this.confirmDelete.set(false);
      await this.trading.refresh();
      this.toast.show('Bot deleted', `${bot.symbol} was removed. Positions were not closed.`, 'success');
      await this.router.navigateByUrl('/bots');
    } catch (error) {
      this.toast.show('Delete blocked', error instanceof Error ? error.message : 'Could not delete this bot.', 'error');
    } finally {
      this.busy.set(false);
    }
  }
}
