import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import {
  asRecord,
  readBool,
  readNullableNumber,
  readNumber,
  readString,
  readStringList
} from '../../../shared/json-read';
import {
  DiscoveredProject,
  GitCredential,
  GitCredentialKind,
  GitCredentialWrite,
  GitHost,
  GitHostKind,
  GitHostWrite,
  GitRepository,
  GitRepositoryCreated,
  GitRepositoryStatus,
  GitSyncMode,
  GitTestResult,
  GitWebhookInfo,
  SshHostKeyScan
} from '../types/git';

@Injectable({
  providedIn: 'root'
})
export class GitService {
  private readonly apiUrl = '/api';

  constructor(private http: HttpClient) { }

  hosts(): Observable<GitHost[]> {
    return this.http.get<unknown>(`${this.apiUrl}/git-host`).pipe(
      map(body => readRows(body).map(normalizeHost))
    );
  }

  createHost(body: GitHostWrite): Observable<GitHost> {
    return this.http.post<unknown>(`${this.apiUrl}/git-host`, hostPayload(body)).pipe(
      map(response => normalizeHost(asRecord(response) ?? {}))
    );
  }

  updateHost(id: number, body: GitHostWrite): Observable<GitHost> {
    return this.http.put<unknown>(`${this.apiUrl}/git-host/${id}`, hostPayload(body)).pipe(
      map(response => normalizeHost(asRecord(response) ?? {}))
    );
  }

  deleteHost(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/git-host/${id}`);
  }

  scanHostKey(id: number): Observable<SshHostKeyScan> {
    return this.http.post<unknown>(`${this.apiUrl}/git-host/${id}/ssh-host-key`, {}).pipe(
      map(body => normalizeScan(body))
    );
  }

  confirmHostKey(id: number, knownHosts: string): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/git-host/${id}/ssh-host-key`, { knownHosts });
  }

  credentials(): Observable<GitCredential[]> {
    return this.http.get<unknown>(`${this.apiUrl}/git-credential`).pipe(
      map(body => readRows(body).map(normalizeCredential))
    );
  }

  createCredential(body: GitCredentialWrite): Observable<GitCredential> {
    const payload: Record<string, unknown> = {
      hostId: body.hostId,
      name: body.name,
      kind: body.kind
    };
    if (body.kind === 'https_token') {
      payload['username'] = body.username;
      payload['secret'] = body.secret;
      if (body.expiresAt) {
        payload['expiresAt'] = body.expiresAt;
      }
    } else if (body.generate) {
      payload['generate'] = true;
    } else {
      payload['generate'] = false;
      payload['secret'] = body.secret;
    }
    return this.http.post<unknown>(`${this.apiUrl}/git-credential`, payload).pipe(
      map(response => normalizeCredential(asRecord(response) ?? {}))
    );
  }

  replaceCredentialSecret(id: number, secret: string, username: string, expiresAt: string): Observable<GitCredential> {
    const payload: Record<string, unknown> = { secret };
    if (username) {
      payload['username'] = username;
    }
    if (expiresAt) {
      payload['expiresAt'] = expiresAt;
    }
    return this.http.put<unknown>(`${this.apiUrl}/git-credential/${id}`, payload).pipe(
      map(response => normalizeCredential(asRecord(response) ?? {}))
    );
  }

  deleteCredential(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/git-credential/${id}`);
  }

  testRepository(hostId: number, cloneUrl: string, credentialId: number): Observable<GitTestResult> {
    return this.http.post<unknown>(`${this.apiUrl}/git-repository/test`, { hostId, cloneUrl, credentialId }).pipe(
      map(body => normalizeTest(body))
    );
  }

  repositories(): Observable<GitRepository[]> {
    return this.http.get<unknown>(`${this.apiUrl}/git-repository`).pipe(
      map(body => readRows(body).map(row => normalizeRepository(row)))
    );
  }

  createRepository(body: {
    hostId: number;
    credentialId: number;
    cloneUrl: string;
    trackedBranches: string[];
    tagPattern: string;
    syncMode: GitSyncMode;
  }): Observable<GitRepositoryCreated> {
    return this.http.post<unknown>(`${this.apiUrl}/git-repository`, body).pipe(
      map(response => {
        const row = asRecord(response) ?? {};
        return {
          ...normalizeRepository(row),
          webhook: normalizeWebhook(row['webhook'])
        };
      })
    );
  }

  deleteRepository(id: number, deleteProjects: boolean): Observable<void> {
    const url = deleteProjects
      ? `${this.apiUrl}/git-repository/${id}?deleteProjects=true`
      : `${this.apiUrl}/git-repository/${id}`;
    return this.http.delete<void>(url);
  }

  syncRepository(id: number): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/git-repository/${id}/sync`, {});
  }

  rotateWebhook(id: number): Observable<GitWebhookInfo | null> {
    return this.http.post<unknown>(`${this.apiUrl}/git-repository/${id}/webhook-secret`, {}).pipe(
      map(body => normalizeWebhook(asRecord(body)?.['webhook'] ?? body))
    );
  }

  discovered(id: number): Observable<DiscoveredProject[]> {
    return this.http.get<unknown>(`${this.apiUrl}/git-repository/${id}/discovered`).pipe(
      map(body => readRows(body, 'projects', 'files', 'items').map(normalizeDiscovered))
    );
  }
}

function hostPayload(body: GitHostWrite): Record<string, unknown> {
  return {
    kind: body.kind,
    name: body.name,
    webUrl: body.webUrl,
    sshHost: body.sshHost || null,
    caCertificate: body.caCertificate || null,
    allowHttp: body.allowHttp
  };
}

function readRows(body: unknown, ...nested: string[]): Record<string, unknown>[] {
  if (Array.isArray(body)) {
    return body.map(asRecord).filter((row): row is Record<string, unknown> => !!row);
  }
  const row = asRecord(body);
  if (!row) {
    return [];
  }
  const keys = nested.length ? nested : ['items', 'value', 'results'];
  for (const key of keys) {
    const value = row[key];
    if (Array.isArray(value)) {
      return value.map(asRecord).filter((item): item is Record<string, unknown> => !!item);
    }
  }
  return [];
}

function hostKind(value: string): GitHostKind {
  if (value === 'github' || value === 'generic' || value === 'gitlab') {
    return value;
  }
  return 'generic';
}

function normalizeHost(row: Record<string, unknown>): GitHost {
  return {
    id: readNumber(row, 'id'),
    name: readString(row, 'name'),
    kind: hostKind(readString(row, 'kind')),
    webUrl: readString(row, 'webUrl'),
    sshHost: readString(row, 'sshHost'),
    caCertificate: readString(row, 'caCertificate'),
    allowHttp: readBool(row, 'allowHttp'),
    isSystem: readBool(row, 'isSystem'),
    sshKnownHosts: readString(row, 'sshKnownHosts')
  };
}

function normalizeScan(body: unknown): SshHostKeyScan {
  const row = asRecord(body) ?? {};
  const fingerprints = readStringList(row['fingerprints'] ?? row['fingerprint']);
  const knownHosts = readString(row, 'knownHosts', 'sshKnownHosts', 'known_hosts');
  if (!fingerprints.length && knownHosts) {
    return { fingerprints: knownHosts.split('\n').filter(line => line.trim()), knownHosts };
  }
  return { fingerprints, knownHosts };
}

function credentialKind(value: string): GitCredentialKind {
  return value === 'ssh_key' ? 'ssh_key' : 'https_token';
}

function normalizeCredential(row: Record<string, unknown>): GitCredential {
  return {
    id: readNumber(row, 'id'),
    hostId: readNumber(row, 'hostId'),
    name: readString(row, 'name'),
    kind: credentialKind(readString(row, 'kind')),
    username: readString(row, 'username'),
    secretHint: readString(row, 'secretHint', 'hint'),
    publicKey: readString(row, 'publicKey'),
    expiresAt: readString(row, 'expiresAt'),
    lastError: readString(row, 'lastError')
  };
}

function syncMode(value: string): GitSyncMode {
  return value === 'webhook' ? 'webhook' : 'poll';
}

function repoStatus(value: string): GitRepositoryStatus {
  if (value === 'ok' || value === 'error' || value === 'disabled' || value === 'new') {
    return value;
  }
  return 'new';
}

function normalizeRepository(row: Record<string, unknown>): GitRepository {
  return {
    id: readNumber(row, 'id'),
    hostId: readNumber(row, 'hostId'),
    credentialId: readNullableNumber(row, 'credentialId'),
    cloneUrl: readString(row, 'cloneUrl'),
    webUrl: readString(row, 'webUrl'),
    providerPath: readString(row, 'providerPath'),
    defaultBranch: readString(row, 'defaultBranch'),
    trackedBranches: readStringList(row['trackedBranches']),
    tagPattern: readString(row, 'tagPattern') || '*',
    syncMode: syncMode(readString(row, 'syncMode')),
    status: repoStatus(readString(row, 'status')),
    lastFetchAt: readString(row, 'lastFetchAt'),
    lastError: readString(row, 'lastError')
  };
}

function instructionsText(value: unknown): string {
  if (typeof value === 'string') {
    return value;
  }
  if (Array.isArray(value)) {
    return value.map(item => String(item)).join('\n');
  }
  const row = asRecord(value);
  if (!row) {
    return '';
  }
  if (typeof row['text'] === 'string') {
    return row['text'];
  }
  if (Array.isArray(row['steps'])) {
    return row['steps'].map(item => String(item)).join('\n');
  }
  return Object.keys(row).map(key => `${key}: ${String(row[key])}`).join('\n');
}

function normalizeWebhook(value: unknown): GitWebhookInfo | null {
  const row = asRecord(value);
  if (!row) {
    return null;
  }
  const url = readString(row, 'url');
  const secret = readString(row, 'secret');
  const instructions = instructionsText(row['instructions']);
  if (!url && !secret && !instructions) {
    return null;
  }
  return { url, secret, instructions };
}

function normalizeTest(body: unknown): GitTestResult {
  const row = asRecord(body) ?? {};
  return {
    ok: readBool(row, 'ok'),
    defaultBranch: readString(row, 'defaultBranch'),
    branches: readStringList(row['branches']),
    tags: readStringList(row['tags']),
    error: readString(row, 'error')
  };
}

function normalizeDiscovered(row: Record<string, unknown>): DiscoveredProject {
  const projectFile = readString(row, 'projectFile', 'path', 'file');
  const fileName = projectFile.split('/').pop() || projectFile;
  const stem = fileName.replace(/\.kicad_pro$/i, '');
  return {
    projectFile,
    name: readString(row, 'name') || stem,
    alreadyProject: readBool(row, 'alreadyProject', 'isProject', 'exists'),
    projectId: readNullableNumber(row, 'projectId')
  };
}
