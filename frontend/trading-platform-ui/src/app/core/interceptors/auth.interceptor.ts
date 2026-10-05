import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const correlationId = crypto.randomUUID();
  const withToken = (request: HttpRequest<unknown>) => {
    const token = auth.accessToken();
    let headers = request.headers.set('X-Correlation-Id', correlationId);
    if (token) {
      headers = headers.set('Authorization', `Bearer ${token}`);
    }
    return request.clone({ headers });
  };

  const isAuthCall = req.url.includes('/auth/');
  return next(withToken(req)).pipe(
    catchError((error: unknown) => {
      if (isAuthCall || !(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }
      return from(auth.refresh()).pipe(
        switchMap((ok) => {
          if (!ok) {
            void router.navigateByUrl('/login');
            return throwError(() => error);
          }
          return next(withToken(req));
        }),
      );
    }),
  );
};
