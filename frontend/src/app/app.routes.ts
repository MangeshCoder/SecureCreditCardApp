import { Routes } from '@angular/router';
import { environment } from '../environments/environment';
import { adminGuard, authGuard, guestGuard } from './core/guards/auth.guards';

/**
 * DEVELOPMENT ONLY: a production build has environment.production = true, so this route is never registered.
 * No guard: the admin needs the sign-in code before being signed in.
 */
const devRoutes: Routes = environment.production ? [] : [
  { path: 'dev/phone', loadComponent: () => import('./features/dev/phone/phone').then(m => m.Phone) }
];

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
    path: 'cards/:cardId/controls',
    canActivate: [authGuard],
    loadComponent: () => import('./features/cards/card-controls/card-controls').then(m => m.CardControls)
  },
  {
    path: 'cards/:cardId/rewards',
    canActivate: [authGuard],
    loadComponent: () => import('./features/cashback/card-rewards/card-rewards').then(m => m.CardRewards)
  },
  {
    path: 'cards/:cardId/emi',
    canActivate: [authGuard],
    loadComponent: () => import('./features/emi/card-emi/card-emi').then(m => m.CardEmi)
  },
  {
    path: 'emi-calculator',
    canActivate: [authGuard],
    loadComponent: () => import('./features/emi/emi-calculator/emi-calculator').then(m => m.EmiCalculator)
  },
  {
    path: 'pay',
    canActivate: [authGuard],
    loadComponent: () => import('./features/transactions/checkout/checkout').then(m => m.Checkout)
  },
  {
    path: 'notifications',
    canActivate: [authGuard],
    loadComponent: () => import('./features/notifications/notifications').then(m => m.Notifications)
  },
  {
    path: 'admin/transactions',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./features/admin/all-transactions/all-transactions').then(m => m.AllTransactions)
  },
  {
    path: 'admin/security-audit',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./features/admin/security-audit/security-audit').then(m => m.SecurityAudit)
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
  ...devRoutes,
  { path: '**', redirectTo: 'cards' }
];
