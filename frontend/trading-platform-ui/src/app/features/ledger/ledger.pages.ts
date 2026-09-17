import { Component, computed, inject, signal } from '@angular/core';
import { TradingService } from '../../core/trading/trading.service';
import { PositionDto, formatTime, money, modeBadge, notionalUsdt, pct, pnlClass, price, qty, signedMoney } from '../../core/trading/trading.models';
import { ToastService } from '../../core/ui/toast.service';
import { UiStateService } from '../../core/ui/ui-state.service';
import { ListQuery, timeValue } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { ConfirmModalComponent, EmptyStateComponent } from '../../shared/ui/ui-kit';

@Component({
  selector: 'app-orders-page',
  imports: [EmptyStateComponent, SortBtnComponent],
  template: `
    <section class="panel">
      @if (trading.workspaceOrders().length === 0) {
        <app-empty-state title="No orders yet" message="Orders for the current header mode appear here." />
      } @else {
        <table class="data-table">
          <thead>
            <tr>
              <th><app-sort-btn column="created" [query]="list">Created</app-sort-btn></th>
              <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
              <th><app-sort-btn column="side" [query]="list">Side</app-sort-btn></th>
              <th><app-sort-btn column="status" [query]="list">Status</app-sort-btn></th>
              <th class="num"><app-sort-btn column="qty" [query]="list" align="end">Qty</app-sort-btn></th>
              <th class="num"><app-sort-btn column="price" [query]="list" align="end">Price</app-sort-btn></th>
              <th class="num"><app-sort-btn column="pnl" [query]="list" align="end">PnL</app-sort-btn></th>
              <th class="num"><app-sort-btn column="fee" [query]="list" align="end">Fee</app-sort-btn></th>
              <th><app-sort-btn column="mode" [query]="list">Mode</app-sort-btn></th>
              <th><app-sort-btn column="id" [query]="list">Id</app-sort-btn></th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.id) {
              <tr>
                <td>{{ formatTime(row.createdAt) }}</td>
                <td>{{ row.symbol }}</td>
                <td><span class="badge" [class.badge-long]="row.side === 'Buy'" [class.badge-short]="row.side !== 'Buy'">{{ row.side }}</span></td>
                <td>{{ row.status }}</td>
                <td class="num">{{ qty(row.quantity) }}</td>
                <td class="num">{{ price(row.price) }}</td>
                <td class="num" [class]="pnlClass(row.pnL)">{{ signedMoney(row.pnL) }}</td>
                <td class="num">{{ row.fee == null ? '—' : money(row.fee, 4) }}</td>
                <td><span class="badge" [class]="modeBadge(trading.workspace())">{{ trading.workspace() }}</span></td>
                <td class="tiny">{{ row.exchangeOrderId }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
})
export class OrdersPage {
  readonly trading = inject(TradingService);
  readonly formatTime = formatTime;
  readonly qty = qty;
  readonly price = price;
  readonly signedMoney = signedMoney;
  readonly pnlClass = pnlClass;
  readonly money = money;
  readonly modeBadge = modeBadge;
  readonly list = new ListQuery();
  readonly rows = computed(() =>
    this.list.apply(
      this.trading.workspaceOrders(),
      (row) => [row.symbol, row.side, row.status, row.exchangeOrderId],
      {
        created: (row) => timeValue(row.createdAt),
        symbol: (row) => row.symbol,
        side: (row) => row.side,
        status: (row) => row.status,
        qty: (row) => row.quantity,
        price: (row) => row.price,
        pnl: (row) => row.pnL,
        fee: (row) => row.fee,
        mode: () => this.trading.workspace(),
        id: (row) => row.exchangeOrderId,
      },
    ),
  );
}

@Component({
  selector: 'app-positions-page',
  imports: [EmptyStateComponent, SortBtnComponent, ConfirmModalComponent],
  template: `
    <section class="panel">
      @if (trading.workspacePositions().length === 0) {
        <app-empty-state title="No active positions" message="No open position in this workspace." actionLabel="View Market" actionLink="/trading" />
      } @else {
        <table class="data-table">
          <thead>
            <tr>
              <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
              <th><app-sort-btn column="side" [query]="list">Side</app-sort-btn></th>
              <th class="num"><app-sort-btn column="entry" [query]="list" align="end">Entry</app-sort-btn></th>
              <th class="num"><app-sort-btn column="mark" [query]="list" align="end">Mark</app-sort-btn></th>
              <th class="num"><app-sort-btn column="qty" [query]="list" align="end">Qty</app-sort-btn></th>
              <th class="num"><app-sort-btn column="size" [query]="list" align="end">Size USDT</app-sort-btn></th>
              <th class="num"><app-sort-btn column="pnl" [query]="list" align="end">PnL</app-sort-btn></th>
              <th><app-sort-btn column="mode" [query]="list">Mode</app-sort-btn></th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.id) {
              <tr>
                <td>{{ row.symbol }}</td>
                <td><span class="badge badge-long">{{ row.side === 'Buy' ? 'LONG' : row.side }}</span></td>
                <td class="num">{{ price(row.averageEntryPrice) }}</td>
                <td class="num">{{ price(row.currentPrice) }}</td>
                <td class="num">{{ qty(row.quantity) }}</td>
                <td class="num">{{ money(notionalUsdt(row.quantity, row.averageEntryPrice)) }}</td>
                <td class="num" [class]="pnlClass(row.unrealizedPnL)">{{ signedMoney(row.unrealizedPnL) }}</td>
                <td><span class="badge" [class]="modeBadge(trading.workspace())">{{ trading.workspace() }}</span></td>
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
          </tbody>
        </table>
      }
    </section>
    <app-confirm-modal
      [open]="!!pending()"
      [title]="ui.isLive() ? 'Close LIVE position' : 'Close paper position'"
      [message]="pending() ? (ui.isLive()
        ? 'Flatten ' + pending()!.symbol + ' on Binance with a reduce-only market order. The exit is stored as a bot close.'
        : 'Flatten ' + pending()!.symbol + ' in paper. The exit is stored as a bot close.') : ''"
      [warning]="ui.isLive() ? 'This sends a real Binance USD-M order. Size and fill are live.' : ''"
      [confirmLabel]="ui.isLive() ? 'Close on Binance' : 'Close position'"
      [danger]="ui.isLive()"
      (cancel)="pending.set(null)"
      (confirm)="confirmClose()"
    />
  `,
})
export class PositionsPage {
  readonly trading = inject(TradingService);
  readonly ui = inject(UiStateService);
  private readonly toast = inject(ToastService);
  readonly price = price;
  readonly qty = qty;
  readonly money = money;
  readonly notionalUsdt = notionalUsdt;
  readonly signedMoney = signedMoney;
  readonly pnlClass = pnlClass;
  readonly modeBadge = modeBadge;
  readonly list = new ListQuery();
  readonly pending = signal<PositionDto | null>(null);
  readonly busyId = signal<string | null>(null);
  readonly rows = computed(() =>
    this.list.apply(
      this.trading.workspacePositions(),
      (row) => [row.symbol, row.side],
      {
        symbol: (row) => row.symbol,
        side: (row) => row.side,
        entry: (row) => row.averageEntryPrice,
        mark: (row) => row.currentPrice,
        qty: (row) => row.quantity,
        size: (row) => notionalUsdt(row.quantity, row.averageEntryPrice),
        pnl: (row) => row.unrealizedPnL,
        mode: () => this.trading.workspace(),
      },
    ),
  );

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
        this.ui.isLive() ? 'LIVE position closed' : 'Paper position closed',
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
  selector: 'app-trades-page',
  imports: [EmptyStateComponent, SortBtnComponent],
  template: `
    <header class="page-header"><p>{{ trading.workspace() }} closed trades only.</p></header>
    <section class="panel">
      @if (trading.workspaceTrades().length === 0) {
        <app-empty-state title="No trades yet" message="Closed fills for this workspace appear here." />
      } @else {
        <table class="data-table">
          <thead>
            <tr>
              <th><app-sort-btn column="time" [query]="list">Time</app-sort-btn></th>
              <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
              <th><app-sort-btn column="side" [query]="list">Side</app-sort-btn></th>
              <th class="num"><app-sort-btn column="entry" [query]="list" align="end">Entry</app-sort-btn></th>
              <th class="num"><app-sort-btn column="exit" [query]="list" align="end">Exit</app-sort-btn></th>
              <th class="num"><app-sort-btn column="pnl" [query]="list" align="end">PnL</app-sort-btn></th>
              <th class="num"><app-sort-btn column="fee" [query]="list" align="end">Fee</app-sort-btn></th>
              <th><app-sort-btn column="mode" [query]="list">Mode</app-sort-btn></th>
              <th><app-sort-btn column="status" [query]="list">Status</app-sort-btn></th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.id) {
              <tr>
                <td>{{ formatTime(row.closedAt ?? row.openedAt) }}</td>
                <td>{{ row.symbol }}</td>
                <td><span class="badge badge-long">LONG</span></td>
                <td class="num">{{ price(row.entryPrice) }}</td>
                <td class="num">{{ price(row.exitPrice) }}</td>
                <td class="num" [class]="pnlClass(row.pnL)">{{ signedMoney(row.pnL) }} · {{ pct(row.pnLPercent) }}</td>
                <td class="num">{{ money(row.fees, 4) }}</td>
                <td><span class="badge" [class]="modeBadge(trading.workspace())">{{ trading.workspace() }}</span></td>
                <td>{{ row.closedAt ? 'Closed' : 'Open' }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
})
export class TradesPage {
  readonly trading = inject(TradingService);
  readonly formatTime = formatTime;
  readonly price = price;
  readonly signedMoney = signedMoney;
  readonly pnlClass = pnlClass;
  readonly pct = pct;
  readonly money = money;
  readonly modeBadge = modeBadge;
  readonly list = new ListQuery();
  readonly rows = computed(() =>
    this.list.apply(
      this.trading.workspaceTrades(),
      (row) => [row.symbol, row.closedAt ? 'Closed' : 'Open'],
      {
        time: (row) => timeValue(row.closedAt ?? row.openedAt),
        symbol: (row) => row.symbol,
        side: () => 'LONG',
        entry: (row) => row.entryPrice,
        exit: (row) => row.exitPrice,
        pnl: (row) => row.pnL,
        fee: (row) => row.fees,
        mode: () => this.trading.workspace(),
        status: (row) => (row.closedAt ? 'Closed' : 'Open'),
      },
    ),
  );
}
