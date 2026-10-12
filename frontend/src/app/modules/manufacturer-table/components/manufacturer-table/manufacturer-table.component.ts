import { AfterViewInit, Component, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { Manufacturer } from '../../types/manufacturer';
import { EMPTY_SEARCH_FILTER, SearchFilter } from '../../../../shared/search-filter';
import { ManufacturerDataSource } from '../../services/manufacturer.dataSource';
import { ManufacturerService } from '../../services/manufacturer.service';
import { AuthService } from '../../../../services/auth.service';
import { MatPaginator } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import { DeleteConfirmationDialogComponent } from '../../../../components/delete-confirmation-dialog/delete-confirmation-dialog.component';
import { MatDialog } from '@angular/material/dialog';
import { ManufacturerEditDialogComponent } from '../manufacturer-edit-dialog/manufacturer-edit-dialog.component';
import { isRowControlEvent } from '../../../../shared/row-click';
import {
  RevisionHistoryDialogComponent,
  RevisionHistoryDialogData
} from '../../../history/components/revision-history-dialog/revision-history-dialog.component';

@Component({
    selector: 'manufacturer-table',
    templateUrl: './manufacturer-table.component.html',
    styleUrl: './manufacturer-table.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ManufacturerTableComponent implements AfterViewInit {
  displayedColumns: string[] = ['id', 'name', 'url', 'trustedPartsId', 'actions'];
  dataSource = new ManufacturerDataSource(this.manufacturerService);
  sort: Sort = { active: 'id', direction: 'asc' };
  filter: SearchFilter = { ...EMPTY_SEARCH_FILTER };

  @ViewChild(MatPaginator, {static: false}) paginator!: MatPaginator;
  //@ViewChild(MatSort, {static: false}) sort: MatSort;

  constructor(
    private manufacturerService: ManufacturerService,
    //private changeDetectorRefs: ChangeDetectorRef,
    public dialog: MatDialog,
    public auth: AuthService
  ) {
    // Assign the data to the data source for the table to render
    this.dataSource.loadManufacturers(this.sort);
    //this.dataSource = new MatTableDataSource();
  }

  ngAfterViewInit() {

    this.dataSource.paginator = this.paginator;
    //this.dataSource.sort = this.sort;
  }

  refresh() {

  }

  openHistory(row: Manufacturer, event?: Event): void {
    event?.stopPropagation();
    const data: RevisionHistoryDialogData = {
      entity: 'manufacturer',
      id: row.id,
      name: row.name
    };
    this.dialog.open(RevisionHistoryDialogComponent, {
      width: '860px',
      maxWidth: '95vw',
      data
    }).afterClosed().subscribe(changed => {
      if (changed) {
        this.dataSource.loadManufacturers(this.sort);
      }
    });
  }

  sortData(sort: Sort) {
    this.sort = sort;
    this.dataSource.loadManufacturers(sort);
  }

  get hasActiveFilters(): boolean {
    return !!this.filter.search.trim();
  }

  onSearchChange(search: string) {
    this.applyFilter({ ...this.filter, search });
  }

  clearFilters() {
    this.applyFilter({ ...EMPTY_SEARCH_FILTER });
  }

  private applyFilter(filter: SearchFilter) {
    this.filter = filter;
    this.dataSource.setFilter(filter);
  }

  // applyFilter(event: Event) {
  //   const filterValue = (event.target as HTMLInputElement).value;
  //   this.dataSource.filter = filterValue.trim().toLowerCase();

  //   if (this.dataSource.paginator) {
  //     this.dataSource.paginator.firstPage();
  //   }
  // }

  onRowClick(row: Manufacturer, event: Event): void {
    if (isRowControlEvent(event)) {
      return;
    }
    this.editRow(row);
  }

  editRow(row: any) {
    const dialogRef = this.dialog.open(ManufacturerEditDialogComponent, {
      width: '50%',
      data: {
        id: +row.id
      }
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.dataSource.loadManufacturers(this.sort);
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
        this.manufacturerService.deleteData(row.id).subscribe({
          next: (response) => console.log(response),
          error: (error) => console.error('Error:', error)
        });
        this.dataSource.loadManufacturers(this.sort);
      }
    });
  }

  addEntry() {
    const dialogRef = this.dialog.open(ManufacturerEditDialogComponent, {
      width: '50%',
      data: {
        id: -1
      }
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.dataSource.loadManufacturers(this.sort);
      }
    });
  }
}
