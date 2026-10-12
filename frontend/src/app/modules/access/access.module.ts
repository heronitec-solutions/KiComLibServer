import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { TrimInputDirective } from '../../directives/trim-input.directive';
import { AccountPageComponent } from './components/account-page/account-page.component';
import { UserTableComponent } from './components/user-table/user-table.component';
import { UserEditDialogComponent } from './components/user-edit-dialog/user-edit-dialog.component';
import { GroupTableComponent } from './components/group-table/group-table.component';
import { GroupEditDialogComponent } from './components/group-edit-dialog/group-edit-dialog.component';
import { BackupPageComponent } from './components/backup-page/backup-page.component';
import { BackupConfirmDialogComponent } from './components/backup-confirm-dialog/backup-confirm-dialog.component';
import { SettingsPageComponent } from './components/settings-page/settings-page.component';
import { ApisPageComponent } from './components/apis-page/apis-page.component';
import { ApiSettingsDialogComponent } from './components/api-settings-dialog/api-settings-dialog.component';
import { DirectoryPageComponent } from './components/directory-page/directory-page.component';

@NgModule({
  declarations: [
    AccountPageComponent,
    UserTableComponent,
    UserEditDialogComponent,
    GroupTableComponent,
    GroupEditDialogComponent,
    BackupPageComponent,
    BackupConfirmDialogComponent,
    SettingsPageComponent,
    ApisPageComponent,
    ApiSettingsDialogComponent,
    DirectoryPageComponent
  ],
  imports: [
    CommonModule,
    FormsModule,
    MatTableModule,
    MatDialogModule,
    MatIconModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    TrimInputDirective,
    AppHeaderComponent
  ],
  exports: [
    AccountPageComponent,
    UserTableComponent,
    GroupTableComponent,
    DirectoryPageComponent,
    BackupPageComponent,
    SettingsPageComponent,
    ApisPageComponent
  ]
})
export class AccessModule { }
