import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import { OrderNumber } from '../types/order-number';
import { Sort } from '@angular/material/sort';

@Injectable({
  providedIn: 'root'
})
export class OrderNumberService {

  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  // GET request to retrieve data
  getOrderNumbers(
    sort: Sort,
    filerPartNumberId: number | undefined = undefined,
    filerDistributorId: number | undefined = undefined
  ): Observable<OrderNumber[]> {

    let params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

      if (filerPartNumberId !== undefined && filerPartNumberId >= 0) {
        params = params.set('_part_number_id', filerPartNumberId);
      }

      if (filerDistributorId !== undefined && filerDistributorId >= 0) {
        params = params.set('_distributor_id', filerDistributorId);
      }

    return this.http.get<OrderNumber[]>(`${this.apiUrl}/ordernumber`, { params});
  }

  getOrderNumber(id: number): Observable<OrderNumber> {
    return this.http.get<OrderNumber>(`${this.apiUrl}/ordernumber/${id}`);
  }

  // POST request to send data
  postData(data: any): Observable<any> {
    return this.http.post(`${this.apiUrl}/ordernumber`, data);
  }

  // PUT request to update data
  updateData(id: number, data: any): Observable<any> {
    return this.http.put(`${this.apiUrl}/ordernumber/${id}`, data);
  }

  // DELETE request to delete data
  deleteData(id: number): Observable<any> {
    return this.http.delete(`${this.apiUrl}/ordernumber/${id}`);
  }
}
