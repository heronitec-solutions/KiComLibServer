import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import { Footprint } from '../types/footprint';
import { Sort } from '@angular/material/sort';

@Injectable({
  providedIn: 'root'
})
export class FootprintService {

  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  // GET request to retrieve data
  getFootprints(sort: Sort): Observable<Footprint[]> {
    const params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

    return this.http.get<Footprint[]>(`${this.apiUrl}/footprint`, { params});
  }

  getFootprint(id: number): Observable<Footprint> {
    return this.http.get<Footprint>(`${this.apiUrl}/footprint/${id}`);
  }

  getFootprintPreview(id: number): Observable<string> {
    return this.http.get(`${this.apiUrl}/footprint/preview/${id}`, { responseType: 'text' });
  }
}
