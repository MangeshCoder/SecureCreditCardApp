import { HttpErrorResponse } from '@angular/common/http';

/**
 * Turns an API error (RFC 7807 ProblemDetails / ValidationProblemDetails) into readable messages.
 */
export function apiErrorMessages(error: unknown): string[] {
  if (!(error instanceof HttpErrorResponse)) return ['Unexpected error.'];
  if (error.status === 0) return ['Cannot reach the server. Is the API running?'];
  if (error.status === 429) return ['Too many attempts. Please wait a minute and try again.'];

  const body = error.error;
  if (body?.errors) {
    return Object.values(body.errors as Record<string, string[]>).flat();
  }
  return [body?.detail ?? body?.title ?? `Request failed (${error.status}).`];
}
