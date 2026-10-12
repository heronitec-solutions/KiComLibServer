import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { ManufacturerService } from '../../services/manufacturer.service';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Manufacturer } from '../../types/manufacturer';

@Component({
    selector: 'manufacturer-edit-dialog',
    templateUrl: './manufacturer-edit-dialog.component.html',
    styleUrl: './manufacturer-edit-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ManufacturerEditDialogComponent {
  manufacturer: Manufacturer = {
    id: -1,
    uuid: '',
    name: '',
    url: '',
    trustedPartsId: '',
    updatedAt: '',
    createdAt: ''
  };

  id: number = -1;


  constructor(
    public dialogRef: MatDialogRef<ManufacturerEditDialogComponent>,
    private manufacturerService: ManufacturerService,
    @Inject(MAT_DIALOG_DATA) public data: {id: number}
  ) {
    this.id = data.id;

    if (this.id >= 0) {
      this.manufacturerService.getManufacturer(this.id).subscribe(manufacturer => {
        this.manufacturer = manufacturer;
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
      this.manufacturerService.updateData(this.id, this.manufacturer).subscribe({
        next: (response) => console.log(response),
        error: (error) => console.error('Error:', error)
      });
    } else {
      this.manufacturerService.postData(this.manufacturer).subscribe({
        next: (response) => console.log(response),
        error: (error) => console.error('Error:', error)
      });
    }

    this.dialogRef.close(true);
  }

}
