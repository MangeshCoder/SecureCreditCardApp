import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AppNotification, UnreadCount } from '../models/notification.models';
import { DevMessage } from '../models/otp.models';
import { PagedResult } from '../models/transaction.models';

/** Module 7: the in-app inbox. `unreadCount` feeds the badge on the bell icon. */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/notifications`;

  readonly unreadCount = signal(0);

  list(page = 1, pageSize = 20, unreadOnly = false): Observable<PagedResult<AppNotification>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize).set('unreadOnly', unreadOnly);
    return this.http.get<PagedResult<AppNotification>>(this.apiUrl, { params });
  }

  refreshUnreadCount(): Observable<UnreadCount> {
    return this.http.get<UnreadCount>(`${this.apiUrl}/unread-count`).pipe(tap(c => this.unreadCount.set(c.count)));
  }

  markRead(notificationId: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${notificationId}/read`, {}).pipe(
      tap(() => this.unreadCount.update(n => Math.max(0, n - 1))));
  }

  markAllRead(): Observable<UnreadCount> {
    return this.http.post<UnreadCount>(`${this.apiUrl}/read-all`, {}).pipe(tap(c => this.unreadCount.set(c.count)));
  }

  /** DEVELOPMENT ONLY: what the API's SMS / e-mail simulator "sent" (404 in every other environment). */
  devMessages(take = 30): Observable<DevMessage[]> {
    return this.http.get<DevMessage[]>(`${environment.apiUrl}/api/dev/messages`, { params: { take } });
  }
}
