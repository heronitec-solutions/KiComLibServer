import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { AccessGroup, GroupWrite } from '../../../../services/auth.service';

export interface GroupEditDialogData {
  group: AccessGroup | null;
}

@Component({
    selector: 'group-edit-dialog',
    templateUrl: './group-edit-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class GroupEditDialogComponent {
  name = '';
  description = '';
  canRead = true;
  canWrite = false;
  canReadWarehouse = false;
  canWriteWarehouse = false;
  canAdmin = false;
  isSystem = false;
  isDirectory = false;

  constructor(
    public dialogRef: MatDialogRef<GroupEditDialogComponent, GroupWrite>,
    @Inject(MAT_DIALOG_DATA) public data: GroupEditDialogData
  ) {
    if (data.group) {
      this.name = data.group.name;
      this.description = data.group.description || '';
      this.canRead = data.group.canRead;
      this.canWrite = data.group.canWrite;
      this.canReadWarehouse = !!data.group.canReadWarehouse;
      this.canWriteWarehouse = !!data.group.canWriteWarehouse;
      this.canAdmin = !!data.group.canAdmin;
      this.isSystem = !!data.group.isSystem;
      this.isDirectory = data.group.source === 'directory';
    }
  }

  close(): void {
    this.dialogRef.close();
  }

  onWriteChange(checked: boolean): void {
    this.canWrite = checked;
    if (checked) {
      this.canRead = true;
    }
  }

  onWarehouseWriteChange(checked: boolean): void {
    this.canWriteWarehouse = checked;
    if (checked) {
      this.canReadWarehouse = true;
    }
  }

  confirm(): void {
    const name = this.name.trim();
    if (!name) {
      return;
    }

    this.dialogRef.close({
      name,
      description: this.description.trim(),
      canRead: this.canRead || this.canWrite,
      canWrite: this.canWrite,
      canAdmin: this.canAdmin,
      canReadWarehouse: this.canReadWarehouse || this.canWriteWarehouse,
      canWriteWarehouse: this.canWriteWarehouse
    });
  }
}
