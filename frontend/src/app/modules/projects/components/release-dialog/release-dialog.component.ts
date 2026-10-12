import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { ProjectRevision, ReleaseWrite } from '../../types/project';

export interface ReleaseDialogData {
  revisions: ProjectRevision[];
}

@Component({
    selector: 'release-dialog',
    templateUrl: './release-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ReleaseDialogComponent {
  name = '';
  notes = '';
  sha = '';
  tag = '';

  constructor(
    public dialogRef: MatDialogRef<ReleaseDialogComponent, ReleaseWrite>,
    @Inject(MAT_DIALOG_DATA) public data: ReleaseDialogData
  ) { }

  close(): void {
    this.dialogRef.close();
  }

  confirm(): void {
    const name = this.name.trim();
    if (!name || (!this.sha.trim() && !this.tag.trim())) {
      return;
    }
    this.dialogRef.close({
      name,
      notes: this.notes.trim(),
      sha: this.sha.trim(),
      tag: this.tag.trim()
    });
  }
}
