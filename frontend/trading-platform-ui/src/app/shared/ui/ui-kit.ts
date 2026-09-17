import { Component, EventEmitter, Input, Output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../icon/icon';

@Component({
  selector: 'app-status-badge',
  template: `<span class="badge" [class]="cls">{{ label }}</span>`,
})
export class StatusBadgeComponent {
  @Input() label = '';
  @Input() cls = 'badge-stopped';
}

@Component({
  selector: 'app-empty-state',
  imports: [RouterLink],
  template: `
    <div class="empty-state">
      <strong>{{ title }}</strong>
      <p>{{ message }}</p>
      @if (actionLabel && actionLink) {
        <a class="btn sm" [routerLink]="actionLink">{{ actionLabel }}</a>
      }
    </div>
  `,
})
export class EmptyStateComponent {
  @Input() title = 'Nothing here yet';
  @Input() message = '';
  @Input() actionLabel?: string;
  @Input() actionLink?: string;
}

@Component({
  selector: 'app-metric-card',
  imports: [IconComponent],
  template: `
    <article class="card metric-card">
      <div class="metric-label">
        <span class="metric-icon"><app-icon [name]="icon" /></span>
        {{ label }}
      </div>
      @if (loading) {
        <div class="skel" style="height:28px;margin-top:12px;width:70%"></div>
        <div class="skel" style="height:10px;margin-top:8px;width:48%"></div>
      } @else {
        <div class="metric-value" [class]="valueClass">{{ value }}</div>
        <div class="metric-sub" [class]="subClass">{{ sub }}</div>
      }
    </article>
  `,
})
export class MetricCardComponent {
  @Input() icon = 'wallet';
  @Input() label = '';
  @Input() value = '—';
  @Input() sub = '';
  @Input() valueClass = '';
  @Input() subClass = '';
  @Input() loading = false;
}

@Component({
  selector: 'app-confirm-modal',
  template: `
    @if (open) {
      <div class="modal-backdrop" role="dialog" aria-modal="true" [attr.aria-label]="title">
        <div class="modal">
          <h2>{{ title }}</h2>
          <p>{{ message }}</p>
          @if (warning) {
            <p class="pnl-neg">{{ warning }}</p>
          }
          <div class="btn-row">
            <button class="btn secondary" type="button" (click)="cancel.emit()">Cancel</button>
            <button class="btn" [class.danger]="danger" type="button" (click)="confirm.emit()">{{ confirmLabel }}</button>
          </div>
        </div>
      </div>
    }
  `,
})
export class ConfirmModalComponent {
  @Input() open = false;
  @Input() title = 'Confirm';
  @Input() message = '';
  @Input() warning = '';
  @Input() confirmLabel = 'Confirm';
  @Input() danger = false;
  @Output() cancel = new EventEmitter<void>();
  @Output() confirm = new EventEmitter<void>();
}
