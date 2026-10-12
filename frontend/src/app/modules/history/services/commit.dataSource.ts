import { Injectable } from '@angular/core';
import { PagedDataSource } from '../../../shared/paged-data-source';
import { CommitSummary } from '../types/commit';
import { HistoryService } from './history.service';

@Injectable()
export class CommitDataSource extends PagedDataSource<CommitSummary> {
  constructor(private historyService: HistoryService) {
    super();
  }

  loadCommits(limit = 500, offset = 0): void {
    this.isLoading$.next(true);
    this.historyService.getCommits(limit, offset).subscribe({
      next: commits => {
        this.setRows(commits);
        this.isLoading$.next(false);
      },
      error: () => {
        this.setRows([]);
        this.isLoading$.next(false);
      }
    });
  }
}
