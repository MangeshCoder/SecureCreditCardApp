import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/** Route requires a logged-in user. (UI convenience only - the API enforces security.) */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.getToken() ? true : inject(Router).createUrlTree(['/login']);
};

/** Route requires the Admin role. */
export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.getToken() && auth.isAdmin() ? true : inject(Router).createUrlTree(['/cards']);
};

/** Login/register pages redirect away when already logged in. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.getToken()) return true;
  return inject(Router).createUrlTree([auth.isAdmin() ? '/admin/cardholders' : '/cards']);
};
