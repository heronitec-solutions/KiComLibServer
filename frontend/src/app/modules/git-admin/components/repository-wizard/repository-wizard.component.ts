import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { concat, of } from 'rxjs';
import { catchError, concatMap, toArray } from 'rxjs/operators';

import { apiErrorMessage } from '../../../../shared/api-error';
import { ProjectService } from '../../../projects/services/project.service';
import { ProjectDetail } from '../../../projects/types/project';
import { GitService } from '../../services/git.service';
import {
  DiscoveredProject,
  GitCredential,
  GitHost,
  GitRepository,
  GitSyncMode,
  GitTestResult,
  GitWebhookInfo
} from '../../types/git';
import { CredentialDialogComponent } from '../credential-dialog/credential-dialog.component';
import { HostDialogComponent } from '../host-dialog/host-dialog.component';

export interface RepositoryWizardData {
  hosts: GitHost[];
  credentials: GitCredential[];
  repository?: GitRepository;
  startAtDiscover?: boolean;
}

interface DiscoverRow extends DiscoveredProject {
  selected: boolean;
  editName: string;
}

@Component({
    selector: 'repository-wizard',
    templateUrl: './repository-wizard.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class RepositoryWizardComponent {
  step = 0;
  hosts: GitHost[] = [];
  credentials: GitCredential[] = [];
  hostId: number | null = null;
  cloneUrl = '';
  credentialId: number | null = null;
  testResult: GitTestResult | null = null;
  tracked = new Set<string>();
  tagPattern = '*';
  syncMode: GitSyncMode = 'poll';
  repositoryId: number | null = null;
  webhook: GitWebhookInfo | null = null;
  discovered: DiscoverRow[] = [];
  created: ProjectDetail[] = [];
  error = '';
  message = '';
  busy = false;
  copied = '';

  constructor(
    public dialogRef: MatDialogRef<RepositoryWizardComponent, boolean>,
    private git: GitService,
    private projects: ProjectService,
    private dialog: MatDialog,
    @Inject(MAT_DIALOG_DATA) public data: RepositoryWizardData
  ) {
    this.hosts = [...data.hosts];
    this.credentials = [...data.credentials];
    if (data.repository) {
      this.repositoryId = data.repository.id;
      this.hostId = data.repository.hostId;
      this.cloneUrl = data.repository.cloneUrl;
      this.credentialId = data.repository.credentialId;
      this.tagPattern = data.repository.tagPattern || '*';
      this.syncMode = data.repository.syncMode;
      data.repository.trackedBranches.forEach(branch => this.tracked.add(branch));
    }
    if (data.startAtDiscover && this.repositoryId) {
      this.step = 6;
      this.loadDiscovered();
    }
  }

  get hostCredentials(): GitCredential[] {
    return this.credentials.filter(item => item.hostId === this.hostId);
  }

  close(): void {
    this.dialogRef.close(this.created.length > 0 || !!this.repositoryId);
  }

  newHost(): void {
    this.dialog.open(HostDialogComponent, {
      width: '40rem',
      maxWidth: '95vw',
      data: { host: null }
    }).afterClosed().subscribe(changed => {
      if (!changed) {
        return;
      }
      this.git.hosts().subscribe(hosts => {
        this.hosts = hosts;
        const newest = hosts[hosts.length - 1];
        if (newest) {
          this.hostId = newest.id;
        }
      });
    });
  }

  newCredential(): void {
    this.dialog.open(CredentialDialogComponent, {
      width: '40rem',
      maxWidth: '95vw',
      data: { hosts: this.hosts, credential: null }
    }).afterClosed().subscribe(changed => {
      if (!changed) {
        return;
      }
      this.git.credentials().subscribe(credentials => {
        this.credentials = credentials;
        const match = credentials.filter(item => item.hostId === this.hostId);
        const newest = match[match.length - 1];
        if (newest) {
          this.credentialId = newest.id;
        }
      });
    });
  }

  next(): void {
    this.error = '';
    if (this.step === 3) {
      this.runTest();
      return;
    }
    if (this.step === 4) {
      this.createRepository();
      return;
    }
    if (this.step < 6) {
      this.step += 1;
    }
  }

  back(): void {
    if (this.step > 0 && !this.data.startAtDiscover) {
      this.step -= 1;
    }
  }

  toggleBranch(name: string, checked: boolean): void {
    if (checked) {
      this.tracked.add(name);
    } else {
      this.tracked.delete(name);
    }
  }

  isTracked(name: string): boolean {
    return name === this.testResult?.defaultBranch || this.tracked.has(name);
  }

  runTest(): void {
    if (!this.hostId || !this.credentialId || !this.cloneUrl.trim()) {
      this.error = 'Host, clone URL and credential are required.';
      return;
    }
    this.busy = true;
    this.error = '';
    this.git.testRepository(this.hostId, this.cloneUrl.trim(), this.credentialId).subscribe({
      next: result => {
        this.testResult = result;
        this.busy = false;
        if (!result.ok) {
          this.error = result.error || 'The repository test failed.';
          return;
        }
        this.step = 4;
      },
      error: error => {
        this.busy = false;
        this.error = apiErrorMessage(error, 'Could not test the repository.');
      }
    });
  }

  createRepository(): void {
    if (!this.hostId || !this.credentialId) {
      return;
    }
    const extra = [...this.tracked].filter(name => name !== this.testResult?.defaultBranch);
    this.busy = true;
    this.error = '';
    this.git.createRepository({
      hostId: this.hostId,
      credentialId: this.credentialId,
      cloneUrl: this.cloneUrl.trim(),
      trackedBranches: extra,
      tagPattern: this.tagPattern.trim() || '*',
      syncMode: this.syncMode
    }).subscribe({
      next: created => {
        this.repositoryId = created.id;
        this.webhook = created.webhook;
        this.busy = false;
        this.step = 5;
      },
      error: error => {
        this.busy = false;
        this.error = apiErrorMessage(error, 'Could not create the repository.');
      }
    });
  }

  copy(value: string, label: string): void {
    if (!value) {
      return;
    }
    const done = () => {
      this.copied = label;
    };
    if (navigator.clipboard?.writeText) {
      void navigator.clipboard.writeText(value).then(done);
      return;
    }
    const area = document.createElement('textarea');
    area.value = value;
    document.body.appendChild(area);
    area.select();
    document.execCommand('copy');
    area.remove();
    done();
  }

  goDiscover(): void {
    this.step = 6;
    this.loadDiscovered();
  }

  syncThenDiscover(): void {
    if (!this.repositoryId) {
      return;
    }
    this.busy = true;
    this.error = '';
    this.git.syncRepository(this.repositoryId).subscribe({
      next: () => {
        this.message = 'Sync queued. Refresh discovery in a moment if the list is still empty.';
        this.busy = false;
        this.loadDiscovered();
      },
      error: error => {
        this.busy = false;
        this.error = apiErrorMessage(error, 'Could not queue a sync.');
      }
    });
  }

  loadDiscovered(): void {
    if (!this.repositoryId) {
      return;
    }
    this.busy = true;
    this.git.discovered(this.repositoryId).subscribe({
      next: rows => {
        this.discovered = rows.map(row => ({
          ...row,
          selected: !row.alreadyProject,
          editName: row.name
        }));
        this.busy = false;
      },
      error: error => {
        this.busy = false;
        this.error = apiErrorMessage(error, 'Could not list KiCad projects in the repository.');
      }
    });
  }

  createProjects(): void {
    if (!this.repositoryId) {
      return;
    }
    const chosen = this.discovered.filter(row => row.selected && !row.alreadyProject && row.projectFile);
    if (!chosen.length) {
      this.error = 'Select at least one KiCad project.';
      return;
    }
    this.busy = true;
    this.error = '';
    concat(...chosen.map(row =>
      this.projects.create({
        repositoryId: this.repositoryId as number,
        projectFile: row.projectFile,
        name: row.editName.trim() || row.name,
        description: '',
        status: 'development',
        customer: '',
        ownerUserId: null,
        exportScope: 'project_dir'
      }).pipe(
        catchError((error: HttpErrorResponse) => {
          this.error = apiErrorMessage(error, `Could not create ${row.projectFile}.`);
          return of(null);
        })
      )
    )).pipe(
      concatMap(project => of(project)),
      toArray()
    ).subscribe(results => {
      this.created = results.filter((item): item is ProjectDetail => !!item);
      this.busy = false;
      if (this.created.length) {
        this.message = `Created ${this.created.length} project${this.created.length === 1 ? '' : 's'}.`;
      }
    });
  }
}
