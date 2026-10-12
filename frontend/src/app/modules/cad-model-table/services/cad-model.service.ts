import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CadModel } from '../types/cad-model';
import { Sort } from '@angular/material/sort';
import { appEnvironment } from '../../../app-environment';

@Injectable({
  providedIn: 'root'
})
export class CadModelService {

  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  // GET request to retrieve data
  getCadModels(sort: Sort): Observable<CadModel[]> {
    const params = new HttpParams()
      .set('_sort', sort.active)
      .set('_order', sort.direction);

    return this.http.get<CadModel[]>(`${this.apiUrl}/model3d`, { params});
  }
}
