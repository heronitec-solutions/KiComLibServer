import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../app-environment';

export interface DirectorySettings {
  enabled: boolean;
  serverType: string;
  host: string;
  port: number;
  encryption: string;
  allowUntrustedCertificate: boolean;
  bindDn: string;
  bindPasswordSet: boolean;
  userBaseDn: string;
  userFilter: string;
  usernameAttribute: string;
  displayNameAttribute: string;
  userIdAttribute: string;
  groupBaseDn: string;
  groupFilter: string;
  groupNameAttribute: string;
  groupIdAttribute: string;
  membershipAttribute: string;
  provisionOnLogin: boolean;
  configured: boolean;
}

export interface DirectoryWrite {
  enabled: boolean;
  serverType: string;
  host: string;
  port: number;
  encryption: string;
  allowUntrustedCertificate: boolean;
  bindDn: string;
  bindPassword?: string;
  userBaseDn: string;
  userFilter: string;
  usernameAttribute: string;
  displayNameAttribute: string;
  userIdAttribute: string;
  groupBaseDn: string;
  groupFilter: string;
  groupNameAttribute: string;
  groupIdAttribute: string;
  membershipAttribute: string;
  provisionOnLogin: boolean;
}

export interface DirectoryUserHit {
  id: string;
  username: string;
  displayName: string;
  distinguishedName: string;
}

export interface DirectoryGroupHit {
  id: string;
  name: string;
  description: string;
  distinguishedName: string;
}

export interface DirectoryImportResult {
  imported: { id: number; username?: string; name?: string; distinguishedName: string }[];
  errors: { distinguishedName: string; error: string }[];
}

export const LDAP_PRESET = {
  serverType: 'ldap',
  port: 389,
  encryption: 'starttls',
  userFilter: '(&(objectClass=inetOrgPerson)(uid={username}))',
  usernameAttribute: 'uid',
  displayNameAttribute: 'displayName',
  userIdAttribute: 'entryUUID',
  groupFilter: '(objectClass=groupOfNames)',
  groupNameAttribute: 'cn',
  groupIdAttribute: 'entryUUID',
  membershipAttribute: 'member'
};

export const AD_PRESET = {
  serverType: 'ad',
  port: 389,
  encryption: 'starttls',
  userFilter: '(&(objectCategory=person)(objectClass=user)(sAMAccountName={username}))',
  usernameAttribute: 'sAMAccountName',
  displayNameAttribute: 'displayName',
  userIdAttribute: 'objectGUID',
  groupFilter: '(objectClass=group)',
  groupNameAttribute: 'cn',
  groupIdAttribute: 'objectGUID',
  membershipAttribute: 'memberOf'
};

@Injectable({
  providedIn: 'root'
})
export class DirectoryService {
  private readonly apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  get(): Observable<DirectorySettings> {
    return this.http.get<DirectorySettings>(`${this.apiUrl}/directory`);
  }

  save(body: DirectoryWrite): Observable<DirectorySettings> {
    return this.http.put<DirectorySettings>(`${this.apiUrl}/directory`, body);
  }

  test(body: DirectoryWrite): Observable<{ ok: boolean; message: string }> {
    return this.http.post<{ ok: boolean; message: string }>(`${this.apiUrl}/directory/test`, body);
  }

  searchUsers(query: string): Observable<DirectoryUserHit[]> {
    const params = new HttpParams().set('q', query);
    return this.http.get<DirectoryUserHit[]>(`${this.apiUrl}/directory/users`, { params });
  }

  searchGroups(query: string): Observable<DirectoryGroupHit[]> {
    const params = new HttpParams().set('q', query);
    return this.http.get<DirectoryGroupHit[]>(`${this.apiUrl}/directory/groups`, { params });
  }

  importUsers(distinguishedNames: string[]): Observable<DirectoryImportResult> {
    return this.http.post<DirectoryImportResult>(`${this.apiUrl}/directory/users/import`, { distinguishedNames });
  }

  importGroups(
    distinguishedNames: string[],
    canRead: boolean,
    canWrite: boolean,
    canAdmin: boolean,
    canReadWarehouse = false,
    canWriteWarehouse = false
  ): Observable<DirectoryImportResult> {
    return this.http.post<DirectoryImportResult>(`${this.apiUrl}/directory/groups/import`, {
      distinguishedNames,
      canRead,
      canWrite,
      canAdmin,
      canReadWarehouse,
      canWriteWarehouse
    });
  }
}
