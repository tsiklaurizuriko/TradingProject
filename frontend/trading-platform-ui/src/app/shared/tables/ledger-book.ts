import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ListQuery, timeValue } from '../lists/list-query';
import { SortBtnComponent } from '../lists/list-tools';
import { EmptyStateComponent } from '../ui/ui-kit';
import { UiStateService } from '../../core/ui/ui-state.service';
import {
  OrderDto,
  PositionDto,
  TradeDto,
  feeCash,
  formatTime,
  isHistoryOrder,
  isProtectionOrder,
  isWorkingOrder,
  orderKindLabel,
  orderStatusLabel,
  pnlClass,
  price,
  qty,
  signedMoney,
  tradeHistoryFills,
} from '../../core/trading/trading.models';
import { PositionTableComponent, TradeTableComponent } from './tables';

export type LedgerBookTab = 'positions' | 'open' | 'history' | 'fills' | 'closed';

@Component({
  selector: 'app-ledger-book',
  imports: [EmptyStateComponent, SortBtnComponent, RouterLink, PositionTableComponent, TradeTableComponent],
  template: `
    <section class="panel panel-fill compact ledger-book">
      <div class="section-head ledger-book-head">
        @if (showBookTabs()) {
          <div class="tabs" role="tablist">
            @for (item of visibleTabs(); track item.id) {
              <button type="button" [class.is-on]="tab() === item.id" (click)="tab.set(item.id)">{{ item.label }}</button>
            }
          </div>
        }
        <div class="section-head-meta">
          <label class="tiny ledger-coin-filter">
            <input type="checkbox" [checked]="coinOnly()" (change)="coinOnly.set($any($event.target).checked)" />
            This coin only
          </label>
          @if (showViewAll()) {
            <a class="tiny" [routerLink]="viewAllLink()">View all</a>
          }
        </div>
      </div>
      @if (tab() === 'open') {
        <div class="tabs ledger-subtabs" role="tablist">
          <button type="button" [class.is-on]="openKind() === 'basic'" (click)="openKind.set('basic')">Basic ({{ basicOpen().length }})</button>
          <button type="button" [class.is-on]="openKind() === 'conditional'" (click)="openKind.set('conditional')">Conditional ({{ conditionalOpen().length }})</button>
        </div>
      }
      @if (tab() === 'positions') {
        <app-position-table [positions]="visiblePositions()" [embedded]="true" />
      } @else if (tab() === 'closed') {
        <app-trade-table [trades]="closedTrades()" [embedded]="true" />
      } @else if (orderRows().length === 0) {
        <app-empty-state [title]="emptyTitle()" [message]="emptyMessage()" />
      } @else {
        <div class="table-scroll">
          <table class="data-table">
            <thead>
              <tr>
                <th><app-sort-btn column="created" [query]="list">Time</app-sort-btn></th>
                <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
                <th><app-sort-btn column="kind" [query]="list">Type</app-sort-btn></th>
                <th><app-sort-btn column="side" [query]="list">Side</app-sort-btn></th>
                <th><app-sort-btn column="status" [query]="list">Status</app-sort-btn></th>
                <th class="num"><app-sort-btn column="qty" [query]="list" align="end">Qty</app-sort-btn></th>
                @if (tab() !== 'open') {
                  <th class="num"><app-sort-btn column="filled" [query]="list" align="end">Filled</app-sort-btn></th>
                }
                <th class="num"><app-sort-btn column="price" [query]="list" align="end">{{ tab() === 'open' && openKind() === 'conditional' ? 'Trigger' : 'Price' }}</app-sort-btn></th>
                @if (tab() === 'fills') {
                  <th class="num"><app-sort-btn column="pnl" [query]="list" align="end">Realized</app-sort-btn></th>
                }
                <th class="num"><app-sort-btn column="fee" [query]="list" align="end">Fee</app-sort-btn></th>
                @if (tab() === 'open' && openKind() === 'conditional') {
                  <th>Reduce</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of visibleOrders(); track row.id) {
                <tr>
                  <td>{{ formatTime(row.createdAt) }}</td>
                  <td>{{ row.symbol }}</td>
                  <td><span class="badge" [class.badge-paused]="isProtectionOrder(row)" [class.badge-ok]="!isProtectionOrder(row)">{{ orderKindLabel(row) }}</span></td>
                  <td><span class="badge" [class.badge-long]="row.side === 'Buy'" [class.badge-short]="row.side !== 'Buy'">{{ row.side }}</span></td>
                  <td>{{ orderStatusLabel(row) }}</td>
                  <td class="num">{{ qty(row.quantity) }}</td>
                  @if (tab() !== 'open') {
                    <td class="num">{{ qty(row.filledQuantity) }}</td>
                  }
                  <td class="num">{{ price(row.price) }}</td>
                  @if (tab() === 'fills') {
                    <td class="num" [class]="pnlClass(row.pnL)">{{ signedMoney(row.pnL) }}</td>
                  }
                  <td class="num" [class]="pnlClass(feeCash(row.fee))">{{ signedMoney(feeCash(row.fee), 4) }}</td>
                  @if (tab() === 'open' && openKind() === 'conditional') {
                    <td>Close Position</td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
  styles: `
    :host { display: block; min-height: 0; height: 100%; }
    .ledger-book {
      display: flex;
      flex-direction: column;
      min-height: 360px;
      height: 100%;
    }
    .ledger-book-head { flex-wrap: wrap; gap: 10px; }
    .ledger-subtabs { margin: 0 12px 8px; align-self: flex-start; }
    .ledger-coin-filter { display: inline-flex; align-items: center; gap: 6px; cursor: pointer; }
    .ledger-book .table-scroll { flex: 1; min-height: 220px; }
    :host ::ng-deep .is-embedded.panel {
      background: transparent;
      border: 0;
      box-shadow: none;
      padding: 0;
      height: auto;
      min-height: 0;
    }
  `,
})
export class LedgerBookComponent {
  readonly positions = input<PositionDto[]>([]);
  readonly orders = input<OrderDto[]>([]);
  readonly trades = input<TradeDto[]>([]);
  readonly initialTab = input<LedgerBookTab>('positions');
  readonly allowedTabs = input<LedgerBookTab[] | null>(null);
  readonly showViewAll = input(true);
  readonly ui = inject(UiStateService);
  readonly formatTime = formatTime;
  readonly qty = qty;
  readonly price = price;
  readonly signedMoney = signedMoney;
  readonly pnlClass = pnlClass;
  readonly feeCash = feeCash;
  readonly isProtectionOrder = isProtectionOrder;
  readonly orderKindLabel = orderKindLabel;
  readonly orderStatusLabel = orderStatusLabel;
  readonly list = new ListQuery();
  readonly tab = linkedSignal<LedgerBookTab>(() => this.initialTab());
  readonly openKind = signal<'basic' | 'conditional'>('conditional');
  readonly coinOnly = signal(false);

  readonly visibleTabs = computed(() => {
    const all: { id: LedgerBookTab; label: string }[] = [
      { id: 'positions', label: `Positions (${this.positions().length})` },
      { id: 'open', label: `Open Orders (${this.openCount()})` },
      { id: 'history', label: 'Order History' },
      { id: 'fills', label: 'Trade History' },
      { id: 'closed', label: 'Position History' },
    ];
    const allowed = this.allowedTabs();
    if (!allowed?.length) {
      return all;
    }
    return all.filter((item) => allowed.includes(item.id));
  });
  readonly showBookTabs = computed(() => this.visibleTabs().length > 1);

  readonly visiblePositions = computed(() => this.filterCoin(this.positions()));
  readonly visibleTrades = computed(() => this.filterCoin(this.trades()));
  readonly closedTrades = computed(() => this.visibleTrades().filter((row) => !!row.closedAt));
  readonly scopedOrders = computed(() => this.filterCoin(this.orders()));
  readonly openOrders = computed(() => this.scopedOrders().filter(isWorkingOrder));
  readonly basicOpen = computed(() => this.openOrders().filter((row) => !isProtectionOrder(row)));
  readonly conditionalOpen = computed(() => this.openOrders().filter(isProtectionOrder));
  readonly openCount = computed(() => this.openOrders().length);
  readonly historyOrders = computed(() => this.scopedOrders().filter(isHistoryOrder));
  readonly fillOrders = computed(() => tradeHistoryFills(this.scopedOrders()));
  readonly orderRows = computed(() => {
    const tab = this.tab();
    if (tab === 'open') {
      return this.openKind() === 'conditional' ? this.conditionalOpen() : this.basicOpen();
    }
    if (tab === 'history') {
      return this.historyOrders();
    }
    if (tab === 'fills') {
      return this.fillOrders();
    }
    return [];
  });
  readonly visibleOrders = computed(() =>
    this.list.apply(
      this.orderRows(),
      (row) => [row.symbol, row.side, row.status, orderKindLabel(row)],
      {
        created: (row) => timeValue(row.createdAt),
        symbol: (row) => row.symbol,
        kind: (row) => orderKindLabel(row),
        side: (row) => row.side,
        status: (row) => orderStatusLabel(row),
        qty: (row) => row.quantity,
        filled: (row) => row.filledQuantity,
        price: (row) => row.price ?? 0,
        pnl: (row) => row.pnL ?? 0,
        fee: (row) => row.fee ?? 0,
      },
    ),
  );

  viewAllLink(): string {
    const tab = this.tab();
    if (tab === 'positions') {
      return '/positions';
    }
    if (tab === 'fills') {
      return '/fills';
    }
    if (tab === 'closed') {
      return '/trades';
    }
    return '/orders';
  }

  emptyTitle(): string {
    const tab = this.tab();
    if (tab === 'open') {
      return this.openKind() === 'conditional' ? 'No working stops' : 'No open orders';
    }
    if (tab === 'fills') {
      return 'No fills yet';
    }
    return 'No order history';
  }

  emptyMessage(): string {
    const tab = this.tab();
    if (tab === 'open' && this.openKind() === 'conditional') {
      return 'Isolated stop and take-profit waiting on Binance appear here.';
    }
    if (tab === 'fills') {
      return 'Every Binance user trade for this workspace appears here, one row per fill.';
    }
    return 'Filled and cancelled orders for this workspace appear here.';
  }

  private filterCoin<T extends { symbol: string }>(rows: T[]): T[] {
    if (!this.coinOnly()) {
      return rows;
    }
    const coin = this.ui.selectedSymbol().trim().toUpperCase();
    return rows.filter((row) => row.symbol.trim().toUpperCase() === coin);
  }
}
