import { Component, EventEmitter, Output, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { TradingService } from '../../core/trading/trading.service';
import { ToastService } from '../../core/ui/toast.service';
import { UiStateService } from '../../core/ui/ui-state.service';
import { ConfirmModalComponent } from '../ui/ui-kit';

@Component({
  selector: 'app-emergency-stop',
  imports: [ConfirmModalComponent],
  template: `
    @if (auth.isOperator() && (hasRunning() || hasPositions())) {
      <div>
        @if (hasPositions()) {
          <button class="emergency-fab" type="button" (click)="flattenOpen.set(true)" aria-label="Close all positions">
            ⛔ <span class="emergency-fab-label">Close All Positions</span>
          </button>
        }
        @if (hasRunning()) {
          <button class="emergency-fab" type="button" (click)="open.set(true)" aria-label="Emergency stop">
            🛑 <span class="emergency-fab-label">Emergency Stop</span>
          </button>
        }
      </div>
    }
    <app-confirm-modal
      [open]="open()"
      title="Emergency Stop"
      [message]="'This stops every running live bot. It does not place a new order. Open positions keep their stop-loss and take-profit on Binance.'"
      [warning]="ui.isLive() ? 'Real Binance bots in this workspace stop.' : 'Simulator bots in this workspace stop.'"
      confirmLabel="Emergency Stop"
      [danger]="true"
      (cancel)="open.set(false)"
      (confirm)="run()"
    />
    <app-confirm-modal
      [open]="flattenOpen()"
      title="Close All Positions"
      [message]="'This stops every bot, cancels their stop-loss and take-profit orders, and closes every open Binance futures position with a reduce-only market order.'"
      warning="Real orders are sent to Binance at market price."
      confirmLabel="Close All Positions"
      [danger]="true"
      (cancel)="flattenOpen.set(false)"
      (confirm)="flatten()"
    />
  `,
})
export class EmergencyStopComponent {
  private readonly trading = inject(TradingService);
  private readonly toast = inject(ToastService);
  readonly auth = inject(AuthService);
  readonly ui = inject(UiStateService);
  readonly open = signal(false);
  readonly flattenOpen = signal(false);
  readonly hasRunning = computed(() => this.trading.runningWorkspaceBots().length > 0);
  readonly hasPositions = computed(() => this.trading.positions().length > 0);
  @Output() stopped = new EventEmitter<void>();

  async run(): Promise<void> {
    this.open.set(false);
    try {
      await this.trading.stopWorkspaceAll();
      await this.trading.refresh();
      this.toast.show('Emergency stop', `${this.ui.workspace()} running bots were stopped.`, 'success');
      this.stopped.emit();
    } catch {
      this.toast.show('Emergency stop failed', 'Could not reach the API.', 'error');
    }
  }

  async flatten(): Promise<void> {
    this.flattenOpen.set(false);
    try {
      const report = await this.trading.flattenAll();
      await this.trading.refresh();
      if (report.flat) {
        this.toast.show('All positions closed', `Binance reports no open position. ${report.botsStopped} bots stopped.`, 'success');
      } else {
        const left = report.remaining.length ? `Still open: ${report.remaining.join(', ')}.` : '';
        this.toast.show('Close all needs attention', `${left} ${report.failures.join(' ')}`.trim(), 'error');
      }
      this.stopped.emit();
    } catch {
      this.toast.show('Close all failed', 'Could not reach the API. Check Binance directly.', 'error');
    }
  }
}
