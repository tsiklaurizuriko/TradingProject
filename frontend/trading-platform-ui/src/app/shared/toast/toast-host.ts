import { Component, inject } from '@angular/core';
import { ToastService } from '../../core/ui/toast.service';

@Component({
  selector: 'app-toast-host',
  template: `
    <div class="toast-host" aria-live="polite">
      @for (toast of toasts.items(); track toast.id) {
        <div class="toast" [class.error]="toast.kind === 'error'" [class.success]="toast.kind === 'success'">
          <strong>{{ toast.title }}</strong>
          <div class="tiny">{{ toast.message }}</div>
        </div>
      }
    </div>
  `,
})
export class ToastHostComponent {
  readonly toasts = inject(ToastService);
}
