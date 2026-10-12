import { AfterViewInit, Component, OnDestroy, OnInit, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { Router } from '@angular/router';
import { MatPaginator } from '@angular/material/paginator';

import { apiErrorMessage } from '../../../../shared/api-error';
import { isRowControlEvent } from '../../../../shared/row-click';
import { projectStatusClass, projectStatusLabel } from '../../labels';
import { ProjectDataSource } from '../../services/project.dataSource';
import { ProjectService } from '../../services/project.service';
import { ProjectSummary } from '../../types/project';

@Component({
    selector: 'project-table',
    templateUrl: './project-table.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ProjectTableComponent implements OnInit, AfterViewInit, OnDestroy {
  readonly displayedColumns = ['name', 'status', 'customer', 'owner', 'revisions', 'components', 'warnings'];
  readonly statusLabel = projectStatusLabel;
  readonly statusClass = projectStatusClass;

  dataSource: ProjectDataSource;
  status = 'all';
  query = '';
  error = '';

  @ViewChild(MatPaginator) paginator?: MatPaginator;

  private queryTimer?: ReturnType<typeof setTimeout>;

  constructor(
    projects: ProjectService,
    private router: Router
  ) {
    this.dataSource = new ProjectDataSource(projects);
  }

  ngOnInit(): void {
    this.reload();
  }

  ngAfterViewInit(): void {
    this.dataSource.paginator = this.paginator;
  }

  ngOnDestroy(): void {
    clearTimeout(this.queryTimer);
  }

  get hasActiveFilters(): boolean {
    return this.status !== 'all' || !!this.query.trim();
  }

  onStatus(status: string): void {
    this.status = status;
    this.resetPage();
    this.reload();
  }

  onQuery(value: string): void {
    this.query = value;
    clearTimeout(this.queryTimer);
    this.queryTimer = setTimeout(() => {
      this.resetPage();
      this.reload();
    }, 300);
  }

  clearFilters(): void {
    this.status = 'all';
    this.query = '';
    this.resetPage();
    this.reload();
  }

  onRowClick(row: ProjectSummary, event: Event): void {
    if (isRowControlEvent(event)) {
      return;
    }
    this.open(row);
  }

  open(row: ProjectSummary): void {
    void this.router.navigate(['/projects', row.id]);
  }

  private reload(): void {
    this.error = '';
    this.dataSource.load(this.status, this.query, message => {
      this.error = message || apiErrorMessage(null, 'Could not load projects.');
    });
  }

  private resetPage(): void {
    if (this.paginator) {
      this.paginator.pageIndex = 0;
    }
  }
}
