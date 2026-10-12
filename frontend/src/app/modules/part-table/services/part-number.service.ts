import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import { PartNumber } from '../types/part-number';
import { Sort } from '@angular/material/sort';

@Injectable({
  providedIn: 'root'
})
export class PartNumberService {

  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  // GET request to retrieve data
  getPartNumbers(
    sort: Sort,
    filerPartId: number | undefined = undefined,
    filerManufacturerId: number | undefined = undefined
  ): Observable<PartNumber[]> {

    let params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

    if (filerPartId !== undefined && filerPartId >= 0) {
      params = params.set('_part_id', filerPartId);
    }

    if (filerManufacturerId !== undefined && filerManufacturerId >= 0) {
      params = params.set('_manufacturer_id', filerManufacturerId);
    }

    return this.http.get<PartNumber[]>(`${this.apiUrl}/partnumber`, { params });
  }

  getPartNumber(id: number): Observable<PartNumber> {
    return this.http.get<PartNumber>(`${this.apiUrl}/partnumber/${id}`);
  }

  // POST request to send data
  postData(data: any): Observable<any> {
    return this.http.post(`${this.apiUrl}/partnumber`, data);
  }

  // PUT request to update data
  updateData(id: number, data: any): Observable<any> {
    return this.http.put(`${this.apiUrl}/partnumber/${id}`, data);
  }

  // DELETE request to delete data
  deleteData(id: number): Observable<any> {
    return this.http.delete(`${this.apiUrl}/partnumber/${id}`);
  }
}
