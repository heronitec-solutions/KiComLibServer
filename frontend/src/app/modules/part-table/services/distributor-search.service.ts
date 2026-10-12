import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { appEnvironment } from '../../../app-environment';
import { apiErrorMessage } from '../../../shared/api-error';
import { LifecycleStatus } from '../types/part';

export interface DistributorProviderInfo {
  id: string;
  name: string;
  enabled: boolean;
}

export interface DistributorProvidersResult {
  providers: DistributorProviderInfo[];
  lookupProvider: string | null;
}

export interface DistributorProduct {
  provider: string;
  manufacturer: string | null;
  manufacturerPartNumber: string | null;
  description: string | null;
  datasheetUrl: string | null;
  productUrl: string | null;
  imageUrl: string | null;
  lifecycleStatus: LifecycleStatus | null;
  package: string | null;
  distributorName: string | null;
  orderNumber: string | null;
  quantityMin: number | null;
  quantitySteps: number | null;
}

export interface DistributorProviderError {
  provider: string;
  message: string;
}

export interface DistributorKeywordResult {
  provider: string;
  results: DistributorProduct[];
}

export interface DistributorMultiResult {
  results: DistributorProduct[];
  errors: DistributorProviderError[];
}

export interface DistributorDatasheetResult {
  id: number;
  uuid: string;
  filename: string;
  directory: string;
}

export interface DistributorPartQuery {
  manufacturerPartNumber: string;
  manufacturer?: string;
}

/** Reads `{ error }` from a distributor-search failure body. */
export function distributorSearchError(error: unknown, fallback: string): string {
  return apiErrorMessage(error, fallback);
}

@Injectable({
  providedIn: 'root'
})
export class DistributorSearchService {
  private apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  getProviders(): Observable<DistributorProvidersResult> {
    return this.http.get<DistributorProvidersResult>(`${this.apiUrl}/distributor-search/providers`);
  }

  /** Keyword search. Pass `provider` to override the admin default. */
  keyword(query: string, provider?: string): Observable<DistributorKeywordResult> {
    const body: { query: string; provider?: string } = { query };
    if (provider) {
      body.provider = provider;
    }

    return this.http.post<DistributorKeywordResult>(`${this.apiUrl}/distributor-search/keyword`, body);
  }

  alternatives(manufacturerPartNumber: string, manufacturer?: string): Observable<DistributorMultiResult> {
    const body: { manufacturerPartNumber: string; manufacturer?: string } = { manufacturerPartNumber };
    if (manufacturer) {
      body.manufacturer = manufacturer;
    }

    return this.http.post<DistributorMultiResult>(`${this.apiUrl}/distributor-search/alternatives`, body);
  }

  orders(partNumbers: DistributorPartQuery[]): Observable<DistributorMultiResult> {
    return this.http.post<DistributorMultiResult>(`${this.apiUrl}/distributor-search/orders`, { partNumbers });
  }

  datasheet(url: string, filename?: string): Observable<DistributorDatasheetResult> {
    const body: { url: string; filename?: string } = { url };
    if (filename) {
      body.filename = filename;
    }

    return this.http.post<DistributorDatasheetResult>(`${this.apiUrl}/distributor-search/datasheet`, body);
  }

  importImage(url: string): Observable<{ hash: string }> {
    return this.http.post<{ hash: string }>(`${this.apiUrl}/distributor-search/image`, { url });
  }
}
