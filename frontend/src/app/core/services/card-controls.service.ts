import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Card } from '../models/card.models';
import { CardControls, UpdateCardControlsRequest } from '../models/card-controls.models';

/** Module 6: card controls (channel switches, daily limits) and the temporary lock. */
@Injectable({ providedIn: 'root' })
export class CardControlsService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/cards`;

  get(cardId: number): Observable<CardControls> {
    return this.http.get<CardControls>(`${this.apiUrl}/${cardId}/controls`);
  }

  update(cardId: number, request: UpdateCardControlsRequest): Observable<CardControls> {
    return this.http.put<CardControls>(`${this.apiUrl}/${cardId}/controls`, request);
  }

  lock(cardId: number): Observable<Card> {
    return this.http.post<Card>(`${this.apiUrl}/${cardId}/lock`, {});
  }

  unlock(cardId: number): Observable<Card> {
    return this.http.post<Card>(`${this.apiUrl}/${cardId}/unlock`, {});
  }
}
