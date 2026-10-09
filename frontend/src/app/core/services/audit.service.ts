import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditLog, AuditLogQuery } from '../models/audit.models';
import { PagedResult } from '../models/transaction.models';

/** Admin-only, read-only access to the security audit trail. */
@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/audit-logs`;

  getLogs(query: AuditLogQuery): Observable<PagedResult<AuditLog>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.actionType) params = params.set('actionType', query.actionType);
    if (query.outcome) params = params.set('outcome', query.outcome);
    if (query.partnerId) params = params.set('partnerId', query.partnerId);
    if (query.userId) params = params.set('userId', query.userId);
    if (query.lastHours) params = params.set('lastHours', query.lastHours);
    return this.http.get<PagedResult<AuditLog>>(this.apiUrl, { params });
  }

  getActionTypes(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/action-types`);
  }
}