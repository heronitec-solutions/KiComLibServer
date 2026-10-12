import { AfterViewInit, Component, OnDestroy, OnInit, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import { MatDialog } from '@angular/material/dialog';
import { Subscription } from 'rxjs';

import { CadModel } from '../../types/cad-model';
import { CadModelDataSource } from '../../services/cad-model.dataSource';
import { CadModelService } from '../../services/cad-model.service';
import { appEnvironment } from '../../../../app-environment';
import { DirectoryFilter, EMPTY_DIRECTORY_FILTER, uniqueSorted } from '../../../../shared/search-filter';
import {
  RevisionHistoryDialogComponent,
  RevisionHistoryDialogData
} from '../../../history/components/revision-history-dialog/revision-history-dialog.component';

@Component({
    selector: 'cad-model-table',
    templateUrl: './cad-model-table.component.html',
    styleUrl: './cad-model-table.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class CadModelTableComponent implements OnInit, AfterViewInit, OnDestroy {
  displayedColumns: string[] = ['id', 'filename', 'directory', 'fileSize', 'hash', 'actions'];
  dataSource = new CadModelDataSource(this.cadModelService);
  sort: Sort = { active: 'id', direction: 'asc' };
  filter: DirectoryFilter = { ...EMPTY_DIRECTORY_FILTER };
  directories: string[] = [];

  private rowsSubscription?: Subscription;

  @ViewChild(MatPaginator, {static: false}) paginator!: MatPaginator;

  constructor(
    private cadModelService: CadModelService,
    public dialog: MatDialog
  ) {
    this.dataSource.loadCadModels(this.sort);
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
    this.dataSource.loadCadModels(sort);
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

  shortHash(row: CadModel): string {
    const hash = row.hash || row.fileHash || '';
    return hash ? `${hash.slice(0, 12)}…` : '—';
  }

  blobUrl(row: CadModel): string | null {
    const hash = row.hash || row.fileHash;
    return hash ? `${appEnvironment.apiUrl}/blob/${hash}` : null;
  }

  openHistory(row: CadModel, event?: Event): void {
    event?.stopPropagation();
    const data: RevisionHistoryDialogData = {
      entity: 'model3d',
      id: row.id,
      name: row.filename
    };
    this.dialog.open(RevisionHistoryDialogComponent, {
      width: '860px',
      maxWidth: '95vw',
      data
    }).afterClosed().subscribe(changed => {
      if (changed) {
        this.dataSource.loadCadModels(this.sort);
      }
    });
  }
}
