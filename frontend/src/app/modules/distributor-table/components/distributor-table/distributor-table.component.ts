import { AfterViewInit, Component, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { Distributor } from '../../types/distributor';
import { DistributorFilter, EMPTY_DISTRIBUTOR_FILTER } from '../../types/distributor-filter';
import { YesNoFilter } from '../../../../shared/search-filter';
import { DistributorDataSource } from '../../services/distributor.dataSource';
import { DistributorService } from '../../services/distributor.service';
import { AuthService } from '../../../../services/auth.service';
import { MatPaginator } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import { DeleteConfirmationDialogComponent } from '../../../../components/delete-confirmation-dialog/delete-confirmation-dialog.component';
import { MatDialog } from '@angular/material/dialog';
import { DistributorEditDialogComponent } from '../distributor-edit-dialog/distributor-edit-dialog.component';
import { isRowControlEvent } from '../../../../shared/row-click';
import {
  RevisionHistoryDialogComponent,
  RevisionHistoryDialogData
} from '../../../history/components/revision-history-dialog/revision-history-dialog.component';

@Component({
    selector: 'distributor-table',
    templateUrl: './distributor-table.component.html',
    styleUrl: './distributor-table.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class DistributorTableComponent implements AfterViewInit {
  displayedColumns: string[] = ['id', 'isFavorite', 'name', 'url', 'trustedPartsId', 'actions'];
  dataSource = new DistributorDataSource(this.distributorService);
  sort: Sort = { active: 'id', direction: 'asc' };
  filter: DistributorFilter = { ...EMPTY_DISTRIBUTOR_FILTER };
  private favoritePending = new Set<number>();

  @ViewChild(MatPaginator, {static: false}) paginator!: MatPaginator;
  //@ViewChild(MatSort, {static: false}) sort: MatSort;

  constructor(
    private distributorService: DistributorService,
    //private changeDetectorRefs: ChangeDetectorRef,
    public dialog: MatDialog,
    public auth: AuthService
  ) {
    // Assign the data to the data source for the table to render
    this.dataSource.loadDistributors(this.sort);
    //this.dataSource = new MatTableDataSource();
  }

  ngAfterViewInit() {

    this.dataSource.paginator = this.paginator;
    //this.dataSource.sort = this.sort;
  }

  refresh() {

  }

  openHistory(row: Distributor, event?: Event): void {
    event?.stopPropagation();
    const data: RevisionHistoryDialogData = {
      entity: 'distributor',
      id: row.id,
      name: row.name
    };
    this.dialog.open(RevisionHistoryDialogComponent, {
      width: '860px',
      maxWidth: '95vw',
      data
    }).afterClosed().subscribe(changed => {
      if (changed) {
        this.dataSource.loadDistributors(this.sort);
      }
    });
  }

  sortData(sort: Sort) {
    this.sort = sort;
    this.dataSource.loadDistributors(sort);
  }

  get hasActiveFilters(): boolean {
    return !!this.filter.search.trim() || this.filter.favorite !== 'all';
  }

  onSearchChange(search: string) {
    this.applyFilter({ ...this.filter, search });
  }

  onFavoriteChange(favorite: YesNoFilter) {
    this.applyFilter({ ...this.filter, favorite });
  }

  clearFilters() {
    this.applyFilter({ ...EMPTY_DISTRIBUTOR_FILTER });
  }

  private applyFilter(filter: DistributorFilter) {
    this.filter = filter;
    this.dataSource.setFilter(filter);
  }

  isFavoritePending(row: Distributor): boolean {
    return this.favoritePending.has(row.id);
  }

  toggleFavorite(row: Distributor, event?: Event): void {
    event?.stopPropagation();
    if (!this.auth.canWrite() || this.favoritePending.has(row.id)) {
      return;
    }

    const previous = row.isFavorite;
    row.isFavorite = !previous;
    this.favoritePending.add(row.id);
    this.dataSource.refreshView();

    this.distributorService.updateData(row.id, row).subscribe({
      next: () => this.favoritePending.delete(row.id),
      error: (error) => {
        console.error('Error:', error);
        row.isFavorite = previous;
        this.favoritePending.delete(row.id);
        this.dataSource.refreshView();
      }
    });
  }

  // applyFilter(event: Event) {
  //   const filterValue = (event.target as HTMLInputElement).value;
  //   this.dataSource.filter = filterValue.trim().toLowerCase();

  //   if (this.dataSource.paginator) {
  //     this.dataSource.paginator.firstPage();
  //   }
  // }

  onRowClick(row: Distributor, event: Event): void {
    if (isRowControlEvent(event)) {
      return;
    }
    this.editRow(row);
  }

  editRow(row: any) {
    const dialogRef = this.dialog.open(DistributorEditDialogComponent, {
      width: '50%',
      data: {
        id: +row.id
      }
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.dataSource.loadDistributors(this.sort);
        //this.changeDetectorRefs.detectChanges();
      }
    });
  }

  deleteRow(row: any) {
    const dialogRef = this.dialog.open(DeleteConfirmationDialogComponent, {
      width: '50%'
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.distributorService.deleteData(row.id).subscribe({
          next: (response) => console.log(response),
          error: (error) => console.error('Error:', error)
        });
        this.dataSource.loadDistributors(this.sort);
      }
    });
  }

  addEntry() {
    const dialogRef = this.dialog.open(DistributorEditDialogComponent, {
      width: '50%',
      data: {
        id: -1
      }
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.dataSource.loadDistributors(this.sort);
      }
    });
  }
}
