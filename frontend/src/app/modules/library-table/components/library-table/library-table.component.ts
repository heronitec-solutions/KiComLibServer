import { AfterViewInit, Component, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { Library } from '../../types/library';
import { EMPTY_SEARCH_FILTER, SearchFilter } from '../../../../shared/search-filter';
import { LibraryDataSource } from '../../services/library.dataSource';
import { LibraryService } from '../../services/library.service';
import { AuthService } from '../../../../services/auth.service';
import { MatPaginator } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import { DeleteConfirmationDialogComponent } from '../../../../components/delete-confirmation-dialog/delete-confirmation-dialog.component';
import { MatDialog } from '@angular/material/dialog';
import { LibraryEditDialogComponent } from '../library-edit-dialog/library-edit-dialog.component';
import { isRowControlEvent } from '../../../../shared/row-click';

@Component({
    selector: 'library-table',
    templateUrl: './library-table.component.html',
    styleUrl: './library-table.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class LibraryTableComponent implements AfterViewInit {
  displayedColumns: string[] = ['id', 'name', 'filename', 'description', 'actions'];
  dataSource = new LibraryDataSource(this.libraryService);
  sort: Sort = { active: 'id', direction: 'asc' };
  filter: SearchFilter = { ...EMPTY_SEARCH_FILTER };

  @ViewChild(MatPaginator, {static: false}) paginator!: MatPaginator;
  //@ViewChild(MatSort, {static: false}) sort: MatSort;

  constructor(
    private libraryService: LibraryService,
    //private changeDetectorRefs: ChangeDetectorRef,
    public dialog: MatDialog,
    public auth: AuthService
  ) {
    // Assign the data to the data source for the table to render
    this.dataSource.loadLibraries(this.sort);
    //this.dataSource = new MatTableDataSource();
  }

  ngAfterViewInit() {

    this.dataSource.paginator = this.paginator;
    //this.dataSource.sort = this.sort;
  }

  refresh() {

  }

  sortData(sort: Sort) {
    this.sort = sort;
    this.dataSource.loadLibraries(sort);
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

  onRowClick(row: Library, event: Event): void {
    if (isRowControlEvent(event)) {
      return;
    }
    this.editRow(row);
  }

  editRow(row: any) {
    const dialogRef = this.dialog.open(LibraryEditDialogComponent, {
      width: '50%',
      data: {
        id: +row.id
      }
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.dataSource.loadLibraries(this.sort);
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
        this.libraryService.deleteData(row.id).subscribe({
          next: (response) => console.log(response),
          error: (error) => console.error('Error:', error)
        });
        this.dataSource.loadLibraries(this.sort);
      }
    });
  }

  addEntry() {
    const dialogRef = this.dialog.open(LibraryEditDialogComponent, {
      width: '50%',
      data: {
        id: -1
      }
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.dataSource.loadLibraries(this.sort);
      }
    });
  }
}
