import { Component, computed, inject, signal } from '@angular/core';
import { TradingService } from '../../core/trading/trading.service';
import {
  KlineBarDto,
  PatternOverlay,
  PriceActionOccurrenceDto,
  PriceActionResearchSummaryDto,
  ScalpingBookDto,
  ScalpingResearchSummaryDto,
  money,
} from '../../core/trading/trading.models';
import { EmptyStateComponent, MetricCardComponent, StatusBadgeComponent } from '../../shared/ui/ui-kit';
import { TradingChartComponent } from '../../shared/chart/trading-chart';

@Component({
  selector: 'app-scalping-page',
  imports: [MetricCardComponent, StatusBadgeComponent, EmptyStateComponent, TradingChartComponent],
  styleUrl: './strategies.page.scss',
  template: `
    <div class="strategies-page">
      <header class="strategies-toolbar">
        <div>
          <p class="tiny" style="margin:0">Research-only. Not a LIVE desk. Labels are Coin; JSON still uses symbol.</p>
          <p class="tiny" style="margin:6px 0 0">{{ banner() }}</p>
        </div>
        <div class="strategies-toolbar-actions">
          @for (tab of tabs; track tab.id) {
            <button type="button" class="ghost-btn" [class.active]="view() === tab.id" (click)="view.set(tab.id)">{{ tab.label }}</button>
          }
        </div>
      </header>

      <section class="kpi-row">
        <app-metric-card icon="flask" label="Mode" value="RESEARCH" sub="Not paper. Not LIVE." />
        <app-metric-card icon="plug" label="LIVE" value="OFF" valueClass="pnl-neg" sub="AllowLive stays false." />
        <app-metric-card icon="shield" label="Paper promotion" value="none" sub="No VALIDATED_FOR_PAPER." />
        <app-metric-card icon="chart" label="PA LIVE" value="OFF" valueClass="pnl-neg" sub="Cup & Handle NOT_IMPLEMENTED." />
      </section>

      @if (error()) {
        <app-empty-state title="Research artifacts unavailable" [message]="error() || ''" />
      }

      @if (view() === 'overview') {
        <section class="panel">
          <h2>Phase 7 data expansion</h2>
          <p class="tiny">Research-only 1m/3m cache coverage. This universe is not a LIVE trading restriction.</p>
          <div class="table-scroll">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Coin</th>
                  <th>TF</th>
                  <th>Actual range</th>
                  <th class="num">Bars</th>
                  <th class="num">Coverage</th>
                  <th class="num">Gaps</th>
                  <th class="num">Duplicates</th>
                  <th>Quality</th>
                </tr>
              </thead>
              <tbody>
                @for (row of pa()?.dataExpansion ?? []; track row.coin + row.timeframe) {
                  <tr>
                    <td>{{ row.coin }}</td>
                    <td>{{ row.timeframe }}</td>
                    <td>{{ row.actualFirstBar }} → {{ row.actualLastBar }}</td>
                    <td class="num">{{ row.barCount }} / {{ row.expectedBarCount }}</td>
                    <td class="num">{{ row.coveragePercent.toFixed(3) }}%</td>
                    <td class="num">{{ row.gapCount }}</td>
                    <td class="num">{{ row.duplicateCount }}</td>
                    <td><app-status-badge [label]="row.status" [cls]="badgeClass(row.status)" /></td>
                  </tr>
                } @empty {
                  <tr><td colspan="8">Run --phase7-data-expand to publish the six-month quality audit.</td></tr>
                }
              </tbody>
            </table>
          </div>
        </section>
        <section class="panel">
          <h2>Coverage</h2>
          <div class="table-scroll">
            <table class="data-table">
              <thead><tr><th>Coin</th><th>Timeframe</th><th class="num">Bars</th><th class="num">Gaps</th><th class="num">Taker</th><th>Status</th></tr></thead>
              <tbody>
                @for (row of summary()?.coverage ?? []; track row.coin + row.timeframe) {
                  <tr>
                    <td>{{ row.coin }}</td>
                    <td>{{ row.timeframe }}</td>
                    <td class="num">{{ row.bars }}</td>
                    <td class="num">{{ row.gaps }}</td>
                    <td class="num">{{ (row.takerCoverage * 100).toFixed(0) }}%</td>
                    <td><app-status-badge [label]="row.status" [cls]="badgeClass(row.status)" /></td>
                  </tr>
                } @empty {
                  <tr><td colspan="6">No coverage artifact yet. Run --scalping-data or --price-action-data.</td></tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }

      @if (view() === 'strategies') {
        <section class="panel">
          <h2>Scalping registry</h2>
          <p class="tiny">Disabled. Out of the operator catalog. Create/Start cannot pick these for LIVE.</p>
          <div class="strategies-grid">
            @for (row of summary()?.strategies ?? []; track row.templateKey) {
              <article class="card strategy-card">
                <div>
                  <strong>{{ row.name }}</strong>
                  <p class="tiny" style="margin:4px 0 0">{{ row.templateKey }} · {{ row.family }}</p>
                </div>
                <app-status-badge [label]="row.status" [cls]="badgeClass(row.status)" />
                <p class="tiny">{{ row.blurb }}</p>
              </article>
            }
          </div>
        </section>
        <section class="panel">
          <h2>Run results</h2>
          <div class="kpi-row" style="margin-bottom:12px">
            <app-metric-card icon="chart" label="OOS books" [value]="'' + oos().length" sub="BASE cost only." />
            <app-metric-card icon="shield" label="COST_FRAGILE" [value]="'' + costFragile()" sub="BASE PF>1 and HIGH PF<1." />
            <app-metric-card icon="positions" label="Same-coin rejects" [value]="'' + (summary()?.sameCoinRejects ?? 0)" sub="Isolated one coin." />
            <app-metric-card icon="layers" label="Slot rejects" [value]="'' + (summary()?.slotRejects ?? 0)" sub="Max 5 unique coins." />
          </div>
          <div class="table-scroll">
            <table class="data-table">
              <thead><tr><th>Candidate</th><th>Coin</th><th>TF</th><th>Phase</th><th>Cost</th><th>Status</th><th class="num">n</th><th class="num">PF</th><th class="num">Hold P50</th></tr></thead>
              <tbody>
                @for (row of visibleBooks(); track row.candidateId + row.coin + row.timeframe + row.phase + row.costLabel) {
                  <tr>
                    <td>{{ row.candidateId }}</td>
                    <td>{{ row.coin }}</td>
                    <td>{{ row.timeframe }}</td>
                    <td>{{ row.phase }}</td>
                    <td>{{ row.costLabel }}</td>
                    <td><app-status-badge [label]="row.status" [cls]="badgeClass(row.status)" /></td>
                    <td class="num">{{ row.tradeCount }}</td>
                    <td class="num">{{ row.profitFactor == null ? '—' : money(row.profitFactor, 2) }}</td>
                    <td class="num">{{ row.medianHoldingMinutes == null ? '—' : row.medianHoldingMinutes.toFixed(0) }}</td>
                  </tr>
                } @empty {
                  <tr><td colspan="9">No scalping books yet. Run --scalping after coverage.</td></tr>
                }
              </tbody>
            </table>
          </div>
        </section>
        <section class="panel">
          <h2>Occupancy rejects</h2>
          <p class="tiny">One Isolated coin globally. Same-coin A long + B short is still a reject.</p>
          <div class="table-scroll">
            <table class="data-table">
              <thead><tr><th>Time</th><th>Strategy</th><th>Coin</th><th>Reason</th></tr></thead>
              <tbody>
                @for (row of summary()?.occupancyRejects ?? []; track $index) {
                  <tr><td>{{ row.time }}</td><td>{{ row.strategyKey }}</td><td>{{ row.coin }}</td><td>{{ row.reason }}</td></tr>
                } @empty {
                  <tr><td colspan="4">No occupancy rejects in the last artifact.</td></tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }

      @if (view() === 'price-action') {
        <section class="panel">
          <h2>Price Action registry</h2>
          <p class="tiny">{{ pa()?.confirmation || 'PRICE ACTION LIVE = OFF. Research-only pa_* keys. Isolated book owns SL/TP.' }}</p>
          <div class="strategies-grid">
            @for (row of pa()?.strategies ?? []; track row.templateKey) {
              <article class="card strategy-card">
                <div>
                  <strong>{{ row.name }}</strong>
                  <p class="tiny" style="margin:4px 0 0">{{ row.templateKey }} · {{ row.family }}</p>
                </div>
                <app-status-badge [label]="row.status" [cls]="badgeClass(row.status)" />
                <p class="tiny">{{ row.blurb }}</p>
              </article>
            }
          </div>
        </section>
        <section class="panel">
          <h2>Hypotheses tested</h2>
          <p class="tiny">Multiple-testing caveat: many variants share the same bars. IS_PROMISING is not validation.</p>
          <ul>
            @for (row of pa()?.hypotheses ?? []; track row) {
              <li class="tiny">{{ row }}</li>
            } @empty {
              <li class="tiny">No price-action run yet. Use --price-action after coverage.</li>
            }
          </ul>
        </section>
        <section class="panel">
          <h2>Strategy books</h2>
          <div class="table-scroll">
            <table class="data-table">
              <thead><tr><th>Candidate</th><th>Coin</th><th>TF</th><th>Phase</th><th>Cost</th><th>Status</th><th class="num">n</th><th class="num">PF</th></tr></thead>
              <tbody>
                @for (row of pa()?.books ?? []; track row.candidateId + row.coin + row.timeframe + row.phase + row.costLabel) {
                  <tr>
                    <td>{{ row.candidateId }}</td>
                    <td>{{ row.coin }}</td>
                    <td>{{ row.timeframe }}</td>
                    <td>{{ row.phase }}</td>
                    <td>{{ row.costLabel }}</td>
                    <td><app-status-badge [label]="row.status" [cls]="badgeClass(row.status)" /></td>
                    <td class="num">{{ row.tradeCount }}</td>
                    <td class="num">{{ row.profitFactor == null ? '—' : money(row.profitFactor, 2) }}</td>
                  </tr>
                } @empty {
                  <tr><td colspan="8">No pa_* books yet.</td></tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }

      @if (view() === 'patterns') {
        <section class="panel">
          <h2>Candle sequences</h2>
          <p class="tiny">Forward labels only. Not live features. Not strategy PnL.</p>
          <div class="table-scroll">
            <table class="data-table">
              <thead><tr><th>Coin</th><th>TF</th><th>Sequence</th><th class="num">N</th><th class="num">Fwd3</th><th class="num">Med MFE</th><th class="num">Med MAE</th></tr></thead>
              <tbody>
                @for (row of pa()?.sequences ?? []; track row.coin + row.timeframe + row.name) {
                  <tr>
                    <td>{{ row.coin }}</td>
                    <td>{{ row.timeframe }}</td>
                    <td>{{ row.name }}</td>
                    <td class="num">{{ row.occurrences }}</td>
                    <td class="num">{{ (row.meanFwd3 * 100).toFixed(2) }}%</td>
                    <td class="num">{{ (row.medianMfe * 100).toFixed(2) }}%</td>
                    <td class="num">{{ (row.medianMae * 100).toFixed(2) }}%</td>
                  </tr>
                } @empty {
                  <tr><td colspan="7">No sequence stats yet.</td></tr>
                }
              </tbody>
            </table>
          </div>
        </section>
        <section class="panel">
          <h2>Pattern statistics</h2>
          <div class="table-scroll">
            <table class="data-table">
              <thead><tr><th>Coin</th><th>TF</th><th>Pattern</th><th>Status</th><th class="num">N</th><th class="num">Fwd3</th></tr></thead>
              <tbody>
                @for (row of pa()?.patterns ?? []; track row.coin + row.timeframe + row.patternType + row.status) {
                  <tr>
                    <td>{{ row.coin }}</td>
                    <td>{{ row.timeframe }}</td>
                    <td>{{ row.patternType }}</td>
                    <td>{{ row.status }}</td>
                    <td class="num">{{ row.occurrences }}</td>
                    <td class="num">{{ (row.meanFwd3 * 100).toFixed(2) }}%</td>
                  </tr>
                } @empty {
                  <tr><td colspan="6">No pattern stats yet.</td></tr>
                }
              </tbody>
            </table>
          </div>
        </section>
        <section class="panel">
          <h2>Visual debug</h2>
          <p class="tiny">Select an occurrence to inspect detection vs confirmation. Research only.</p>
          <div class="table-scroll" style="max-height:220px">
            <table class="data-table">
              <thead><tr><th>Coin</th><th>TF</th><th>Pattern</th><th>Direction</th><th>Confirm</th></tr></thead>
              <tbody>
                @for (row of pa()?.occurrences ?? []; track $index) {
                  <tr (click)="selected.set(row)" [class.active]="selected() === row">
                    <td>{{ row.coin }}</td>
                    <td>{{ row.timeframe }}</td>
                    <td>{{ row.patternType }}</td>
                    <td>{{ row.direction }}</td>
                    <td>{{ row.confirmation }}</td>
                  </tr>
                } @empty {
                  <tr><td colspan="5">No stored occurrences.</td></tr>
                }
              </tbody>
            </table>
          </div>
          @if (debugBars().length) {
            <div style="height:320px;margin-top:12px">
              <app-trading-chart [bars]="debugBars()" [symbol]="selected()?.coin || 'BTCUSDT'" [patternOverlay]="overlay()" />
            </div>
          }
        </section>
      }
    </div>
  `,
})
export class ScalpingPage {
  readonly trading = inject(TradingService);
  readonly money = money;
  readonly tabs = [
    { id: 'overview', label: 'Overview' },
    { id: 'strategies', label: 'Strategies' },
    { id: 'price-action', label: 'Price Action' },
    { id: 'patterns', label: 'Pattern research' },
  ];
  readonly view = signal('overview');
  readonly summary = signal<ScalpingResearchSummaryDto | null>(null);
  readonly pa = signal<PriceActionResearchSummaryDto | null>(null);
  readonly selected = signal<PriceActionOccurrenceDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly oos = computed(() => (this.summary()?.books ?? []).filter((row) => row.phase === 'OOS' && row.costLabel === 'BASE'));
  readonly costFragile = computed(() => (this.summary()?.books ?? []).filter((row) => row.status === 'COST_FRAGILE').length);
  readonly visibleBooks = computed((): ScalpingBookDto[] => this.summary()?.books ?? []);
  readonly banner = computed(() => this.pa()?.confirmation || this.summary()?.confirmation || 'LIVE = OFF. Scalping LIVE = OFF. Price Action LIVE = OFF.');
  readonly debugBars = computed((): KlineBarDto[] =>
    (this.selected()?.bars ?? []).map((bar) => ({
      time: bar.time,
      open: bar.open,
      high: bar.high,
      low: bar.low,
      close: bar.close,
      volume: bar.volume,
    })));
  readonly overlay = computed((): PatternOverlay | null => {
    const row = this.selected();
    return row ? { neckline: row.neckline, detection: row.detection, confirmation: row.confirmation, entry: row.entry, points: row.points } : null;
  });

  badgeClass(status: string): string {
    switch (status) {
      case 'IS_PROMISING':
      case 'CONFIRMED':
      case 'FULL_COVERAGE':
        return 'badge-ok';
      case 'RESEARCHING':
      case 'RESEARCH_COMPLETE':
      case 'DETECTED':
        return 'badge-paper';
      case 'COST_FRAGILE':
      case 'OOS_FAILED':
      case 'VALIDATION_FAILED':
      case 'FAILED':
        return 'badge-error';
      case 'DATA_UNAVAILABLE':
      case 'INSUFFICIENT_DATA':
      case 'NO_TRADES':
      case 'NOT_IMPLEMENTED':
      case 'PARTIAL_COVERAGE':
        return 'badge-paused';
      case 'DATA_QUALITY_FAILED':
        return 'badge-error';
      default:
        return 'badge-stopped';
    }
  }

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.summary.set(await this.trading.scalpingResearch());
      this.error.set(null);
    } catch (err) {
      this.error.set(err instanceof Error ? err.message : 'Scalping research is unavailable.');
    }

    try {
      this.pa.set(await this.trading.priceActionResearch());
    } catch {
      this.pa.set(null);
    }
  }
}
