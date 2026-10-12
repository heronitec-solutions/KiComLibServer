import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { LibraryTableComponent } from './components/library-table/library-table.component';
import { LibraryService } from './services/library.service';
import { LibraryDataSource } from './services/library.dataSource';
import { LibraryEditDialogComponent } from './components/library-edit-dialog/library-edit-dialog.component';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { TrimInputDirective } from '../../directives/trim-input.directive';




@NgModule({
  declarations: [
    LibraryTableComponent,
    LibraryEditDialogComponent
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
    MatIconModule,
    AppHeaderComponent,
    TrimInputDirective
  ],
  exports: [
    LibraryTableComponent
  ],
  providers: [
    LibraryService,
    LibraryDataSource
  ]
})
export class LibraryTableModule { }
