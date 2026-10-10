import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { OtpPromptService, otpChallengeOf } from '../services/otp-prompt.service';

/**
 * Module 7: when the API answers 428 "Verification required", ask the user for the one-time code and repeat
 * the request with it. One place for every protected action: sign-in, online payments, unlock, PIN change,
 * card number, card controls - no page needs its own OTP code.
 */
export const otpInterceptor: HttpInterceptorFn = (req, next) => {
  const prompt = inject(OtpPromptService);
  return next(req).pipe(
    catchError((error: unknown) => {
      const challenge = otpChallengeOf(error);
      return challenge ? prompt.verify(req, next, challenge) : throwError(() => error);
    })
  );
};
