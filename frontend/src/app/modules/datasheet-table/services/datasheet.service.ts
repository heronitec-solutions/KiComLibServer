import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import { Datasheet } from '../types/datasheet';
import { Sort } from '@angular/material/sort';

@Injectable({
  providedIn: 'root'
})
export class DatasheetService {

  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  // GET request to retrieve data
  getDatasheets(sort: Sort): Observable<Datasheet[]> {
    const params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

    return this.http.get<Datasheet[]>(`${this.apiUrl}/datasheet`, { params});
  }

  /** Opens the PDF inline. Pass download to send it as an attachment. */
  fileUrl(id: number, download = false): string {
    const url = `${this.apiUrl}/datasheet/${id}/file`;
    return download ? `${url}?download=true` : url;
  }
}
