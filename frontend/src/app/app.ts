import { Component, inject } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { EMPTY, catchError, filter, merge, switchMap, timer } from 'rxjs';
import { environment } from '../environments/environment';
import { AuthService } from './core/services/auth.service';
import { NotificationService } from './core/services/notification.service';
import { OtpDialog } from './shared/otp-dialog/otp-dialog';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, OtpDialog],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly notifications = inject(NotificationService);
  protected readonly devMode = !environment.production;

  constructor() {
    // Module 7: keep the bell's unread badge fresh - every 20 seconds and after each page change.
    const router = inject(Router);
    toObservable(this.auth.isLoggedIn).pipe(
      switchMap(loggedIn => {
        if (!loggedIn) {
          this.notifications.unreadCount.set(0);
          return EMPTY;
        }
        return merge(timer(0, 20_000), router.events.pipe(filter(e => e instanceof NavigationEnd))).pipe(
          switchMap(() => this.notifications.refreshUnreadCount().pipe(catchError(() => EMPTY))));
      }),
      takeUntilDestroyed()
    ).subscribe();
  }
}
