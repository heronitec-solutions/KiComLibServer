import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { DistributorService } from '../../services/distributor.service';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Distributor } from '../../types/distributor';

@Component({
    selector: 'distributor-edit-dialog',
    templateUrl: './distributor-edit-dialog.component.html',
    styleUrl: './distributor-edit-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class DistributorEditDialogComponent {
  distributor: Distributor = {
    id: -1,
    uuid: '',
    name: '',
    url: '',
    trustedPartsId: '',
    isFavorite: false,
    updatedAt: '',
    createdAt: ''
  };

  id: number = -1;


  constructor(
    public dialogRef: MatDialogRef<DistributorEditDialogComponent>,
    private distributorService: DistributorService,
    @Inject(MAT_DIALOG_DATA) public data: {id: number}
  ) {
    this.id = data.id;

    if (this.id >= 0) {
      this.distributorService.getDistributor(this.id).subscribe(distributor => {
        this.distributor = distributor;
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
      this.distributorService.updateData(this.id, this.distributor).subscribe({
        next: (response) => console.log(response),
        error: (error) => console.error('Error:', error)
      });
    } else {
      this.distributorService.postData(this.distributor).subscribe({
        next: (response) => console.log(response),
        error: (error) => console.error('Error:', error)
      });
    }

    this.dialogRef.close(true);
  }
}
