import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';

import { AuthService } from '../../../../services/auth.service';
import { WarehouseService } from '../../services/warehouse.service';
import { WarehouseItem } from '../../types/warehouse';
import { WarehouseEditDialogComponent } from '../warehouse-edit-dialog/warehouse-edit-dialog.component';

export interface WarehouseViewDialogData {
  item: WarehouseItem;
  takenPartIds?: number[];
}

@Component({
    selector: 'warehouse-view-dialog',
    templateUrl: './warehouse-view-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class WarehouseViewDialogComponent {
  item: WarehouseItem;
  private readonly takenPartIds: number[];
  private changed = false;

  constructor(
    public dialogRef: MatDialogRef<WarehouseViewDialogComponent, boolean>,
    @Inject(MAT_DIALOG_DATA) data: WarehouseViewDialogData,
    private warehouse: WarehouseService,
    private dialog: MatDialog,
    public auth: AuthService
  ) {
    this.item = data.item;
    this.takenPartIds = data.takenPartIds ?? [];
  }

  get imageUrl(): string | null {
    if (!this.item.hasImage) {
      return null;
    }

    return this.item.imageHash
      ? this.warehouse.imageContentUrl(this.item.imageHash)
      : this.warehouse.imageUrl(this.item.id);
  }

  close(): void {
    this.dialogRef.close(this.changed);
  }

  edit(): void {
    if (!this.auth.canWriteWarehouse()) {
      return;
    }

    this.dialog.open(WarehouseEditDialogComponent, {
      width: '720px',
      maxWidth: '95vw',
      data: { item: this.item, takenPartIds: this.takenPartIds }
    }).afterClosed().subscribe(saved => {
      if (!saved) {
        return;
      }

      this.changed = true;
      this.warehouse.get(this.item.id).subscribe({
        next: item => {
          this.item = item;
        },
        error: () => this.dialogRef.close(true)
      });
    });
  }
}
