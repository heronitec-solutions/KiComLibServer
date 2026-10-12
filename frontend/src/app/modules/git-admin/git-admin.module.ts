import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';

import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { CredentialDialogComponent } from './components/credential-dialog/credential-dialog.component';
import { GitAdminPageComponent } from './components/git-admin-page/git-admin-page.component';
import { HostDialogComponent } from './components/host-dialog/host-dialog.component';
import { RepositoryWizardComponent } from './components/repository-wizard/repository-wizard.component';

@NgModule({
  declarations: [
    GitAdminPageComponent,
    HostDialogComponent,
    CredentialDialogComponent,
    RepositoryWizardComponent
  ],
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatTableModule,
    MatTabsModule,
    AppHeaderComponent
  ],
  exports: [
    GitAdminPageComponent
  ]
})
export class GitAdminModule { }
