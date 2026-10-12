import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { LibraryFileService } from '../../services/library-file.service';
import { LibraryFile } from '../../types/library-file';

@Component({
    selector: 'library-file-edit-dialog',
    templateUrl: './library-file-edit-dialog.component.html',
    styleUrl: './library-file-edit-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class LibraryFileEditDialogComponent {
  libraryFile: LibraryFile = {
    id: -1,
    uuid: '',
    type: 1,
    name: '',
    comment: '',
    filename: '',
    description: '',
    version: '',
    updatedAt: '',
    createdAt: '',
    footprints: [],
    symbols: []
  };

  id = -1;

  constructor(
    public dialogRef: MatDialogRef<LibraryFileEditDialogComponent>,
    private libraryFileService: LibraryFileService,
    @Inject(MAT_DIALOG_DATA) public data: { id: number }
  ) {
    this.id = data.id;

    if (this.id >= 0) {
      this.libraryFileService.getLibraryFile(this.id).subscribe(libraryFile => {
        this.libraryFile = libraryFile;
      });
    }
  }

  close(): void {
    this.dialogRef.close(false);
  }

  confirm(): void {
    if (this.id >= 0) {
      this.libraryFileService.updateData(this.id, this.libraryFile).subscribe({
        next: () => this.dialogRef.close(true),
        error: () => this.dialogRef.close(false)
      });
      return;
    }

    this.libraryFileService.postData(this.libraryFile).subscribe({
      next: () => this.dialogRef.close(true),
      error: () => this.dialogRef.close(false)
    });
  }
}
