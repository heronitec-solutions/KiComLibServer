import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

export interface DatasheetDownloadDialogData {
  /** Filename when one can be read from the URL, otherwise the URL itself. */
  label: string;
}

@Component({
    selector: 'datasheet-download-dialog',
    templateUrl: './datasheet-download-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class DatasheetDownloadDialogComponent {
  constructor(
    public dialogRef: MatDialogRef<DatasheetDownloadDialogComponent, boolean>,
    @Inject(MAT_DIALOG_DATA) public data: DatasheetDownloadDialogData
  ) { }

  skip(): void {
    this.dialogRef.close(false);
  }

  download(): void {
    this.dialogRef.close(true);
  }
}
