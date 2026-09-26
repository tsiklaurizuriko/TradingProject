import { Component, inject, signal } from '@angular/core';
import { TradingService } from '../../core/trading/trading.service';
import { NewsDeskDto, NewsFeedItem } from '../../core/trading/trading.models';

@Component({
  selector: 'app-news-page',
  template: `
    <header class="page-header">
      <div>
        <h1>News</h1>
        <p>News trading reads new articles, confirms the market, then sends a Binance USD-M order with the existing stop and take profit.</p>
      </div>
      <button type="button" (click)="toggle()">{{ desk()?.running ? 'Stop' : 'Start news trading' }}</button>
    </header>
    @if (error()) {
      <section class="panel">{{ error() }}</section>
    } @else if (!desk()) {
      <section class="panel">Loading news status…</section>
    } @else {
      <section class="news-stats">
        <article class="card"><span>Mode</span><strong>{{ desk()!.mode }}</strong><small>{{ desk()!.running ? 'RUNNING' : 'STOPPED' }}</small></article>
        <article class="card"><span>Universe</span><strong>{{ desk()!.universeCount }}</strong><small>USDT-M perpetuals</small></article>
        <article class="card"><span>Articles</span><strong>{{ desk()!.articles }}</strong><small>in the database</small></article>
        <article class="card"><span>Events</span><strong>{{ desk()!.events }}</strong><small>stored</small></article>
      </section>
      <section class="panel">
        <h2>Latest decision</h2>
        <p>{{ desk()!.signal || 'No signal yet' }}</p>
        <p class="tiny">News {{ desk()!.newsScore ?? '—' }} · Market {{ desk()!.marketScore ?? '—' }} · Final {{ desk()!.finalScore ?? '—' }} · Risk {{ desk()!.risk || '—' }} · Order {{ desk()!.order || '—' }}</p>
        <p class="tiny">{{ desk()!.reason }}</p>
      </section>
      <section class="panel">
        <h2>What was read</h2>
        @if (!items().length) {
          <p class="tiny">No articles in the database yet.</p>
        } @else {
          <div class="table-scroll">
            <table class="data-table">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Source</th>
                  <th>Headline</th>
                  <th>Coin</th>
                  <th>What happened</th>
                </tr>
              </thead>
              <tbody>
                @for (row of items(); track row.url + row.publishedAt) {
                  <tr>
                    <td>{{ when(row.publishedAt) }}</td>
                    <td>{{ row.source }}</td>
                    <td>
                      @if (row.url) {
                        <a [href]="row.url" target="_blank" rel="noopener">{{ row.title }}</a>
                      } @else {
                        {{ row.title }}
                      }
                    </td>
                    <td>{{ row.coin || '—' }}</td>
                    <td>{{ row.outcome }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    }
  `,
  styles: `
    .news-stats { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin: 16px 0; }
    .news-stats .card { display: grid; gap: 4px; }
    .news-stats span, .news-stats small { color: var(--text-secondary); font-size: 12px; }
    .news-stats strong { font-size: 22px; }
    .panel { margin-bottom: 12px; }
    .panel h2 { margin-bottom: 8px; }
    .data-table a { color: inherit; text-decoration: underline; }
    @media (max-width: 800px) { .news-stats { grid-template-columns: 1fr 1fr; } }
  `,
})
export class NewsPage {
  private readonly trading = inject(TradingService);
  readonly desk = signal<NewsDeskDto | null>(null);
  readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
    setInterval(() => void this.load(), 15000);
  }

  items(): NewsFeedItem[] {
    return this.desk()?.items ?? [];
  }

  when(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }
    return date.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  async toggle(): Promise<void> {
    this.error.set(null);
    try {
      if (this.desk()?.running) {
        await this.trading.stopNewsTrading();
      } else {
        await this.trading.startNewsTrading();
      }
      await this.load();
    } catch {
      this.error.set('News trading could not be changed. Restart the API so the database migration is applied.');
    }
  }

  private async load(): Promise<void> {
    try {
      this.desk.set(await this.trading.newsDesk());
    } catch {
      this.error.set('News status could not be loaded. The API may be stopped, or the news tables are not migrated yet.');
    }
  }
}
