import { Component, ElementRef, ViewChild, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { DevMessage } from '../../core/models/otp.models';
import { NotificationService } from '../../core/services/notification.service';
import { OtpPromptService } from '../../core/services/otp-prompt.service';

/** Module 7: the "Verify it's you" dialog, shown by the OTP interceptor for any protected request. */
@Component({
  selector: 'app-otp-dialog',
  imports: [DatePipe, FormsModule, RouterLink],
  templateUrl: './otp-dialog.html'
})
export class OtpDialog {
  protected readonly otp = inject(OtpPromptService);
  private readonly notifications = inject(NotificationService);

  protected readonly devMode = !environment.production;
  /** Development only: the SMS from the simulated phone, so you don't have to switch tabs. */
  protected readonly devSms = signal<DevMessage | null>(null);
  protected code = '';

  @ViewChild('codeInput') set codeInput(input: ElementRef<HTMLInputElement> | undefined) {
    input?.nativeElement.focus();
  }

  constructor() {
    effect(() => {
      const challenge = this.otp.challenge();
      this.code = '';
      this.devSms.set(null);
      if (!challenge || !this.devMode) return;
      const last4 = challenge.sentTo.slice(-4);
      this.notifications.devMessages(20).subscribe({
        next: list => this.devSms.set(
          list.find(m => m.channel === 'Sms' && m.to.endsWith(last4) && m.body.includes(' is your Secure Credit EMI code ')) ?? null),
        error: () => this.devSms.set(null)
      });
    });
  }

  verify(): void {
    if (/^\d{6}$/.test(this.code)) this.otp.submit(this.code);
  }
}
