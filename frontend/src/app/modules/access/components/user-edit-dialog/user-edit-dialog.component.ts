import { Component, Inject, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { AccessGroup, AppUser, UserWrite } from '../../../../services/auth.service';

export interface UserEditDialogData {
  user: AppUser | null;
  groups: AccessGroup[];
}

@Component({
    selector: 'user-edit-dialog',
    templateUrl: './user-edit-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class UserEditDialogComponent implements OnInit {
  username = '';
  password = '';
  isActive = true;
  selected = new Set<number>();

  constructor(
    public dialogRef: MatDialogRef<UserEditDialogComponent, UserWrite>,
    @Inject(MAT_DIALOG_DATA) public data: UserEditDialogData
  ) { }

  ngOnInit(): void {
    if (this.data.user) {
      this.username = this.data.user.username;
      this.isActive = this.data.user.isActive;
      this.selected = new Set(this.data.user.groups.map(group => group.id));
    }
  }

  close(): void {
    this.dialogRef.close();
  }

  get isDirectory(): boolean {
    return this.data.user?.source === 'directory';
  }

  isInstalledDefault(): boolean {
    return this.password.trim() === 'kicomlib';
  }

  isDirectoryMembership(id: number): boolean {
    return !!this.data.user?.groups.some(group => group.id === id && group.origin === 'directory');
  }

  isSelected(id: number): boolean {
    return this.selected.has(id);
  }

  toggleGroup(id: number, checked: boolean): void {
    if (checked) {
      this.selected.add(id);
    } else {
      this.selected.delete(id);
    }
  }

  groupRights(group: AccessGroup): string {
    const parts: string[] = [];
    if (group.canWrite) {
      parts.push('catalog read and write');
    } else if (group.canRead) {
      parts.push('catalog read');
    }
    if (group.canWriteWarehouse) {
      parts.push('warehouse read and write');
    } else if (group.canReadWarehouse) {
      parts.push('warehouse read');
    }
    if (group.canAdmin) {
      parts.push('administration');
    }
    return parts.length ? parts.join(', ') : 'no access';
  }

  confirm(): void {
    const username = this.username.trim();
    if (!username) {
      return;
    }
    if (this.isInstalledDefault()) {
      return;
    }
    if (!this.isDirectory && !this.data.user && this.password.trim().length < 12) {
      return;
    }
    if (!this.isDirectory && this.password && this.password.trim().length < 12) {
      return;
    }

    const body: UserWrite = {
      username,
      isActive: this.isActive,
      groupIds: Array.from(this.selected)
    };
    if (this.password) {
      body.password = this.password;
    }
    this.dialogRef.close(body);
  }
}
