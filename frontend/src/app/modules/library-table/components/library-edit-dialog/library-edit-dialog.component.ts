import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { LibraryService } from '../../services/library.service';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Library } from '../../types/library';

@Component({
    selector: 'library-edit-dialog',
    templateUrl: './library-edit-dialog.component.html',
    styleUrl: './library-edit-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class LibraryEditDialogComponent {
  library: Library = {
    id: -1,
    uuid: '',
    name: '',
    filename: '',
    description: '',
    specialColumns: undefined,
    updatedAt: '',
    createdAt: ''
  };

  id: number = -1;

  constructor(
    public dialogRef: MatDialogRef<LibraryEditDialogComponent>,
    private libraryService: LibraryService,
    @Inject(MAT_DIALOG_DATA) public data: {id: number}
  ) {
    this.id = data.id;

    if (this.id >= 0) {
      this.libraryService.getLibrary(this.id).subscribe(library => {
        this.library = library;
      });
    }
  }

  ngAfterViewInit(): void {

  }

  close(): void {
    this.dialogRef.close(false);
  }

  confirm(): void {
    if (this.id >= 0) {
      this.libraryService.updateData(this.id, this.library).subscribe({
        next: (response) => console.log(response),
        error: (error) => console.error('Error:', error)
      });
    } else {
      this.libraryService.postData(this.library).subscribe({
        next: (response) => console.log(response),
        error: (error) => console.error('Error:', error)
      });
    }

    this.dialogRef.close(true);
  }

}
