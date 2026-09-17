import { Component, EventEmitter, Output, computed, inject, signal } from '@angular/core';
import { TradingService } from '../../core/trading/trading.service';
import { ToastService } from '../../core/ui/toast.service';
import { UiStateService } from '../../core/ui/ui-state.service';
import { ConfirmModalComponent } from '../ui/ui-kit';

@Component({
  selector: 'app-emergency-stop',
  imports: [ConfirmModalComponent],
  template: `
    @if (hasRunning()) {
      <button class="emergency-fab" type="button" (click)="open.set(true)" aria-label="Emergency stop">
        🛑 <span class="emergency-fab-label">Emergency Stop</span>
      </button>
    }
    <app-confirm-modal
      [open]="open()"
      title="Emergency Stop"
      [message]="ui.isLive() ? 'This stops every running LIVE bot.' : 'This stops every running paper bot.'"
      [warning]="ui.isLive() ? 'Real Binance bots in this workspace stop.' : 'Simulator bots in this workspace stop.'"
      confirmLabel="Emergency Stop"
      [danger]="true"
      (cancel)="open.set(false)"
      (confirm)="run()"
    />
  `,
})
export class EmergencyStopComponent {
  private readonly trading = inject(TradingService);
  private readonly toast = inject(ToastService);
  readonly ui = inject(UiStateService);
  readonly open = signal(false);
  readonly hasRunning = computed(() => this.trading.runningWorkspaceBots().length > 0);
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
}
