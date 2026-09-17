import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../auth/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.accessToken();
  const correlationId = crypto.randomUUID();
  let headers = req.headers.set('X-Correlation-Id', correlationId);
  if (token) {
    headers = headers.set('Authorization', `Bearer ${token}`);
  }
  return next(req.clone({ headers }));
};
