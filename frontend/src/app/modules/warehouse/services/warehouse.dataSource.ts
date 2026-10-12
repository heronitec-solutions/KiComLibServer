import { Injectable } from '@angular/core';
import { Sort } from '@angular/material/sort';
import { BehaviorSubject } from 'rxjs';

import { PagedDataSource } from '../../../shared/paged-data-source';
import { apiErrorMessage } from '../../../shared/api-error';
import { matchesText } from '../../../shared/search-filter';
import { WarehouseService } from './warehouse.service';
import { EMPTY_WAREHOUSE_FILTER, WarehouseFilter, WarehouseItem } from '../types/warehouse';

@Injectable()
export class WarehouseDataSource extends PagedDataSource<WarehouseItem> {
  readonly loadError$ = new BehaviorSubject<string>('');

  private filter: WarehouseFilter = { ...EMPTY_WAREHOUSE_FILTER };

  constructor(private warehouse: WarehouseService) {
    super();
  }

  loadItems(sort: Sort): void {
    this.isLoading$.next(true);
    this.loadError$.next('');
    this.warehouse.list(sort).subscribe({
      next: items => {
        this.setRows(items);
        this.isLoading$.next(false);
      },
      error: error => {
        this.setRows([]);
        this.loadError$.next(apiErrorMessage(error, 'Could not load the warehouse'));
        this.isLoading$.next(false);
      }
    });
  }

  setFilter(filter: WarehouseFilter): void {
    this.filter = filter;
    this.refresh({ resetPage: true });
  }

  protected override applyFilter(rows: WarehouseItem[]): WarehouseItem[] {
    return rows.filter(row => this.matchesStatus(row) && matchesText(this.filter.search, [
      row.name,
      row.value,
      row.comment,
      row.locations,
      row.partId ? 'catalog' : 'manual',
      ...row.batches.flatMap(batch => [batch.name, batch.location])
    ]));
  }

  private matchesStatus(row: WarehouseItem): boolean {
    switch (this.filter.status) {
      case 'stock':
        return row.quantity > 0;
      case 'empty':
        return row.quantity === 0;
      case 'ordered':
        return row.batches.some(batch => batch.name.toLowerCase() === 'ordered');
      default:
        return true;
    }
  }
}
