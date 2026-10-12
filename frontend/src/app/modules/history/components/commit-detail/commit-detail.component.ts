import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { Observable } from 'rxjs';

import { RevisionHistoryDialogComponent } from '../revision-history-dialog/revision-history-dialog.component';

import { historyError, HistoryService } from '../../services/history.service';
import {
  CommitChange,
  CommitDetail,
  DiffResult
} from '../../types/commit';
import { AuthService } from '../../../../services/auth.service';

@Component({
    selector: 'commit-detail',
    templateUrl: './commit-detail.component.html',
    styleUrl: './commit-detail.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class CommitDetailComponent implements OnInit {
  commit?: CommitDetail;
  isLoading = true;
  loadError = '';

  displayedColumns: string[] = ['entity', 'op', 'name', 'revisionNo', 'actions'];
  expandedChangeKey: string | null = null;
  expandedDiff: DiffResult | null = null;
  isLoadingDiff = false;
  isReverting = false;
  actionError = '';
  actionMessage = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private historyService: HistoryService,
    private dialog: MatDialog,
    public auth: AuthService
  ) { }

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!Number.isFinite(id) || id <= 0) {
      this.loadError = 'Invalid commit id.';
      this.isLoading = false;
      return;
    }

    this.historyService.getCommit(id).subscribe({
      next: commit => {
        this.commit = commit;
        this.isLoading = false;
      },
      error: () => {
        this.loadError = 'Commit not found.';
        this.isLoading = false;
      }
    });
  }

  back(): void {
    void this.router.navigate(['/commits']);
  }

  changeKey(change: CommitChange): string {
    return `${change.entity}:${change.id}:${change.revisionNo}`;
  }

  toggleDiff(change: CommitChange): void {
    const key = this.changeKey(change);
    if (this.expandedChangeKey === key) {
      this.expandedChangeKey = null;
      this.expandedDiff = null;
      return;
    }

    this.expandedChangeKey = key;
    this.expandedDiff = null;
    this.isLoadingDiff = true;

    const to = change.revisionNo;
    const from = Math.max(1, to - 1);

    this.historyService.getDiff(change.entity, change.id, from, to).subscribe({
      next: diff => {
        this.expandedDiff = diff;
        this.isLoadingDiff = false;
      },
      error: () => {
        this.expandedDiff = null;
        this.isLoadingDiff = false;
      }
    });
  }

  canRevertCommit(): boolean {
    return this.auth.canWrite() && !!this.commit && this.commit.author !== 'system' && this.commit.changes.length > 0;
  }

  revertCommit(): void {
    if (!this.commit || this.isReverting || !this.canRevertCommit()) {
      return;
    }
    if (!confirm(`Revert commit #${this.commit.id}? A new commit will undo every change that is still current.`)) {
      return;
    }
    this.runRevert(this.historyService.revertCommit(this.commit.id));
  }

  revertChange(change: CommitChange): void {
    if (!this.commit || this.isReverting || !change.isHead || !this.canRevertCommit()) {
      return;
    }
    if (!confirm(`Undo ${change.op} of ${change.entity} '${change.name}'? A new commit will restore the previous state.`)) {
      return;
    }
    this.runRevert(this.historyService.revertCommit(this.commit.id, 'web-ui', change.entity, change.id));
  }

  openHistory(change: CommitChange, event?: Event): void {
    event?.stopPropagation();
    this.dialog.open(RevisionHistoryDialogComponent, {
      width: '860px',
      maxWidth: '95vw',
      data: {
        entity: change.entity,
        id: change.id,
        name: change.name
      }
    }).afterClosed().subscribe(changed => {
      if (changed && this.commit) {
        this.historyService.getCommit(this.commit.id).subscribe(commit => {
          this.commit = commit;
        });
      }
    });
  }

  private runRevert(request: Observable<{ commitId: number }>): void {
    this.isReverting = true;
    this.actionError = '';
    this.actionMessage = '';
    request.subscribe({
      next: result => {
        this.isReverting = false;
        void this.router.navigate(['/commits', result.commitId]);
      },
      error: err => {
        this.actionError = historyError(err, 'Could not revert this change.');
        this.isReverting = false;
      }
    });
  }

  opBadgeClass(op: string): string {
    switch ((op || '').toLowerCase()) {
      case 'create':
        return 'badge-success';
      case 'delete':
        return 'badge-destructive';
      case 'rename':
        return 'badge-muted';
      default:
        return 'badge-muted';
    }
  }
}
