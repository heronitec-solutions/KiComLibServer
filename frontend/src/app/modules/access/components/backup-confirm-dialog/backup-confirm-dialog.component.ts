import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

export interface BackupConfirmData {
  title: string;
  message: string;
  confirmLabel: string;
  danger?: boolean;
  askPassphrase?: boolean;
}

export interface BackupConfirmResult {
  confirmed: boolean;
  passphrase: string;
}

@Component({
    selector: 'backup-confirm-dialog',
    templateUrl: './backup-confirm-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class BackupConfirmDialogComponent {
  passphrase = '';

  constructor(
    public dialogRef: MatDialogRef<BackupConfirmDialogComponent, BackupConfirmResult>,
    @Inject(MAT_DIALOG_DATA) public data: BackupConfirmData
  ) { }

  get confirmDisabled(): boolean {
    return !!this.data.askPassphrase && this.passphrase.trim().length < 12;
  }

  close(): void {
    this.dialogRef.close({ confirmed: false, passphrase: '' });
  }

  confirm(): void {
    if (this.confirmDisabled) {
      return;
    }

    this.dialogRef.close({ confirmed: true, passphrase: this.passphrase.trim() });
  }
}
