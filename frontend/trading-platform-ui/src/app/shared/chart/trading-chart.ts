import {
  Component,
  ElementRef,
  OnDestroy,
  afterNextRender,
  effect,
  input,
  viewChild,
} from '@angular/core';
import {
  CandlestickSeries,
  HistogramSeries,
  IChartApi,
  ISeriesApi,
  LineSeries,
  UTCTimestamp,
  createChart,
  createSeriesMarkers,
} from 'lightweight-charts';
import { KlineBarDto, PositionDto, SignalDto, TradeDto, signalLabel } from '../../core/trading/trading.models';

@Component({
  selector: 'app-trading-chart',
  template: `<div class="chart-canvas" #host></div>`,
  styles: [':host { display:block; height:100%; min-height:0; } .chart-canvas { height:100%; min-height:240px; }'],
})
export class TradingChartComponent implements OnDestroy {
  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  readonly bars = input<KlineBarDto[]>([]);
  readonly positions = input<PositionDto[]>([]);
  readonly trades = input<TradeDto[]>([]);
  readonly signals = input<SignalDto[]>([]);
  readonly symbol = input('BTCUSDT');

  private chart?: IChartApi;
  private candles?: ISeriesApi<'Candlestick'>;
  private volume?: ISeriesApi<'Histogram'>;
  private emaFast?: ISeriesApi<'Line'>;
  private emaSlow?: ISeriesApi<'Line'>;
  private markers?: { setMarkers(markers: object[]): void };
  private resize?: ResizeObserver;

  constructor() {
    afterNextRender(() => this.build());
    effect(() => {
      this.bars();
      this.positions();
      this.trades();
      this.signals();
      this.symbol();
      this.render();
    });
  }

  ngOnDestroy(): void {
    this.resize?.disconnect();
    this.chart?.remove();
  }

  private build(): void {
    const el = this.host().nativeElement;
    const width = Math.max(el.clientWidth, 1);
    const height = Math.max(el.clientHeight, 240);
    this.chart = createChart(el, {
      autoSize: true,
      width,
      height,
      layout: {
        background: { color: '#111a2a' },
        textColor: '#8d9bb0',
        fontFamily: 'Inter, sans-serif',
        fontSize: 11,
      },
      grid: {
        vertLines: { color: '#1e2b40' },
        horzLines: { color: '#1e2b40' },
      },
      rightPriceScale: { borderColor: '#1e2b40' },
      timeScale: { borderColor: '#1e2b40', timeVisible: true },
      crosshair: { mode: 0 },
    });
    this.candles = this.chart.addSeries(CandlestickSeries, {
      upColor: '#00c853',
      downColor: '#ff3d57',
      borderUpColor: '#00c853',
      borderDownColor: '#ff3d57',
      wickUpColor: '#00c853',
      wickDownColor: '#ff3d57',
    });
    this.volume = this.chart.addSeries(HistogramSeries, {
      priceScaleId: 'vol',
      priceFormat: { type: 'volume' },
    });
    this.chart.priceScale('vol').applyOptions({ scaleMargins: { top: 0.78, bottom: 0 } });
    this.emaFast = this.chart.addSeries(LineSeries, { color: '#1677ff', lineWidth: 2, priceLineVisible: false });
    this.emaSlow = this.chart.addSeries(LineSeries, { color: '#ffb020', lineWidth: 2, priceLineVisible: false });
    this.markers = createSeriesMarkers(this.candles, []) as { setMarkers(markers: object[]): void };
    this.resize = new ResizeObserver(() => {
      if (!this.chart) {
        return;
      }
      const nextWidth = el.clientWidth;
      const nextHeight = el.clientHeight;
      if (nextWidth > 0 && nextHeight > 0) {
        this.chart.resize(nextWidth, nextHeight);
      }
    });
    this.resize.observe(el);
    this.render();
  }

  private render(): void {
    if (!this.chart || !this.candles || !this.volume || !this.emaFast || !this.emaSlow) {
      return;
    }
    const bars = this.bars();
    this.candles.setData(
      bars.map((bar) => ({
        time: bar.time as UTCTimestamp,
        open: Number(bar.open),
        high: Number(bar.high),
        low: Number(bar.low),
        close: Number(bar.close),
      })),
    );
    this.volume.setData(
      bars.map((bar) => ({
        time: bar.time as UTCTimestamp,
        value: Number(bar.volume),
        color: Number(bar.close) >= Number(bar.open) ? 'rgba(0,200,83,0.35)' : 'rgba(255,61,87,0.35)',
      })),
    );
    this.emaFast.setData(ema(bars, 20));
    this.emaSlow.setData(ema(bars, 50));
    const markers = [
      ...this.signals()
        .filter((item) => item.symbol === this.symbol() && Number.isFinite(Date.parse(item.timestamp)))
        .map((item) => {
          const long = signalLabel(item.signalType) === 'LONG';
          return {
            time: Math.floor(new Date(item.timestamp).getTime() / 1000) as UTCTimestamp,
            position: long ? ('belowBar' as const) : ('aboveBar' as const),
            color: long ? '#00c853' : '#ff3d57',
            shape: long ? ('arrowUp' as const) : ('arrowDown' as const),
            text: signalLabel(item.signalType),
          };
        }),
      ...this.positions()
        .filter((item) => item.symbol === this.symbol() && Number.isFinite(Date.parse(item.openedAt)))
        .map((item) => ({
          time: Math.floor(new Date(item.openedAt).getTime() / 1000) as UTCTimestamp,
          position: 'belowBar' as const,
          color: '#36a3ff',
          shape: 'circle' as const,
          text: 'ENTRY',
        })),
      ...this.trades()
        .filter((item) => item.symbol === this.symbol() && item.closedAt && Number.isFinite(Date.parse(item.closedAt)))
        .map((item) => ({
          time: Math.floor(new Date(item.closedAt!).getTime() / 1000) as UTCTimestamp,
          position: 'aboveBar' as const,
          color: item.pnL >= 0 ? '#00c853' : '#ff3d57',
          shape: 'arrowDown' as const,
          text: 'EXIT',
        })),
    ];
    this.markers?.setMarkers(markers);
    if (bars.length) {
      this.chart.timeScale().fitContent();
    }
  }
}

function ema(bars: KlineBarDto[], period: number) {
  const k = 2 / (period + 1);
  let prev = 0;
  return bars.map((bar, index) => {
    const close = Number(bar.close);
    prev = index === 0 ? close : close * k + prev * (1 - k);
    return { time: bar.time as UTCTimestamp, value: prev };
  });
}
