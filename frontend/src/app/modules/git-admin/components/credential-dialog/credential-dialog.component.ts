import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { apiErrorMessage } from '../../../../shared/api-error';
import { GitService } from '../../services/git.service';
import { GitCredential, GitCredentialKind, GitHost } from '../../types/git';

export interface CredentialDialogData {
  hosts: GitHost[];
  credential: GitCredential | null;
}

@Component({
    selector: 'credential-dialog',
    templateUrl: './credential-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class CredentialDialogComponent {
  hostId: number | null = null;
  name = '';
  kind: GitCredentialKind = 'https_token';
  username = '';
  secret = '';
  generate = true;
  expiresAt = '';
  saved: GitCredential | null = null;
  error = '';
  busy = false;
  copied = '';

  constructor(
    public dialogRef: MatDialogRef<CredentialDialogComponent, boolean>,
    private git: GitService,
    @Inject(MAT_DIALOG_DATA) public data: CredentialDialogData
  ) {
    if (data.credential) {
      this.hostId = data.credential.hostId;
      this.name = data.credential.name;
      this.kind = data.credential.kind;
      this.username = data.credential.username;
      this.expiresAt = toDateInput(data.credential.expiresAt);
      this.generate = false;
      this.saved = data.credential;
    } else if (data.hosts.length) {
      this.hostId = data.hosts[0].id;
    }
  }

  close(): void {
    this.dialogRef.close(!!this.saved);
  }

  save(): void {
    if (!this.hostId || !this.name.trim()) {
      return;
    }
    this.busy = true;
    this.error = '';
    if (this.data.credential) {
      this.git.replaceCredentialSecret(
        this.data.credential.id,
        this.secret,
        this.username.trim(),
        this.expiresAt
      ).subscribe({
        next: credential => {
          this.saved = { ...credential, publicKey: credential.publicKey || this.saved?.publicKey || '' };
          this.secret = '';
          this.busy = false;
          this.dialogRef.close(true);
        },
        error: error => {
          this.busy = false;
          this.error = apiErrorMessage(error, 'Could not replace the credential.');
        }
      });
      return;
    }

    this.git.createCredential({
      hostId: this.hostId,
      name: this.name.trim(),
      kind: this.kind,
      username: this.username.trim(),
      secret: this.secret,
      generate: this.kind === 'ssh_key' && this.generate,
      expiresAt: this.expiresAt
    }).subscribe({
      next: credential => {
        this.saved = credential;
        this.secret = '';
        this.busy = false;
        if (credential.kind === 'https_token') {
          this.dialogRef.close(true);
        }
      },
      error: error => {
        this.busy = false;
        this.error = apiErrorMessage(error, 'Could not create the credential.');
      }
    });
  }

  copyPublicKey(): void {
    const value = this.saved?.publicKey || '';
    if (!value) {
      return;
    }
    const done = () => {
      this.copied = 'Public key copied.';
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
}

function toDateInput(value: string): string {
  if (!value) {
    return '';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value.slice(0, 10);
  }
  return date.toISOString().slice(0, 10);
}
