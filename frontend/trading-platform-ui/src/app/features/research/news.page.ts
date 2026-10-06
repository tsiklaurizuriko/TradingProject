import { Component, inject, signal } from '@angular/core';
import { TradingService } from '../../core/trading/trading.service';
import { NewsActivityItem, NewsDeskDto, NewsFeedItem } from '../../core/trading/trading.models';
import { IconComponent } from '../../shared/icon/icon';

type NewsFilter = 'all' | 'orders' | 'skipped';

@Component({
  selector: 'app-news-page',
  imports: [IconComponent],
  template: `
    <header class="page-header">
      <div>
        <h1>News</h1>
        <p>Each card is one decision: what the news was, what we decided, and whether an order went to Binance.</p>
      </div>
      <button type="button" class="btn" [class.danger]="desk()?.running" (click)="toggle()">
        {{ desk()?.running ? 'Stop' : 'Start news trading' }}
      </button>
    </header>

    @if (error()) {
      <section class="panel">{{ error() }}</section>
    } @else if (!desk()) {
      <section class="panel">Loading news…</section>
    } @else {
      <section class="news-status">
        <span class="badge" [class.badge-running]="desk()!.running" [class.badge-stopped]="!desk()!.running">
          {{ desk()!.running ? 'Running' : 'Stopped' }}
        </span>
        <span class="badge" [class.badge-live]="desk()!.mode === 'Live'" [class.badge-stopped]="desk()!.mode !== 'Live'">
          {{ desk()!.mode === 'Live' ? 'Live orders on' : desk()!.mode }}
        </span>
        @if (desk()!.collectionStatus) {
          <span class="tiny">Last collection: {{ desk()!.collectionStatus }}</span>
        }
      </section>

      <section class="news-stats">
        <article class="card">
          <span>Orders sent</span>
          <strong>{{ orderCount() }}</strong>
          <small>in the list below</small>
        </article>
        <article class="card">
          <span>No order</span>
          <strong>{{ skippedCount() }}</strong>
          <small>skipped, with a reason</small>
        </article>
        <article class="card">
          <span>Coins watched</span>
          <strong>{{ desk()!.universeCount }}</strong>
          <small>USDT-M perpetuals</small>
        </article>
      </section>

      <div class="tabs news-tabs">
        <button type="button" [class.is-on]="filter() === 'all'" (click)="filter.set('all')">All {{ cards().length }}</button>
        <button type="button" [class.is-on]="filter() === 'orders'" (click)="filter.set('orders')">Orders {{ orderCount() }}</button>
        <button type="button" [class.is-on]="filter() === 'skipped'" (click)="filter.set('skipped')">No order {{ skippedCount() }}</button>
      </div>

      @if (!visible().length) {
        <section class="panel">
          <h2>No decisions yet</h2>
          <p class="tiny">When a news item is checked, it shows up here with the reason an order was sent or skipped.</p>
        </section>
      } @else {
        <section class="decision-list">
          @for (card of visible(); track card.coin + card.at + card.headline) {
            <article class="decision" [class.is-long]="card.verdict === 'LONG'" [class.is-short]="card.verdict === 'SHORT'">
              <header>
                <div class="decision-coin">
                  <strong>{{ base(card.coin) }}</strong>
                  <span>{{ card.coin || 'No coin' }}</span>
                </div>
                <span class="badge" [class.badge-long]="card.verdict === 'LONG'" [class.badge-short]="card.verdict === 'SHORT'" [class.badge-stopped]="card.verdict === 'NO_TRADE'">
                  {{ verdictLabel(card.verdict) }}
                </span>
                <span class="badge" [class.badge-ok]="sent(card)" [class.badge-error]="card.orderState === 'Rejected'" [class.badge-stopped]="!sent(card) && card.orderState !== 'Rejected'">
                  {{ orderLabel(card.orderState) }}
                </span>
                <time>{{ when(card.at) }}</time>
              </header>
              @if (card.url) {
                <a class="headline" [href]="card.url" target="_blank" rel="noopener">{{ card.headline }}</a>
              } @else {
                <p class="headline">{{ card.headline }}</p>
              }
              <p class="why">{{ card.why }}</p>
              <p class="meta">
                @if (card.source) { <span>{{ card.source }}</span> }
                @if (card.confidence != null) { <span>Confidence {{ card.confidence }}</span> }
                @if (card.impact != null) { <span>Impact {{ card.impact }}</span> }
                @if (card.alreadyPricedIn != null) { <span>Already priced {{ card.alreadyPricedIn }}</span> }
              </p>
              @if (card.orderLine) {
                <p class="order-line">{{ card.orderLine }}</p>
              }
            </article>
          }
        </section>
      }

      <section class="panel" [class.is-collapsed]="!sourcesOpen()">
        <div class="section-head">
          <button type="button" class="section-fold" (click)="sourcesOpen.set(!sourcesOpen())" [attr.aria-expanded]="sourcesOpen()">
            <app-icon name="chevron" [class.is-closed]="!sourcesOpen()" />
            <h2>Sources</h2>
          </button>
        </div>
        @if (sourcesOpen()) {
          @if (!providers().length) {
            <p class="tiny">No source has been polled yet.</p>
          } @else {
            <ul class="source-list">
              @for (row of providers(); track row.provider) {
                <li>
                  <span class="status-dot" [class.ok]="row.status === 'Working'" [class.warn]="row.status === 'Waiting' || row.status === 'Pending'" [class.bad]="row.status === 'Failing' || row.status === 'Disabled'"></span>
                  <strong>{{ row.provider }}</strong>
                  <span>{{ sourceLine(row.status, row.lastSuccessUtc, row.lastError) }}</span>
                </li>
              }
            </ul>
          }
        }
      </section>
    }
  `,
  styles: `
    .news-status { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin: 14px 0; }
    .news-stats { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; margin-bottom: 14px; }
    .news-stats .card { display: grid; gap: 4px; }
    .news-stats span, .news-stats small, .tiny { color: var(--text-secondary); font-size: 12px; }
    .news-stats strong { font-size: 22px; }
    .news-tabs { width: fit-content; margin-bottom: 12px; }
    .decision-list { display: grid; gap: 10px; margin-bottom: 12px; }
    .decision {
      background: var(--bg-card);
      border: 1px solid var(--border);
      border-left: 3px solid var(--border);
      border-radius: var(--radius);
      padding: 14px 16px;
      display: grid;
      gap: 8px;
    }
    .decision.is-long { border-left-color: var(--success); }
    .decision.is-short { border-left-color: var(--danger); }
    .decision header { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .decision-coin { display: flex; align-items: baseline; gap: 8px; margin-right: auto; }
    .decision-coin strong { font-size: 16px; }
    .decision-coin span, .decision time, .meta { color: var(--text-secondary); font-size: 12px; }
    .headline { margin: 0; font-size: 14px; font-weight: 650; color: inherit; }
    a.headline { text-decoration: none; }
    a.headline:hover { text-decoration: underline; }
    .why { margin: 0; font-size: 14px; line-height: 1.45; }
    .meta { display: flex; gap: 12px; flex-wrap: wrap; margin: 0; }
    .order-line {
      margin: 0;
      padding: 8px 10px;
      border-radius: 8px;
      background: var(--bg-secondary);
      font-size: 13px;
    }
    .source-list { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .source-list li { display: flex; align-items: center; gap: 8px; font-size: 13px; }
    .source-list span:last-child { color: var(--text-secondary); }
    .panel { margin-bottom: 12px; }
    @media (max-width: 800px) { .news-stats { grid-template-columns: 1fr; } }
  `,
})
export class NewsPage {
  private readonly trading = inject(TradingService);
  readonly desk = signal<NewsDeskDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly sourcesOpen = signal(false);
  readonly filter = signal<NewsFilter>('all');

  constructor() {
    void this.load();
    setInterval(() => void this.load(), 15000);
  }

  cards(): NewsActivityItem[] {
    const desk = this.desk();
    if (!desk) {
      return [];
    }
    if (desk.activity?.length) {
      return desk.activity;
    }
    return (desk.items ?? []).map(item => this.fromFeed(item));
  }

  visible(): NewsActivityItem[] {
    const filter = this.filter();
    return this.cards().filter(card => {
      if (filter === 'orders') {
        return this.sent(card);
      }
      if (filter === 'skipped') {
        return !this.sent(card);
      }
      return true;
    });
  }

  orderCount(): number {
    return this.cards().filter(card => this.sent(card)).length;
  }

  skippedCount(): number {
    return this.cards().filter(card => !this.sent(card)).length;
  }

  providers() {
    return this.desk()?.providers ?? [];
  }

  sent(card: NewsActivityItem): boolean {
    return card.orderState === 'Sent' || card.orderState === 'Filled' || card.orderState === 'Closed';
  }

  base(coin: string): string {
    return coin.replace(/(USDT|USDC)$/i, '') || 'News';
  }

  verdictLabel(verdict: string): string {
    if (verdict === 'LONG') {
      return 'Long';
    }
    if (verdict === 'SHORT') {
      return 'Short';
    }
    return 'No trade';
  }

  orderLabel(state: string): string {
    switch (state) {
      case 'Filled': return 'Order filled';
      case 'Sent': return 'Order sent';
      case 'Closed': return 'Position closed';
      case 'Rejected': return 'Risk rejected';
      default: return 'No order';
    }
  }

  sourceLine(status: string, lastSuccess: string | null, lastError: string | null): string {
    if (lastError && (status === 'Failing' || status === 'Disabled')) {
      return status + ' — ' + lastError;
    }
    if (lastSuccess) {
      return status + ' — last success ' + this.when(lastSuccess);
    }
    return status;
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

  private fromFeed(item: NewsFeedItem): NewsActivityItem {
    const classification = item.classification ?? '';
    const verdict = /bearish|short/i.test(classification) ? 'SHORT' : /bullish|long/i.test(classification) ? 'LONG' : 'NO_TRADE';
    return {
      at: item.publishedAt,
      coin: item.coin ?? '',
      headline: item.title,
      url: item.url || null,
      source: item.publisher,
      verdict,
      orderState: 'NotSent',
      why: humanize(item.detail),
      orderLine: null,
      confidence: percent(item.confidence),
      impact: percent(item.impact),
      alreadyPricedIn: null,
    };
  }

  private async load(): Promise<void> {
    try {
      this.desk.set(await this.trading.newsDesk());
      this.error.set(null);
    } catch {
      this.error.set('News status could not be loaded. The API may be stopped, or the news tables are not migrated yet.');
    }
  }
}

function percent(value: number | null): number | null {
  if (value == null || value <= 0) {
    return null;
  }
  return Math.round(value <= 1 ? value * 100 : value);
}

function humanize(detail: string): string {
  const age = detail.match(/age:\s*(\d+)m exceeds (\d+)m/i);
  if (age) {
    return `The news is ${age[1]} minutes old. Trades stop after ${age[2]} minutes, so no order was sent.`;
  }
  if (/stopped at confidence/i.test(detail)) {
    return 'Confidence is below the minimum. No order was sent.';
  }
  if (/stopped at impact/i.test(detail)) {
    return 'Impact is below the minimum. No order was sent.';
  }
  if (/stopped at direction/i.test(detail)) {
    return 'The direction is not long or short. No order was sent.';
  }
  if (/stopped at coin/i.test(detail)) {
    return 'No coin was identified. No order was sent.';
  }
  if (!detail) {
    return 'No order was sent.';
  }
  return /order/i.test(detail) ? detail : detail.replace(/\.$/, '') + '. No order was sent.';
}
