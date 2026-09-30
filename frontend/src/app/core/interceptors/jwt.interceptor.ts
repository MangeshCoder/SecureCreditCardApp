import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';

/**
 * Adds "Authorization: Bearer <token>" to every call to OUR API.
 * The origin check matters: the token must never be sent to third-party URLs.
 * (Functional interceptor - the modern equivalent of the class-based JwtSecurityInterceptor in the spec.)
 */
export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  const isApiCall = req.url.startsWith(`${environment.apiUrl}/api/`);
  const token = isApiCall ? inject(AuthService).getToken() : null;

  if (token) {
    req = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`,
        'X-Client-Version': '1.0.0'
      }
    });
  }
  return next(req);
};
