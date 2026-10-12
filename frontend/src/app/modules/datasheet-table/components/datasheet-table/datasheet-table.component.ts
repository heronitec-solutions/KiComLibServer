import { AfterViewInit, Component, OnDestroy, OnInit, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import { MatDialog } from '@angular/material/dialog';
import { Subscription } from 'rxjs';

import { Datasheet } from '../../types/datasheet';
import { DatasheetDataSource } from '../../services/datasheet.dataSource';
import { DatasheetService } from '../../services/datasheet.service';
import { DirectoryFilter, EMPTY_DIRECTORY_FILTER, uniqueSorted } from '../../../../shared/search-filter';
import {
  RevisionHistoryDialogComponent,
  RevisionHistoryDialogData
} from '../../../history/components/revision-history-dialog/revision-history-dialog.component';

@Component({
    selector: 'datasheet-table',
    templateUrl: './datasheet-table.component.html',
    styleUrl: './datasheet-table.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class DatasheetTableComponent implements OnInit, AfterViewInit, OnDestroy {
  displayedColumns: string[] = ['id', 'filename', 'directory', 'fileSize', 'hash', 'actions'];
  dataSource = new DatasheetDataSource(this.datasheetService);
  sort: Sort = { active: 'id', direction: 'asc' };
  filter: DirectoryFilter = { ...EMPTY_DIRECTORY_FILTER };
  directories: string[] = [];

  private rowsSubscription?: Subscription;

  @ViewChild(MatPaginator, {static: false}) paginator!: MatPaginator;

  constructor(
    private datasheetService: DatasheetService,
    public dialog: MatDialog
  ) {
    this.dataSource.loadDatasheets(this.sort);
  }

  ngOnInit(): void {
    this.rowsSubscription = this.dataSource.allRows$.subscribe(rows => {
      this.directories = uniqueSorted(rows.map(row => row.directory));
    });
  }

  ngAfterViewInit() {
    this.dataSource.paginator = this.paginator;
  }

  ngOnDestroy(): void {
    this.rowsSubscription?.unsubscribe();
  }

  sortData(sort: Sort) {
    this.sort = sort;
    this.dataSource.loadDatasheets(sort);
  }

  get hasActiveFilters(): boolean {
    return !!this.filter.search.trim() || this.filter.directory !== 'all';
  }

  onSearchChange(search: string) {
    this.applyFilter({ ...this.filter, search });
  }

  onDirectoryChange(directory: string) {
    this.applyFilter({ ...this.filter, directory });
  }

  clearFilters() {
    this.applyFilter({ ...EMPTY_DIRECTORY_FILTER });
  }

  private applyFilter(filter: DirectoryFilter) {
    this.filter = filter;
    this.dataSource.setFilter(filter);
  }

  shortHash(row: Datasheet): string {
    const hash = row.hash || row.fileHash || '';
    return hash ? `${hash.slice(0, 12)}…` : '—';
  }

  fileUrl(row: Datasheet, download = false): string | null {
    return row.id ? this.datasheetService.fileUrl(row.id, download) : null;
  }

  openHistory(row: Datasheet, event?: Event): void {
    event?.stopPropagation();
    const data: RevisionHistoryDialogData = {
      entity: 'datasheet',
      id: row.id,
      name: row.filename
    };
    this.dialog.open(RevisionHistoryDialogComponent, {
      width: '860px',
      maxWidth: '95vw',
      data
    }).afterClosed().subscribe(changed => {
      if (changed) {
        this.dataSource.loadDatasheets(this.sort);
      }
    });
  }
}
