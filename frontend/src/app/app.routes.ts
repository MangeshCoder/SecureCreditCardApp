import { Routes } from '@angular/router';
import { adminGuard, authGuard, guestGuard } from './core/guards/auth.guards';

// Every feature page is lazy-loaded, so its code is only downloaded when first visited.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'cards' },
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login/login').then(m => m.Login)
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/register/register').then(m => m.Register)
  },
  {
    path: 'cards',
    canActivate: [authGuard],
    loadComponent: () => import('./features/cards/my-cards/my-cards').then(m => m.MyCards)
  },
  {
    path: 'cards/:cardId/transactions',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/transactions/card-transactions/card-transactions').then(m => m.CardTransactions)
  },
  {
    path: 'pay',
    canActivate: [authGuard],
    loadComponent: () => import('./features/transactions/checkout/checkout').then(m => m.Checkout)
  },
  {
    path: 'admin/transactions',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./features/admin/all-transactions/all-transactions').then(m => m.AllTransactions)
  },
  {
    path: 'admin/cardholders',
    canActivate: [adminGuard],
    loadComponent: () => import('./features/admin/cardholders/cardholders').then(m => m.Cardholders)
  },
  {
    path: 'admin/cards',
    canActivate: [adminGuard],
    loadComponent: () => import('./features/admin/all-cards/all-cards').then(m => m.AllCards)
  },
  { path: '**', redirectTo: 'cards' }
];
