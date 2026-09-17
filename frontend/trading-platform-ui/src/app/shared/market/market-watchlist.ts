import { Component, computed, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MarketQuoteDto, TickerDto, compact, pct, pnlClass, price } from '../../core/trading/trading.models';
import { ListQuery } from '../lists/list-query';
import { SortBtnComponent } from '../lists/list-tools';

@Component({
  selector: 'app-market-watchlist',
  imports: [RouterLink, SortBtnComponent],
  template: `
    <section class="panel panel-fill compact">
      <div class="watch-head-wrap">
        <div class="section-head" style="margin:0">
          <h2>Market Watch</h2>
          <a class="tiny" routerLink="/scanner">View all</a>
        </div>
        <div class="tabs" role="tablist">
          <button type="button" [class.is-on]="tab() === 'top'" (click)="tab.set('top')">Top Coins</button>
          <button type="button" [class.is-on]="tab() === 'watch'" (click)="tab.set('watch')">Watchlist</button>
          <button type="button" [class.is-on]="tab() === 'signals'" (click)="tab.set('signals')">Signals</button>
        </div>
      </div>
      <div class="watch-row watch-head">
        <app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn>
        <app-sort-btn column="price" [query]="list" align="end">Price</app-sort-btn>
        <app-sort-btn column="change" [query]="list" align="end">24h</app-sort-btn>
        <app-sort-btn column="volume" [query]="list" align="end">Volume</app-sort-btn>
      </div>
      <div class="table-scroll">
        @for (row of visible(); track row.symbol) {
          <div class="watch-row" [class.is-on]="row.symbol === selected()" (click)="select.emit(row.symbol)">
            <span><strong>{{ row.symbol.replace('USDT', '') }}</strong></span>
            <span class="num">{{ price(row.price) }}</span>
            <span class="num" [class]="pnlClass(row.changePercent24h)">{{ pct(row.changePercent24h) }}</span>
            <span class="num">{{ compact(row.quoteVolume) }}</span>
          </div>
        } @empty {
          <p class="empty-state">No market data yet.</p>
        }
      </div>
    </section>
  `,
  styles: [`
    :host { display:flex; flex-direction:column; height:100%; min-height:0; }
    .watch-head-wrap { flex-shrink:0; display:flex; flex-direction:column; justify-content:center; gap:6px; padding-bottom:8px; }
    .tabs { align-self: stretch; }
    .table-scroll { flex:1; min-height:0; overflow:auto; }
  `],
})
export class MarketWatchlistComponent {
  readonly quotes = input<MarketQuoteDto[]>([]);
  readonly tickers = input<TickerDto[]>([]);
  readonly watchlist = input<string[]>([]);
  readonly signalSymbols = input<string[]>([]);
  readonly selected = input('BTCUSDT');
  readonly select = output<string>();
  readonly tab = signal<'top' | 'watch' | 'signals'>('top');
  readonly list = new ListQuery();
  readonly price = price;
  readonly pct = pct;
  readonly compact = compact;
  readonly pnlClass = pnlClass;

  readonly merged = computed(() => {
    const bySymbol = new Map(this.quotes().map((item) => [item.symbol, { ...item }]));
    for (const ticker of this.tickers()) {
      const current = bySymbol.get(ticker.symbol);
      if (current) {
        current.price = ticker.price;
      } else {
        bySymbol.set(ticker.symbol, {
          symbol: ticker.symbol,
          displayName: ticker.displayName,
          marketCapRank: ticker.marketCapRank,
          price: ticker.price,
          changePercent24h: 0,
          quoteVolume: 0,
          timestamp: ticker.timestamp,
          highPrice24h: 0,
          lowPrice24h: 0,
          trades24h: 0,
        });
      }
    }
    return [...bySymbol.values()].sort((a, b) => a.marketCapRank - b.marketCapRank);
  });

  readonly scoped = computed(() => {
    const rows = this.merged();
    if (this.tab() === 'watch') {
      return rows.filter((row) => this.watchlist().includes(row.symbol));
    }
    if (this.tab() === 'signals') {
      return rows.filter((row) => this.signalSymbols().includes(row.symbol));
    }
    return rows.slice(0, 30);
  });

  readonly visible = computed(() =>
    this.list.apply(
      this.scoped(),
      (row) => [row.symbol, row.displayName],
      {
        symbol: (row) => row.symbol,
        price: (row) => row.price,
        change: (row) => row.changePercent24h,
        volume: (row) => row.quoteVolume,
      },
    ),
  );
}
