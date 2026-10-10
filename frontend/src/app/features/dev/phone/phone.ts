import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { DevMessage } from '../../../core/models/otp.models';
import { NotificationService } from '../../../core/services/notification.service';

/**
 * DEVELOPMENT ONLY (Module 7): shows the SMS and e-mails the API's simulator "sent" - one-time codes and
 * alerts - like the customer's phone. A production build never registers the route, and the API endpoint
 * answers 404 outside Development.
 */
@Component({
  selector: 'app-phone',
  imports: [DatePipe],
  templateUrl: './phone.html'
})
export class Phone implements OnInit, OnDestroy {
  private readonly notifications = inject(NotificationService);
  private timer?: ReturnType<typeof setInterval>;

  protected readonly messages = signal<DevMessage[]>([]);
  protected readonly channel = signal<'Sms' | 'Email'>('Sms');
  protected readonly unavailable = signal(false);

  ngOnInit(): void {
    this.refresh();
    this.timer = setInterval(() => this.refresh(), 3000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected shown(): DevMessage[] {
    return this.messages().filter(m => m.channel === this.channel());
  }

  private refresh(): void {
    this.notifications.devMessages(50).subscribe({
      next: m => {
        this.messages.set(m);
        this.unavailable.set(false);
      },
      error: () => this.unavailable.set(true)
    });
  }
}
