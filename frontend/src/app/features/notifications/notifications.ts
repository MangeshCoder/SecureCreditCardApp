import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AppNotification, NotificationCategory } from '../../core/models/notification.models';
import { PagedResult } from '../../core/models/transaction.models';
import { NotificationService } from '../../core/services/notification.service';
import { apiErrorMessages } from '../../core/utils/api-error';

/** Module 7: the in-app inbox of alerts (the same alerts are sent by SMS and e-mail). */
@Component({
  selector: 'app-notifications',
  imports: [DatePipe, RouterLink],
  templateUrl: './notifications.html'
})
export class Notifications implements OnInit {
  protected readonly service = inject(NotificationService);

  protected readonly page = signal<PagedResult<AppNotification> | null>(null);
  protected readonly unreadOnly = signal(false);
  protected readonly errors = signal<string[]>([]);

  ngOnInit(): void {
    this.load(1);
  }

  load(page: number): void {
    this.service.list(page, 15, this.unreadOnly()).subscribe({
      next: p => this.page.set(p),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  toggleUnreadOnly(): void {
    this.unreadOnly.update(v => !v);
    this.load(1);
  }

  open(n: AppNotification): void {
    if (n.isRead) return;
    this.service.markRead(n.notificationId).subscribe({
      next: () => this.page.update(p => p && { ...p, items: p.items.map(i => i === n ? { ...i, isRead: true } : i) }),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  markAllRead(): void {
    this.service.markAllRead().subscribe({
      next: () => this.load(this.page()?.page ?? 1),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  protected badge(category: NotificationCategory): string {
    return category === 'Transaction' ? 'text-bg-primary' : category === 'Security' ? 'text-bg-warning' : 'text-bg-info';
  }

  protected delivery(n: AppNotification): string {
    return n.deliveryStatus === 'Sent' ? 'Sent by SMS and e-mail'
      : n.deliveryStatus === 'Failed' ? 'SMS / e-mail delivery failed' : 'Sending by SMS and e-mail…';
  }
}
