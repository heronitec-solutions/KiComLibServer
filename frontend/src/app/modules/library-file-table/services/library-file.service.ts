import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import { LibraryFile } from '../types/library-file';
import { Sort } from '@angular/material/sort';

@Injectable({
  providedIn: 'root'
})
export class LibraryFileService {

  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  // GET request to retrieve data
  getLibraryFiles(sort: Sort): Observable<LibraryFile[]> {
    const params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

    return this.http.get<LibraryFile[]>(`${this.apiUrl}/libraryfile`, { params});
  }

  getLibraryFile(id: number): Observable<LibraryFile> {
    return this.http.get<LibraryFile>(`${this.apiUrl}/libraryfile/${id}`);
  }

  // POST request to send data
  postData(data: any): Observable<any> {
    return this.http.post(`${this.apiUrl}/libraryfile`, data);
  }

  // PUT request to update data
  updateData(id: number, data: any): Observable<any> {
    return this.http.put(`${this.apiUrl}/libraryfile/${id}`, data);
  }

  // DELETE request to delete data
  deleteData(id: number): Observable<any> {
    return this.http.delete(`${this.apiUrl}/libraryfile/${id}`);
  }
}
