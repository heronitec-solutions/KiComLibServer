import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable, of, throwError, timer } from 'rxjs';
import { switchMap, takeUntil } from 'rxjs/operators';

/**
 * Loads SVG bytes with the bearer interceptor and exposes them only as blob
 * URLs. Callers bind those URLs on <img>. Repository SVG must never be parsed
 * or assigned through innerHTML.
 */
export class SvgObjectUrls {
  private readonly urls = new Map<string, string>();
  private readonly loading = new Set<string>();
  private dead = false;
  private generation = 0;

  constructor(
    private http: HttpClient,
    private destroyed$: Observable<void>,
    private changed?: () => void
  ) { }

  src(path: string | null | undefined): string | null {
    if (!path) {
      return null;
    }
    return this.urls.get(path) ?? null;
  }

  isLoading(path: string | null | undefined): boolean {
    return !!path && this.loading.has(path);
  }

  fetch(path: string | null | undefined, url: string): void {
    if (!path || this.dead || this.urls.has(path) || this.loading.has(path)) {
      return;
    }
    const generation = this.generation;
    this.loading.add(path);
    this.pull(url, 0).pipe(takeUntil(this.destroyed$)).subscribe({
      next: blob => {
        this.loading.delete(path);
        if (this.dead || generation !== this.generation) {
          return;
        }
        this.urls.set(path, URL.createObjectURL(blob));
        this.changed?.();
      },
      error: () => {
        this.loading.delete(path);
        this.changed?.();
      }
    });
  }

  /** Drops cached images when the viewer switches revision or diff. */
  clear(): void {
    this.generation += 1;
    this.loading.clear();
    for (const url of this.urls.values()) {
      URL.revokeObjectURL(url);
    }
    this.urls.clear();
    this.changed?.();
  }

  revoke(): void {
    this.dead = true;
    for (const url of this.urls.values()) {
      URL.revokeObjectURL(url);
    }
    this.urls.clear();
    this.loading.clear();
  }

  private pull(url: string, attempt: number): Observable<Blob> {
    return this.http.get(url, { observe: 'response', responseType: 'blob' }).pipe(
      switchMap((response: HttpResponse<Blob>) => {
        if (response.status === 202) {
          if (attempt >= 60) {
            return throwError(() => new Error('The image is still being rendered.'));
          }
          return timer(2000).pipe(switchMap(() => this.pull(url, attempt + 1)));
        }
        if (!response.body) {
          return throwError(() => new Error('Empty image response.'));
        }
        return of(response.body);
      })
    );
  }
}
