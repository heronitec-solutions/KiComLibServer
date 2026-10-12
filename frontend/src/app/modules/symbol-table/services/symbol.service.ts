import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import { Symbol } from '../types/symbol';
import { Sort } from '@angular/material/sort';

@Injectable({
  providedIn: 'root'
})
export class SymbolService {

  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  // GET request to retrieve data
  getSymbols(sort: Sort): Observable<Symbol[]> {
    const params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

    return this.http.get<Symbol[]>(`${this.apiUrl}/symbol`, { params});
  }

  getSymbolPreview(id: number): Observable<string> {
      return this.http.get(`${this.apiUrl}/symbol/preview/${id}`, { responseType: 'text' });
  }
}
