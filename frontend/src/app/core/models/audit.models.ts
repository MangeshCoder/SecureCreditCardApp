export type AuditOutcome = 'Success' | 'Rejected' | 'Failed';

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