import { Component, OnDestroy, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

import { AuthService } from '../../../../services/auth.service';
import { apiErrorMessage } from '../../../../shared/api-error';
import { DeleteConfirmationDialogComponent } from '../../../../components/delete-confirmation-dialog/delete-confirmation-dialog.component';
import { projectStatusClass, projectStatusLabel, shortSha } from '../../labels';
import { ProjectService } from '../../services/project.service';
import { ProjectAccessEntry, ProjectDetail, ProjectRevision } from '../../types/project';
import { ProjectAccessDialogComponent } from '../project-access-dialog/project-access-dialog.component';
import { ProjectEditDialogComponent } from '../project-edit-dialog/project-edit-dialog.component';

@Component({
    selector: 'project-detail',
    templateUrl: './project-detail.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ProjectDetailComponent implements OnInit, OnDestroy {
  readonly statusLabel = projectStatusLabel;
  readonly statusClass = projectStatusClass;
  readonly shortSha = shortSha;

  project: ProjectDetail | null = null;
  selectedSha = '';
  selectedRevision: ProjectRevision | null = null;
  selectedLabel = '';
  accessEntries: ProjectAccessEntry[] = [];
  accessError = '';
  error = '';
  loading = true;

  private readonly destroyed$ = new Subject<void>();
  private projectId = 0;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private projects: ProjectService,
    private dialog: MatDialog,
    public auth: AuthService
  ) { }

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntil(this.destroyed$)).subscribe(params => {
      const nextId = Number(params.get('id'));
      if (nextId !== this.projectId) {
        this.selectedSha = '';
        this.selectedRevision = null;
        this.selectedLabel = '';
      }
      this.projectId = nextId;
      this.load();
    });
  }

  ngOnDestroy(): void {
    this.destroyed$.next();
    this.destroyed$.complete();
  }

  onRevision(row: ProjectRevision): void {
    this.applySelection(row.commitSha, row);
  }

  get selectedIsHead(): boolean {
    return !!this.selectedSha && this.selectedSha === this.project?.headSha;
  }

  get selectedRef(): string {
    const refs = this.selectedRevision?.refs || [];
    const tag = refs.find(name => name.startsWith('refs/tags/') && !name.endsWith('^{}'));
    const branch = refs.find(name => name.startsWith('refs/heads/'));
    const ref = tag || branch;
    return ref ? ref.replace(/^refs\/(?:tags|heads)\//, '') : '';
  }

  edit(): void {
    if (!this.project) {
      return;
    }
    this.dialog.open(ProjectEditDialogComponent, {
      width: '40rem',
      maxWidth: '95vw',
      data: { project: this.project }
    }).afterClosed().subscribe(result => {
      if (!result || !this.project) {
        return;
      }
      this.projects.update(this.project.id, result).subscribe({
        next: project => {
          this.project = project;
        },
        error: error => {
          this.error = apiErrorMessage(error, 'Could not save the project.');
        }
      });
    });
  }

  editAccess(): void {
    if (!this.project) {
      return;
    }
    this.auth.getGroups().subscribe({
      next: groups => {
        this.dialog.open(ProjectAccessDialogComponent, {
          width: '36rem',
          maxWidth: '95vw',
          data: {
            groups,
            entries: this.accessEntries.map(entry => ({ groupId: entry.groupId, canWrite: entry.canWrite }))
          }
        }).afterClosed().subscribe(result => {
          if (!result || !this.project) {
            return;
          }
          this.projects.saveAccess(this.project.id, result.entries).subscribe({
            next: entries => {
              this.accessEntries = entries;
              this.accessError = '';
            },
            error: error => {
              this.accessError = apiErrorMessage(error, 'Could not save project access.');
            }
          });
        });
      },
      error: error => {
        this.accessError = apiErrorMessage(error, 'Could not load groups.');
      }
    });
  }

  remove(): void {
    if (!this.project) {
      return;
    }
    this.dialog.open(DeleteConfirmationDialogComponent, { width: '28rem' }).afterClosed().subscribe(confirmed => {
      if (!confirmed || !this.project) {
        return;
      }
      this.projects.delete(this.project.id).subscribe({
        next: () => {
          void this.router.navigate(['/projects']);
        },
        error: error => {
          this.error = apiErrorMessage(error, 'Could not delete the project.');
        }
      });
    });
  }

  private load(): void {
    this.loading = true;
    this.error = '';
    this.projects.get(this.projectId).pipe(takeUntil(this.destroyed$)).subscribe({
      next: project => {
        this.project = project;
        this.loading = false;
        if (!this.selectedSha && project.headSha) {
          this.applySelection(project.headSha, null);
        }
        this.loadAccess();
      },
      error: error => {
        this.loading = false;
        this.error = apiErrorMessage(error, 'Could not load the project.');
      }
    });
  }

  private loadAccess(): void {
    if (!this.project?.permissions.canAdmin) {
      this.accessEntries = [];
      return;
    }
    this.projects.getAccess(this.project.id).subscribe({
      next: entries => {
        this.accessEntries = entries;
      },
      error: error => {
        this.accessError = apiErrorMessage(error, 'Could not load project access.');
      }
    });
  }

  private applySelection(sha: string, known: ProjectRevision | null): void {
    this.selectedSha = sha;
    this.selectedRevision = known;
    this.selectedLabel = '';
    if (!sha || !this.projectId) {
      return;
    }
    this.projects.revision(this.projectId, sha).pipe(takeUntil(this.destroyed$)).subscribe({
      next: detail => {
        if (this.selectedSha !== sha) {
          return;
        }
        this.selectedRevision = detail;
        this.selectedLabel = detail.revisionLabel;
      }
    });
  }
}
