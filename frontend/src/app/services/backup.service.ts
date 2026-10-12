import { Injectable } from '@angular/core';
import { HttpClient, HttpEvent } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../app-environment';

export interface BackupInfo {
  id: string;
  name: string;
  fileName: string;
  createdAt: string;
  sizeBytes: number;
  source: 'created' | 'uploaded';
  encrypted: boolean;
}

export interface BackupRestoreResult {
  restored: string;
  restarting: boolean;
  keysRestored: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class BackupService {
  private readonly apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  list(): Observable<BackupInfo[]> {
    return this.http.get<BackupInfo[]>(`${this.apiUrl}/backup`);
  }

  create(name: string, passphrase: string): Observable<BackupInfo> {
    return this.http.post<BackupInfo>(`${this.apiUrl}/backup`, { name, passphrase });
  }

  upload(file: File, name: string): Observable<HttpEvent<BackupInfo>> {
    const body = new FormData();
    body.append('file', file);
    if (name.trim()) {
      body.append('name', name.trim());
    }
    return this.http.post<BackupInfo>(`${this.apiUrl}/backup/upload`, body, {
      reportProgress: true,
      observe: 'events'
    });
  }

  download(backup: BackupInfo): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/backup/${backup.id}/file`, { responseType: 'blob' });
  }

  restore(id: string, passphrase: string): Observable<BackupRestoreResult> {
    return this.http.post<BackupRestoreResult>(`${this.apiUrl}/backup/${id}/restore`, { passphrase });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/backup/${id}`);
  }
}
