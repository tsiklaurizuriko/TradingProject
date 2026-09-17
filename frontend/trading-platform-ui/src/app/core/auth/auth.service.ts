import { Injectable, computed, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AuthSession {
  accessToken: string;
  refreshToken: string;
  email: string;
  roles: string[];
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly session = signal<AuthSession | null>(readStoredSession());
  readonly current = this.session.asReadonly();
  readonly isAuthenticated = computed(() => !!this.session());

  constructor(private readonly http: HttpClient) {}

  async login(payload: { email: string; password: string }): Promise<void> {
    const session = await firstValueFrom(
      this.http.post<AuthSession>(`${environment.apiBaseUrl}/auth/login`, payload),
    );
    this.persist(session);
  }

  logout(): void {
    this.session.set(null);
    localStorage.removeItem('tp.session');
  }

  changePassword(currentPassword: string, newPassword: string): Promise<void> {
    const email = this.session()?.email;
    if (!email) {
      return Promise.reject(new Error('Sign in first.'));
    }
    return firstValueFrom(
      this.http.post<void>(`${environment.apiBaseUrl}/auth/change-password`, {
        email,
        currentPassword,
        newPassword,
      }),
    );
  }

  accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  private persist(session: AuthSession): void {
    this.session.set(session);
    localStorage.setItem('tp.session', JSON.stringify(session));
  }
}

function readStoredSession(): AuthSession | null {
  const raw = localStorage.getItem('tp.session');
  if (!raw) {
    return null;
  }
  try {
    return JSON.parse(raw) as AuthSession;
  } catch {
    return null;
  }
}
