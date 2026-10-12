import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';

import { AuthService } from '../../../../services/auth.service';
import { apiErrorMessage } from '../../../../shared/api-error';
import { DeleteConfirmationDialogComponent } from '../../../../components/delete-confirmation-dialog/delete-confirmation-dialog.component';
import { GitService } from '../../services/git.service';
import { GitCredential, GitHost, GitRepository, GitWebhookInfo } from '../../types/git';
import { CredentialDialogComponent } from '../credential-dialog/credential-dialog.component';
import { HostDialogComponent } from '../host-dialog/host-dialog.component';
import { RepositoryWizardComponent } from '../repository-wizard/repository-wizard.component';

@Component({
    selector: 'git-admin-page',
    templateUrl: './git-admin-page.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class GitAdminPageComponent implements OnInit {
  readonly hostColumns = ['name', 'kind', 'webUrl', 'ssh', 'actions'];
  readonly credentialColumns = ['name', 'host', 'kind', 'hint', 'expires', 'actions'];
  readonly repositoryColumns = ['path', 'status', 'sync', 'fetch', 'error', 'actions'];

  hosts: GitHost[] = [];
  credentials: GitCredential[] = [];
  repositories: GitRepository[] = [];
  webhookNotice: GitWebhookInfo | null = null;
  error = '';
  message = '';
  loading = true;
  copied = '';

  constructor(
    private git: GitService,
    private dialog: MatDialog,
    public auth: AuthService
  ) { }

  ngOnInit(): void {
    if (!this.auth.profile().isAdmin) {
      this.loading = false;
      this.error = 'Git integration is available to administrators.';
      return;
    }
    this.reload();
  }

  reload(): void {
    this.loading = true;
    this.error = '';
    this.git.hosts().subscribe({
      next: hosts => {
        this.hosts = hosts;
        this.git.credentials().subscribe({
          next: credentials => {
            this.credentials = credentials;
            this.git.repositories().subscribe({
              next: repositories => {
                this.repositories = repositories;
                this.loading = false;
              },
              error: error => this.fail(error, 'Could not load repositories.')
            });
          },
          error: error => this.fail(error, 'Could not load credentials.')
        });
      },
      error: error => this.fail(error, 'Could not load Git hosts.')
    });
  }

  hostName(hostId: number): string {
    return this.hosts.find(host => host.id === hostId)?.name || '—';
  }

  expiryLabel(value: string): string {
    if (!value) {
      return '—';
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }
    const days = (date.getTime() - Date.now()) / 86400000;
    if (days < 0) {
      return 'Expired';
    }
    if (days < 14) {
      return 'Expires soon';
    }
    return date.toLocaleDateString();
  }

  editHost(host: GitHost | null): void {
    this.dialog.open(HostDialogComponent, {
      width: '40rem',
      maxWidth: '95vw',
      data: { host }
    }).afterClosed().subscribe(changed => {
      if (changed) {
        this.reload();
      }
    });
  }

  deleteHost(host: GitHost): void {
    this.confirmDelete(() => {
      this.git.deleteHost(host.id).subscribe({
        next: () => this.reload(),
        error: error => {
          this.error = apiErrorMessage(error, 'Could not delete the host.');
        }
      });
    });
  }

  editCredential(credential: GitCredential | null): void {
    this.dialog.open(CredentialDialogComponent, {
      width: '40rem',
      maxWidth: '95vw',
      data: { hosts: this.hosts, credential }
    }).afterClosed().subscribe(changed => {
      if (changed) {
        this.reload();
      }
    });
  }

  deleteCredential(credential: GitCredential): void {
    this.confirmDelete(() => {
      this.git.deleteCredential(credential.id).subscribe({
        next: () => this.reload(),
        error: error => {
          this.error = apiErrorMessage(error, 'Could not delete the credential.');
        }
      });
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

  addRepository(): void {
    this.dialog.open(RepositoryWizardComponent, {
      width: '44rem',
      maxWidth: '95vw',
      data: { hosts: this.hosts, credentials: this.credentials }
    }).afterClosed().subscribe(changed => {
      if (changed) {
        this.reload();
      }
    });
  }

  discover(repository: GitRepository): void {
    this.dialog.open(RepositoryWizardComponent, {
      width: '44rem',
      maxWidth: '95vw',
      data: {
        hosts: this.hosts,
        credentials: this.credentials,
        repository,
        startAtDiscover: true
      }
    }).afterClosed().subscribe(() => this.reload());
  }

  sync(repository: GitRepository): void {
    this.git.syncRepository(repository.id).subscribe({
      next: () => {
        this.message = 'Sync queued.';
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not queue a sync.');
      }
    });
  }

  rotateWebhook(repository: GitRepository): void {
    this.git.rotateWebhook(repository.id).subscribe({
      next: info => {
        this.webhookNotice = info;
        this.message = 'Webhook secret rotated. Copy it now; it will not be shown again.';
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not rotate the webhook secret.');
      }
    });
  }

  deleteRepository(repository: GitRepository, deleteProjects: boolean): void {
    this.confirmDelete(() => {
      this.git.deleteRepository(repository.id, deleteProjects).subscribe({
        next: () => this.reload(),
        error: (error: HttpErrorResponse) => {
          this.error = error.status === 409
            ? apiErrorMessage(error, 'This repository still has projects. Delete those projects as well, then try again.')
            : apiErrorMessage(error, 'Could not delete the repository.');
        }
      });
    });
  }

  private confirmDelete(action: () => void): void {
    this.dialog.open(DeleteConfirmationDialogComponent, { width: '28rem' }).afterClosed().subscribe(confirmed => {
      if (confirmed) {
        action();
      }
    });
  }

  private fail(error: unknown, fallback: string): void {
    this.loading = false;
    this.error = apiErrorMessage(error, fallback);
  }
}
