import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { LibraryFileTableComponent } from './components/library-file-table/library-file-table.component';
import { LibraryFileService } from './services/library-file.service';
import { LibraryFileDataSource } from './services/library-file.dataSource';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { LibraryFileEditDialogComponent } from './components/library-file-edit-dialog/library-file-edit-dialog.component';




@NgModule({
  declarations: [
    LibraryFileTableComponent,
    LibraryFileEditDialogComponent
  ],
  imports: [
    CommonModule,
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatTableModule,
    MatSortModule,
    MatPaginatorModule,
    MatButtonModule,
    MatDialogModule,
    MatSelectModule,
    MatIconModule,
    AppHeaderComponent
  ],
  exports: [
    LibraryFileTableComponent
  ],
  providers: [
    LibraryFileService,
    LibraryFileDataSource
  ]
})
export class LibraryFileTableModule { }
