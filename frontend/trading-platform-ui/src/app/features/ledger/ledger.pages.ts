import { Component, computed, inject, signal } from '@angular/core';
import { TradingService } from '../../core/trading/trading.service';
import { PositionDto, feeCash, formatTime, groupPositionsByStrategy, isProtectionOrder, money, modeBadge, notionalUsdt, orderKindLabel, orderStatusLabel, pct, pnlClass, price, qty, signedMoney } from '../../core/trading/trading.models';
import { ToastService } from '../../core/ui/toast.service';
import { UiStateService } from '../../core/ui/ui-state.service';
import { ListQuery, timeValue } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { IconComponent } from '../../shared/icon/icon';
import { ConfirmModalComponent, EmptyStateComponent } from '../../shared/ui/ui-kit';

@Component({
  selector: 'app-orders-page',
  imports: [EmptyStateComponent, SortBtnComponent],
  template: `
    <section class="panel">
      <div class="section-head" style="margin:0 0 14px">
        <div class="tabs" role="tablist">
          <button type="button" [class.is-on]="tab() === 'fills'" (click)="tab.set('fills')">Fills ({{ fillCount() }})</button>
          <button type="button" [class.is-on]="tab() === 'protection'" (click)="tab.set('protection')">Stops / TP ({{ protectionCount() }})</button>
          <button type="button" [class.is-on]="tab() === 'all'" (click)="tab.set('all')">All ({{ trading.workspaceOrders().length }})</button>
        </div>
      </div>
      @if (visible().length === 0) {
        <app-empty-state
          [title]="tab() === 'protection' ? 'No working stops' : 'No orders yet'"
          [message]="tab() === 'protection'
            ? 'Isolated stop and take-profit waiting on Binance appear here.'
            : 'Orders for the current header mode appear here.'"
        />
      } @else {
        <table class="data-table">
          <thead>
            <tr>
              <th><app-sort-btn column="created" [query]="list">Created</app-sort-btn></th>
              <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
              <th><app-sort-btn column="kind" [query]="list">Kind</app-sort-btn></th>
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
                <td><span class="badge" [class.badge-paused]="isProtectionOrder(row)" [class.badge-ok]="!isProtectionOrder(row)">{{ orderKindLabel(row) }}</span></td>
                <td><span class="badge" [class.badge-long]="row.side === 'Buy'" [class.badge-short]="row.side !== 'Buy'">{{ row.side }}</span></td>
                <td>{{ orderStatusLabel(row) }}</td>
                <td class="num">{{ qty(row.quantity) }}</td>
                <td class="num">{{ price(row.price) }}</td>
                <td class="num" [class]="pnlClass(row.pnL)">{{ signedMoney(row.pnL) }}</td>
                <td class="num" [class]="pnlClass(feeCash(row.fee))">{{ signedMoney(feeCash(row.fee), 4) }}</td>
                <td><span class="badge" [class]="modeBadge(row.mode || trading.workspace())">{{ row.mode || trading.workspace() }}</span></td>
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
  readonly feeCash = feeCash;
  readonly modeBadge = modeBadge;
  readonly isProtectionOrder = isProtectionOrder;
  readonly orderKindLabel = orderKindLabel;
  readonly orderStatusLabel = orderStatusLabel;
  readonly list = new ListQuery();
  readonly tab = signal<'fills' | 'protection' | 'all'>('all');
  readonly fillCount = computed(() => this.trading.workspaceOrders().filter((row) => !isProtectionOrder(row)).length);
  readonly protectionCount = computed(() => this.trading.workspaceOrders().filter((row) => isProtectionOrder(row)).length);
  readonly visible = computed(() => {
    const rows = this.trading.workspaceOrders();
    const tab = this.tab();
    if (tab === 'fills') {
      return rows.filter((row) => !isProtectionOrder(row));
    }
    if (tab === 'protection') {
      return rows.filter((row) => isProtectionOrder(row));
    }
    return rows;
  });
  readonly rows = computed(() =>
    this.list.apply(
      this.visible(),
      (row) => [row.symbol, row.side, row.status, row.exchangeOrderId, orderKindLabel(row)],
      {
        created: (row) => timeValue(row.createdAt),
        symbol: (row) => row.symbol,
        kind: (row) => orderKindLabel(row),
        side: (row) => row.side,
        status: (row) => orderStatusLabel(row),
        qty: (row) => row.quantity,
        price: (row) => row.price,
        pnl: (row) => row.pnL,
        fee: (row) => row.fee,
        mode: (row) => row.mode || this.trading.workspace(),
        id: (row) => row.exchangeOrderId,
      },
    ),
  );
}

@Component({
  selector: 'app-positions-page',
  imports: [EmptyStateComponent, SortBtnComponent, ConfirmModalComponent, IconComponent],
  template: `
    @if (trading.workspacePositions().length === 0) {
      <section class="panel">
        <app-empty-state title="No active positions" message="No open position in this workspace." actionLabel="View Market" actionLink="/trading" />
      </section>
    } @else {
      <div style="display:grid;gap:12px">
        @for (group of groups(); track group.key) {
          <section class="panel" [class.is-collapsed]="ui.isCollapsed(groupKey(group.key))">
            <div class="section-head">
              <button type="button" class="section-fold" (click)="ui.toggleCollapsed(groupKey(group.key))" [attr.aria-expanded]="!ui.isCollapsed(groupKey(group.key))">
                <app-icon name="chevron" [class.is-closed]="ui.isCollapsed(groupKey(group.key))" />
                <h2>{{ group.name }} ({{ group.rows.length }})</h2>
              </button>
              <div class="section-head-meta">
                <span class="tiny">{{ money(group.notional) }} notional</span>
                <span class="tiny" [class]="pnlClass(group.pnl)">{{ signedMoney(group.pnl) }}</span>
              </div>
            </div>
            @if (!ui.isCollapsed(groupKey(group.key))) {
              <table class="data-table">
                <thead>
                  <tr>
                    <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
                    <th><app-sort-btn column="side" [query]="list">Side</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="entry" [query]="list" align="end">Entry</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="mark" [query]="list" align="end">Mark</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="qty" [query]="list" align="end">Qty</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="size" [query]="list" align="end">Notional</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="risk" [query]="list" align="end">Planned Risk</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="margin" [query]="list" align="end">Margin</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="lev" [query]="list" align="end">Lev</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="sl" [query]="list" align="end">SL</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="tp" [query]="list" align="end">TP</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="liq" [query]="list" align="end">Liq</app-sort-btn></th>
                    <th>Margin mode</th>
                    <th class="num"><app-sort-btn column="pnl" [query]="list" align="end">PnL</app-sort-btn></th>
                    <th><app-sort-btn column="mode" [query]="list">Mode</app-sort-btn></th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of group.rows; track row.id) {
                    <tr>
                      <td>{{ row.symbol }}</td>
                      <td><span class="badge" [class.badge-long]="row.side === 'Buy' || row.side === 'Long'" [class.badge-short]="row.side !== 'Buy' && row.side !== 'Long'">{{ row.side === 'Buy' ? 'LONG' : row.side }}</span></td>
                      <td class="num">{{ price(row.averageEntryPrice) }}</td>
                      <td class="num">{{ price(row.currentPrice) }}</td>
                      <td class="num">{{ qty(row.quantity) }}</td>
                      <td class="num">{{ money(row.notionalUsdt || notionalUsdt(row.quantity, row.averageEntryPrice)) }}</td>
                      <td class="num">{{ row.initialRiskUsdt ? money(row.initialRiskUsdt) : '—' }}</td>
                      <td class="num">{{ row.marginUsdt ? money(row.marginUsdt) : '—' }}</td>
                      <td class="num">{{ row.leverage ? row.leverage + 'x' : '—' }}</td>
                      <td class="num">{{ row.stopLossPercent ? row.stopLossPercent + '%' : '—' }}{{ row.stopLossPrice ? ' @ ' + price(row.stopLossPrice) : '' }}</td>
                      <td class="num">{{ row.takeProfitPercent ? row.takeProfitPercent + '%' : '—' }}{{ row.takeProfitPrice ? ' @ ' + price(row.takeProfitPrice) : '' }}</td>
                      <td class="num">{{ row.liquidationPrice ? price(row.liquidationPrice) : '—' }}</td>
                      <td>Isolated</td>
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
        }
      </div>
    }
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
  readonly groups = computed(() => {
    const sort = {
      symbol: (row: PositionDto) => row.symbol,
      side: (row: PositionDto) => row.side,
      entry: (row: PositionDto) => row.averageEntryPrice,
      mark: (row: PositionDto) => row.currentPrice,
      qty: (row: PositionDto) => row.quantity,
      size: (row: PositionDto) => row.notionalUsdt || notionalUsdt(row.quantity, row.averageEntryPrice),
      risk: (row: PositionDto) => row.initialRiskUsdt ?? 0,
      margin: (row: PositionDto) => row.marginUsdt ?? 0,
      lev: (row: PositionDto) => row.leverage ?? 0,
      sl: (row: PositionDto) => row.stopLossPercent ?? 0,
      tp: (row: PositionDto) => row.takeProfitPercent ?? 0,
      liq: (row: PositionDto) => row.liquidationPrice ?? 0,
      pnl: (row: PositionDto) => row.unrealizedPnL,
      mode: () => this.trading.workspace(),
    };
    return groupPositionsByStrategy(this.trading.workspacePositions(), this.trading.workspaceBots()).map((group) => ({
      ...group,
      rows: this.list.apply(group.rows, (row) => [row.symbol, row.side], sort),
    }));
  });

  groupKey(key: string): string {
    return `positions-strategy-${key}`;
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
              <th class="num"><app-sort-btn column="pnlPct" [query]="list" align="end">PnL %</app-sort-btn></th>
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
                <td class="num" [class]="pnlClass(row.pnL)">{{ signedMoney(row.pnL) }}</td>
                <td class="num" [class]="pnlClass(row.pnLPercent)">{{ pct(row.pnLPercent) }}</td>
                <td class="num" [class]="pnlClass(feeCash(row.fees))">{{ signedMoney(feeCash(row.fees), 4) }}</td>
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
  readonly feeCash = feeCash;
  readonly pct = pct;
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
        pnlPct: (row) => row.pnLPercent,
        fee: (row) => row.fees,
        mode: () => this.trading.workspace(),
        status: (row) => (row.closedAt ? 'Closed' : 'Open'),
      },
    ),
  );
}
