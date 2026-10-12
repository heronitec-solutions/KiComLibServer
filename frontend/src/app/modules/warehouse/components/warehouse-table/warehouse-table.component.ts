import { AfterViewInit, Component, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatPaginator } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';

import { AuthService } from '../../../../services/auth.service';
import { DeleteConfirmationDialogComponent } from '../../../../components/delete-confirmation-dialog/delete-confirmation-dialog.component';
import { WarehouseDataSource } from '../../services/warehouse.dataSource';
import { WarehouseService } from '../../services/warehouse.service';
import { EMPTY_WAREHOUSE_FILTER, WarehouseFilter, WarehouseItem, WarehouseStatusFilter } from '../../types/warehouse';
import { WarehouseEditDialogComponent } from '../warehouse-edit-dialog/warehouse-edit-dialog.component';
import { WarehouseViewDialogComponent } from '../warehouse-view-dialog/warehouse-view-dialog.component';
import { isRowControlEvent } from '../../../../shared/row-click';

@Component({
    selector: 'warehouse-table',
    templateUrl: './warehouse-table.component.html',
    styleUrl: './warehouse-table.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class WarehouseTableComponent implements AfterViewInit {
  displayedColumns: string[] = ['image', 'name', 'source', 'value', 'quantity', 'location', 'batches', 'actions'];
  dataSource = new WarehouseDataSource(this.warehouse);
  sort: Sort = { active: 'name', direction: 'asc' };
  filter: WarehouseFilter = { ...EMPTY_WAREHOUSE_FILTER };
  actionError = '';

  @ViewChild(MatPaginator, { static: false }) paginator!: MatPaginator;

  constructor(
    private warehouse: WarehouseService,
    public dialog: MatDialog,
    public auth: AuthService
  ) {
    this.dataSource.loadItems(this.sort);
  }

  ngAfterViewInit(): void {
    this.dataSource.paginator = this.paginator;
  }

  imageUrl(row: WarehouseItem): string {
    return row.imageHash
      ? this.warehouse.imageContentUrl(row.imageHash)
      : this.warehouse.imageUrl(row.id);
  }

  get hasActiveFilters(): boolean {
    return !!this.filter.search.trim() || this.filter.status !== 'all';
  }

  sortData(sort: Sort): void {
    this.sort = sort;
    this.dataSource.loadItems(sort);
  }

  onSearchChange(search: string): void {
    this.applyFilter({ ...this.filter, search });
  }

  onStatusChange(status: WarehouseStatusFilter): void {
    this.applyFilter({ ...this.filter, status });
  }

  clearFilters(): void {
    this.applyFilter({ ...EMPTY_WAREHOUSE_FILTER });
  }

  openPart(row: WarehouseItem): void {
    if (!row.partId || !this.auth.canRead()) {
      return;
    }

    const partId = row.partId;
    void import('../../../part-table/part-table.module').then(module => {
      this.dialog.open(module.PartDetailDialogComponent, {
        width: '70%',
        maxWidth: '72rem',
        data: {
          id: partId,
          manufacturerList: [],
          distributorList: [],
          datasheetList: [],
          symbolList: [],
          footprintList: [],
          libraryList: []
        }
      });
    });
  }

  addEntry(): void {
    this.openEditor(null);
  }

  onRowClick(row: WarehouseItem, event: Event): void {
    if (isRowControlEvent(event)) {
      return;
    }
    this.viewRow(row);
  }

  viewRow(row: WarehouseItem): void {
    this.dialog.open(WarehouseViewDialogComponent, {
      width: '640px',
      maxWidth: '95vw',
      data: { item: row, takenPartIds: this.takenPartIds(row) }
    }).afterClosed().subscribe(changed => {
      if (changed) {
        this.actionError = '';
        this.dataSource.loadItems(this.sort);
      }
    });
  }

  editRow(row: WarehouseItem): void {
    if (!this.auth.canWriteWarehouse()) {
      this.viewRow(row);
      return;
    }
    this.openEditor(row);
  }

  deleteRow(row: WarehouseItem): void {
    this.dialog.open(DeleteConfirmationDialogComponent, { width: '28rem' }).afterClosed().subscribe(confirmed => {
      if (!confirmed) {
        return;
      }

      this.actionError = '';
      this.warehouse.delete(row.id).subscribe({
        next: () => this.dataSource.loadItems(this.sort),
        error: () => this.actionError = 'Could not delete the warehouse item'
      });
    });
  }

  private takenPartIds(item: WarehouseItem | null): number[] {
    return (this.dataSource.allRows$.value ?? [])
      .map(row => row.partId)
      .filter((id): id is number => id != null && id !== item?.partId);
  }

  private openEditor(item: WarehouseItem | null): void {
    const takenPartIds = this.takenPartIds(item);

    this.dialog.open(WarehouseEditDialogComponent, {
      width: '720px',
      maxWidth: '95vw',
      data: { item, takenPartIds }
    }).afterClosed().subscribe(saved => {
      if (saved) {
        this.actionError = '';
        this.dataSource.loadItems(this.sort);
      }
    });
  }

  private applyFilter(filter: WarehouseFilter): void {
    this.filter = filter;
    this.dataSource.setFilter(filter);
  }
}
