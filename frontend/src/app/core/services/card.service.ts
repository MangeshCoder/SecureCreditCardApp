import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Card, ChangePinRequest, IssueCardRequest, IssuedCardResponse, RevealCardNumberResponse
} from '../models/card.models';

@Injectable({ providedIn: 'root' })
export class CardService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/cards`;

  getMyCards(): Observable<Card[]> {
    return this.http.get<Card[]>(`${this.apiUrl}/my`);
  }

  getAllCards(): Observable<Card[]> {
    return this.http.get<Card[]>(this.apiUrl);
  }

  issueCard(request: IssueCardRequest): Observable<IssuedCardResponse> {
    return this.http.post<IssuedCardResponse>(this.apiUrl, request);
  }

  blockCard(cardId: number): Observable<Card> {
    return this.http.post<Card>(`${this.apiUrl}/${cardId}/block`, {});
  }

  activateCard(cardId: number): Observable<Card> {
    return this.http.post<Card>(`${this.apiUrl}/${cardId}/activate`, {});
  }

  updateCreditLimit(cardId: number, newCreditLimit: number): Observable<Card> {
    return this.http.put<Card>(`${this.apiUrl}/${cardId}/credit-limit`, { newCreditLimit });
  }

  changePin(cardId: number, request: ChangePinRequest): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${cardId}/pin`, request);
  }

  revealCardNumber(cardId: number, pin: string): Observable<RevealCardNumberResponse> {
    return this.http.post<RevealCardNumberResponse>(`${this.apiUrl}/${cardId}/reveal`, { pin });
  }
}
