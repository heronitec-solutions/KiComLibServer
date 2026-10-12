import { Component, Input, OnChanges, SimpleChanges, ChangeDetectionStrategy } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { HttpErrorResponse } from '@angular/common/http';

import { apiErrorMessage } from '../../../../shared/api-error';
import { bomStatusClass, bomStatusLabel } from '../../labels';
import { ProjectService } from '../../services/project.service';
import { BomGroup, BomLine, BomStatus } from '../../types/project';

@Component({
    selector: 'bom-table',
    templateUrl: './bom-table.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class BomTableComponent implements OnChanges {
  @Input() projectId = 0;
  @Input() sha = '';

  readonly columns = ['reference', 'value', 'footprint', 'mpn', 'quantity', 'status', 'part'];
  readonly statusLabel = bomStatusLabel;
  readonly statusClass = bomStatusClass;

  lines: BomLine[] = [];
  group: BomGroup = 'part';
  problemsOnly = false;
  loading = false;
  error = '';

  constructor(
    private projects: ProjectService,
    private dialog: MatDialog
  ) { }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['projectId'] || changes['sha']) && this.projectId && this.sha) {
      this.load();
    }
  }

  onGroup(group: BomGroup): void {
    this.group = group;
    this.load();
  }

  get visibleLines(): BomLine[] {
    if (!this.problemsOnly) {
      return this.lines;
    }
    return this.lines.filter(line => this.isProblem(line));
  }

  statuses(line: BomLine): BomStatus[] {
    const listed = Array.isArray(line.status) ? line.status : [line.status];
    const unique = listed.filter((status, index) => listed.indexOf(status) === index);
    if (line.dnp && !unique.includes('dnp')) {
      unique.push('dnp');
    }
    return unique;
  }

  openPart(line: BomLine): void {
    if (!line.partId) {
      return;
    }
    const partId = line.partId;
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

  load(): void {
    if (!this.projectId || !this.sha) {
      return;
    }
    this.loading = true;
    this.error = '';
    this.projects.bom(this.projectId, this.sha, this.group).subscribe({
      next: lines => {
        this.lines = lines;
        this.loading = false;
      },
      error: (error: HttpErrorResponse) => {
        this.lines = [];
        this.loading = false;
        this.error = apiErrorMessage(error, 'Could not load the bill of materials.');
      }
    });
  }

  private isProblem(line: BomLine): boolean {
    return this.statuses(line).some(status => status !== 'ok');
  }
}
