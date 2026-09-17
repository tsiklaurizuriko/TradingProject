import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule],
  template: `
    <div class="login-shell">
      <section class="panel login-card">
        <div class="brand" style="padding:0 0 16px">
          <div class="brand-mark">⚡</div>
          <div class="brand-copy">
            <strong>TradeBot</strong>
            <span>Automate. Backtest. Trade.</span>
          </div>
        </div>
        <h1 style="margin:0 0 8px;font-size:28px">Sign in</h1>
        <p class="muted">Trader and admin access. Seeded local login: admin@localhost</p>
        <form class="form" style="max-width:none;margin-top:20px" [formGroup]="form" (ngSubmit)="submit()">
          <label>Email <input type="email" formControlName="email" autocomplete="username" /></label>
          <label>Password <input type="password" formControlName="password" autocomplete="current-password" /></label>
          @if (error()) {
            <p class="pnl-neg">{{ error() }}</p>
          }
          <button class="btn" type="submit" [disabled]="pending()">{{ pending() ? 'Signing in…' : 'Sign in' }}</button>
        </form>
      </section>
    </div>
  `,
})
export class LoginPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly error = signal<string | null>(null);
  readonly pending = signal(false);
  readonly form = this.fb.nonNullable.group({
    email: ['admin@localhost', [Validators.required, Validators.email]],
    password: ['ChangeMe_Admin_123!', [Validators.required, Validators.minLength(8)]],
  });

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.pending.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.form.getRawValue());
      await this.router.navigateByUrl('/dashboard');
    } catch {
      this.error.set('Sign in failed. Check the API and credentials.');
    } finally {
      this.pending.set(false);
    }
  }
}
