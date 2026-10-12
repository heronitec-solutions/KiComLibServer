import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import { Distributor } from '../types/distributor';
import { Sort } from '@angular/material/sort';

@Injectable({
  providedIn: 'root'
})
export class DistributorService {

  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  // GET request to retrieve data
  getDistributors(sort: Sort): Observable<Distributor[]> {
    const params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

    return this.http.get<Distributor[]>(`${this.apiUrl}/distributor`, { params});
  }

  getDistributor(id: number): Observable<Distributor> {
    return this.http.get<Distributor>(`${this.apiUrl}/distributor/${id}`);
  }

  // POST request to send data
  postData(data: any): Observable<any> {
    return this.http.post(`${this.apiUrl}/distributor`, data);
  }

  // PUT request to update data
  updateData(id: number, data: any): Observable<any> {
    return this.http.put(`${this.apiUrl}/distributor/${id}`, data);
  }

  // DELETE request to delete data
  deleteData(id: number): Observable<any> {
    return this.http.delete(`${this.apiUrl}/distributor/${id}`);
  }
}
