import { Component, computed, inject, signal } from '@angular/core';
import { TradingService } from '../../core/trading/trading.service';
import { PositionDto, feeCash, formatTime, groupPositionsByStrategy, holdDuration, isolatedRoi, isLongSide, money, modeBadge, notionalUsdt, pct, pnlClass, price, qty, sideLabel, signedMoney } from '../../core/trading/trading.models';
import { ToastService } from '../../core/ui/toast.service';
import { UiStateService } from '../../core/ui/ui-state.service';
import { ListQuery, timeValue } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { IconComponent } from '../../shared/icon/icon';
import { ConfirmModalComponent, EmptyStateComponent } from '../../shared/ui/ui-kit';
import { LedgerBookComponent, LedgerBookTab } from '../../shared/tables/ledger-book';

@Component({
  selector: 'app-orders-page',
  imports: [LedgerBookComponent],
  template: `
    <app-ledger-book
      [orders]="trading.workspaceOrders()"
      initialTab="open"
      [allowedTabs]="orderTabs"
      [showViewAll]="false"
    />
  `,
})
export class OrdersPage {
  readonly trading = inject(TradingService);
  readonly orderTabs: LedgerBookTab[] = ['open', 'history'];

  constructor() {
    void this.trading.loadLedgerBook();
  }
}

@Component({
  selector: 'app-fills-page',
  imports: [LedgerBookComponent],
  template: `
    <app-ledger-book
      [orders]="trading.workspaceOrders()"
      initialTab="fills"
      [allowedTabs]="fillTabs"
      [showViewAll]="false"
    />
  `,
})
export class FillsPage {
  readonly trading = inject(TradingService);
  readonly fillTabs: LedgerBookTab[] = ['fills'];

  constructor() {
    void this.trading.loadLedgerBook();
  }
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
                    <th class="num"><app-sort-btn column="roi" [query]="list" align="end">ROI</app-sort-btn></th>
                    <th><app-sort-btn column="mode" [query]="list">Mode</app-sort-btn></th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of group.rows; track row.id) {
                    <tr>
                      <td>{{ row.symbol }}</td>
                      <td><span class="badge" [class.badge-long]="row.side === 'Buy' || row.side === 'Long'" [class.badge-short]="row.side !== 'Buy' && row.side !== 'Long'">{{ row.side === 'Buy' || row.side === 'Long' ? 'Isolated Long' : 'Isolated Short' }}</span></td>
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
                      <td class="num" [class]="pnlClass(row.unrealizedPnL)">{{ isolatedRoi(row.unrealizedPnL, row.marginUsdt) || '—' }}</td>
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
  readonly isolatedRoi = isolatedRoi;
  readonly list = new ListQuery();
  readonly pending = signal<PositionDto | null>(null);
  readonly busyId = signal<string | null>(null);
  readonly groups = computed(() => {
    const sort = {
      opened: (row: PositionDto) => timeValue(row.openedAt),
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
      roi: (row: PositionDto) => row.marginUsdt ? row.unrealizedPnL / row.marginUsdt : 0,
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
    <header class="page-header"><p>{{ trading.workspace() }} closed Isolated positions — one row per round-trip, like Binance Position History.</p></header>
    <section class="panel">
      @if (rows().length === 0) {
        <app-empty-state title="No trades yet" message="Closed Isolated positions for this workspace appear here." />
      } @else {
        <table class="data-table">
          <thead>
            <tr>
              <th><app-sort-btn column="opened" [query]="list">Opened</app-sort-btn></th>
              <th><app-sort-btn column="closed" [query]="list">Closed</app-sort-btn></th>
              <th><app-sort-btn column="hold" [query]="list">Duration</app-sort-btn></th>
              <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
              <th><app-sort-btn column="side" [query]="list">Side</app-sort-btn></th>
              <th class="num"><app-sort-btn column="qty" [query]="list" align="end">Closed Vol</app-sort-btn></th>
              <th class="num"><app-sort-btn column="entry" [query]="list" align="end">Entry</app-sort-btn></th>
              <th class="num"><app-sort-btn column="exit" [query]="list" align="end">Avg Close</app-sort-btn></th>
              <th class="num"><app-sort-btn column="pnl" [query]="list" align="end">Realized PnL</app-sort-btn></th>
              <th class="num"><app-sort-btn column="pnlPct" [query]="list" align="end">PnL %</app-sort-btn></th>
              <th class="num"><app-sort-btn column="fee" [query]="list" align="end">Fee</app-sort-btn></th>
              <th><app-sort-btn column="mode" [query]="list">Mode</app-sort-btn></th>
              <th><app-sort-btn column="status" [query]="list">Status</app-sort-btn></th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.id) {
              <tr>
                <td>{{ formatTime(row.openedAt) }}</td>
                <td>{{ formatTime(row.closedAt) }}</td>
                <td>{{ holdDuration(row.openedAt, row.closedAt) }}</td>
                <td>{{ row.symbol }}</td>
                <td><span class="badge" [class.badge-long]="isLongSide(row.side)" [class.badge-short]="!isLongSide(row.side)">{{ sideLabel(row.side) }}</span></td>
                <td class="num">{{ qty(row.quantity) }}</td>
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
  readonly holdDuration = holdDuration;
  readonly isLongSide = isLongSide;
  readonly sideLabel = sideLabel;
  readonly qty = qty;
  readonly price = price;
  readonly signedMoney = signedMoney;
  readonly pnlClass = pnlClass;
  readonly feeCash = feeCash;
  readonly pct = pct;
  readonly modeBadge = modeBadge;
  readonly list = new ListQuery();

  constructor() {
    void this.trading.loadLedgerBook();
  }
  readonly rows = computed(() =>
    this.list.apply(
      this.trading.workspaceTrades().filter((row) => !!row.closedAt),
      (row) => [row.symbol, sideLabel(row.side), row.closedAt ? 'Closed' : 'Open'],
      {
        opened: (row) => timeValue(row.openedAt),
        closed: (row) => timeValue(row.closedAt),
        hold: (row) => timeValue(row.closedAt) - timeValue(row.openedAt),
        symbol: (row) => row.symbol,
        side: (row) => sideLabel(row.side),
        qty: (row) => row.quantity,
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
