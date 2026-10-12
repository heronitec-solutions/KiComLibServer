import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import { Manufacturer } from '../types/manufacturer';
import { Sort } from '@angular/material/sort';

@Injectable({
  providedIn: 'root'
})
export class ManufacturerService {

  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  // GET request to retrieve data
  getManufacturers(sort: Sort): Observable<Manufacturer[]> {
    const params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

    return this.http.get<Manufacturer[]>(`${this.apiUrl}/manufacturer`, { params});
  }

  getManufacturer(id: number): Observable<Manufacturer> {
    return this.http.get<Manufacturer>(`${this.apiUrl}/manufacturer/${id}`);
  }

  // POST request to send data
  postData(data: any): Observable<any> {
    return this.http.post(`${this.apiUrl}/manufacturer`, data);
  }

  // PUT request to update data
  updateData(id: number, data: any): Observable<any> {
    return this.http.put(`${this.apiUrl}/manufacturer/${id}`, data);
  }

  // DELETE request to delete data
  deleteData(id: number): Observable<any> {
    return this.http.delete(`${this.apiUrl}/manufacturer/${id}`);
  }
}
