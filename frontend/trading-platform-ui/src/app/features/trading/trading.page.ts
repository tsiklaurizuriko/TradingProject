import { Component, computed, inject } from '@angular/core';
import { TradingService } from '../../core/trading/trading.service';
import { pct, pnlClass, price } from '../../core/trading/trading.models';
import { UiStateService } from '../../core/ui/ui-state.service';
import { TradingChartComponent } from '../../shared/chart/trading-chart';
import { MarketWatchlistComponent } from '../../shared/market/market-watchlist';

@Component({
  selector: 'app-trading-page',
  imports: [TradingChartComponent, MarketWatchlistComponent],
  template: `
    <section class="trading-stack">
      <article class="panel chart-panel">
        <div class="chart-head">
          <div>
            <div class="tiny">{{ ui.selectedSymbol() }}</div>
            <div class="price-line">
              <strong>{{ price(quote()?.price) }}</strong>
              <span [class]="pnlClass(change())">{{ pct(change()) }}</span>
            </div>
          </div>
          <div class="tf-group">
            @for (tf of timeframes; track tf) {
              <button type="button" [class.is-on]="ui.timeframe() === tf" (click)="setTf(tf)">{{ tf }}</button>
            }
          </div>
        </div>
        <app-trading-chart [bars]="trading.klines()" [positions]="trading.workspacePositions()" [trades]="trading.workspaceTrades()" [signals]="trading.workspaceSignals()" [symbol]="ui.selectedSymbol()" />
      </article>
      <app-market-watchlist
        class="trading-watch"
        [quotes]="trading.markets()"
        [tickers]="trading.tickers()"
        [watchlist]="ui.watchlist()"
        [signalSymbols]="trading.workspaceSignals().map(s => s.symbol)"
        [selected]="ui.selectedSymbol()"
        (select)="select($event)"
      />
    </section>
  `,
})
export class TradingPage {
  readonly trading = inject(TradingService);
  readonly ui = inject(UiStateService);
  readonly price = price;
  readonly pct = pct;
  readonly pnlClass = pnlClass;
  readonly timeframes = ['1m', '5m', '15m', '1h', '4h', '1d'];
  readonly quote = computed(() => this.trading.markets().find((m) => m.symbol === this.ui.selectedSymbol()) ?? this.trading.tickers().find((t) => t.symbol === this.ui.selectedSymbol()));
  change(): number {
    const q = this.quote();
    if (q && 'changePercent24h' in q && typeof q.changePercent24h === 'number') {
      return q.changePercent24h;
    }
    return 0;
  }
  constructor() {
    void this.trading.loadKlines(this.ui.selectedSymbol(), this.ui.timeframe());
  }
  async select(symbol: string): Promise<void> {
    this.ui.setSymbol(symbol);
    await this.trading.loadKlines(symbol, this.ui.timeframe());
  }
  async setTf(tf: string): Promise<void> {
    this.ui.setTimeframe(tf);
    await this.trading.loadKlines(this.ui.selectedSymbol(), tf);
  }
}
