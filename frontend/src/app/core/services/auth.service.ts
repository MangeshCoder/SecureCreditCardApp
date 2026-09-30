import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResponse, LoginRequest, RegisterRequest, UserInfo } from '../models/auth.models';

const STORAGE_KEY = 'secure-emi.session';

interface StoredSession {
  token: string;
  expiresAtUtc: string;
  user: UserInfo;
}

/**
 * Holds the logged-in session in an Angular signal.
 * The token is kept in sessionStorage (cleared when the tab closes) rather than localStorage,
 * which limits how long a stolen token remains on the device.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly apiUrl = `${environment.apiUrl}/api/auth`;

  private readonly session = signal<StoredSession | null>(this.restore());

  readonly currentUser = computed(() => this.session()?.user ?? null);
  readonly isLoggedIn = computed(() => this.session() !== null);
  readonly isAdmin = computed(() => this.session()?.user.role === 'Admin');

  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/register`, request).pipe(tap(r => this.store(r)));
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/login`, request).pipe(tap(r => this.store(r)));
  }

  logout(): void {
    sessionStorage.removeItem(STORAGE_KEY);
    this.session.set(null);
    this.router.navigate(['/login']);
  }

  /** Returns the JWT only while it is still valid. */
  getToken(): string | null {
    const s = this.session();
    if (!s) return null;
    if (new Date(s.expiresAtUtc).getTime() <= Date.now()) {
      this.logout();
      return null;
    }
    return s.token;
  }

  private store(response: AuthResponse): void {
    const s: StoredSession = { token: response.token, expiresAtUtc: response.expiresAtUtc, user: response.user };
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(s));
    this.session.set(s);
  }

  private restore(): StoredSession | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const s = JSON.parse(raw) as StoredSession;
      return new Date(s.expiresAtUtc).getTime() > Date.now() ? s : null;
    } catch {
      return null;
    }
  }
}
