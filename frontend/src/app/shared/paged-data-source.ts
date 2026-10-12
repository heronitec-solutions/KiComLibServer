import { DataSource } from '@angular/cdk/collections';
import { MatPaginator } from '@angular/material/paginator';
import { BehaviorSubject, Observable, Subscription } from 'rxjs';

/**
 * Table data source that holds the full server result in memory and hands the
 * table only the rows belonging to the current page.
 *
 * Subclasses fetch their rows and pass them to `setRows`, and may narrow the
 * result by overriding `applyFilter`.
 */
export abstract class PagedDataSource<T> extends DataSource<T> {
  /** Rows of the current page — what the table renders. */
  readonly rows$ = new BehaviorSubject<T[]>([]);
  /** Every row from the last load, before filtering and paging. */
  readonly allRows$ = new BehaviorSubject<T[]>([]);
  /** Number of rows left after filtering, across all pages. */
  readonly filteredCount$ = new BehaviorSubject<number>(0);
  readonly isLoading$ = new BehaviorSubject<boolean>(false);

  private pageSubscription?: Subscription;
  private attachedPaginator?: MatPaginator;

  get paginator(): MatPaginator | undefined {
    return this.attachedPaginator;
  }

  /** Assigned by the table component once its view has initialised. */
  set paginator(paginator: MatPaginator | undefined) {
    this.pageSubscription?.unsubscribe();
    this.attachedPaginator = paginator;
    this.pageSubscription = paginator?.page.subscribe(() => this.emitPage());
    this.emitPage();
  }

  connect(): Observable<T[]> {
    return this.rows$.asObservable();
  }

  disconnect(): void {
    this.pageSubscription?.unsubscribe();
    this.rows$.complete();
    this.allRows$.complete();
    this.filteredCount$.complete();
    this.isLoading$.complete();
  }

  /** Narrowing applied before paging. The default keeps every row. */
  protected applyFilter(rows: T[]): T[] {
    return rows;
  }

  /** Replaces the full row set, keeping the reader on their current page. */
  protected setRows(rows: T[]): void {
    this.allRows$.next(rows);
    this.emitPage();
  }

  /**
   * Re-runs filtering and paging over the rows already loaded. Filter changes
   * should reset to the first page, since the current page may no longer exist.
   */
  protected refresh(options: { resetPage?: boolean } = {}): void {
    if (options.resetPage && this.attachedPaginator) {
      this.attachedPaginator.pageIndex = 0;
    }
    this.emitPage();
  }

  private emitPage(): void {
    const filtered = this.applyFilter(this.allRows$.value);
    this.filteredCount$.next(filtered.length);

    const paginator = this.attachedPaginator;
    if (!paginator) {
      this.rows$.next(filtered);
      return;
    }

    // Clamp when the result shrank below the page the reader is on.
    const lastPageIndex = Math.max(0, Math.ceil(filtered.length / paginator.pageSize) - 1);
    const pageIndex = Math.min(paginator.pageIndex, lastPageIndex);
    const start = pageIndex * paginator.pageSize;

    this.rows$.next(filtered.slice(start, start + paginator.pageSize));

    // Deferred like MatTableDataSource does, so writing back to the paginator's
    // inputs cannot happen inside the change detection pass that read them.
    Promise.resolve().then(() => {
      if (this.attachedPaginator !== paginator) {
        return;
      }
      paginator.length = filtered.length;
      if (paginator.pageIndex !== pageIndex) {
        paginator.pageIndex = pageIndex;
      }
    });
  }
}
