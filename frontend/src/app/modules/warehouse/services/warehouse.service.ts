import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Sort } from '@angular/material/sort';

import { appEnvironment } from '../../../app-environment';
import { WarehouseItem, WarehouseWrite } from '../types/warehouse';

@Injectable()
export class WarehouseService {
  private readonly apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  list(sort: Sort): Observable<WarehouseItem[]> {
    const params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

    return this.http.get<WarehouseItem[]>(`${this.apiUrl}/warehouse`, { params });
  }

  get(id: number): Observable<WarehouseItem> {
    return this.http.get<WarehouseItem>(`${this.apiUrl}/warehouse/${id}`);
  }

  create(body: WarehouseWrite): Observable<WarehouseItem> {
    return this.http.post<WarehouseItem>(`${this.apiUrl}/warehouse`, body);
  }

  update(id: number, body: WarehouseWrite): Observable<WarehouseItem> {
    return this.http.put<WarehouseItem>(`${this.apiUrl}/warehouse/${id}`, body);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/warehouse/${id}`);
  }

  imageUrl(id: number): string {
    return `${this.apiUrl}/warehouse/${id}/image`;
  }

  /** Photo stored by hash, before it is attached to a manual entry. */
  imageContentUrl(hash: string): string {
    return `${this.apiUrl}/warehouse/image/${hash}`;
  }

  uploadImage(file: File): Observable<{ hash: string }> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.post<{ hash: string }>(`${this.apiUrl}/warehouse/image`, body);
  }
}
