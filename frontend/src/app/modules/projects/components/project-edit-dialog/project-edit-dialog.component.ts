import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { AppUser, AuthService } from '../../../../services/auth.service';
import { ExportScope, ProjectDetail, ProjectStatus, ProjectWrite } from '../../types/project';

export interface ProjectEditDialogData {
  project: ProjectDetail;
}

@Component({
    selector: 'project-edit-dialog',
    templateUrl: './project-edit-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ProjectEditDialogComponent {
  name = '';
  description = '';
  status: ProjectStatus = 'development';
  customer = '';
  ownerUserId: number | null = null;
  exportScope: ExportScope = 'project_dir';
  users: AppUser[] = [];

  constructor(
    public dialogRef: MatDialogRef<ProjectEditDialogComponent, ProjectWrite>,
    auth: AuthService,
    @Inject(MAT_DIALOG_DATA) public data: ProjectEditDialogData
  ) {
    const project = data.project;
    this.name = project.name;
    this.description = project.description;
    this.status = project.status;
    this.customer = project.customer;
    this.ownerUserId = project.ownerUserId;
    this.exportScope = project.exportScope;
    if (auth.profile().isAdmin) {
      auth.getUsers().subscribe({
        next: users => {
          this.users = users;
        },
        error: () => {
          this.users = [];
        }
      });
    }
  }

  close(): void {
    this.dialogRef.close();
  }

  confirm(): void {
    const name = this.name.trim();
    if (!name) {
      return;
    }
    this.dialogRef.close({
      name,
      description: this.description.trim(),
      status: this.status,
      customer: this.customer.trim(),
      ownerUserId: this.ownerUserId,
      exportScope: this.exportScope
    });
  }
}
