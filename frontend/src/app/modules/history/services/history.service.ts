import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import {
  CommitDetail,
  CommitDiffEntry,
  CommitSummary,
  DiffResult,
  RevisionInfo,
  VersionedEntity
} from '../types/commit';

export function historyError(err: unknown, fallback: string): string {
  const body = err instanceof HttpErrorResponse ? err.error : null;
  if (!body || typeof body !== 'object') {
    return fallback;
  }

  const record = body as { error?: string; conflicts?: { entity?: string; reason?: string }[]; inUse?: { entity?: string; name?: string }[] };
  let message = record.error || fallback;
  if (record.conflicts?.length) {
    const details = record.conflicts
      .map(item => [item.entity, item.reason].filter(Boolean).join(' '))
      .filter(Boolean);
    if (details.length) {
      message = `${message} (${details.join(', ')})`;
    }
  }
  if (record.inUse?.length) {
    const details = record.inUse.map(item => `${item.entity || 'item'} ${item.name || ''}`.trim());
    message = `${message}: ${details.join(', ')}`;
  }
  return message;
}

@Injectable({
  providedIn: 'root'
})
export class HistoryService {
  private readonly apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  getCommits(limit = 500, offset = 0): Observable<CommitSummary[]> {
    const params = new HttpParams()
      .set('_limit', String(limit))
      .set('_offset', String(offset));
    return this.http.get<CommitSummary[]>(`${this.apiUrl}/commit`, { params });
  }

  getCommit(id: number): Observable<CommitDetail> {
    return this.http.get<CommitDetail>(`${this.apiUrl}/commit/${id}`);
  }

  getCommitDiff(id: number): Observable<CommitDiffEntry[]> {
    return this.http.get<CommitDiffEntry[]>(`${this.apiUrl}/commit/${id}/diff`);
  }

  getHistory(entity: VersionedEntity | string, id: number): Observable<RevisionInfo[]> {
    return this.http.get<RevisionInfo[]>(`${this.apiUrl}/${entity}/${id}/history`);
  }

  getRevision(entity: VersionedEntity | string, id: number, revisionNo: number): Observable<unknown> {
    return this.http.get(`${this.apiUrl}/${entity}/${id}/revision/${revisionNo}`);
  }

  getDiff(
    entity: VersionedEntity | string,
    id: number,
    from: number,
    to: number
  ): Observable<DiffResult> {
    const params = new HttpParams()
      .set('from', String(from))
      .set('to', String(to));
    return this.http.get<DiffResult>(`${this.apiUrl}/${entity}/${id}/diff`, { params });
  }

  revertCommit(id: number, author = 'web-ui', entity?: string, entityId?: number): Observable<{ commitId: number }> {
    const body: { author: string; entity?: string; id?: number } = { author };
    if (entity && entityId != null) {
      body.entity = entity;
      body.id = entityId;
    }
    return this.http.post<{ commitId: number }>(`${this.apiUrl}/commit/${id}/revert`, body);
  }

  restoreRevision(
    entity: VersionedEntity | string,
    id: number,
    revisionNo: number,
    author = 'web-ui'
  ): Observable<{ commitId: number }> {
    return this.http.post<{ commitId: number }>(`${this.apiUrl}/${entity}/${id}/revert`, {
      author,
      revisionNo
    });
  }
}
