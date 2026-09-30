import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Cardholder } from '../models/card.models';

/** Admin-only back-office API for customers. */
@Injectable({ providedIn: 'root' })
export class CardholderService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/cardholders`;

  getAll(): Observable<Cardholder[]> {
    return this.http.get<Cardholder[]>(this.apiUrl);
  }

  setActive(cardholderId: number, active: boolean): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${cardholderId}/${active ? 'activate' : 'deactivate'}`, {});
  }
}
