import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TradingService } from '../../core/trading/trading.service';
import { BotDto, botStatus, modeBadge, signedMoney } from '../../core/trading/trading.models';
import { ratingFor, ratingSortValue, starText, verdictLabel, isOperatorCatalog } from '../../core/trading/strategy-ratings';
import { ToastService } from '../../core/ui/toast.service';
import { UiStateService } from '../../core/ui/ui-state.service';
import { IconComponent } from '../../shared/icon/icon';
import { ListQuery, compareValues } from '../../shared/lists/list-query';
import { SortBtnComponent } from '../../shared/lists/list-tools';
import { ConfirmModalComponent, StatusBadgeComponent } from '../../shared/ui/ui-kit';

@Component({
  selector: 'app-bots-page',
  imports: [RouterLink, StatusBadgeComponent, FormsModule, ConfirmModalComponent, IconComponent, SortBtnComponent],
  templateUrl: './bots.page.html',
  styleUrl: './bots.page.scss',
})
export class BotsPage {
  readonly trading = inject(TradingService);
  readonly ui = inject(UiStateService);
  private readonly toast = inject(ToastService);
  readonly botStatus = botStatus;
  readonly modeBadge = modeBadge;
  readonly signedMoney = signedMoney;
  busy = false;
  busyId: string | null = null;
  readonly strategyId = signal(this.ui.preferredStrategyId());
  readonly coinQuery = signal('');
  readonly selected = signal<Set<string>>(new Set());
  readonly confirmDelete = signal<BotDto[] | null>(null);
  readonly createOpen = signal(false);

  readonly bots = computed(() => this.trading.workspaceBots());
  readonly list = new ListQuery();
  readonly strategyGroups = computed(() => {
    const groups = new Map<string, { key: string; name: string; bots: BotDto[] }>();
    for (const bot of this.bots()) {
      const key = bot.strategyId || bot.strategyName || 'unassigned';
      const name = bot.strategyName || 'Unassigned strategy';
      const current = groups.get(key) ?? { key, name, bots: [] };
      current.bots.push(bot);
      groups.set(key, current);
    }
    return [...groups.values()]
      .map((group) => ({
        ...group,
        bots: [...group.bots].sort((a, b) => a.symbol.localeCompare(b.symbol)),
      }))
      .sort((a, b) => a.name.localeCompare(b.name));
  });
  readonly visibleGroups = computed(() => {
    const key = this.list.key();
    const dir = this.list.dir() === 'asc' ? 1 : -1;
    return this.strategyGroups().map((group) => {
      let bots = [...group.bots];
      if (key) {
        const get = (bot: BotDto): unknown => {
          if (key === 'status') {
            return bot.status;
          }
          if (key === 'pnl') {
            return this.pnl(bot.id);
          }
          return bot.symbol;
        };
        bots = [...bots].sort((a, b) => compareValues(get(a), get(b)) * dir);
      }
      return { ...group, bots };
    });
  });
  readonly selectedStrategy = computed(() =>
    this.rankedStrategies().find((row) => row.id === this.strategyId()) ?? this.rankedStrategies()[0] ?? null,
  );
  readonly rankedStrategies = computed(() =>
    [...this.trading.strategies()]
      .filter((row) => isOperatorCatalog(row.templateKey))
      .sort((a, b) => ratingSortValue(b.templateKey) - ratingSortValue(a.templateKey)),
  );
  readonly ratingFor = ratingFor;

  strategyOptionLabel(row: { name: string; templateKey: string; isEnabled: boolean; appliesToAllSymbols: boolean; allowedSymbols: string[] }): string {
    const rate = ratingFor(row.templateKey);
    const coins = row.appliesToAllSymbols ? 'All coins' : `${row.allowedSymbols.length} coins`;
    const disabled = row.isEnabled === false ? ' · disabled' : '';
    return `${row.name} · ${starText(rate.stars)} ${verdictLabel(row.templateKey)}${disabled} · ${coins}`;
  }
  readonly coins = computed(() => {
    const markets = this.trading.markets();
    const all: { symbol: string; displayName: string; eligible?: boolean }[] = markets.length
      ? markets.map((row) => ({ symbol: row.symbol, displayName: row.displayName, eligible: row.eligible }))
      : this.trading.tickers().map((row) => ({ symbol: row.symbol, displayName: row.displayName }));
    const strategy = this.selectedStrategy();
    const scoped = all.filter((row) => {
      if (strategy && !strategy.appliesToAllSymbols && !(strategy.allowedSymbols ?? []).includes(row.symbol)) {
        return false;
      }
      return true;
    });
    const q = this.coinQuery().trim().toUpperCase();
    if (!q) {
      return scoped;
    }
    return scoped.filter((row) => row.symbol.includes(q) || (row.displayName ?? '').toUpperCase().includes(q));
  });
  readonly creatableVisible = computed(() => this.coins().filter((row) => !this.alreadyCreated(row.symbol)));
  readonly selectedCount = computed(() => this.selected().size);

  alreadyCreated(symbol: string): boolean {
    const strategy = this.selectedStrategy();
    return this.bots().some((bot) => {
      if (bot.symbol !== symbol) {
        return false;
      }
      return strategy
        ? bot.strategyId === strategy.id || (!bot.strategyId && bot.strategyName === strategy.name)
        : true;
    });
  }

  isSelected(symbol: string): boolean {
    return this.selected().has(symbol);
  }

  allVisibleSelected(): boolean {
    const rows = this.creatableVisible();
    return rows.length > 0 && rows.every((row) => this.selected().has(row.symbol));
  }

  toggle(symbol: string, on: boolean): void {
    if (this.alreadyCreated(symbol)) {
      return;
    }
    this.selected.update((current) => {
      const next = new Set(current);
      if (on) {
        next.add(symbol);
      } else {
        next.delete(symbol);
      }
      return next;
    });
  }

  toggleAllVisible(on: boolean): void {
    const visible = this.creatableVisible().map((row) => row.symbol);
    this.selected.update((current) => {
      const next = new Set(current);
      for (const symbol of visible) {
        if (on) {
          next.add(symbol);
        } else {
          next.delete(symbol);
        }
      }
      return next;
    });
  }

  async createBots(): Promise<void> {
    const strategy = this.selectedStrategy();
    const risk = this.trading.risk() ?? this.trading.riskProfiles().find((row) => row.isActive) ?? this.trading.riskProfiles()[0];
    const symbols = [...this.selected()].filter((symbol) => !this.alreadyCreated(symbol));
    if (!strategy?.id || !risk?.id) {
      this.toast.show('Pick a strategy', 'Choose a strategy. New bots use the active Isolated book.', 'error');
      return;
    }
    if (!symbols.length) {
      this.toast.show('No coins', 'Mark at least one coin that does not already have this combination.', 'error');
      return;
    }
    this.busy = true;
    try {
      const result = await this.trading.createWorkspaceBots(strategy.id, risk.id, symbols);
      this.selected.set(new Set());
      if (result.created) {
        this.createOpen.set(false);
      }
      await this.trading.refresh();
      this.toast.show(
        result.created ? 'Bots created' : 'Nothing created',
        `${result.created} created, ${result.skipped} skipped. They stay stopped until you press Start.`,
        result.created ? 'success' : 'info',
      );
    } catch {
      this.toast.show(
        'Create blocked',
        this.ui.isLive() ? 'Save a Binance API key on Live Connection first.' : 'Could not create these paper bots.',
        'error',
      );
    } finally {
      this.busy = false;
    }
  }

  openCreate(): void {
    const ranked = this.rankedStrategies();
    const preferred = this.ui.preferredStrategyId();
    this.strategyId.set(ranked.some((row) => row.id === preferred) ? preferred : ranked[0]?.id ?? '');
    this.createOpen.set(true);
  }

  closeCreate(): void {
    this.createOpen.set(false);
  }

  async start(bot: BotDto): Promise<void> {
    this.busyId = bot.id;
    try {
      await this.trading.startWorkspaceBot(bot);
      await this.trading.refresh();
    } catch (error) {
      this.toast.show('Start blocked', error instanceof Error ? error.message : 'Could not start this bot.', 'error');
    } finally {
      this.busyId = null;
    }
  }

  async stop(bot: BotDto): Promise<void> {
    this.busyId = bot.id;
    try {
      await this.trading.stopWorkspaceBot(bot);
      await this.trading.refresh();
    } catch (error) {
      this.toast.show('Stop blocked', error instanceof Error ? error.message : 'Could not stop this bot.', 'error');
    } finally {
      this.busyId = null;
    }
  }

  groupKey(key: string): string {
    return `bots-strategy-${key}`;
  }

  runningCount(bots: BotDto[]): number {
    return bots.filter((bot) => bot.status === 'Running').length;
  }

  askDeleteGroup(bots: BotDto[]): void {
    this.askDelete(bots);
  }

  askDelete(bots: BotDto[]): void {
    if (!bots.length) {
      return;
    }
    this.confirmDelete.set(bots);
  }

  askDeleteOne(bot: BotDto): void {
    this.askDelete([bot]);
  }

  deleteTitle(): string {
    const rows = this.confirmDelete() ?? [];
    return rows.length === 1 ? `Delete ${rows[0].symbol}` : 'Delete bots';
  }

  deleteMessage(): string {
    const rows = this.confirmDelete() ?? [];
    if (rows.length === 1) {
      return `Remove ${rows[0].symbol} from this workspace? The bot will stop if it is running.`;
    }
    return `Remove ${rows.length} bots from this workspace? Running bots will stop first.`;
  }

  cancelDelete(): void {
    this.confirmDelete.set(null);
  }

  deleteWarning(): string {
    const rows = this.confirmDelete() ?? [];
    const running = rows.filter((bot) => bot.status === 'Running').length;
    if (this.ui.isLive()) {
      return running
        ? `${running} running LIVE bot(s) will stop. Binance positions stay open.`
        : 'LIVE positions on Binance stay open. This only removes the bot from this workspace.';
    }
    return running
      ? `${running} running paper bot(s) will stop. Simulated positions stay open.`
      : 'Paper positions stay open. This only removes the bot record.';
  }

  async confirmDeleteBots(): Promise<void> {
    const rows = this.confirmDelete();
    if (!rows?.length) {
      return;
    }
    this.busy = true;
    try {
      const result = await this.trading.deleteWorkspaceBots(rows);
      this.confirmDelete.set(null);
      await this.trading.refresh();
      this.toast.show(
        result.deleted ? 'Bots deleted' : 'Nothing deleted',
        `${result.deleted} deleted${result.skipped ? `, ${result.skipped} skipped` : ''}. Positions were not closed.`,
        result.deleted ? 'success' : 'info',
      );
    } catch (error) {
      this.toast.show('Delete blocked', error instanceof Error ? error.message : 'Could not delete these bots.', 'error');
    } finally {
      this.busy = false;
    }
  }

  pnl(botId: string): number {
    return this.trading.workspacePositions().filter((p) => p.botId === botId).reduce((sum, p) => sum + p.unrealizedPnL, 0);
  }

  tradesToday(botId: string): number {
    const today = new Date().toISOString().slice(0, 10);
    return this.trading.workspaceTrades().filter((t) => t.botId === botId && (t.openedAt ?? '').startsWith(today)).length;
  }

  winRate(botId: string): string {
    const closed = this.trading.workspaceTrades().filter((t) => t.botId === botId && t.closedAt);
    if (!closed.length) {
      return '—';
    }
    const wins = closed.filter((t) => t.pnL > 0).length;
    return `${((wins / closed.length) * 100).toFixed(0)}%`;
  }
}
