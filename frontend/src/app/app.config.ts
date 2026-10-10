import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { jwtInterceptor } from './core/interceptors/jwt.interceptor';
import { otpInterceptor } from './core/interceptors/otp.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // Order: jwt adds the token → error sees only the final outcome → otp (innermost) repeats a request
    // answered with 428 using the one-time code, keeping the token the jwt interceptor already added.
    provideHttpClient(withInterceptors([jwtInterceptor, errorInterceptor, otpInterceptor]))
  ]
};
