import { AfterViewInit, Component, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { Router } from '@angular/router';
import { MatPaginator } from '@angular/material/paginator';

import { CommitDataSource } from '../../services/commit.dataSource';
import { HistoryService } from '../../services/history.service';
import { CommitSummary } from '../../types/commit';

@Component({
    selector: 'commit-table',
    templateUrl: './commit-table.component.html',
    styleUrl: './commit-table.component.scss',
    providers: [CommitDataSource],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class CommitTableComponent implements AfterViewInit {
  displayedColumns: string[] = ['id', 'message', 'author', 'client', 'createdAt', 'changeCount'];
  dataSource: CommitDataSource;

  @ViewChild(MatPaginator, { static: false }) paginator!: MatPaginator;

  constructor(
    private historyService: HistoryService,
    private router: Router
  ) {
    this.dataSource = new CommitDataSource(this.historyService);
    this.dataSource.loadCommits();
  }

  ngAfterViewInit(): void {
    this.dataSource.paginator = this.paginator;
  }

  openCommit(row: CommitSummary): void {
    void this.router.navigate(['/commits', row.id]);
  }
}
