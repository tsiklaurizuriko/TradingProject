import { Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../icon/icon';
import { ListQuery, timeValue } from '../lists/list-query';
import { SortBtnComponent } from '../lists/list-tools';
import { ConfirmModalComponent, EmptyStateComponent, StatusBadgeComponent } from '../ui/ui-kit';
import { UiStateService } from '../../core/ui/ui-state.service';
import { ToastService } from '../../core/ui/toast.service';
import { TradingService } from '../../core/trading/trading.service';
import {
  BotDto,
  PositionDto,
  SignalDto,
  TradeDto,
  botStatus,
  groupPositionsByStrategy,
  isLongSide,
  money,
  pct,
  pnlClass,
  price,
  qty,
  sideLabel,
  signedMoney,
} from '../../core/trading/trading.models';

@Component({
  selector: 'app-bot-table',
  imports: [IconComponent, StatusBadgeComponent, EmptyStateComponent, RouterLink, SortBtnComponent],
  template: `
    <section class="panel panel-fill compact" [class.is-collapsed]="ui.isCollapsed('bots')">
      <div class="section-head">
        <button type="button" class="section-fold" (click)="ui.toggleCollapsed('bots')" [attr.aria-expanded]="!ui.isCollapsed('bots')">
          <app-icon name="chevron" [class.is-closed]="ui.isCollapsed('bots')" />
          <h2>{{ title() }} ({{ bots().length }})</h2>
        </button>
        <a class="tiny" routerLink="/bots">View all →</a>
      </div>
      @if (!ui.isCollapsed('bots')) {
        @if (bots().length === 0) {
          <app-empty-state title="No bots in this workspace" message="Start one coin from Bots." actionLabel="Start a Bot" actionLink="/bots" />
        } @else {
          <div class="table-scroll">
            <table class="data-table">
              <thead>
                <tr>
                  <th>#</th>
                  <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
                  <th><app-sort-btn column="tf" [query]="list">TF</app-sort-btn></th>
                  <th><app-sort-btn column="strategy" [query]="list">Strategy</app-sort-btn></th>
                  <th><app-sort-btn column="status" [query]="list">Status</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="pnl" [query]="list" align="end">PnL</app-sort-btn></th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (bot of visible(); track bot.id; let i = $index) {
                  <tr class="clickable" [routerLink]="['/bots', bot.id]">
                    <td>{{ i + 1 }}</td>
                    <td><strong>{{ bot.symbol }}</strong></td>
                    <td>{{ bot.timeframe }}</td>
                    <td>{{ bot.strategyName }}</td>
                    <td><app-status-badge [label]="status(bot.status).label" [cls]="status(bot.status).cls" /></td>
                    <td class="num" [class]="pnlClass(pnl(bot.id))">{{ signedMoney(pnl(bot.id)) }}</td>
                    <td>
                      <div class="icon-actions" (click)="$event.stopPropagation()">
                        <button type="button" title="Pause is not available; use Stop" disabled><app-icon name="pause" /></button>
                        @if (bot.status === 'Running') {
                          <button class="danger" type="button" title="Stop" (click)="stop.emit(bot)"><app-icon name="stop" /></button>
                        } @else {
                          <button type="button" title="Start" (click)="start.emit(bot)"><app-icon name="play" /></button>
                        }
                        <a [routerLink]="['/bots', bot.id]" title="Settings"><app-icon name="gear" /></a>
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </section>
  `,
})
export class BotTableComponent {
  readonly title = input('Active Bots');
  readonly bots = input<BotDto[]>([]);
  readonly positions = input<PositionDto[]>([]);
  readonly signals = input<SignalDto[]>([]);
  readonly start = output<BotDto>();
  readonly stop = output<BotDto>();
  readonly status = botStatus;
  readonly pnlClass = pnlClass;
  readonly signedMoney = signedMoney;
  readonly ui = inject(UiStateService);
  readonly list = new ListQuery();
  readonly visible = computed(() =>
    this.list.apply(
      this.bots(),
      (bot) => [bot.symbol, bot.displayName, bot.strategyName, bot.status, bot.timeframe],
      {
        symbol: (bot) => bot.symbol,
        tf: (bot) => bot.timeframe,
        strategy: (bot) => bot.strategyName,
        status: (bot) => bot.status,
        pnl: (bot) => this.pnl(bot.id),
      },
    ),
  );

  pnl(botId: string): number {
    return this.positions()
      .filter((item) => item.botId === botId)
      .reduce((sum, item) => sum + item.unrealizedPnL, 0);
  }
}

@Component({
  selector: 'app-position-table',
  imports: [RouterLink, IconComponent, SortBtnComponent, ConfirmModalComponent],
  template: `
    <section class="panel panel-fill compact" [class.is-collapsed]="!embedded() && ui.isCollapsed('positions')" [class.is-embedded]="embedded()">
      @if (!embedded()) {
      <div class="section-head">
        <button type="button" class="section-fold" (click)="ui.toggleCollapsed('positions')" [attr.aria-expanded]="!ui.isCollapsed('positions')">
          <app-icon name="chevron" [class.is-closed]="ui.isCollapsed('positions')" />
          <h2>Open Positions ({{ positions().length }})</h2>
        </button>
        <div class="section-head-meta">
          @if (positions().length) {
            <span class="tiny">Size {{ money(totalSize()) }} USDT</span>
          }
          <a class="tiny" routerLink="/positions">View all</a>
        </div>
      </div>
      }
      @if (embedded() || !ui.isCollapsed('positions')) {
        @if (positions().length === 0) {
          <div class="empty-state">
            <div class="empty-diamond" aria-hidden="true"></div>
            <strong>No active positions</strong>
            <p>Open Binance or bot positions for this workspace appear here.</p>
            <a class="btn sm" routerLink="/trading">View Market</a>
          </div>
        } @else {
          <div class="table-scroll">
            <table class="data-table">
              <thead>
                <tr>
                  <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
                  <th><app-sort-btn column="side" [query]="list">Side</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="entry" [query]="list" align="end">Entry</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="mark" [query]="list" align="end">Mark</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="size" [query]="list" align="end">Notional</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="risk" [query]="list" align="end">Planned Risk</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="margin" [query]="list" align="end">Margin</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="lev" [query]="list" align="end">Lev</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="pnl" [query]="list" align="end">PnL</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="pct" [query]="list" align="end">PnL %</app-sort-btn></th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (group of groups(); track group.key) {
                  <tr class="group-row">
                    <td colspan="11">
                      <strong>{{ group.name }}</strong>
                      <span class="tiny" style="margin-left:8px">{{ group.rows.length }}</span>
                      <span class="tiny" style="margin-left:8px" [class]="pnlClass(group.pnl)">{{ signedMoney(group.pnl) }}</span>
                    </td>
                  </tr>
                  @for (row of group.rows; track row.id) {
                    <tr>
                      <td><strong>{{ row.symbol }}</strong></td>
                      <td><span class="badge" [class.badge-long]="row.side === 'Buy' || row.side === 'Long'" [class.badge-short]="row.side !== 'Buy' && row.side !== 'Long'">{{ row.side === 'Buy' || row.side === 'Long' ? 'Isolated Long' : 'Isolated Short' }}</span></td>
                      <td class="num">{{ price(row.averageEntryPrice) }}</td>
                      <td class="num">{{ price(row.currentPrice) }}</td>
                      <td class="num">{{ money(size(row)) }}</td>
                      <td class="num">{{ row.initialRiskUsdt ? money(row.initialRiskUsdt) : '—' }}</td>
                      <td class="num">{{ row.marginUsdt ? money(row.marginUsdt) : '—' }}</td>
                      <td class="num">{{ row.leverage ? row.leverage + 'x' : '—' }}</td>
                      <td class="num" [class]="pnlClass(row.unrealizedPnL)"><strong>{{ signedMoney(row.unrealizedPnL) }}</strong></td>
                      <td class="num" [class]="pnlClass(change(row))">{{ pct(change(row)) }}</td>
                      <td>
                        <button
                          class="btn sm"
                          type="button"
                          [class.danger]="ui.isLive()"
                          [disabled]="busyId() === row.id"
                          (click)="requestClose(row)"
                        >{{ busyId() === row.id ? 'Closing…' : 'Close' }}</button>
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        }
      }
    </section>
    <app-confirm-modal
      [open]="!!pending()"
      [title]="'Close live position'"
      [message]="pending() ? 'Flatten ' + pending()!.symbol + ' and store the exit as a bot close.' : ''"
      [warning]="ui.isLive() ? 'This sends a real reduce-only market order on Binance.' : ''"
      [confirmLabel]="ui.isLive() ? 'Close on Binance' : 'Close position'"
      [danger]="ui.isLive()"
      (cancel)="pending.set(null)"
      (confirm)="confirmClose()"
    />
  `,
})
export class PositionTableComponent {
  readonly positions = input<PositionDto[]>([]);
  readonly embedded = input(false);
  readonly price = price;
  readonly pct = pct;
  readonly pnlClass = pnlClass;
  readonly signedMoney = signedMoney;
  readonly money = money;
  readonly ui = inject(UiStateService);
  private readonly trading = inject(TradingService);
  private readonly toast = inject(ToastService);
  readonly pending = signal<PositionDto | null>(null);
  readonly busyId = signal<string | null>(null);
  readonly list = new ListQuery();
  readonly visible = computed(() =>
    this.list.apply(
      this.positions(),
      (row) => [row.symbol, row.side],
      {
        opened: (row) => timeValue(row.openedAt),
        symbol: (row) => row.symbol,
        side: (row) => row.side,
        entry: (row) => row.averageEntryPrice,
        mark: (row) => row.currentPrice,
        size: (row) => this.size(row),
        risk: (row) => row.initialRiskUsdt ?? 0,
        margin: (row) => row.marginUsdt ?? 0,
        lev: (row) => row.leverage ?? 0,
        pnl: (row) => row.unrealizedPnL,
        pct: (row) => this.change(row),
      },
    ),
  );
  readonly groups = computed(() => {
    const sorted = this.visible();
    const order = new Map(sorted.map((row, index) => [row.id, index]));
    return groupPositionsByStrategy(sorted, this.trading.workspaceBots()).map((group) => ({
      ...group,
      rows: [...group.rows].sort((a, b) => (order.get(a.id) ?? 0) - (order.get(b.id) ?? 0)),
    }));
  });
  size(row: PositionDto): number {
    return row.notionalUsdt || (row.quantity ?? 0) * (row.averageEntryPrice ?? 0);
  }
  totalSize(): number {
    return this.positions().reduce((sum, row) => sum + this.size(row), 0);
  }
  change(row: PositionDto): number {
    if (!row.averageEntryPrice) {
      return 0;
    }
    const dir = row.side === 'Sell' || row.side === 'Short' ? -1 : 1;
    return ((row.currentPrice - row.averageEntryPrice) / row.averageEntryPrice) * 100 * dir;
  }
  requestClose(row: PositionDto): void {
    this.pending.set(row);
  }
  async confirmClose(): Promise<void> {
    const row = this.pending();
    if (!row) {
      return;
    }
    this.pending.set(null);
    this.busyId.set(row.id);
    try {
      await this.trading.closePosition(row.id);
      await this.trading.refresh();
      await this.trading.refreshPerformance();
      this.toast.show(
        'Close submitted. The position stays open until Binance reports the fill.',
        `${row.symbol} was flattened and stored as a bot exit.`,
        this.ui.isLive() ? 'error' : 'success',
      );
    } catch (error) {
      this.toast.show('Close blocked', error instanceof Error ? error.message : 'Could not close this position.', 'error');
    } finally {
      this.busyId.set(null);
    }
  }
}

@Component({
  selector: 'app-trade-table',
  imports: [EmptyStateComponent, RouterLink, IconComponent, SortBtnComponent],
  template: `
    <section class="panel panel-fill compact" [class.is-collapsed]="!embedded() && ui.isCollapsed('trades')" [class.is-embedded]="embedded()">
      @if (!embedded()) {
      <div class="section-head">
        <button type="button" class="section-fold" (click)="ui.toggleCollapsed('trades')" [attr.aria-expanded]="!ui.isCollapsed('trades')">
          <app-icon name="chevron" [class.is-closed]="ui.isCollapsed('trades')" />
          <h2>Position History</h2>
        </button>
        <a class="tiny" routerLink="/trades">View all</a>
      </div>
      }
      @if (embedded() || !ui.isCollapsed('trades')) {
        @if (trades().length === 0) {
          <app-empty-state title="No closed positions" message="Closed Isolated round-trips for this workspace appear here." />
        } @else {
          <div class="table-scroll">
            <table class="data-table">
              <thead>
                <tr>
                  <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
                  <th><app-sort-btn column="side" [query]="list">Side</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="qty" [query]="list" align="end">Vol</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="entry" [query]="list" align="end">Entry</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="exit" [query]="list" align="end">Avg Close</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="pnl" [query]="list" align="end">PnL</app-sort-btn></th>
                  <th class="num"><app-sort-btn column="pnlPct" [query]="list" align="end">PnL %</app-sort-btn></th>
                </tr>
              </thead>
              <tbody>
                @for (row of visible(); track row.id) {
                  <tr>
                    <td><strong>{{ row.symbol }}</strong></td>
                    <td><span class="badge" [class.badge-long]="isLongSide(row.side)" [class.badge-short]="!isLongSide(row.side)">{{ sideLabel(row.side) }}</span></td>
                    <td class="num">{{ qty(row.quantity) }}</td>
                    <td class="num">{{ price(row.entryPrice) }}</td>
                    <td class="num">{{ price(row.exitPrice) }}</td>
                    <td class="num" [class]="pnlClass(row.pnL)">{{ signedMoney(row.pnL) }}</td>
                    <td class="num" [class]="pnlClass(row.pnLPercent)">{{ row.closedAt ? pct(row.pnLPercent) : '—' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </section>
  `,
})
export class TradeTableComponent {
  readonly trades = input<TradeDto[]>([]);
  readonly embedded = input(false);
  readonly isLongSide = isLongSide;
  readonly sideLabel = sideLabel;
  readonly qty = qty;
  readonly price = price;
  readonly pnlClass = pnlClass;
  readonly signedMoney = signedMoney;
  readonly pct = pct;
  readonly ui = inject(UiStateService);
  readonly list = new ListQuery();
  readonly visible = computed(() =>
    this.list.apply(
      this.trades().filter((row) => !!row.closedAt),
      (row) => [row.symbol],
      {
        time: (row) => timeValue(row.closedAt ?? row.openedAt),
        symbol: (row) => row.symbol,
        side: (row) => sideLabel(row.side),
        qty: (row) => row.quantity,
        entry: (row) => row.entryPrice,
        exit: (row) => row.exitPrice,
        pnl: (row) => row.pnL,
        pnlPct: (row) => row.pnLPercent,
      },
    ),
  );
}
