import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Card } from '../models/card.models';
import {
  CardEmiSummary, EligibleTransaction, EmiCalculation, EmiOption, EmiPlan, EmiRules
} from '../models/emi.models';

@Injectable({ providedIn: 'root' })
export class EmiService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/emi`;

  getRules(): Observable<EmiRules> {
    return this.http.get<EmiRules>(`${this.apiUrl}/rules`);
  }

  /** The bank decides the interest rate - the client only sends amount and tenure. */
  previewEmiPlan(principalAmount: number, tenureMonths: number): Observable<EmiCalculation> {
    return this.http.post<EmiCalculation>(`${this.apiUrl}/calculate-preview`, { principalAmount, tenureMonths });
  }

  getOptions(amount: number): Observable<EmiOption[]> {
    return this.http.get<EmiOption[]>(`${this.apiUrl}/options`, { params: new HttpParams().set('amount', amount) });
  }

  getEligible(cardId: number): Observable<EligibleTransaction[]> {
    return this.http.get<EligibleTransaction[]>(`${this.apiUrl}/eligible/card/${cardId}`);
  }

  convertTransactionToEmi(transactionId: number, tenureMonths: number): Observable<EmiPlan> {
    return this.http.post<EmiPlan>(`${this.apiUrl}/convert-transaction/${transactionId}`, { tenureMonths });
  }

  getPlans(cardId: number): Observable<EmiPlan[]> {
    return this.http.get<EmiPlan[]>(`${this.apiUrl}/plans/card/${cardId}`);
  }

  getSummary(cardId: number): Observable<CardEmiSummary> {
    return this.http.get<CardEmiSummary>(`${this.apiUrl}/summary/card/${cardId}`);
  }

  payInstallment(emiPlanId: number, installmentNumber: number): Observable<{ plan: EmiPlan; card: Card; transactionId: number }> {
    return this.http.post<{ plan: EmiPlan; card: Card; transactionId: number }>(
      `${this.apiUrl}/plans/${emiPlanId}/installments/${installmentNumber}/pay`, {});
  }
}
