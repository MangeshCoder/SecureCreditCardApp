/** Module 7: returned with HTTP 428 - a one-time code was sent; the request must be repeated with it. */
export interface OtpChallenge {
  challengeId: number;
  purpose: string;
  /** e.g. "to unlock card ending 4057" */
  description: string;
  /** Masked phone number, e.g. +91******6072. */
  sentTo: string;
  expiresAt: string;
  codeLength: number;
}

/** DEVELOPMENT ONLY: a message "sent" by the API's SMS / e-mail simulator. */
export interface DevMessage {
  id: number;
  channel: 'Sms' | 'Email';
  to: string;
  subject: string | null;
  body: string;
  sentAtUtc: string;
}
