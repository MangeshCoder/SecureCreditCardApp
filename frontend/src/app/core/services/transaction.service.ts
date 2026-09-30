import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  BalanceChangeResponse, MerchantCategory, PagedResult, SwipeRequest, SwipeResponse, Transaction
} from '../models/transaction.models';

@Injectable({ providedIn: 'root' })
export class TransactionService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/transactions`;

  swipe(request: SwipeRequest): Observable<SwipeResponse> {
    return this.http.post<SwipeResponse>(`${this.apiUrl}/swipe`, request);
  }

  load(cardId: number, amount: number): Observable<BalanceChangeResponse> {
    return this.http.post<BalanceChangeResponse>(`${this.apiUrl}/load`, { cardId, amount });
  }

  refund(transactionId: number): Observable<BalanceChangeResponse> {
    return this.http.post<BalanceChangeResponse>(`${this.apiUrl}/${transactionId}/refund`, {});
  }

  getForCard(cardId: number, page = 1, pageSize = 10): Observable<PagedResult<Transaction>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<Transaction>>(`${this.apiUrl}/card/${cardId}`, { params });
  }

  getAll(page = 1, pageSize = 20): Observable<PagedResult<Transaction>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<Transaction>>(this.apiUrl, { params });
  }

  getMerchantCategories(): Observable<MerchantCategory[]> {
    return this.http.get<MerchantCategory[]>(`${this.apiUrl}/merchant-categories`);
  }
}
