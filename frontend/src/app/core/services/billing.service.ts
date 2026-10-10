import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BillingRules, BillingSummary, Statement, StatementDetail } from '../models/billing.models';

/** Module 8: statements, "your bill" and PDF download. */
@Injectable({ providedIn: 'root' })
export class BillingService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/billing`;

  rules(): Observable<BillingRules> {
    return this.http.get<BillingRules>(`${this.apiUrl}/rules`);
  }

  summary(cardId: number): Observable<BillingSummary> {
    return this.http.get<BillingSummary>(`${this.apiUrl}/cards/${cardId}/summary`);
  }

  statements(cardId: number): Observable<Statement[]> {
    return this.http.get<Statement[]>(`${this.apiUrl}/cards/${cardId}/statements`);
  }

  /** Admin: close the billing cycle now. */
  generate(cardId: number): Observable<Statement> {
    return this.http.post<Statement>(`${this.apiUrl}/cards/${cardId}/statements`, {});
  }

  detail(statementId: number): Observable<StatementDetail> {
    return this.http.get<StatementDetail>(`${this.apiUrl}/statements/${statementId}`);
  }

  /** Downloads the PDF (with the JWT, so not a plain link) and lets the browser save it. */
  downloadPdf(statementId: number): Observable<void> {
    return this.http.get(`${this.apiUrl}/statements/${statementId}/pdf`, { responseType: 'blob', observe: 'response' }).pipe(
      map(response => {
        const disposition = response.headers.get('Content-Disposition') ?? '';
        const name = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? `statement-${statementId}.pdf`;
        const url = URL.createObjectURL(response.body!);
        const link = document.createElement('a');
        link.href = url;
        link.download = name;
        link.click();
        URL.revokeObjectURL(url);
      }));
  }
}
