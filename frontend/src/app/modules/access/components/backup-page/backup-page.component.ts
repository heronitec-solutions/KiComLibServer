import { Component, ElementRef, OnInit, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { HttpEventType } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';

import { apiErrorMessage } from '../../../../services/auth.service';
import { BackupInfo, BackupRestoreResult, BackupService } from '../../../../services/backup.service';
import { BackupConfirmDialogComponent, BackupConfirmResult } from '../backup-confirm-dialog/backup-confirm-dialog.component';

type BackupBusyAction = 'create' | 'upload' | 'restore';

@Component({
    selector: 'backup-page',
    templateUrl: './backup-page.component.html',
    styleUrl: './backup-page.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class BackupPageComponent implements OnInit {
  @ViewChild('fileInput') fileInput?: ElementRef<HTMLInputElement>;

  backups: BackupInfo[] = [];
  name = '';
  passphrase = '';
  confirmPassphrase = '';
  isLoading = true;
  busyAction: BackupBusyAction | null = null;
  uploadProgress = 0;
  restoringId: string | null = null;
  error = '';
  message = '';

  readonly columns = ['name', 'createdAt', 'size', 'source', 'actions'];

  constructor(
    private backupsApi: BackupService,
    private dialog: MatDialog
  ) { }

  get isBusy(): boolean {
    return this.busyAction !== null;
  }

  get busyLabel(): string {
    switch (this.busyAction) {
      case 'create':
        return 'Creating backup…';
      case 'upload':
        return this.uploadProgress >= 100
          ? 'Saving uploaded backup…'
          : 'Uploading backup…';
      case 'restore':
        return 'Restoring backup…';
      default:
        return '';
    }
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading = true;
    this.error = '';
    this.backupsApi.list().subscribe({
      next: backups => {
        this.backups = backups;
        this.isLoading = false;
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not load backups');
        this.isLoading = false;
      }
    });
  }

  create(): void {
    const passphrase = this.passphrase.trim();
    if (passphrase.length < 12) {
      this.error = 'Passphrase must be at least 12 characters';
      this.message = '';
      return;
    }
    if (passphrase !== this.confirmPassphrase.trim()) {
      this.error = 'Passphrases do not match';
      this.message = '';
      return;
    }

    this.error = '';
    this.message = '';
    this.busyAction = 'create';
    this.backupsApi.create(this.name.trim(), passphrase).subscribe({
      next: () => {
        this.name = '';
        this.passphrase = '';
        this.confirmPassphrase = '';
        this.message = 'Encrypted backup created. Keep the passphrase. It is not stored.';
        this.busyAction = null;
        this.load();
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not create the backup');
        this.busyAction = null;
      }
    });
  }

  chooseFile(): void {
    this.fileInput?.nativeElement.click();
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }

    this.error = '';
    this.message = '';
    this.busyAction = 'upload';
    this.uploadProgress = 0;
    this.backupsApi.upload(file, this.name.trim()).subscribe({
      next: event => {
        if (event.type === HttpEventType.UploadProgress) {
          const total = event.total || file.size;
          this.uploadProgress = total > 0
            ? Math.min(100, Math.round((event.loaded / total) * 100))
            : 0;
          return;
        }
        if (event.type !== HttpEventType.Response) {
          return;
        }

        this.name = '';
        this.message = 'Backup imported.';
        this.busyAction = null;
        this.uploadProgress = 0;
        this.load();
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not import the backup');
        this.busyAction = null;
        this.uploadProgress = 0;
      }
    });
  }

  download(backup: BackupInfo): void {
    this.error = '';
    this.backupsApi.download(backup).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = backup.fileName || 'kicomlib-backup.kibkp';
        link.click();
        URL.revokeObjectURL(url);
      },
      error: () => this.error = 'Could not download the backup'
    });
  }

  restore(backup: BackupInfo): void {
    const encrypted = backup.encrypted;
    this.dialog.open(BackupConfirmDialogComponent, {
      width: '520px',
      data: {
        title: 'Restore backup',
        message: encrypted
          ? `Replace the current database and key ring with “${backup.name}”? Library, users, settings and stored secrets return to that point. The server restarts afterwards.`
          : `Replace the current database with “${backup.name}”? This older backup does not include the key ring. You may need to sign in again.`,
        confirmLabel: 'Restore',
        danger: true,
        askPassphrase: encrypted
      }
    }).afterClosed().subscribe((result: BackupConfirmResult | undefined) => {
      if (!result?.confirmed) {
        return;
      }

      this.error = '';
      this.message = '';
      this.busyAction = 'restore';
      this.restoringId = backup.id;
      this.backupsApi.restore(backup.id, result.passphrase).subscribe({
        next: (restored: BackupRestoreResult) => {
          this.message = this.restoreMessage(backup, restored);
          this.busyAction = null;
          this.restoringId = null;
          if (!restored.restarting) {
            this.load();
          }
        },
        error: error => {
          this.error = apiErrorMessage(error, 'Could not restore the backup');
          this.busyAction = null;
          this.restoringId = null;
        }
      });
    });
  }

  remove(backup: BackupInfo): void {
    this.dialog.open(BackupConfirmDialogComponent, {
      width: '480px',
      data: {
        title: 'Delete backup',
        message: `Delete “${backup.name}” from the backup list? The live database is not changed.`,
        confirmLabel: 'Delete',
        danger: true
      }
    }).afterClosed().subscribe((result: BackupConfirmResult | undefined) => {
      if (!result?.confirmed) {
        return;
      }

      this.backupsApi.delete(backup.id).subscribe({
        next: () => this.load(),
        error: error => this.error = apiErrorMessage(error, 'Could not delete the backup')
      });
    });
  }

  private restoreMessage(backup: BackupInfo, restored: BackupRestoreResult): string {
    if (restored.restarting) {
      return 'Backup restored. The server is restarting to load the key ring. Sign in again when it is back. If it does not come back on its own, start it again.';
    }
    if (restored.keysRestored) {
      return 'Backup restored, including the key ring. Start the server again so the restored keys are loaded, then sign in.';
    }
    if (backup.encrypted) {
      return 'Backup restored. This archive had no key files, so the current key ring was left unchanged. Sign in again if your session no longer matches.';
    }
    return 'Database restored from this older backup. The key ring was left unchanged. Sign in again if your session no longer matches.';
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
      return `${(bytes / 1024).toFixed(1)} KB`;
    }
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
