import { AfterViewInit, Component, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { LibraryFile } from '../../types/library-file';
import { EMPTY_LIBRARY_FILE_FILTER, LibraryFileFilter, LibraryFileKindFilter } from '../../types/library-file-filter';
import { LibraryFileDataSource } from '../../services/library-file.dataSource';
import { LibraryFileService } from '../../services/library-file.service';
import { MatPaginator } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import { MatDialog } from '@angular/material/dialog';
import { isRowControlEvent } from '../../../../shared/row-click';
import { LibraryFileEditDialogComponent } from '../library-file-edit-dialog/library-file-edit-dialog.component';

@Component({
    selector: 'library-file-table',
    templateUrl: './library-file-table.component.html',
    styleUrl: './library-file-table.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class LibraryFileTableComponent implements AfterViewInit {
  displayedColumns: string[] = ['id', 'name', 'type', 'comment', 'filename', 'description', 'version'];
  dataSource = new LibraryFileDataSource(this.libraryFileService);
  sort: Sort = { active: 'id', direction: 'asc' };
  filter: LibraryFileFilter = { ...EMPTY_LIBRARY_FILE_FILTER };

  @ViewChild(MatPaginator, {static: false}) paginator!: MatPaginator;
  //@ViewChild(MatSort, {static: false}) sort: MatSort;

  constructor(
    private libraryFileService: LibraryFileService,
    public dialog: MatDialog
  ) {
    // Assign the data to the data source for the table to render
    this.dataSource.loadLibraryFiles(this.sort);
    //this.dataSource = new MatTableDataSource();
  }

  ngAfterViewInit() {

    this.dataSource.paginator = this.paginator;
    //this.dataSource.sort = this.sort;
  }

  sortData(sort: Sort) {
    this.sort = sort;
    this.dataSource.loadLibraryFiles(sort);
  }

  get hasActiveFilters(): boolean {
    return !!this.filter.search.trim() || this.filter.kind !== 'all';
  }

  onSearchChange(search: string) {
    this.applyFilter({ ...this.filter, search });
  }

  onKindChange(kind: LibraryFileKindFilter) {
    this.applyFilter({ ...this.filter, kind });
  }

  clearFilters() {
    this.applyFilter({ ...EMPTY_LIBRARY_FILE_FILTER });
  }

  private applyFilter(filter: LibraryFileFilter) {
    this.filter = filter;
    this.dataSource.setFilter(filter);
  }

  onRowClick(row: LibraryFile, event: Event): void {
    if (isRowControlEvent(event)) {
      return;
    }
    this.editRow(row);
  }

  editRow(row: LibraryFile): void {
    this.dialog.open(LibraryFileEditDialogComponent, {
      width: '50%',
      data: { id: row.id }
    }).afterClosed().subscribe(result => {
      if (result) {
        this.dataSource.loadLibraryFiles(this.sort);
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
}
