import { Component, computed, effect, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TradingService } from '../../core/trading/trading.service';
import {
  BacktestResultDto,
  SaveStrategyRequest,
  StrategyDto,
  StrategyPreviewDto,
  formatTime,
  money,
  pnlClass,
  signedMoney,
} from '../../core/trading/trading.models';
import { ListQuery, timeValue } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { ToastService } from '../../core/ui/toast.service';
import { IconComponent } from '../../shared/icon/icon';
import {
  ratingFor,
  ratingMeta,
  ratingSortValue,
  starSlots,
  verdictLabel,
} from '../../core/trading/strategy-ratings';

interface StrategyDraft {
  name: string;
  description: string;
  timeframe: string;
  all: boolean;
  symbols: string;
  templateKey: string;
  allowedSide: string;
  emaFast: number;
  emaSlow: number;
  rsiPeriod: number;
  rsiMinimum: number;
  rsiLongMax: number;
  rsiOversold: number;
  rsiOverbought: number;
  macdFast: number;
  macdSlow: number;
  macdSignal: number;
  bbPeriod: number;
  bbStdDev: number;
  donchianLength: number;
  requireVolume: boolean;
  volumeLookback: number;
  minAtrPercent: number;
  maxAtrPercent: number;
  entryLookback: number;
  exitLookback: number;
  atrPeriod: number;
  atrStopMultiplier: number;
  trendEmaPeriod: number;
  volumeFilterEnabled: boolean;
  relativeVolumePeriod: number;
  minimumRelativeVolume: number;
  maxVwapDistanceAtr: number;
  stopAtrMultiplier: number;
  volatilityLookback: number;
  compressionPercentile: number;
  atrExpansionLookback: number;
  breakoutRelativeVolume: number;
  supertrendPeriod: number;
  supertrendMultiplier: number;
  adxPeriod: number;
  minimumAdx: number;
}

const timeframes = ['1m', '3m', '5m', '15m', '30m', '1h', '4h', '1d'];
const templateOptions = [
  { key: 'ema_rsi_trend', label: 'EMA RSI Trend' },
  { key: 'macd_trend', label: 'MACD Trend' },
  { key: 'rsi_pullback', label: 'RSI Pullback' },
  { key: 'bollinger_reversion', label: 'Bollinger Reversion' },
  { key: 'donchian_breakout', label: 'Donchian Breakout' },
  { key: 'turtle_tsm', label: 'Turtle Time-Series Momentum' },
  { key: 'vwap_pullback_trend', label: 'VWAP Pullback Trend' },
  { key: 'volatility_breakout', label: 'Volatility Breakout' },
  { key: 'supertrend_ema_trend', label: 'Supertrend EMA Trend' },
  { key: 'oi_price_momentum', label: 'Open Interest Price Momentum' },
  { key: 'funding_oi_regime', label: 'Funding Rate Price OI Regime' },
  { key: 'vp_vwap_reversion', label: 'Volume Profile VWAP Mean Reversion' },
  { key: 'liq_sweep_reversal', label: 'Liquidity Sweep Reversal' },
  { key: 'liq_sweep_continuation', label: 'Liquidity Sweep Breakout Continuation' },
  { key: 'funding_basis_rv', label: 'Funding Basis Carry Relative Value' },
  { key: 'funding_oi_reversal', label: 'Funding Extreme OI Price Reversal' },
  { key: 'taker_flow_momentum', label: 'Taker Flow Volume Imbalance Momentum' },
  { key: 'oi_price_volume_regime', label: 'OI Price Volume Regime' },
  { key: 'vwap_deviation_reversion', label: 'VWAP Deviation Reversion' },
  { key: 'vwap_breakout_volume', label: 'VWAP Breakout Volume' },
  { key: 'failed_breakout_reversal', label: 'Failed Breakout Reversal' },
  { key: 'vol_squeeze_structure', label: 'Volatility Squeeze Structure Break' },
  { key: 'market_structure_trend', label: 'Market Structure Trend Continuation' },
  { key: 'market_structure_pullback', label: 'Market Structure Pullback' },
  { key: 'atr_normalized_momentum', label: 'ATR-Normalized Momentum' },
  { key: 'mtf_trend_structure', label: 'Multi-Timeframe Trend LTF Structure' },
  { key: 'zscore_mean_reversion', label: 'Z-Score Statistical Mean Reversion' },
  { key: 'crypto_pairs_arb', label: 'Crypto Pairs Statistical Arbitrage' },
  { key: 'xs_relative_strength', label: 'Cross-Sectional Relative Strength Momentum' },
  { key: 'regime_strategy_router', label: 'Regime-Adaptive Strategy Router' },
  { key: 'funding_price_momentum', label: 'Funding Price Momentum' },
  { key: 'funding_extreme_momentum_exhaustion', label: 'Funding Extreme Momentum Exhaustion' },
  { key: 'basis_mean_reversion', label: 'Basis Mean Reversion' },
  { key: 'funding_basis_vwap', label: 'Funding Basis VWAP' },
  { key: 'oi_breakout_confirmation', label: 'OI Breakout Confirmation' },
] as const;
const sideOptions = [
  { key: 'Long', label: 'Long' },
  { key: 'Short', label: 'Short' },
  { key: 'Both', label: 'Both' },
] as const;
const templateLogic: Record<string, string> = {
  ema_rsi_trend:
    'ახალ ტრენდს იწყებს: სწრაფი EMA ნელს კვეთს, RSI ადასტურებს. მიზანი — მიმართულების ცვლილება, სუსტი გადაკვეთების გარეშე.',
  macd_trend:
    'იმპულსის გადაკვეთას მიყვება (MACD × სიგნალი, ჰისტოგრამა, ნელი EMA). მიზანი — ტრენდის გაგრძელება, არა მოკლე ხმაური.',
  rsi_pullback:
    'ტრენდში უკან დახევას იჭერს: RSI oversold/overbought-იდან ბრუნდება. მიზანი — ტრენდში იაფ შესვლა, არა წვერზე ნადირობა.',
  bollinger_reversion:
    'ზოლიდან გადახრილ ფასს შუაში აბრუნებს. მიზანი — ექსტრემის კორექცია, არა გარღვევა.',
  donchian_breakout:
    'ბოლო N სანთლის მაღალ/დაბალ ზოლს არღვევს და იმ მიმართულებით შედის. მიზანი — ახალი ექსტრემის გაგრძელება.',
  turtle_tsm:
    'Systematic trend-following strategy using prior-range breakouts, EMA trend confirmation and ATR-based volatility control.',
  vwap_pullback_trend:
    'Trend-following pullback strategy using VWAP, EMA structure, RSI confirmation and volatility-aware stops.',
  volatility_breakout:
    'Volatility-compression breakout strategy using Bollinger width, ATR expansion and relative volume.',
  supertrend_ema_trend:
    'Trend-following strategy using Supertrend direction, EMA structure and ADX trend-strength confirmation.',
  oi_price_momentum:
    'Futures-specific strategy researching conditional relationships between price movement, open interest, volume and trend.',
  funding_oi_regime:
    'Perpetual-futures strategy researching funding extremes together with price momentum and open-interest regimes.',
  vp_vwap_reversion:
    'Research whether VAL/VAH rejections revert toward POC/VWAP outside strong-trend regimes.',
  liq_sweep_reversal:
    'Research failed breaks of causally confirmed swing highs/lows followed by a close back through the level.',
  liq_sweep_continuation:
    'Research sweeps that hold beyond the level with volume as breakout continuation, separate from reversal.',
  funding_basis_rv:
    'Research funding and basis extremes. Requires aligned funding/index. Not fabricated.',
  funding_oi_reversal:
    'Research extreme funding plus OI and price displacement as a reversal hypothesis.',
  taker_flow_momentum:
    'Research persistent taker buy/sell imbalance with price and volume confirmation.',
  oi_price_volume_regime:
    'Research conditional expectancy of price/OI/volume states without pre-assigned labels.',
  vwap_deviation_reversion:
    'Research ATR-scaled VWAP deviations with rejection and a trend-regime filter.',
  vwap_breakout_volume:
    'Research VWAP-aligned local breakouts with relative volume, on transition only.',
  failed_breakout_reversal:
    'Research Donchian breakouts that fail to hold and close back inside the range.',
  vol_squeeze_structure:
    'Research Bollinger/Keltner compression then expansion with a structure break and volume.',
  market_structure_trend:
    'Research causal HH/HL or LH/LL continuation on a new confirmed swing.',
  market_structure_pullback:
    'Research pullbacks to EMA/VWAP while causal market structure stays intact.',
  atr_normalized_momentum:
    'Research (Close[t]-Close[t-N])/ATR with trend and a persistence transition.',
  mtf_trend_structure:
    'Research last-completed HTF EMA trend with LTF structure/pullback entry.',
  zscore_mean_reversion:
    'Research rolling close Z-score extremes with mean reversion disabled in strong ADX.',
  crypto_pairs_arb:
    'Research rolling cointegrated crypto spreads. Causal pair selection only.',
  xs_relative_strength:
    'Research cross-sectional momentum ranks. Requires a universe snapshot.',
  regime_strategy_router:
    'Deferred interpretable router. Must not be fit on OOS.',
  funding_price_momentum:
    'Research funding with price momentum as continuation vs contrarian. Not LIVE.',
  funding_extreme_momentum_exhaustion:
    'Research funding extremes with weakening momentum. Not LIVE.',
  basis_mean_reversion:
    'Research normalized basis z-score as reversion and continuation separately. Not LIVE.',
  funding_basis_vwap:
    'Research funding + basis + VWAP deviation. Not LIVE.',
  oi_breakout_confirmation:
    'Research whether OI expansion adds information to a volume breakout. OI_SAMPLE_LIMITED. Not LIVE.',
};

const dataDependencies: Record<string, string> = {
  oi_price_momentum: 'Historical open interest, timestamp-aligned. Missing series = DATA_UNAVAILABLE.',
  funding_oi_regime: 'Historical funding + open interest, timestamp-aligned. Missing series = DATA_UNAVAILABLE.',
  funding_basis_rv: 'Historical funding, mark, and index/basis. Missing series = DATA_UNAVAILABLE.',
  funding_oi_reversal: 'Historical funding and open interest. Missing series = DATA_UNAVAILABLE.',
  taker_flow_momentum: 'Kline taker buy volume. Missing field = DATA_UNAVAILABLE.',
  oi_price_volume_regime: 'Historical open interest plus OHLCV. Missing OI = DATA_UNAVAILABLE.',
  crypto_pairs_arb: 'Multi-symbol OHLCV with causal pair windows. Single-book = DATA_UNAVAILABLE.',
  xs_relative_strength: 'Universe snapshot at each timestamp. Single-book = DATA_UNAVAILABLE.',
  mtf_trend_structure: 'Entry OHLCV plus last completed HTF candles only.',
  vp_vwap_reversion: 'OHLCV and volume. Volume profile reconstructed from typical-price × volume.',
  funding_price_momentum: 'OHLCV + settled funding. Missing = DATA_UNAVAILABLE.',
  funding_extreme_momentum_exhaustion: 'OHLCV + settled funding. Missing = DATA_UNAVAILABLE.',
  basis_mean_reversion: 'OHLCV + mark/index/basis. Missing = DATA_UNAVAILABLE.',
  funding_basis_vwap: 'OHLCV + funding + basis. Missing = DATA_UNAVAILABLE.',
  oi_breakout_confirmation: 'OHLCV + OI. OI_SAMPLE_LIMITED (~29d).',
};

const frozenKeys = new Set([
  'ema_rsi_trend',
  'macd_trend',
  'rsi_pullback',
  'bollinger_reversion',
  'donchian_breakout',
]);

function isResearchOnly(key: string | undefined): boolean {
  return !!key && !frozenKeys.has(key);
}

function familyFor(key: string | undefined, fallback?: string): string {
  if (fallback) {
    return fallback;
  }
  switch (key) {
    case 'vp_vwap_reversion':
    case 'vwap_deviation_reversion':
    case 'zscore_mean_reversion':
    case 'crypto_pairs_arb':
    case 'bollinger_reversion':
      return 'MEAN REVERSION';
    case 'liq_sweep_reversal':
    case 'failed_breakout_reversal':
    case 'funding_oi_reversal':
      return 'REVERSAL';
    case 'turtle_tsm':
    case 'volatility_breakout':
    case 'vol_squeeze_structure':
    case 'atr_normalized_momentum':
    case 'liq_sweep_continuation':
    case 'vwap_breakout_volume':
    case 'donchian_breakout':
      return 'BREAKOUT / TREND';
    case 'vwap_pullback_trend':
    case 'supertrend_ema_trend':
    case 'market_structure_trend':
    case 'market_structure_pullback':
    case 'mtf_trend_structure':
      return 'TREND / STRUCTURE';
    case 'regime_strategy_router':
      return 'ROUTER';
    case 'oi_price_momentum':
    case 'funding_oi_regime':
    case 'funding_basis_rv':
    case 'taker_flow_momentum':
    case 'oi_price_volume_regime':
    case 'xs_relative_strength':
    case 'funding_price_momentum':
    case 'funding_extreme_momentum_exhaustion':
    case 'basis_mean_reversion':
    case 'funding_basis_vwap':
    case 'oi_breakout_confirmation':
      return 'FUTURES / FLOW';
    default:
      return 'TREND';
  }
}

function depsFor(key: string): string {
  return dataDependencies[key] || 'Closed kline candles only.';
}

@Component({
  selector: 'app-strategies-page',
  imports: [FormsModule, NgTemplateOutlet, SortBtnComponent, IconComponent],
  styleUrl: './strategies.page.scss',
  template: `
    <div class="strategies-page">
      <header class="strategies-toolbar">
        <div class="strategies-toolbar-actions">
          <label class="field">Use
            <select [ngModel]="useFilter()" (ngModelChange)="useFilter.set($event)">
              <option value="">All</option>
              <option value="paper">გამოიყენე PAPER-ზე</option>
              <option value="weak">სუსტი</option>
              <option value="avoid">არ გამოიყენო</option>
              <option value="blocked">ვერ გაეშვება</option>
            </select>
          </label>
          <label class="field">Family
            <select [ngModel]="familyFilter()" (ngModelChange)="familyFilter.set($event)">
              <option value="">All families</option>
              <option value="TREND">TREND</option>
              <option value="TREND / STRUCTURE">TREND / STRUCTURE</option>
              <option value="BREAKOUT / TREND">BREAKOUT / TREND</option>
              <option value="MEAN REVERSION">MEAN REVERSION</option>
              <option value="REVERSAL">REVERSAL</option>
              <option value="FUTURES / FLOW">FUTURES / FLOW</option>
              <option value="ROUTER">ROUTER</option>
            </select>
          </label>
          <label class="field">Status
            <select [ngModel]="statusFilter()" (ngModelChange)="statusFilter.set($event)">
              <option value="">All statuses</option>
              <option value="VALIDATION_PENDING">VALIDATION_PENDING</option>
              <option value="RESEARCHING">RESEARCHING</option>
              <option value="DATA_UNAVAILABLE">DATA_UNAVAILABLE</option>
            </select>
          </label>
          <label class="field">Timeframe
            <select [ngModel]="timeframeFilter()" (ngModelChange)="timeframeFilter.set($event)">
              <option value="">All TF</option>
              <option value="5m">5m</option>
              <option value="15m">15m</option>
              <option value="1h">1h</option>
            </select>
          </label>
          <label class="field">Direction
            <select [ngModel]="directionFilter()" (ngModelChange)="directionFilter.set($event)">
              <option value="">All</option>
              <option value="Long">Long</option>
              <option value="Short">Short</option>
              <option value="Both">Both</option>
            </select>
          </label>
          <label class="field">Data
            <select [ngModel]="dataFilter()" (ngModelChange)="dataFilter.set($event)">
              <option value="">All data</option>
              <option value="ohlcv">OHLCV</option>
              <option value="oi">Open interest</option>
              <option value="funding">Funding / basis</option>
              <option value="taker">Taker flow</option>
              <option value="universe">Universe / pairs</option>
            </select>
          </label>
          <label class="field">Coin
            <select [(ngModel)]="previewCoin">
              @for (coin of coins(); track coin.symbol) {
                <option [value]="coin.symbol">{{ coin.symbol }} · {{ coin.displayName }}</option>
              }
            </select>
          </label>
          <button class="btn accent" type="button" [disabled]="busy" (click)="beginCreate()">Add strategy</button>
        </div>
        <div class="list-sorts">
          <app-sort-btn column="name" [query]="list">Name</app-sort-btn>
          <app-sort-btn column="tf" [query]="list">TF</app-sort-btn>
          <app-sort-btn column="rating" [query]="list">Rating</app-sort-btn>
        </div>
      </header>

      @if (creating(); as form) {
        <section class="panel">
          <div class="section-head">
            <h2>New strategy</h2>
            <button class="btn ghost sm" type="button" (click)="cancel()">Cancel</button>
          </div>
          <ng-container [ngTemplateOutlet]="editor" [ngTemplateOutletContext]="{ $implicit: form }" />
        </section>
      }

      <div class="strategies-grid">
        @for (row of visible(); track row.id) {
          <section class="panel strategy-card" [class.is-editing]="editingId() === row.id">
            <div class="strategy-card-head">
              <div>
                <div class="strategy-title-row">
                  <strong>{{ row.name }}</strong>
                  @if (ratingFor(row.templateKey); as rate) {
                    <div class="strategy-rating" [attr.title]="rate.note + ' ' + ratingMeta(rate)">
                      <span class="strategy-stars" [attr.aria-label]="verdictLabel(row.templateKey) + ', ' + rate.stars + ' of 5'">
                        @for (n of starSlots; track n) {
                          <app-icon name="star" [size]="14" [filled]="n <= rate.stars" [class.is-on]="n <= rate.stars" />
                        }
                      </span>
                      <span class="strategy-verdict" [class.is-paper]="rate.verdict === 'paper'" [class.is-weak]="rate.verdict === 'weak'" [class.is-avoid]="rate.verdict === 'avoid'" [class.is-blocked]="rate.verdict === 'blocked'">{{ verdictLabel(row.templateKey) }}</span>
                    </div>
                  }
                </div>
                @if (ratingFor(row.templateKey); as rate) {
                  <p class="strategy-rating-meta">{{ rate.note }}</p>
                  <p class="strategy-rating-meta">{{ ratingMeta(rate) }}</p>
                }
                <div class="strategy-pills">
                  <span class="badge badge-stopped">{{ row.timeframe }}</span>
                  <span class="badge" [class.badge-long]="(row.allowedSide || 'Long') === 'Long'" [class.badge-short]="row.allowedSide === 'Short'" [class.badge-paper]="row.allowedSide === 'Both'">{{ row.allowedSide || 'Long' }}</span>
                  <span class="badge" [class.badge-running]="qualityOn(row)" [class.badge-stopped]="!qualityOn(row)">
                    {{ qualityOn(row) ? 'Quality on' : 'Quality off' }}
                  </span>
                  <span class="badge" [class.badge-running]="row.appliesToAllSymbols" [class.badge-paused]="!row.appliesToAllSymbols">
                    {{ row.appliesToAllSymbols ? 'All coins' : ((row.allowedSymbols?.length ?? 0) + ' coins') }}
                  </span>
                  @if (row.versionUsed) {
                    <span class="badge badge-paused">v{{ row.version }} used</span>
                  }
                  <span class="badge" [class.badge-running]="row.isEnabled !== false" [class.badge-stopped]="row.isEnabled === false">
                    {{ row.isEnabled === false ? 'Disabled' : 'Enabled' }}
                  </span>
                  <span class="badge badge-paused">{{ familyFor(row.templateKey, row.family) }}</span>
                  @if (isResearchOnly(row.templateKey)) {
                    <span class="badge badge-stopped">RESEARCH ONLY</span>
                  }
                  <span class="badge badge-paused">{{ row.validationStatus || 'VALIDATION_PENDING' }}</span>
                  <span class="badge badge-paused">{{ (row.supportedDirections?.length ? row.supportedDirections.join('/') : 'LONG/SHORT') }}</span>
                </div>
              </div>
              @if (editingId() !== row.id) {
                <div class="strategy-actions">
                  <button class="btn ghost sm" type="button" [disabled]="busy" (click)="beginEdit(row)">Edit</button>
                  <button class="btn ghost sm" type="button" [disabled]="busy" (click)="toggleEnabled(row)">
                    {{ row.isEnabled === false ? 'Enable' : 'Disable' }}
                  </button>
                  <button class="btn accent sm" type="button" [disabled]="previewBusy() === row.id" (click)="preview(row)">
                    {{ previewBusy() === row.id ? 'Preview…' : 'Preview' }}
                  </button>
                </div>
              }
            </div>

            @if (editingId() === row.id && draft(); as form) {
              <ng-container [ngTemplateOutlet]="editor" [ngTemplateOutletContext]="{ $implicit: form }" />
            } @else {
              <p class="strategy-blurb">{{ blurbFor(row.templateKey) }}</p>
              <p class="tiny">{{ row.dataDependencies || depsFor(row.templateKey) }} · TF {{ (row.supportedTimeframes || ['5m','15m','1h']).join(', ') }} · research, not LIVE</p>
              @if (previews()[row.id]; as snap) {
                <div class="strategy-preview">
                  <div class="strategy-preview-head">
                    <span class="tiny">{{ snap.symbol }} · last <span [class]="signalClass(snap.lastSignal)">{{ snap.lastSignal }}</span></span>
                    <span class="tiny">{{ snap.lastReason }}</span>
                  </div>
                  <div class="table-scroll" style="max-height:220px">
                    <table class="data-table">
                      <thead>
                        <tr>
                          <th>Time</th>
                          <th>Signal</th>
                          <th class="num">Close</th>
                          <th>Reason</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (bar of newestBars(snap.bars); track bar.time) {
                          <tr>
                            <td>{{ formatTime(bar.time) }}</td>
                            <td [class]="signalClass(bar.signal)">{{ bar.signal }}</td>
                            <td class="num">{{ money(bar.close, 4) }}</td>
                            <td>{{ bar.reason }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                </div>
              }
            }
          </section>
        } @empty {
          @if (!creating()) {
            <section class="panel">
              <div class="empty-state">
                <strong>No strategies yet</strong>
                <p>Add one to assign coins. This does not start bots.</p>
              </div>
            </section>
          }
        }
      </div>
    </div>

    <ng-template #editor let-form>
      <div class="form strategy-editor">
        <div class="form-grid">
          <label class="field">Name <input [(ngModel)]="form.name" /></label>
          <label class="field">Timeframe
            <select [(ngModel)]="form.timeframe">
              @for (tf of timeframes; track tf) {
                <option [value]="tf">{{ tf }}</option>
              }
            </select>
          </label>
        </div>
        <div class="form-grid">
          <label class="field">Template
            <select [(ngModel)]="form.templateKey">
              @for (row of templates; track row.key) {
                <option [value]="row.key">{{ row.label }} · {{ verdictLabel(row.key) }}</option>
              }
            </select>
          </label>
          <label class="field">Allowed side
            <select [(ngModel)]="form.allowedSide">
              @for (row of sides; track row.key) {
                <option [value]="row.key">{{ row.label }}</option>
              }
            </select>
          </label>
        </div>
        <p class="strategy-hint">{{ blurbFor(form.templateKey) }}</p>
        <p class="tiny">{{ depsFor(form.templateKey) }} · LONG and SHORT · 5m / 15m / 1h · not auto-promoted to Paper or LIVE</p>
        @if (ratingFor(form.templateKey); as rate) {
          <p class="strategy-rating-note">{{ verdictLabel(form.templateKey) }} · {{ rate.stars }}/5 · {{ rate.note }} {{ ratingMeta(rate) }}</p>
        }
        <label class="field">Note <textarea rows="2" [(ngModel)]="form.description"></textarea></label>
        @if (form.templateKey === 'ema_rsi_trend') {
          <div class="form-grid cols-3">
            <label class="field">EMA fast <input type="number" [(ngModel)]="form.emaFast" /></label>
            <label class="field">EMA slow <input type="number" [(ngModel)]="form.emaSlow" /></label>
            <label class="field">RSI period <input type="number" [(ngModel)]="form.rsiPeriod" /></label>
            <label class="field">RSI min <input type="number" [(ngModel)]="form.rsiMinimum" /></label>
            <label class="field">RSI long max <input type="number" [(ngModel)]="form.rsiLongMax" /></label>
          </div>
        }
        @if (form.templateKey === 'macd_trend') {
          <div class="form-grid cols-3">
            <label class="field">MACD fast <input type="number" [(ngModel)]="form.macdFast" /></label>
            <label class="field">MACD slow <input type="number" [(ngModel)]="form.macdSlow" /></label>
            <label class="field">MACD signal <input type="number" [(ngModel)]="form.macdSignal" /></label>
            <label class="field">Slow EMA <input type="number" [(ngModel)]="form.emaSlow" /></label>
          </div>
        }
        @if (form.templateKey === 'rsi_pullback') {
          <div class="form-grid cols-3">
            <label class="field">Slow EMA <input type="number" [(ngModel)]="form.emaSlow" /></label>
            <label class="field">RSI period <input type="number" [(ngModel)]="form.rsiPeriod" /></label>
            <label class="field">Oversold <input type="number" [(ngModel)]="form.rsiOversold" /></label>
            <label class="field">Overbought <input type="number" [(ngModel)]="form.rsiOverbought" /></label>
          </div>
        }
        @if (form.templateKey === 'bollinger_reversion') {
          <div class="form-grid cols-3">
            <label class="field">BB period <input type="number" [(ngModel)]="form.bbPeriod" /></label>
            <label class="field">BB stddev <input type="number" step="0.1" [(ngModel)]="form.bbStdDev" /></label>
            <label class="field">Slow EMA <input type="number" [(ngModel)]="form.emaSlow" /></label>
          </div>
        }
        @if (form.templateKey === 'donchian_breakout') {
          <div class="form-grid cols-3">
            <label class="field">Donchian length <input type="number" [(ngModel)]="form.donchianLength" /></label>
            <label class="field">EMA fast <input type="number" [(ngModel)]="form.emaFast" /></label>
            <label class="field">EMA slow <input type="number" [(ngModel)]="form.emaSlow" /></label>
          </div>
        }
        @if (form.templateKey === 'turtle_tsm') {
          <div class="form-grid cols-3">
            <label class="field">Entry lookback <input type="number" [(ngModel)]="form.entryLookback" /></label>
            <label class="field">Exit lookback <input type="number" [(ngModel)]="form.exitLookback" /></label>
            <label class="field">Trend EMA <input type="number" [(ngModel)]="form.trendEmaPeriod" /></label>
            <label class="field">ATR period <input type="number" [(ngModel)]="form.atrPeriod" /></label>
            <label class="field">ATR stop × <input type="number" step="0.1" [(ngModel)]="form.atrStopMultiplier" /></label>
            <label class="field">Min rel volume <input type="number" step="0.1" [(ngModel)]="form.minimumRelativeVolume" /></label>
          </div>
        }
        @if (form.templateKey === 'vwap_pullback_trend') {
          <div class="form-grid cols-3">
            <label class="field">EMA fast <input type="number" [(ngModel)]="form.emaFast" /></label>
            <label class="field">EMA slow <input type="number" [(ngModel)]="form.emaSlow" /></label>
            <label class="field">Max VWAP distance ATR <input type="number" step="0.05" [(ngModel)]="form.maxVwapDistanceAtr" /></label>
            <label class="field">Stop ATR × <input type="number" step="0.1" [(ngModel)]="form.stopAtrMultiplier" /></label>
            <label class="field">Min rel volume <input type="number" step="0.1" [(ngModel)]="form.minimumRelativeVolume" /></label>
            <label class="field">RSI period <input type="number" [(ngModel)]="form.rsiPeriod" /></label>
          </div>
        }
        @if (form.templateKey === 'volatility_breakout') {
          <div class="form-grid cols-3">
            <label class="field">BB period <input type="number" [(ngModel)]="form.bbPeriod" /></label>
            <label class="field">BB stddev <input type="number" step="0.1" [(ngModel)]="form.bbStdDev" /></label>
            <label class="field">Compression % <input type="number" step="0.05" [(ngModel)]="form.compressionPercentile" /></label>
            <label class="field">Vol lookback <input type="number" [(ngModel)]="form.volatilityLookback" /></label>
            <label class="field">Breakout rel vol <input type="number" step="0.1" [(ngModel)]="form.breakoutRelativeVolume" /></label>
            <label class="field">ATR expansion lookback <input type="number" [(ngModel)]="form.atrExpansionLookback" /></label>
          </div>
        }
        @if (form.templateKey === 'supertrend_ema_trend') {
          <div class="form-grid cols-3">
            <label class="field">ST ATR period <input type="number" [(ngModel)]="form.supertrendPeriod" /></label>
            <label class="field">ST multiplier <input type="number" step="0.1" [(ngModel)]="form.supertrendMultiplier" /></label>
            <label class="field">EMA fast <input type="number" [(ngModel)]="form.emaFast" /></label>
            <label class="field">EMA slow <input type="number" [(ngModel)]="form.emaSlow" /></label>
            <label class="field">ADX period <input type="number" [(ngModel)]="form.adxPeriod" /></label>
            <label class="field">Min ADX <input type="number" [(ngModel)]="form.minimumAdx" /></label>
          </div>
        }
        @if (form.templateKey === 'oi_price_momentum' || form.templateKey === 'funding_oi_regime' || form.templateKey === 'crypto_pairs_arb' || form.templateKey === 'xs_relative_strength' || form.templateKey === 'taker_flow_momentum') {
          <p class="tiny">This template stays DATA_UNAVAILABLE until the required historical series is timestamp-aligned. It will not invent data or auto-enable LIVE.</p>
        }
        @if (form.templateKey === 'funding_basis_rv' || form.templateKey === 'funding_oi_reversal' || form.templateKey === 'oi_price_volume_regime' || form.templateKey === 'funding_price_momentum' || form.templateKey === 'funding_extreme_momentum_exhaustion' || form.templateKey === 'basis_mean_reversion' || form.templateKey === 'funding_basis_vwap' || form.templateKey === 'oi_breakout_confirmation') {
          <p class="tiny">Phase 3 research-only. Continuation and contrarian are tested independently. OI books are OI_SAMPLE_LIMITED (~29d). Not LIVE. Not auto-promoted.</p>
        }
        @if (form.templateKey === 'regime_strategy_router') {
          <p class="tiny">Router is deferred until independent candidates are validated. Routing rules will not be fit on OOS. RESEARCH ONLY.</p>
        }
        <div class="strategy-filters">
          <div class="form-grid cols-3">
            <label class="field">
              <span class="settings-check">
                <input type="checkbox" [(ngModel)]="form.requireVolume" />
                Volume confirmation
              </span>
            </label>
            <label class="field">Volume lookback <input type="number" [(ngModel)]="form.volumeLookback" /></label>
            <label class="field">Min ATR% <input type="number" step="0.01" [(ngModel)]="form.minAtrPercent" /></label>
            <label class="field">Max ATR% <input type="number" step="0.01" [(ngModel)]="form.maxAtrPercent" /></label>
          </div>
          <p class="tiny">Filters skip some noisy bars. They do not cap loss or place SL/TP.</p>
        </div>
        <label class="field">
          <span class="settings-check">
            <input type="checkbox" [(ngModel)]="form.all" />
            Apply to every USD-M USDT perpetual
          </span>
        </label>
        @if (!form.all) {
          <label class="field">Assigned coins
            <textarea rows="3" [(ngModel)]="form.symbols" placeholder="BTCUSDT, ETHUSDT, SOLUSDT"></textarea>
          </label>
          <p class="tiny">Comma-separated coins. This does not start bots.</p>
        }
        <div class="btn-row">
          <button class="btn accent" type="button" [disabled]="busy" (click)="save()">Save strategy</button>
          <button class="btn secondary" type="button" [disabled]="busy" (click)="cancel()">Cancel</button>
        </div>
      </div>
    </ng-template>
  `,
})
export class StrategiesPage {
  readonly trading = inject(TradingService);
  private readonly toast = inject(ToastService);
  readonly timeframes = timeframes;
  readonly templates = templateOptions;
  readonly sides = sideOptions;
  readonly formatTime = formatTime;
  readonly money = money;

  newestBars(bars: StrategyPreviewDto['bars']): StrategyPreviewDto['bars'] {
    return [...bars].sort((a, b) => timeValue(b.time) - timeValue(a.time));
  }
  readonly creating = signal<StrategyDraft | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly draft = signal<StrategyDraft | null>(null);
  readonly previews = signal<Record<string, StrategyPreviewDto>>({});
  readonly previewBusy = signal<string | null>(null);
  previewCoin = 'BTCUSDT';
  readonly starSlots = starSlots;
  readonly ratingFor = ratingFor;
  readonly ratingMeta = ratingMeta;
  readonly verdictLabel = verdictLabel;
  readonly depsFor = depsFor;
  readonly isResearchOnly = isResearchOnly;
  readonly familyFor = familyFor;
  readonly familyFilter = signal('');
  readonly useFilter = signal('');
  readonly statusFilter = signal('');
  readonly timeframeFilter = signal('');
  readonly directionFilter = signal('');
  readonly dataFilter = signal('');
  readonly list = (() => {
    const query = new ListQuery();
    query.key.set('rating');
    query.dir.set('desc');
    return query;
  })();
  readonly visible = computed(() => {
    const family = this.familyFilter();
    const use = this.useFilter();
    const status = this.statusFilter();
    const tf = this.timeframeFilter();
    const direction = this.directionFilter();
    const data = this.dataFilter();
    const rows = this.list.apply(
      this.trading.strategies(),
      (row) => [row.name, row.description, row.timeframe, row.templateKey, row.family, row.validationStatus, row.allowedSymbols?.join(' ')],
      {
        name: (row) => row.name,
        tf: (row) => row.timeframe,
        rating: (row) => ratingSortValue(row.templateKey),
      },
    );
    return rows.filter((row) => {
      if (family && familyFor(row.templateKey, row.family) !== family) {
        return false;
      }
      if (use && ratingFor(row.templateKey).verdict !== use) {
        return false;
      }
      if (status && (row.validationStatus || '') !== status) {
        return false;
      }
      if (tf && row.timeframe !== tf && !(row.supportedTimeframes || []).includes(tf)) {
        return false;
      }
      if (direction && (row.allowedSide || 'Long') !== direction) {
        return false;
      }
      if (data) {
        const deps = (row.dataDependencies || depsFor(row.templateKey)).toLowerCase();
        if (data === 'ohlcv' && (deps.includes('open interest') || deps.includes('funding') || deps.includes('taker') || deps.includes('universe'))) {
          return false;
        }
        if (data === 'oi' && !deps.includes('open interest')) {
          return false;
        }
        if (data === 'funding' && !deps.includes('funding')) {
          return false;
        }
        if (data === 'taker' && !deps.includes('taker')) {
          return false;
        }
        if (data === 'universe' && !deps.includes('universe') && !deps.includes('pair')) {
          return false;
        }
      }
      return true;
    });
  });
  readonly coins = computed(() => {
    const rows = this.trading.markets();
    if (rows.length) {
      return rows.map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
    }
    return this.trading.tickers().map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
  });
  busy = false;

  constructor() {
    void this.trading.refreshCatalog();
    void this.trading.refreshMarkets();
  }

  qualityOn(row: StrategyDto): boolean {
    return !!row.requireVolume || (row.minAtrPercent ?? 0) > 0 || (row.maxAtrPercent ?? 0) > 0;
  }

  signalClass(signal: string | undefined): string {
    switch ((signal || '').toLowerCase()) {
      case 'buy':
        return 'sig-buy';
      case 'sell':
        return 'sig-sell';
      case 'exit':
        return 'sig-exit';
      case 'hold':
        return 'sig-hold';
      default:
        return 'sig-none';
    }
  }

  blurbFor(key: string): string {
    return (
      templateLogic[key] ||
      'Closed-candle signal only. Isolated size, stop loss, and take profit stay on Risk.'
    );
  }

  beginCreate(): void {
    this.editingId.set(null);
    this.draft.set(null);
    this.creating.set(blankStrategy());
  }

  beginEdit(row: StrategyDto): void {
    this.creating.set(null);
    this.editingId.set(row.id);
    this.draft.set(fromStrategy(row));
  }

  cancel(): void {
    this.creating.set(null);
    this.editingId.set(null);
    this.draft.set(null);
  }

  async toggleEnabled(row: StrategyDto): Promise<void> {
    this.busy = true;
    try {
      const enabled = row.isEnabled === false;
      await this.trading.setStrategyEnabled(row.id, enabled);
      await this.trading.refreshCatalog();
      this.toast.show(
        enabled ? 'Strategy enabled' : 'Strategy disabled',
        enabled
          ? 'It can be assigned to bots again. This does not start bots.'
          : 'Bots cannot start on it. The template is not deleted.',
        'success',
      );
    } catch {
      this.toast.show('Update blocked', 'Could not change enabled state.', 'error');
    } finally {
      this.busy = false;
    }
  }

  async preview(row: StrategyDto): Promise<void> {
    this.previewBusy.set(row.id);
    try {
      const snap = await this.trading.previewStrategy(row.id, this.previewCoin);
      this.previews.update((current) => ({ ...current, [row.id]: snap }));
    } catch {
      this.toast.show('Preview blocked', 'Could not load closed candles for that coin.', 'error');
    } finally {
      this.previewBusy.set(null);
    }
  }

  async save(): Promise<void> {
    const form = this.creating() ?? this.draft();
    const id = this.creating() ? null : this.editingId();
    if (!form) {
      return;
    }
    const body = toRequest(form);
    this.busy = true;
    try {
      const saved = id
        ? await this.trading.updateStrategy(id, body)
        : await this.trading.createStrategy(body);
      await this.trading.refreshCatalog();
      this.cancel();
      this.toast.show(
        id ? 'Strategy saved' : 'Strategy added',
        saved.versionUsed
          ? `Saved as version ${saved.version}. Running bots keep the previous version until you stop and start them.`
          : (form.all ? 'Can run on every USDT-M coin.' : `Assigned to ${body.symbols.length} coin(s).`),
        'success',
      );
    } catch {
      this.toast.show('Save blocked', 'Check the name, template values, and coin assignment.', 'error');
    } finally {
      this.busy = false;
    }
  }
}

function blankStrategy(): StrategyDraft {
  return {
    name: '',
    description: '',
    timeframe: '5m',
    all: true,
    symbols: '',
    templateKey: 'ema_rsi_trend',
    allowedSide: 'Both',
    emaFast: 20,
    emaSlow: 50,
    rsiPeriod: 14,
    rsiMinimum: 50,
    rsiLongMax: 68,
    rsiOversold: 30,
    rsiOverbought: 70,
    macdFast: 12,
    macdSlow: 26,
    macdSignal: 9,
    bbPeriod: 20,
    bbStdDev: 2,
    donchianLength: 20,
    requireVolume: true,
    volumeLookback: 20,
    minAtrPercent: 0.15,
    maxAtrPercent: 4,
    entryLookback: 20,
    exitLookback: 10,
    atrPeriod: 14,
    atrStopMultiplier: 2,
    trendEmaPeriod: 50,
    volumeFilterEnabled: true,
    relativeVolumePeriod: 20,
    minimumRelativeVolume: 1,
    maxVwapDistanceAtr: 0.75,
    stopAtrMultiplier: 1.5,
    volatilityLookback: 100,
    compressionPercentile: 0.2,
    atrExpansionLookback: 20,
    breakoutRelativeVolume: 1.2,
    supertrendPeriod: 10,
    supertrendMultiplier: 3,
    adxPeriod: 14,
    minimumAdx: 20,
  };
}

function fromStrategy(row: StrategyDto): StrategyDraft {
  return {
    name: row.name,
    description: row.description ?? '',
    timeframe: row.timeframe || '5m',
    all: row.appliesToAllSymbols,
    symbols: (row.allowedSymbols ?? []).join(', '),
    templateKey: row.templateKey || 'ema_rsi_trend',
    allowedSide: row.allowedSide || 'Long',
    emaFast: row.emaFast ?? 20,
    emaSlow: row.emaSlow ?? 50,
    rsiPeriod: row.rsiPeriod ?? 14,
    rsiMinimum: row.rsiMinimum ?? 50,
    rsiLongMax: row.rsiLongMax ?? 68,
    rsiOversold: row.rsiOversold ?? 30,
    rsiOverbought: row.rsiOverbought ?? 70,
    macdFast: row.macdFast ?? 12,
    macdSlow: row.macdSlow ?? 26,
    macdSignal: row.macdSignal ?? 9,
    bbPeriod: row.bbPeriod ?? 20,
    bbStdDev: row.bbStdDev ?? 2,
    donchianLength: row.donchianLength ?? 20,
    requireVolume: !!row.requireVolume,
    volumeLookback: row.volumeLookback ?? 20,
    minAtrPercent: row.minAtrPercent ?? 0,
    maxAtrPercent: row.maxAtrPercent ?? 0,
    entryLookback: row.entryLookback ?? 20,
    exitLookback: row.exitLookback ?? 10,
    atrPeriod: row.atrPeriod ?? 14,
    atrStopMultiplier: row.atrStopMultiplier ?? 2,
    trendEmaPeriod: row.trendEmaPeriod ?? 50,
    volumeFilterEnabled: row.volumeFilterEnabled !== false,
    relativeVolumePeriod: row.relativeVolumePeriod ?? 20,
    minimumRelativeVolume: row.minimumRelativeVolume ?? 1,
    maxVwapDistanceAtr: row.maxVwapDistanceAtr ?? 0.75,
    stopAtrMultiplier: row.stopAtrMultiplier ?? 1.5,
    volatilityLookback: row.volatilityLookback ?? 100,
    compressionPercentile: row.compressionPercentile ?? 0.2,
    atrExpansionLookback: row.atrExpansionLookback ?? 20,
    breakoutRelativeVolume: row.breakoutRelativeVolume ?? 1.2,
    supertrendPeriod: row.supertrendPeriod ?? 10,
    supertrendMultiplier: row.supertrendMultiplier ?? 3,
    adxPeriod: row.adxPeriod ?? 14,
    minimumAdx: row.minimumAdx ?? 20,
  };
}

function parseSymbols(value: string): string[] {
  return value.split(/[\s,]+/).map((item) => item.trim().toUpperCase()).filter(Boolean);
}

function toRequest(form: StrategyDraft): SaveStrategyRequest {
  return {
    name: form.name.trim(),
    description: form.description.trim(),
    timeframe: form.timeframe,
    appliesToAllSymbols: form.all,
    symbols: parseSymbols(form.symbols),
    templateKey: form.templateKey,
    allowedSide: form.allowedSide,
    emaFast: Number(form.emaFast),
    emaSlow: Number(form.emaSlow),
    rsiPeriod: Number(form.rsiPeriod),
    rsiMinimum: Number(form.rsiMinimum),
    rsiLongMax: Number(form.rsiLongMax),
    rsiOversold: Number(form.rsiOversold),
    rsiOverbought: Number(form.rsiOverbought),
    macdFast: Number(form.macdFast),
    macdSlow: Number(form.macdSlow),
    macdSignal: Number(form.macdSignal),
    bbPeriod: Number(form.bbPeriod),
    bbStdDev: Number(form.bbStdDev),
    donchianLength: Number(form.donchianLength),
    requireVolume: !!form.requireVolume,
    volumeLookback: Number(form.volumeLookback),
    minAtrPercent: Number(form.minAtrPercent),
    maxAtrPercent: Number(form.maxAtrPercent),
    entryLookback: Number(form.entryLookback),
    exitLookback: Number(form.exitLookback),
    atrPeriod: Number(form.atrPeriod),
    atrStopMultiplier: Number(form.atrStopMultiplier),
    trendEmaPeriod: Number(form.trendEmaPeriod),
    volumeFilterEnabled: form.volumeFilterEnabled !== false,
    relativeVolumePeriod: Number(form.relativeVolumePeriod),
    minimumRelativeVolume: Number(form.minimumRelativeVolume),
    maxVwapDistanceAtr: Number(form.maxVwapDistanceAtr),
    stopAtrMultiplier: Number(form.stopAtrMultiplier),
    volatilityLookback: Number(form.volatilityLookback),
    compressionPercentile: Number(form.compressionPercentile),
    atrExpansionLookback: Number(form.atrExpansionLookback),
    breakoutRelativeVolume: Number(form.breakoutRelativeVolume),
    supertrendPeriod: Number(form.supertrendPeriod),
    supertrendMultiplier: Number(form.supertrendMultiplier),
    adxPeriod: Number(form.adxPeriod),
    minimumAdx: Number(form.minimumAdx),
  };
}

@Component({
  selector: 'app-backtesting-page',
  imports: [FormsModule, SortBtnComponent],
  template: `
    <section class="split-2">
      <aside class="panel">
        <h2>Configuration</h2>
        <div class="form" style="max-width:none">
          <label class="field">Coin
            <select [(ngModel)]="symbol">
              @for (row of coins(); track row.symbol) {
                <option [value]="row.symbol">{{ row.symbol }} · {{ row.displayName }}</option>
              }
            </select>
          </label>
          <label class="field">Timeframe
            <select [(ngModel)]="timeframe">
              <option value="5m">5m</option>
              <option value="15m">15m</option>
              <option value="1h">1h</option>
              <option value="1d">1d</option>
            </select>
          </label>
          <label class="field">Strategy
            <select [(ngModel)]="strategyId">
              @for (row of trading.strategies(); track row.id) {
                <option [value]="row.id">{{ row.name }} · v{{ row.version }}</option>
              }
            </select>
          </label>
          <label class="field">From <input type="date" [(ngModel)]="from" /></label>
          <label class="field">To <input type="date" [(ngModel)]="to" /></label>
          <label class="field">Initial capital <input type="number" [(ngModel)]="capital" /></label>
          <p class="tiny">Sizing uses the active Isolated book {{ trading.risk()?.name || '—' }} · {{ trading.risk()?.riskPerTradePercent ?? '—' }}% R · {{ trading.risk()?.stopLossPercent ?? '—' }}% SL · {{ trading.risk()?.takeProfitPercent ?? '—' }}% TP · {{ trading.risk()?.maxLeverage ?? '—' }}x. Change it on Risk.</p>
          <label class="field">Fees % <input type="number" step="0.01" [(ngModel)]="fees" /></label>
          <label class="field">Slippage % <input type="number" step="0.01" [(ngModel)]="slippage" /></label>
          <button class="btn accent" type="button" [disabled]="busy() || !strategyId" (click)="run()">
            {{ busy() ? 'Running…' : 'Run backtest' }}
          </button>
        </div>
      </aside>
      <section class="panel">
        <h2>Results</h2>
        @if (result(); as row) {
          <p class="tiny" style="margin:0 0 12px">{{ row.strategyName }} v{{ row.strategyVersion }} · {{ row.symbol }} · {{ row.timeframe }} · {{ row.barsUsed }} bars</p>
          <div class="kpi-row">
            <article class="card"><div class="metric-label">Net Profit</div><div class="metric-value" [class]="pnlCls(row.netProfit)">{{ signed(row.netProfit) }}</div></article>
            <article class="card"><div class="metric-label">Return</div><div class="metric-value" [class]="pnlCls(row.returnPercent)">{{ pct(row.returnPercent) }}</div></article>
            <article class="card"><div class="metric-label">Win Rate</div><div class="metric-value">{{ pct(row.winRate) }}</div></article>
            <article class="card"><div class="metric-label">Max Drawdown</div><div class="metric-value" [class]="row.maximumDrawdown ? 'pnl-neg' : ''">{{ pct(-Math.abs(row.maximumDrawdown)) }}</div></article>
          </div>
          <div class="kpi-row" style="margin-top:12px">
            <article class="card"><div class="metric-label">Trades</div><div class="metric-value">{{ row.numberOfTrades }}</div></article>
            <article class="card"><div class="metric-label">Profit factor</div><div class="metric-value">{{ money(row.profitFactor, 2) }}</div></article>
            <article class="card"><div class="metric-label">Fees</div><div class="metric-value">{{ money(row.feesPaid) }}</div></article>
            <article class="card"><div class="metric-label">Final equity</div><div class="metric-value">{{ money(row.finalBalance) }}</div></article>
          </div>
          @if (row.trades.length === 0) {
            <p class="empty-state">No trades in this window. The engine ran, but entry conditions were not met.</p>
          } @else {
            <div class="table-scroll" style="max-height:360px;margin-top:16px">
              <table class="data-table">
                <thead>
                  <tr>
                    <th><app-sort-btn column="opened" [query]="tradeQuery">Opened</app-sort-btn></th>
                    <th><app-sort-btn column="closed" [query]="tradeQuery">Closed</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="qty" [query]="tradeQuery" align="end">Qty</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="entry" [query]="tradeQuery" align="end">Entry</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="exit" [query]="tradeQuery" align="end">Exit</app-sort-btn></th>
                    <th class="num"><app-sort-btn column="pnl" [query]="tradeQuery" align="end">PnL</app-sort-btn></th>
                    <th><app-sort-btn column="reason" [query]="tradeQuery">Reason</app-sort-btn></th>
                  </tr>
                </thead>
                <tbody>
                  @for (trade of visibleTrades(); track $index) {
                    <tr>
                      <td>{{ formatTime(trade.openedAt) }}</td>
                      <td>{{ formatTime(trade.closedAt) }}</td>
                      <td class="num">{{ money(trade.quantity, 4) }}</td>
                      <td class="num">{{ money(trade.entryPrice, 4) }}</td>
                      <td class="num">{{ money(trade.exitPrice, 4) }}</td>
                      <td class="num" [class]="pnlCls(trade.pnL)">{{ signed(trade.pnL) }}</td>
                      <td>{{ trade.reason }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        } @else {
          <p class="empty-state">Run a backtest to see net profit, win rate, drawdown, and fills from Binance history.</p>
        }
      </section>
    </section>
  `,
})
export class BacktestingPage {
  readonly trading = inject(TradingService);
  private readonly toast = inject(ToastService);
  readonly money = money;
  readonly formatTime = formatTime;
  readonly Math = Math;
  symbol = 'BTCUSDT';
  timeframe = '1h';
  strategyId = '';
  from = dateInput(daysAgo(90));
  to = dateInput(new Date());
  capital = 10000;
  fees = 0.04;
  slippage = 0.02;
  readonly busy = signal(false);
  readonly result = signal<BacktestResultDto | null>(null);
  readonly tradeQuery = new ListQuery();
  readonly visibleTrades = computed(() =>
    this.tradeQuery.apply(
      this.result()?.trades ?? [],
      (trade) => [trade.reason, trade.pnL],
      {
        opened: (trade) => timeValue(trade.openedAt),
        closed: (trade) => timeValue(trade.closedAt),
        qty: (trade) => trade.quantity,
        entry: (trade) => trade.entryPrice,
        exit: (trade) => trade.exitPrice,
        pnl: (trade) => trade.pnL,
        reason: (trade) => trade.reason,
      },
    ),
  );
  readonly coins = computed(() => {
    const rows = this.trading.markets();
    if (rows.length) {
      return rows.map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
    }
    return this.trading.tickers().map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
  });

  constructor() {
    void this.trading.refreshCatalog();
    void this.trading.refreshRisk();
    void this.trading.refreshMarkets();
    effect(() => {
      const first = this.trading.strategies()[0];
      if (first && !this.strategyId) {
        this.strategyId = first.id;
      }
    });
  }

  signed(value: number): string {
    return signedMoney(value);
  }

  pct(value: number): string {
    return `${value >= 0 ? '' : ''}${value.toFixed(2)}%`;
  }

  pnlCls(value: number): string {
    return pnlClass(value);
  }

  async run(): Promise<void> {
    const symbol = this.symbol.trim().toUpperCase();
    if (!this.strategyId) {
      this.toast.show('Pick a strategy', 'Save a strategy first, then run the replay.', 'error');
      return;
    }
    this.busy.set(true);
    try {
      const row = await this.trading.runBacktest({
        strategyId: this.strategyId,
        symbol,
        timeframe: this.timeframe,
        from: `${this.from}T00:00:00.000Z`,
        to: `${this.to}T23:59:59.000Z`,
        initialCapital: Number(this.capital),
        riskPercent: this.trading.risk()?.riskPerTradePercent ?? 1,
        leverage: this.trading.risk()?.maxLeverage ?? 3,
        feesPercent: Number(this.fees),
        slippagePercent: Number(this.slippage),
      });
      this.result.set(row);
      this.toast.show(
        'Backtest finished',
        `${row.numberOfTrades} trades · ${signedMoney(row.netProfit)} · ${row.barsUsed} bars`,
        'success',
      );
    } catch (error) {
      this.toast.show('Backtest blocked', apiMessage(error), 'error');
    } finally {
      this.busy.set(false);
    }
  }
}

function daysAgo(days: number): Date {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() - days);
  return date;
}

function dateInput(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function apiMessage(error: unknown): string {
  const http = error as { status?: number; message?: string; error?: unknown };
  if (http.status === 0 || http.status === 502 || http.status === 504) {
    return 'API is not running on port 5080. Start or restart the backend, then run again.';
  }
  const body = http.error;
  if (typeof body === 'string') {
    const trimmed = body.trim();
    if (trimmed && !trimmed.startsWith('<')) {
      return trimmed;
    }
  }
  if (body && typeof body === 'object') {
    const payload = body as {
      message?: string;
      Message?: string;
      title?: string;
      Title?: string;
      errors?: Record<string, string[] | undefined>;
    };
    const firstError = payload.errors
      ? Object.values(payload.errors).flat().find((row) => !!row)
      : null;
    return payload.message || payload.Message || firstError || payload.title || payload.Title || 'Could not run this backtest.';
  }
  return http.message || 'Could not run this backtest.';
}
