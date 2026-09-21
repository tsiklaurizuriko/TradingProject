import { signal } from '@angular/core';

export type SortDir = 'asc' | 'desc';

const DATE_COLUMNS = ['created', 'closed', 'opened', 'time', 'timestamp'] as const;

export class ListQuery {
  readonly q = signal('');
  readonly key = signal('');
  readonly dir = signal<SortDir>('asc');

  toggle(column: string): void {
    if (this.key() === column) {
      this.dir.update((value) => (value === 'asc' ? 'desc' : 'asc'));
      return;
    }
    this.key.set(column);
    this.dir.set(isDateColumn(column) ? 'desc' : 'asc');
  }

  apply<T>(
    source: readonly T[],
    search: (row: T) => unknown[],
    columns: Record<string, (row: T) => unknown>,
  ): T[] {
    const needle = this.q().trim().toUpperCase();
    const next = needle
      ? source.filter((row) => search(row).some((value) => String(value ?? '').toUpperCase().includes(needle)))
      : [...source];
    const column = this.key();
    const get = column ? columns[column] : undefined;
    const dateKey = DATE_COLUMNS.find((key) => key in columns);
    const dateGet = dateKey ? columns[dateKey] : undefined;
    const sign = this.dir() === 'asc' ? 1 : -1;
    return next.sort((a, b) => {
      if (get) {
        const primary = compareValues(get(a), get(b)) * sign;
        if (primary !== 0) {
          return primary;
        }
      }
      if (dateGet && column !== dateKey) {
        return compareValues(dateGet(b), dateGet(a));
      }
      return 0;
    });
  }
}

function isDateColumn(column: string): boolean {
  return (DATE_COLUMNS as readonly string[]).includes(column);
}

export function compareValues(a: unknown, b: unknown): number {
  const emptyA = a == null || a === '';
  const emptyB = b == null || b === '';
  if (emptyA && emptyB) {
    return 0;
  }
  if (emptyA) {
    return 1;
  }
  if (emptyB) {
    return -1;
  }
  if (typeof a === 'number' && typeof b === 'number') {
    return a - b;
  }
  if (typeof a === 'boolean' && typeof b === 'boolean') {
    return Number(a) - Number(b);
  }
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
}

export function timeValue(value: string | null | undefined): number {
  return value ? Date.parse(value) : 0;
}

export function byTimeDesc(a: string | null | undefined, b: string | null | undefined): number {
  return timeValue(b) - timeValue(a);
}
