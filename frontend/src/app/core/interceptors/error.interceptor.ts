import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/** If the API says the token is invalid/expired (401 on a protected call), end the session. */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  return next(req).pipe(
    catchError((error: unknown) => {
      const isAuthEndpoint = req.url.includes('/api/auth/');
      const isPinCheck = /\/api\/cards\/\d+\/(pin|reveal)$/.test(req.url); // wrong PIN also returns 401
      if (error instanceof HttpErrorResponse && error.status === 401 && !isAuthEndpoint && !isPinCheck) {
        auth.logout();
      }
      return throwError(() => error);
    })
  );
};
