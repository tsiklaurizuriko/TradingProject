import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TradingService } from '../../core/trading/trading.service';
import { BotDto, StartBotsResult, StopBotsResult, botStatus, modeBadge, signedMoney } from '../../core/trading/trading.models';
import { ratingFor, ratingSortValue, starText, verdictLabel } from '../../core/trading/strategy-ratings';
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
  busyGroup: string | null = null;
  groupAction: 'start' | 'stop' | null = null;
  readonly confirmGroup = signal<{ key: string; name: string } | null>(null);
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
  readonly rankedStrategies = computed(() => {
    const rows = [...this.trading.strategies()];
    return rows.sort((a, b) => ratingSortValue(b.templateKey) - ratingSortValue(a.templateKey) || a.name.localeCompare(b.name));
  });
  readonly ratingFor = ratingFor;

  bookLabel(bot: { strategyId?: string; riskProfileName?: string } | undefined): string {
    const row = this.trading.strategies().find((item) => item.id === bot?.strategyId);
    if (!row || row.stopLossPercent == null || row.takeProfitPercent == null) {
      return bot?.riskProfileName || 'Strategy risk';
    }
    return `SL ${row.stopLossPercent}% · TP ${row.takeProfitPercent}% · ${row.maxLeverage ?? '—'}x`;
  }

  strategyOptionLabel(row: { name: string; templateKey: string; isEnabled: boolean; appliesToAllSymbols: boolean; allowedSymbols: string[] }): string {
    const rate = ratingFor(row.templateKey);
    const coins = row.appliesToAllSymbols ? 'All coins' : `${row.allowedSymbols.length} coins`;
    const disabled = row.isEnabled === false ? ' · disabled' : '';
    return `${row.name} · ${starText(rate.stars)} ${verdictLabel(row.templateKey)}${disabled} · ${coins}`;
  }
  readonly coins = computed(() => {
    const markets = this.trading.markets();
    const all: CoinPick[] = markets.length
      ? markets.map((row) => ({
          symbol: row.symbol,
          displayName: row.displayName,
          eligible: row.eligible,
          quoteVolume: row.quoteVolume ?? 0,
        }))
      : this.trading.tickers().map((row) => ({
          symbol: row.symbol,
          displayName: row.displayName,
          quoteVolume: 0,
          marketCapRank: row.marketCapRank,
        }));
    const strategy = this.selectedStrategy();
    const scoped = all.filter((row) => {
      if (strategy && !strategy.appliesToAllSymbols && !(strategy.allowedSymbols ?? []).includes(row.symbol)) {
        return false;
      }
      return true;
    });
    scoped.sort(compareCoinPick);
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
    const symbols = [...this.selected()].filter((symbol) => !this.alreadyCreated(symbol));
    if (!strategy?.id) {
      this.toast.show('Pick a strategy', 'Choose a strategy. The order uses that strategy stop and take profit.', 'error');
      return;
    }
    if (!symbols.length) {
      this.toast.show('No coins', 'Mark at least one coin that does not already have this combination.', 'error');
      return;
    }
    this.busy = true;
    try {
      const result = await this.trading.createWorkspaceBots(strategy.id, strategy.riskProfileId || '00000000-0000-0000-0000-000000000000', symbols);
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
    } catch (error) {
      this.toast.show('Create blocked', apiMessage(error), 'error');
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

  idleCount(bots: BotDto[]): number {
    return bots.filter((bot) => bot.status !== 'Running').length;
  }

  requestStartGroup(group: StrategyBotGroup): void {
    if (!this.idleCount(group.bots)) {
      this.toast.show('Nothing to start', `Every bot in ${group.name} is already running.`, 'info');
      return;
    }
    if (this.ui.isLive() || this.ui.confirmStartAll()) {
      this.confirmGroup.set({ key: group.key, name: group.name });
      return;
    }
    void this.startGroup(group);
  }

  cancelStartGroup(): void {
    this.confirmGroup.set(null);
  }

  groupStartTitle(): string {
    return this.ui.isLive() ? 'LIVE TRADING WARNING' : `Start ${this.confirmGroup()?.name ?? 'group'}`;
  }

  groupStartMessage(): string {
    const name = this.confirmGroup()?.name ?? 'this group';
    if (!this.ui.isLive()) {
      return `This starts every stopped bot in ${name}. Other groups stay as they are.`;
    }
    return `LIVE bots in ${name} only. Other groups stay as they are. Isolated still allows only one open LIVE position per coin.`;
  }

  groupStartWarning(): string {
    return this.ui.isLive()
      ? 'Real Binance USD-M Isolated orders can fire as soon as a strategy signals. Planned Risk is not a guaranteed maximum loss.'
      : '';
  }

  confirmStartGroup(): void {
    const pending = this.confirmGroup();
    this.confirmGroup.set(null);
    if (!pending) {
      return;
    }
    const group = this.strategyGroups().find((row) => row.key === pending.key) ?? { ...pending, bots: [] };
    void this.startGroup(group);
  }

  async startGroup(group: StrategyBotGroup): Promise<void> {
    await this.runGroup(group, 'start');
  }

  async stopGroup(group: StrategyBotGroup): Promise<void> {
    if (!this.runningCount(group.bots)) {
      this.toast.show('Nothing to stop', `No running bots in ${group.name}.`, 'info');
      return;
    }
    await this.runGroup(group, 'stop');
  }

  private async runGroup(group: StrategyBotGroup, action: 'start' | 'stop'): Promise<void> {
    const current = this.strategyGroups().find((row) => row.key === group.key) ?? group;
    this.busy = true;
    this.busyGroup = current.key;
    this.groupAction = action;
    try {
      const result = action === 'start' ? await this.startGroupBots(current) : await this.stopGroupBots(current);
      await this.trading.refresh();
      this.toastGroup(current.name, action, result);
    } catch {
      this.toast.show(
        action === 'start' ? 'Start failed' : 'Stop failed',
        action === 'start' ? `Could not start ${current.name}.` : `Could not stop ${current.name}.`,
        'error',
      );
    } finally {
      this.busy = false;
      this.busyGroup = null;
      this.groupAction = null;
    }
  }

  private async startGroupBots(group: StrategyBotGroup): Promise<StartBotsResult> {
    const idle = group.bots.filter((bot) => bot.status !== 'Running');
    if (!isGuid(group.key)) {
      return this.startListed(idle);
    }
    try {
      return await this.trading.startWorkspaceStrategy(group.key);
    } catch (error) {
      if (!isMissingRoute(error)) {
        throw error;
      }
      return this.startListed(idle);
    }
  }

  private async stopGroupBots(group: StrategyBotGroup): Promise<StopBotsResult> {
    const running = group.bots.filter((bot) => bot.status === 'Running');
    if (!isGuid(group.key)) {
      return this.stopListed(running);
    }
    try {
      return await this.trading.stopWorkspaceStrategy(group.key);
    } catch (error) {
      if (!isMissingRoute(error)) {
        throw error;
      }
      return this.stopListed(running);
    }
  }

  private async startListed(bots: BotDto[]): Promise<StartBotsResult> {
    let started = 0;
    let failed = 0;
    let detail: string | null = null;
    for (const bot of bots) {
      try {
        await this.trading.startWorkspaceBot(bot);
        started++;
      } catch (error) {
        failed++;
        detail = error instanceof Error ? error.message : 'Could not start this bot.';
      }
    }
    return { started, failed, detail };
  }

  private async stopListed(bots: BotDto[]): Promise<StopBotsResult> {
    let stopped = 0;
    let failed = 0;
    let detail: string | null = null;
    for (const bot of bots) {
      try {
        await this.trading.stopWorkspaceBot(bot);
        stopped++;
      } catch (error) {
        failed++;
        detail = error instanceof Error ? error.message : 'Could not stop this bot.';
      }
    }
    return { stopped, failed, detail };
  }

  private toastGroup(name: string, action: 'start' | 'stop', result: StartBotsResult | StopBotsResult): void {
    if (action === 'start') {
      const row = result as StartBotsResult;
      if (row.failed && row.started) {
        this.toast.show('Partial start', `${row.started} ${name} bot(s) started, ${row.failed} skipped.${row.detail ? ' ' + row.detail : ''}`, this.ui.isLive() ? 'error' : 'info');
      } else if (row.failed) {
        this.toast.show('Start blocked', row.detail || `Could not start ${name}.`, 'error');
      } else {
        this.toast.show(`${name} started`, `${row.started} bot(s) in this group are running. Other groups were not changed.`, this.ui.isLive() ? 'error' : 'success');
      }
      return;
    }
    const row = result as StopBotsResult;
    if (row.failed && row.stopped) {
      this.toast.show('Partial stop', `${row.stopped} ${name} bot(s) stopped, ${row.failed} skipped.${row.detail ? ' ' + row.detail : ''}`, 'info');
    } else if (row.failed) {
      this.toast.show('Stop blocked', row.detail || `Could not stop ${name}.`, 'error');
    } else {
      this.toast.show(`${name} stopped`, `${row.stopped} bot(s) in this group were stopped. Other groups were not changed. Positions were not closed.`, 'success');
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
      ? `${running} running live bot(s) will stop. Exchange positions stay open until Binance reports a fill.`
      : 'This removes the bot record. Historical fills stay stored.';
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

interface StrategyBotGroup {
  key: string;
  name: string;
  bots: BotDto[];
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

function isMissingRoute(error: unknown): boolean {
  return !!error && typeof error === 'object' && 'status' in error && (error as { status: unknown }).status === 404;
}

interface CoinPick {
  symbol: string;
  displayName: string;
  eligible?: boolean;
  quoteVolume: number;
  marketCapRank?: number;
}

function compareCoinPick(a: CoinPick, b: CoinPick): number {
  const volume = b.quoteVolume - a.quoteVolume;
  if (volume !== 0) {
    return volume;
  }
  const rank = coinRank(a) - coinRank(b);
  if (rank !== 0) {
    return rank;
  }
  return a.symbol.localeCompare(b.symbol);
}

function coinRank(row: CoinPick): number {
  const rank = row.marketCapRank ?? 0;
  return rank > 0 && rank < 999 ? rank : 9999;
}

function apiMessage(error: unknown): string {
  const http = error as { error?: { message?: string } | string };
  if (typeof http.error === 'string' && http.error.trim() && !http.error.trim().startsWith('<')) {
    return http.error.trim();
  }
  if (http.error && typeof http.error === 'object' && http.error.message) {
    return http.error.message;
  }
  return error instanceof Error ? error.message : 'Could not create these bots.';
}
