import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

export interface ErrorNoticeDialogData {
  message: string;
  details: string[];
}

@Component({
    selector: 'error-notice-dialog',
    templateUrl: './error-notice-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ErrorNoticeDialogComponent {
  constructor(
    public dialogRef: MatDialogRef<ErrorNoticeDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: ErrorNoticeDialogData
  ) { }

  close(): void {
    this.dialogRef.close();
  }
}
