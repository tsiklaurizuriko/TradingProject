import { Component, input } from '@angular/core';
import { ListQuery } from './list-query';

@Component({
  selector: 'app-sort-btn',
  template: `
    <button
      type="button"
      class="sort-th"
      [class.is-on]="on()"
      [class.num]="align() === 'end'"
      [attr.aria-sort]="on() ? (query().dir() === 'asc' ? 'ascending' : 'descending') : 'none'"
      (click)="query().toggle(column())"
    >
      <ng-content />
      <span class="sort-ind">{{ query().dir() === 'desc' && on() ? '▼' : '▲' }}</span>
    </button>
  `,
  styles: [`
    :host { display: contents; }
  `],
})
export class SortBtnComponent {
  readonly column = input.required<string>();
  readonly query = input.required<ListQuery>();
  readonly align = input<'start' | 'end'>('start');

  on(): boolean {
    return this.query().key() === this.column();
  }
}
