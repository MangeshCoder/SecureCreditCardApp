import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { OtpChallenge } from '../models/otp.models';
import { apiErrorMessages } from '../utils/api-error';

export const OTP_CHALLENGE_HEADER = 'X-Otp-Challenge-Id';
export const OTP_CODE_HEADER = 'X-Otp-Code';

/** The challenge from a 428 "Verification required" answer, or null for any other error. */
export function otpChallengeOf(error: unknown): OtpChallenge | null {
  return error instanceof HttpErrorResponse && error.status === 428 && error.error?.otp
    ? (error.error.otp as OtpChallenge)
    : null;
}

/**
 * Module 7: drives the one-time-code dialog. The OTP interceptor hands over a request that was answered
 * with 428; this service shows the dialog and repeats THE SAME request with the X-Otp-* headers until it
 * succeeds, fails for another reason, or the user cancels. The page that made the request never knows -
 * it simply gets its normal response, a bit later.
 */
@Injectable({ providedIn: 'root' })
export class OtpPromptService {
  /** Challenge of the open dialog; null = no dialog. */
  readonly challenge = signal<OtpChallenge | null>(null);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  private actions: { submit(code: string): void; resend(): void; cancel(): void } | null = null;

  verify(req: HttpRequest<unknown>, next: HttpHandlerFn, first: OtpChallenge): Observable<HttpEvent<unknown>> {
    return new Observable<HttpEvent<unknown>>(subscriber => {
      let inner: Subscription | undefined;

      const close = () => {
        this.actions = null;
        this.challenge.set(null);
        this.error.set(null);
        this.busy.set(false);
      };

      const send = (request: HttpRequest<unknown>) => {
        inner?.unsubscribe();
        this.busy.set(true);
        this.error.set(null);
        inner = next(request).subscribe({
          next: event => {
            if (event instanceof HttpResponse) close();
            subscriber.next(event);
          },
          complete: () => subscriber.complete(),
          error: (e: unknown) => {
            this.busy.set(false);
            const again = otpChallengeOf(e);
            if (again) {
              this.challenge.set(again);            // "Send a new code": a new challenge
              return;
            }
            if (e instanceof HttpErrorResponse && (e.error?.otpFailed || e.status === 429)) {
              this.error.set(apiErrorMessages(e).join(' ')); // wrong / expired code: the user can try again
              return;
            }
            close();
            subscriber.error(e);                     // the action itself failed (e.g. "Card is not locked")
          }
        });
      };

      this.challenge.set(first);
      this.error.set(null);
      this.actions = {
        submit: code => send(req.clone({
          setHeaders: { [OTP_CHALLENGE_HEADER]: String(this.challenge()!.challengeId), [OTP_CODE_HEADER]: code }
        })),
        resend: () => send(req),                     // without a code → the API sends a new one (428)
        cancel: () => {
          inner?.unsubscribe();
          close();
          subscriber.error(new HttpErrorResponse({ status: 428, url: req.url, error: { detail: 'Verification cancelled.' } }));
        }
      };

      // The page went away (navigation) while the dialog was open: close it.
      return () => {
        inner?.unsubscribe();
        if (this.actions) close();
      };
    });
  }

  submit(code: string): void {
    this.actions?.submit(code);
  }

  resend(): void {
    this.actions?.resend();
  }

  cancel(): void {
    this.actions?.cancel();
  }
}
