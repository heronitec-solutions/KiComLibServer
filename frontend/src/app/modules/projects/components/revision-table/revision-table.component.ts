import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, ChangeDetectionStrategy } from '@angular/core';
import { PageEvent } from '@angular/material/paginator';

import { apiErrorMessage } from '../../../../shared/api-error';
import { isRowControlEvent } from '../../../../shared/row-click';
import { analysisStatusLabel, shortSha } from '../../labels';
import { ProjectService } from '../../services/project.service';
import { ProjectRevision } from '../../types/project';

@Component({
    selector: 'revision-table',
    templateUrl: './revision-table.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class RevisionTableComponent implements OnChanges {
  @Input() projectId = 0;
  @Input() canWrite = false;
  @Input() selectedSha = '';
  @Output() selected = new EventEmitter<ProjectRevision>();

  readonly columns = ['sha', 'message', 'author', 'date', 'refs', 'analysis', 'counts', 'actions'];
  readonly analysisLabel = analysisStatusLabel;
  readonly shortSha = shortSha;

  rows: ProjectRevision[] = [];
  total = 0;
  pageSize = 25;
  pageIndex = 0;
  refFilter = '';
  includeUnreachable = false;
  loading = false;
  error = '';
  busySha = '';

  constructor(private projects: ProjectService) { }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['projectId'] && this.projectId) {
      this.pageIndex = 0;
      this.load();
    }
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.load();
  }

  applyFilter(): void {
    this.pageIndex = 0;
    this.load();
  }

  load(): void {
    if (!this.projectId) {
      return;
    }
    this.loading = true;
    this.error = '';
    this.projects.revisions(this.projectId, {
      limit: this.pageSize,
      offset: this.pageIndex * this.pageSize,
      ref: this.refFilter,
      includeUnreachable: this.includeUnreachable
    }).subscribe({
      next: page => {
        this.rows = page.items;
        this.total = page.total;
        this.loading = false;
      },
      error: error => {
        this.rows = [];
        this.total = 0;
        this.error = apiErrorMessage(error, 'Could not load revisions.');
        this.loading = false;
      }
    });
  }

  isSelected(row: ProjectRevision): boolean {
    return !!this.selectedSha && row.commitSha === this.selectedSha;
  }

  onRowClick(row: ProjectRevision, event: Event): void {
    if (isRowControlEvent(event)) {
      return;
    }
    this.use(row);
  }

  use(row: ProjectRevision): void {
    this.selected.emit(row);
  }

  analyze(row: ProjectRevision): void {
    this.rerun(row, 'analyze');
  }

  rematch(row: ProjectRevision): void {
    this.rerun(row, 'rematch');
  }

  private rerun(row: ProjectRevision, action: 'analyze' | 'rematch'): void {
    this.busySha = row.commitSha;
    const request = action === 'analyze'
      ? this.projects.analyze(this.projectId, row.commitSha)
      : this.projects.rematch(this.projectId, row.commitSha);
    request.subscribe({
      next: () => {
        this.busySha = '';
        this.load();
      },
      error: error => {
        this.busySha = '';
        this.error = apiErrorMessage(error, 'Could not queue the revision job.');
      }
    });
  }
}
