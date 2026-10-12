import { Component, Inject, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { historyError, HistoryService } from '../../services/history.service';
import { DiffResult, RevisionInfo, VersionedEntity } from '../../types/commit';
import { AuthService } from '../../../../services/auth.service';

export interface RevisionHistoryDialogData {
  entity: VersionedEntity | string;
  id: number;
  name?: string;
}

@Component({
    selector: 'revision-history-dialog',
    templateUrl: './revision-history-dialog.component.html',
    styleUrl: './revision-history-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class RevisionHistoryDialogComponent implements OnInit {
  revisions: RevisionInfo[] = [];
  isLoading = true;
  loadError = '';

  fromRev: number | null = null;
  toRev: number | null = null;
  diff: DiffResult | null = null;
  isLoadingDiff = false;
  diffError = '';
  actionError = '';
  actionMessage = '';
  isRestoring = false;
  private changed = false;

  readonly revisionColumns = ['revisionNo', 'message', 'author', 'createdAt', 'actions'];

  constructor(
    public dialogRef: MatDialogRef<RevisionHistoryDialogComponent>,
    private historyService: HistoryService,
    public auth: AuthService,
    @Inject(MAT_DIALOG_DATA) public data: RevisionHistoryDialogData
  ) { }

  ngOnInit(): void {
    this.historyService.getHistory(this.data.entity, this.data.id).subscribe({
      next: revisions => {
        this.revisions = revisions;
        this.isLoading = false;
        if (revisions.length) {
          const latest = revisions[revisions.length - 1];
          const previous = revisions.length > 1 ? revisions[revisions.length - 2] : latest;
          this.fromRev = previous.revisionNo;
          this.toRev = latest.revisionNo;
          if (revisions.length >= 1) {
            this.loadDiff();
          }
        }
      },
      error: () => {
        this.loadError = 'Could not load revision history.';
        this.isLoading = false;
      }
    });
  }

  close(): void {
    this.dialogRef.close(this.changed);
  }

  isCurrent(rev: RevisionInfo): boolean {
    if (!this.revisions.length) {
      return false;
    }
    const latest = this.revisions[this.revisions.length - 1];
    return rev.revisionNo === latest.revisionNo;
  }

  restore(rev: RevisionInfo): void {
    if (this.isCurrent(rev) || this.isRestoring) {
      return;
    }
    const label = this.data.name ? `'${this.data.name}'` : `${this.data.entity} #${this.data.id}`;
    if (!confirm(`Restore ${label} to revision ${rev.revisionNo}? The current data is kept in history and replaced by this revision.`)) {
      return;
    }

    this.isRestoring = true;
    this.actionError = '';
    this.actionMessage = '';
    this.historyService.restoreRevision(this.data.entity, this.data.id, rev.revisionNo).subscribe({
      next: result => {
        this.changed = true;
        this.actionMessage = `Restored as commit #${result.commitId}.`;
        this.reload();
      },
      error: err => {
        this.actionError = historyError(err, 'Could not restore this revision.');
        this.isRestoring = false;
      }
    });
  }

  private reload(): void {
    this.historyService.getHistory(this.data.entity, this.data.id).subscribe({
      next: revisions => {
        this.revisions = revisions;
        this.isRestoring = false;
        if (revisions.length) {
          const latest = revisions[revisions.length - 1];
          const previous = revisions.length > 1 ? revisions[revisions.length - 2] : latest;
          this.fromRev = previous.revisionNo;
          this.toRev = latest.revisionNo;
          this.loadDiff();
        }
      },
      error: () => {
        this.isRestoring = false;
        this.actionError = 'Restored, but the history could not be reloaded.';
      }
    });
  }

  loadDiff(): void {
    if (this.fromRev == null || this.toRev == null) {
      return;
    }

    this.isLoadingDiff = true;
    this.diffError = '';
    this.diff = null;

    this.historyService.getDiff(this.data.entity, this.data.id, this.fromRev, this.toRev).subscribe({
      next: diff => {
        this.diff = diff;
        this.isLoadingDiff = false;
      },
      error: () => {
        this.diffError = 'Could not load diff for the selected revisions.';
        this.isLoadingDiff = false;
      }
    });
  }
}
