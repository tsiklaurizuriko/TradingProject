import { Injectable, inject, signal } from '@angular/core';
import { NotifyTopic, UiStateService } from './ui-state.service';

export interface Toast {
  id: number;
  title: string;
  message: string;
  kind: 'info' | 'success' | 'error';
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  private readonly ui = inject(UiStateService);
  readonly items = signal<Toast[]>([]);
  readonly history = signal<Toast[]>([]);

  show(title: string, message: string, kind: Toast['kind'] = 'info', topic: NotifyTopic = 'system'): void {
    const toast: Toast = { id: this.nextId++, title, message, kind };
    this.history.update((rows) => [toast, ...rows].slice(0, 40));
    if (!this.ui.allowsToast(topic)) {
      return;
    }
    this.items.update((rows) => [...rows, toast]);
    window.setTimeout(() => this.dismiss(toast.id), 4200);
  }

  dismiss(id: number): void {
    this.items.update((rows) => rows.filter((item) => item.id !== id));
  }
}
