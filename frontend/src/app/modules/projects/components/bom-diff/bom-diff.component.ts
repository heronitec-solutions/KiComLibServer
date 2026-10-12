import { Component, Input, OnChanges, SimpleChanges, ChangeDetectionStrategy } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';

import { apiErrorMessage } from '../../../../shared/api-error';
import { bomStatusClass, bomStatusLabel, changeGroupLabel, revisionOptionLabel, shortSha } from '../../labels';
import { ProjectService } from '../../services/project.service';
import { BomDiff, BomDiffItem, ProjectRevision } from '../../types/project';

@Component({
    selector: 'bom-diff',
    templateUrl: './bom-diff.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class BomDiffComponent implements OnChanges {
  @Input() projectId = 0;

  readonly groups = ['added', 'removed', 'replaced', 'changed', 'renamed'] as const;
  readonly groupLabel = changeGroupLabel;
  readonly statusLabel = bomStatusLabel;
  readonly statusClass = bomStatusClass;
  readonly shortSha = shortSha;
  readonly revisionLabel = revisionOptionLabel;

  revisions: ProjectRevision[] = [];
  baseSha = '';
  targetSha = '';
  diff: BomDiff | null = null;
  loading = false;
  error = '';

  constructor(private projects: ProjectService) { }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['projectId'] && this.projectId) {
      this.projects.revisions(this.projectId, { limit: 200, offset: 0 }).subscribe({
        next: page => {
          this.revisions = page.items;
          if (!this.targetSha && page.items.length) {
            this.targetSha = page.items[0].commitSha;
          }
          if (!this.baseSha && page.items.length > 1) {
            this.baseSha = page.items[1].commitSha;
          }
        },
        error: error => {
          this.error = apiErrorMessage(error, 'Could not load revisions for the diff.');
        }
      });
    }
  }

  items(group: typeof this.groups[number]): BomDiffItem[] {
    return this.diff ? this.diff[group] : [];
  }

  changeText(item: BomDiffItem): string {
    return Object.keys(item.changes).map(field => {
      const pair = item.changes[field] ?? [];
      return `${field}: ${pair[0] ?? ''} → ${pair[1] ?? ''}`;
    }).join('; ');
  }

  load(): void {
    if (!this.projectId || !this.baseSha || !this.targetSha) {
      this.error = 'Choose a base revision and a target revision.';
      return;
    }
    this.loading = true;
    this.error = '';
    this.projects.bomDiff(this.projectId, this.baseSha, this.targetSha).subscribe({
      next: diff => {
        this.diff = diff;
        this.loading = false;
      },
      error: (error: HttpErrorResponse) => {
        this.diff = null;
        this.loading = false;
        this.error = apiErrorMessage(error, 'Could not load the bill of materials diff.');
      }
    });
  }
}
