/** Challenged (Module 7): answered with 428 - a one-time code was sent first. */
export type AuditOutcome = 'Success' | 'Rejected' | 'Failed' | 'Challenged';

export interface AuditLog {
  auditId: number;
  timestamp: string;
  actionType: string;
  outcome: AuditOutcome;
  httpStatus: number | null;
  endpoint: string;
  signatureValid: boolean;
  userId: number | null;
  partnerId: string | null;
  clientIp: string | null;
  correlationId: string | null;
  detail: string | null;
  payloadHash: string;
}

export interface AuditLogQuery {
  actionType?: string;
  outcome?: string;
  partnerId?: string;
  userId?: number | null;
  lastHours?: number | null;
  page: number;
  pageSize: number;
}