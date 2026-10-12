import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { apiErrorMessage } from '../../../../shared/api-error';
import { GitService } from '../../services/git.service';
import { GitHost, GitHostKind, GitHostWrite } from '../../types/git';

export interface HostDialogData {
  host: GitHost | null;
}

@Component({
    selector: 'host-dialog',
    templateUrl: './host-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class HostDialogComponent {
  kind: GitHostKind = 'gitlab';
  name = '';
  webUrl = '';
  sshHost = '';
  caCertificate = '';
  allowHttp = false;
  savedId: number | null = null;
  fingerprints: string[] = [];
  knownHosts = '';
  error = '';
  message = '';
  busy = false;

  constructor(
    public dialogRef: MatDialogRef<HostDialogComponent, boolean>,
    private git: GitService,
    @Inject(MAT_DIALOG_DATA) public data: HostDialogData
  ) {
    if (data.host) {
      this.kind = data.host.kind;
      this.name = data.host.name;
      this.webUrl = data.host.webUrl;
      this.sshHost = data.host.sshHost;
      this.caCertificate = data.host.caCertificate;
      this.allowHttp = data.host.allowHttp;
      this.savedId = data.host.id;
      this.knownHosts = data.host.sshKnownHosts;
    }
  }

  get isSystem(): boolean {
    return !!this.data.host?.isSystem;
  }

  close(): void {
    this.dialogRef.close(!!this.savedId && !this.data.host);
  }

  save(): void {
    const body: GitHostWrite = {
      kind: this.kind,
      name: this.name.trim(),
      webUrl: this.webUrl.trim(),
      sshHost: this.sshHost.trim(),
      caCertificate: this.caCertificate.trim(),
      allowHttp: this.allowHttp
    };
    if (!body.name || !body.webUrl) {
      return;
    }
    this.busy = true;
    this.error = '';
    const request = this.savedId
      ? this.git.updateHost(this.savedId, body)
      : this.git.createHost(body);
    request.subscribe({
      next: host => {
        this.savedId = host.id;
        this.busy = false;
        this.message = 'Host saved.';
        if (this.data.host) {
          this.dialogRef.close(true);
        }
      },
      error: error => {
        this.busy = false;
        this.error = apiErrorMessage(error, 'Could not save the host.');
      }
    });
  }

  scan(): void {
    if (!this.savedId) {
      return;
    }
    this.busy = true;
    this.error = '';
    this.git.scanHostKey(this.savedId).subscribe({
      next: scan => {
        this.fingerprints = scan.fingerprints;
        this.knownHosts = scan.knownHosts;
        this.busy = false;
        this.message = 'Compare the fingerprint with the host owner, then confirm it.';
      },
      error: error => {
        this.busy = false;
        this.error = apiErrorMessage(error, 'Could not scan the SSH host key.');
      }
    });
  }

  confirmKey(): void {
    if (!this.savedId || !this.knownHosts.trim()) {
      return;
    }
    this.busy = true;
    this.git.confirmHostKey(this.savedId, this.knownHosts.trim()).subscribe({
      next: () => {
        this.busy = false;
        this.message = 'SSH host key saved.';
      },
      error: error => {
        this.busy = false;
        this.error = apiErrorMessage(error, 'Could not save the SSH host key.');
      }
    });
  }
}
