import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import { Library } from '../types/library';
import { Sort } from '@angular/material/sort';

@Injectable({
  providedIn: 'root'
})
export class LibraryService {

  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  // GET request to retrieve data
  getLibraries(sort: Sort): Observable<Library[]> {
    const params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

    return this.http.get<Library[]>(`${this.apiUrl}/library`, { params});
  }

  getLibrary(id: number): Observable<Library> {
    return this.http.get<Library>(`${this.apiUrl}/library/${id}`);
  }

  // POST request to send data
  postData(data: any): Observable<any> {
    return this.http.post(`${this.apiUrl}/library`, data);
  }

  // PUT request to update data
  updateData(id: number, data: any): Observable<any> {
    return this.http.put(`${this.apiUrl}/library/${id}`, data);
  }

  // DELETE request to delete data
  deleteData(id: number): Observable<any> {
    return this.http.delete(`${this.apiUrl}/library/${id}`);
  }
}
