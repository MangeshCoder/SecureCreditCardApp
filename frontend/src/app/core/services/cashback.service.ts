import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CashbackLog, CashbackRules, CashbackSummary } from '../models/cashback.models';
import { PagedResult } from '../models/transaction.models';

@Injectable({ providedIn: 'root' })
export class CashbackService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/cashback`;

  getRules(): Observable<CashbackRules> {
    return this.http.get<CashbackRules>(`${this.apiUrl}/rules`);
  }

  getSummary(cardId: number): Observable<CashbackSummary> {
    return this.http.get<CashbackSummary>(`${this.apiUrl}/card/${cardId}/summary`);
  }

  getHistory(cardId: number, page = 1, pageSize = 10): Observable<PagedResult<CashbackLog>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<CashbackLog>>(`${this.apiUrl}/card/${cardId}`, { params });
  }
}
