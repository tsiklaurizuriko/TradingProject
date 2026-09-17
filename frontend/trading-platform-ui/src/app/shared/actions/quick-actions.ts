import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../icon/icon';

@Component({
  selector: 'app-quick-actions',
  imports: [RouterLink, IconComponent],
  template: `
    <section class="panel panel-fill compact">
      <div class="section-head"><h2>Quick Actions</h2></div>
      <div class="quick-grid">
        <a class="quick-btn" routerLink="/scanner"><app-icon name="scan" /> Market Scanner</a>
        <a class="quick-btn" routerLink="/backtesting"><app-icon name="flask" /> Backtest</a>
        <a class="quick-btn" routerLink="/bots"><app-icon name="plus" /> Add Bot</a>
        <a class="quick-btn" routerLink="/risk"><app-icon name="shield" /> Risk Settings</a>
      </div>
    </section>
  `,
})
export class QuickActionsComponent {}
