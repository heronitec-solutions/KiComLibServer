import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { AccessGroup } from '../../../../services/auth.service';

export interface ProjectAccessDialogData {
  groups: AccessGroup[];
  entries: { groupId: number; canWrite: boolean }[];
}

export interface ProjectAccessDialogResult {
  entries: { groupId: number; canWrite: boolean }[];
}

interface AccessRow {
  group: AccessGroup;
  enabled: boolean;
  canWrite: boolean;
}

@Component({
    selector: 'project-access-dialog',
    templateUrl: './project-access-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ProjectAccessDialogComponent {
  rows: AccessRow[] = [];

  constructor(
    public dialogRef: MatDialogRef<ProjectAccessDialogComponent, ProjectAccessDialogResult>,
    @Inject(MAT_DIALOG_DATA) data: ProjectAccessDialogData
  ) {
    const selected = new Map(data.entries.map(entry => [entry.groupId, entry.canWrite]));
    this.rows = data.groups.map(group => ({
      group,
      enabled: selected.has(group.id),
      canWrite: selected.get(group.id) === true
    }));
  }

  close(): void {
    this.dialogRef.close();
  }

  onEnabled(row: AccessRow, enabled: boolean): void {
    row.enabled = enabled;
    if (!enabled) {
      row.canWrite = false;
    }
  }

  confirm(): void {
    this.dialogRef.close({
      entries: this.rows
        .filter(row => row.enabled)
        .map(row => ({ groupId: row.group.id, canWrite: row.canWrite }))
    });
  }
}
