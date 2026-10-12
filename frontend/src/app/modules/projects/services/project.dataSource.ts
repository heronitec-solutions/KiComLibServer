import { Injectable } from '@angular/core';

import { PagedDataSource } from '../../../shared/paged-data-source';
import { ProjectRelease, ProjectSummary } from '../types/project';
import { ProjectService } from './project.service';

@Injectable()
export class ProjectDataSource extends PagedDataSource<ProjectSummary> {
  constructor(private projects: ProjectService) {
    super();
  }

  load(status: string, q: string, onError?: (message: string) => void): void {
    this.isLoading$.next(true);
    this.projects.list(status, q).subscribe({
      next: rows => {
        this.setRows(rows);
        this.isLoading$.next(false);
      },
      error: () => {
        this.setRows([]);
        this.isLoading$.next(false);
        onError?.('Could not load projects.');
      }
    });
  }
}

export class ReleaseDataSource extends PagedDataSource<ProjectRelease> {
  replace(rows: ProjectRelease[]): void {
    this.setRows(rows);
  }
}
