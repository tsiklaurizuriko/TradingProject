import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TradingService } from '../../core/trading/trading.service';
import {
  compact,
  MarketQuoteDto,
  money,
  pct,
  pnlClass,
  price,
  rate,
  signalLabel,
  signedMoney,
} from '../../core/trading/trading.models';
import { UiStateService } from '../../core/ui/ui-state.service';
import { ListQuery } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { EmptyStateComponent, MetricCardComponent } from '../../shared/ui/ui-kit';

@Component({
  selector: 'app-scanner-page',
  imports: [SortBtnComponent],
  template: `
    <section class="panel">
      <p class="tiny" style="margin:0 0 12px">Full Binance USDⓈ-M USDT perpetual universe. Ranked by the scanner; Watch is not a trade. Start still requires current eligibility.</p>
      <table class="data-table">
        <thead>
          <tr>
            <th><app-sort-btn column="rank" [query]="list">#</app-sort-btn></th>
            <th><app-sort-btn column="symbol" [query]="list">Coin</app-sort-btn></th>
            <th class="num"><app-sort-btn column="price" [query]="list" align="end">Price</app-sort-btn></th>
            <th class="num"><app-sort-btn column="change" [query]="list" align="end">24h</app-sort-btn></th>
            <th class="num"><app-sort-btn column="volume" [query]="list" align="end">Quote vol</app-sort-btn></th>
            <th class="num"><app-sort-btn column="score" [query]="list" align="end">Score</app-sort-btn></th>
            <th class="num"><app-sort-btn column="spread" [query]="list" align="end">Spread</app-sort-btn></th>
            <th class="num"><app-sort-btn column="funding" [query]="list" align="end">Funding</app-sort-btn></th>
            <th><app-sort-btn column="eligible" [query]="list">Trade</app-sort-btn></th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track row.symbol) {
            <tr class="clickable" (click)="ui.setSymbol(row.symbol)">
              <td>{{ row.marketCapRank }}</td>
              <td><strong>{{ row.symbol }}</strong><div class="tiny">{{ row.displayName }}</div></td>
              <td class="num">{{ price(row.price) }}</td>
              <td class="num" [class]="pnlClass(row.changePercent24h)">{{ pct(row.changePercent24h) }}</td>
              <td class="num">{{ compact(row.quoteVolume) }}</td>
              <td class="num">{{ (row.scanScore ?? 0).toFixed(2) }}</td>
              <td class="num">{{ (row.spreadBps ?? 0).toFixed(2) }}</td>
              <td class="num">{{ ((row.fundingRate ?? 0) * 100).toFixed(4) }}%</td>
              <td>{{ row.eligible ? 'Eligible' : 'Watch' }}</td>
              <td><button class="btn sm ghost" type="button" (click)="$event.stopPropagation(); ui.toggleWatch(row.symbol)">{{ ui.watchlist().includes(row.symbol) ? 'Watched' : 'Watch' }}</button></td>
            </tr>
          }
        </tbody>
      </table>
    </section>
  `,
})
export class ScannerPage {
  readonly trading = inject(TradingService);
  readonly ui = inject(UiStateService);
  readonly price = price;
  readonly pct = pct;
  readonly compact = compact;
  readonly pnlClass = pnlClass;
  readonly list = new ListQuery();
  readonly source = computed(() => {
    const q = this.ui.search().trim().toUpperCase();
    const quotes = this.trading.markets();
    const source: MarketQuoteDto[] = quotes.length
      ? quotes
      : this.trading.tickers().map((t) => ({ ...t, changePercent24h: 0, quoteVolume: 0 }));
    if (!q) {
      return source;
    }
    return source.filter((row) => row.symbol.includes(q) || (row.displayName ?? '').toUpperCase().includes(q));
  });
  readonly rows = computed(() =>
    this.list.apply(
      this.source(),
      (row) => [row.symbol, row.displayName],
      {
        rank: (row) => row.marketCapRank,
        symbol: (row) => row.symbol,
        price: (row) => row.price,
        change: (row) => row.changePercent24h,
        volume: (row) => row.quoteVolume,
        score: (row) => row.scanScore ?? 0,
        spread: (row) => row.spreadBps ?? 0,
        funding: (row) => row.fundingRate ?? 0,
        eligible: (row) => (row.eligible ? 1 : 0),
      },
    ),
  );
}

@Component({
  selector: 'app-watchlist-page',
  imports: [RouterLink, MetricCardComponent, EmptyStateComponent, SortBtnComponent],
  templateUrl: './watchlist.page.html',
  styleUrl: './watchlist.page.scss',
})
export class WatchlistPage {
  readonly trading = inject(TradingService);
  readonly ui = inject(UiStateService);
  private readonly router = inject(Router);
  readonly price = price;
  readonly pct = pct;
  readonly compact = compact;
  readonly pnlClass = pnlClass;
  readonly money = money;
  readonly signedMoney = signedMoney;
  readonly list = new ListQuery();
  readonly allRows = computed(() => {
    const quotes = new Map<string, {
      symbol: string;
      displayName: string;
      marketCapRank: number;
      price: number;
      changePercent24h: number;
      quoteVolume: number;
      timestamp: string;
      highPrice24h?: number;
      lowPrice24h?: number;
      trades24h?: number;
    }>();
    for (const row of this.trading.markets()) {
      quotes.set(row.symbol, { ...row });
    }
    for (const ticker of this.trading.tickers()) {
      const current = quotes.get(ticker.symbol);
      if (current) {
        current.price = ticker.price;
        current.timestamp = ticker.timestamp;
      } else {
        quotes.set(ticker.symbol, {
          symbol: ticker.symbol,
          displayName: ticker.displayName,
          marketCapRank: ticker.marketCapRank,
          price: ticker.price,
          changePercent24h: 0,
          quoteVolume: 0,
          timestamp: ticker.timestamp,
        });
      }
    }
    const bots = this.trading.workspaceBots();
    const positions = this.trading.workspacePositions();
    const signals = this.trading.workspaceSignals();
    const trades = this.trading.workspaceTrades();
    return this.ui.watchlist().map((symbol) => {
      const quote = quotes.get(symbol);
      const coinBots = bots.filter((bot) => bot.symbol === symbol);
      const coinPositions = positions.filter((row) => row.symbol === symbol);
      const latest = [...signals.filter((row) => row.symbol === symbol)].sort((a, b) =>
        Date.parse(b.timestamp) - Date.parse(a.timestamp),
      )[0];
      const closed = trades.filter((row) => row.symbol === symbol && row.closedAt);
      return {
        symbol,
        displayName: quote?.displayName || symbol.replace(/USDT$/i, ''),
        marketCapRank: quote?.marketCapRank ?? 0,
        price: quote?.price ?? 0,
        changePercent24h: quote && 'changePercent24h' in quote ? quote.changePercent24h : 0,
        quoteVolume: quote && 'quoteVolume' in quote ? quote.quoteVolume : 0,
        highPrice24h: quote && 'highPrice24h' in quote ? (quote.highPrice24h ?? 0) : 0,
        lowPrice24h: quote && 'lowPrice24h' in quote ? (quote.lowPrice24h ?? 0) : 0,
        trades24h: quote && 'trades24h' in quote ? (quote.trades24h ?? 0) : 0,
        bots: coinBots.length,
        running: coinBots.filter((bot) => bot.status === 'Running').length,
        positions: coinPositions.length,
        positionNotional: coinPositions.reduce((sum, row) => sum + row.quantity * row.averageEntryPrice, 0),
        unrealized: coinPositions.reduce((sum, row) => sum + row.unrealizedPnL, 0),
        signal: latest ? signalLabel(latest.signalType) : null,
        closedTrades: closed.length,
        realized: closed.reduce((sum, row) => sum + row.pnL, 0),
      };
    });
  });
  readonly rows = computed(() =>
    this.list.apply(
      this.allRows(),
      (row) => [row.symbol, row.displayName, row.signal, row.running ? 'RUNNING' : row.bots ? 'STOPPED' : 'No bot'],
      {
        symbol: (row) => row.symbol,
        price: (row) => row.price,
        change: (row) => row.changePercent24h,
        range: (row) => (row.highPrice24h || 0) - (row.lowPrice24h || 0),
        volume: (row) => row.quoteVolume,
        bot: (row) => row.running || row.bots,
        position: (row) => row.unrealized,
        signal: (row) => row.signal ?? '',
      },
    ),
  );
  readonly gainers = computed(() => this.allRows().filter((row) => row.changePercent24h > 0).length);
  readonly losers = computed(() => this.allRows().filter((row) => row.changePercent24h < 0).length);
  readonly avgChangeRaw = computed(() => {
    const rows = this.allRows().filter((row) => row.price);
    if (!rows.length) {
      return 0;
    }
    return rows.reduce((sum, row) => sum + row.changePercent24h, 0) / rows.length;
  });
  readonly totalVolume = computed(() => this.allRows().reduce((sum, row) => sum + row.quoteVolume, 0));
  readonly botsOnList = computed(() => this.allRows().reduce((sum, row) => sum + row.bots, 0));
  readonly runningOnList = computed(() => this.allRows().reduce((sum, row) => sum + row.running, 0));
  readonly openCount = computed(() => this.allRows().reduce((sum, row) => sum + row.positions, 0));
  readonly openPnl = computed(() => this.allRows().reduce((sum, row) => sum + row.unrealized, 0));

  avgChange(): string {
    return this.allRows().some((row) => row.price) ? rate(this.avgChangeRaw()) : '—';
  }

  rangePos(row: { price: number; highPrice24h: number; lowPrice24h: number }): number {
    const span = row.highPrice24h - row.lowPrice24h;
    if (span <= 0 || !row.price) {
      return 50;
    }
    return Math.min(100, Math.max(0, ((row.price - row.lowPrice24h) / span) * 100));
  }

  openChart(symbol: string): void {
    this.ui.setSymbol(symbol);
    void this.router.navigate(['/trading']);
  }
}
