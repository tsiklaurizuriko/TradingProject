import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';
import { PortfolioDto } from '../trading/trading.models';
import { TradingService } from '../trading/trading.service';

@Injectable({ providedIn: 'root' })
export class TradingHubService implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly trading = inject(TradingService);
  private connection: HubConnection | null = null;
  readonly connected = signal(false);

  async connect(): Promise<void> {
    if (this.connection) {
      return;
    }
    this.connection = new HubConnectionBuilder()
      .withUrl(environment.hubUrl, {
        accessTokenFactory: () => this.auth.accessToken() ?? '',
      })
      .withAutomaticReconnect()
      .build();
    this.connection.on('ticker', (payload: { symbol: string; price: number; timestamp: string }) => {
      this.trading.applyTicker(payload.symbol, payload.price, payload.timestamp);
    });
    this.connection.on('overview', (payload: PortfolioDto) => {
      this.trading.applyOverview(payload);
    });
    await this.connection.start();
    this.connected.set(true);
  }

  async disconnect(): Promise<void> {
    if (!this.connection) {
      return;
    }
    await this.connection.stop();
    this.connection = null;
    this.connected.set(false);
  }

  ngOnDestroy(): void {
    void this.disconnect();
  }
}
