import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, finalize, map, of, shareReplay, tap } from 'rxjs';

import { appEnvironment } from '../app-environment';

export interface AccessGroup {
  id: number;
  name: string;
  canRead: boolean;
  canWrite: boolean;
  canAdmin?: boolean;
  canReadWarehouse?: boolean;
  canWriteWarehouse?: boolean;
  description?: string;
  isSystem?: boolean;
  source?: string;
  directoryDn?: string | null;
  origin?: string;
  memberCount?: number;
}

export interface AccessProfile {
  authenticated: boolean;
  username?: string | null;
  source?: string | null;
  isAdmin: boolean;
  canRead: boolean;
  canWrite: boolean;
  canReadWarehouse: boolean;
  canWriteWarehouse: boolean;
  mustChangePassword?: boolean;
  authType?: string;
  groups: AccessGroup[];
}

export interface AppUser {
  id: number;
  username: string;
  displayName?: string;
  source?: string;
  isAdmin: boolean;
  isActive: boolean;
  canRead: boolean;
  canWrite: boolean;
  canReadWarehouse: boolean;
  canWriteWarehouse: boolean;
  groups: AccessGroup[];
}

export interface ApiTokenInfo {
  id: number;
  name: string;
  prefix: string;
  canWrite: boolean;
  createdAt: string;
  expiresAt?: string | null;
  lastUsedAt?: string | null;
  token?: string;
}

export interface UserWrite {
  username: string;
  password?: string;
  isActive: boolean;
  groupIds: number[];
}

export interface GroupWrite {
  name: string;
  description: string;
  canRead: boolean;
  canWrite: boolean;
  canAdmin: boolean;
  canReadWarehouse: boolean;
  canWriteWarehouse: boolean;
}

const ANONYMOUS: AccessProfile = {
  authenticated: false,
  username: null,
  isAdmin: false,
  canRead: true,
  canWrite: false,
  canReadWarehouse: false,
  canWriteWarehouse: false,
  groups: []
};

export { apiErrorMessage } from '../shared/api-error';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly apiUrl = appEnvironment.apiUrl;
  private pendingSession: Observable<AccessProfile> | null = null;

  readonly profile = signal<AccessProfile>({ ...ANONYMOUS, groups: [] });

  constructor(private http: HttpClient) {
    localStorage.removeItem('kicomlib.session');
    this.ensureSession().subscribe();
  }

  canRead(): boolean {
    return this.profile().canRead === true;
  }

  canWrite(): boolean {
    return this.profile().canWrite === true;
  }

  canReadWarehouse(): boolean {
    const profile = this.profile();
    return profile.canReadWarehouse === true || profile.canWriteWarehouse === true;
  }

  canWriteWarehouse(): boolean {
    return this.profile().canWriteWarehouse === true;
  }

  ensureSession(): Observable<AccessProfile> {
    if (this.profile().authenticated) {
      return of(this.profile());
    }

    if (!this.pendingSession) {
      this.pendingSession = this.http.get<AccessProfile>(`${this.apiUrl}/auth/me`).pipe(
        tap(profile => {
          if (profile.authenticated) {
            this.profile.set(profile);
          } else {
            this.clearSession();
          }
        }),
        catchError(error => {
          if ((error as { status?: number }).status === 401) {
            this.clearSession();
          }
          return of(this.profile());
        }),
        finalize(() => {
          this.pendingSession = null;
        }),
        shareReplay(1)
      );
    }

    return this.pendingSession;
  }

  refresh(): Observable<AccessProfile> {
    return this.ensureSession();
  }

  login(username: string, password: string): Observable<AccessProfile> {
    return this.http.post<{ user: AccessProfile }>(`${this.apiUrl}/auth/login`, { username, password }).pipe(
      tap(result => {
        this.profile.set({ ...result.user, authenticated: true });
      }),
      map(() => this.profile())
    );
  }

  logout(): Observable<void> {
    return this.http.post(`${this.apiUrl}/auth/logout`, {}).pipe(
      catchError(() => of(null)),
      tap(() => this.clearSession()),
      map(() => undefined)
    );
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http.post<{ user?: AccessProfile }>(`${this.apiUrl}/auth/password`, { currentPassword, newPassword }).pipe(
      tap(result => {
        if (result?.user) {
          this.profile.set({ ...result.user, authenticated: true });
        }
      }),
      map(() => undefined)
    );
  }

  clearSession(): void {
    localStorage.removeItem('kicomlib.session');
    this.profile.set({ ...ANONYMOUS, groups: [] });
  }

  getUsers(): Observable<AppUser[]> {
    return this.http.get<AppUser[]>(`${this.apiUrl}/user`);
  }

  createUser(body: UserWrite): Observable<AppUser> {
    return this.http.post<AppUser>(`${this.apiUrl}/user`, body);
  }

  updateUser(id: number, body: UserWrite): Observable<AppUser> {
    return this.http.put<AppUser>(`${this.apiUrl}/user/${id}`, body);
  }

  deleteUser(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/user/${id}`);
  }

  getGroups(): Observable<AccessGroup[]> {
    return this.http.get<AccessGroup[]>(`${this.apiUrl}/group`);
  }

  createGroup(body: GroupWrite): Observable<AccessGroup> {
    return this.http.post<AccessGroup>(`${this.apiUrl}/group`, body);
  }

  updateGroup(id: number, body: GroupWrite): Observable<AccessGroup> {
    return this.http.put<AccessGroup>(`${this.apiUrl}/group/${id}`, body);
  }

  deleteGroup(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/group/${id}`);
  }

  getTokens(): Observable<ApiTokenInfo[]> {
    return this.http.get<ApiTokenInfo[]>(`${this.apiUrl}/token`);
  }

  createToken(name: string, canWrite: boolean): Observable<ApiTokenInfo> {
    return this.http.post<ApiTokenInfo>(`${this.apiUrl}/token`, { name, canWrite });
  }

  revokeToken(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/token/${id}`);
  }
}
